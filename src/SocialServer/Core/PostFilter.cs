using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// Filters for the post search. Every field is optional; an empty filter simply
    /// means "everything public". The store applies them in one pass, which is fine
    /// for a self-hosted network and keeps the JSON tables flat.
    /// </summary>
    public sealed class PostFilter
    {
        public string Query = "";        // free text, matched against the body
        public long Author;              // 0 = any author
        public string Tag = "";          // #hashtag without the hash
        public string Media = "";        // "" | image | video | poll | text
        public DateTime Since = DateTime.MinValue;
        public DateTime Until = DateTime.MinValue;
        public string Sort = "recent";   // recent | old | popular

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrEmpty(Query) && Author <= 0 && string.IsNullOrEmpty(Tag) &&
                       string.IsNullOrEmpty(Media) && Since == DateTime.MinValue && Until == DateTime.MinValue;
            }
        }

        public bool Matches(Post p)
        {
            if (p == null || p.Deleted) return false;
            if (Author > 0 && p.Author != Author) return false;
            if (Since != DateTime.MinValue && p.Created < Since) return false;
            if (Until != DateTime.MinValue && p.Created > Until) return false;

            if (!string.IsNullOrEmpty(Media))
            {
                switch (Media)
                {
                    case "image": if (string.IsNullOrEmpty(p.Image)) return false; break;
                    case "video": if (string.IsNullOrEmpty(p.Video)) return false; break;
                    case "poll": if (!Polls.Has(p)) return false; break;
                    case "text":
                        if (!string.IsNullOrEmpty(p.Image) || !string.IsNullOrEmpty(p.Video) || Polls.Has(p)) return false;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(Tag))
            {
                List<string> tags = Util.Hashtags(p.Body);
                if (!tags.Contains(Tag.ToLowerInvariant())) return false;
            }

            if (!string.IsNullOrEmpty(Query))
            {
                if (string.IsNullOrEmpty(p.Body)) return false;
                if (p.Body.IndexOf(Query, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        public static PostFilter Parse(string query, string author, string tag, string media,
                                       string since, string until, string sort)
        {
            PostFilter f = new PostFilter();
            f.Query = (query ?? "").Trim();
            long authorId;
            if (!string.IsNullOrEmpty(author) &&
                long.TryParse(author.Trim(), out authorId) && authorId > 0) f.Author = authorId;

            string t = (tag ?? "").Trim();
            if (t.StartsWith("#", StringComparison.Ordinal)) t = t.Substring(1);
            f.Tag = t.ToLowerInvariant();

            string m = (media ?? "").Trim().ToLowerInvariant();
            if (m == "image" || m == "video" || m == "poll" || m == "text") f.Media = m;

            DateTime parsed;
            if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, out parsed))
                f.Since = parsed.ToUniversalTime();
            if (!string.IsNullOrEmpty(until) && DateTime.TryParse(until, out parsed))
                f.Until = parsed.ToUniversalTime();

            string s = (sort ?? "").Trim().ToLowerInvariant();
            if (s == "old" || s == "popular") f.Sort = s;
            return f;
        }
    }
}