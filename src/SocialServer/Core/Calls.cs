using System;
using System.Collections.Generic;

namespace Social.Core
{
    public sealed class CallMember
    {
        public long User;
        public bool Accepted;
        public bool Declined;
        public bool Muted;
        public bool Video;
    }

    /// <summary>
    /// A live call. Media never touches the server: the browsers talk to each
    /// other over WebRTC, and we only relay the signalling (SDP offers/answers and
    /// ICE candidates) through the same long-poll channel the chat uses.
    /// </summary>
    public sealed class LiveCall
    {
        public long Id;
        public string Kind = "audio";   // audio | video
        public string Room = "";        // non-empty for a conference
        public long Initiator;
        public List<CallMember> Members = new List<CallMember>();
        public DateTime Created = DateTime.UtcNow;
        public DateTime Ended = DateTime.MinValue;

        public bool IsEnded { get { return Ended != DateTime.MinValue; } }

        public CallMember Member(long userId)
        {
            for (int i = 0; i < Members.Count; i++) if (Members[i].User == userId) return Members[i];
            return null;
        }

        public int AcceptedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Members.Count; i++) if (Members[i].Accepted) n++;
                return n;
            }
        }
    }

    /// <summary>One entry in a user's call event queue.</summary>
    public sealed class CallEvent
    {
        public long Id;
        public string Type = "";
        public long From;
        public string Payload = "";     // raw JSON object, relayed verbatim
        public long Call;
    }

    /// <summary>
    /// In-memory registry of live calls plus a small per-user event queue. Nothing
    /// here is persisted: a restart drops calls, which is the honest behaviour for
    /// something that only lives as long as the browsers are connected.
    /// </summary>
    public static class CallRegistry
    {
        private const int QueueLimit = 200;
        private const int RingSeconds = 45;

        private static readonly object Sync = new object();
        private static readonly Dictionary<long, LiveCall> Calls = new Dictionary<long, LiveCall>();
        private static readonly Dictionary<long, List<CallEvent>> Queues = new Dictionary<long, List<CallEvent>>();
        private static long _nextEvent = 1;
        private static DateTime _lastSweep = DateTime.UtcNow;

        public static LiveCall Create(long id, long initiator, string kind, string room, List<long> invitees)
        {
            lock (Sync)
            {
                Sweep();
                LiveCall call = new LiveCall();
                call.Id = id;
                call.Initiator = initiator;
                call.Kind = kind == "video" ? "video" : "audio";
                call.Room = room ?? "";
                call.Created = DateTime.UtcNow;

                CallMember owner = new CallMember();
                owner.User = initiator;
                owner.Accepted = true;
                owner.Video = call.Kind == "video";
                call.Members.Add(owner);

                for (int i = 0; i < invitees.Count; i++)
                {
                    long user = invitees[i];
                    if (user <= 0 || user == initiator || call.Member(user) != null) continue;
                    CallMember m = new CallMember();
                    m.User = user;
                    m.Video = call.Kind == "video";
                    call.Members.Add(m);
                }

                Calls[id] = call;
                return call;
            }
        }

        public static LiveCall Find(long id)
        {
            lock (Sync)
            {
                LiveCall call;
                return Calls.TryGetValue(id, out call) ? call : null;
            }
        }

        /// <summary>Live calls the user is a member of.</summary>
        public static List<LiveCall> ForUser(long userId)
        {
            List<LiveCall> list = new List<LiveCall>();
            lock (Sync)
            {
                foreach (KeyValuePair<long, LiveCall> pair in Calls)
                {
                    if (pair.Value.IsEnded) continue;
                    if (pair.Value.Member(userId) != null) list.Add(pair.Value);
                }
            }
            return list;
        }

        /// <summary>Live calls in a named room (any owner).</summary>
        public static List<LiveCall> ByRoom(string room)
        {
            List<LiveCall> list = new List<LiveCall>();
            if (string.IsNullOrEmpty(room)) return list;
            lock (Sync)
            {
                foreach (KeyValuePair<long, LiveCall> pair in Calls)
                    if (!pair.Value.IsEnded && pair.Value.Room == room) list.Add(pair.Value);
            }
            return list;
        }

        public static bool Join(long id, long userId, bool video, out LiveCall call)
        {
            lock (Sync)
            {
                call = null;
                if (!Calls.TryGetValue(id, out call) || call.IsEnded) return false;
                CallMember m = call.Member(userId);
                if (m == null)
                {
                    m = new CallMember();
                    m.User = userId;
                    call.Members.Add(m);
                }
                m.Accepted = true;
                m.Declined = false;
                if (video) m.Video = true;
                return true;
            }
        }

        /// <summary>Marks a member as gone; ends the call when nobody is left.</summary>
        public static bool Leave(long id, long userId, out bool ended)
        {
            ended = false;
            lock (Sync)
            {
                LiveCall call;
                if (!Calls.TryGetValue(id, out call)) return false;
                call.Members.RemoveAll(delegate(CallMember m) { return m.User == userId; });
                int active = 0;
                for (int i = 0; i < call.Members.Count; i++) if (call.Members[i].Accepted) active++;
                if (active == 0 && !call.IsEnded)
                {
                    call.Ended = DateTime.UtcNow;
                    ended = true;
                }
                return true;
            }
        }

        public static void End(long id)
        {
            lock (Sync)
            {
                LiveCall call;
                if (!Calls.TryGetValue(id, out call)) return;
                call.Ended = DateTime.UtcNow;
            }
        }

        public static bool SetMedia(long id, long userId, bool? muted, bool? video)
        {
            lock (Sync)
            {
                LiveCall call;
                if (!Calls.TryGetValue(id, out call)) return false;
                CallMember m = call.Member(userId);
                if (m == null) return false;
                if (muted.HasValue) m.Muted = muted.Value;
                if (video.HasValue) m.Video = video.Value;
                return true;
            }
        }

        // ------------------------------------------------------------- events

        public static void Push(long userId, string type, long from, long call, string payload)
        {
            if (userId <= 0) return;
            lock (Sync)
            {
                List<CallEvent> queue;
                if (!Queues.TryGetValue(userId, out queue))
                {
                    queue = new List<CallEvent>();
                    Queues[userId] = queue;
                }
                CallEvent e = new CallEvent();
                e.Id = _nextEvent++;
                e.Type = type;
                e.From = from;
                e.Call = call;
                e.Payload = payload ?? "";
                queue.Add(e);
                if (queue.Count > QueueLimit) queue.RemoveRange(0, queue.Count - QueueLimit);
            }
        }

        public static List<CallEvent> Since(long userId, long after)
        {
            List<CallEvent> list = new List<CallEvent>();
            lock (Sync)
            {
                List<CallEvent> queue;
                if (!Queues.TryGetValue(userId, out queue)) return list;
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i].Id > after) list.Add(queue[i]);
            }
            return list;
        }

        public static long LastEventId()
        {
            lock (Sync) { return _nextEvent - 1; }
        }

        /// <summary>Drops finished calls and stale queues so memory stays flat.</summary>
        public static void Sweep()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastSweep).TotalSeconds < 20) return;
            _lastSweep = now;

            List<long> deadCalls = new List<long>();
            foreach (KeyValuePair<long, LiveCall> pair in Calls)
            {
                LiveCall c = pair.Value;
                if (c.IsEnded && (now - c.Ended).TotalSeconds > 60) { deadCalls.Add(pair.Key); continue; }
                if (!c.IsEnded && (now - c.Created).TotalSeconds > RingSeconds &&
                    c.AcceptedCount <= 1 && c.Members.Count <= 1)
                {
                    c.Ended = now;
                }
            }
            for (int i = 0; i < deadCalls.Count; i++) Calls.Remove(deadCalls[i]);

            List<long> deadQueues = new List<long>();
            foreach (KeyValuePair<long, List<CallEvent>> pair in Queues)
            {
                if (pair.Value.Count == 0) deadQueues.Add(pair.Key);
                else if (pair.Value.Count > QueueLimit) pair.Value.RemoveRange(0, pair.Value.Count - QueueLimit);
            }
            for (int i = 0; i < deadQueues.Count; i++) Queues.Remove(deadQueues[i]);
        }
    }
}
