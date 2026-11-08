using System;
using System.Collections.Generic;
using System.Threading;

namespace Social.Core
{
    /// <summary>
    /// Background simulation: every <c>ai.intervalSec</c> one imported AI account does
    /// one thing — writes a post, comments, likes or (optionally) messages someone.
    ///
    /// Everything is capped per hour and gated by <c>ai.*</c> flags, so a misconfigured
    /// provider can never flood the network. A failure is logged once and backs off
    /// instead of hammering a dead endpoint.
    /// </summary>
    public sealed class AiRunner
    {
        private readonly Config _cfg;
        private readonly SocialStore _store;
        private readonly AiEngine _ai;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _sync = new object();
        private readonly List<DateTime> _posts = new List<DateTime>();
        private readonly List<DateTime> _comments = new List<DateTime>();
        private readonly List<DateTime> _likes = new List<DateTime>();
        private readonly List<DateTime> _messages = new List<DateTime>();
        private Thread _thread;
        private DateTime _nextRun = DateTime.UtcNow;
        private int _consecutiveFailures;
        private string _lastAction = "";
        private DateTime _lastActionAt = DateTime.MinValue;
        private long _actions;
        private long _skipped;

        public AiRunner(Config cfg, SocialStore store, AiEngine ai)
        {
            _cfg = cfg;
            _store = store;
            _ai = ai;
        }

        public string LastAction { get { lock (_sync) { return _lastAction; } } }
        public DateTime LastActionAt { get { lock (_sync) { return _lastActionAt; } } }
        public long Actions { get { lock (_sync) { return _actions; } } }
        public long Skipped { get { lock (_sync) { return _skipped; } } }
        public int ConsecutiveFailures { get { lock (_sync) { return _consecutiveFailures; } } }
        public DateTime NextRunUtc { get { lock (_sync) { return _nextRun; } } }

        public int PostsLastHour { get { lock (_sync) { return Count(_posts, DateTime.UtcNow); } } }
        public int CommentsLastHour { get { lock (_sync) { return Count(_comments, DateTime.UtcNow); } } }
        public int LikesLastHour { get { lock (_sync) { return Count(_likes, DateTime.UtcNow); } } }
        public int MessagesLastHour { get { lock (_sync) { return Count(_messages, DateTime.UtcNow); } } }

        public void Start()
        {
            if (!_cfg.AiEnabled) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "ai-runner";
            _thread.Start();
            Log.Info("AI", "simulation started: " + _ai.ProviderLabel + " every " + _cfg.AiIntervalSec + "s");
        }

        public void Stop()
        {
            try { _stop.Set(); } catch { }
            try { if (_thread != null && _thread.IsAlive) _thread.Join(3000); } catch { }
        }

        /// <summary>Forces one action right now (admin button / CLI). Returns a description.</summary>
        public string StepOnce()
        {
            try
            {
                return Act();
            }
            catch (Exception ex)
            {
                Log.Error("AI", "step failed: " + ex.Message);
                return "error: " + ex.Message;
            }
        }

        private void Loop()
        {
            while (!_stop.WaitOne(5000))
            {
                if (!_cfg.AiEnabled || !_ai.IsReady) continue;
                DateTime now = DateTime.UtcNow;
                DateTime next;
                lock (_sync) { next = _nextRun; }
                if (now < next) continue;

                int interval = _cfg.AiIntervalSec;
                int failures;
                lock (_sync) { failures = _consecutiveFailures; }
                if (failures > 0)
                {
                    // back off: 1x, 2x, 4x ... up to 8x the base interval
                    int factor = Math.Min(8, 1 << Math.Min(failures, 3));
                    interval = _cfg.AiIntervalSec * factor;
                }
                lock (_sync) { _nextRun = DateTime.UtcNow.AddSeconds(interval); }

                try
                {
                    string result = Act();
                    Log.Debug("AI", result);
                }
                catch (Exception ex)
                {
                    lock (_sync) { _consecutiveFailures++; }
                    Log.Warn("AI", "simulation step: " + ex.Message);
                }
            }
            Log.Debug("AI", "simulation stopped");
        }

        private string Act()
        {
            List<User> bots = BotUsers();
            if (bots.Count == 0) { lock (_sync) { _skipped++; } return "no AI accounts"; }
            if (!_ai.IsReady) { lock (_sync) { _skipped++; } return "AI is not ready"; }

            Random rnd;
            lock (_sync) { rnd = new Random(unchecked(Environment.TickCount * 31 + (int)_actions)); }
            User bot = bots[rnd.Next(bots.Count)];

            // choose an action that is allowed and still under its hourly cap
            List<string> options = new List<string>();
            if (_cfg.AiAllowPosts && PostsLastHour < _cfg.AiMaxPostsPerHour) options.Add("post");
            if (_cfg.AiAllowComments && CommentsLastHour < _cfg.AiMaxCommentsPerHour) options.Add("comment");
            if (_cfg.AiAllowLikes && LikesLastHour < _cfg.AiMaxLikesPerHour) options.Add("like");
            if (_cfg.AiAllowMessages && MessagesLastHour < _cfg.AiMaxMessagesPerHour) options.Add("message");

            if (options.Count == 0)
            {
                lock (_sync) { _skipped++; }
                return "hourly caps reached";
            }

            string action = options[rnd.Next(options.Count)];
            string error;
            string result;

            if (action == "post")
            {
                string text = _ai.GeneratePost(bot, out error);
                if (text.Length == 0) { OnFailure(error); return "post failed: " + error; }
                Post post = _store.CreatePost(bot.Id, 0, 0, text, "", "public");
                post.LegacySource = bot.LegacySource;
                _store.MarkUserDirty(bot);
                bot.LastPostAt = DateTime.UtcNow;
                lock (_sync) { _posts.Add(DateTime.UtcNow); _actions++; _consecutiveFailures = 0; }
                result = "@" + bot.Username + " posted: " + Util.SingleLine(text, 90);
            }
            else if (action == "comment")
            {
                List<Post> posts = _store.RecentPosts(40);
                Post target = PickPost(posts, bot, rnd);
                if (target == null) { lock (_sync) { _skipped++; } return "no post to comment on"; }

                string text = _ai.GenerateComment(bot, target, out error);
                if (text.Length == 0) { OnFailure(error); return "comment failed: " + error; }

                Comment c = _store.AddComment(target.Id, bot.Id, text);
                if (c == null) { lock (_sync) { _skipped++; } return "comment target gone"; }
                if (target.Author != bot.Id)
                    _store.Notify(target.Author, bot.Id, "comment", "commented on your post", "#/post/" + target.Id);
                lock (_sync) { _comments.Add(DateTime.UtcNow); _actions++; _consecutiveFailures = 0; }
                result = "@" + bot.Username + " commented on #" + target.Id + ": " + Util.SingleLine(text, 70);
            }
            else if (action == "like")
            {
                List<Post> posts = _store.RecentPosts(60);
                Post target = PickPost(posts, bot, rnd);
                if (target == null || target.Likes.Contains(bot.Id)) { lock (_sync) { _skipped++; } return "nothing to like"; }

                _store.ToggleLike(target.Id, bot.Id);
                if (target.Author != bot.Id)
                    _store.Notify(target.Author, bot.Id, "like", "liked your post", "#/post/" + target.Id);
                lock (_sync) { _likes.Add(DateTime.UtcNow); _actions++; _consecutiveFailures = 0; }
                result = "@" + bot.Username + " liked #" + target.Id;
            }
            else
            {
                User target = PickHuman(bots, rnd);
                if (target == null) { lock (_sync) { _skipped++; } return "no human to message"; }

                string text = _ai.GenerateMessage(bot, target, out error);
                if (text.Length == 0) { OnFailure(error); return "message failed: " + error; }

                _store.CreateMessage(bot.Id, target.Id, text);
                _store.Notify(target.Id, bot.Id, "message", "sent you a message", "#/im/" + bot.Id);
                lock (_sync) { _messages.Add(DateTime.UtcNow); _actions++; _consecutiveFailures = 0; }
                result = "@" + bot.Username + " messaged @" + target.Username;
            }

            lock (_sync)
            {
                _lastAction = result;
                _lastActionAt = DateTime.UtcNow;
            }
            return result;
        }

        private void OnFailure(string error)
        {
            lock (_sync)
            {
                _consecutiveFailures++;
                _skipped++;
            }
            Log.Warn("AI", "provider failure: " + error);
        }

        private List<User> BotUsers()
        {
            List<User> list = new List<User>();
            List<User> all = _store.AllUsers();
            for (int i = 0; i < all.Count; i++)
            {
                User u = all[i];
                if (u.Deleted || u.Banned) continue;
                if (string.IsNullOrEmpty(u.Persona) && u.Role != "ai" && u.Role != "bot") continue;
                list.Add(u);
            }
            return list;
        }

        private Post PickPost(List<Post> posts, User bot, Random rnd)
        {
            if (posts == null || posts.Count == 0) return null;
            bool preferHuman = rnd.Next(100) < _cfg.AiHumanTargetPercent;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Post p = posts[rnd.Next(posts.Count)];
                if (p.Deleted || p.Visibility != "public") continue;
                if (p.Author == bot.Id) continue;
                User author = _store.FindUser(p.Author);
                bool human = author != null && string.IsNullOrEmpty(author.Persona) && author.Role != "ai";
                if (preferHuman && !human) continue;
                if (!preferHuman && human) continue;
                return p;
            }
            // fall back to any post that is not the bot's own
            for (int i = 0; i < posts.Count; i++)
                if (!posts[i].Deleted && posts[i].Author != bot.Id && posts[i].Visibility == "public") return posts[i];
            return null;
        }

        private User PickHuman(List<User> bots, Random rnd)
        {
            List<User> humans = new List<User>();
            List<User> all = _store.AllUsers();
            for (int i = 0; i < all.Count; i++)
            {
                User u = all[i];
                if (u.Deleted || u.Banned) continue;
                if (!string.IsNullOrEmpty(u.Persona)) continue;
                if (u.Role == "ai" || u.Role == "bot") continue;
                humans.Add(u);
            }
            if (humans.Count == 0) return null;
            return humans[rnd.Next(humans.Count)];
        }

        private static int Count(List<DateTime> list, DateTime now)
        {
            DateTime cutoff = now.AddHours(-1);
            int n = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] < cutoff) break;
                n++;
            }
            return n;
        }
    }
}
