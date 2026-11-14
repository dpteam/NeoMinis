using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// Per-area privacy switches, stored on the user as one flat string:
    /// <c>write=friends,call=all,wall=all,gifts=friends,albums=friends,friends=all</c>.
    ///
    /// A flat string keeps the storage honest (one field, no schema migration) and
    /// an unknown or missing key always falls back to <see cref="All"/>, so an
    /// account created before this feature keeps behaving exactly as it used to.
    /// </summary>
    public static class Privacy
    {
        public const string All = "all";          // anybody, signed in
        public const string Friends = "friends";  // friends only
        public const string Nobody = "nobody";    // nobody but the owner

        /// <summary>Scopes the settings screen offers, in display order.</summary>
        public static readonly string[] Scopes = new string[]
        {
            "write", "call", "wall", "gifts", "albums", "friends"
        };

        public static bool KnownScope(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return false;
            for (int i = 0; i < Scopes.Length; i++) if (Scopes[i] == scope) return true;
            return false;
        }

        public static string Normalize(string value)
        {
            if (value == Friends) return Friends;
            if (value == Nobody) return Nobody;
            return All;
        }

        public static Dictionary<string, string> Parse(string prefs)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Scopes.Length; i++) map[Scopes[i]] = All;
            if (string.IsNullOrEmpty(prefs)) return map;

            string[] parts = prefs.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string piece = parts[i].Trim();
                int eq = piece.IndexOf('=');
                if (eq <= 0) continue;
                string key = piece.Substring(0, eq).Trim().ToLowerInvariant();
                string value = piece.Substring(eq + 1).Trim().ToLowerInvariant();
                if (!KnownScope(key)) continue;
                map[key] = Normalize(value);
            }
            return map;
        }

        public static string Serialize(Dictionary<string, string> map)
        {
            if (map == null) return "";
            List<string> parts = new List<string>();
            for (int i = 0; i < Scopes.Length; i++)
            {
                string value = All;
                if (map.ContainsKey(Scopes[i])) value = Normalize(map[Scopes[i]]);
                // only non-default values are written, so the field stays short
                if (value != All) parts.Add(Scopes[i] + "=" + value);
            }
            return string.Join(",", parts.ToArray());
        }

        /// <summary>The rule in force for one scope; unknown scopes are open.</summary>
        public static string Value(User u, string scope)
        {
            if (u == null || string.IsNullOrEmpty(u.PrivacyPrefs)) return All;
            if (!KnownScope(scope)) return All;
            Dictionary<string, string> map = Parse(u.PrivacyPrefs);
            return map.ContainsKey(scope) ? map[scope] : All;
        }

        /// <summary>
        /// True when the viewer may do the thing. The owner always may, staff always
        /// may, and a blocked relationship always wins over a permissive setting.
        /// </summary>
        public static bool Allows(User target, User viewer, string scope, bool isFriend)
        {
            if (target == null) return false;
            if (viewer != null && viewer.Id == target.Id) return true;
            if (viewer != null && viewer.IsModerator) return true;
            if (viewer == null) return false;

            string rule = Value(target, scope);
            if (rule == Nobody) return false;
            if (rule == Friends) return isFriend;
            return true;
        }
    }
}
