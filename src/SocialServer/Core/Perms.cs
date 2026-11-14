using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// Granular moderator rights ("janitors"): a moderator is not all-or-nothing
    /// any more. The set is stored on the user as one flat comma separated string
    /// (<c>posts,reports,warn</c>) so there is no schema to migrate.
    ///
    /// An administrator always has every right. A moderator with an <em>empty</em>
    /// set keeps every right too, which is exactly how moderators behaved before
    /// this feature — only a moderator somebody deliberately narrowed is limited.
    /// </summary>
    public static class Perms
    {
        public const string Posts = "posts";        // remove or edit anybody's post and comment
        public const string Reports = "reports";    // read the report queue and close reports
        public const string Warn = "warn";          // issue a warning to an account
        public const string Mute = "mute";          // silence an account's messages for a while
        public const string Feedback = "feedback";  // read and handle anonymous feedback
        public const string Users = "users";        // open the user list and user cards
        public const string Logs = "logs";          // read the log viewer
        public const string Games = "games";        // upload Flash games

        /// <summary>Every flag, in the order the admin panel shows them.</summary>
        public static readonly string[] All = new string[]
        {
            Posts, Reports, Warn, Mute, Feedback, Users, Logs, Games
        };

        public static bool Known(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return false;
            for (int i = 0; i < All.Length; i++) if (All[i] == flag) return true;
            return false;
        }

        public static List<string> Parse(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(value)) return list;
            string[] parts = value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string flag = parts[i].Trim().ToLowerInvariant();
                if (!Known(flag) || list.Contains(flag)) continue;
                list.Add(flag);
            }
            return list;
        }

        /// <summary>Serializes in canonical order, so equal sets always look equal.</summary>
        public static string Serialize(List<string> flags)
        {
            if (flags == null || flags.Count == 0) return "";
            List<string> parts = new List<string>();
            for (int i = 0; i < All.Length; i++) if (flags.Contains(All[i])) parts.Add(All[i]);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>The set in force, expanding the "moderator with no set" default.</summary>
        public static List<string> Effective(User u)
        {
            List<string> list = Parse(u == null ? "" : u.Perms);
            if (list.Count == 0 && u != null && u.Role == "moderator")
                list = new List<string>(All);
            return list;
        }

        /// <summary>True when this account may do the thing. Staff checks stay separate.</summary>
        public static bool Has(User u, string flag)
        {
            if (u == null || u.Deleted) return false;
            if (u.Role == "admin") return true;
            if (u.Role != "moderator") return false;
            // an empty set means "the old full moderator", not "no rights at all"
            if (string.IsNullOrEmpty(u.Perms)) return true;
            return Parse(u.Perms).Contains(flag);
        }

        /// <summary>True when the account may open the admin board at all.</summary>
        public static bool Any(User u)
        {
            if (u == null || u.Deleted) return false;
            if (u.Role == "admin") return true;
            return u.Role == "moderator";
        }

        /// <summary>
        /// True when the account may look at anything: an administrator, or a
        /// janitor holding either the "users" or the "posts" right. A janitor with
        /// only, say, "feedback" is not staff for viewing purposes, so per-area
        /// privacy still applies to them.
        /// </summary>
        public static bool SeesAll(User u)
        {
            if (u == null || u.Deleted) return false;
            if (u.Role == "admin") return true;
            return Has(u, Users) || Has(u, Posts);
        }
    }
}
