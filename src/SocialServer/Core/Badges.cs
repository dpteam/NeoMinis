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

            // photos and albums
            new BadgeDef("first_photo", "camera", false),
            new BadgeDef("photographer", "camera-retro", false),
            new BadgeDef("album_maker", "folder-open", false),
            new BadgeDef("curator",     "images", false),

            // stories
            new BadgeDef("storyteller", "circle-play", false),
            new BadgeDef("chronicler",  "film", false),

            // gifts
            new BadgeDef("giver",       "gift", false),
            new BadgeDef("generous",    "hand-holding-heart", false),
            new BadgeDef("beloved",     "heart-circle-check", false),

            // library and games
            new BadgeDef("gamer",       "gamepad", false),
            new BadgeDef("archivist",   "headphones", false),
            new BadgeDef("cinephile",   "clapperboard", false),

            // communities
            new BadgeDef("community",   "people-roof", false),
            new BadgeDef("organizer",   "people-group", false),

            // conversation and language
            new BadgeDef("messenger",   "paper-plane", false),
            new BadgeDef("polyglot",    "language", false),

            // one very liked post
            new BadgeDef("spotlight",   "star", false),

            // manual only: handed out by an administrator
            new BadgeDef("verified",    "circle-check", true),
            new BadgeDef("admin_grant", "user-shield", true),
            new BadgeDef("founder",     "flag", true),
            new BadgeDef("legend",      "crown", true),
            new BadgeDef("helper",      "handshake", true),
            new BadgeDef("joker",       "face-laugh-squint", true)
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

            // photos, albums and stories
            List<Post> photos = store.PhotoPosts(u.Id, 0, 200);
            int photoCount = 0;
            int bestPost = 0;
            for (int i = 0; i < photos.Count; i++)
            {
                if (!string.IsNullOrEmpty(photos[i].Image)) photoCount++;
                if (photos[i].Likes.Count > bestPost) bestPost = photos[i].Likes.Count;
            }
            if (photoCount >= 1) earned.Add("first_photo");
            if (photoCount >= 10) earned.Add("photographer");
            int albums = store.CountAlbums(u.Id);
            if (albums >= 1) earned.Add("album_maker");
            if (albums >= 5) earned.Add("curator");
            int stories = store.CountStoriesBy(u.Id);
            if (stories >= 1) earned.Add("storyteller");
            if (stories >= 20) earned.Add("chronicler");

            // gifts
            int giftsSent = store.CountGiftsSent(u.Id);
            int giftsIn = store.CountGifts(u.Id);
            if (giftsSent >= 1) earned.Add("giver");
            if (giftsSent >= 10) earned.Add("generous");
            if (giftsIn >= 5) earned.Add("beloved");

            // library and games
            if (store.CountMedia(u.Id, "game") >= 1) earned.Add("gamer");
            if (store.CountMedia(u.Id, "audio") >= 10) earned.Add("archivist");
            if (store.CountMedia(u.Id, "video") >= 5) earned.Add("cinephile");

            // communities
            int communities = store.GroupsFor(u.Id).Count;
            if (communities >= 3) earned.Add("community");
            if (communities >= 10) earned.Add("organizer");

            // conversation and language: polyglot means the account really switched
            // away from the site default, not that it merely has a language set
            if (messages >= 1000) earned.Add("messenger");
            if (!string.IsNullOrEmpty(u.Lang) &&
                !string.Equals(u.Lang, store.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                earned.Add("polyglot");

            // a post that many people liked
            if (bestPost >= 10) earned.Add("spotlight");

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