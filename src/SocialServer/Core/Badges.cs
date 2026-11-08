using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// One achievement definition. Auto badges are granted by the server when the
    /// matching counter is reached, manual ones only by an administrator.
    /// </summary>
    public sealed class BadgeDef
    {
        public readonly string Id;
        public readonly string Icon;
        public readonly bool Manual;

        public BadgeDef(string id, string icon, bool manual)
        {
            Id = id;
            Icon = icon;
            Manual = manual;
        }
    }

    /// <summary>
    /// Achievement catalogue and the little rules that grant them.
    /// Badges are stored on the user as a comma separated list of ids, which is
    /// exactly the format DeadVK used, so imported profiles keep their badges.
    /// </summary>
    public static class Badges
    {
        public const int MaxPerUser = 32;

        public static readonly BadgeDef[] All = new BadgeDef[]
        {
            new BadgeDef("first_post",  "pen-nib", false),
            new BadgeDef("author",      "feather", false),
            new BadgeDef("writer",      "book-open", false),
            new BadgeDef("commenter",   "comment-dots", false),
            new BadgeDef("liker",       "heart-circle-plus", false),
            new BadgeDef("lover",       "heart", false),
            new BadgeDef("social",      "user-group", false),
            new BadgeDef("popular",     "fire", false),
            new BadgeDef("collector",   "bookmark", false),
            new BadgeDef("chatter",     "comments", false),
            new BadgeDef("veteran",     "clock-rotate-left", false),
            new BadgeDef("verified",    "circle-check", true),
            new BadgeDef("admin_grant", "user-shield", true)
        };

        public static BadgeDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        public static bool Known(string id) { return Find(id) != null; }

        public static List<string> Parse(string csv)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            string[] parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim().ToLowerInvariant();
                if (id.Length > 0 && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        public static string Join(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            return string.Join(",", ids.ToArray());
        }

        public static bool Has(User u, string id)
        {
            return u != null && Parse(u.Badges).Contains(id);
        }

        /// <summary>Adds a badge. Returns true when the list actually changed.</summary>
        public static bool Grant(User u, string id)
        {
            if (u == null || !Known(id)) return false;
            List<string> list = Parse(u.Badges);
            if (list.Contains(id)) return false;
            if (list.Count >= MaxPerUser) return false;
            list.Add(id);
            u.Badges = Join(list);
            return true;
        }

        public static bool Revoke(User u, string id)
        {
            if (u == null) return false;
            List<string> list = Parse(u.Badges);
            if (!list.Remove(id)) return false;
            u.Badges = Join(list);
            return true;
        }

        /// <summary>
        /// Badges the user has earned but does not own yet. Counters come straight
        /// from the store, so the check is idempotent and safe to call often.
        /// </summary>
        public static List<string> Earned(SocialStore store, User u)
        {
            List<string> earned = new List<string>();
            if (store == null || u == null) return earned;

            int posts = store.CountPosts(u.Id);
            int comments = store.CountCommentsBy(u.Id);
            int likesIn = store.CountLikesReceived(u.Id);
            int likesOut = store.CountLikesGiven(u.Id);
            int friends = store.CountFriends(u.Id);
            int marks = store.CountBookmarks(u.Id);
            int messages = store.CountMessagesBy(u.Id);
            double days = (DateTime.UtcNow - u.Created).TotalDays;

            if (posts >= 1) earned.Add("first_post");
            if (posts >= 10) earned.Add("author");
            if (posts >= 100) earned.Add("writer");
            if (comments >= 10) earned.Add("commenter");
            if (likesOut >= 50) earned.Add("liker");
            if (likesIn >= 10) earned.Add("lover");
            if (friends >= 5) earned.Add("social");
            if (friends >= 25) earned.Add("popular");
            if (marks >= 10) earned.Add("collector");
            if (messages >= 100) earned.Add("chatter");
            if (days >= 365) earned.Add("veteran");
            return earned;
        }

        /// <summary>Grants everything listed in Earned(); returns the new ids only.</summary>
        public static List<string> Apply(SocialStore store, User u)
        {
            List<string> added = new List<string>();
            List<string> earned = Earned(store, u);
            for (int i = 0; i < earned.Count; i++)
                if (Grant(u, earned[i])) added.Add(earned[i]);
            return added;
        }

        /// <summary>Lower-case progress line for the profile card.</summary>
        public static string Progress(SocialStore store, User u)
        {
            if (store == null || u == null) return "";
            int owned = Parse(u.Badges).Count;
            return owned + "/" + All.Length;
        }
    }
}