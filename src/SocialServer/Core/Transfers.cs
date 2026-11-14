using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Social.Core
{
    /// <summary>
    /// One file moving from one browser to another. The server is only a relay:
    /// it holds the chunks in memory while the transfer is alive and forgets them
    /// the moment it ends, so nothing lands in <c>data/media</c> and nothing
    /// survives a restart.
    /// </summary>
    public sealed class Transfer
    {
        public long Id;
        public string Code = "";        // 8 digits, grouped 1234-5678 for reading aloud
        public long From;
        public long To;
        public string FileName = "";
        public long Size;
        public string Sha256 = "";      // the sender's hash, checked by the receiver
        public string State = "offered";// offered | claimed | active | done | declined | cancelled
        public DateTime Created = DateTime.UtcNow;
        public DateTime Expires = DateTime.UtcNow.AddMinutes(30);
        public DateTime Finished = DateTime.MinValue;
        public int ChunkSize;           // bytes per chunk, the last one may be shorter
        public int ChunkCount;          // how many the sender announced
        public int Received;            // how many the server actually holds
        public long Bytes;              // bytes held right now
        public string Error = "";

        /// <summary>Live payload. Empty as soon as the transfer is finished.</summary>
        public Dictionary<int, byte[]> Chunks = new Dictionary<int, byte[]>();

        public bool IsOpen { get { return State == "offered" || State == "claimed" || State == "active"; } }
        public bool IsLive { get { return State == "active"; } }
    }

    /// <summary>
    /// In-memory registry of file transfers, plus the 8-digit codes that pair the
    /// two sides. Everything is bounded: a transfer may not exceed the configured
    /// size, only a few may be live at once, and expired ones are swept on every
    /// call, so a forgotten transfer cannot sit in RAM forever.
    /// </summary>
    public static class TransferRegistry
    {
        private const int MaxOpen = 64;

        private static readonly object Sync = new object();
        private static readonly Dictionary<long, Transfer> ById = new Dictionary<long, Transfer>();
        private static readonly Dictionary<string, long> ByCode = new Dictionary<string, long>(StringComparer.Ordinal);
        private static long _nextId = 1;
        private static DateTime _lastSweep = DateTime.UtcNow;

        /// <summary>Digits only, no dashes: what the two sides compare.</summary>
        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(8);
            for (int i = 0; i < code.Length && sb.Length < 8; i++)
            {
                char c = code[i];
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Pretty(string code)
        {
            string digits = NormalizeCode(code);
            if (digits.Length != 8) return digits;
            return digits.Substring(0, 4) + "-" + digits.Substring(4);
        }

        /// <summary>A code nobody else is using right now.</summary>
        private static string NewCode()
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                byte[] raw = Crypto.RandomBytes(8);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(8);
                for (int i = 0; i < 8; i++) sb.Append((char)('0' + (raw[i] % 10)));
                string code = sb.ToString();
                if (!ByCode.ContainsKey(code)) return code;
            }
            // astronomically unlikely; fall back to a counter-derived code
            return (DateTime.UtcNow.Ticks % 90000000 + 10000000).ToString();
        }

        public static Transfer Create(long from, long to, string fileName, long size, string sha256,
                                     int ttlMinutes, long maxBytes)
        {
            lock (Sync)
            {
                Sweep();
                if (from <= 0 || to <= 0 || from == to) return null;
                if (size <= 0 || size > maxBytes) return null;

                int open = 0;
                foreach (KeyValuePair<long, Transfer> pair in ById)
                    if (pair.Value.IsOpen && (pair.Value.From == from || pair.Value.To == to)) open++;
                if (open >= MaxOpen) return null;

                Transfer t = new Transfer();
                t.Id = _nextId++;
                t.Code = NewCode();
                t.From = from;
                t.To = to;
                t.FileName = Util.SingleLine(fileName, 160);
                if (t.FileName.Length == 0) t.FileName = "file";
                t.Size = size;
                t.Sha256 = (sha256 ?? "").Trim().ToLowerInvariant();
                t.Created = DateTime.UtcNow;
                t.Expires = DateTime.UtcNow.AddMinutes(ttlMinutes <= 0 ? 30 : ttlMinutes);

                ById[t.Id] = t;
                ByCode[t.Code] = t.Id;
                return t;
            }
        }

        public static Transfer Find(long id)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t)) return null;
                if (t.IsOpen && t.Expires <= DateTime.UtcNow) { Drop(t, "expired"); return t; }
                return t;
            }
        }

        /// <summary>The offer behind a code, but only for the person it is addressed to.</summary>
        public static Transfer ByCodeFor(string code, long userId)
        {
            lock (Sync)
            {
                Sweep();
                string digits = NormalizeCode(code);
                if (digits.Length != 8) return null;
                long id;
                if (!ByCode.TryGetValue(digits, out id)) return null;
                Transfer t;
                if (!ById.TryGetValue(id, out t)) return null;
                if (!t.IsOpen || t.To != userId) return null;
                return t;
            }
        }

        /// <summary>The receiver proves they know the code, which is the whole point of it.</summary>
        public static bool Claim(long id, long userId)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsOpen) return false;
                if (t.To != userId || t.State != "offered") return false;
                t.State = "claimed";
                return true;
            }
        }

        public static bool Accept(long id, long userId, int chunkSize, int chunkCount)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsOpen) return false;
                if (t.To != userId || t.State == "offered") return false;
                t.State = "active";
                t.ChunkSize = chunkSize > 0 ? chunkSize : 256 * 1024;
                t.ChunkCount = chunkCount > 0 ? chunkCount : 1;
                t.Chunks.Clear();
                t.Received = 0;
                t.Bytes = 0;
                return true;
            }
        }

        public static bool Decline(long id, long userId)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsOpen) return false;
                if (t.To != userId) return false;
                Drop(t, "declined");
                return true;
            }
        }

        public static bool Cancel(long id, long userId)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsOpen) return false;
                if (t.From != userId && t.To != userId) return false;
                Drop(t, "cancelled");
                return true;
            }
        }

        /// <summary>Stores one chunk. Only the sender may push, only while active.</summary>
        public static bool Put(long id, long userId, int index, byte[] data, long maxBytes)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsLive) return false;
                if (t.From != userId) return false;
                if (index < 0 || index >= t.ChunkCount) return false;
                if (data == null || data.Length == 0) return false;
                if (t.Bytes + data.Length > maxBytes) return false;

                byte[] existing;
                if (t.Chunks.TryGetValue(index, out existing))
                {
                    // a retried chunk replaces the old one instead of doubling the memory
                    t.Bytes -= existing.Length;
                    t.Received--;
                }
                t.Chunks[index] = data;
                t.Bytes += data.Length;
                t.Received++;
                return true;
            }
        }

        /// <summary>Hands one chunk to the receiver.</summary>
        public static byte[] Take(long id, long userId, int index)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t) || !t.IsLive) return null;
                if (t.To != userId) return null;
                byte[] data;
                return t.Chunks.TryGetValue(index, out data) ? data : null;
            }
        }

        /// <summary>
        /// The receiver says the file arrived and matched its hash. The payload is
        /// dropped right there, which is the only promise this thing makes.
        /// </summary>
        public static bool Finish(long id, long userId, bool ok, string error)
        {
            lock (Sync)
            {
                Transfer t;
                if (!ById.TryGetValue(id, out t)) return false;
                if (t.To != userId) return false;
                if (!t.IsOpen) return false;
                t.Error = error ?? "";
                Drop(t, ok ? "done" : "cancelled");
                return true;
            }
        }

        /// <summary>Transfers the user is part of, newest first.</summary>
        public static List<Transfer> ForUser(long userId, int limit)
        {
            List<Transfer> list = new List<Transfer>();
            lock (Sync)
            {
                Sweep();
                foreach (KeyValuePair<long, Transfer> pair in ById)
                {
                    Transfer t = pair.Value;
                    if (t.From != userId && t.To != userId) continue;
                    list.Add(t);
                }
            }
            list.Sort(delegate(Transfer a, Transfer b) { return b.Id.CompareTo(a.Id); });
            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        /// <summary>Offers waiting for this user, i.e. ones they have not claimed yet.</summary>
        public static List<Transfer> OffersFor(long userId)
        {
            List<Transfer> list = new List<Transfer>();
            lock (Sync)
            {
                Sweep();
                foreach (KeyValuePair<long, Transfer> pair in ById)
                {
                    Transfer t = pair.Value;
                    if (t.To != userId || !t.IsOpen) continue;
                    list.Add(t);
                }
            }
            list.Sort(delegate(Transfer a, Transfer b) { return b.Id.CompareTo(a.Id); });
            return list;
        }

        /// <summary>Drops the payload but keeps the row, so the two sides can see how it ended.</summary>
        private static void Drop(Transfer t, string state)
        {
            t.State = state;
            t.Finished = DateTime.UtcNow;
            t.Chunks.Clear();
            t.Bytes = 0;
            t.Received = 0;
        }

        /// <summary>
        /// Forgets finished rows and expired offers. Called on every entry point, so
        /// there is no timer thread to babysit.
        /// </summary>
        public static void Sweep()
        {
            lock (Sync)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastSweep).TotalSeconds < 5) return;
                _lastSweep = now;

                List<Transfer> dead = new List<Transfer>();
                foreach (KeyValuePair<long, Transfer> pair in ById)
                {
                    Transfer t = pair.Value;
                    if (t.IsOpen && t.Expires <= now) { Drop(t, "expired"); }
                    // finished rows linger for a few minutes so both sides can read the outcome
                    else if (!t.IsOpen && t.Finished != DateTime.MinValue &&
                             (now - t.Finished).TotalMinutes > 10) dead.Add(t);
                }
                for (int i = 0; i < dead.Count; i++)
                {
                    ById.Remove(dead[i].Id);
                    ByCode.Remove(dead[i].Code);
                }
            }
        }

        /// <summary>Bytes held in memory right now, for the admin overview.</summary>
        public static long BytesInFlight()
        {
            lock (Sync)
            {
                long total = 0;
                foreach (KeyValuePair<long, Transfer> pair in ById) total += pair.Value.Bytes;
                return total;
            }
        }
    }
}
