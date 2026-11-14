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
        private readonly Config _cfg;        /// <summary>The language new accounts start with, so "polyglot" means a real switch.</summary>
        public string DefaultLanguage { get { return _cfg == null ? "en" : _cfg.DefaultLanguage; } }

        private readonly List<User> _users = new List<User>();
        private readonly List<Post> _posts = new List<Post>();
        private readonly List<Comment> _comments = new List<Comment>();
        private readonly List<FriendLink> _friends = new List<FriendLink>();
        private readonly List<Message> _messages = new List<Message>();
        private readonly List<Notification> _notifications = new List<Notification>();
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<Report> _reports = new List<Report>();
        private readonly List<Bookmark> _bookmarks = new List<Bookmark>();
        private readonly List<Draft> _drafts = new List<Draft>();
        private readonly List<MediaItem> _media = new List<MediaItem>();
        private readonly List<Playlist> _playlists = new List<Playlist>();
        private readonly List<Feedback> _feedback = new List<Feedback>();
        private readonly List<CallRecord> _calls = new List<CallRecord>();
        private readonly List<Gift> _gifts = new List<Gift>();
        private readonly List<Album> _albums = new List<Album>();
        private readonly List<Story> _stories = new List<Story>();
        private Meta _meta = new Meta();

        private readonly Dictionary<long, User> _userById = new Dictionary<long, User>();
        private readonly Dictionary<string, User> _userByName = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);

        private bool _dirtyUsers, _dirtyPosts, _dirtyComments, _dirtyFriends, _dirtyMessages;
        private bool _dirtyNotifications, _dirtyGroups, _dirtyReports, _dirtyBookmarks, _dirtyMeta, _dirtyDrafts, _dirtyMedia, _dirtyPlaylists, _dirtyFeedback, _dirtyCalls, _dirtyGifts, _dirtyAlbums, _dirtyStories;
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
                           _dirtyNotifications || _dirtyGroups || _dirtyReports || _dirtyBookmarks || _dirtyMeta ||
                           _dirtyDrafts || _dirtyMedia || _dirtyPlaylists || _dirtyFeedback || _dirtyCalls || _dirtyGifts || _dirtyAlbums || _dirtyStories;
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
                _drafts.Clear();
                _drafts.AddRange(LoadTable("drafts.json", DraftFromDict));
                _media.Clear();
                _media.AddRange(LoadTable("media.json", MediaFromDict));
                _playlists.Clear();
                _playlists.AddRange(LoadTable("playlists.json", PlaylistFromDict));
                _feedback.Clear();
                _feedback.AddRange(LoadTable("feedback.json", FeedbackFromDict));
                _calls.Clear();
                _calls.AddRange(LoadTable("calls.json", CallRecordFromDict));
                // a restart means every call that was still open is over
                for (int i = 0; i < _calls.Count; i++)
                    if (_calls[i].Ended == DateTime.MinValue) _calls[i].Ended = _calls[i].Started;
                _gifts.Clear();
                _gifts.AddRange(LoadTable("gifts.json", GiftFromDict));
                _albums.Clear();
                _albums.AddRange(LoadTable("albums.json", AlbumFromDict));
                _stories.Clear();
                _stories.AddRange(LoadTable("stories.json", StoryFromDict));

                // sort newest first, keeping the hot queries cheap
                _posts.Sort(delegate(Post a, Post b) { return b.Id.CompareTo(a.Id); });
                _comments.Sort(delegate(Comment a, Comment b) { return b.Id.CompareTo(a.Id); });
                _messages.Sort(delegate(Message a, Message b) { return b.Id.CompareTo(a.Id); });
                _notifications.Sort(delegate(Notification a, Notification b) { return b.Id.CompareTo(a.Id); });

                EnsureCounters();
                _loaded = true;
                _dirtyUsers = _dirtyPosts = _dirtyComments = _dirtyFriends = _dirtyMessages = false;
                _dirtyNotifications = _dirtyGroups = _dirtyReports = _dirtyBookmarks = _dirtyMeta = _dirtyDrafts = _dirtyMedia = _dirtyPlaylists = _dirtyFeedback = _dirtyCalls = _dirtyGifts = _dirtyAlbums = _dirtyStories = false;

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
                m.NextDraftId = Json.GetLong(d, "nextDraftId", 1);
                m.NextMediaId = Json.GetLong(d, "nextMediaId", 1);
                m.NextPlaylistId = Json.GetLong(d, "nextPlaylistId", 1);
                m.NextFeedbackId = Json.GetLong(d, "nextFeedbackId", 1);
                m.NextCallId = Json.GetLong(d, "nextCallId", 1);
                m.NextGiftId = Json.GetLong(d, "nextGiftId", 1);
                m.NextAlbumId = Json.GetLong(d, "nextAlbumId", 1);
                m.NextStoryId = Json.GetLong(d, "nextStoryId", 1);
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
            long maxUser = 0, maxPost = 0, maxComment = 0, maxMessage = 0, maxNotif = 0, maxGroup = 0, maxReport = 0, maxDraft = 0, maxMedia = 0, maxPlaylist = 0, maxFeedback = 0, maxCall = 0, maxGift = 0;
            for (int i = 0; i < _users.Count; i++) if (_users[i].Id > maxUser) maxUser = _users[i].Id;
            for (int i = 0; i < _posts.Count; i++) if (_posts[i].Id > maxPost) maxPost = _posts[i].Id;
            for (int i = 0; i < _comments.Count; i++) if (_comments[i].Id > maxComment) maxComment = _comments[i].Id;
            for (int i = 0; i < _messages.Count; i++) if (_messages[i].Id > maxMessage) maxMessage = _messages[i].Id;
            for (int i = 0; i < _notifications.Count; i++) if (_notifications[i].Id > maxNotif) maxNotif = _notifications[i].Id;
            for (int i = 0; i < _groups.Count; i++) if (_groups[i].Id > maxGroup) maxGroup = _groups[i].Id;
            for (int i = 0; i < _reports.Count; i++) if (_reports[i].Id > maxReport) maxReport = _reports[i].Id;
            for (int i = 0; i < _drafts.Count; i++) if (_drafts[i].Id > maxDraft) maxDraft = _drafts[i].Id;
            for (int i = 0; i < _media.Count; i++) if (_media[i].Id > maxMedia) maxMedia = _media[i].Id;
            for (int i = 0; i < _playlists.Count; i++) if (_playlists[i].Id > maxPlaylist) maxPlaylist = _playlists[i].Id;
            for (int i = 0; i < _feedback.Count; i++) if (_feedback[i].Id > maxFeedback) maxFeedback = _feedback[i].Id;
            for (int i = 0; i < _calls.Count; i++) if (_calls[i].Id > maxCall) maxCall = _calls[i].Id;
            for (int i = 0; i < _gifts.Count; i++) if (_gifts[i].Id > maxGift) maxGift = _gifts[i].Id;
            if (_meta.NextUserId <= maxUser) _meta.NextUserId = maxUser + 1;
            if (_meta.NextPostId <= maxPost) _meta.NextPostId = maxPost + 1;
            if (_meta.NextCommentId <= maxComment) _meta.NextCommentId = maxComment + 1;
            if (_meta.NextMessageId <= maxMessage) _meta.NextMessageId = maxMessage + 1;
            if (_meta.NextNotificationId <= maxNotif) _meta.NextNotificationId = maxNotif + 1;
            if (_meta.NextGroupId <= maxGroup) _meta.NextGroupId = maxGroup + 1;
            if (_meta.NextReportId <= maxReport) _meta.NextReportId = maxReport + 1;
            if (_meta.NextDraftId <= maxDraft) _meta.NextDraftId = maxDraft + 1;
            if (_meta.NextMediaId <= maxMedia) _meta.NextMediaId = maxMedia + 1;
            if (_meta.NextPlaylistId <= maxPlaylist) _meta.NextPlaylistId = maxPlaylist + 1;
            if (_meta.NextFeedbackId <= maxFeedback) _meta.NextFeedbackId = maxFeedback + 1;
            if (_meta.NextCallId <= maxCall) _meta.NextCallId = maxCall + 1;
            if (_meta.NextGiftId <= maxGift) _meta.NextGiftId = maxGift + 1;
            long maxAlbum = 0;
            for (int i = 0; i < _albums.Count; i++) if (_albums[i].Id > maxAlbum) maxAlbum = _albums[i].Id;
            if (_meta.NextAlbumId <= maxAlbum) _meta.NextAlbumId = maxAlbum + 1;
            long maxStory = 0;
            for (int i = 0; i < _stories.Count; i++) if (_stories[i].Id > maxStory) maxStory = _stories[i].Id;
            if (_meta.NextStoryId <= maxStory) _meta.NextStoryId = maxStory + 1;
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
                    if (force || _dirtyDrafts) WriteTable("drafts.json", DraftsToArray());
                    if (force || _dirtyMedia) WriteTable("media.json", MediaToArray());
                    if (force || _dirtyPlaylists) WriteTable("playlists.json", PlaylistsToArray());
                    if (force || _dirtyFeedback) WriteTable("feedback.json", FeedbackToArray());
                    if (force || _dirtyCalls) WriteTable("calls.json", CallsToArray());
                    if (force || _dirtyGifts) WriteTable("gifts.json", GiftsToArray());
                    if (force || _dirtyAlbums) WriteTable("albums.json", AlbumsToArray());
                    if (force || _dirtyStories) WriteTable("stories.json", StoriesToArray());

                    _dirtyUsers = _dirtyPosts = _dirtyComments = _dirtyFriends = _dirtyMessages = false;
                    _dirtyNotifications = _dirtyGroups = _dirtyReports = _dirtyMeta = _dirtyDrafts = _dirtyMedia = _dirtyPlaylists = _dirtyFeedback = _dirtyCalls = _dirtyGifts = _dirtyAlbums = _dirtyStories = false;
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
                   _dirtyNotifications || _dirtyGroups || _dirtyReports || _dirtyBookmarks || _dirtyMeta ||
                   _dirtyDrafts || _dirtyMedia || _dirtyPlaylists || _dirtyFeedback;
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
        private void MarkDrafts() { _dirtyDrafts = true; }
        private void MarkMedia() { _dirtyMedia = true; }
        private void MarkPlaylists() { _dirtyPlaylists = true; }
        private void MarkFeedback() { _dirtyFeedback = true; }
        private void MarkCalls() { _dirtyCalls = true; }
        private void MarkGifts() { _dirtyGifts = true; }
        private void MarkAlbums() { _dirtyAlbums = true; }
        private void MarkStories() { _dirtyStories = true; }

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

        /// <summary>Flags a post that the caller mutated directly (e.g. queued it).</summary>
        public void MarkPostDirty(Post p)
        {
            lock (_sync)
            {
                if (p != null) MarkPosts();
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
            return CreatePost(author, wall, group, body, image, visibility, 0);
        }

        public Post CreatePost(long author, long wall, long group, string body, string image, string visibility, long repostOf)
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
                p.RepostOf = repostOf;
                p.Created = DateTime.UtcNow;
                _posts.Insert(0, p);
                if (repostOf > 0)
                {
                    Post origin = FindPost(repostOf);
                    if (origin != null && !origin.Deleted) origin.Reposts++;
                }
                MarkPosts();
                return p;
            }
        }

        /// <summary>
        /// Removes a repost and takes one off the original's counter. Returns the
        /// original id (0 when the post was not a repost) so callers can react.
        /// </summary>
        public long DetachRepost(long id)
        {
            lock (_sync)
            {
                Post p = FindPost(id);
                if (p == null || p.RepostOf <= 0) return 0;
                Post origin = FindPost(p.RepostOf);
                if (origin != null && origin.Reposts > 0) origin.Reposts--;
                return p.RepostOf;
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

        /// <summary>Attaches a poll to a freshly created post.</summary>
        public void SetPoll(long id, string question, List<string> options)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null || p.Deleted) return;
                Polls.Set(p, question, options);
                MarkPosts();
            }
        }

        /// <summary>Applies a vote; returns the option the user now backs or -1.</summary>
        public int VotePoll(long id, long userId, int option)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null || p.Deleted || !Polls.Has(p)) return -1;
                int result = Polls.Vote(p, userId, option);
                MarkPosts();
                return result;
            }
        }

        /// <summary>Toggles an emoji reaction on a post. Returns null when missing.</summary>
        public Post ReactPost(long id, long userId, string emoji)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(id);
                if (p == null || p.Deleted) return null;
                string updated;
                Reactions.Toggle(p.Reactions, userId, emoji, out updated);
                p.Reactions = updated;
                MarkPosts();
                return p;
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
                    if (!IsLive(p)) continue;
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
            // a scheduled post stays invisible until its time comes
            if (!IsLive(p)) return false;
            // a post waiting for a community editor is visible only in the queue
            if (p.Pending) return false;

            // A community post is gated by the community, not by the post's own
            // visibility: the wall is private for outsiders and fully readable for
            // members, so the same rule must apply to every post on it.
            if (p.Group > 0)
            {
                Group g = FindGroup(p.Group);
                if (g == null) return false;
                if (viewerId > 0 && (g.Owner == viewerId || g.Members.Contains(viewerId))) return true;
                return g.Visibility != "private" && p.Visibility == "public";
            }

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
                    if (!IsLive(p)) continue;
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
                    if (!IsLive(p)) continue;
                    if (p.Body.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0) result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// Filtered search over the posts the viewer may see. Returns at most
        /// <paramref name="limit"/> items; <paramref name="hasMore"/> tells whether a
        /// further page exists. Sorted by the filter (recent / old / popular).
        /// </summary>
        public List<Post> SearchPosts(PostFilter filter, long viewerId, int limit, out bool hasMore)
        {
            hasMore = false;
            List<Post> result = new List<Post>();
            if (filter == null) return result;

            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Group != 0) continue;
                    if (!filter.Matches(p)) continue;
                    if (!VisibleTo(viewerId, p)) continue;
                    result.Add(p);
                }
            }

            if (filter.Sort == "old")
                result.Sort(delegate(Post a, Post b) { return a.Id.CompareTo(b.Id); });
            else if (filter.Sort == "popular")
                result.Sort(delegate(Post a, Post b)
                {
                    int scoreA = a.Likes.Count * 3 + a.Comments;
                    int scoreB = b.Likes.Count * 3 + b.Comments;
                    int byScore = scoreB.CompareTo(scoreA);
                    return byScore != 0 ? byScore : b.Id.CompareTo(a.Id);
                });

            if (result.Count > limit)
            {
                hasMore = true;
                result.RemoveRange(limit, result.Count - limit);
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
            return CreateMessage(from, to, body, image, "", 0, replyTo);
        }

        public Message CreateMessage(long from, long to, string body, string image, string audio, int audioSec, long replyTo)
        {
            return CreateMessage(from, to, body, image, audio, audioSec, replyTo, 0);
        }

        public Message CreateMessage(long from, long to, string body, string image, string audio, int audioSec, long replyTo, long forwardedFrom)
        {
            return CreateMessage(from, to, body, image, audio, audioSec, replyTo, forwardedFrom, "", "", 0);
        }

        public Message CreateMessage(long from, long to, string body, string image, string audio, int audioSec,
                                     long replyTo, long forwardedFrom, string fileUrl, string fileName, long fileSize)
        {
            return CreateMessage(from, to, body, image, "", audio, audioSec, replyTo, forwardedFrom,
                fileUrl, fileName, fileSize);
        }

        public Message CreateMessage(long from, long to, string body, string image, string images, string audio,
                                     int audioSec, long replyTo, long forwardedFrom,
                                     string fileUrl, string fileName, long fileSize)
        {
            return CreateMessage(from, to, body, image, images, audio, audioSec, replyTo, forwardedFrom,
                fileUrl, fileName, fileSize, "");
        }

        public Message CreateMessage(long from, long to, string body, string image, string images, string audio,
                                     int audioSec, long replyTo, long forwardedFrom,
                                     string fileUrl, string fileName, long fileSize, string video)
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
                m.Images = images ?? "";
                m.Video = video ?? "";
                m.Audio = audio ?? "";
                m.AudioSec = audioSec;
                m.ReplyTo = replyTo;
                m.ForwardedFrom = forwardedFrom;
                m.FileUrl = fileUrl ?? "";
                m.FileName = fileName ?? "";
                m.FileSize = fileSize;
                m.Created = DateTime.UtcNow;
                _messages.Insert(0, m);
                MarkMessages();
                return m;
            }
        }

        /// <summary>
        /// Timestamp up to which the peer has read our messages in this conversation,
        /// or MinValue when they have not read anything yet.
        /// </summary>
        public DateTime ReadUpTo(long viewer, long other)
        {
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.From != viewer || m.To != other || m.DeletedByFrom) continue;
                    return m.Read ? m.Created : DateTime.MinValue;
                }
                return DateTime.MinValue;
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

        /// <summary>Toggles a reaction on a message the user is part of.</summary>
        public Message ReactMessage(long id, long userId, string emoji)
        {
            lock (_sync)
            {
                EnsureWritable();
                Message m = FindMessage(id);
                if (m == null) return null;
                if (m.From != userId && m.To != userId) return null;
                string updated;
                Reactions.Toggle(m.Reactions, userId, emoji, out updated);
                m.Reactions = updated;
                MarkMessages();
                return m;
            }
        }

        /// <summary>
        /// Full text search over the viewer's own conversations. When
        /// <paramref name="with"/> is set only that conversation is searched.
        /// </summary>
        public List<Message> SearchMessages(long viewer, long with, string query, int limit)
        {
            List<Message> result = new List<Message>();
            if (string.IsNullOrEmpty(query) || query.Trim().Length < 2) return result;
            string q = query.Trim().ToLowerInvariant();
            lock (_sync)
            {
                for (int i = 0; i < _messages.Count && result.Count < limit; i++)
                {
                    Message m = _messages[i];
                    if (m.From != viewer && m.To != viewer) continue;
                    if (m.From == viewer && m.DeletedByFrom) continue;
                    if (m.To == viewer && m.DeletedByTo) continue;
                    if (with > 0 && m.From != with && m.To != with) continue;
                    if (string.IsNullOrEmpty(m.Body)) continue;
                    if (m.Body.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                    result.Add(m);
                }
            }
            return result;
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
                // leaving also drops the editor badge and any pending request
                bool changed = g.Members.Remove(userId);
                if (g.Editors.Remove(userId)) changed = true;
                if (g.Requests.Remove(userId)) changed = true;
                if (changed) MarkGroups();
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
                g.Editors.Remove(userId);
                g.Requests.Remove(userId);
                MarkGroups();
                return true;
            }
        }

        // ======================================================== communities

        /// <summary>"owner", "editor", "member" or "" for an outsider.</summary>
        public static string RoleIn(Group g, long userId)
        {
            if (g == null || userId <= 0) return "";
            if (g.Owner == userId) return "owner";
            if (g.Editors.Contains(userId)) return "editor";
            if (g.Members.Contains(userId)) return "member";
            return "";
        }

        /// <summary>Owner, an appointed editor, or a site administrator.</summary>
        public static bool CanModerate(Group g, User viewer)
        {
            if (g == null || viewer == null) return false;
            if (viewer.IsModerator) return true;
            string role = RoleIn(g, viewer.Id);
            return role == "owner" || role == "editor";
        }

        public bool HasGroupRequest(Group g, long userId)
        {
            lock (_sync)
            {
                return g != null && g.Requests.Contains(userId);
            }
        }

        /// <summary>Asks to join a closed community. Returns false when it is pointless.</summary>
        public bool RequestJoin(long groupId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null || userId <= 0) return false;
                if (g.Owner == userId || g.Members.Contains(userId)) return false;
                if (g.Requests.Contains(userId)) return true;
                g.Requests.Add(userId);
                MarkGroups();
                return true;
            }
        }

        public bool CancelJoinRequest(long groupId, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null || !g.Requests.Remove(userId)) return false;
                MarkGroups();
                return true;
            }
        }

        /// <summary>Accepts or refuses a pending request; the request always disappears.</summary>
        public bool AnswerJoinRequest(long groupId, long userId, bool accept)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null || !g.Requests.Remove(userId)) return false;
                if (accept && !g.Members.Contains(userId)) g.Members.Add(userId);
                MarkGroups();
                return true;
            }
        }

        /// <summary>Makes somebody an editor, or takes the role away. The owner is untouchable.</summary>
        public bool SetEditor(long groupId, long userId, bool editor)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null || userId == g.Owner) return false;
                if (!g.Members.Contains(userId)) return false;
                bool changed;
                if (editor) changed = !g.Editors.Contains(userId) && AddOnce(g.Editors, userId);
                else changed = g.Editors.Remove(userId);
                if (changed) MarkGroups();
                return changed;
            }
        }

        private static bool AddOnce(List<long> list, long value)
        {
            if (list.Contains(value)) return false;
            list.Add(value);
            return true;
        }

        /// <summary>Changes who may post and whether their posts need review.</summary>
        public bool SetGroupPolicy(long groupId, string policy)
        {
            lock (_sync)
            {
                EnsureWritable();
                Group g = FindGroup(groupId);
                if (g == null) return false;
                g.PostPolicy = policy == "editors" || policy == "review" ? policy : "all";
                MarkGroups();
                return true;
            }
        }

        /// <summary>Posts of a community that wait for an editor, newest first.</summary>
        public List<Post> PendingPosts(long groupId, int limit)
        {
            List<Post> list = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && list.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || !p.Pending || p.Group != groupId) continue;
                    list.Add(p);
                }
            }
            return list;
        }

        public int CountPending(long groupId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++)
                    if (!_posts[i].Deleted && _posts[i].Pending && _posts[i].Group == groupId) n++;
                return n;
            }
        }

        /// <summary>Publishes a queued post, or drops it when it is refused.</summary>
        public Post ReviewPost(long postId, bool approve)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(postId);
                if (p == null || !p.Pending) return null;
                if (approve)
                {
                    p.Pending = false;
                    p.Created = DateTime.UtcNow;
                }
                else
                {
                    p.Deleted = true;
                }
                MarkPosts();
                return p;
            }
        }

        // ============================================================== search

        /// <summary>
        /// Comments whose body matches, newest first. A comment is only visible when
        /// its post is: the store checks the parent, so a private wall never leaks
        /// through search.
        /// </summary>
        public List<Comment> SearchComments(string query, long viewerId, long author,
                                            DateTime since, DateTime until, long before, int limit, out bool hasMore)
        {
            hasMore = false;
            List<Comment> result = new List<Comment>();
            if (string.IsNullOrEmpty(query) || query.Trim().Length < 2) return result;

            lock (_sync)
            {
                for (int i = 0; i < _comments.Count; i++)
                {
                    Comment c = _comments[i];
                    if (c.Deleted) continue;
                    if (author > 0 && c.Author != author) continue;
                    if (since != DateTime.MinValue && c.Created < since) continue;
                    if (until != DateTime.MinValue && c.Created > until) continue;
                    if (before > 0 && c.Id >= before) continue;
                    if (string.IsNullOrEmpty(c.Body)) continue;
                    if (c.Body.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    Post parent = FindPost(c.Post);
                    if (parent == null || !VisibleTo(viewerId, parent)) continue;

                    if (result.Count >= limit) { hasMore = true; break; }
                    result.Add(c);
                }
            }
            return result;
        }

        /// <summary>Public communities whose name or description matches.</summary>
        public List<Group> SearchGroups(string query, long viewerId, int limit)
        {
            List<Group> result = new List<Group>();
            if (string.IsNullOrEmpty(query)) return result;
            string q = query.Trim();

            lock (_sync)
            {
                for (int i = 0; i < _groups.Count && result.Count < limit; i++)
                {
                    Group g = _groups[i];
                    if (g.Deleted) continue;
                    // a closed community is only findable by its members
                    if (g.Visibility == "private" && g.Owner != viewerId && !g.Members.Contains(viewerId)) continue;
                    if (g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                        g.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    result.Add(g);
                }
            }
            return result;
        }

        /// <summary>
        /// Messages in the viewer's own conversations that match. There is no way to
        /// search somebody else's chat: the loop only ever looks at messages the
        /// viewer sent or received.
        /// </summary>
        public List<Message> SearchOwnMessages(string query, long viewer, long author, long with,
                                               DateTime since, DateTime until, long before, int limit, out bool hasMore)
        {
            hasMore = false;
            List<Message> result = new List<Message>();
            if (string.IsNullOrEmpty(query) || query.Trim().Length < 2) return result;

            lock (_sync)
            {
                for (int i = 0; i < _messages.Count; i++)
                {
                    Message m = _messages[i];
                    if (m.From != viewer && m.To != viewer) continue;
                    if (m.From == viewer && m.DeletedByFrom) continue;
                    if (m.To == viewer && m.DeletedByTo) continue;
                    if (author > 0 && m.From != author) continue;
                    if (with > 0 && m.From != with && m.To != with) continue;
                    if (since != DateTime.MinValue && m.Created < since) continue;
                    if (until != DateTime.MinValue && m.Created > until) continue;
                    if (before > 0 && m.Id >= before) continue;
                    if (string.IsNullOrEmpty(m.Body)) continue;
                    if (m.Body.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    if (result.Count >= limit) { hasMore = true; break; }
                    result.Add(m);
                }
            }
            return result;
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
                    if (!IsLive(p)) continue;
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

        // ============================================================ drafts

        public List<Draft> DraftsFor(long userId)
        {
            List<Draft> list = new List<Draft>();
            lock (_sync)
            {
                for (int i = 0; i < _drafts.Count; i++)
                    if (_drafts[i].User == userId) list.Add(_drafts[i]);
            }
            list.Sort(delegate(Draft a, Draft b) { return b.Updated.CompareTo(a.Updated); });
            return list;
        }

        public Draft FindDraft(long id, long userId)
        {
            lock (_sync)
            {
                for (int i = 0; i < _drafts.Count; i++)
                    if (_drafts[i].Id == id && _drafts[i].User == userId) return _drafts[i];
                return null;
            }
        }

        /// <summary>Creates a draft, or updates the one with that id when it exists.</summary>
        public Draft SaveDraft(long id, long userId, string body, string video, string visibility,
                               long wall, long group, string pollQuestion, string pollOptions)
        {
            lock (_sync)
            {
                EnsureWritable();
                Draft x = id > 0 ? FindDraft(id, userId) : null;
                if (x == null)
                {
                    x = new Draft();
                    x.Id = _meta.NextDraftId++;
                    x.User = userId;
                    x.Created = DateTime.UtcNow;
                    _drafts.Add(x);
                }
                x.Body = body ?? "";
                x.Video = video ?? "";
                x.Visibility = visibility ?? "public";
                x.Wall = wall;
                x.Group = group;
                x.PollQuestion = pollQuestion ?? "";
                x.PollOptions = pollOptions ?? "";
                x.Updated = DateTime.UtcNow;
                MarkDrafts();
                _dirtyMeta = true;
                return x;
            }
        }

        public bool DeleteDraft(long id, long userId)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _drafts.Count; i++)
                {
                    if (_drafts[i].Id != id || _drafts[i].User != userId) continue;
                    _drafts.RemoveAt(i);
                    MarkDrafts();
                    return true;
                }
                return false;
            }
        }

        public int CountDrafts(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _drafts.Count; i++) if (_drafts[i].User == userId) n++;
                return n;
            }
        }

        // ======================================================== media library

        public List<MediaItem> MediaFor(long userId, string kind)
        {
            List<MediaItem> list = new List<MediaItem>();
            lock (_sync)
            {
                for (int i = 0; i < _media.Count; i++)
                {
                    MediaItem m = _media[i];
                    if (userId > 0 && m.Owner != userId) continue;
                    if (!string.IsNullOrEmpty(kind) && m.Kind != kind) continue;
                    list.Add(m);
                }
            }
            list.Sort(delegate(MediaItem a, MediaItem b) { return b.Id.CompareTo(a.Id); });
            return list;
        }

        public MediaItem FindMedia(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _media.Count; i++) if (_media[i].Id == id) return _media[i];
                return null;
            }
        }

        public bool UpdateMedia(long id, long owner, string title, string artist, string description)
        {
            lock (_sync)
            {
                EnsureWritable();
                MediaItem m = FindMedia(id);
                if (m == null || m.Owner != owner) return false;
                if (m.Kind == "game") return false;   // games have their own endpoint
                if (title != null) m.Title = Util.SingleLine(title, 120);
                if (artist != null) m.Artist = Util.SingleLine(artist, 120);
                if (description != null) m.Description = Util.CleanText(description, 2000);
                MarkMedia();
                return true;
            }
        }

        public int CountMedia(long userId, string kind)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _media.Count; i++)
                    if (_media[i].Owner == userId && _media[i].Kind == kind) n++;
                return n;
            }
        }

        public MediaItem AddMedia(long owner, string kind, string title, string artist, string url, long size, int duration)
        {
            lock (_sync)
            {
                EnsureWritable();
                MediaItem m = new MediaItem();
                m.Id = _meta.NextMediaId++;
                m.Owner = owner;
                m.Kind = kind == "video" ? "video" : kind == "game" ? "game" : "audio";
                m.Title = Util.SingleLine(title ?? "", 120);
                m.Artist = Util.SingleLine(artist ?? "", 120);
                m.Url = url ?? "";
                m.Size = size;
                m.Duration = duration < 0 ? 0 : duration;
                m.Created = DateTime.UtcNow;
                _media.Add(m);
                MarkMedia();
                _dirtyMeta = true;
                return m;
            }
        }

        /// <summary>Removes an entry; returns its url so the caller can delete the file.</summary>
        public string RemoveMedia(long id, long owner)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _media.Count; i++)
                {
                    if (_media[i].Id != id || _media[i].Owner != owner) continue;
                    string url = _media[i].Url;
                    _media.RemoveAt(i);
                    MarkMedia();
                    return url;
                }
                return null;
            }
        }

        /// <summary>
        /// Adds somebody else's item to the caller's shelf. The record points at the
        /// same file; deletion only removes the file once nobody references it.
        /// </summary>
        public MediaItem SaveMediaTo(long id, long owner)
        {
            lock (_sync)
            {
                EnsureWritable();
                MediaItem src = FindMedia(id);
                if (src == null) return null;
                for (int i = 0; i < _media.Count; i++)
                    if (_media[i].Owner == owner && _media[i].Url == src.Url) return null;  // already saved

                MediaItem copy = new MediaItem();
                copy.Id = _meta.NextMediaId++;
                copy.Owner = owner;
                copy.Kind = src.Kind;
                copy.Title = src.Title;
                copy.Artist = src.Artist;
                copy.Url = src.Url;
                copy.Size = src.Size;
                copy.Duration = src.Duration;
                copy.SavedFrom = src.SavedFrom > 0 ? src.SavedFrom : src.Owner;
                copy.Created = DateTime.UtcNow;
                _media.Add(copy);
                MarkMedia();
                _dirtyMeta = true;
                return copy;
            }
        }

        /// <summary>True while any item still points at that file.</summary>
        public bool IsMediaUrlUsed(string url)        {
            if (string.IsNullOrEmpty(url)) return false;
            lock (_sync)
            {
                for (int i = 0; i < _media.Count; i++) if (_media[i].Url == url) return true;
                return false;
            }
        }

        // ============================================================== albums

        public Album CreateAlbum(long owner, string name, string description)
        {
            lock (_sync)
            {
                EnsureWritable();
                Album a = new Album();
                a.Id = _meta.NextAlbumId++;
                a.Owner = owner;
                a.Name = Util.SingleLine(name, 60);
                a.Description = Util.CleanText(description, 500);
                a.Created = DateTime.UtcNow;
                a.Updated = a.Created;
                _albums.Add(a);
                MarkAlbums();
                _dirtyMeta = true;
                return a;
            }
        }

        public Album FindAlbum(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _albums.Count; i++) if (_albums[i].Id == id) return _albums[i];
                return null;
            }
        }

        /// <summary>Albums of one person, newest activity first.</summary>
        public List<Album> AlbumsFor(long owner)
        {
            List<Album> list = new List<Album>();
            lock (_sync)
            {
                for (int i = 0; i < _albums.Count; i++) if (_albums[i].Owner == owner) list.Add(_albums[i]);
            }
            list.Sort(delegate(Album a, Album b) { return b.Updated.CompareTo(a.Updated); });
            return list;
        }

        public int CountAlbums(long owner)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _albums.Count; i++) if (_albums[i].Owner == owner) n++;
                return n;
            }
        }

        public bool UpdateAlbum(long id, long owner, string name, string description)
        {
            lock (_sync)
            {
                EnsureWritable();
                Album a = FindAlbum(id);
                if (a == null || a.Owner != owner) return false;
                if (!string.IsNullOrEmpty(name)) a.Name = Util.SingleLine(name, 60);
                if (description != null) a.Description = Util.CleanText(description, 500);
                a.Updated = DateTime.UtcNow;
                MarkAlbums();
                return true;
            }
        }

        public bool SetAlbumCover(long id, long owner, string url)
        {
            lock (_sync)
            {
                EnsureWritable();
                Album a = FindAlbum(id);
                if (a == null || a.Owner != owner) return false;
                a.Cover = url ?? "";
                a.Updated = DateTime.UtcNow;
                MarkAlbums();
                return true;
            }
        }

        /// <summary>Deletes the album; the photos stay on the wall, only the grouping goes.</summary>
        public bool DeleteAlbum(long id, long owner)
        {
            lock (_sync)
            {
                EnsureWritable();
                Album a = FindAlbum(id);
                if (a == null || a.Owner != owner) return false;
                _albums.Remove(a);
                for (int i = 0; i < _posts.Count; i++)
                    if (_posts[i].Album == id) _posts[i].Album = 0;
                MarkAlbums();
                MarkPosts();
                return true;
            }
        }

        /// <summary>Puts a photo into an album (0 = take it out). Only the photo's author may move it.</summary>
        public bool AssignPhoto(long postId, long albumId, long owner)
        {
            lock (_sync)
            {
                EnsureWritable();
                Post p = FindPost(postId);
                if (p == null || p.Deleted || p.Author != owner || string.IsNullOrEmpty(p.Image)) return false;
                if (albumId > 0)
                {
                    Album a = FindAlbum(albumId);
                    if (a == null || a.Owner != owner) return false;
                    a.Updated = DateTime.UtcNow;
                    MarkAlbums();
                }
                p.Album = albumId;
                MarkPosts();
                return true;
            }
        }

        /// <summary>Photos of one album, newest first.</summary>
        public List<Post> AlbumPhotos(long albumId, long before, int limit)
        {
            List<Post> result = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count && result.Count < limit; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Album != albumId || string.IsNullOrEmpty(p.Image)) continue;
                    if (!IsLive(p)) continue;
                    if (before > 0 && p.Id >= before) continue;
                    result.Add(p);
                }
            }
            return result;
        }

        public int CountAlbumPhotos(long albumId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Album != albumId || string.IsNullOrEmpty(p.Image)) continue;
                    n++;
                }
                return n;
            }
        }

        /// <summary>First picture of an album, used as a fallback cover.</summary>
        public string AlbumCover(Album a)
        {
            if (a == null) return "";
            if (!string.IsNullOrEmpty(a.Cover)) return a.Cover;
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Album != a.Id || string.IsNullOrEmpty(p.Image)) continue;
                    return p.Image;
                }
            }
            return "";
        }

        private List<object> AlbumsToArray()
        {
            List<object> list = new List<object>(_albums.Count);
            for (int i = 0; i < _albums.Count; i++)
            {
                Album a = _albums[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = a.Id; d["owner"] = a.Owner; d["name"] = a.Name;
                d["description"] = a.Description; d["cover"] = a.Cover;
                d["created"] = a.Created; d["updated"] = a.Updated;
                list.Add(d);
            }
            return list;
        }

        private static Album AlbumFromDict(Dictionary<string, object> d)
        {
            Album a = new Album();
            a.Id = Json.GetLong(d, "id", 0);
            a.Owner = Json.GetLong(d, "owner", 0);
            a.Name = Json.GetString(d, "name", "");
            a.Description = Json.GetString(d, "description", "");
            a.Cover = Json.GetString(d, "cover", "");
            a.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            a.Updated = Json.GetDate(d, "updated", a.Created);
            if (a.Id <= 0 || a.Owner <= 0) return null;
            return a;
        }

        // ============================================================== games

        /// <summary>
        /// The games shelf: newest first, optionally filtered by a search string
        /// and by owner. Paged with an id cursor so the client can "load more".
        /// </summary>
        public List<MediaItem> Games(long owner, string query, long before, int limit, out bool hasMore)
        {
            List<MediaItem> list = new List<MediaItem>();
            hasMore = false;
            string needle = (query ?? "").Trim().ToLowerInvariant();
            lock (_sync)
            {
                for (int i = 0; i < _media.Count; i++)
                {
                    MediaItem m = _media[i];
                    if (m.Kind != "game") continue;
                    if (owner > 0 && m.Owner != owner) continue;
                    if (before > 0 && m.Id >= before) continue;
                    if (needle.Length > 0)
                    {
                        bool hit = m.Title.ToLowerInvariant().Contains(needle) ||
                                   m.Description.ToLowerInvariant().Contains(needle) ||
                                   m.Artist.ToLowerInvariant().Contains(needle);
                        if (!hit) continue;
                    }
                    if (list.Count >= limit) { hasMore = true; break; }
                    list.Add(m);
                }
            }
            return list;
        }

        public int CountGames()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _media.Count; i++) if (_media[i].Kind == "game") n++;
                return n;
            }
        }

        public MediaItem AddGame(long owner, string title, string description, string url, long size)
        {
            MediaItem m = AddMedia(owner, "game", title, "", url, size, 0);
            if (m == null) return null;
            lock (_sync)
            {
                m.Description = description ?? "";
                MarkMedia();
                return m;
            }
        }

        /// <summary>Renames a game; only the owner may do it.</summary>
        public bool UpdateGame(long id, long owner, string title, string description)
        {
            lock (_sync)
            {
                EnsureWritable();
                MediaItem m = FindMedia(id);
                if (m == null || m.Owner != owner || m.Kind != "game") return false;
                if (title != null) m.Title = Util.SingleLine(title, 120);
                if (description != null) m.Description = Util.CleanText(description, 1000);
                MarkMedia();
                return true;
            }
        }

        public bool SetGameCover(long id, long owner, string url)
        {
            lock (_sync)
            {
                EnsureWritable();
                MediaItem m = FindMedia(id);
                if (m == null || m.Kind != "game") return false;
                if (m.Owner != owner) return false;
                m.Cover = url ?? "";
                MarkMedia();
                return true;
            }
        }

        /// <summary>Counts one play. Cheap on purpose: no notification, no history.</summary>
        public int AddGamePlay(long id)
        {
            lock (_sync)
            {
                MediaItem m = FindMedia(id);
                if (m == null || m.Kind != "game") return -1;
                m.Plays++;
                MarkMedia();
                return m.Plays;
            }
        }

        /// <summary>Toggles the caller's like; returns the new count, -1 when missing.</summary>
        public int ToggleGameLike(long id, long userId, out bool liked)
        {
            liked = false;
            lock (_sync)
            {
                EnsureWritable();
                MediaItem m = FindMedia(id);
                if (m == null || m.Kind != "game") return -1;
                if (m.Likes.Contains(userId)) m.Likes.Remove(userId);
                else { m.Likes.Add(userId); liked = true; }
                MarkMedia();
                return m.Likes.Count;
            }
        }

        // ============================================================ playlists

        public List<Playlist> PlaylistsFor(long userId)
        {
            List<Playlist> list = new List<Playlist>();
            lock (_sync)
            {
                for (int i = 0; i < _playlists.Count; i++)
                    if (_playlists[i].Owner == userId) list.Add(_playlists[i]);
            }
            list.Sort(delegate(Playlist a, Playlist b) { return b.Updated.CompareTo(a.Updated); });
            return list;
        }

        public Playlist FindPlaylist(long id, long owner)
        {
            lock (_sync)
            {
                for (int i = 0; i < _playlists.Count; i++)
                    if (_playlists[i].Id == id && _playlists[i].Owner == owner) return _playlists[i];
                return null;
            }
        }

        public Playlist SavePlaylist(long id, long owner, string name, List<long> items)
        {
            lock (_sync)
            {
                EnsureWritable();
                Playlist p = id > 0 ? FindPlaylist(id, owner) : null;
                if (p == null)
                {
                    p = new Playlist();
                    p.Id = _meta.NextPlaylistId++;
                    p.Owner = owner;
                    p.Created = DateTime.UtcNow;
                    _playlists.Add(p);
                }
                p.Name = Util.SingleLine(name ?? "", 80);
                if (p.Name.Length == 0) p.Name = "Playlist";
                p.Items = new List<long>();
                if (items != null)
                {
                    for (int i = 0; i < items.Count && p.Items.Count < 200; i++)
                        if (items[i] > 0 && !p.Items.Contains(items[i])) p.Items.Add(items[i]);
                }
                p.Updated = DateTime.UtcNow;
                MarkPlaylists();
                _dirtyMeta = true;
                return p;
            }
        }

        public bool DeletePlaylist(long id, long owner)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _playlists.Count; i++)
                {
                    if (_playlists[i].Id != id || _playlists[i].Owner != owner) continue;
                    _playlists.RemoveAt(i);
                    MarkPlaylists();
                    return true;
                }
                return false;
            }
        }

        /// <summary>Adds, removes or drops a deleted track from every playlist.</summary>
        public bool PlaylistAddItem(long id, long owner, long mediaId, bool add)
        {
            lock (_sync)
            {
                EnsureWritable();
                Playlist p = FindPlaylist(id, owner);
                if (p == null) return false;
                if (add)
                {
                    if (p.Items.Contains(mediaId) || p.Items.Count >= 200) return true;
                    p.Items.Add(mediaId);
                }
                else p.Items.Remove(mediaId);
                p.Updated = DateTime.UtcNow;
                MarkPlaylists();
                return true;
            }
        }

        private List<object> PlaylistsToArray()
        {
            List<object> list = new List<object>(_playlists.Count);
            for (int i = 0; i < _playlists.Count; i++)
            {
                Playlist p = _playlists[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = p.Id; d["owner"] = p.Owner; d["name"] = p.Name;
                List<object> ids = new List<object>(p.Items.Count);
                for (int k = 0; k < p.Items.Count; k++) ids.Add(p.Items[k]);
                d["items"] = ids;
                d["created"] = p.Created; d["updated"] = p.Updated;
                list.Add(d);
            }
            return list;
        }

        private static Playlist PlaylistFromDict(Dictionary<string, object> d)
        {
            Playlist p = new Playlist();
            p.Id = Json.GetLong(d, "id", 0);
            p.Owner = Json.GetLong(d, "owner", 0);
            p.Name = Json.GetString(d, "name", "");
            p.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            p.Updated = Json.GetDate(d, "updated", p.Created);
            List<object> ids = Json.Get(d, "items") as List<object>;
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    long id;
                    if (ids[i] is long) id = (long)ids[i];
                    else if (!long.TryParse(Convert.ToString(ids[i]), out id)) continue;
                    if (id > 0) p.Items.Add(id);
                }
            }
            if (p.Id <= 0 || p.Owner <= 0) return null;
            return p;
        }

        private List<object> MediaToArray()
        {
            List<object> list = new List<object>(_media.Count);
            for (int i = 0; i < _media.Count; i++)
            {
                MediaItem m = _media[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = m.Id; d["owner"] = m.Owner; d["kind"] = m.Kind;
                d["title"] = m.Title; d["artist"] = m.Artist; d["url"] = m.Url;
                d["size"] = m.Size; d["duration"] = m.Duration; d["created"] = m.Created;
                if (m.SavedFrom > 0) d["savedFrom"] = m.SavedFrom;
                if (m.Kind == "game")
                {
                    d["description"] = m.Description;
                    d["plays"] = m.Plays;
                    if (!string.IsNullOrEmpty(m.Cover)) d["cover"] = m.Cover;
                    List<object> likes = new List<object>(m.Likes.Count);
                    for (int k = 0; k < m.Likes.Count; k++) likes.Add(m.Likes[k]);
                    d["likes"] = likes;
                }
                list.Add(d);
            }
            return list;
        }

        private static MediaItem MediaFromDict(Dictionary<string, object> d)
        {
            MediaItem m = new MediaItem();
            m.Id = Json.GetLong(d, "id", 0);
            m.Owner = Json.GetLong(d, "owner", 0);
            m.Kind = Json.GetString(d, "kind", "audio");
            m.Title = Json.GetString(d, "title", "");
            m.Artist = Json.GetString(d, "artist", "");
            m.Url = Json.GetString(d, "url", "");
            m.Size = Json.GetLong(d, "size", 0);
            m.Duration = Json.GetInt(d, "duration", 0);
            m.SavedFrom = Json.GetLong(d, "savedFrom", 0);
            m.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            m.Description = Json.GetString(d, "description", "");
            m.Plays = Json.GetInt(d, "plays", 0);
            m.Cover = Json.GetString(d, "cover", "");
            List<object> likes = Json.Get(d, "likes") as List<object>;
            if (likes != null)
            {
                for (int i = 0; i < likes.Count; i++)
                {
                    long id;
                    if (likes[i] is long) id = (long)likes[i];
                    else if (!long.TryParse(Convert.ToString(likes[i]), out id)) continue;
                    if (id > 0) m.Likes.Add(id);
                }
            }
            if (m.Id <= 0 || m.Owner <= 0 || string.IsNullOrEmpty(m.Url)) return null;
            return m;
        }

        // =========================================================== scheduling

        /// <summary>True when the post is not waiting for its publication time.</summary>
        public static bool IsLive(Post p)
        {
            return p != null && (p.ScheduledFor == DateTime.MinValue || p.ScheduledFor <= DateTime.UtcNow);
        }

        /// <summary>Posts of the user that are waiting for their time.</summary>
        public List<Post> ScheduledFor(long userId)
        {
            List<Post> list = new List<Post>();
            lock (_sync)
            {
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted || p.Author != userId) continue;
                    if (p.ScheduledFor == DateTime.MinValue || p.ScheduledFor <= DateTime.UtcNow) continue;
                    list.Add(p);
                }
            }
            list.Sort(delegate(Post a, Post b) { return a.ScheduledFor.CompareTo(b.ScheduledFor); });
            return list;
        }

        /// <summary>
        /// Publishes everything whose time has come. Returns the ids that went live so
        /// the caller can log them; safe to call often.
        /// </summary>
        public List<long> PublishDue()
        {
            List<long> published = new List<long>();
            lock (_sync)
            {
                if (_posts.Count == 0) return published;
                DateTime now = DateTime.UtcNow;
                bool changed = false;
                for (int i = 0; i < _posts.Count; i++)
                {
                    Post p = _posts[i];
                    if (p.Deleted) continue;
                    if (p.ScheduledFor == DateTime.MinValue || p.ScheduledFor > now) continue;
                    p.ScheduledFor = DateTime.MinValue;
                    published.Add(p.Id);
                    changed = true;
                }
                if (changed) MarkPosts();
            }
            return published;
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

        // =============================================================== calls

        public long NextCallId()
        {
            lock (_sync)
            {
                long id = _meta.NextCallId++;
                _dirtyMeta = true;
                return id;
            }
        }

        public CallRecord AddCallRecord(long id, string kind, string room, long initiator, List<long> participants)
        {
            lock (_sync)
            {
                EnsureWritable();
                CallRecord r = new CallRecord();
                r.Id = id;
                r.Kind = kind;
                r.Room = room ?? "";
                r.Initiator = initiator;
                r.Participants = new List<long>(participants);
                r.Started = DateTime.UtcNow;
                _calls.Insert(0, r);
                if (_calls.Count > 500) _calls.RemoveRange(500, _calls.Count - 500);
                MarkCalls();
                return r;
            }
        }

        public void EndCallRecord(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _calls.Count; i++)
                {
                    if (_calls[i].Id != id) continue;
                    _calls[i].Ended = DateTime.UtcNow;
                    MarkCalls();
                    return;
                }
            }
        }

        /// <summary>Recent calls the user took part in.</summary>
        public List<CallRecord> CallsFor(long userId, int limit)
        {
            List<CallRecord> list = new List<CallRecord>();
            lock (_sync)
            {
                for (int i = 0; i < _calls.Count && list.Count < limit; i++)
                    if (_calls[i].Participants.Contains(userId)) list.Add(_calls[i]);
            }
            return list;
        }

        private List<object> CallsToArray()
        {
            List<object> list = new List<object>(_calls.Count);
            for (int i = 0; i < _calls.Count; i++)
            {
                CallRecord r = _calls[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = r.Id; d["kind"] = r.Kind; d["room"] = r.Room; d["initiator"] = r.Initiator;
                List<object> ids = new List<object>(r.Participants.Count);
                for (int k = 0; k < r.Participants.Count; k++) ids.Add(r.Participants[k]);
                d["participants"] = ids;
                d["started"] = r.Started; d["ended"] = r.Ended;
                list.Add(d);
            }
            return list;
        }

        private static CallRecord CallRecordFromDict(Dictionary<string, object> d)
        {
            CallRecord r = new CallRecord();
            r.Id = Json.GetLong(d, "id", 0);
            r.Kind = Json.GetString(d, "kind", "audio");
            r.Room = Json.GetString(d, "room", "");
            r.Initiator = Json.GetLong(d, "initiator", 0);
            r.Started = Json.GetDate(d, "started", DateTime.UtcNow);
            r.Ended = Json.GetDate(d, "ended", DateTime.MinValue);
            List<object> ids = Json.Get(d, "participants") as List<object>;
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    long id;
                    if (ids[i] is long) id = (long)ids[i];
                    else if (!long.TryParse(Convert.ToString(ids[i]), out id)) continue;
                    if (id > 0) r.Participants.Add(id);
                }
            }
            if (r.Id <= 0 || r.Initiator <= 0) return null;
            return r;
        }

        // ============================================================= stories

        /// <summary>Live stories of one author, oldest first (that is the play order).</summary>
        public List<Story> StoriesOf(long author, bool includeExpired)
        {
            List<Story> list = new List<Story>();
            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                for (int i = 0; i < _stories.Count; i++)
                {
                    Story s = _stories[i];
                    if (s.Author != author) continue;
                    if (!includeExpired && s.Expires <= now) continue;
                    list.Add(s);
                }
            }
            list.Sort(delegate(Story a, Story b) { return a.Id.CompareTo(b.Id); });
            return list;
        }

        /// <summary>Authors who have at least one live story right now.</summary>
        public List<long> StoryAuthors()
        {
            List<long> authors = new List<long>();
            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                for (int i = 0; i < _stories.Count; i++)
                {
                    Story s = _stories[i];
                    if (s.Expires <= now) continue;
                    if (!authors.Contains(s.Author)) authors.Add(s.Author);
                }
            }
            return authors;
        }

        public Story FindStory(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _stories.Count; i++) if (_stories[i].Id == id) return _stories[i];
                return null;
            }
        }

        /// <summary>How many stories the author posted in the last 24 hours.</summary>
        public int CountStoriesToday(long author)
        {
            DateTime since = DateTime.UtcNow.AddHours(-24);
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _stories.Count; i++)
                    if (_stories[i].Author == author && _stories[i].Created >= since) n++;
                return n;
            }
        }

        public Story AddStory(long author, string image, string video, string caption, int hours)
        {
            lock (_sync)
            {
                EnsureWritable();
                Story s = new Story();
                s.Id = _meta.NextStoryId++;
                s.Author = author;
                s.Image = image ?? "";
                s.Video = video ?? "";
                s.Caption = caption ?? "";
                s.Created = DateTime.UtcNow;
                s.Expires = s.Created.AddHours(hours);
                _stories.Add(s);
                MarkStories();
                _dirtyMeta = true;
                return s;
            }
        }

        /// <summary>Records that the viewer saw the story; a repeat view changes nothing.</summary>
        public bool MarkStorySeen(long id, long viewer)
        {
            lock (_sync)
            {
                Story s = FindStory(id);
                if (s == null || viewer <= 0) return false;
                if (s.Author == viewer || s.Viewers.Contains(viewer)) return false;
                s.Viewers.Add(viewer);
                MarkStories();
                return true;
            }
        }

        /// <summary>Removes a story; returns the file urls so the caller can delete them.</summary>
        public List<string> RemoveStory(long id, long owner)
        {
            List<string> urls = new List<string>();
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _stories.Count; i++)
                {
                    Story s = _stories[i];
                    if (s.Id != id || s.Author != owner) continue;
                    if (!string.IsNullOrEmpty(s.Image)) urls.Add(s.Image);
                    if (!string.IsNullOrEmpty(s.Video)) urls.Add(s.Video);
                    _stories.RemoveAt(i);
                    MarkStories();
                    break;
                }
            }
            return urls;
        }

        /// <summary>
        /// Drops everything that expired and hands back the files to delete. Called
        /// by the supervisor, so a story really does go away on time.
        /// </summary>
        public List<string> PurgeExpiredStories()
        {
            List<string> urls = new List<string>();
            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                for (int i = _stories.Count - 1; i >= 0; i--)
                {
                    Story s = _stories[i];
                    if (s.Expires > now) continue;
                    if (!string.IsNullOrEmpty(s.Image)) urls.Add(s.Image);
                    if (!string.IsNullOrEmpty(s.Video)) urls.Add(s.Video);
                    _stories.RemoveAt(i);
                }
                if (urls.Count > 0) MarkStories();
            }
            return urls;
        }

        /// <summary>Every story the author ever posted, live or expired.</summary>
        public int CountStoriesBy(long author)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _stories.Count; i++) if (_stories[i].Author == author) n++;
                return n;
            }
        }

        public int CountLiveStories()        {
            DateTime now = DateTime.UtcNow;
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _stories.Count; i++) if (_stories[i].Expires > now) n++;
                return n;
            }
        }

        private List<object> StoriesToArray()
        {
            List<object> list = new List<object>(_stories.Count);
            for (int i = 0; i < _stories.Count; i++)
            {
                Story s = _stories[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = s.Id; d["author"] = s.Author;
                d["image"] = s.Image; d["video"] = s.Video; d["caption"] = s.Caption;
                d["created"] = s.Created; d["expires"] = s.Expires;
                List<object> viewers = new List<object>(s.Viewers.Count);
                for (int k = 0; k < s.Viewers.Count; k++) viewers.Add(s.Viewers[k]);
                d["viewers"] = viewers;
                list.Add(d);
            }
            return list;
        }

        private static Story StoryFromDict(Dictionary<string, object> d)
        {
            Story s = new Story();
            s.Id = Json.GetLong(d, "id", 0);
            s.Author = Json.GetLong(d, "author", 0);
            s.Image = Json.GetString(d, "image", "");
            s.Video = Json.GetString(d, "video", "");
            s.Caption = Json.GetString(d, "caption", "");
            s.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            s.Expires = Json.GetDate(d, "expires", s.Created.AddHours(24));
            ReadIds(d, "viewers", s.Viewers);
            if (s.Id <= 0 || s.Author <= 0) return null;
            if (string.IsNullOrEmpty(s.Image) && string.IsNullOrEmpty(s.Video)) return null;
            return s;
        }

        // =============================================================== gifts

        public Gift AddGift(long from, long to, string kind, string message, bool anonymous)
        {
            lock (_sync)
            {
                EnsureWritable();
                Gift g = new Gift();
                g.Id = _meta.NextGiftId++;
                g.From = from;
                g.To = to;
                g.Kind = kind;
                g.Message = message ?? "";
                g.Anonymous = anonymous;
                g.Created = DateTime.UtcNow;
                _gifts.Insert(0, g);
                if (_gifts.Count > 5000) _gifts.RemoveRange(5000, _gifts.Count - 5000);
                MarkGifts();
                _dirtyMeta = true;
                return g;
            }
        }

        /// <summary>Gifts the user received, newest first, hidden ones skipped.</summary>
        public List<Gift> GiftsTo(long userId, long before, int limit, out bool hasMore)
        {
            List<Gift> list = new List<Gift>();
            hasMore = false;
            lock (_sync)
            {
                for (int i = 0; i < _gifts.Count; i++)
                {
                    Gift g = _gifts[i];
                    if (g.To != userId || g.Hidden) continue;
                    if (before > 0 && g.Id >= before) continue;
                    if (list.Count >= limit) { hasMore = true; break; }
                    list.Add(g);
                }
            }
            return list;
        }

        /// <summary>Gifts the user sent, newest first.</summary>
        public List<Gift> GiftsFrom(long userId, long before, int limit, out bool hasMore)
        {
            List<Gift> list = new List<Gift>();
            hasMore = false;
            lock (_sync)
            {
                for (int i = 0; i < _gifts.Count; i++)
                {
                    Gift g = _gifts[i];
                    if (g.From != userId) continue;
                    if (before > 0 && g.Id >= before) continue;
                    if (list.Count >= limit) { hasMore = true; break; }
                    list.Add(g);
                }
            }
            return list;
        }

        public int CountGifts(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _gifts.Count; i++) if (_gifts[i].To == userId && !_gifts[i].Hidden) n++;
                return n;
            }
        }

        /// <summary>Gifts the user gave away, hidden ones included.</summary>
        public int CountGiftsSent(long userId)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _gifts.Count; i++) if (_gifts[i].From == userId) n++;
                return n;
            }
        }

        public int CountUnseenGifts(long userId)        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _gifts.Count; i++)
                    if (_gifts[i].To == userId && !_gifts[i].Hidden && !_gifts[i].Seen) n++;
                return n;
            }
        }

        /// <summary>How many gifts of one kind the user got, for the profile counter.</summary>
        public int CountGiftsOfKind(long userId, string kind)
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _gifts.Count; i++)
                    if (_gifts[i].To == userId && !_gifts[i].Hidden && _gifts[i].Kind == kind) n++;
                return n;
            }
        }

        public Gift FindGift(long id)
        {
            lock (_sync)
            {
                for (int i = 0; i < _gifts.Count; i++) if (_gifts[i].Id == id) return _gifts[i];
                return null;
            }
        }

        /// <summary>Marks one gift (or every gift) of this user as seen.</summary>
        public bool MarkGiftSeen(long userId, long id)
        {
            lock (_sync)
            {
                bool any = false;
                for (int i = 0; i < _gifts.Count; i++)
                {
                    Gift g = _gifts[i];
                    if (g.To != userId || g.Seen) continue;
                    if (id > 0 && g.Id != id) continue;
                    g.Seen = true;
                    any = true;
                }
                if (any) MarkGifts();
                return any;
            }
        }

        /// <summary>The recipient takes a gift off their wall.</summary>
        public bool HideGift(long userId, long id)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _gifts.Count; i++)
                {
                    if (_gifts[i].Id != id || _gifts[i].To != userId) continue;
                    _gifts[i].Hidden = true;
                    MarkGifts();
                    return true;
                }
                return false;
            }
        }

        /// <summary>Drops every gift sent to or by an account that is being deleted.</summary>
        public int PurgeGiftsOf(long userId)
        {
            lock (_sync)
            {
                int n = _gifts.RemoveAll(delegate(Gift g) { return g.To == userId || g.From == userId; });
                if (n > 0) MarkGifts();
                return n;
            }
        }

        private List<object> GiftsToArray()
        {
            List<object> list = new List<object>(_gifts.Count);
            for (int i = 0; i < _gifts.Count; i++)
            {
                Gift g = _gifts[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = g.Id; d["from"] = g.From; d["to"] = g.To; d["kind"] = g.Kind;
                d["message"] = g.Message; d["anonymous"] = g.Anonymous;
                d["created"] = g.Created; d["seen"] = g.Seen; d["hidden"] = g.Hidden;
                list.Add(d);
            }
            return list;
        }

        private static Gift GiftFromDict(Dictionary<string, object> d)
        {
            Gift g = new Gift();
            g.Id = Json.GetLong(d, "id", 0);
            g.From = Json.GetLong(d, "from", 0);
            g.To = Json.GetLong(d, "to", 0);
            g.Kind = Json.GetString(d, "kind", "heart");
            g.Message = Json.GetString(d, "message", "");
            g.Anonymous = Json.GetBool(d, "anonymous", false);
            g.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            g.Seen = Json.GetBool(d, "seen", false);
            g.Hidden = Json.GetBool(d, "hidden", false);
            if (g.Id <= 0 || g.To <= 0) return null;
            return g;
        }

        // ============================================================= feedback

        public Feedback AddFeedback(string kind, string message, string contact, string page,
                                    string app, string ipHash)
        {
            lock (_sync)
            {
                EnsureWritable();
                Feedback f = new Feedback();
                f.Id = _meta.NextFeedbackId++;
                f.Kind = kind;
                f.Message = message ?? "";
                f.Contact = contact ?? "";
                f.Page = page ?? "";
                f.App = app ?? "";
                f.IpHash = ipHash ?? "";
                f.Created = DateTime.UtcNow;
                f.Status = "new";
                _feedback.Insert(0, f);
                MarkFeedback();
                _dirtyMeta = true;
                return f;
            }
        }

        public List<Feedback> FeedbackList(string status, int limit)
        {
            List<Feedback> list = new List<Feedback>();
            lock (_sync)
            {
                for (int i = 0; i < _feedback.Count && list.Count < limit; i++)
                {
                    if (!string.IsNullOrEmpty(status) && _feedback[i].Status != status) continue;
                    list.Add(_feedback[i]);
                }
            }
            return list;
        }

        public int CountNewFeedback()
        {
            lock (_sync)
            {
                int n = 0;
                for (int i = 0; i < _feedback.Count; i++) if (_feedback[i].Status == "new") n++;
                return n;
            }
        }

        public bool UpdateFeedback(long id, string status, string note, long adminId)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _feedback.Count; i++)
                {
                    if (_feedback[i].Id != id) continue;
                    if (!string.IsNullOrEmpty(status)) _feedback[i].Status = status;
                    if (note != null) _feedback[i].Note = note;
                    _feedback[i].HandledBy = adminId;
                    _feedback[i].HandledAt = DateTime.UtcNow;
                    MarkFeedback();
                    return true;
                }
                return false;
            }
        }

        public bool DeleteFeedback(long id)
        {
            lock (_sync)
            {
                EnsureWritable();
                for (int i = 0; i < _feedback.Count; i++)
                {
                    if (_feedback[i].Id != id) continue;
                    _feedback.RemoveAt(i);
                    MarkFeedback();
                    return true;
                }
                return false;
            }
        }

        private List<object> FeedbackToArray()
        {
            List<object> list = new List<object>(_feedback.Count);
            for (int i = 0; i < _feedback.Count; i++)
            {
                Feedback f = _feedback[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = f.Id; d["kind"] = f.Kind; d["message"] = f.Message;
                d["contact"] = f.Contact; d["page"] = f.Page; d["app"] = f.App;
                d["ipHash"] = f.IpHash; d["created"] = f.Created; d["status"] = f.Status;
                d["note"] = f.Note; d["handledBy"] = f.HandledBy; d["handledAt"] = f.HandledAt;
                list.Add(d);
            }
            return list;
        }

        private static Feedback FeedbackFromDict(Dictionary<string, object> d)
        {
            Feedback f = new Feedback();
            f.Id = Json.GetLong(d, "id", 0);
            f.Kind = Json.GetString(d, "kind", "other");
            f.Message = Json.GetString(d, "message", "");
            f.Contact = Json.GetString(d, "contact", "");
            f.Page = Json.GetString(d, "page", "");
            f.App = Json.GetString(d, "app", "");
            f.IpHash = Json.GetString(d, "ipHash", "");
            f.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            f.Status = Json.GetString(d, "status", "new");
            f.Note = Json.GetString(d, "note", "");
            f.HandledBy = Json.GetLong(d, "handledBy", 0);
            f.HandledAt = Json.GetDate(d, "handledAt", DateTime.MinValue);
            if (f.Id <= 0) return null;
            return f;
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

                // gifts sent or received
                removed += PurgeGiftsOf(userId);

                // albums owned by the user (their photos go with the posts)
                for (int a = _albums.Count - 1; a >= 0; a--)
                    if (_albums[a].Owner == userId) _albums.RemoveAt(a);

                // stories go with the account
                for (int s = _stories.Count - 1; s >= 0; s--)
                    if (_stories[s].Author == userId) _stories.RemoveAt(s);

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
                MarkAlbums();
                MarkStories();
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
            d["nextDraftId"] = m.NextDraftId;
            d["nextMediaId"] = m.NextMediaId;
            d["nextPlaylistId"] = m.NextPlaylistId;
            d["nextFeedbackId"] = m.NextFeedbackId;
            d["nextCallId"] = m.NextCallId;
            d["nextGiftId"] = m.NextGiftId;
            d["nextAlbumId"] = m.NextAlbumId;
            d["nextStoryId"] = m.NextStoryId;
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
            if (u.CanGames) d["canGames"] = true;
            if (u.Vip) d["vip"] = true;
            if (!string.IsNullOrEmpty(u.Cover)) d["cover"] = u.Cover;
            if (!string.IsNullOrEmpty(u.PrivacyPrefs)) d["privacyPrefs"] = u.PrivacyPrefs;
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
            u.CanGames = Json.GetBool(d, "canGames", false);
            u.Vip = Json.GetBool(d, "vip", false);
            u.Cover = Json.GetString(d, "cover", "");
            u.PrivacyPrefs = Json.GetString(d, "privacyPrefs", "");
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
                if (p.Pending) d["pending"] = true;
                if (p.Album > 0) d["album"] = p.Album;
                d["legacySource"] = p.LegacySource; d["legacyId"] = p.LegacyId;
                d["pollQuestion"] = p.PollQuestion; d["pollOptions"] = p.PollOptions; d["pollVotes"] = p.PollVotes;
                d["reactions"] = p.Reactions;
                if (p.ScheduledFor != DateTime.MinValue) d["scheduledFor"] = p.ScheduledFor;
                if (p.RepostOf > 0) d["repostOf"] = p.RepostOf;
                if (p.Reposts > 0) d["reposts"] = p.Reposts;
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
            p.Pending = Json.GetBool(d, "pending", false);
            p.Album = Json.GetLong(d, "album", 0);
            p.Video = Json.GetString(d, "video", "");
            p.LegacySource = Json.GetString(d, "legacySource", "");
            p.LegacyId = Json.GetString(d, "legacyId", "");
            p.PollQuestion = Json.GetString(d, "pollQuestion", "");
            p.PollOptions = Json.GetString(d, "pollOptions", "");
            p.PollVotes = Json.GetString(d, "pollVotes", "");
            p.Reactions = Json.GetString(d, "reactions", "");
            p.ScheduledFor = Json.GetDate(d, "scheduledFor", DateTime.MinValue);
            p.RepostOf = Json.GetLong(d, "repostOf", 0);
            p.Reposts = Json.GetInt(d, "reposts", 0);
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
                d["image"] = m.Image; d["images"] = m.Images; d["replyTo"] = m.ReplyTo;
                if (!string.IsNullOrEmpty(m.Video)) d["video"] = m.Video;
                d["audio"] = m.Audio; d["audioSec"] = m.AudioSec;
                d["reactions"] = m.Reactions;
                d["forwardedFrom"] = m.ForwardedFrom;
                d["fileUrl"] = m.FileUrl; d["fileName"] = m.FileName; d["fileSize"] = m.FileSize;
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
            m.Images = Json.GetString(d, "images", "");
            m.Video = Json.GetString(d, "video", "");
            m.Audio = Json.GetString(d, "audio", "");
            m.AudioSec = Json.GetInt(d, "audioSec", 0);
            m.ReplyTo = Json.GetLong(d, "replyTo", 0);
            m.Reactions = Json.GetString(d, "reactions", "");
            m.ForwardedFrom = Json.GetLong(d, "forwardedFrom", 0);
            m.FileUrl = Json.GetString(d, "fileUrl", "");
            m.FileName = Json.GetString(d, "fileName", "");
            m.FileSize = Json.GetLong(d, "fileSize", 0);
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
                if (g.Editors.Count > 0) d["editors"] = g.Editors;
                if (g.Requests.Count > 0) d["requests"] = g.Requests;
                if (g.PostPolicy != "all") d["postPolicy"] = g.PostPolicy;
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
            ReadIds(d, "editors", g.Editors);
            ReadIds(d, "requests", g.Requests);
            g.PostPolicy = Json.GetString(d, "postPolicy", "all");
            if (g.PostPolicy != "editors" && g.PostPolicy != "review") g.PostPolicy = "all";
            return g;
        }

        /// <summary>Reads a JSON array of ids into a list, ignoring junk.</summary>
        private static void ReadIds(Dictionary<string, object> d, string key, List<long> into)
        {
            List<object> arr = Json.Get(d, key) as List<object>;
            if (arr == null) return;
            for (int i = 0; i < arr.Count; i++)
            {
                long v;
                if (arr[i] is long) v = (long)arr[i];
                else if (!long.TryParse(Convert.ToString(arr[i], CultureInfo.InvariantCulture), out v)) continue;
                if (v > 0 && !into.Contains(v)) into.Add(v);
            }
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

        private List<object> DraftsToArray()
        {
            List<object> list = new List<object>(_drafts.Count);
            for (int i = 0; i < _drafts.Count; i++)
            {
                Draft x = _drafts[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["id"] = x.Id; d["user"] = x.User; d["body"] = x.Body; d["video"] = x.Video;
                d["visibility"] = x.Visibility; d["wall"] = x.Wall; d["group"] = x.Group;
                d["pollQuestion"] = x.PollQuestion; d["pollOptions"] = x.PollOptions;
                d["created"] = x.Created; d["updated"] = x.Updated;
                list.Add(d);
            }
            return list;
        }

        private static Draft DraftFromDict(Dictionary<string, object> d)
        {
            Draft x = new Draft();
            x.Id = Json.GetLong(d, "id", 0);
            x.User = Json.GetLong(d, "user", 0);
            x.Body = Json.GetString(d, "body", "");
            x.Video = Json.GetString(d, "video", "");
            x.Visibility = Json.GetString(d, "visibility", "public");
            x.Wall = Json.GetLong(d, "wall", 0);
            x.Group = Json.GetLong(d, "group", 0);
            x.PollQuestion = Json.GetString(d, "pollQuestion", "");
            x.PollOptions = Json.GetString(d, "pollOptions", "");
            x.Created = Json.GetDate(d, "created", DateTime.UtcNow);
            x.Updated = Json.GetDate(d, "updated", x.Created);
            if (x.Id <= 0 || x.User <= 0) return null;
            return x;
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

