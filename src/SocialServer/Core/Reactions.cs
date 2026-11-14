using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// Emoji reactions on private messages. Stored on the message as lines of
    /// "emoji|id,id,id" so the JSON tables stay flat. One reaction per user per
    /// message: picking another emoji replaces the old one, picking the same one
    /// takes it back.
    /// </summary>
    public static class Reactions
    {
        public const int MaxPerMessage = 8;

        public static readonly string[] Allowed = new string[]
        {
            "\u2764\uFE0F",   // red heart
            "\uD83D\uDC4D",   // thumbs up
            "\uD83D\uDC4E",   // thumbs down
            "\uD83D\uDE02",   // laughing
            "\uD83D\uDE00",   // grin
            "\uD83D\uDE2E",   // surprised
            "\uD83D\uDE22",   // crying
            "\uD83D\uDD25"    // fire
        };

        public static bool IsAllowed(string emoji)
        {
            if (string.IsNullOrEmpty(emoji)) return false;
            for (int i = 0; i < Allowed.Length; i++) if (Allowed[i] == emoji) return true;
            return false;
        }

        /// <summary>Ordered map emoji -> voters; the first emoji wins the display order.</summary>
        public static List<KeyValuePair<string, List<long>>> Parse(string raw)
        {
            List<KeyValuePair<string, List<long>>> list = new List<KeyValuePair<string, List<long>>>();
            if (string.IsNullOrEmpty(raw)) return list;
            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                int bar = line.IndexOf('|');
                string emoji = bar < 0 ? line.Trim() : line.Substring(0, bar).Trim();
                if (emoji.Length == 0) continue;
                List<long> voters = new List<long>();
                if (bar >= 0)
                {
                    string[] ids = line.Substring(bar + 1).Split(',');
                    for (int k = 0; k < ids.Length; k++)
                    {
                        long id;
                        if (long.TryParse(ids[k].Trim(), out id) && id > 0 && !voters.Contains(id)) voters.Add(id);
                    }
                }
                if (voters.Count > 0) list.Add(new KeyValuePair<string, List<long>>(emoji, voters));
            }
            return list;
        }

        public static string Join(List<KeyValuePair<string, List<long>>> list)
        {
            if (list == null || list.Count == 0) return "";
            List<string> lines = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                List<string> ids = new List<string>();
                for (int k = 0; k < list[i].Value.Count; k++) ids.Add(list[i].Value[k].ToString());
                lines.Add(list[i].Key + "|" + string.Join(",", ids.ToArray()));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Emoji the user currently backs, "" when none.</summary>
        public static string Mine(string raw, long userId)
        {
            List<KeyValuePair<string, List<long>>> list = Parse(raw);
            for (int i = 0; i < list.Count; i++) if (list[i].Value.Contains(userId)) return list[i].Key;
            return "";
        }

        /// <summary>
        /// Applies a reaction and writes the new state into <paramref name="updated"/>.
        /// Returns the emoji the user now backs, or "" when the click removed it.
        /// </summary>
        public static string Toggle(string raw, long userId, string emoji, out string updated)
        {
            string mine = Mine(raw, userId);
            List<KeyValuePair<string, List<long>>> list = Parse(raw);

            // the user can only back one emoji, so drop every previous vote first
            for (int i = 0; i < list.Count; i++) list[i].Value.Remove(userId);
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].Value.Count == 0) list.RemoveAt(i);

            if (mine == emoji)
            {
                updated = Join(list);
                return "";
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key != emoji) continue;
                list[i].Value.Add(userId);
                updated = Join(list);
                return emoji;
            }

            if (list.Count >= MaxPerMessage)
            {
                updated = Join(list);
                return "";
            }

            List<long> voters = new List<long>();
            voters.Add(userId);
            list.Add(new KeyValuePair<string, List<long>>(emoji, voters));
            updated = Join(list);
            return emoji;
        }

        public static int Total(string raw)
        {
            List<KeyValuePair<string, List<long>>> list = Parse(raw);
            int n = 0;
            for (int i = 0; i < list.Count; i++) n += list[i].Value.Count;
            return n;
        }
    }
}