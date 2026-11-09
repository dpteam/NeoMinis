using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Social.Core
{
    public sealed class ThreadInfo
    {
        public User Other;
        public Message Last;
        public int Unread;
    }

    /// <summary>
    /// In-memory social graph persisted as small JSON tables. Every mutation happens
    /// under one lock; writes are atomic and debounced by the supervisor.
    /// </summary>
    public sealed class SocialStore
    {
        private readonly object _sync = new object();
        private readonly Config _cfg;
        private readonly List<User> _users = new List<User>();
        private readonly List<Post> _posts = new List<Post>();
        private readonly List<Comment> _comments = new List<Comment>();
        private readonly List<FriendLink> _friends = new List<FriendLink>();
        private readonly List<Message> _messages = new List<Message>();
        private readonly List<Notification> _notifications = new List<Notification>();
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<Report> _reports = new List<Report>();
        private readonly List<Bookmark> _bookmarks = new List<Bookmark>();
        private Meta _meta = new Meta();

        private readonly Dictionary<long, User> _userById = new Dictionary<long, User>();
        private readonly Dictionary<string, User> _userByName = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);

        private bool _dirtyUsers, _dirtyPosts, _dirtyComments, _dirtyFriends, _dirtyMessages;
        private bool _dirtyNotifications, _dirtyGroups, _dirtyReports, _dirtyBookmarks, _dirtyMeta;
        private bool _readOnly;
        private string _saveError = "";
        private DateTime _lastSave = DateTime.MinValue;
        private bool _loaded;

        public SocialStore(Config cfg)
        {
            _cfg = cfg;
            _meta.InstallId = Crypto.RandomHex(8);
        }

        public object Sync { get { return _sync; } }
        public bool IsReadOnly { get { lock (_sync) { return _readOnly; } } }
        public string SaveError { get { lock (_sync) { return _saveError; } } }
        public DateTime LastSaveUtc { get { lock (_sync) { return _lastSave; } } }
        public Meta MetaData { get { lock (_sync) { return _meta; } } }
        public bool IsLoaded { get { lock (_sync) { return _loaded; } } }

        public bool HasDirty
        {
            get
            {
                lock (_sync)
                {
                    return _dirtyUsers || _dirtyPosts || _dirtyComments || _dirtyFriends || _dirtyMessages ||
                           _dirtyNotifications || _dirtyGroups || _dirtyReports || _dirtyBookmarks || _dirtyMeta;
                }
            }
        }

        // ============================================================== load

        public void Load()
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_cfg.DataDir);
                Directory.CreateDirectory(_cfg.MediaDir);
                Directory.CreateDirectory(Path.Combine(_cfg.MediaDir, "avatars"));
                Directory.CreateDirectory(Path.Combine(_cfg.MediaDir, "posts"));
                Directory.CreateDirectory(_cfg.BackupDir);

                _meta = LoadMeta();

                _users.Clear(); _userById.Clear(); _userByName.Clear();
                _users.AddRange(LoadTable("users.json", UserFromDict));
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u == null || u.Id <= 0) continue;
                    _userById[u.Id] = u;
                    if (!string.IsNullOrEmpty(u.Username)) _userByName[u.Username] = u;
                }

                _posts.Clear();
                _posts.AddRange(LoadTable("posts.json", PostFromDict));
                _comments.Clear();
                _comments.AddRange(LoadTable("comments.json", CommentFromDict));
                _friends.Clear();
                _friends.AddRange(LoadTable("friends.json", FriendFromDict));
                _messages.Clear();
                _messages.AddRange(LoadTable("messages.json", MessageFromDict));
                _notifications.Clear();
                _notifications.AddRange(LoadTable("notifications.json", NotificationFromDict));
                _groups.Clear();
                _groups.AddRange(LoadTable("groups.json", GroupFromDict));
                _reports.Clear();
                _reports.AddRange(LoadTable("reports.json", ReportFromDict));
                _bookmarks.Clear();
                _bookmarks.AddRange(LoadTable("bookmarks.json", BookmarkFromDict));

                // sort newest first, keeping the hot queries cheap
                _posts.Sort(delegate(Post a, Post b) { return b.Id.CompareTo(a.Id); });
                _comments.Sort(delegate(Comment a, Comment b) { return b.Id.CompareTo(a.Id); });
                _messages.Sort(delegate(Message a, Message b) { return b.Id.CompareTo(a.Id); });
                _notifications.Sort(delegate(Notification a, Notification b) { return b.Id.CompareTo(a.Id); });

                EnsureCounters();
                _loaded = true;
                _dirtyUsers = _dirtyPosts = _dirtyComments = _dirtyFriends = _dirtyMessages = false;
                _dirtyNotifications = _dirtyGroups = _dirtyReports = _dirtyBookmarks = _dirtyMeta = false;

                Log.Info("DATA", "loaded users=" + _users.Count + " posts=" + _posts.Count +
                                  " comments=" + _comments.Count + " friends=" + _friends.Count +
                                  " messages=" + _messages.Count + " groups=" + _groups.Count +
                                  " reports=" + _reports.Count + " bookmarks=" + _bookmarks.Count);
            }
        }

        private Meta LoadMeta()
        {
            string path = Path.Combine(_cfg.DataDir, "meta.json");
            string text = ReadJson(path);
            if (text == null) return new Meta();
            try
            {
                Dictionary<string, object> d = Json.ParseObject(text, 8);
                Meta m = new Meta();
                m.Version = Json.GetInt(d, "version", 1);
                m.Created = Json.GetDate(d, "created", DateTime.UtcNow);
                m.NextUserId = Json.GetLong(d, "nextUserId", 1);
                m.NextPostId = Json.GetLong(d, "nextPostId", 1);
                m.NextCommentId = Json.GetLong(d, "nextCommentId", 1);
                m.NextMessageId = Json.GetLong(d, "nextMessageId", 1);
                m.NextNotificationId = Json.GetLong(d, "nextNotificationId", 1);
                m.NextGroupId = Json.GetLong(d, "nextGroupId", 1);
                m.NextReportId = Json.GetLong(d, "nextReportId", 1);
                m.TotalRegistrations = Json.GetLong(d, "totalRegistrations", 0);
                m.LastBackup = Json.GetDate(d, "lastBackup", DateTime.MinValue);
                m.InstallId = Json.GetString(d, "installId", "");
                if (string.IsNullOrEmpty(m.InstallId)) m.InstallId = Crypto.RandomHex(8);
                return m;
            }
            catch (Exception ex)
            {
                Log.Warn("DATA", "meta.json is broken (" + ex.Message + "), using defaults");
                return new Meta();
            }
        }

        private void EnsureCounters()
        {
            long maxUser = 0, maxPost = 0, maxComment = 0, maxMessage = 0, maxNotif = 0, maxGroup = 0, maxReport = 0;
            for (int i = 0; i < _users.Count; i++) if (_users[i].Id > maxUser) maxUser = _users[i].Id;
            for (int i = 0; i < _posts.Count; i++) if (_posts[i].Id > maxPost) maxPost = _posts[i].Id;
            for (int i = 0; i < _comments.Count; i++) if (_comments[i].Id > maxComment) maxComment = _comments[i].Id;
            for (int i = 0; i < _messages.Count; i++) if (_messages[i].Id > maxMessage) maxMessage = _messages[i].Id;
            for (int i = 0; i < _notifications.Count; i++) if (_notifications[i].Id > maxNotif) maxNotif = _notifications[i].Id;
            for (int i = 0; i < _groups.Count; i++) if (_groups[i].Id > maxGroup) maxGroup = _groups[i].Id;
            for (int i = 0; i < _reports.Count; i++) if (_reports[i].Id > maxReport) maxReport = _reports[i].Id;

            if (_meta.NextUserId <= maxUser) _meta.NextUserId = maxUser + 1;
            if (_meta.NextPostId <= maxPost) _meta.NextPostId = maxPost + 1;
            if (_meta.NextCommentId <= maxComment) _meta.NextCommentId = maxComment + 1;
            if (_meta.NextMessageId <= maxMessage) _meta.NextMessageId = maxMessage + 1;
            if (_meta.NextNotificationId <= maxNotif) _meta.NextNotificationId = maxNotif + 1;
            if (_meta.NextGroupId <= maxGroup) _meta.NextGroupId = maxGroup + 1;
            if (_meta.NextReportId <= maxReport) _meta.NextReportId = maxReport + 1;
        }

        private string ReadJson(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text == null || text.Trim().Length == 0) return null;
                return text;
            }
            catch (Exception ex)
            {
                Log.Warn("DATA", "cannot read " + Path.GetFileName(path) + ": " + ex.Message);
                return null;
            }
        }

        private List<T> LoadTable<T>(string file, Func<Dictionary<string, object>, T> map)
        {
            string path = Path.Combine(_cfg.DataDir, file);
            string text = ReadJson(path);

            if (text != null)
            {
                try { return ParseArray(text, map); }
                catch (Exception ex)
                {
                    Log.Error("DATA", file + " is corrupt: " + ex.Message);
                    text = null;
                }
            }

            string bak = path + ".bak";
            if (File.Exists(bak))
            {
                try
                {
                    string backup = File.ReadAllText(bak, Encoding.UTF8);
                    List<T> list = ParseArray(backup, map);
                    Log.Warn("DATA", file + " restored from .bak (" + list.Count + " records)");
                    return list;
                }
                catch (Exception ex) { Log.Error("DATA", file + ".bak is corrupt too: " + ex.Message); }
            }

            if (File.Exists(path))
            {
                JsonFile.Quarantine(path);
                Log.Error("DATA", file + " quarantined; starting with an empty table");
            }
            return new List<T>();
        }

        private static List<T> ParseArray<T>(string text, Func<Dictionary<string, object>, T> map)
        {
            List<T> list = new List<T>();
            object root = Json.Parse(text, 64);
            List<object> arr = root as List<object>;
            if (arr == null) throw new JsonException("root is not an array");
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                if (d == null) continue;
                T item = map(d);
                if (item != null) list.Add(item);
            }
            return list;
        }

        // ============================================================== save

        public void SaveIfDirty()
        {
            SaveAll(false);
        }

        public void SaveNow()
        {
            SaveAll(true);
        }

        public void SaveAll(bool force)
        {
            lock (_sync)
            {
                if (!_loaded) return;
                if (!force && !HasDirtyLocked()) return;
                if (_readOnly && !force) return;

                try
                {
                    if (force || _dirtyMeta) WriteTable("meta.json", MetaToDict(_meta));
                    if (force || _dirtyUsers) WriteTable("users.json", UsersToArray());
                    if (force || _dirtyPosts) WriteTable("posts.json", PostsToArray());
                    if (force || _dirtyComments) WriteTable("comments.json", CommentsToArray());
                    if (force || _dirtyFriends) WriteTable("friends.json", FriendsToArray());
                    if (force || _dirtyMessages) WriteTable("messages.json", MessagesToArray());
                    if (force || _dirtyNotifications) WriteTable("notifications.json", NotificationsToArray());
                    if (force || _dirtyGroups) WriteTable("groups.json", GroupsToArray());
                    if (force || _dirtyReports) WriteTable("reports.json", ReportsToArray());
                    if (force || _dirtyBookmarks) WriteTable("bookmarks.json", BookmarksToArray());

                    _dirtyUsers = _dirtyPosts = _dirtyComments = _dirtyFriends = _dirtyMessages = false;
                    _dirtyNotifications = _dirtyGroups = _dirtyReports = _dirtyMeta = false;
                    _lastSave = DateTime.UtcNow;
                    _saveError = "";
                    if (_readOnly)
                    {
                        _readOnly = false;
                        Log.Info("DATA", "storage recovered, writes are enabled again");
                    }
                }
                catch (Exception ex)
                {
                    _saveError = ex.Message;
                    Log.Error("DATA", "save failed: " + ex.Message);
                    if (_cfg.ReadOnlyOnError && !_readOnly)
                    {
                        _readOnly = true;
                        Log.Error("DATA", "switching to read-only mode until the next successful save");
                    }
                }
            }
        }

        private bool HasDirtyLocked()
        {
            return _dirtyUsers || _dirtyPosts || _dirtyComments || _dirtyFriends || _dirtyMessages ||
                   _dirtyNotifications || _dirtyGroups || _dirtyReports || _dirtyBookmarks || _dirtyMeta;
        }

        private void WriteTable(string file, object value)
        {
            string path = Path.Combine(_cfg.DataDir, file);
            JsonFile.WriteAtomic(path, Json.Stringify(value));
        }

        public string CreateBackup()
        {
            SaveAll(true);
            string dir = JsonFile.Snapshot(_cfg.DataDir, _cfg.BackupDir, _cfg.BackupKeep);
            lock (_sync)
            {
                if (dir != null)
                {
                    _meta.LastBackup = DateTime.UtcNow;
                    _dirtyMeta = true;
                }
            }
            return dir;
        }

        public void ResetReadOnly()
        {
            lock (_sync)
            {
                _readOnly = false;
                _saveError = "";
            }
        }

        private void EnsureWritable()
        {
            if (_readOnly) throw new StoreReadOnlyException("storage is read-only: " + _saveError);
        }

        private void MarkUsers() { _dirtyUsers = true; _dirtyMeta = true; }
        private void MarkPosts() { _dirtyPosts = true; _dirtyMeta = true; }
        private void MarkComments() { _dirtyComments = true; }
        private void MarkFriends() { _dirtyFriends = true; }
        private void MarkMessages() { _dirtyMessages = true; }
        private void MarkNotifications() { _dirtyNotifications = true; }
        private void MarkGroups() { _dirtyGroups = true; }
        private void MarkReports() { _dirtyReports = true; }
        private void MarkBookmarks() { _dirtyBookmarks = true; }

        // ============================================================== users

        public User FindUser(long id)
        {
            lock (_sync)
            {
                User u;
                return _userById.TryGetValue(id, out u) ? u : null;
            }
        }

        public User FindUser(string username)
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(username)) return null;
                User u;
                return _userByName.TryGetValue(username.Trim(), out u) ? u : null;
            }
        }

        public List<User> AllUsers()
        {
            lock (_sync) { return new List<User>(_users); }
        }

        public bool UsernameExists(string username)
        {
            return FindUser(username) != null;
        }

        public User CreateUser(string username, string displayName, string verifier, string recoveryCode,
                               string role, string ipHash)
        {
            lock (_sync)
            {
                EnsureWritable();
                if (FindUser(username) != null) return null;

                User u = new User();
                u.Id = _meta.NextUserId++;
                u.Username = username;
                u.DisplayName = Util.DisplayName(displayName, username, _cfg.MaxNameLength);
                u.Salt = Crypto.RandomHex(Crypto.SaltBytes);
                u.Verifier = Crypto.HashVerifier(verifier, u.Salt, _cfg.KdfIterations);
                u.RecoverySalt = Crypto.RandomHex(Crypto.SaltBytes);
                u.Recovery = Crypto.HashVerifier(recoveryCode, u.RecoverySalt, _cfg.KdfIterations);
                u.Role = string.IsNullOrEmpty(role) ? "user" : role;
                u.Created = DateTime.UtcNow;
                u.LastSeen = DateTime.UtcNow;
                u.LastLogin = DateTime.UtcNow;
                u.Theme = _cfg.DefaultTheme;
                u.Lang = _cfg.DefaultLanguage;
                u.Sound = _cfg.DefaultSound;
                u.Privacy = "public";
                u.IpHash = ipHash ?? "";
                u.LoginCount = 1;

                _users.Add(u);
                _userById[u.Id] = u;
                _userByName[u.Username] = u;
                _meta.TotalRegistrations++;
                MarkUsers();
                Log.Info("AUTH", "user #" + u.Id + " @" + u.Username + " registered from " + ipHash);
                return u;
            }
        }

        public void TouchUser(User u)
        {
            if (u == null) return;
            lock (_sync)
            {
                u.LastSeen = DateTime.UtcNow;
            }
        }

        public void MarkUserDirty(User u)
        {
            lock (_sync)
            {
                if (u != null) MarkUsers();
            }
        }

        public bool SetVerifier(User u, string verifier)
        {
            lock (_sync)
            {
                EnsureWritable();
                if (u == null || string.IsNullOrEmpty(verifier)) return false;
                u.Salt = Crypto.RandomHex(Crypto.SaltBytes);
                u.Verifier = Crypto.HashVerifier(verifier, u.Salt, _cfg.KdfIterations);
                MarkUsers();
                return true;
            }
        }

        public bool SetRecovery(User u, string recoveryCode)
        {
            lock (_sync)
            {
                EnsureWritable();
                if (u == null || string.IsNullOrEmpty(recoveryCode)) return false;
                u.RecoverySalt = Crypto.RandomHex(Crypto.SaltBytes);
                u.Recovery = Crypto.HashVerifier(recoveryCode, u.RecoverySalt, _cfg.KdfIterations);
                MarkUsers();
                return true;
            }
        }

        public bool CheckVerifier(User u, string verifier)
        {
            if (u == null || string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(u.Verifier)) return false;
            string hash = Crypto.HashVerifier(verifier, u.Salt, _cfg.KdfIterations);
            return Crypto.FixedEquals(hash, u.Verifier);
        }

        public bool CheckRecovery(User u, string code)
        {
            if (u == null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(u.Recovery)) return false;
            string hash = Crypto.HashVerifier(code.Trim().ToUpperInvariant(), u.RecoverySalt, _cfg.KdfIterations);
            return Crypto.FixedEquals(hash, u.Recovery);
        }

        public bool IsAdmin(long userId)
        {
            User u = FindUser(userId);
            return u != null && u.Role == "admin" && !u.Deleted;
        }

        public int CountUsers()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _users.Count; i++) if (!_users[i].Deleted) n++;
                return n;
            }
        }

        public int CountAdmins()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _users.Count; i++)
                    if (!_users[i].Deleted && _users[i].Role == "admin") n++;
                return n;
            }
        }

        public List<User> RecentUsers(int limit)
        {
            List<User> list = new List<User>(_users);
            list.Sort(delegate(User a, User b) { return b.Created.CompareTo(a.Created); });
            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        public int CountOnline(int windowSec)
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-windowSec);
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u.Deleted || u.Banned || u.HideOnline) continue;
                    if (u.LastSeen >= cutoff) n++;
                }
                return n;
            }
        }

        public List<User> OnlineUsers(int windowSec, int limit)
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-windowSec);
            List<User> list = new List<User>();
            lock (_sync)
            {
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u.Deleted || u.Banned || u.HideOnline || u.LastSeen < cutoff) continue;
                    list.Add(u);
                }
                list.Sort(delegate(User a, User b) { return b.LastSeen.CompareTo(a.LastSeen); });
                if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            }
            return list;
        }

        public List<User> SearchUsers(string query, int limit)
        {
            List<User> list = new List<User>();
            if (string.IsNullOrEmpty(query)) return list;
            string q = query.Trim().ToLowerInvariant();
            lock (_sync)
            {
                for (int i = 0; i < _users.Count && list.Count < limit; i++)
                {
                    User u = _users[i];
                    if (u.Deleted) continue;
                    if (u.Username.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0 ||
                        u.DisplayName.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0)
                        list.Add(u);
                }
            }
            list.Sort(delegate(User a, User b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        // ============================================================== posts

        public Post CreatePost(long author, long wall, long group, string body, string image, string visibility)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = new Post();
                p.Id = _meta.NextPostId++;
                p.Author = author;
                p.Wall = wall;
                p.Group = group;
                p.Body = body ?? "";
                p.Image = image ?? "";
                p.Visibility = visibility ?? "public";
                p.Created = DateTime.UtcNow;
                _posts.Insert(0, p);
                MarkPosts();
                return p;
            }
        }

        public Post FindPost(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++) if (_posts[i].Id == id) return _posts[i];
                return null;
            }
        }

        public bool DeletePost(long id, bool hard)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null) return false;
                if (hard)
                {
                    _posts.Remove(p);
                    for (int i = _comments.Count - 1; i >= 0; i--) if (_comments[i].Post == id) _comments.RemoveAt(i);
                    MarkComments();
                }
                else
                {
                    p.Deleted = true;
                    p.Body = "";
                    p.Image = "";
                }
                MarkPosts();
                // nobody can see the post any more, so saved copies go too
                bool marksChanged = false;
                for (int b = _bookmarks.Count - 1; b >= 0; b--)
                    if (_bookmarks[b].Post == id) { _bookmarks.RemoveAt(b); marksChanged = true; }
                if (marksChanged) MarkBookmarks();
                return true;
            }
        }

        public void UpdatePost(long id, string body, string image, string video, string visibility)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null || p.Deleted) return;
                p.Body = body ?? "";
                p.Image = image ?? "";
                p.Video = video ?? "";
                p.Visibility = visibility ?? "public";
                p.Edited = DateTime.UtcNow;
                MarkPosts();
            }
        }

        /// <summary>Pins a post; only one post per wall or group stays pinned.</summary>
        public bool PinPost(long id, bool pinned)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null || p.Deleted) return false;
                if (pinned)
                {
                    for (int i = 0; i < _posts.Count; i++)
                    {
                        Post q = _posts[i];
                        if (q == p || q.Deleted) continue;
                        if (q.Group != p.Group || q.Wall != p.Wall) continue;
                        q.Pinned = false;
                    }
                }
                p.Pinned = pinned;
                MarkPosts();
                return true;
            }
        }

        /// <summary>Posts with an image, newest first, for the profile gallery.</summary>
        public List<Post> PhotoPosts(long userId, long before, int limit)
        {
            List<Post> result = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Author != userId || string.IsNullOrEmpty(p.Image)) continue;
                    if (before > 0 && p.Id >= before) continue;
                    result.Add(p);
                }
            }
            return result;
        }

        public bool ToggleLike(long postId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(postId);
                if (p == null || p.Deleted) return false;
                if (p.Likes.Contains(userId)) p.Likes.Remove(userId);
                else p.Likes.Add(userId);
                MarkPosts();
                return p.Likes.Contains(userId);
            }
        }

        public int LikeCount(Post p)
        {
            lock (_sync) { return p == null ? 0 : p.Likes.Count; }
        }

        public Comment AddComment(long postId, long author, string body)
        {            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(postId);
                if (p == null || p.Deleted) return null;
                Comment c = new Comment();
                c.Id = _meta.NextCommentId++;
                c.Post = postId;
                c.Author = author;
                c.Body = body ?? "";
                c.Created = DateTime.UtcNow;
                _comments.Insert(0, c);
                p.Comments++;
                MarkComments();
                MarkPosts();
                return c;
            }
        }

        public Comment FindComment(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _comments.Count; i++) if (_comments[i].Id == id) return _comments[i];
                return null;
            }
        }

        public bool DeleteComment(long id)
        {            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _comments.Count; i++)
                {
                    if (_comments[i].Id != id) continue;
                    Comment c = _comments[i];
                    if (!c.Deleted)
                    {
                        c.Deleted = true;
                        c.Body = "";
                        Post p = FindPost(c.Post);
                        if (p != null && p.Comments > 0) p.Comments--;
                        MarkPosts();
                    }
                    MarkComments();
                    return true;
                }
                return false;
            }
        }

        public List<Comment> CommentsFor(long postId, long before, int limit)
        {
            List<Comment> list = new List<Comment>();
            lock (_sync)
            {
                for (int i = 0; i < _comments.Count && list.Count < limit; i++)
                {
                    Comment c = _comments[i];
                    if (c.Post != postId || c.Deleted) continue;
                    if (before > 0 && c.Id >= before) continue;
                    list.Add(c);
                }
            }
            return list;
        }

        public int CountComments(long postId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _comments.Count; i++) if (_comments[i].Post == postId && !_comments[i].Deleted) n++;
                return n;
            }
        }

        public bool VisibleTo(long viewerId, Post p)
        {
            if (p == null || p.Deleted) return false;
            if (p.Visibility == "public") return true;
            if (viewerId == p.Author) return true;
            if (p.Visibility == "private") return false;
            // friends-only: author, wall owner and their friends
            if (viewerId > 0)
            {
                if (p.Wall > 0 && viewerId == p.Wall) return true;
                if (AreFriends(viewerId, p.Author)) return true;
                if (p.Wall > 0 && AreFriends(viewerId, p.Wall)) return true;
            }
            return false;
        }

        public List<Post> Feed(long userId, string tab, long before, int limit)
        {
            List<Post> result = new List<Post>();
            HashSet<long> friends = null;
            if (tab == "home" || tab == "friends") friends = new HashSet<long>(FriendIds(userId));

            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Group != 0) continue;
                    if (before > 0 && p.Id >= before) continue;

                    if (tab == "global")
                    {
                        if (p.Visibility != "public") continue;
                    }
                    else if (tab == "friends")
                    {
                        if (!friends.Contains(p.Author)) continue;
                        if (!VisibleTo(userId, p)) continue;
                    }
                    else // home
                    {
                        bool relevant = p.Author == userId || p.Wall == userId || friends.Contains(p.Author);
                        if (!relevant) continue;
                        if (!VisibleTo(userId, p)) continue;
                    }
                    result.Add(p);
                }
            }
            return result;
        }

        public List<Post> Wall(long userId, long viewerId, long before, int limit)
        {
            List<Post> result = new List<Post>();
            lock (_sync)
            {
                // pinned posts come first on the profile wall, page one only
                if (before <= 0)
                {
                    for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                    {
                        Post p = _posts[i];
                        if (p.Deleted || p.Group != 0 || !p.Pinned) continue;
                        if (!(p.Wall == userId || (p.Wall == 0 && p.Author == userId))) continue;
                        if (!VisibleTo(viewerId, p)) continue;
                        result.Add(p);
                    }
                }
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Group != 0 || p.Pinned) continue;
                    if (!(p.Wall == userId || (p.Wall == 0 && p.Author == userId))) continue;
                    if (before > 0 && p.Id >= before) continue;
                    if (!VisibleTo(viewerId, p)) continue;
                    result.Add(p);
                }
            }
            return result;
        }

        public List<Post> GroupFeed(long groupId, long viewerId, long before, int limit)
        {
            List<Post> result = new List<Post>();
            lock (_sync)
            {
                if (before <= 0)
                {
                    for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                    {
                        Post p = _posts[i];
                        if (p.Deleted || p.Group != groupId || !p.Pinned) continue;
                        if (!VisibleTo(viewerId, p)) continue;
                        result.Add(p);
                    }
                }
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Group != groupId || p.Pinned) continue;
                    if (before > 0 && p.Id >= before) continue;
                    if (!VisibleTo(viewerId, p)) continue;
                    result.Add(p);
                }
            }
            return result;
        }

        public List<Post> SearchPosts(string query, int limit)
        {
            List<Post> result = new List<Post>();
            if (string.IsNullOrEmpty(query)) return result;
            string q = query.Trim().ToLowerInvariant();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Group != 0 || p.Visibility != "public") continue;
                    if (p.Body.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) result.Add(p);
                }
            }
            return result;
        }

        public int CountPosts(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (!p.Deleted && p.Author == userId) n++;
                }
                return n;
            }
        }

        public int CountAllPosts()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++) if (!_posts[i].Deleted) n++;
                return n;
            }
        }

        // ============================================================ friends

        public FriendLink Friendship(long a, long b)
        {
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    FriendLink f = _friends[i];
                    if ((f.From == a && f.To == b) || (f.From == b && f.To == a)) return f;
                }
                return null;
            }
        }

        public bool AreFriends(long a, long b)
        {
            if (a <= 0 || b <= 0 || a == b) return false;
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    FriendLink f = _friends[i];
                    if (f.Status != "accepted") continue;
                    if ((f.From == a && f.To == b) || (f.From == b && f.To == a)) return true;
                }
                return false;
            }
        }

        public List<long> FriendIds(long userId)
        {
            List<long> list = new List<long>();
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    FriendLink f = _friends[i];
                    if (f.Status != "accepted") continue;
                    if (f.From == userId) list.Add(f.To);
                    else if (f.To == userId) list.Add(f.From);
                }
            }
            return list;
        }

        public List<User> FriendsOf(long userId)
        {
            List<long> ids = FriendIds(userId);
            List<User> list = new List<User>();
            lock (_sync)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    User u;
                    if (_userById.TryGetValue(ids[i], out u) && !u.Deleted) list.Add(u);
                }
            }
            list.Sort(delegate(User a, User b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public string SendFriendRequest(long from, long to)
        {
            if (from == to) return "self";
            lock (_sync)
            {
                EnsureWritable();
                User target = FindUser(to);
                if (target == null || target.Deleted) return "notfound";
                FriendLink existing = Friendship(from, to);
                if (existing != null)
                {
                    if (existing.Status == "accepted") return "already";
                    if (existing.Status == "blocked") return "blocked";
                    if (existing.Status == "pending")
                    {
                        if (existing.From == from) return "pending";
                        existing.Status = "accepted";
                        existing.Updated = DateTime.UtcNow;
                        MarkFriends();
                        Notify(existing.From, to, "friend", "accepted your friend request", "#/friends");
                        return "accepted";
                    }
                    existing.From = from;
                    existing.To = to;
                    existing.Status = "pending";
                    existing.Updated = DateTime.UtcNow;
                    MarkFriends();
                    Notify(to, from, "friend", "sent you a friend request", "#/friends");
                    return "pending";
                }
                FriendLink link = new FriendLink();
                link.From = from;
                link.To = to;
                link.Status = "pending";
                link.Created = DateTime.UtcNow;
                link.Updated = DateTime.UtcNow;
                _friends.Add(link);
                MarkFriends();
                Notify(to, from, "friend", "sent you a friend request", "#/friends");
                return "pending";
            }
        }

        public bool AcceptFriend(long requester, long addressee)
        {
            lock (_sync)
            {
                EnsureWritable();
                FriendLink f = Friendship(requester, addressee);
                if (f == null || f.Status != "pending" || f.From != requester) return false;
                f.Status = "accepted";
                f.Updated = DateTime.UtcNow;
                MarkFriends();
                Notify(requester, addressee, "friend", "accepted your friend request", "#/friends");
                return true;
            }
        }

        public bool DeclineFriend(long requester, long addressee)
        {
            lock (_sync)
            {
                EnsureWritable();
                FriendLink f = Friendship(requester, addressee);
                if (f == null || f.Status != "pending" || f.From != requester) return false;
                f.Status = "declined";
                f.Updated = DateTime.UtcNow;
                MarkFriends();
                return true;
            }
        }

        public bool RemoveFriend(long a, long b)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _friends.Count; i++)
                {
                    FriendLink f = _friends[i];
                    if ((f.From == a && f.To == b) || (f.From == b && f.To == a))
                    {
                        _friends.RemoveAt(i);
                        MarkFriends();
                        return true;
                    }
                }
                return false;
            }
        }

        public List<FriendLink> PendingRequests(long userId)
        {
            List<FriendLink> list = new List<FriendLink>();
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                    if (_friends[i].To == userId && _friends[i].Status == "pending") list.Add(_friends[i]);
            }
            return list;
        }

        public List<FriendLink> OutgoingRequests(long userId)
        {
            List<FriendLink> list = new List<FriendLink>();
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                    if (_friends[i].From == userId && _friends[i].Status == "pending") list.Add(_friends[i]);
            }
            return list;
        }

        public bool BlockUser(long blocker, long blocked)
        {
            if (blocker == blocked) return false;
            lock (_sync)
            {
                EnsureWritable();
                FriendLink existing = Friendship(blocker, blocked);
                if (existing != null)
                {
                    existing.From = blocker;
                    existing.To = blocked;
                    existing.Status = "blocked";
                    existing.Updated = DateTime.UtcNow;
                }
                else
                {
                    FriendLink f = new FriendLink();
                    f.From = blocker;
                    f.To = blocked;
                    f.Status = "blocked";
                    f.Created = DateTime.UtcNow;
                    f.Updated = DateTime.UtcNow;
                    _friends.Add(f);
                }
                MarkFriends();
                return true;
            }
        }

        public bool IsBlocked(long blocker, long blocked)
        {
            lock (_sync)
            {
                for (int i = 0; i < _friends.Count; i++)
                {
                    FriendLink f = _friends[i];
                    if (f.Status == "blocked" && f.From == blocker && f.To == blocked) return true;
                }
                return false;
            }
        }

        // =========================================================== messages

        public Message CreateMessage(long from, long to, string body)
        {
            return CreateMessage(from, to, body, "", 0);
        }

        public Message CreateMessage(long from, long to, string body, string image, long replyTo)
        {
            lock (_sync)
            {
                EnsureWritable();
                Message m = new Message();
                m.Id = _meta.NextMessageId++;
                m.From = from;
                m.To = to;
                m.Body = body ?? "";
                m.Image = image ?? "";
                m.ReplyTo = replyTo;
                m.Created = DateTime.UtcNow;
                _messages.Insert(0, m);
                MarkMessages();
                return m;
            }
        }

        public Message FindMessage(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count; i++) if (_messages[i].Id == id) return _messages[i];
                return null;
            }
        }

        /// <summary>Messages of a conversation with an id greater than <paramref name="after"/>.</summary>
        public List<Message> MessagesAfter(long viewer, long other, long after, int limit)
        {
            List<Message> list = new List<Message>();
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count && list.Count < limit; i++)
                {
                    Message m = _messages[i];
                    if (m.Id <= after) continue;
                    if (!((m.From == viewer && m.To == other) || (m.From == other && m.To == viewer))) continue;
                    if (m.From == viewer && m.DeletedByFrom) continue;
                    if (m.To == viewer && m.DeletedByTo) continue;
                    list.Add(m);
                }
            }
            return list;
        }

        /// <summary>Newest message id of a conversation, 0 when empty.</summary>
        public long LastMessageId(long viewer, long other)
        {
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (!((m.From == viewer && m.To == other) || (m.From == other && m.To == viewer))) continue;
                    if (m.From == viewer && m.DeletedByFrom) continue;
                    if (m.To == viewer && m.DeletedByTo) continue;
                    return m.Id;
                }
                return 0;
            }
        }

        /// <summary>True when the newest message the viewer sent has been read.</summary>
        public bool LastMineRead(long viewer, long other)
        {
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.From != viewer || m.To != other || m.DeletedByFrom) continue;
                    return m.Read;
                }
                return false;
            }
        }

        /// <summary>Conversation as seen by <paramref name="viewer"/> (deletions are per side).</summary>
        public List<Message> Conversation(long viewer, long other, long before, int limit)
        {
            List<Message> list = new List<Message>();
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count && list.Count < limit; i++)
                {
                    Message m = _messages[i];
                    if (!((m.From == viewer && m.To == other) || (m.From == other && m.To == viewer))) continue;
                    if (m.From == viewer && m.DeletedByFrom) continue;
                    if (m.To == viewer && m.DeletedByTo) continue;
                    if (before > 0 && m.Id >= before) continue;
                    list.Add(m);
                }
            }
            return list;
        }

        public List<ThreadInfo> ThreadsFor(long userId)
        {
            List<ThreadInfo> list = new List<ThreadInfo>();
            lock (_sync)
            {
                Dictionary<long, ThreadInfo> byOther = new Dictionary<long, ThreadInfo>();
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.From != userId && m.To != userId) continue;
                    if (m.From == userId && m.DeletedByFrom) continue;
                    if (m.To == userId && m.DeletedByTo) continue;
                    long other = m.From == userId ? m.To : m.From;
                    ThreadInfo t;
                    if (!byOther.TryGetValue(other, out t))
                    {
                        User u = FindUser(other);
                        if (u == null || u.Deleted) continue;
                        t = new ThreadInfo();
                        t.Other = u;
                        t.Last = m;
                        t.Unread = 0;
                        byOther[other] = t;
                        list.Add(t);
                    }
                    if (m.To == userId && !m.Read) t.Unread++;
                }
            }
            list.Sort(delegate(ThreadInfo a, ThreadInfo b) { return b.Last.Created.CompareTo(a.Last.Created); });
            return list;
        }

        public int UnreadMessages(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.To == userId && !m.Read && !m.DeletedByTo) n++;
                }
                return n;
            }
        }

        public void MarkThreadRead(long userId, long other)
        {
            lock (_sync)
            {
                bool changed = false;
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.To == userId && m.From == other && !m.Read)
                    {
                        m.Read = true;
                        changed = true;
                    }
                }
                if (changed) MarkMessages();
            }
        }

        public bool DeleteMessage(long id, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.Id != id) continue;
                    if (m.From == userId) m.DeletedByFrom = true;
                    else if (m.To == userId) m.DeletedByTo = true;
                    else return false;
                    MarkMessages();
                    return true;
                }
                return false;
            }
        }

        // ====================================================== notifications

        public Notification Notify(long userId, long actorId, string type, string text, string link)
        {
            lock (_sync)
            {
                if (userId <= 0) return null;
                if (userId == actorId) return null;
                if (IsNotifyMuted(FindUser(userId), type)) return null;
                Notification n = new Notification();
                n.Id = _meta.NextNotificationId++;
                n.User = userId;
                n.Actor = actorId;
                n.Type = type ?? "system";
                n.Text = text ?? "";
                n.Link = link ?? "";
                n.Created = DateTime.UtcNow;
                _notifications.Insert(0, n);

                // keep the per-user history bounded
                int count = 0;
                for (int i = 0; i < _notifications.Count; i++)
                {
                    if (_notifications[i].User != userId) continue;
                    count++;
                    if (count > _cfg.NotificationsPerUser)
                    {
                        _notifications.RemoveAt(i);
                        i--;
                    }
                }
                MarkNotifications();
                return n;
            }
        }

        public List<Notification> NotificationsFor(long userId, long before, int limit)
        {
            List<Notification> list = new List<Notification>();
            lock (_sync)
            {
                for (int i = 0; i < _notifications.Count && list.Count < limit; i++)
                {
                    Notification n = _notifications[i];
                    if (n.User != userId) continue;
                    if (before > 0 && n.Id >= before) continue;
                    list.Add(n);
                }
            }
            return list;
        }

        public bool IsNotifyMuted(User u, string type)
        {
            if (u == null || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(u.NotifyMute)) return false;
            // admin broadcasts and system messages always get through
            if (type == "admin" || type == "system") return false;
            string[] parts = u.NotifyMute.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), type, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Newest notification id for a user, 0 when there is none.</summary>
        public long LatestNotificationId(long userId)
        {
            lock (_sync)
            {
                for (int i = 0; i < _notifications.Count; i++)
                    if (_notifications[i].User == userId) return _notifications[i].Id;
                return 0;
            }
        }

        public int UnreadNotifications(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _notifications.Count; i++)
                    if (_notifications[i].User == userId && !_notifications[i].Read) n++;
                return n;
            }
        }

        public void MarkNotifications(long userId, long id, bool all)
        {
            lock (_sync)
            {
                bool changed = false;
                for (int i = 0; i < _notifications.Count; i++)
                {
                    Notification n = _notifications[i];
                    if (n.User != userId) continue;
                    if (!all && n.Id != id) continue;
                    if (!n.Read) { n.Read = true; changed = true; }
                }
                if (changed) MarkNotifications();
            }
        }

        // ============================================================= groups

        public Group CreateGroup(long owner, string name, string description, string visibility)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = new Group();
                g.Id = _meta.NextGroupId++;
                g.Name = Util.SingleLine(name, 80);
                g.Description = Util.CleanText(description, 2000);
                g.Owner = owner;
                g.Visibility = visibility == "private" ? "private" : "public";
                g.Created = DateTime.UtcNow;
                g.Slug = Util.Slug(g.Name, 40);
                if (g.Slug.Length == 0) g.Slug = "group";
                g.Slug = g.Slug + "-" + g.Id.ToString(CultureInfo.InvariantCulture);
                g.Members.Add(owner);
                _groups.Add(g);
                MarkGroups();
                return g;
            }
        }

        public Group FindGroup(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _groups.Count; i++) if (_groups[i].Id == id && !_groups[i].Deleted) return _groups[i];
                return null;
            }
        }

        public List<Group> GroupsFor(long userId)
        {
            List<Group> list = new List<Group>();
            lock (_sync)
            {
                for (int i = 0; i < _groups.Count; i++)
                {
                    Group g = _groups[i];
                    if (g.Deleted) continue;
                    if (g.Visibility == "public" || g.Members.Contains(userId) || g.Owner == userId) list.Add(g);
                }
            }
            list.Sort(delegate(Group a, Group b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public bool JoinGroup(long groupId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null) return false;
                if (!g.Members.Contains(userId))
                {
                    g.Members.Add(userId);
                    MarkGroups();
                }
                return true;
            }
        }

        public bool LeaveGroup(long groupId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null) return false;
                if (g.Owner == userId) return false;
                if (g.Members.Remove(userId)) MarkGroups();
                return true;
            }
        }

        public int CountGroups()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _groups.Count; i++) if (!_groups[i].Deleted) n++;
                return n;
            }
        }

        public bool UpdateGroup(long id, string name, string description, string visibility)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(id);
                if (g == null) return false;
                if (!string.IsNullOrEmpty(name))
                {
                    g.Name = Util.SingleLine(name, 80);
                    g.Slug = Util.Slug(g.Name, 40);
                    if (g.Slug.Length == 0) g.Slug = "group";
                    g.Slug = g.Slug + "-" + g.Id.ToString(CultureInfo.InvariantCulture);
                }
                g.Description = Util.CleanText(description, 2000);
                g.Visibility = visibility == "private" ? "private" : "public";
                MarkGroups();
                return true;
            }
        }

        public bool SetGroupAvatar(long id, string url)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(id);
                if (g == null) return false;
                g.Avatar = url ?? "";
                MarkGroups();
                return true;
            }
        }

        public bool RemoveGroupMember(long groupId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null || g.Owner == userId) return false;
                if (!g.Members.Remove(userId)) return false;
                MarkGroups();
                return true;
            }
        }

        // ============================================================== tags

        /// <summary>Public posts containing #tag, newest first.</summary>
        public List<Post> PostsByTag(string tag, long viewerId, long before, int limit)
        {
            List<Post> result = new List<Post>();
            if (string.IsNullOrEmpty(tag)) return result;
            string needle = "#" + tag.ToLowerInvariant();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || string.IsNullOrEmpty(p.Body)) continue;
                    if (before > 0 && p.Id >= before) continue;
                    if (!VisibleTo(viewerId, p)) continue;
                    if (p.Body.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                    // exact tag, not a prefix of a longer word
                    List<string> tags = Util.Hashtags(p.Body);
                    if (!tags.Contains(tag.ToLowerInvariant())) continue;
                    result.Add(p);
                }
            }
            return result;
        }

        public List<KeyValuePair<string, int>> TopTags(int limit)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Visibility != "public" || string.IsNullOrEmpty(p.Body)) continue;
                    List<string> tags = Util.Hashtags(p.Body);
                    for (int t = 0; t < tags.Count; t++)
                    {
                        int n;
                        counts.TryGetValue(tags[t], out n);
                        counts[tags[t]] = n + 1;
                    }
                }
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(counts);
            list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        // ============================================================ reports

        public Report CreateReport(string type, long target, long reporter, string reason)
        {
            lock (_sync)
            {
                EnsureWritable();
                Report r = new Report();
                r.Id = _meta.NextReportId++;
                r.Type = type;
                r.Target = target;
                r.Reporter = reporter;
                r.Reason = reason ?? "";
                r.Created = DateTime.UtcNow;
                r.Status = "open";
                _reports.Insert(0, r);
                MarkReports();
                return r;
            }
        }

        public List<Report> Reports(string status, int limit)
        {
            List<Report> list = new List<Report>();
            lock (_sync)
            {
                for (int i = 0; i < _reports.Count && list.Count < limit; i++)
                {
                    if (!string.IsNullOrEmpty(status) && _reports[i].Status != status) continue;
                    list.Add(_reports[i]);
                }
            }
            return list;
        }

        public bool CloseReport(long id, long adminId, string note)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _reports.Count; i++)
                {
                    if (_reports[i].Id != id) continue;
                    _reports[i].Status = "closed";
                    _reports[i].Note = note ?? "";
                    _reports[i].HandledBy = adminId;
                    _reports[i].HandledAt = DateTime.UtcNow;
                    MarkReports();
                    return true;
                }
                return false;
            }
        }

        // ============================================================= admin

        public bool SetRole(long userId, string role)
        {
            lock (_sync)
            {
                EnsureWritable();
                User u = FindUser(userId);
                if (u == null) return false;
                if (role != "admin" && role != "moderator" && role != "user") return false;
                u.Role = role;
                MarkUsers();
                return true;
            }
        }

        public bool SetBanned(long userId, bool banned, string reason, DateTime until)
        {
            lock (_sync)
            {
                EnsureWritable();
                User u = FindUser(userId);
                if (u == null) return false;
                u.Banned = banned;
                u.BanReason = banned ? (reason ?? "") : "";
                u.BannedUntil = banned ? until : DateTime.MinValue;
                MarkUsers();
                return true;
            }
        }

        public bool SoftDeleteUser(long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                User u = FindUser(userId);
                if (u == null) return false;
                u.Deleted = true;
                u.Banned = true;
                u.BanReason = "deleted";
                MarkUsers();
                return true;
            }
        }

        /// <summary>
        /// Removes an account and everything it produced (posts, comments, messages,
        /// notifications, likes, bookmarks, friendships). Used by the CLI purge and by
        /// "delete" in the admin board when a hard removal is requested.
        /// </summary>
        public int HardDeleteUser(long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                User u = FindUser(userId);
                if (u == null) return 0;

                int removed = 0;

                // posts authored by the user
                for (int i = _posts.Count - 1; i >= 0; i--)
                {
                    if (_posts[i].Author != userId && _posts[i].Wall != userId) continue;
                    long postId = _posts[i].Id;
                    _posts.RemoveAt(i);
                    removed++;
                    // comments of that post and likes on it
                    for (int c = _comments.Count - 1; c >= 0; c--)
                        if (_comments[c].Post == postId) _comments.RemoveAt(c);
                    for (int b = _bookmarks.Count - 1; b >= 0; b--)
                        if (_bookmarks[b].Post == postId) _bookmarks.RemoveAt(b);
                }

                // comments elsewhere
                for (int c = _comments.Count - 1; c >= 0; c--)
                    if (_comments[c].Author == userId) _comments.RemoveAt(c);

                // likes given
                for (int p = 0; p < _posts.Count; p++)
                {
                    Post post = _posts[p];
                    if (post.Likes.Remove(userId)) removed++;
                }

                // private messages either way
                for (int m = _messages.Count - 1; m >= 0; m--)
                    if (_messages[m].From == userId || _messages[m].To == userId) _messages.RemoveAt(m);

                // notifications for and about the user
                for (int n = _notifications.Count - 1; n >= 0; n--)
                    if (_notifications[n].User == userId || _notifications[n].Actor == userId) _notifications.RemoveAt(n);

                // friendships and blocks
                for (int f = _friends.Count - 1; f >= 0; f--)
                    if (_friends[f].From == userId || _friends[f].To == userId) _friends.RemoveAt(f);

                // bookmarks owned
                for (int b = _bookmarks.Count - 1; b >= 0; b--)
                    if (_bookmarks[b].User == userId) _bookmarks.RemoveAt(b);

                // group membership
                for (int g = 0; g < _groups.Count; g++)
                    if (_groups[g].Members.Remove(userId)) removed++;

                _users.Remove(u);
                _userById.Remove(userId);
                if (!string.IsNullOrEmpty(u.Username)) _userByName.Remove(u.Username);

                MarkUsers();
                MarkPosts();
                MarkComments();
                MarkMessages();
                MarkNotifications();
                MarkFriends();
                MarkBookmarks();
                MarkGroups();
                return removed + 1;
            }
        }

        public int Broadcast(string text, long actorId)
        {
            lock (_sync)
            {
                EnsureWritable();
                int n = 0;
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u.Deleted || u.Banned) continue;
                    Notify(u.Id, actorId, "admin", text, "#/notifications");
                    n++;
                }
                return n;
            }
        }

        // =========================================================== bookmarks

        public bool IsBookmarked(long userId, long postId)
        {
            lock (_sync)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                    if (_bookmarks[i].User == userId && _bookmarks[i].Post == postId) return true;
                return false;
            }
        }

        public bool ToggleBookmark(long userId, long postId)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i].User != userId || _bookmarks[i].Post != postId) continue;
                    _bookmarks.RemoveAt(i);
                    MarkBookmarks();
                    return false;
                }
                Bookmark b = new Bookmark();
                b.User = userId;
                b.Post = postId;
                b.Created = DateTime.UtcNow;
                _bookmarks.Insert(0, b);
                MarkBookmarks();
                return true;
            }
        }

        public List<Post> BookmarksFor(long userId, long before, int limit)
        {
            List<Post> list = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _bookmarks.Count && list.Count < limit; i++)
                {
                    Bookmark b = _bookmarks[i];
                    if (b.User != userId) continue;
                    if (before > 0 && b.Post >= before) continue;
                    Post p = FindPost(b.Post);
                    if (p == null || p.Deleted) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        public int CountBookmarks(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _bookmarks.Count; i++) if (_bookmarks[i].User == userId) n++;
                return n;
            }
        }

        // ======================================================= legacy lookup

        public User FindUserByLegacy(string source, string legacyId)
        {
            if (string.IsNullOrEmpty(legacyId)) return null;
            lock (_sync)
            {
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u.LegacySource == source && u.LegacyId == legacyId) return u;
                }
                return null;
            }
        }

        public Post FindPostByLegacy(string source, string legacyId)
        {
            if (string.IsNullOrEmpty(legacyId)) return null;
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.LegacySource == source && p.LegacyId == legacyId) return p;
                }
                return null;
            }
        }

        public int CountImported()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _users.Count; i++)
                    if (!string.IsNullOrEmpty(_users[i].LegacySource)) n++;
                return n;
            }
        }

        // ============================================================ counters

        public int CountFriends(long userId)
        {
            return FriendIds(userId).Count;
        }

        public int CountLikesGiven(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++)
                    if (!_posts[i].Deleted && _posts[i].Likes.Contains(userId)) n++;
                return n;
            }
        }

        public int CountLikesReceived(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++)
                    if (!_posts[i].Deleted && _posts[i].Author == userId) n += _posts[i].Likes.Count;
                return n;
            }
        }

        public int CountCommentsBy(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _comments.Count; i++)
                    if (!_comments[i].Deleted && _comments[i].Author == userId) n++;
                return n;
            }
        }

        public int CountMessagesBy(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _messages.Count; i++)
                    if (_messages[i].From == userId) n++;
                return n;
            }
        }

        public int CountAllLikes()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++) if (!_posts[i].Deleted) n += _posts[i].Likes.Count;
                return n;
            }
        }

        public int CountAllComments()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _comments.Count; i++) if (!_comments[i].Deleted) n++;
                return n;
            }
        }

        public int CountAllMessages()
        {
            lock (_sync) { return _messages.Count; }
        }

        public List<KeyValuePair<User, int>> TopUsers(int limit)
        {
            List<KeyValuePair<User, int>> list = new List<KeyValuePair<User, int>>();
            lock (_sync)
            {
                for (int i = 0; i < _users.Count; i++)
                {
                    User u = _users[i];
                    if (u.Deleted) continue;
                    list.Add(new KeyValuePair<User, int>(u, CountPosts(u.Id)));
                }
            }
            list.Sort(delegate(KeyValuePair<User, int> a, KeyValuePair<User, int> b) { return b.Value.CompareTo(a.Value); });
            if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        public List<Post> RecentPosts(int limit)
        {
            List<Post> list = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && list.Count < limit; i++)
                    if (!_posts[i].Deleted) list.Add(_posts[i]);
            }
            return list;
        }

        public long TotalStorageBytes()
        {
            try
            {
                long total = 0;
                string[] files = Directory.GetFiles(_cfg.DataDir, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    try { total += new FileInfo(files[i]).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        // ========================================================== serialize

        private Dictionary<string, object> MetaToDict(Meta m)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["version"] = m.Version;
            d["created"] = m.Created;
            d["nextUserId"] = m.NextUserId;
            d["nextPostId"] = m.NextPostId;
            d["nextCommentId"] = m.NextCommentId;
            d["nextMessageId"] = m.NextMessageId;
            d["nextNotificationId"] = m.NextNotificationId;
            d["nextGroupId"] = m.NextGroupId;
            d["nextReportId"] = m.NextReportId;
            d["totalRegistrations"] = m.TotalRegistrations;
            d["lastBackup"] = m.LastBackup;
            d["installId"] = m.InstallId;
            return d;
        }

        private List<object> UsersToArray()
        {
            List<object> list = new List<object>(_users.Count);
            for (int i = 0; i < _users.Count; i++) list.Add(UserToDict(_users[i]));
            return list;
        }

        private static Dictionary<string, object> UserToDict(User u)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = u.Id; d["username"] = u.Username; d["displayName"] = u.DisplayName;
            d["verifier"] = u.Verifier; d["salt"] = u.Salt; d["recovery"] = u.Recovery; d["recoverySalt"] = u.RecoverySalt;
            d["role"] = u.Role; d["created"] = u.Created; d["lastSeen"] = u.LastSeen; d["lastLogin"] = u.LastLogin;
            d["status"] = u.Status; d["bio"] = u.Bio; d["city"] = u.City; d["country"] = u.Country;
            d["birthday"] = u.Birthday; d["gender"] = u.Gender; d["website"] = u.Website; d["avatar"] = u.Avatar;
            d["theme"] = u.Theme; d["lang"] = u.Lang; d["sound"] = u.Sound; d["privacy"] = u.Privacy;
            d["hideOnline"] = u.HideOnline; d["banned"] = u.Banned; d["banReason"] = u.BanReason;
            d["bannedUntil"] = u.BannedUntil; d["warnings"] = u.Warnings; d["karma"] = u.Karma;
            d["email"] = u.Email; d["ipHash"] = u.IpHash; d["loginCount"] = u.LoginCount;
            d["deleted"] = u.Deleted; d["lastPostAt"] = u.LastPostAt; d["lastCommentAt"] = u.LastCommentAt;
            d["lastMessageAt"] = u.LastMessageAt;
            d["favGames"] = u.FavGames; d["favMovies"] = u.FavMovies; d["favMusic"] = u.FavMusic;
            d["favBooks"] = u.FavBooks; d["persona"] = u.Persona; d["mood"] = u.Mood;
            d["badges"] = u.Badges; d["notifyMute"] = u.NotifyMute; d["legacySource"] = u.LegacySource; d["legacyId"] = u.LegacyId;
            return d;
        }

        private static User UserFromDict(Dictionary<string, object> d)
        {
            User u = new User();
            u.Id = Json.GetLong(d, "id", 0);
            u.Username = Json.GetString(d, "username", "");
            u.DisplayName = Json.GetString(d, "displayName", u.Username);
            u.Verifier = Json.GetString(d, "verifier", "");
            u.Salt = Json.GetString(d, "salt", "");
            u.Recovery = Json.GetString(d, "recovery", "");
            u.RecoverySalt = Json.GetString(d, "recoverySalt", "");
            u.Role = Json.GetString(d, "role", "user");
            u.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            u.LastSeen = Json.GetDate(d, "lastSeen", DateTime.UtcNow);
            u.LastLogin = Json.GetDate(d, "lastLogin", DateTime.UtcNow);
            u.Status = Json.GetString(d, "status", "");
            u.Bio = Json.GetString(d, "bio", "");
            u.City = Json.GetString(d, "city", "");
            u.Country = Json.GetString(d, "country", "");
            u.Birthday = Json.GetString(d, "birthday", "");
            u.Gender = Json.GetString(d, "gender", "");
            u.Website = Json.GetString(d, "website", "");
            u.Avatar = Json.GetString(d, "avatar", "");
            u.Theme = Json.GetString(d, "theme", "");
            u.Lang = Json.GetString(d, "lang", "");
            u.Sound = Json.GetBool(d, "sound", true);
            u.Privacy = Json.GetString(d, "privacy", "public");
            u.HideOnline = Json.GetBool(d, "hideOnline", false);
            u.Banned = Json.GetBool(d, "banned", false);
            u.BanReason = Json.GetString(d, "banReason", "");
            u.BannedUntil = Json.GetDate(d, "bannedUntil", DateTime.MinValue);
            u.Warnings = Json.GetInt(d, "warnings", 0);
            u.Karma = Json.GetInt(d, "karma", 0);
            u.Email = Json.GetString(d, "email", "");
            u.IpHash = Json.GetString(d, "ipHash", "");
            u.LoginCount = Json.GetInt(d, "loginCount", 0);
            u.Deleted = Json.GetBool(d, "deleted", false);
            u.LastPostAt = Json.GetDate(d, "lastPostAt", DateTime.MinValue);
            u.LastCommentAt = Json.GetDate(d, "lastCommentAt", DateTime.MinValue);
            u.LastMessageAt = Json.GetDate(d, "lastMessageAt", DateTime.MinValue);
            u.FavGames = Json.GetString(d, "favGames", "");
            u.FavMovies = Json.GetString(d, "favMovies", "");
            u.FavMusic = Json.GetString(d, "favMusic", "");
            u.FavBooks = Json.GetString(d, "favBooks", "");
            u.Persona = Json.GetString(d, "persona", "");
            u.Mood = Json.GetString(d, "mood", "");
            u.Badges = Json.GetString(d, "badges", "");
            u.NotifyMute = Json.GetString(d, "notifyMute", "");
            u.LegacySource = Json.GetString(d, "legacySource", "");
            u.LegacyId = Json.GetString(d, "legacyId", "");
            return u;
        }

        private List<object> PostsToArray()
        {
            List<object> list = new List<object>(_posts.Count);
            for (int i = 0; i < _posts.Count; i++)
            {
                Post p = _posts[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = p.Id; d["author"] = p.Author; d["wall"] = p.Wall; d["group"] = p.Group;
                d["body"] = p.Body; d["image"] = p.Image; d["video"] = p.Video;
                d["created"] = p.Created; d["edited"] = p.Edited;
                d["visibility"] = p.Visibility; d["likes"] = p.Likes; d["comments"] = p.Comments;
                d["deleted"] = p.Deleted; d["pinned"] = p.Pinned;
                d["legacySource"] = p.LegacySource; d["legacyId"] = p.LegacyId;
                list.Add(d);
            }
            return list;
        }

        private static Post PostFromDict(Dictionary<string, object> d)
        {
            Post p = new Post();
            p.Id = Json.GetLong(d, "id", 0);
            p.Author = Json.GetLong(d, "author", 0);
            p.Wall = Json.GetLong(d, "wall", 0);
            p.Group = Json.GetLong(d, "group", 0);
            p.Body = Json.GetString(d, "body", "");
            p.Image = Json.GetString(d, "image", "");
            p.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            p.Edited = Json.GetDate(d, "edited", DateTime.MinValue);
            p.Visibility = Json.GetString(d, "visibility", "public");
            p.Comments = Json.GetInt(d, "comments", 0);
            p.Deleted = Json.GetBool(d, "deleted", false);
            p.Pinned = Json.GetBool(d, "pinned", false);
            p.Video = Json.GetString(d, "video", "");
            p.LegacySource = Json.GetString(d, "legacySource", "");
            p.LegacyId = Json.GetString(d, "legacyId", "");
            object likes = Json.Get(d, "likes");
            List<object> arr = likes as List<object>;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is long) p.Likes.Add((long)arr[i]);
                    else
                    {
                        long v;
                        if (long.TryParse(Convert.ToString(arr[i], CultureInfo.InvariantCulture), out v)) p.Likes.Add(v);
                    }
                }
            }
            return p;
        }

        private List<object> CommentsToArray()
        {
            List<object> list = new List<object>(_comments.Count);
            for (int i = 0; i < _comments.Count; i++)
            {
                Comment c = _comments[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = c.Id; d["post"] = c.Post; d["author"] = c.Author; d["body"] = c.Body;
                d["created"] = c.Created; d["deleted"] = c.Deleted;
                list.Add(d);
            }
            return list;
        }

        private static Comment CommentFromDict(Dictionary<string, object> d)
        {
            Comment c = new Comment();
            c.Id = Json.GetLong(d, "id", 0);
            c.Post = Json.GetLong(d, "post", 0);
            c.Author = Json.GetLong(d, "author", 0);
            c.Body = Json.GetString(d, "body", "");
            c.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            c.Deleted = Json.GetBool(d, "deleted", false);
            return c;
        }

        private List<object> FriendsToArray()
        {
            List<object> list = new List<object>(_friends.Count);
            for (int i = 0; i < _friends.Count; i++)
            {
                FriendLink f = _friends[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["from"] = f.From; d["to"] = f.To; d["status"] = f.Status;
                d["created"] = f.Created; d["updated"] = f.Updated;
                list.Add(d);
            }
            return list;
        }

        private static FriendLink FriendFromDict(Dictionary<string, object> d)
        {
            FriendLink f = new FriendLink();
            f.From = Json.GetLong(d, "from", 0);
            f.To = Json.GetLong(d, "to", 0);
            f.Status = Json.GetString(d, "status", "pending");
            f.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            f.Updated = Json.GetDate(d, "updated", DateTime.UtcNow);
            return f;
        }

        private List<object> MessagesToArray()
        {
            List<object> list = new List<object>(_messages.Count);
            for (int i = 0; i < _messages.Count; i++)
            {
                Message m = _messages[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = m.Id; d["from"] = m.From; d["to"] = m.To; d["body"] = m.Body;
                d["image"] = m.Image; d["replyTo"] = m.ReplyTo;
                d["created"] = m.Created; d["read"] = m.Read;
                d["deletedByFrom"] = m.DeletedByFrom; d["deletedByTo"] = m.DeletedByTo;
                list.Add(d);
            }
            return list;
        }

        private static Message MessageFromDict(Dictionary<string, object> d)
        {
            Message m = new Message();
            m.Id = Json.GetLong(d, "id", 0);
            m.From = Json.GetLong(d, "from", 0);
            m.To = Json.GetLong(d, "to", 0);
            m.Body = Json.GetString(d, "body", "");
            m.Image = Json.GetString(d, "image", "");
            m.ReplyTo = Json.GetLong(d, "replyTo", 0);
            m.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            m.Read = Json.GetBool(d, "read", false);
            m.DeletedByFrom = Json.GetBool(d, "deletedByFrom", false);
            m.DeletedByTo = Json.GetBool(d, "deletedByTo", false);
            return m;
        }

        private List<object> NotificationsToArray()
        {
            List<object> list = new List<object>(_notifications.Count);
            for (int i = 0; i < _notifications.Count; i++)
            {
                Notification n = _notifications[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = n.Id; d["user"] = n.User; d["actor"] = n.Actor; d["type"] = n.Type;
                d["text"] = n.Text; d["link"] = n.Link; d["created"] = n.Created; d["read"] = n.Read;
                list.Add(d);
            }
            return list;
        }

        private static Notification NotificationFromDict(Dictionary<string, object> d)
        {
            Notification n = new Notification();
            n.Id = Json.GetLong(d, "id", 0);
            n.User = Json.GetLong(d, "user", 0);
            n.Actor = Json.GetLong(d, "actor", 0);
            n.Type = Json.GetString(d, "type", "system");
            n.Text = Json.GetString(d, "text", "");
            n.Link = Json.GetString(d, "link", "");
            n.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            n.Read = Json.GetBool(d, "read", false);
            return n;
        }

        private List<object> GroupsToArray()
        {
            List<object> list = new List<object>(_groups.Count);
            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = g.Id; d["name"] = g.Name; d["slug"] = g.Slug; d["description"] = g.Description;
                d["avatar"] = g.Avatar; d["owner"] = g.Owner; d["members"] = g.Members;
                d["created"] = g.Created; d["visibility"] = g.Visibility; d["deleted"] = g.Deleted;
                list.Add(d);
            }
            return list;
        }

        private static Group GroupFromDict(Dictionary<string, object> d)
        {
            Group g = new Group();
            g.Id = Json.GetLong(d, "id", 0);
            g.Name = Json.GetString(d, "name", "");
            g.Slug = Json.GetString(d, "slug", "");
            g.Description = Json.GetString(d, "description", "");
            g.Avatar = Json.GetString(d, "avatar", "");
            g.Owner = Json.GetLong(d, "owner", 0);
            g.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            g.Visibility = Json.GetString(d, "visibility", "public");
            g.Deleted = Json.GetBool(d, "deleted", false);
            object members = Json.Get(d, "members");
            List<object> arr = members as List<object>;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is long) g.Members.Add((long)arr[i]);
                    else
                    {
                        long v;
                        if (long.TryParse(Convert.ToString(arr[i], CultureInfo.InvariantCulture), out v)) g.Members.Add(v);
                    }
                }
            }
            return g;
        }

        private List<object> BookmarksToArray()
        {
            List<object> list = new List<object>(_bookmarks.Count);
            for (int i = 0; i < _bookmarks.Count; i++)
            {
                Bookmark b = _bookmarks[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["user"] = b.User; d["post"] = b.Post; d["created"] = b.Created;
                list.Add(d);
            }
            return list;
        }

        private static Bookmark BookmarkFromDict(Dictionary<string, object> d)
        {
            Bookmark b = new Bookmark();
            b.User = Json.GetLong(d, "user", 0);
            b.Post = Json.GetLong(d, "post", 0);
            b.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            if (b.User <= 0 || b.Post <= 0) return null;
            return b;
        }

        private List<object> ReportsToArray()
        {
            List<object> list = new List<object>(_reports.Count);
            for (int i = 0; i < _reports.Count; i++)
            {
                Report r = _reports[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = r.Id; d["type"] = r.Type; d["target"] = r.Target; d["reporter"] = r.Reporter;
                d["reason"] = r.Reason; d["created"] = r.Created; d["status"] = r.Status;
                d["note"] = r.Note; d["handledBy"] = r.HandledBy; d["handledAt"] = r.HandledAt;
                list.Add(d);
            }
            return list;
        }

        private static Report ReportFromDict(Dictionary<string, object> d)
        {
            Report r = new Report();
            r.Id = Json.GetLong(d, "id", 0);
            r.Type = Json.GetString(d, "type", "post");
            r.Target = Json.GetLong(d, "target", 0);
            r.Reporter = Json.GetLong(d, "reporter", 0);
            r.Reason = Json.GetString(d, "reason", "");
            r.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            r.Status = Json.GetString(d, "status", "open");
            r.Note = Json.GetString(d, "note", "");
            r.HandledBy = Json.GetLong(d, "handledBy", 0);
            r.HandledAt = Json.GetDate(d, "handledAt", DateTime.MinValue);
            return r;
        }
    }
}

