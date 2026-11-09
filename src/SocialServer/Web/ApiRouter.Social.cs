using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ================================================================ me

        private void HandleMe(ApiCtx ctx)
        {
            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Name("me").Raw(MeView(ctx.User, ctx.Session));
            b.Name("site").Raw(SiteJson());
            b.Name("notifications").BeginArray();
            List<Notification> notes = _store.NotificationsFor(ctx.User.Id, 0, 5);
            for (int i = 0; i < notes.Count; i++) b.Raw(NotificationView(notes[i], ctx.User));
            b.EndArray();
            b.Name("threads").BeginArray();
            List<ThreadInfo> threads = _store.ThreadsFor(ctx.User.Id);
            for (int i = 0; i < threads.Count && i < 5; i++) b.Raw(ThreadView(threads[i], ctx.User));
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleLogout(ApiCtx ctx)
        {
            _sessions.Remove(ctx.Session.Id);
            ClearSessionCookie(ctx.Req, ctx.Resp);
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        // =========================================================== badges

        /// <summary>
        /// Grants every achievement the user has earned. Cheap counters, no side
        /// effects when nothing new is unlocked.
        /// </summary>
        private void GrantBadges(User u)
        {
            if (u == null || u.Deleted) return;
            List<string> added = Badges.Apply(_store, u);
            if (added.Count == 0) return;
            _store.MarkUserDirty(u);
            for (int i = 0; i < added.Count; i++)
            {
                _store.Notify(u.Id, u.Id, "badge", "unlocked the \"" + added[i] + "\" badge", "#/profile/" + u.Id);
                Log.Info("USER", "@" + u.Username + " unlocked badge " + added[i]);
            }
        }

        // =========================================================== mentions

        private void NotifyMentions(User author, string text, string link)
        {
            if (author == null || string.IsNullOrEmpty(text)) return;
            List<string> names = Util.Mentions(text);
            int sent = 0;
            for (int i = 0; i < names.Count && sent < 10; i++)
            {
                User target = _store.FindUser(names[i]);
                if (target == null || target.Deleted || target.Id == author.Id) continue;
                _store.Notify(target.Id, author.Id, "mention", "mentioned you", link);
                sent++;
            }
        }

        // ========================================================== settings

        private void HandleSettings(ApiCtx ctx)
        {
            if (IsMethod(ctx.Req, "GET"))
            {
                GrantBadges(ctx.User);
                JsonBuilder gb = new JsonBuilder(512);
                gb.BeginObject();
                gb.Name("user").Raw(UserView(ctx.User, ctx.User, true));
                gb.Name("site").Raw(SiteJson());
                SendOk(ctx, gb);
                return;
            }
            RequireMethod(ctx.Req.Method, "POST");

            if (ctx.Body.ContainsKey("theme"))
            {
                string theme = BodyStr(ctx, "theme", "").Trim().ToLowerInvariant();
                string[] allowed = _cfg.Themes();
                bool ok = false;
                for (int i = 0; i < allowed.Length; i++) if (allowed[i] == theme) ok = true;
                if (!ok) throw new ApiException(400, "bad-theme", "Unknown theme");
                ctx.User.Theme = theme;
            }
            if (ctx.Body.ContainsKey("lang"))
            {
                string lang = BodyStr(ctx, "lang", "").Trim().ToLowerInvariant();
                if (lang != "ru" && lang != "en" && lang != "uk" && lang != "lt")
                    throw new ApiException(400, "bad-lang", "Unknown language");
                ctx.User.Lang = lang;
            }
            if (ctx.Body.ContainsKey("sound")) ctx.User.Sound = BodyBool(ctx, "sound", ctx.User.Sound);
            if (ctx.Body.ContainsKey("hideOnline")) ctx.User.HideOnline = BodyBool(ctx, "hideOnline", ctx.User.HideOnline);
            if (ctx.Body.ContainsKey("privacy"))
            {
                string p = BodyStr(ctx, "privacy", "").Trim().ToLowerInvariant();
                if (p != "public" && p != "friends" && p != "private")
                    throw new ApiException(400, "bad-privacy", "Unknown privacy level");
                ctx.User.Privacy = p;
            }
            if (ctx.Body.ContainsKey("notifyMute"))
            {
                string[] allowed = new string[] { "like", "comment", "friend", "message", "wall", "group", "mention", "badge" };
                List<string> mute = new List<string>();
                List<object> raw = Json.Get(ctx.Body, "notifyMute") as List<object>;
                if (raw != null)
                {
                    for (int i = 0; i < raw.Count; i++)
                    {
                        string type = (raw[i] as string ?? "").Trim().ToLowerInvariant();
                        if (type.Length == 0) continue;
                        bool known = false;
                        for (int k = 0; k < allowed.Length; k++) if (allowed[k] == type) known = true;
                        if (known && !mute.Contains(type)) mute.Add(type);
                    }
                }
                ctx.User.NotifyMute = Badges.Join(mute);
            }
            if (ctx.Body.ContainsKey("status"))
                ctx.User.Status = Util.SingleLine(BodyStr(ctx, "status", ""), _cfg.MaxStatusLength);
            _store.MarkUserDirty(ctx.User);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("user").Raw(UserView(ctx.User, ctx.User, true));
            SendOk(ctx, b);
        }

        private void HandlePassword(ApiCtx ctx)
        {
            string oldVerifier = BodyStr(ctx, "oldVerifier", "");
            string newVerifier = BodyStr(ctx, "newVerifier", "");
            if (!Crypto.IsHex(oldVerifier, 64, 64) || !Crypto.IsHex(newVerifier, 64, 64))
                throw new ApiException(400, "bad-verifier", "Bad verifier");
            if (!_store.CheckVerifier(ctx.User, oldVerifier))
                throw new ApiException(403, "bad-password", "Current password is wrong");

            string sessionSalt = Crypto.RandomHex(Crypto.SaltBytes);
            byte[] newKey = Crypto.DeriveKey(newVerifier, sessionSalt, _cfg.KdfIterations);
            _store.SetVerifier(ctx.User, newVerifier);
            _sessions.RemoveForUser(ctx.User.Id, ctx.Session.Id);

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("sessionSalt", sessionSalt);
            b.Prop("ok", true);
            b.EndObject();
            // answer with the old key, then rotate the live session
            SendSealedKey(ctx.Req, ctx.Resp, ctx.Session.Key, 200, b.ToString());
            ctx.Session.Key = newKey;
            Log.Info("AUTH", "password changed for @" + ctx.User.Username);
        }

        // ============================================================== feed

        private void HandleFeed(ApiCtx ctx)
        {
            string tab = GetQuery(ctx.Query, "tab");
            if (tab == null) tab = "home";
            tab = tab.ToLowerInvariant();
            if (tab != "home" && tab != "global" && tab != "friends") tab = "home";
            int limit = LimitParam(ctx, 20, 50);
            long before = LongQuery(ctx, "before");

            List<Post> posts = _store.Feed(ctx.User.Id, tab, before, limit + 1);
            bool hasMore = posts.Count > limit;
            if (hasMore) posts.RemoveAt(posts.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Prop("tab", tab);
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("hasMore", hasMore);
            b.Prop("next", posts.Count == 0 ? 0 : posts[posts.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void HandleWall(ApiCtx ctx)
        {
            long userId = LongQuery(ctx, "user");
            User target = userId > 0 ? RequireUser(userId) : ctx.User;
            if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");

            int limit = LimitParam(ctx, 20, 50);
            long before = LongQuery(ctx, "before");
            List<Post> posts = _store.Wall(target.Id, ctx.User.Id, before, limit + 1);
            bool hasMore = posts.Count > limit;
            if (hasMore) posts.RemoveAt(posts.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("user").Raw(UserView(target, ctx.User, true));
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("hasMore", hasMore);
            b.Prop("next", posts.Count == 0 ? 0 : posts[posts.Count - 1].Id);
            b.Prop("canPost", CanPostOnWall(ctx.User, target));
            SendOk(ctx, b);
        }

        // ============================================================= posts

        private void HandlePost(ApiCtx ctx)
        {
            if (IsMethod(ctx.Req, "GET"))
            {
                long id = LongQuery(ctx, "id");
                Post p = RequirePost(id);
                if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
                int limit = LimitParam(ctx, 30, 100);
                List<Comment> comments = _store.CommentsFor(p.Id, 0, limit);
                JsonBuilder gb = new JsonBuilder(4096);
                gb.BeginObject();
                gb.Name("post").Raw(PostView(p, ctx.User));
                gb.Name("comments").BeginArray();
                for (int i = comments.Count - 1; i >= 0; i--) gb.Raw(CommentView(comments[i], ctx.User));
                gb.EndArray();
                SendOk(ctx, gb);
                return;
            }

            RequireMethod(ctx.Req.Method, "POST");
            if (!Allow(ctx.User.Id, "post", _cfg.PostsPer10Min, 600))
                throw new ApiException(429, "slow-down", "You are posting too fast");

            string text = Util.CleanText(BodyStr(ctx, "body", ""), _cfg.MaxPostLength);
            string imageData = BodyStr(ctx, "image", "");
            string video = BodyStr(ctx, "video", "").Trim();
            if (video.Length > 0 && !Util.IsHttpUrl(video))
                throw new ApiException(400, "bad-video", "Video link must start with http:// or https://");
            long wallId = BodyLong(ctx, "wall", 0);
            long groupId = BodyLong(ctx, "group", 0);
            string visibility = BodyStr(ctx, "visibility", "public").Trim().ToLowerInvariant();
            if (visibility != "public" && visibility != "friends" && visibility != "private") visibility = "public";
            if (text.Length == 0 && string.IsNullOrEmpty(imageData) && video.Length == 0 &&
                Json.Get(ctx.Body, "poll") == null)
                throw new ApiException(400, "empty", "Write something first");

            if (groupId > 0)
            {
                Group g = _store.FindGroup(groupId);
                if (g == null) throw new ApiException(404, "no-group", "Group not found");
                if (!g.Members.Contains(ctx.User.Id) && g.Owner != ctx.User.Id)
                    throw new ApiException(403, "not-member", "Join the group first");
                wallId = 0;
                visibility = g.Visibility == "private" ? "private" : "public";
            }
            else if (wallId > 0)
            {
                User wall = RequireUser(wallId);
                if (wall.Id != ctx.User.Id)
                {
                    if (!CanViewProfile(ctx.User, wall)) throw new ApiException(403, "private", "This profile is private");
                    if (!_store.AreFriends(ctx.User.Id, wall.Id))
                        throw new ApiException(403, "not-friend", "You can only post on friends' walls");
                }
            }

            string image = "";
            if (!string.IsNullOrEmpty(imageData))
            {
                string err;
                image = _media.SaveDataUrl(imageData, "post", _cfg.MaxPostImageKB, out err);
                if (image == null) throw new ApiException(400, err, "Image could not be saved");
            }

            Post post = _store.CreatePost(ctx.User.Id, wallId, groupId, text, image, visibility);
            post.Video = Util.SingleLine(video, 500);

            Dictionary<string, object> poll = Json.Get(ctx.Body, "poll") as Dictionary<string, object>;
            if (poll != null)
            {
                string question = Util.SingleLine(Json.GetString(poll, "question", ""), Polls.MaxQuestionLength);
                List<object> rawOptions = Json.Get(poll, "options") as List<object>;
                List<string> options = new List<string>();
                if (rawOptions != null)
                {
                    for (int i = 0; i < rawOptions.Count && options.Count < Polls.MaxOptions; i++)
                    {
                        string o = Util.SingleLine(rawOptions[i] as string ?? "", Polls.MaxOptionLength);
                        if (o.Length > 0) options.Add(o);
                    }
                }
                if (question.Length == 0) throw new ApiException(400, "bad-poll", "A poll needs a question");
                if (options.Count < 2) throw new ApiException(400, "bad-poll", "A poll needs at least two options");
                _store.SetPoll(post.Id, question, options);
            }

            ctx.User.LastPostAt = DateTime.UtcNow;
            _store.MarkUserDirty(ctx.User);
            _stats.OnPost();

            if (wallId > 0 && wallId != ctx.User.Id)
                _store.Notify(wallId, ctx.User.Id, "wall", "posted on your wall", "#/post/" + post.Id);
            if (groupId > 0)
            {
                Group g = _store.FindGroup(groupId);
                if (g != null && g.Owner != ctx.User.Id)
                    _store.Notify(g.Owner, ctx.User.Id, "group", "posted in " + g.Name, "#/group/" + g.Id);
            }
            GrantBadges(ctx.User);
            NotifyMentions(ctx.User, text, "#/post/" + post.Id);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("post").Raw(PostView(post, ctx.User));
            SendOk(ctx, b);
        }

        private void HandlePostDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Post p = RequirePost(id);
            if (!CanDeletePost(ctx.User, p)) throw new ApiException(403, "forbidden", "You cannot delete this post");
            if (!string.IsNullOrEmpty(p.Image)) _media.Delete(p.Image);
            _store.DeletePost(id, false);
            Log.Info("DATA", "post #" + id + " deleted by @" + ctx.User.Username);
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        private void HandlePostEdit(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Post p = RequirePost(id);
            if (!CanEditPost(ctx.User, p)) throw new ApiException(403, "forbidden", "You cannot edit this post");
            if (!ctx.User.IsModerator && _cfg.EditWindowMin > 0 &&
                (DateTime.UtcNow - p.Created).TotalMinutes > _cfg.EditWindowMin)
                throw new ApiException(403, "edit-window", "The edit window has expired");

            string text = Util.CleanText(BodyStr(ctx, "body", p.Body), _cfg.MaxPostLength);
            string video = BodyStr(ctx, "video", p.Video).Trim();
            if (video.Length > 0 && !Util.IsHttpUrl(video))
                throw new ApiException(400, "bad-video", "Video link must start with http:// or https://");

            string visibility = BodyStr(ctx, "visibility", p.Visibility).Trim().ToLowerInvariant();
            if (visibility != "public" && visibility != "friends" && visibility != "private")
                visibility = p.Visibility;

            string image = p.Image;
            string imageAction = BodyStr(ctx, "imageAction", "keep").Trim().ToLowerInvariant();
            if (imageAction == "remove")
            {
                if (!string.IsNullOrEmpty(p.Image)) _media.Delete(p.Image);
                image = "";
            }
            else if (imageAction == "replace")
            {
                string imageData = BodyStr(ctx, "image", "");
                string err;
                string url = _media.SaveDataUrl(imageData, "post", _cfg.MaxPostImageKB, out err);
                if (url == null) throw new ApiException(400, err, "Image could not be saved");
                if (!string.IsNullOrEmpty(p.Image)) _media.Delete(p.Image);
                image = url;
            }

            if (text.Length == 0 && string.IsNullOrEmpty(image) && video.Length == 0)
                throw new ApiException(400, "empty", "Write something first");

            _store.UpdatePost(p.Id, text, image, video, visibility);
            Log.Info("DATA", "post #" + p.Id + " edited by @" + ctx.User.Username);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("post").Raw(PostView(p, ctx.User));
            SendOk(ctx, b);
        }

        private void HandlePostPin(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Post p = RequirePost(id);
            if (!CanPinPost(ctx.User, p)) throw new ApiException(403, "forbidden", "You cannot pin this post");
            bool on = BodyBool(ctx, "on", true);
            _store.PinPost(p.Id, on);
            Log.Info("DATA", "post #" + p.Id + (on ? " pinned" : " unpinned") + " by @" + ctx.User.Username);

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("pinned", p.Pinned);
            SendOk(ctx, b);
        }

        private void HandlePollVote(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "post", 0);
            int option = (int)BodyLong(ctx, "option", -1);
            Post p = RequirePost(id);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            if (!Polls.Has(p)) throw new ApiException(400, "no-poll", "This post has no poll");
            if (option < 0 || option >= Polls.Options(p).Count)
                throw new ApiException(400, "bad-option", "Unknown poll option");
            int mine = _store.VotePoll(p.Id, ctx.User.Id, option);
            Log.Debug("DATA", "poll vote on #" + p.Id + " by @" + ctx.User.Username + " -> " + mine);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("post").Raw(PostView(p, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleLikes(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "post");
            Post p = RequirePost(id);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            int limit = LimitParam(ctx, 50, 200);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("users").BeginArray();
            int shown = 0;
            for (int i = p.Likes.Count - 1; i >= 0 && shown < limit; i--)
            {
                User u = _store.FindUser(p.Likes[i]);
                if (u == null || u.Deleted) continue;
                b.Raw(UserView(u, ctx.User, false));
                shown++;
            }
            b.EndArray();
            b.Prop("total", p.Likes.Count);
            SendOk(ctx, b);
        }

        private void HandlePhotos(ApiCtx ctx)
        {
            long userId = LongQuery(ctx, "user");
            User target = userId > 0 ? RequireUser(userId) : ctx.User;
            if (target == null || target.Deleted) throw new ApiException(404, "no-user", "User not found");
            if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");

            int limit = LimitParam(ctx, 24, 60);
            long before = LongQuery(ctx, "before");
            List<Post> raw = _store.PhotoPosts(target.Id, before, limit + 12);
            List<Post> photos = new List<Post>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (!CanViewPost(ctx.User, raw[i])) continue;
                photos.Add(raw[i]);
                if (photos.Count > limit) break;
            }
            bool hasMore = photos.Count > limit;
            if (hasMore) photos.RemoveAt(photos.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("user").Raw(UserSummary(target));
            AppendPhotos(b, "photos", photos);
            b.Prop("hasMore", hasMore);
            b.Prop("next", photos.Count == 0 ? 0 : photos[photos.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void AppendPhotos(JsonBuilder b, string name, List<Post> posts)
        {
            b.Name(name).BeginArray();
            for (int i = 0; i < posts.Count; i++)
            {
                Post p = posts[i];
                b.BeginObject();
                b.Prop("id", p.Id);
                b.Prop("image", p.Image);
                b.Prop("created", Util.Iso(p.Created));
                b.Prop("likes", p.Likes.Count);
                b.Prop("comments", p.Comments);
                b.EndObject();
            }
            b.EndArray();
        }

        private void HandleBookmark(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Post p = RequirePost(id);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            bool on = _store.ToggleBookmark(ctx.User.Id, id);
            if (on) GrantBadges(ctx.User);
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("bookmarked", on);
            b.Prop("total", _store.CountBookmarks(ctx.User.Id));
            SendOk(ctx, b);
        }

        private void HandleBookmarks(ApiCtx ctx)
        {
            int limit = LimitParam(ctx, 20, 50);
            long before = LongQuery(ctx, "before");
            List<Post> posts = _store.BookmarksFor(ctx.User.Id, before, limit + 1);
            bool hasMore = posts.Count > limit;
            if (hasMore) posts.RemoveAt(posts.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("hasMore", hasMore);
            b.Prop("next", posts.Count == 0 ? 0 : posts[posts.Count - 1].Id);
            b.Prop("total", _store.CountBookmarks(ctx.User.Id));
            SendOk(ctx, b);
        }

        private void HandleLike(ApiCtx ctx)        {
            long id = BodyLong(ctx, "id", 0);
            Post p = RequirePost(id);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            if (!Allow(ctx.User.Id, "like", _cfg.LikesPerMin, 60))
                throw new ApiException(429, "slow-down", "Too many likes");

            bool liked = _store.ToggleLike(id, ctx.User.Id);
            if (liked)
            {
                _stats.OnLike();
                if (p.Author != ctx.User.Id)
                    _store.Notify(p.Author, ctx.User.Id, "like", "liked your post", "#/post/" + p.Id);
                GrantBadges(ctx.User);
                GrantBadges(_store.FindUser(p.Author));
            }

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("liked", liked);
            b.Prop("likes", p.Likes.Count);
            SendOk(ctx, b);
        }

        private void HandleComments(ApiCtx ctx)
        {
            long postId = LongQuery(ctx, "post");
            Post p = RequirePost(postId);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            int limit = LimitParam(ctx, 30, 100);
            long before = LongQuery(ctx, "before");
            List<Comment> comments = _store.CommentsFor(postId, before, limit + 1);
            bool hasMore = comments.Count > limit;
            if (hasMore) comments.RemoveAt(comments.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("comments").BeginArray();
            for (int i = comments.Count - 1; i >= 0; i--) b.Raw(CommentView(comments[i], ctx.User));
            b.EndArray();
            b.Prop("hasMore", hasMore);
            b.Prop("next", comments.Count == 0 ? 0 : comments[comments.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void HandleComment(ApiCtx ctx)
        {
            long postId = BodyLong(ctx, "post", 0);
            Post p = RequirePost(postId);
            if (!CanViewPost(ctx.User, p)) throw new ApiException(403, "private", "This post is not available");
            if (!Allow(ctx.User.Id, "comment", _cfg.CommentsPer10Min, 600))
                throw new ApiException(429, "slow-down", "You are commenting too fast");
            string text = Util.CleanText(BodyStr(ctx, "body", ""), _cfg.MaxCommentLength);
            if (text.Length == 0) throw new ApiException(400, "empty", "Write something first");

            Comment c = _store.AddComment(postId, ctx.User.Id, text);
            if (c == null) throw new ApiException(404, "no-post", "Post not found");
            ctx.User.LastCommentAt = DateTime.UtcNow;
            _store.MarkUserDirty(ctx.User);
            _stats.OnComment();

            if (p.Author != ctx.User.Id)
                _store.Notify(p.Author, ctx.User.Id, "comment", "commented on your post", "#/post/" + p.Id);
            if (p.Wall > 0 && p.Wall != ctx.User.Id && p.Wall != p.Author)
                _store.Notify(p.Wall, ctx.User.Id, "comment", "commented on a post on your wall", "#/post/" + p.Id);
            GrantBadges(ctx.User);
            GrantBadges(_store.FindUser(p.Author));
            NotifyMentions(ctx.User, text, "#/post/" + p.Id);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("comment").Raw(CommentView(c, ctx.User));
            b.Prop("comments", p.Comments);
            SendOk(ctx, b);
        }

        private void HandleCommentDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Comment c = _store.FindComment(id);
            if (c == null || c.Deleted) throw new ApiException(404, "no-comment", "Comment not found");
            if (!CanDeleteComment(ctx.User, c)) throw new ApiException(403, "forbidden", "You cannot delete this comment");
            _store.DeleteComment(id);
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        // =========================================================== profile

        private void HandleProfile(ApiCtx ctx)
        {
            if (IsMethod(ctx.Req, "GET"))
            {
                long id = LongQuery(ctx, "user");
                string username = GetQuery(ctx.Query, "username");
                User target;
                if (!string.IsNullOrEmpty(username)) target = _store.FindUser(username);
                else target = id > 0 ? RequireUser(id) : ctx.User;
                if (target == null || target.Deleted) throw new ApiException(404, "no-user", "User not found");
                if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");

                List<Post> posts = _store.Wall(target.Id, ctx.User.Id, 0, 10);
                List<User> friends = _store.FriendsOf(target.Id);
                if (friends.Count > 12) friends.RemoveRange(12, friends.Count - 12);
                List<Group> groups = _store.GroupsFor(target.Id);
                if (groups.Count > 10) groups.RemoveRange(10, groups.Count - 10);

                JsonBuilder gb = new JsonBuilder(8192);
                gb.BeginObject();
                gb.Name("user").Raw(UserView(target, ctx.User, true));
                AppendPosts(gb, "posts", posts, ctx.User);
                gb.Name("friends").BeginArray();
                for (int i = 0; i < friends.Count; i++) gb.Raw(UserView(friends[i], ctx.User, false));
                gb.EndArray();
                gb.Name("groups").BeginArray();
                for (int i = 0; i < groups.Count; i++) gb.Raw(GroupView(groups[i], ctx.User));
                gb.EndArray();

                List<Post> photoPosts = _store.PhotoPosts(target.Id, 0, 24);
                List<Post> visiblePhotos = new List<Post>();
                for (int i = 0; i < photoPosts.Count && visiblePhotos.Count < 9; i++)
                    if (CanViewPost(ctx.User, photoPosts[i])) visiblePhotos.Add(photoPosts[i]);
                AppendPhotos(gb, "photos", visiblePhotos);

                gb.Prop("canPost", CanPostOnWall(ctx.User, target));
                SendOk(ctx, gb);
                return;
            }

            RequireMethod(ctx.Req.Method, "POST");
            if (ctx.Body.ContainsKey("displayName"))
                ctx.User.DisplayName = Util.DisplayName(BodyStr(ctx, "displayName", ""), ctx.User.Username, _cfg.MaxNameLength);
            if (ctx.Body.ContainsKey("bio"))
                ctx.User.Bio = Util.CleanText(BodyStr(ctx, "bio", ""), _cfg.MaxBioLength);
            if (ctx.Body.ContainsKey("city"))
                ctx.User.City = Util.SingleLine(BodyStr(ctx, "city", ""), 80);
            if (ctx.Body.ContainsKey("country"))
                ctx.User.Country = Util.SingleLine(BodyStr(ctx, "country", ""), 80);
            if (ctx.Body.ContainsKey("birthday"))
            {
                string bd = BodyStr(ctx, "birthday", "").Trim();
                if (bd.Length > 0)
                {
                    DateTime parsed;
                    if (!DateTime.TryParseExact(bd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out parsed) || parsed.Year < 1900)
                        throw new ApiException(400, "bad-birthday", "Birthday must be YYYY-MM-DD");
                }
                ctx.User.Birthday = bd;
            }
            if (ctx.Body.ContainsKey("gender"))
            {
                string g = BodyStr(ctx, "gender", "").Trim().ToLowerInvariant();
                if (g != "" && g != "m" && g != "f" && g != "other")
                    throw new ApiException(400, "bad-gender", "Unknown gender");
                ctx.User.Gender = g;
            }
            if (ctx.Body.ContainsKey("website"))
            {
                string site = BodyStr(ctx, "website", "").Trim();
                if (site.Length > 0 && !site.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    throw new ApiException(400, "bad-website", "Website must start with http:// or https://");
                ctx.User.Website = Util.SingleLine(site, 200);
            }
            if (ctx.Body.ContainsKey("email"))
            {
                string email = BodyStr(ctx, "email", "").Trim();
                if (email.Length > 0 && !Util.IsValidEmail(email))
                    throw new ApiException(400, "bad-email", "Bad email address");
                ctx.User.Email = Util.SingleLine(email, 160);
            }
            if (ctx.Body.ContainsKey("status"))
                ctx.User.Status = Util.SingleLine(BodyStr(ctx, "status", ""), _cfg.MaxStatusLength);
            if (ctx.Body.ContainsKey("favGames"))
                ctx.User.FavGames = Util.SingleLine(BodyStr(ctx, "favGames", ""), 200);
            if (ctx.Body.ContainsKey("favMovies"))
                ctx.User.FavMovies = Util.SingleLine(BodyStr(ctx, "favMovies", ""), 200);
            if (ctx.Body.ContainsKey("favMusic"))
                ctx.User.FavMusic = Util.SingleLine(BodyStr(ctx, "favMusic", ""), 200);
            if (ctx.Body.ContainsKey("favBooks"))
                ctx.User.FavBooks = Util.SingleLine(BodyStr(ctx, "favBooks", ""), 200);
            if (ctx.Body.ContainsKey("mood"))
            {
                string mood = BodyStr(ctx, "mood", "").Trim().ToLowerInvariant();
                if (mood != "" && mood != "happy" && mood != "sad" && mood != "neutral")
                    throw new ApiException(400, "bad-mood", "Unknown mood");
                ctx.User.Mood = mood;
            }
            _store.MarkUserDirty(ctx.User);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("user").Raw(UserView(ctx.User, ctx.User, true));
            SendOk(ctx, b);
        }

        private void HandleAvatar(ApiCtx ctx)
        {
            string data = BodyStr(ctx, "data", "");
            string err;
            string url = _media.SaveDataUrl(data, "avatar", _cfg.MaxAvatarKB, out err);
            if (url == null) throw new ApiException(400, err, "Avatar could not be saved");
            string old = ctx.User.Avatar;
            ctx.User.Avatar = url;
            _store.MarkUserDirty(ctx.User);
            if (!string.IsNullOrEmpty(old)) _media.Delete(old);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("avatar", url);
            SendOk(ctx, b);
        }

        // ============================================================= people

        private void HandlePeople(ApiCtx ctx)
        {
            string q = GetQuery(ctx.Query, "q");
            int limit = LimitParam(ctx, 24, 60);
            int offset = 0;
            string off = GetQuery(ctx.Query, "offset");
            if (!string.IsNullOrEmpty(off)) int.TryParse(off, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);

            List<User> users;
            if (!string.IsNullOrEmpty(q)) users = _store.SearchUsers(q, 200);
            else
            {
                users = _store.AllUsers();
                users.Sort(delegate(User x, User y) { return y.LastSeen.CompareTo(x.LastSeen); });
            }
            int total = users.Count;
            if (offset > 0 && offset < users.Count) users.RemoveRange(0, offset);
            else if (offset >= users.Count) users.Clear();
            if (users.Count > limit) users.RemoveRange(limit, users.Count - limit);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("users").BeginArray();
            for (int i = 0; i < users.Count; i++) b.Raw(UserView(users[i], ctx.User, false));
            b.EndArray();
            b.Prop("total", total);
            b.Prop("offset", offset);
            b.Prop("hasMore", offset + users.Count < total);
            SendOk(ctx, b);
        }

        private void HandleSearch(ApiCtx ctx)
        {
            string q = GetQuery(ctx.Query, "q");
            if (string.IsNullOrEmpty(q) || q.Trim().Length < 2)
            {
                JsonBuilder empty = new JsonBuilder(64);
                empty.BeginObject();
                empty.Name("users").BeginArray().EndArray();
                empty.Name("posts").BeginArray().EndArray();
                SendOk(ctx, empty);
                return;
            }
            if (!Allow(ctx.User.Id, "search", _cfg.SearchPerMin, 60))
                throw new ApiException(429, "slow-down", "Too many searches");

            List<User> users = _store.SearchUsers(q, 12);
            List<Post> posts = _store.SearchPosts(q, 20);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("users").BeginArray();
            for (int i = 0; i < users.Count; i++) b.Raw(UserView(users[i], ctx.User, false));
            b.EndArray();
            AppendPosts(b, "posts", posts, ctx.User);
            SendOk(ctx, b);
        }

        // ============================================================ friends

        private void HandleFriends(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "user");
            User target = id > 0 ? RequireUser(id) : ctx.User;
            if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");

            List<User> friends = _store.FriendsOf(target.Id);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("user").Raw(UserSummary(target));
            b.Name("friends").BeginArray();
            for (int i = 0; i < friends.Count; i++) b.Raw(UserView(friends[i], ctx.User, false));
            b.EndArray();
            b.Prop("total", friends.Count);

            if (target.Id == ctx.User.Id)
            {
                b.Name("incoming").BeginArray();
                List<FriendLink> incoming = _store.PendingRequests(ctx.User.Id);
                for (int i = 0; i < incoming.Count; i++)
                {
                    User u = _store.FindUser(incoming[i].From);
                    if (u != null) b.Raw(UserView(u, ctx.User, false));
                }
                b.EndArray();
                b.Name("outgoing").BeginArray();
                List<FriendLink> outgoing = _store.OutgoingRequests(ctx.User.Id);
                for (int i = 0; i < outgoing.Count; i++)
                {
                    User u = _store.FindUser(outgoing[i].To);
                    if (u != null) b.Raw(UserView(u, ctx.User, false));
                }
                b.EndArray();
            }
            SendOk(ctx, b);
        }

        private void HandleFriendAction(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "user", 0);
            string action = BodyStr(ctx, "action", "").Trim().ToLowerInvariant();
            User target = RequireUser(id);
            if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "That is you");

            string status = "";
            if (action == "request") status = _store.SendFriendRequest(ctx.User.Id, target.Id);
            else if (action == "accept")
            {
                if (!_store.AcceptFriend(target.Id, ctx.User.Id)) throw new ApiException(400, "no-request", "No pending request");
                status = "accepted";
            }
            else if (action == "decline")
            {
                if (!_store.DeclineFriend(target.Id, ctx.User.Id)) throw new ApiException(400, "no-request", "No pending request");
                status = "declined";
            }
            else if (action == "remove")
            {
                _store.RemoveFriend(ctx.User.Id, target.Id);
                status = "removed";
            }
            else if (action == "block")
            {
                _store.BlockUser(ctx.User.Id, target.Id);
                status = "blocked";
            }
            else if (action == "unblock")
            {
                _store.RemoveFriend(ctx.User.Id, target.Id);
                status = "unblocked";
            }
            else throw new ApiException(400, "bad-action", "Unknown friend action");

            if (status == "accepted" || status == "request")
            {
                GrantBadges(ctx.User);
                GrantBadges(target);
            }

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("status", status);
            b.Name("user").Raw(UserView(target, ctx.User, false));
            SendOk(ctx, b);
        }

        // =========================================================== messages

        private void HandleThreads(ApiCtx ctx)
        {
            List<ThreadInfo> threads = _store.ThreadsFor(ctx.User.Id);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("threads").BeginArray();
            for (int i = 0; i < threads.Count; i++) b.Raw(ThreadView(threads[i], ctx.User));
            b.EndArray();
            b.Prop("unread", _store.UnreadMessages(ctx.User.Id));
            SendOk(ctx, b);
        }

        private void HandleMessages(ApiCtx ctx)
        {
            RequireMethod(ctx.Req.Method, "GET");
            long id = LongQuery(ctx, "with");
            User other = RequireUser(id);
            if (_store.IsBlocked(other.Id, ctx.User.Id) || _store.IsBlocked(ctx.User.Id, other.Id))
                throw new ApiException(403, "blocked", "This conversation is blocked");

            int limit = LimitParam(ctx, 40, 100);
            long before = LongQuery(ctx, "before");
            List<Message> messages = _store.Conversation(ctx.User.Id, other.Id, before, limit + 1);
            bool hasMore = messages.Count > limit;
            if (hasMore) messages.RemoveAt(messages.Count - 1);
            long next = messages.Count == 0 ? 0 : messages[messages.Count - 1].Id;
            _store.MarkThreadRead(ctx.User.Id, other.Id);

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("other").Raw(UserSummary(other));
            b.Name("messages").BeginArray();
            for (int i = messages.Count - 1; i >= 0; i--) b.Raw(MessageView(messages[i], ctx.User));
            b.EndArray();
            b.Prop("hasMore", hasMore);
            b.Prop("next", next);
            SendOk(ctx, b);
        }

        private void HandleMessageSend(ApiCtx ctx)
        {
            long to = BodyLong(ctx, "to", 0);
            User target = RequireUser(to);
            if (!CanMessageUser(ctx.User, target)) throw new ApiException(403, "cannot-message", "You cannot message this user");
            if (!Allow(ctx.User.Id, "message", _cfg.MessagesPerMin, 60))
                throw new ApiException(429, "slow-down", "You are sending messages too fast");
            string text = Util.CleanText(BodyStr(ctx, "body", ""), _cfg.MaxMessageLength);
            string imageData = BodyStr(ctx, "image", "");
            long replyTo = BodyLong(ctx, "replyTo", 0);
            if (text.Length == 0 && string.IsNullOrEmpty(imageData))
                throw new ApiException(400, "empty", "Write something first");

            string image = "";
            if (!string.IsNullOrEmpty(imageData))
            {
                string err;
                image = _media.SaveDataUrl(imageData, "message", _cfg.MaxMessageImageKB, out err);
                if (image == null) throw new ApiException(400, err, "Image could not be saved");
            }
            if (replyTo > 0)
            {
                Message quoted = _store.FindMessage(replyTo);
                if (quoted == null || (quoted.From != ctx.User.Id && quoted.To != ctx.User.Id))
                    throw new ApiException(400, "bad-reply", "The quoted message is not part of this conversation");
            }

            Message m = _store.CreateMessage(ctx.User.Id, target.Id, text, image, replyTo);
            TypingRegistry.Clear(ctx.User.Id, target.Id);
            ctx.User.LastMessageAt = DateTime.UtcNow;
            _store.MarkUserDirty(ctx.User);
            _stats.OnMessage();
            NotifyMessageOnce(target.Id, ctx.User.Id, m.Id);
            GrantBadges(ctx.User);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("message").Raw(MessageView(m, ctx.User));
            SendOk(ctx, b);
        }

        private void NotifyMessageOnce(long to, long from, long messageId)
        {
            // one notification per conversation per 10 minutes is enough noise
            List<Notification> recent = _store.NotificationsFor(to, 0, 30);
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            for (int i = 0; i < recent.Count; i++)
            {
                Notification n = recent[i];
                if (n.Type == "message" && n.Actor == from && n.Created >= cutoff) return;
            }
            _store.Notify(to, from, "message", "sent you a message", "#/im/" + from);
        }

        private void HandleMessageRead(ApiCtx ctx)
        {
            long with = BodyLong(ctx, "with", 0);
            _store.MarkThreadRead(ctx.User.Id, with);
            TypingRegistry.Clear(ctx.User.Id, with);
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("unread", _store.UnreadMessages(ctx.User.Id));
            SendOk(ctx, b);
        }

        private void HandleMessageTyping(ApiCtx ctx)
        {
            long to = BodyLong(ctx, "to", 0);
            User target = RequireUser(to);
            if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "That is you");
            TypingRegistry.Touch(ctx.User.Id, target.Id);
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            b.Prop("ok", true);
            SendOk(ctx, b);
        }

        /// <summary>
        /// Long poll for one conversation: answers as soon as a message newer than
        /// `after` arrives, the peer starts or stops typing, or the read state of
        /// our last message changes. Everything else is a short wait.
        /// </summary>
        private void HandleMessagesWait(ApiCtx ctx)
        {
            long with = LongQuery(ctx, "with");
            User other = RequireUser(with);
            if (_store.IsBlocked(other.Id, ctx.User.Id) || _store.IsBlocked(ctx.User.Id, other.Id))
                throw new ApiException(403, "blocked", "This conversation is blocked");

            long after = LongQuery(ctx, "after");
            int timeout = _cfg.NotifyWaitSec;
            string raw = GetQuery(ctx.Query, "timeout");
            if (!string.IsNullOrEmpty(raw))
            {
                int parsed;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) timeout = parsed;
            }
            if (timeout < 1) timeout = 1;
            if (timeout > 25) timeout = 25;
            if (!_cfg.LiveNotifications) timeout = 1;

            bool typingAtStart = TypingRegistry.IsTyping(other.Id, ctx.User.Id);
            bool readAtStart = _store.LastMineRead(ctx.User.Id, other.Id);
            DateTime until = DateTime.UtcNow.AddSeconds(timeout);
            List<Message> fresh = new List<Message>();

            while (true)
            {
                if (_store.LastMessageId(ctx.User.Id, other.Id) > after)
                {
                    fresh = _store.MessagesAfter(ctx.User.Id, other.Id, after, 50);
                    if (fresh.Count > 0) break;
                }
                bool typing = TypingRegistry.IsTyping(other.Id, ctx.User.Id);
                bool read = _store.LastMineRead(ctx.User.Id, other.Id);
                if (typing != typingAtStart || read != readAtStart) break;
                if (DateTime.UtcNow >= until) break;
                Thread.Sleep(300);
            }

            // newest first from the store, oldest first for the client
            if (fresh.Count > 1) fresh.Reverse();
            if (fresh.Count > 0) _store.MarkThreadRead(ctx.User.Id, other.Id);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("messages").BeginArray();
            for (int i = 0; i < fresh.Count; i++) b.Raw(MessageView(fresh[i], ctx.User));
            b.EndArray();
            b.Prop("latest", _store.LastMessageId(ctx.User.Id, other.Id));
            b.Prop("typing", TypingRegistry.IsTyping(other.Id, ctx.User.Id));
            b.Prop("read", _store.LastMineRead(ctx.User.Id, other.Id));
            b.Prop("unread", _store.UnreadMessages(ctx.User.Id));
            b.Prop("live", _cfg.LiveNotifications);
            SendOk(ctx, b);
        }

        private void HandleMessageDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Message m = _store.FindMessage(id);
            if (m == null) throw new ApiException(404, "no-message", "Message not found");
            string image = m.Image;
            if (!_store.DeleteMessage(id, ctx.User.Id)) throw new ApiException(404, "no-message", "Message not found");
            // the picture only goes away once both sides dropped the message
            if (!string.IsNullOrEmpty(image) && m.DeletedByFrom && m.DeletedByTo) _media.Delete(image);
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        // ====================================================== notifications

        private void HandleNotifications(ApiCtx ctx)
        {
            int limit = LimitParam(ctx, 30, 100);
            long before = LongQuery(ctx, "before");
            List<Notification> list = _store.NotificationsFor(ctx.User.Id, before, limit + 1);
            bool hasMore = list.Count > limit;
            if (hasMore) list.RemoveAt(list.Count - 1);

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("notifications").BeginArray();
            for (int i = 0; i < list.Count; i++) b.Raw(NotificationView(list[i], ctx.User));
            b.EndArray();
            b.Prop("unread", _store.UnreadNotifications(ctx.User.Id));
            b.Prop("hasMore", hasMore);
            b.Prop("next", list.Count == 0 ? 0 : list[list.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void HandleNotificationsRead(ApiCtx ctx)
        {
            bool all = BodyBool(ctx, "all", false);
            long id = BodyLong(ctx, "id", 0);
            _store.MarkNotifications(ctx.User.Id, id, all);
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("unread", _store.UnreadNotifications(ctx.User.Id));
            SendOk(ctx, b);
        }

        // ========================================================= live notifications

        /// <summary>
        /// Long poll: blocks until a notification newer than `after` exists or the
        /// timeout expires, then answers with the new ones. Used by the SPA so new
        /// likes/comments/mentions pop up instantly instead of on the next poll tick.
        /// </summary>
        private void HandleNotificationsWait(ApiCtx ctx)
        {
            long after = LongQuery(ctx, "after");
            int timeout = _cfg.NotifyWaitSec;
            string raw = GetQuery(ctx.Query, "timeout");
            if (!string.IsNullOrEmpty(raw))
            {
                int parsed;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) timeout = parsed;
            }
            if (timeout < 1) timeout = 1;
            if (timeout > 25) timeout = 25;
            if (!_cfg.LiveNotifications) timeout = 1;

            DateTime until = DateTime.UtcNow.AddSeconds(timeout);
            List<Notification> fresh = new List<Notification>();
            while (true)
            {
                if (_store.LatestNotificationId(ctx.User.Id) > after)
                {
                    List<Notification> all = _store.NotificationsFor(ctx.User.Id, 0, 20);
                    for (int i = 0; i < all.Count; i++) if (all[i].Id > after) fresh.Add(all[i]);
                    if (fresh.Count > 0) break;
                }
                if (DateTime.UtcNow >= until) break;
                // short sleeps keep shutdown responsive while staying cheap
                Thread.Sleep(350);
            }

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("notifications").BeginArray();
            for (int i = 0; i < fresh.Count; i++) b.Raw(NotificationView(fresh[i], ctx.User));
            b.EndArray();
            b.Prop("unread", _store.UnreadNotifications(ctx.User.Id));
            b.Prop("latest", _store.LatestNotificationId(ctx.User.Id));
            b.Prop("live", _cfg.LiveNotifications);
            SendOk(ctx, b);
        }

        // ============================================================= online

        private void HandleOnline(ApiCtx ctx)
        {
            List<User> users = _store.OnlineUsers(_cfg.OnlineWindowSec, 60);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("users").BeginArray();
            for (int i = 0; i < users.Count; i++) b.Raw(UserView(users[i], ctx.User, false));
            b.EndArray();
            b.Prop("online", _store.CountOnline(_cfg.OnlineWindowSec));
            b.Prop("total", _store.CountUsers());
            SendOk(ctx, b);
        }

        private void HandleStats(ApiCtx ctx)
        {
            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Prop("users", _store.CountUsers());
            b.Prop("online", _store.CountOnline(_cfg.OnlineWindowSec));
            b.Prop("posts", _store.CountAllPosts());
            b.Prop("comments", _store.CountAllComments());
            b.Prop("likes", _store.CountAllLikes());
            b.Prop("messages", _store.CountAllMessages());
            b.Prop("groups", _store.CountGroups());
            b.Prop("peakOnline", _stats.PeakOnline);
            b.Prop("visitsTotal", _stats.VisitsTotal);
            b.Prop("uniqueTotal", _stats.UniqueTotal);
            b.Prop("visitsToday", _stats.ViewsToday());
            b.Prop("uniqueToday", _stats.UniqueToday());
            b.Prop("starts", _stats.Starts);
            b.Name("days").BeginArray();
            List<KeyValuePair<string, DayStat>> days = _stats.RecentDays(7);
            for (int i = 0; i < days.Count; i++)
            {
                b.BeginObject();
                b.Prop("day", days[i].Key);
                b.Prop("views", days[i].Value.Views);
                b.Prop("unique", days[i].Value.Unique);
                b.EndObject();
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        // ============================================================= groups

        private void HandleGroups(ApiCtx ctx)
        {
            if (IsMethod(ctx.Req, "GET"))
            {
                List<Group> groups = _store.GroupsFor(ctx.User.Id);
                JsonBuilder b = new JsonBuilder(4096);
                b.BeginObject();
                b.Name("groups").BeginArray();
                for (int i = 0; i < groups.Count; i++) b.Raw(GroupView(groups[i], ctx.User));
                b.EndArray();
                SendOk(ctx, b);
                return;
            }
            RequireMethod(ctx.Req.Method, "POST");
            HandleGroupCreate(ctx);
        }

        private void HandleGroupCreate(ApiCtx ctx)
        {
            if (!Allow(ctx.User.Id, "group", 5, 3600))
                throw new ApiException(429, "slow-down", "Too many groups");
            string name = Util.SingleLine(BodyStr(ctx, "name", ""), 80);
            if (name.Length < 2) throw new ApiException(400, "bad-name", "Group name is too short");
            string description = Util.CleanText(BodyStr(ctx, "description", ""), 2000);
            string visibility = BodyStr(ctx, "visibility", "public").Trim().ToLowerInvariant();
            Group g = _store.CreateGroup(ctx.User.Id, name, description, visibility);
            Log.Info("DATA", "group #" + g.Id + " " + g.Name + " created by @" + ctx.User.Username);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGroup(ApiCtx ctx)
        {
            RequireMethod(ctx.Req.Method, "GET");
            long id = LongQuery(ctx, "id");
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            bool member = g.Members.Contains(ctx.User.Id) || g.Owner == ctx.User.Id;
            if (g.Visibility == "private" && !member) throw new ApiException(403, "private", "This group is private");

            List<Post> posts = _store.GroupFeed(g.Id, ctx.User.Id, 0, 10);
            List<User> members = new List<User>();
            for (int i = 0; i < g.Members.Count && i < 60; i++)
            {
                User u = _store.FindUser(g.Members[i]);
                if (u != null) members.Add(u);
            }

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            AppendPosts(b, "posts", posts, ctx.User);
            b.Name("members").BeginArray();
            for (int i = 0; i < members.Count; i++) b.Raw(UserView(members[i], ctx.User, false));
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleGroupJoin(ApiCtx ctx, bool join)
        {
            long id = BodyLong(ctx, "id", 0);
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            if (join) _store.JoinGroup(id, ctx.User.Id);
            else if (!_store.LeaveGroup(id, ctx.User.Id))
                throw new ApiException(400, "owner", "The owner cannot leave the group");

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGroupEdit(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            if (g.Owner != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-owner", "Only the owner can edit the group");

            string name = BodyStr(ctx, "name", g.Name).Trim();
            if (name.Length < 2) throw new ApiException(400, "bad-name", "Name is too short");
            if (name.Length > 80) name = Util.SingleLine(name, 80);

            _store.UpdateGroup(g.Id, name, BodyStr(ctx, "description", g.Description),
                BodyStr(ctx, "visibility", g.Visibility));
            Log.Info("DATA", "group #" + g.Id + " edited by @" + ctx.User.Username);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGroupAvatar(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            if (g.Owner != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-owner", "Only the owner can edit the group");

            string err;
            string url = _media.SaveDataUrl(BodyStr(ctx, "data", ""), "group", _cfg.MaxAvatarKB, out err);
            if (url == null) throw new ApiException(400, err, "Image could not be saved");
            string old = g.Avatar;
            _store.SetGroupAvatar(g.Id, url);
            if (!string.IsNullOrEmpty(old)) _media.Delete(old);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("avatar", url);
            SendOk(ctx, b);
        }

        private void HandleGroupKick(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            long userId = BodyLong(ctx, "user", 0);
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            if (g.Owner != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-owner", "Only the owner can remove members");
            if (userId == g.Owner) throw new ApiException(400, "owner", "The owner cannot be removed");
            if (!_store.RemoveGroupMember(g.Id, userId))
                throw new ApiException(400, "not-member", "That user is not a member");

            _store.Notify(userId, ctx.User.Id, "group", "removed you from " + g.Name, "#/group/" + g.Id);
            Log.Info("DATA", "@" + ctx.User.Username + " removed #" + userId + " from group #" + g.Id);

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        // ============================================================== tags

        private void HandleTag(ApiCtx ctx)
        {
            string tag = (GetQuery(ctx.Query, "tag") ?? "").Trim();
            if (tag.StartsWith("#", StringComparison.Ordinal)) tag = tag.Substring(1);
            tag = tag.ToLowerInvariant();
            if (tag.Length < 2 || tag.Length > 40)
                throw new ApiException(400, "bad-tag", "Tag must be 2-40 characters");

            int limit = LimitParam(ctx, 20, 50);
            long before = LongQuery(ctx, "before");
            List<Post> posts = _store.PostsByTag(tag, ctx.User.Id, before, limit + 1);
            bool hasMore = posts.Count > limit;
            if (hasMore) posts.RemoveAt(posts.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Prop("tag", tag);
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("hasMore", hasMore);
            b.Prop("next", posts.Count == 0 ? 0 : posts[posts.Count - 1].Id);
            SendOk(ctx, b);
        }

        private void HandleTags(ApiCtx ctx)
        {
            List<KeyValuePair<string, int>> tags = _store.TopTags(20);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("tags").BeginArray();
            for (int i = 0; i < tags.Count; i++)
            {
                b.BeginObject();
                b.Prop("tag", tags[i].Key);
                b.Prop("count", tags[i].Value);
                b.EndObject();
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleGroupFeed(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Group not found");
            bool member = g.Members.Contains(ctx.User.Id) || g.Owner == ctx.User.Id;
            if (g.Visibility == "private" && !member) throw new ApiException(403, "private", "This group is private");
            int limit = LimitParam(ctx, 20, 50);
            long before = LongQuery(ctx, "before");
            List<Post> posts = _store.GroupFeed(id, ctx.User.Id, before, limit + 1);
            bool hasMore = posts.Count > limit;
            if (hasMore) posts.RemoveAt(posts.Count - 1);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            AppendPosts(b, "posts", posts, ctx.User);
            b.Prop("hasMore", hasMore);
            b.Prop("next", posts.Count == 0 ? 0 : posts[posts.Count - 1].Id);
            SendOk(ctx, b);
        }

        // ============================================================= report

        private void HandleReport(ApiCtx ctx)
        {
            if (!Allow(ctx.User.Id, "report", _cfg.ReportsPerHour, 3600))
                throw new ApiException(429, "slow-down", "Too many reports");
            string type = BodyStr(ctx, "type", "").Trim().ToLowerInvariant();
            if (type != "post" && type != "comment" && type != "user" && type != "message")
                throw new ApiException(400, "bad-type", "Unknown report type");
            long target = BodyLong(ctx, "target", 0);
            if (target <= 0) throw new ApiException(400, "bad-target", "Missing target");
            string reason = Util.CleanText(BodyStr(ctx, "reason", ""), 1000);
            if (reason.Length == 0) throw new ApiException(400, "empty", "Describe the problem");

            Report r = _store.CreateReport(type, target, ctx.User.Id, reason);
            _stats.OnReport();
            List<User> users = _store.AllUsers();
            for (int i = 0; i < users.Count; i++)
            {
                if (users[i].Role != "admin" || users[i].Deleted) continue;
                _store.Notify(users[i].Id, ctx.User.Id, "report", "new report #" + r.Id + " (" + type + ")", "#/admin/reports");
            }

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("id", r.Id);
            SendOk(ctx, b);
        }

        // ============================================================ helpers

        private bool CanViewPost(User viewer, Post p)
        {
            if (p == null || p.Deleted) return false;
            if (p.Group > 0)
            {
                Group g = _store.FindGroup(p.Group);
                if (g == null) return false;
                if (g.Visibility == "private" && (viewer == null || (!g.Members.Contains(viewer.Id) && g.Owner != viewer.Id)))
                    return false;
                return _store.VisibleTo(viewer == null ? 0 : viewer.Id, p);
            }
            return _store.VisibleTo(viewer == null ? 0 : viewer.Id, p);
        }

        private bool CanPostOnWall(User viewer, User wall)
        {
            if (viewer == null || wall == null || wall.Deleted || wall.Banned) return false;
            if (viewer.Id == wall.Id) return true;
            return _store.AreFriends(viewer.Id, wall.Id);
        }

        private void AppendPosts(JsonBuilder b, string name, List<Post> posts, User viewer)
        {
            b.Name(name).BeginArray();
            for (int i = 0; i < posts.Count; i++) b.Raw(PostView(posts[i], viewer));
            b.EndArray();
        }
    }
}

