using System;
using System.Collections.Generic;

namespace Social.Web
{
    /// <summary>
    /// In-memory "is typing" registry. Purely cosmetic: an entry lives for a few
    /// seconds, so it never touches the data files and survives nothing.
    /// </summary>
    public static class TypingRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, DateTime> Entries = new Dictionary<string, DateTime>();
        private static DateTime _lastSweep = DateTime.UtcNow;

        public static void Touch(long from, long to)
        {
            if (from <= 0 || to <= 0 || from == to) return;
            lock (Sync)
            {
                Sweep();
                Entries[Key(from, to)] = DateTime.UtcNow;
            }
        }

        public static void Clear(long from, long to)
        {
            lock (Sync) { Entries.Remove(Key(from, to)); }
        }

        public static bool IsTyping(long from, long to)
        {
            lock (Sync)
            {
                DateTime at;
                if (!Entries.TryGetValue(Key(from, to), out at)) return false;
                return (DateTime.UtcNow - at).TotalSeconds < 6;
            }
        }

        private static string Key(long from, long to)
        {
            return from.ToString() + ":" + to.ToString();
        }

        private static void Sweep()
        {
            if ((DateTime.UtcNow - _lastSweep).TotalSeconds < 30) return;
            _lastSweep = DateTime.UtcNow;
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in Entries)
                if ((DateTime.UtcNow - pair.Value).TotalSeconds > 60) dead.Add(pair.Key);
            for (int i = 0; i < dead.Count; i++) Entries.Remove(dead[i]);
        }
    }
}