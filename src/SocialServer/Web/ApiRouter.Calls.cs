using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // =============================================================== calls

        private string CallJson(LiveCall c, User viewer)
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", c.Id);
            b.Prop("kind", c.Kind);
            b.Prop("room", c.Room);
            b.Prop("initiator", c.Initiator);
            b.Prop("ended", c.IsEnded);
            b.Prop("created", Util.Iso(c.Created));
            b.Name("members").BeginArray();
            for (int i = 0; i < c.Members.Count; i++)
            {
                CallMember m = c.Members[i];
                User u = _store.FindUser(m.User);
                b.BeginObject();
                b.Prop("id", m.User);
                b.Prop("accepted", m.Accepted);
                b.Prop("declined", m.Declined);
                b.Prop("muted", m.Muted);
                b.Prop("video", m.Video);
                b.Prop("mine", viewer != null && viewer.Id == m.User);
                b.Name("user").Raw(UserSummary(u));
                b.EndObject();
            }
            b.EndArray();
            b.EndObject();
            return b.ToString();
        }

        private LiveCall RequireCallMember(ApiCtx ctx, long id)
        {
            LiveCall c = CallRegistry.Find(id);
            if (c == null) throw new ApiException(404, "no-call", "Call not found");
            if (c.Member(ctx.User.Id) == null)
                throw new ApiException(403, "not-in-call", "You are not part of this call");
            return c;
        }

        private void HandleCallStart(ApiCtx ctx)
        {
            CallRegistry.Sweep();
            bool video = BodyBool(ctx, "video", false);
            string room = Util.SingleLine(BodyStr(ctx, "room", ""), 60);
            long with = BodyLong(ctx, "with", 0);

            if (with > 0)
            {
                User target = RequireUser(with);
                if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "You cannot call yourself");
                if (!CanCallUser(ctx.User, target))
                    throw new ApiException(403, "cannot-call", "You cannot call this user");

                // already ringing? hand the same call back instead of stacking
                List<LiveCall> mine = CallRegistry.ForUser(ctx.User.Id);
                for (int i = 0; i < mine.Count; i++)
                {
                    LiveCall c = mine[i];
                    if (c.Room.Length == 0 && !c.IsEnded && c.Member(target.Id) != null)
                    {
                        JsonBuilder again = new JsonBuilder(1024);
                        again.BeginObject();
                        again.Name("call").Raw(CallJson(c, ctx.User));
                        SendOk(ctx, again);
                        return;
                    }
                }

                long id = _store.NextCallId();
                List<long> invitees = new List<long>();
                invitees.Add(target.Id);
                LiveCall call = CallRegistry.Create(id, ctx.User.Id, video ? "video" : "audio", "", invitees);
                _store.AddCallRecord(id, call.Kind, "", ctx.User.Id, new List<long> { ctx.User.Id, target.Id });

                CallRegistry.Push(target.Id, "incoming", ctx.User.Id, id, CallJson(call, target));
                Log.Info("CALL", "@" + ctx.User.Username + " calls @" + target.Username + " (" + call.Kind + ")");

                JsonBuilder b = new JsonBuilder(1024);
                b.BeginObject();
                b.Name("call").Raw(CallJson(call, ctx.User));
                SendOk(ctx, b);
                return;
            }

            // conference: join the room if it is alive, otherwise open it
            if (room.Length < 3) throw new ApiException(400, "bad-room", "A room needs a name of 3+ characters");
            if (!Util.IsValidUsername(room.ToLowerInvariant().Replace(" ", "_"), 60))
                throw new ApiException(400, "bad-room", "Use letters, digits, _ or . for the room name");

            LiveCall existing = null;
            List<LiveCall> all = CallRegistry.ForUser(ctx.User.Id);
            for (int i = 0; i < all.Count; i++)
                if (all[i].Room == room && !all[i].IsEnded) { existing = all[i]; break; }
            if (existing == null)
            {
                // a room may exist that this user is not in yet: find it by name
                List<LiveCall> any = CallRegistry.ByRoom(room);
                for (int i = 0; i < any.Count; i++) if (!any[i].IsEnded) { existing = any[i]; break; }
            }

            if (existing != null)
            {
                if (existing.Members.Count >= _cfg.MaxCallMembers)
                    throw new ApiException(400, "call-full", "This room is full");
                bool wasIn = existing.Member(ctx.User.Id) != null;
                LiveCall joined;
                CallRegistry.Join(existing.Id, ctx.User.Id, video, out joined);
                if (!wasIn)
                {
                    for (int i = 0; i < joined.Members.Count; i++)
                    {
                        long uid = joined.Members[i].User;
                        if (uid == ctx.User.Id) continue;
                        CallRegistry.Push(uid, "joined", ctx.User.Id, joined.Id, CallJson(joined, null));
                    }
                    Log.Info("CALL", "@" + ctx.User.Username + " joined room " + room);
                }
                JsonBuilder again = new JsonBuilder(1024);
                again.BeginObject();
                again.Name("call").Raw(CallJson(joined, ctx.User));
                SendOk(ctx, again);
                return;
            }

            long newId = _store.NextCallId();
            LiveCall fresh = CallRegistry.Create(newId, ctx.User.Id, video ? "video" : "audio", room, new List<long>());
            _store.AddCallRecord(newId, fresh.Kind, room, ctx.User.Id, new List<long> { ctx.User.Id });
            Log.Info("CALL", "@" + ctx.User.Username + " opened room " + room);

            JsonBuilder outb = new JsonBuilder(1024);
            outb.BeginObject();
            outb.Name("call").Raw(CallJson(fresh, ctx.User));
            SendOk(ctx, outb);
        }

        private void HandleCallAnswer(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            bool accept = BodyBool(ctx, "accept", false);
            LiveCall c = RequireCallMember(ctx, id);
            if (c.IsEnded) throw new ApiException(400, "ended", "This call has already ended");

            CallMember me = c.Member(ctx.User.Id);
            if (me == null) throw new ApiException(403, "not-in-call", "You are not part of this call");
            if (accept) { me.Accepted = true; me.Declined = false; }
            else { me.Accepted = false; me.Declined = true; }

            string type = accept ? "accepted" : "declined";
            for (int i = 0; i < c.Members.Count; i++)
            {
                long uid = c.Members[i].User;
                if (uid == ctx.User.Id) continue;
                CallRegistry.Push(uid, type, ctx.User.Id, c.Id, CallJson(c, null));
            }

            if (!accept)
            {
                int active = 0;
                for (int i = 0; i < c.Members.Count; i++) if (c.Members[i].Accepted) active++;
                if (active == 0)
                {
                    CallRegistry.End(c.Id);
                    _store.EndCallRecord(c.Id);
                    for (int i = 0; i < c.Members.Count; i++)
                        CallRegistry.Push(c.Members[i].User, "ended", 0, c.Id, "{}");
                }
            }

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("call").Raw(CallJson(c, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleCallLeave(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            LiveCall c = RequireCallMember(ctx, id);
            bool ended;
            CallRegistry.Leave(id, ctx.User.Id, out ended);
            if (ended) _store.EndCallRecord(id);

            for (int i = 0; i < c.Members.Count; i++)
            {
                long uid = c.Members[i].User;
                if (uid == ctx.User.Id) continue;
                CallRegistry.Push(uid, ended ? "ended" : "left", ctx.User.Id, id, "{}");
            }
            Log.Debug("CALL", "@" + ctx.User.Username + " left call #" + id + (ended ? " (ended)" : ""));

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("ended", ended);
            SendOk(ctx, b);
        }

        private void HandleCallSignal(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            long to = BodyLong(ctx, "to", 0);
            LiveCall c = RequireCallMember(ctx, id);

            // relay the payload verbatim; it is SDP or an ICE candidate, nothing else
            string payload = BodyStr(ctx, "payload", "");
            if (payload.Length == 0 || payload.Length > 65536)
                throw new ApiException(400, "bad-signal", "Bad signalling payload");
            Dictionary<string, object> probe = Json.ParseObject(payload, _cfg.MaxJsonDepth);
            if (probe == null) throw new ApiException(400, "bad-signal", "Signalling payload must be a JSON object");

            if (to > 0)
            {
                if (c.Member(to) == null) throw new ApiException(404, "no-member", "That user is not in this call");
                CallRegistry.Push(to, "signal", ctx.User.Id, id, payload);
            }
            else
            {
                for (int i = 0; i < c.Members.Count; i++)
                {
                    long uid = c.Members[i].User;
                    if (uid == ctx.User.Id) continue;
                    CallRegistry.Push(uid, "signal", ctx.User.Id, id, payload);
                }
            }

            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        private void HandleCallMedia(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            LiveCall c = RequireCallMember(ctx, id);
            bool? muted = ctx.Body.ContainsKey("muted") ? (bool?)BodyBool(ctx, "muted", false) : null;
            bool? video = ctx.Body.ContainsKey("video") ? (bool?)BodyBool(ctx, "video", false) : null;
            CallRegistry.SetMedia(id, ctx.User.Id, muted, video);

            string payload = "{\"muted\":" + (muted.HasValue && muted.Value ? "true" : "false") +
                             ",\"video\":" + (video.HasValue && video.Value ? "true" : "false") + "}";
            for (int i = 0; i < c.Members.Count; i++)
            {
                long uid = c.Members[i].User;
                if (uid == ctx.User.Id) continue;
                CallRegistry.Push(uid, "media", ctx.User.Id, id, payload);
            }

            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        /// <summary>Long poll: answers as soon as this user has call events.</summary>
        private void HandleCallWait(ApiCtx ctx)
        {
            long after = LongQuery(ctx, "after");
            int timeout = _cfg.NotifyWaitSec;
            string raw = GetQuery(ctx.Query, "timeout");
            if (!string.IsNullOrEmpty(raw))
            {
                int parsed;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) timeout = parsed;
            }
            if (timeout < 1) timeout = 1;
            if (timeout > 25) timeout = 25;

            List<CallEvent> events = new List<CallEvent>();
            DateTime until = DateTime.UtcNow.AddSeconds(timeout);

            if (!EnterWait())
            {
                events = CallRegistry.Since(ctx.User.Id, after);
                JsonBuilder busy = new JsonBuilder(512);
                busy.BeginObject();
                busy.Name("events").BeginArray();
                for (int i = 0; i < events.Count; i++) RawCallEvent(busy, events[i]);
                busy.EndArray();
                busy.Prop("latest", CallRegistry.LastEventId());
                busy.Prop("busy", true);
                SendOk(ctx, busy);
                return;
            }

            try
            {
                while (true)
                {
                    CallRegistry.Sweep();
                    events = CallRegistry.Since(ctx.User.Id, after);
                    if (events.Count > 0) break;
                    if (DateTime.UtcNow >= until) break;
                    Thread.Sleep(300);
                }
            }
            finally
            {
                ExitWait();
            }

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("events").BeginArray();
            for (int i = 0; i < events.Count; i++) RawCallEvent(b, events[i]);
            b.EndArray();
            b.Prop("latest", CallRegistry.LastEventId());
            SendOk(ctx, b);
        }

        private static void RawCallEvent(JsonBuilder b, CallEvent e)
        {
            b.BeginObject();
            b.Prop("id", e.Id);
            b.Prop("type", e.Type);
            b.Prop("from", e.From);
            b.Prop("call", e.Call);
            b.Name("payload").Raw(string.IsNullOrEmpty(e.Payload) ? "null" : e.Payload);
            b.EndObject();
        }

        private void HandleCallActive(ApiCtx ctx)
        {
            CallRegistry.Sweep();
            List<LiveCall> calls = CallRegistry.ForUser(ctx.User.Id);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("calls").BeginArray();
            for (int i = 0; i < calls.Count; i++)
            {
                if (calls[i].IsEnded) continue;
                b.Raw(CallJson(calls[i], ctx.User));
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleCallHistory(ApiCtx ctx)
        {
            int limit = LimitParam(ctx, 20, 50);
            List<CallRecord> list = _store.CallsFor(ctx.User.Id, limit);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("calls").BeginArray();
            for (int i = 0; i < list.Count; i++)
            {
                CallRecord r = list[i];
                b.BeginObject();
                b.Prop("id", r.Id);
                b.Prop("kind", r.Kind);
                b.Prop("room", r.Room);
                b.Prop("initiator", r.Initiator);
                b.Prop("started", Util.Iso(r.Started));
                b.Prop("ended", r.Ended == DateTime.MinValue ? "" : Util.Iso(r.Ended));
                // an unfinished record means the browsers vanished without hanging up
                int seconds = r.Ended == DateTime.MinValue ? 0 : (int)(r.Ended - r.Started).TotalSeconds;
                b.Prop("duration", seconds < 0 ? 0 : seconds);
                b.Name("participants").BeginArray();
                for (int k = 0; k < r.Participants.Count; k++)
                {
                    User u = _store.FindUser(r.Participants[k]);
                    b.Raw(UserSummary(u));
                }
                b.EndArray();
                b.EndObject();
            }
            b.EndArray();
            SendOk(ctx, b);
        }
    }
}
