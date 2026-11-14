using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    /// <summary>
    /// One search box over everything the viewer is allowed to see: people, posts,
    /// comments, their own messages, communities and games. Every branch reuses the
    /// same visibility rules as the normal endpoints, so search can never show
    /// something the viewer could not open directly.
    /// </summary>
    public sealed partial class ApiRouter
    {
        private void HandleSearchAll(ApiCtx ctx)
        {
            if (!Allow(ctx.User.Id, "search", _cfg.SearchPerMin, 60))
                throw new ApiException(429, "slow-down", "Too many searches");

            string query = (GetQuery(ctx.Query, "q") ?? "").Trim();
            string kind = (GetQuery(ctx.Query, "kind") ?? "all").Trim().ToLowerInvariant();
            if (kind != "posts" && kind != "comments" && kind != "messages" &&
                kind != "people" && kind != "groups" && kind != "games")
                kind = "all";

            long author = LongQuery(ctx, "author");
            if (author == 0)
            {
                // allow ?author=username as well as ?author=17
                string raw = GetQuery(ctx.Query, "author");
                if (!string.IsNullOrEmpty(raw))
                {
                    User byName = _store.FindUser(raw);
                    if (byName != null) author = byName.Id;
                }
            }
            long with = LongQuery(ctx, "with");
            DateTime since = ParseDay(GetQuery(ctx.Query, "since"), false);
            DateTime until = ParseDay(GetQuery(ctx.Query, "until"), true);
            long before = LongQuery(ctx, "before");
            int limit = LimitParam(ctx, 12, 40);

            bool wantPeople = kind == "all" || kind == "people";
            bool wantPosts = kind == "all" || kind == "posts";
            bool wantComments = kind == "all" || kind == "comments";
            bool wantMessages = kind == "all" || kind == "messages";
            bool wantGroups = kind == "all" || kind == "groups";
            bool wantGames = kind == "all" || kind == "games";

            bool hasQuery = query.Length >= 2;
            bool hasFilter = author > 0 || since != DateTime.MinValue || until != DateTime.MinValue;
            if (!hasQuery && !hasFilter && kind == "all")
                throw new ApiException(400, "empty-search", "Type at least two characters or pick a filter");

            JsonBuilder b = new JsonBuilder(16384);
            b.BeginObject();
            b.Prop("q", query);
            b.Prop("kind", kind);

            // ------------------------------------------------------------- people
            b.Name("users").BeginArray();
            int userCount = 0;
            if (wantPeople && hasQuery)
            {
                List<User> users = _store.SearchUsers(query, limit);
                for (int i = 0; i < users.Count && userCount < limit; i++)
                {
                    if (!CanViewProfile(ctx.User, users[i])) continue;
                    b.Raw(UserView(users[i], ctx.User, false));
                    userCount++;
                }
            }
            b.EndArray();

            // -------------------------------------------------------------- posts
            PostFilter filter = PostFilter.Parse(hasQuery ? query : "", author > 0 ?
                author.ToString(CultureInfo.InvariantCulture) : "", "", "",
                GetQuery(ctx.Query, "since"), GetQuery(ctx.Query, "until"), "recent");
            bool postsMore = false;
            List<Post> posts = new List<Post>();
            if (wantPosts && (hasQuery || hasFilter) && !filter.IsEmpty)
            {
                posts = _store.SearchPosts(filter, ctx.User.Id, limit + 1, out postsMore);
                if (posts.Count > limit) posts.RemoveAt(posts.Count - 1);
            }
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("postsMore", postsMore);

            // ----------------------------------------------------------- comments
            bool commentsMore = false;
            List<Comment> comments = new List<Comment>();
            if (wantComments && hasQuery)
                comments = _store.SearchComments(query, ctx.User.Id, author, since, until, before, limit, out commentsMore);

            b.Name("comments").BeginArray();
            for (int i = 0; i < comments.Count; i++)
            {
                Comment c = comments[i];
                Post parent = _store.FindPost(c.Post);
                b.BeginObject();
                b.Prop("id", c.Id);
                b.Prop("body", c.Body);
                b.Prop("created", Util.Iso(c.Created));
                b.Prop("post", c.Post);
                b.Name("author").Raw(UserSummary(_store.FindUser(c.Author)));
                b.Name("postPreview").Raw(parent == null ? "null" : PostSummary(parent));
                b.EndObject();
            }
            b.EndArray();
            b.Prop("commentsMore", commentsMore);

            // ----------------------------------------------------------- messages
            bool messagesMore = false;
            List<Message> messages = new List<Message>();
            if (wantMessages && hasQuery)
                messages = _store.SearchOwnMessages(query, ctx.User.Id, author, with, since, until, before, limit, out messagesMore);

            b.Name("messages").BeginArray();
            for (int i = 0; i < messages.Count; i++)
            {
                Message m = messages[i];
                long other = m.From == ctx.User.Id ? m.To : m.From;
                b.BeginObject();
                b.Prop("id", m.Id);
                b.Prop("body", m.Body);
                b.Prop("created", Util.Iso(m.Created));
                b.Prop("outgoing", m.From == ctx.User.Id);
                b.Name("with").Raw(UserSummary(_store.FindUser(other)));
                b.EndObject();
            }
            b.EndArray();
            b.Prop("messagesMore", messagesMore);

            // ------------------------------------------------------------- groups
            b.Name("groups").BeginArray();
            int groupCount = 0;
            if (wantGroups && hasQuery)
            {
                List<Group> groups = _store.SearchGroups(query, ctx.User.Id, limit);
                for (int i = 0; i < groups.Count; i++)
                {
                    b.Raw(GroupView(groups[i], ctx.User));
                    groupCount++;
                }
            }
            b.EndArray();

            // -------------------------------------------------------------- games
            b.Name("games").BeginArray();
            int gameCount = 0;
            if (wantGames && hasQuery && GamesAvailable())
            {
                bool gamesMore;
                List<MediaItem> games = _store.Games(0, query, 0, limit, out gamesMore);
                for (int i = 0; i < games.Count; i++)
                {
                    b.Raw(GameJson(games[i], ctx.User));
                    gameCount++;
                }
            }
            b.EndArray();

            // the counts drive the tab labels on the client
            b.Name("counts").BeginObject();
            b.Prop("users", userCount);
            b.Prop("posts", posts.Count);
            b.Prop("comments", comments.Count);
            b.Prop("messages", messages.Count);
            b.Prop("groups", groupCount);
            b.Prop("games", gameCount);
            b.EndObject();

            SendOk(ctx, b);
        }

        /// <summary>Short preview of a post for a comment hit.</summary>
        private string PostSummary(Post p)
        {
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("id", p.Id);
            string body = p.Body ?? "";
            if (body.Length > 120) body = body.Substring(0, 120) + "…";
            b.Prop("body", body);
            b.Prop("image", p.Image);
            b.Name("author").Raw(UserSummary(_store.FindUser(p.Author)));
            b.EndObject();
            return b.ToString();
        }

        /// <summary>Parses YYYY-MM-DD (or a full ISO stamp); end-of-day when asked.</summary>
        private static DateTime ParseDay(string raw, bool endOfDay)
        {
            if (string.IsNullOrEmpty(raw)) return DateTime.MinValue;
            DateTime parsed;
            if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return endOfDay ? parsed.AddDays(1).AddSeconds(-1) : parsed;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed;
            return DateTime.MinValue;
        }
    }
}
