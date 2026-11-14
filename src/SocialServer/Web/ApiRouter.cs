using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Social.Core;

namespace Social.Web
{
    /// <summary>
    /// All /api/ endpoints. Every authenticated call is HMAC signed and (optionally)
    /// sealed with the session key; bodies are parsed only after the signature checks out.
    /// </summary>
    public sealed partial class ApiRouter
    {
        public const string Version = "1.0.0";

        // Long polls park a thread until something happens. Cap how many may do
        // that at once so a pile of open tabs can never starve the thread pool;
        // the overflow answers immediately with busy=true and the client retries.
        private const int MaxActiveWaits = 48;
        private static int _activeWaits;
        public static int ActiveWaits { get { return _activeWaits; } }

        private bool EnterWait()
        {
            int n = Interlocked.Increment(ref _activeWaits);
            if (n > MaxActiveWaits)
            {
                Interlocked.Decrement(ref _activeWaits);
                return false;
            }
            return true;
        }

        private void ExitWait() { Interlocked.Decrement(ref _activeWaits); }

        private readonly WebServer _web;
        private readonly Config _cfg;
        private readonly SocialStore _store;
        private readonly SessionManager _sessions;
        private readonly StatsStore _stats;
        private readonly CaptchaStore _captcha;
        private readonly MediaStore _media;
        private readonly AiEngine _ai;
        private readonly AiRunner _aiRunner;
        private readonly NonceCache _preAuthNonces = new NonceCache(TimeSpan.FromMinutes(10), 20000);
        private readonly Dictionary<string, ActionWindow> _actions = new Dictionary<string, ActionWindow>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActionWindow> _registerRate = new Dictionary<string, ActionWindow>(StringComparer.Ordinal);
        private readonly object _actionLock = new object();
        private static readonly DateTime StartedUtc = DateTime.UtcNow;

        public ApiRouter(WebServer web, Config cfg, SocialStore store, SessionManager sessions,
                         StatsStore stats, CaptchaStore captcha, MediaStore media, AiEngine ai, AiRunner aiRunner)
        {
            _web = web;
            _cfg = cfg;
            _store = store;
            _sessions = sessions;
            _stats = stats;
            _captcha = captcha;
            _media = media;
            _ai = ai;
            _aiRunner = aiRunner;
        }

        private sealed class ApiCtx
        {
            public HttpRequest Req;
            public HttpResponse Resp;
            public string Ip;
            public string Route;
            public Session Session;
            public User User;
            public bool Encrypted;
            public Dictionary<string, object> Body;
            public Dictionary<string, string> Query;
        }

        private sealed class ApiException : Exception
        {
            public readonly int Status;
            public readonly string Code;
            public ApiException(int status, string code, string message) : base(message)
            {
                Status = status;
                Code = code;
            }
        }

        private sealed class ActionWindow
        {
            public DateTime StartUtc;
            public int Count;
        }

        // ============================================================== entry

        public bool Handle(HttpRequest req, HttpResponse resp, string ip)
        {
            if (req == null || req.Path == null) return false;
            if (!req.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;

            string route = req.Path.Substring(5).Trim('/').ToLowerInvariant();
            try
            {
                if (route == "handshake") { HandleHandshake(req, resp); return true; }
                if (route == "health") { HandleHealth(req, resp); return true; }
                if (route == "register") { HandleRegister(req, resp, ip); return true; }
                if (route == "login") { HandleLogin(req, resp, ip); return true; }
                if (route == "recover") { HandleRecover(req, resp, ip); return true; }

                return HandleAuthenticated(req, resp, ip, route);
            }
            catch (ApiException ex)
            {
                FailPlain(resp, ex.Status, ex.Code, ex.Message, 0);
                return true;
            }
            catch (JsonException ex)
            {
                Log.Warn("API", "bad json from " + ip + ": " + ex.Message);
                FailPlain(resp, 400, "bad-json", "Malformed JSON", 0);
                return true;
            }
            catch (Exception ex)
            {
                _stats.OnApiError();
                Log.Error("API", req.Method + " " + req.Path + " from " + ip + " crashed: " + ex);
                FailPlain(resp, 500, "internal", "Internal server error", 0);
                return true;
            }
        }

        // ========================================================= handshake

        private void HandleHandshake(HttpRequest req, HttpResponse resp)
        {
            if (!IsMethod(req, "GET", "POST")) throw new ApiException(405, "method", "GET only");

            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Prop("ok", true);
            b.Prop("version", Version);
            b.Prop("scheme", Crypto.Scheme);
            b.Prop("kdf", "pbkdf2-sha256");
            b.Prop("kdfIterations", _cfg.KdfIterations);
            b.Prop("serverTime", Util.Iso(DateTime.UtcNow));
            b.Prop("serverTimeUnix", Util.UnixNow());
            b.Prop("encryptApi", _cfg.EncryptApi);
            b.Prop("captchaRequired", _cfg.RequireCaptcha);
            b.Prop("userCount", _store.CountUsers());
            b.Prop("firstUserAdmin", _cfg.FirstUserAdmin);
            b.Name("site").Raw(SiteJson());

            if (_cfg.RequireCaptcha)
            {
                CaptchaChallenge c = _captcha.New();
                b.Name("captcha").BeginObject()
                    .Prop("id", c.Id)
                    .Prop("svg", c.Svg)
                    .Prop("ttl", 300)
                    .EndObject();
            }
            else b.Name("captcha").Null();

            b.EndObject();
            _web.SendJson(req, resp, 200, b.ToString());
        }

        private string SiteJson()
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("name", _cfg.SiteName);
            b.Prop("shortName", _cfg.SiteShort);
            b.Prop("tagline", _cfg.SiteTagline);
            b.Prop("description", _cfg.SiteDescription);
            b.Prop("baseUrl", _cfg.BaseUrl);
            b.Prop("registration", _cfg.Registration);
            b.Prop("inviteOnly", _cfg.InviteOnly);
            // tells the SPA whether the admin board is reachable from this origin
            b.Prop("adminBoard", _web != null && _web.IsAdminBoard);
            b.Prop("adminPort", _cfg.AdminEnabled ? _cfg.AdminPort : 0);
            b.Prop("defaultLanguage", _cfg.DefaultLanguage);
            b.Prop("defaultTheme", _cfg.DefaultTheme);
            b.Prop("defaultSound", _cfg.DefaultSound);
            b.Prop("motd", _cfg.Motd);
            b.Prop("footer", _cfg.Footer);
            b.Prop("supportEmail", _cfg.SupportEmail);
            b.Prop("version", Version);
            b.Prop("onlineWindowSec", _cfg.OnlineWindowSec);
            b.Prop("refreshMs", _cfg.RefreshMs);
            b.Prop("liveNotifications", _cfg.LiveNotifications);
            b.Prop("notifyWaitSec", _cfg.NotifyWaitSec);
            b.Prop("maxPostLength", _cfg.MaxPostLength);
            b.Prop("maxCommentLength", _cfg.MaxCommentLength);
            b.Prop("maxMessageLength", _cfg.MaxMessageLength)
                .Prop("maxMessageImageKB", _cfg.MaxMessageImageKB)
                .Prop("maxVoiceSec", _cfg.MaxVoiceSec)
                .Prop("maxFileKB", _cfg.MaxFileKB);
            b.Prop("maxStatusLength", _cfg.MaxStatusLength);
            b.Prop("maxBioLength", _cfg.MaxBioLength);
            b.Prop("maxNameLength", _cfg.MaxNameLength);
            b.Prop("maxAvatarKB", _cfg.MaxAvatarKB);
            b.Prop("maxPostImageKB", _cfg.MaxPostImageKB);
            b.Prop("allowSounds", _cfg.AllowSounds);
            b.Name("themes").BeginArray();
            string[] themes = _cfg.Themes();
            for (int i = 0; i < themes.Length; i++) b.Str(themes[i]);
            b.EndArray();
            b.Name("languages").BeginArray();
            b.Str("ru"); b.Str("en"); b.Str("uk"); b.Str("lt");
            b.EndArray();
            // achievement catalogue: id -> icon, so the SPA never hardcodes it
            b.Name("badges").BeginArray();
            for (int i = 0; i < Badges.All.Length; i++)
            {
                b.BeginObject();
                b.Prop("id", Badges.All[i].Id);
                b.Prop("icon", Badges.All[i].Icon);
                b.Prop("manual", Badges.All[i].Manual);
                b.EndObject();
            }
            b.EndArray();
            b.EndObject();
            return b.ToString();
        }

        private void HandleHealth(HttpRequest req, HttpResponse resp)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("ok", true);
            b.Prop("status", _store.IsReadOnly ? "degraded" : "ok");
            b.Prop("version", Version);
            b.Prop("uptimeSec", (long)(DateTime.UtcNow - StartedUtc).TotalSeconds);
            b.Prop("users", _store.CountUsers());
            b.Prop("online", _store.CountOnline(_cfg.OnlineWindowSec));
            b.Prop("storageReadOnly", _store.IsReadOnly);
            b.Prop("lastSave", _store.LastSaveUtc == DateTime.MinValue ? "" : Util.Iso(_store.LastSaveUtc));
            b.Prop("serverTime", Util.Iso(DateTime.UtcNow));
            b.EndObject();
            _web.SendJson(req, resp, 200, b.ToString());
        }

        // ========================================================== register

        private void HandleRegister(HttpRequest req, HttpResponse resp, string ip)
        {
            if (!IsMethod(req, "POST")) throw new ApiException(405, "method", "POST only");
            if (!_cfg.Registration) throw new ApiException(403, "registration-closed", "Registration is closed");
            if (_web.AuthBlocked(ip)) throw new ApiException(429, "locked", "Too many attempts, try later");
            if (!AllowRegister(ip)) throw new ApiException(429, "rate-limit", "Too many registrations from this address");

            string username = Util.NormalizeUsername(req.Header("X-Auth-User"));
            string verifier = req.Header("X-Auth-Verifier");
            string salt = req.Header("X-Auth-Salt");
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(salt))
                throw new ApiException(400, "auth-headers", "Missing authentication headers");
            if (!Util.IsValidUsername(username, _cfg.MaxUsernameLength))
                throw new ApiException(400, "bad-username", "Username must be 3-" + _cfg.MaxUsernameLength + " letters, digits, _ or .");
            if (!Crypto.IsHex(verifier, 64, 64)) throw new ApiException(400, "bad-verifier", "Bad verifier");
            if (!Crypto.IsHex(salt, 32, 32)) throw new ApiException(400, "bad-salt", "Bad salt");
            if (_store.UsernameExists(username)) throw new ApiException(409, "username-taken", "This username is already taken");

            byte[] k0 = Crypto.DeriveKey(verifier, salt, _cfg.KdfIterations);
            VerifySigned(req, k0, "reg", ip);

            Dictionary<string, object> body = DecryptBody(req, k0);
            if (_cfg.RequireCaptcha)
            {
                string capId = Json.GetString(body, "captchaId", "");
                string cap = Json.GetString(body, "captcha", "");
                if (!_captcha.Validate(capId, cap)) throw new ApiException(400, "captcha", "Wrong captcha code");
            }
            if (_cfg.InviteOnly)
            {
                string invite = Json.GetString(body, "invite", "");
                if (!Crypto.FixedEquals(invite.Trim(), _cfg.InviteCode)) throw new ApiException(403, "invite", "An invite code is required");
            }

            string displayName = Json.GetString(body, "displayName", username);
            string role = (_store.CountUsers() == 0 && _cfg.FirstUserAdmin) ? "admin" : "user";
            string recovery = Crypto.NewRecoveryCode();
            string ipHash = Crypto.HashIp(ip, _cfg.ServerSecret);

            User user;
            try
            {
                user = _store.CreateUser(username, displayName, verifier, recovery, role, ipHash);
            }
            catch (StoreReadOnlyException ex)
            {
                throw new ApiException(503, "read-only", ex.Message);
            }
            if (user == null) throw new ApiException(409, "username-taken", "This username is already taken");

            _stats.OnRegistration();
            _stats.OnLogin();
            _web.ClearAuthFailures(ip);
            Log.Info("AUTH", "register @" + user.Username + " #" + user.Id + " from " + ip + " role=" + user.Role);

            string sessionSalt = Crypto.RandomHex(Crypto.SaltBytes);
            byte[] sessionKey = Crypto.DeriveKey(verifier, sessionSalt, _cfg.KdfIterations);
            Session session = _sessions.Create(user.Id, sessionKey, ipHash, UaHash(req), ip);
            SetSessionCookie(req, resp, session);

            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Prop("ok", true);
            b.Prop("sessionSalt", sessionSalt);
            b.Prop("recoveryCode", recovery);
            b.Name("user").Raw(UserView(user, user, true));
            b.Name("site").Raw(SiteJson());
            b.Name("me").Raw(MeView(user, session));
            b.EndObject();
            SendSealedKey(req, resp, k0, 200, b.ToString());
        }

        // ============================================================= login

        private void HandleLogin(HttpRequest req, HttpResponse resp, string ip)
        {
            if (!IsMethod(req, "POST")) throw new ApiException(405, "method", "POST only");
            if (_web.AuthBlocked(ip)) throw new ApiException(429, "locked", "Too many attempts, try later");

            string username = Util.NormalizeUsername(req.Header("X-Auth-User"));
            string verifier = req.Header("X-Auth-Verifier");
            string salt = req.Header("X-Auth-Salt");
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(salt))
                throw new ApiException(400, "auth-headers", "Missing authentication headers");
            if (!Crypto.IsHex(verifier, 64, 64)) throw new ApiException(400, "bad-verifier", "Bad verifier");
            if (!Crypto.IsHex(salt, 32, 32)) throw new ApiException(400, "bad-salt", "Bad salt");

            User user = _store.FindUser(username);
            if (user == null || user.Deleted)
            {
                _web.RegisterAuthFailure(ip);
                throw new ApiException(401, "bad-credentials", "Wrong username or password");
            }

            byte[] k0 = Crypto.DeriveKey(verifier, salt, _cfg.KdfIterations);
            try { VerifySigned(req, k0, "login", ip); }
            catch (ApiException) { _web.RegisterAuthFailure(ip); throw; }

            if (!_store.CheckVerifier(user, verifier))
            {
                _web.RegisterAuthFailure(ip);
                _store.MarkUserDirty(user);
                Log.Warn("AUTH", "failed login @" + user.Username + " from " + ip);
                throw new ApiException(401, "bad-credentials", "Wrong username or password");
            }

            Dictionary<string, object> body = DecryptBody(req, k0);
            if (_cfg.RequireCaptcha && _web.AuthFailureCount(ip) >= 3)
            {
                string capId = Json.GetString(body, "captchaId", "");
                string cap = Json.GetString(body, "captcha", "");
                if (!_captcha.Validate(capId, cap)) throw new ApiException(400, "captcha", "Wrong captcha code");
            }

            if (user.Banned && user.BannedUntil != DateTime.MinValue && user.BannedUntil <= DateTime.UtcNow)
            {
                user.Banned = false;
                user.BanReason = "";
                user.BannedUntil = DateTime.MinValue;
            }
            if (user.Banned)
            {
                JsonBuilder banned = new JsonBuilder(512);
                banned.BeginObject();
                banned.Prop("ok", false);
                banned.Prop("error", "banned");
                banned.Prop("message", string.IsNullOrEmpty(user.BanReason) ? "This account is blocked" : user.BanReason);
                banned.Prop("until", user.BannedUntil == DateTime.MinValue ? "" : Util.Iso(user.BannedUntil));
                banned.EndObject();
                SendSealedKey(req, resp, k0, 403, banned.ToString());
                return;
            }

            user.LastLogin = DateTime.UtcNow;
            user.LastSeen = DateTime.UtcNow;
            user.LoginCount++;
            user.IpHash = Crypto.HashIp(ip, _cfg.ServerSecret);
            _store.MarkUserDirty(user);
            _stats.OnLogin();
            _web.ClearAuthFailures(ip);
            GrantBadges(user);

            string sessionSalt = Crypto.RandomHex(Crypto.SaltBytes);
            byte[] sessionKey = Crypto.DeriveKey(verifier, sessionSalt, _cfg.KdfIterations);
            Session session = _sessions.Create(user.Id, sessionKey, user.IpHash, UaHash(req), ip);
            SetSessionCookie(req, resp, session);
            Log.Info("AUTH", "login @" + user.Username + " #" + user.Id + " from " + ip);

            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Prop("ok", true);
            b.Prop("sessionSalt", sessionSalt);
            b.Name("user").Raw(UserView(user, user, true));
            b.Name("site").Raw(SiteJson());
            b.Name("me").Raw(MeView(user, session));
            b.EndObject();
            SendSealedKey(req, resp, k0, 200, b.ToString());
        }

        // =========================================================== recover

        private void HandleRecover(HttpRequest req, HttpResponse resp, string ip)
        {
            if (!IsMethod(req, "POST")) throw new ApiException(405, "method", "POST only");
            if (_web.AuthBlocked(ip)) throw new ApiException(429, "locked", "Too many attempts, try later");

            string username = Util.NormalizeUsername(req.Header("X-Auth-User"));
            string recovery = req.Header("X-Auth-Recover");
            string salt = req.Header("X-Auth-Salt");
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(recovery) || string.IsNullOrEmpty(salt))
                throw new ApiException(400, "auth-headers", "Missing authentication headers");
            if (!Crypto.IsHex(salt, 32, 32)) throw new ApiException(400, "bad-salt", "Bad salt");

            User user = _store.FindUser(username);
            if (user == null || user.Deleted)
            {
                _web.RegisterAuthFailure(ip);
                throw new ApiException(401, "bad-recovery", "Wrong recovery code");
            }

            string code = recovery.Trim().ToUpperInvariant();
            byte[] k0 = Crypto.DeriveKey(code, salt, _cfg.KdfIterations);
            try { VerifySigned(req, k0, "rec", ip); }
            catch (ApiException) { _web.RegisterAuthFailure(ip); throw; }

            if (!_store.CheckRecovery(user, code))
            {
                _web.RegisterAuthFailure(ip);
                Log.Warn("AUTH", "failed recovery @" + user.Username + " from " + ip);
                throw new ApiException(401, "bad-recovery", "Wrong recovery code");
            }

            Dictionary<string, object> body = DecryptBody(req, k0);
            string newVerifier = Json.GetString(body, "newVerifier", "");
            if (!Crypto.IsHex(newVerifier, 64, 64)) throw new ApiException(400, "bad-verifier", "Bad verifier");

            string newRecovery = Crypto.NewRecoveryCode();
            _store.SetVerifier(user, newVerifier);
            _store.SetRecovery(user, newRecovery);
            _sessions.RemoveForUser(user.Id, null);
            _web.ClearAuthFailures(ip);
            Log.Info("AUTH", "password recovered @" + user.Username + " #" + user.Id + " from " + ip);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("ok", true);
            b.Prop("recoveryCode", newRecovery);
            b.EndObject();
            SendSealedKey(req, resp, k0, 200, b.ToString());
        }

        // ===================================================== authenticated

        private bool HandleAuthenticated(HttpRequest req, HttpResponse resp, string ip, string route)
        {
            string sid = req.Cookie(_cfg.CookieName);
            Session session = _sessions.Get(sid);
            if (session == null)
            {
                FailPlain(resp, 401, "auth-required", "Please sign in", 0);
                return true;
            }

            User user = _store.FindUser(session.UserId);
            if (user == null || user.Deleted)
            {
                _sessions.Remove(sid);
                FailPlain(resp, 401, "auth-required", "Please sign in", 0);
                return true;
            }
            if (user.Banned && (user.BannedUntil == DateTime.MinValue || user.BannedUntil > DateTime.UtcNow))
            {
                FailPlain(resp, 403, "banned", string.IsNullOrEmpty(user.BanReason) ? "Account blocked" : user.BanReason, 0);
                return true;
            }

            bool encrypted = string.Equals(req.Header("X-Api-Enc") ?? "", "1", StringComparison.Ordinal);
            if (_cfg.EncryptApi && !encrypted)
            {
                FailPlain(resp, 400, "encryption-required", "Encrypted API requests are required", 0);
                return true;
            }

            try
            {
                VerifySigned(req, session.Key, "sess:" + session.Id, ip);
            }
            catch (ApiException ex)
            {
                if (ex.Code == "bad-signature" || ex.Code == "replay" || ex.Code == "stale")
                    Log.Warn("SEC", "rejected " + req.Method + " " + req.Path + " from " + ip + ": " + ex.Code);
                FailPlain(resp, ex.Status, ex.Code, ex.Message, 0);
                return true;
            }

            ApiCtx ctx = new ApiCtx();
            ctx.Req = req;
            ctx.Resp = resp;
            ctx.Ip = ip;
            ctx.Route = route;
            ctx.Session = session;
            ctx.User = user;
            ctx.Encrypted = encrypted;
            ctx.Query = HttpRequest.ParseQuery(req.Query);

            try
            {
                ctx.Body = ReadBody(req, session.Key, encrypted);
            }
            catch (ApiException ex)
            {
                SendError(ctx, ex.Status, ex.Code, ex.Message);
                return true;
            }

            _sessions.Touch(session, Crypto.HashIp(ip, _cfg.ServerSecret), UaHash(req));
            _store.TouchUser(user);

            try
            {
                Dispatch(ctx);
            }
            catch (ApiException ex)
            {
                SendError(ctx, ex.Status, ex.Code, ex.Message);
            }
            catch (StoreReadOnlyException ex)
            {
                SendError(ctx, 503, "read-only", ex.Message);
            }
            catch (JsonException ex)
            {
                SendError(ctx, 400, "bad-json", ex.Message);
            }
            catch (Exception ex)
            {
                _stats.OnApiError();
                Log.Error("API", route + " for @" + user.Username + " crashed: " + ex);
                SendError(ctx, 500, "internal", "Internal server error");
            }
            return true;
        }

        private void Dispatch(ApiCtx ctx)
        {
            string r = ctx.Route;
            string m = ctx.Req.Method;

            if (r == "me") { RequireMethod(m, "GET"); HandleMe(ctx); return; }
            if (r == "logout") { RequireMethod(m, "POST"); HandleLogout(ctx); return; }
            if (r == "settings") { HandleSettings(ctx); return; }
            if (r == "password") { RequireMethod(m, "POST"); HandlePassword(ctx); return; }
            if (r == "feed") { RequireMethod(m, "GET"); HandleFeed(ctx); return; }
            if (r == "wall") { RequireMethod(m, "GET"); HandleWall(ctx); return; }
            if (r == "post") { HandlePost(ctx); return; }
            if (r == "post/delete") { RequireMethod(m, "POST"); HandlePostDelete(ctx); return; }
            if (r == "post/edit") { RequireMethod(m, "POST"); HandlePostEdit(ctx); return; }
            if (r == "post/pin") { RequireMethod(m, "POST"); HandlePostPin(ctx); return; }
            if (r == "likes") { RequireMethod(m, "GET"); HandleLikes(ctx); return; }
            if (r == "poll") { RequireMethod(m, "POST"); HandlePollVote(ctx); return; }
            if (r == "reaction") { RequireMethod(m, "POST"); HandlePostReact(ctx); return; }
            if (r == "photos") { RequireMethod(m, "GET"); HandlePhotos(ctx); return; }
            if (r == "like") { RequireMethod(m, "POST"); HandleLike(ctx); return; }
            if (r == "bookmarks") { RequireMethod(m, "GET"); HandleBookmarks(ctx); return; }
            if (r == "bookmark") { RequireMethod(m, "POST"); HandleBookmark(ctx); return; }
            if (r == "comments") { RequireMethod(m, "GET"); HandleComments(ctx); return; }
            if (r == "comment") { RequireMethod(m, "POST"); HandleComment(ctx); return; }
            if (r == "comment/delete") { RequireMethod(m, "POST"); HandleCommentDelete(ctx); return; }
            if (r == "profile") { HandleProfile(ctx); return; }
            if (r == "avatar") { RequireMethod(m, "POST"); HandleAvatar(ctx); return; }
            if (r == "people") { RequireMethod(m, "GET"); HandlePeople(ctx); return; }
            if (r == "search") { RequireMethod(m, "GET"); HandleSearch(ctx); return; }
            if (r == "search/posts") { RequireMethod(m, "GET"); HandleSearchPosts(ctx); return; }
            if (r == "friends") { RequireMethod(m, "GET"); HandleFriends(ctx); return; }
            if (r == "friend") { RequireMethod(m, "POST"); HandleFriendAction(ctx); return; }
            if (r == "messages/threads") { RequireMethod(m, "GET"); HandleThreads(ctx); return; }
            if (r == "messages") { HandleMessages(ctx); return; }
            if (r == "messages/send") { RequireMethod(m, "POST"); HandleMessageSend(ctx); return; }
            if (r == "messages/read") { RequireMethod(m, "POST"); HandleMessageRead(ctx); return; }
            if (r == "messages/delete") { RequireMethod(m, "POST"); HandleMessageDelete(ctx); return; }
            if (r == "messages/wait") { RequireMethod(m, "GET"); HandleMessagesWait(ctx); return; }
            if (r == "messages/typing") { RequireMethod(m, "POST"); HandleMessageTyping(ctx); return; }
            if (r == "messages/react") { RequireMethod(m, "POST"); HandleMessageReact(ctx); return; }
            if (r == "messages/forward") { RequireMethod(m, "POST"); HandleMessageForward(ctx); return; }
            if (r == "messages/search") { RequireMethod(m, "GET"); HandleMessageSearch(ctx); return; }
            if (r == "notifications") { RequireMethod(m, "GET"); HandleNotifications(ctx); return; }
            if (r == "notifications/read") { RequireMethod(m, "POST"); HandleNotificationsRead(ctx); return; }
            if (r == "notifications/wait") { RequireMethod(m, "GET"); HandleNotificationsWait(ctx); return; }
            if (r == "online") { RequireMethod(m, "GET"); HandleOnline(ctx); return; }
            if (r == "stats") { RequireMethod(m, "GET"); HandleStats(ctx); return; }
            if (r == "groups") { HandleGroups(ctx); return; }
            if (r == "group") { HandleGroup(ctx); return; }
            if (r == "group/create") { RequireMethod(m, "POST"); HandleGroupCreate(ctx); return; }
            if (r == "group/join") { RequireMethod(m, "POST"); HandleGroupJoin(ctx, true); return; }
            if (r == "group/leave") { RequireMethod(m, "POST"); HandleGroupJoin(ctx, false); return; }
            if (r == "group/feed") { RequireMethod(m, "GET"); HandleGroupFeed(ctx); return; }
            if (r == "group/edit") { RequireMethod(m, "POST"); HandleGroupEdit(ctx); return; }
            if (r == "group/avatar") { RequireMethod(m, "POST"); HandleGroupAvatar(ctx); return; }
            if (r == "group/kick") { RequireMethod(m, "POST"); HandleGroupKick(ctx); return; }
            if (r == "tag") { RequireMethod(m, "GET"); HandleTag(ctx); return; }
            if (r == "tags") { RequireMethod(m, "GET"); HandleTags(ctx); return; }
            if (r == "report") { RequireMethod(m, "POST"); HandleReport(ctx); return; }

            if (r.StartsWith("admin/", StringComparison.Ordinal)) { DispatchAdmin(ctx, r.Substring(6)); return; }

            throw new ApiException(404, "unknown-endpoint", "Unknown endpoint");
        }

        // ============================================================ helpers

        private static bool IsMethod(HttpRequest req, params string[] methods)
        {
            for (int i = 0; i < methods.Length; i++)
                if (string.Equals(req.Method, methods[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void RequireMethod(string actual, string expected)
        {
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(405, "method", expected + " only");
        }

        private void VerifySigned(HttpRequest req, byte[] key, string scope, string ip)
        {
            string ts = req.Header("X-Api-Ts");
            string nonce = req.Header("X-Api-Nonce");
            string sig = req.Header("X-Api-Sig");
            if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(sig))
                throw new ApiException(401, "unsigned", "Signed request required");
            if (!Crypto.IsFreshTimestamp(ts, DateTime.UtcNow))
                throw new ApiException(401, "stale", "Clock skew or stale request");
            if (!Crypto.IsValidNonce(nonce))
                throw new ApiException(401, "bad-nonce", "Malformed nonce");

            string expected = Crypto.Signature(key, req.Method, req.CanonicalTarget(), ts, nonce, req.Body);
            if (!Crypto.FixedEquals(expected, sig))
                throw new ApiException(401, "bad-signature", "Bad signature");

            if (!_preAuthNonces.TryUse(scope + ":" + nonce))
                throw new ApiException(401, "replay", "Replayed request");
        }

        private Dictionary<string, object> DecryptBody(HttpRequest req, byte[] key)
        {
            if (req.Body == null || req.Body.Length == 0) return new Dictionary<string, object>();
            if (!_cfg.EncryptApi) return Json.ParseObject(req.BodyText(), _cfg.MaxJsonDepth);
            byte[] plain = Crypto.Open(key, req.BodyText());
            if (plain == null) throw new ApiException(400, "bad-envelope", "Cannot decrypt request body");
            return Json.ParseObject(Encoding.UTF8.GetString(plain), _cfg.MaxJsonDepth);
        }

        private Dictionary<string, object> ReadBody(HttpRequest req, byte[] key, bool encrypted)
        {
            if (req.Body == null || req.Body.Length == 0) return new Dictionary<string, object>();
            string text;
            if (encrypted)
            {
                byte[] plain = Crypto.Open(key, req.BodyText());
                if (plain == null) throw new ApiException(400, "bad-envelope", "Cannot decrypt request body");
                text = Encoding.UTF8.GetString(plain);
            }
            else text = req.BodyText();
            return Json.ParseObject(text, _cfg.MaxJsonDepth);
        }

        private void SetSessionCookie(HttpRequest req, HttpResponse resp, Session session)
        {
            int maxAge = _cfg.SessionHours * 3600;
            resp.SetCookie(_cfg.CookieName, session.Id, _cfg.CookiePath(), maxAge,
                           _web.CookieSecure(req), true, "Lax");
        }

        private void ClearSessionCookie(HttpRequest req, HttpResponse resp)
        {
            resp.SetCookie(_cfg.CookieName, "", _cfg.CookiePath(), 0,
                           _web.CookieSecure(req), true, "Lax");
        }

        private static string UaHash(HttpRequest req)
        {
            string ua = req.Header("User-Agent") ?? "";
            return Crypto.Hex(Crypto.Sha256(Encoding.UTF8.GetBytes(ua))).Substring(0, 16);
        }

        private void SendSealedKey(HttpRequest req, HttpResponse resp, byte[] key, int status, string json)
        {
            byte[] plain = Encoding.UTF8.GetBytes(json ?? "");
            if (_cfg.EncryptApi && key != null)
            {
                string envelope = Crypto.Seal(key, plain);
                resp.Status = status;
                resp.ContentType = "text/plain; charset=utf-8";
                resp.Body = Encoding.UTF8.GetBytes(envelope);
                resp.Set("X-Api-Enc", "1");
                resp.Set("Cache-Control", "no-store");
                _web.ApplySecurityHeaders(resp, req);
            }
            else
            {
                _web.SendJson(req, resp, status, json);
            }
        }

        private void SendSealed(ApiCtx ctx, int status, string json)
        {
            SendSealedKey(ctx.Req, ctx.Resp, ctx.Session == null ? null : ctx.Session.Key, status, json);
        }

        private void SendOk(ApiCtx ctx, JsonBuilder body)
        {
            body.Prop("ok", true);
            body.EndObject();
            SendSealed(ctx, 200, body.ToString());
        }

        private void SendError(ApiCtx ctx, int status, string code, string message)
        {
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("ok", false);
            b.Prop("error", code);
            b.Prop("message", message);
            b.EndObject();
            SendSealed(ctx, status, b.ToString());
        }

        private void FailPlain(HttpResponse resp, int status, string code, string message, int retryAfter)
        {
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("ok", false);
            b.Prop("error", code);
            b.Prop("message", message);
            if (retryAfter > 0) b.Prop("retryAfter", retryAfter);
            b.EndObject();
            resp.Status = status;
            resp.ContentType = "application/json; charset=utf-8";
            resp.Body = Encoding.UTF8.GetBytes(b.ToString());
            resp.Set("Cache-Control", "no-store");
            if (retryAfter > 0) resp.Set("Retry-After", retryAfter.ToString(CultureInfo.InvariantCulture));
        }

        private bool Allow(long userId, string action, int limit, int windowSec)
        {
            if (limit <= 0) return true;
            string key = userId.ToString(CultureInfo.InvariantCulture) + ":" + action;
            DateTime now = DateTime.UtcNow;
            lock (_actionLock)
            {
                ActionWindow w;
                if (!_actions.TryGetValue(key, out w) || (now - w.StartUtc).TotalSeconds >= windowSec)
                {
                    w = new ActionWindow();
                    w.StartUtc = now;
                    w.Count = 0;
                    _actions[key] = w;
                }
                w.Count++;
                if (_actions.Count > 5000 && w.Count == 1) PruneActionsLocked(now);
                return w.Count <= limit;
            }
        }

        private void PruneActionsLocked(DateTime now)
        {
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, ActionWindow> kv in _actions)
                if ((now - kv.Value.StartUtc).TotalSeconds > 3600) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _actions.Remove(dead[i]);
        }

        private bool AllowRegister(string ip)
        {
            int limit = _cfg.RegistrationsPerHour;
            if (limit <= 0) return true;
            DateTime now = DateTime.UtcNow;
            lock (_actionLock)
            {
                ActionWindow w;
                if (!_registerRate.TryGetValue(ip, out w) || (now - w.StartUtc).TotalHours >= 1)
                {
                    w = new ActionWindow();
                    w.StartUtc = now;
                    w.Count = 0;
                    _registerRate[ip] = w;
                }
                w.Count++;
                return w.Count <= limit;
            }
        }

        private void RequireAdmin(User u)
        {
            if (u == null || u.Role != "admin") throw new ApiException(403, "forbidden", "Administrator rights required");
        }

        private void RequireModerator(User u)
        {
            if (u == null || (u.Role != "admin" && u.Role != "moderator"))
                throw new ApiException(403, "forbidden", "Moderator rights required");
        }

        private int LimitParam(ApiCtx ctx, int def, int max)
        {
            int v = 0;
            string s = ctx.Query == null ? null : GetQuery(ctx.Query, "limit");
            if (!string.IsNullOrEmpty(s)) int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            if (v <= 0) v = def;
            return Math.Min(v, max);
        }

        private long LongQuery(ApiCtx ctx, string key)
        {
            string s = ctx.Query == null ? null : GetQuery(ctx.Query, key);
            long v;
            if (!string.IsNullOrEmpty(s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static string GetQuery(Dictionary<string, string> q, string key)
        {
            string v;
            return q != null && q.TryGetValue(key, out v) ? v : null;
        }

        private static string BodyStr(ApiCtx ctx, string key, string def)
        {
            return Json.GetString(ctx.Body, key, def);
        }

        private static long BodyLong(ApiCtx ctx, string key, long def)
        {
            return Json.GetLong(ctx.Body, key, def);
        }

        private static bool BodyBool(ApiCtx ctx, string key, bool def)
        {
            return Json.GetBool(ctx.Body, key, def);
        }
    }
}

