using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// The permission vocabulary for third-party apps. A scope is a single verb
    /// somebody has to approve before an app can do it, and an app can only ever
    /// ask for the ones on this list.
    ///
    /// Deliberately small. Every scope here is one somebody can understand from
    /// its name, and there is no "everything" scope: an app that wants two things
    /// asks for two things.
    /// </summary>
    public static class AppScopes
    {
        public const string Profile = "profile";          // read the signed-in user's own name, avatar, id
        public const string WallPost = "wall.post";       // post on the user's own wall
        public const string MessagesRead = "messages.read"; // read the user's conversations
        public const string Storage = "storage";          // keep key/value data per user
        public const string Score = "score";              // submit and read leaderboard scores

        public static readonly string[] All = new string[]
        {
            Profile, WallPost, MessagesRead, Storage, Score
        };

        public static bool Known(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return false;
            for (int i = 0; i < All.Length; i++) if (All[i] == scope) return true;
            return false;
        }

        /// <summary>
        /// Cleans a requested set: unknown scopes are dropped rather than rejected,
        /// so an app asking for something a newer server offers still works on an
        /// older one with the rest of its list intact.
        /// </summary>
        public static List<string> Parse(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(value)) return list;
            string[] parts = value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string scope = parts[i].Trim().ToLowerInvariant();
                if (!Known(scope) || list.Contains(scope)) continue;
                list.Add(scope);
            }
            return list;
        }

        /// <summary>Serializes in canonical order, so equal sets always look equal.</summary>
        public static string Serialize(List<string> scopes)
        {
            if (scopes == null || scopes.Count == 0) return "";
            List<string> parts = new List<string>();
            for (int i = 0; i < All.Length; i++) if (scopes.Contains(All[i])) parts.Add(All[i]);
            return string.Join(",", parts.ToArray());
        }

        public static bool Has(string granted, string scope)
        {
            if (string.IsNullOrEmpty(granted) || string.IsNullOrEmpty(scope)) return false;
            return Parse(granted).Contains(scope);
        }

        /// <summary>App types. Only an administrator may publish an "html" app.</summary>
        public static bool KnownType(string type)
        {
            return type == "external" || type == "html" || type == "swf";
        }
    }
}
