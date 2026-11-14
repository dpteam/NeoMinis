using System;
using System.Collections.Generic;
using Social.Core;

namespace Social.Web
{
    /// <summary>
    /// Stories: photos and short clips that disappear after a while. Everything
    /// here is time-sensitive, so an expired story is invisible to every endpoint
    /// even before the supervisor sweeps its file away.
    /// </summary>
    public sealed partial class ApiRouter
    {
        private static DateTime _lastStorySweep = DateTime.UtcNow;

        private string StoryJson(Story s, User viewer)
        {
            bool mine = viewer != null && viewer.Id == s.Author;
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", s.Id);
            b.Prop("image", s.Image);
            b.Prop("video", s.Video);
            b.Prop("caption", s.Caption);
            b.Prop("created", Util.Iso(s.Created));
            b.Prop("expires", Util.Iso(s.Expires));
            b.Prop("expiresIn", Math.Max(0, (int)(s.Expires - DateTime.UtcNow).TotalSeconds));
            b.Prop("mine", mine);
            b.Prop("seen", mine || (viewer != null && s.Viewers.Contains(viewer.Id)));
            b.Prop("views", mine ? s.Viewers.Count : 0);
            b.Name("author").Raw(UserSummary(_store.FindUser(s.Author)));
            b.EndObject();
            return b.ToString();
        }

        /// <summary>
        /// The bar above the feed: one entry per author who has something live,
        /// own stories first so the "add" tile sits next to them.
        /// </summary>
        private void HandleStories(ApiCtx ctx)
        {
            if (!_cfg.StoriesEnabled) throw new ApiException(403, "stories-off", "Stories are switched off here");

            List<long> authors = _store.StoryAuthors();
            // own stories first, then friends, then everybody else
            List<long> ordered = new List<long>();
            if (authors.Contains(ctx.User.Id)) ordered.Add(ctx.User.Id);
            for (int i = 0; i < authors.Count; i++)
            {
                long id = authors[i];
                if (id == ctx.User.Id) continue;
                if (_store.AreFriends(ctx.User.Id, id)) ordered.Add(id);
            }
            for (int i = 0; i < authors.Count; i++)
            {
                long id = authors[i];
                if (id == ctx.User.Id || ordered.Contains(id)) continue;
                User u = _store.FindUser(id);
                if (u == null || u.Deleted) continue;
                if (!CanViewProfile(ctx.User, u)) continue;
                ordered.Add(id);
            }

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("groups").BeginArray();
            for (int i = 0; i < ordered.Count; i++)
            {
                User author = _store.FindUser(ordered[i]);
                if (author == null || author.Deleted) continue;
                if (ordered[i] != ctx.User.Id && !CanViewProfile(ctx.User, author)) continue;
                List<Story> list = _store.StoriesOf(author.Id, false);
                if (list.Count == 0) continue;

                bool unseen = false;
                for (int k = 0; k < list.Count; k++)
                    if (list[k].Author != ctx.User.Id && !list[k].Viewers.Contains(ctx.User.Id)) unseen = true;

                b.BeginObject();
                b.Name("user").Raw(UserSummary(author));
                b.Prop("mine", author.Id == ctx.User.Id);
                b.Prop("unseen", unseen);
                b.Prop("count", list.Count);
                b.Name("stories").BeginArray();
                for (int k = 0; k < list.Count; k++) b.Raw(StoryJson(list[k], ctx.User));
                b.EndArray();
                b.EndObject();
            }
            b.EndArray();
            b.Prop("canPost", _cfg.StoryPerDay == 0 || _store.CountStoriesToday(ctx.User.Id) < _cfg.StoryPerDay);
            b.Prop("leftToday", _cfg.StoryPerDay <= 0 ? -1 :
                Math.Max(0, _cfg.StoryPerDay - _store.CountStoriesToday(ctx.User.Id)));
            SendOk(ctx, b);
        }

        private void HandleStory(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            Story s = _store.FindStory(id);
            if (s == null || s.Expires <= DateTime.UtcNow)
                throw new ApiException(404, "no-story", "Story not found or expired");

            User author = _store.FindUser(s.Author);
            if (author == null || author.Deleted) throw new ApiException(404, "no-story", "Story not found");
            if (!CanViewProfile(ctx.User, author)) throw new ApiException(403, "private", "This profile is private");

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("story").Raw(StoryJson(s, ctx.User));
            SendOk(ctx, b);
        }

        /// <summary>The "seen by" list of one's own story.</summary>
        private void HandleStoryViewers(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            Story s = _store.FindStory(id);
            if (s == null) throw new ApiException(404, "no-story", "Story not found");
            if (s.Author != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your story");

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("viewers").BeginArray();
            for (int i = 0; i < s.Viewers.Count && i < 200; i++)
            {
                User u = _store.FindUser(s.Viewers[i]);
                if (u == null || u.Deleted) continue;
                b.Raw(UserSummary(u));
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleStoryAdd(ApiCtx ctx)
        {
            if (!_cfg.StoriesEnabled) throw new ApiException(403, "stories-off", "Stories are switched off here");
            if (_cfg.StoryPerDay > 0 && _store.CountStoriesToday(ctx.User.Id) >= _cfg.StoryPerDay)
                throw new ApiException(429, "slow-down", "That is enough stories for one day");

            string imageData = BodyStr(ctx, "image", "");
            string videoData = BodyStr(ctx, "video", "");
            if (imageData.Length == 0 && videoData.Length == 0)
                throw new ApiException(400, "empty", "A story needs a photo or a clip");
            if (imageData.Length > 0 && videoData.Length > 0)
                throw new ApiException(400, "both", "A story is either a photo or a clip");

            string err;
            string image = "", video = "";
            if (imageData.Length > 0)
            {
                image = _media.SaveDataUrl(imageData, "story", _cfg.MaxStoryKB, out err);
                if (image == null)
                {
                    if (err == "image-too-large")
                        throw new ApiException(400, "too-large", "Photo is larger than " + _cfg.MaxStoryKB + " KB");
                    throw new ApiException(400, err, "Photo could not be saved");
                }
            }
            else
            {
                video = _media.SaveDataUrl(videoData, "storyvideo", _cfg.MaxStoryVideoKB, out err);
                if (video == null)
                {
                    if (err == "image-too-large")
                        throw new ApiException(400, "too-large", "Clip is larger than " + _cfg.MaxStoryVideoKB + " KB");
                    throw new ApiException(400, err, "That clip format is not supported");
                }
            }

            string caption = Util.CleanText(BodyStr(ctx, "caption", ""), _cfg.MaxStoryCaption);
            Story story = _store.AddStory(ctx.User.Id, image, video, caption, _cfg.StoryHours);
            Log.Info("STORY", "@" + ctx.User.Username + " posted story #" + story.Id +
                     (video.Length > 0 ? " (clip)" : " (photo)"));

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("story").Raw(StoryJson(story, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleStorySeen(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Story s = _store.FindStory(id);
            if (s == null || s.Expires <= DateTime.UtcNow)
                throw new ApiException(404, "no-story", "Story not found or expired");
            _store.MarkStorySeen(id, ctx.User.Id);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("ok", true);
            SendOk(ctx, b);
        }

        private void HandleStoryDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Story s = _store.FindStory(id);
            if (s == null) throw new ApiException(404, "no-story", "Story not found");
            if (s.Author != ctx.User.Id && !ctx.User.IsModerator)
                throw new ApiException(403, "not-mine", "That is not your story");

            List<string> urls = _store.RemoveStory(id, s.Author);
            for (int i = 0; i < urls.Count; i++) _media.Delete(urls[i]);
            Log.Info("STORY", "@" + ctx.User.Username + " deleted story #" + id);

            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            b.Prop("id", id);
            SendOk(ctx, b);
        }

        /// <summary>
        /// Housekeeping: deletes expired stories and their files. Wired into the
        /// supervisor loop, so "24 hours" means 24 hours even if nobody opens the app.
        /// Throttled, because the loop ticks every couple of seconds.
        /// </summary>
        public void SweepStories()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastStorySweep).TotalSeconds < 60) return;
            _lastStorySweep = now;

            List<string> urls = _store.PurgeExpiredStories();
            for (int i = 0; i < urls.Count; i++)
            {
                try { _media.Delete(urls[i]); }
                catch (Exception ex) { Log.Warn("STORY", "cannot delete " + urls[i] + ": " + ex.Message); }
            }
            if (urls.Count > 0) Log.Info("STORY", "removed " + (urls.Count / 2) + "+ expired stories");
        }
    }
}
