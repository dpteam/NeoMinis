using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        private void DispatchAdmin(ApiCtx ctx, string sub)
        {
            if (_web == null || !_web.IsAdminBoard)
                throw new ApiException(404, "unknown-endpoint", "Unknown endpoint");
            if (sub == "overview") { RequireMethod(ctx.Req.Method, "GET"); RequireModerator(ctx.User); HandleAdminOverview(ctx); return; }
            if (sub == "users") { RequireMethod(ctx.Req.Method, "GET"); RequireModerator(ctx.User); HandleAdminUsers(ctx); return; }
            if (sub == "badges/recalc") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminBadgesRecalc(ctx); return; }
            if (sub == "notify-test") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminNotifyTest(ctx); return; }
            if (sub == "user")
            {
                if (IsMethod(ctx.Req, "GET")) { RequireModerator(ctx.User); HandleAdminUserGet(ctx); return; }
                RequireMethod(ctx.Req.Method, "POST");
                RequireAdmin(ctx.User);
                HandleAdminUser(ctx);
                return;
            }
            if (sub == "reports") { RequireMethod(ctx.Req.Method, "GET"); RequireModerator(ctx.User); HandleAdminReports(ctx); return; }
            if (sub == "report") { RequireMethod(ctx.Req.Method, "POST"); RequireModerator(ctx.User); HandleAdminReport(ctx); return; }
            if (sub == "broadcast") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminBroadcast(ctx); return; }
            if (sub == "logs") { RequireMethod(ctx.Req.Method, "GET"); RequireModerator(ctx.User); HandleAdminLogs(ctx); return; }
            if (sub == "reload") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminReload(ctx); return; }
            if (sub == "backup") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminBackup(ctx); return; }
            if (sub == "repair") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminRepair(ctx); return; }
            if (sub == "ai") { HandleAdminAi(ctx); return; }
            if (sub == "ai/config") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminAiConfig(ctx); return; }
            if (sub == "ai/test") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminAiTest(ctx); return; }
            if (sub == "ai/step") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminAiStep(ctx); return; }
            if (sub == "ai/persona") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminAiPersona(ctx); return; }
            if (sub == "import") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminImport(ctx); return; }
            if (sub == "settings") { HandleAdminSettings(ctx); return; }
            if (sub == "settings/save") { RequireMethod(ctx.Req.Method, "POST"); RequireAdmin(ctx.User); HandleAdminSettingsSave(ctx); return; }
            if (sub == "feedback") { RequireMethod(ctx.Req.Method, "GET"); RequireModerator(ctx.User); HandleAdminFeedback(ctx); return; }
            if (sub == "feedback/update") { RequireMethod(ctx.Req.Method, "POST"); RequireModerator(ctx.User); HandleAdminFeedbackUpdate(ctx); return; }
            throw new ApiException(404, "unknown-endpoint", "Unknown admin endpoint");
        }

        // ========================================================== overview

        private void HandleAdminOverview(ApiCtx ctx)
        {
            int online = _store.CountOnline(_cfg.OnlineWindowSec);
            _stats.NotePeak(online);
            List<User> recent = _store.RecentUsers(8);
            List<KeyValuePair<User, int>> top = _store.TopUsers(5);
            List<Report> openReports = _store.Reports("open", 20);
            List<LogEntry> logs = Log.Tail(0, 40, null, null);

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("counts").BeginObject()
                .Prop("users", _store.CountUsers())
                .Prop("online", online)
                .Prop("posts", _store.CountAllPosts())
                .Prop("comments", _store.CountAllComments())
                .Prop("likes", _store.CountAllLikes())
                .Prop("messages", _store.CountAllMessages())
                .Prop("groups", _store.CountGroups())
                .Prop("sessions", _sessions.Count)
                .Prop("openReports", openReports.Count)
                .Prop("newFeedback", _store.CountNewFeedback())
                .EndObject();
            b.Prop("storageBytes", _store.TotalStorageBytes());
            b.Prop("storageReadOnly", _store.IsReadOnly);
            b.Prop("saveError", _store.SaveError);
            b.Prop("lastSave", _store.LastSaveUtc == DateTime.MinValue ? "" : Util.Iso(_store.LastSaveUtc));
            b.Prop("uptimeSec", (long)(DateTime.UtcNow - StartedUtc).TotalSeconds);
            b.Prop("peakOnline", _stats.PeakOnline);
            b.Prop("visitsToday", _stats.ViewsToday());
            b.Prop("uniqueToday", _stats.UniqueToday());
            b.Prop("apiRequests", _stats.ApiRequests);
            b.Prop("apiErrors", _stats.ApiErrors);
            b.Prop("activeWaits", ActiveWaits);
            int workerFree, ioFree;
            ThreadPool.GetAvailableThreads(out workerFree, out ioFree);
            b.Prop("threadsFree", workerFree);
            b.Prop("memoryMB", GC.GetTotalMemory(false) / (1024 * 1024));

            b.Name("recentUsers").BeginArray();
            for (int i = 0; i < recent.Count; i++) b.Raw(AdminUserJson(recent[i], ctx.User));
            b.EndArray();
            b.Name("topUsers").BeginArray();
            for (int i = 0; i < top.Count; i++)
            {
                b.BeginObject();
                b.Name("user").Raw(UserSummary(top[i].Key));
                b.Prop("posts", top[i].Value);
                b.EndObject();
            }
            b.EndArray();
            b.Name("reports").BeginArray();
            for (int i = 0; i < openReports.Count && i < 10; i++) b.Raw(ReportJson(openReports[i]));
            b.EndArray();
            b.Name("logs").BeginArray();
            for (int i = 0; i < logs.Count; i++) b.Raw(LogJson(logs[i]));
            b.EndArray();

            b.Name("config").BeginObject()
                .Prop("siteName", _cfg.SiteName)
                .Prop("port", _cfg.Port)
                .Prop("bind", _cfg.Bind)
                .Prop("publicBasePath", _cfg.PublicBasePath)
                .Prop("registration", _cfg.Registration)
                .Prop("inviteOnly", _cfg.InviteOnly)
                .Prop("encryptApi", _cfg.EncryptApi)
                .Prop("kdfIterations", _cfg.KdfIterations)
                .Prop("requireCaptcha", _cfg.RequireCaptcha)
                .Prop("logLevel", _cfg.LogLevel.ToString())
                .Prop("dataDir", _cfg.DataDir)
                .Prop("webRoot", _cfg.WebRoot)
                .Prop("backupKeep", _cfg.BackupKeep)
                .EndObject();
            SendOk(ctx, b);
        }

        private string AdminUserJson(User u, User viewer)
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", u.Id);
            b.Prop("username", u.Username);
            b.Prop("displayName", u.DisplayName);
            b.Prop("avatar", u.Avatar);
            b.Prop("role", u.Role);
            b.Prop("email", u.Email);
            b.Prop("ipHash", u.IpHash);
            b.Prop("created", Util.Iso(u.Created));
            b.Prop("lastSeen", Util.Iso(u.LastSeen));
            b.Prop("lastLogin", Util.Iso(u.LastLogin));
            b.Prop("loginCount", u.LoginCount);
            b.Prop("banned", u.Banned);
            b.Prop("banReason", u.BanReason);
            b.Prop("bannedUntil", u.BannedUntil == DateTime.MinValue ? "" : Util.Iso(u.BannedUntil));
            b.Prop("warnings", u.Warnings);
            b.Prop("badges", u.Badges);
            b.Prop("persona", u.Persona);
            b.Prop("mood", u.Mood);
            b.Prop("deleted", u.Deleted);
            b.Prop("posts", _store.CountPosts(u.Id));
            b.Prop("friends", _store.CountFriends(u.Id));
            b.Prop("isSelf", viewer != null && viewer.Id == u.Id);
            b.Prop("canGames", u.CanGames || u.IsAdmin);
            b.EndObject();
            return b.ToString();
        }

        // ============================================================= users

        private void HandleAdminNotifyTest(ApiCtx ctx)
        {
            Notification n = _store.Notify(ctx.User.Id, 0, "system",
                "test notification — live delivery is working", "#/notifications");
            Log.Info("ADMIN", "@" + ctx.User.Username + " sent a test notification to self");
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("id", n == null ? 0 : n.Id);
            SendOk(ctx, b);
        }

        private void HandleAdminBadgesRecalc(ApiCtx ctx)
        {
            List<User> users = _store.AllUsers();
            int granted = 0;
            for (int i = 0; i < users.Count; i++)
            {
                if (users[i].Deleted) continue;
                List<string> added = Badges.Apply(_store, users[i]);
                if (added.Count > 0)
                {
                    _store.MarkUserDirty(users[i]);
                    granted += added.Count;
                }
            }
            Log.Info("ADMIN", "@" + ctx.User.Username + " recalculated badges, granted " + granted);
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("granted", granted);
            b.Prop("users", users.Count);
            SendOk(ctx, b);
        }

        private void HandleAdminUsers(ApiCtx ctx)
        {
            string q = GetQuery(ctx.Query, "q");
            int limit = LimitParam(ctx, 40, 100);
            int offset = 0;
            string off = GetQuery(ctx.Query, "offset");
            if (!string.IsNullOrEmpty(off)) int.TryParse(off, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);

            List<User> users = string.IsNullOrEmpty(q) ? _store.AllUsers() : _store.SearchUsers(q, 500);
            users.Sort(delegate(User x, User y) { return y.Id.CompareTo(x.Id); });
            int total = users.Count;
            if (offset > 0 && offset < users.Count) users.RemoveRange(0, offset);
            else if (offset >= users.Count) users.Clear();
            if (users.Count > limit) users.RemoveRange(limit, users.Count - limit);

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("users").BeginArray();
            for (int i = 0; i < users.Count; i++) b.Raw(AdminUserJson(users[i], ctx.User));
            b.EndArray();
            b.Prop("total", total);
            b.Prop("offset", offset);
            b.Prop("hasMore", offset + users.Count < total);
            SendOk(ctx, b);
        }

        private void HandleAdminUserGet(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            if (id <= 0) throw new ApiException(400, "bad-id", "Missing user id");
            User target = _store.FindUser(id);
            if (target == null) throw new ApiException(404, "no-user", "User not found");

            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Name("user").Raw(AdminUserJson(target, ctx.User));
            b.Prop("sessions", _sessions.CountForUser(target.Id));
            b.Prop("bookmarks", _store.CountBookmarks(target.Id));
            b.Prop("canAdmin", ctx.User.Role == "admin");
            b.Prop("isSelf", target.Id == ctx.User.Id);
            b.Prop("admins", _store.CountAdmins());
            b.Prop("allowRoleChange", _cfg.AllowRoleChange);
            b.Prop("allowSelfDemote", _cfg.AllowSelfDemote);
            b.Prop("allowSelfDelete", _cfg.AllowSelfDelete);
            b.Prop("protectLastAdmin", _cfg.ProtectLastAdmin);
            SendOk(ctx, b);
        }

        private void HandleAdminUser(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            string action = BodyStr(ctx, "action", "").Trim().ToLowerInvariant();
            User target = _store.FindUser(id);
            if (target == null) throw new ApiException(404, "no-user", "User not found");

            if (action == "ban")
            {
                if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "You cannot ban yourself");
                if (target.Role == "admin" && _store.CountAdmins() <= 1)
                    throw new ApiException(400, "last-admin", "Cannot ban the last administrator");
                int days = Json.GetInt(ctx.Body, "days", 7);
                if (days < 0) days = 0;
                DateTime until = days == 0 ? DateTime.MinValue : DateTime.UtcNow.AddDays(days);
                string reason = Util.SingleLine(BodyStr(ctx, "reason", ""), 300);
                _store.SetBanned(target.Id, true, reason, until);
                _sessions.RemoveForUser(target.Id, null);
                Log.Warn("ADMIN", "@" + ctx.User.Username + " banned @" + target.Username + " (" + days + "d): " + reason);
            }
            else if (action == "unban")
            {
                _store.SetBanned(target.Id, false, "", DateTime.MinValue);
                Log.Info("ADMIN", "@" + ctx.User.Username + " unbanned @" + target.Username);
            }
            else if (action == "role")
            {
                if (!_cfg.AllowRoleChange)
                    throw new ApiException(403, "role-locked", "Role changes are disabled in the configuration");

                string role = BodyStr(ctx, "role", "").Trim().ToLowerInvariant();
                if (role != "user" && role != "moderator" && role != "admin")
                    throw new ApiException(400, "bad-role", "Unknown role");

                if (target.Id == ctx.User.Id && role != "admin" && !_cfg.AllowSelfDemote)
                    throw new ApiException(400, "self-demote", "You cannot remove your own administrator rights");

                if (target.Role == "admin" && role != "admin" && _cfg.ProtectLastAdmin && _store.CountAdmins() <= 1)
                    throw new ApiException(400, "last-admin", "Cannot demote the last administrator");

                string before = target.Role;
                _store.SetRole(target.Id, role);
                _store.MarkUserDirty(target);

                if (before != role)
                {
                    _store.Notify(target.Id, ctx.User.Id, "admin",
                        "your role changed: " + before + " -> " + role, "#/notifications");
                    // a demoted administrator must not keep a live admin session
                    if (before == "admin" && role != "admin") _sessions.RemoveForUser(target.Id, null);
                }
                Log.Info("ADMIN", "@" + ctx.User.Username + " set role of @" + target.Username + " to " + role);
            }
            else if (action == "grant-admin")
            {
                if (!_cfg.AllowRoleChange)
                    throw new ApiException(403, "role-locked", "Role changes are disabled in the configuration");
                _store.SetRole(target.Id, "admin");
                _store.Notify(target.Id, ctx.User.Id, "admin", "you are now an administrator", "#/notifications");
                Log.Info("ADMIN", "@" + ctx.User.Username + " granted admin to @" + target.Username);
            }
            else if (action == "revoke-admin")
            {
                if (!_cfg.AllowRoleChange)
                    throw new ApiException(403, "role-locked", "Role changes are disabled in the configuration");
                if (target.Id == ctx.User.Id && !_cfg.AllowSelfDemote)
                    throw new ApiException(400, "self-demote", "You cannot remove your own administrator rights");
                if (_cfg.ProtectLastAdmin && target.Role == "admin" && _store.CountAdmins() <= 1)
                    throw new ApiException(400, "last-admin", "Cannot demote the last administrator");
                _store.SetRole(target.Id, "user");
                _sessions.RemoveForUser(target.Id, null);
                _store.Notify(target.Id, ctx.User.Id, "admin", "your administrator rights were removed", "#/notifications");
                Log.Warn("ADMIN", "@" + ctx.User.Username + " revoked admin from @" + target.Username);
            }
            else if (action == "grant-mod")
            {
                if (!_cfg.AllowRoleChange)
                    throw new ApiException(403, "role-locked", "Role changes are disabled in the configuration");
                _store.SetRole(target.Id, "moderator");
                Log.Info("ADMIN", "@" + ctx.User.Username + " granted moderator to @" + target.Username);
            }
            else if (action == "badge")
            {
                string badge = BodyStr(ctx, "badge", "").Trim().ToLowerInvariant();
                if (!Badges.Known(badge)) throw new ApiException(400, "bad-badge", "Unknown badge");
                bool on = BodyBool(ctx, "on", true);
                bool changed = on ? Badges.Grant(target, badge) : Badges.Revoke(target, badge);
                if (changed)
                {
                    _store.MarkUserDirty(target);
                    _store.Notify(target.Id, ctx.User.Id, "badge",
                        (on ? "was given the \"" : "lost the \"") + badge + "\" badge",
                        "#/profile/" + target.Id);
                    Log.Info("ADMIN", "@" + ctx.User.Username + (on ? " granted " : " revoked ") +
                             "badge " + badge + " for @" + target.Username);
                }
            }
            else if (action == "allow-games")
            {
                // SWF uploads are a privilege: an admin hands it out per account.
                bool on = BodyBool(ctx, "on", true);
                target.CanGames = on;
                _store.MarkUserDirty(target);
                _store.Notify(target.Id, ctx.User.Id, "admin",
                    on ? "allowed you to upload Flash games" : "took away your Flash game uploads",
                    "#/games");
                Log.Info("ADMIN", "@" + ctx.User.Username + (on ? " allowed" : " forbade") +
                         " game uploads for @" + target.Username);
            }
            else if (action == "delete")
            {
                if (target.Id == ctx.User.Id && !_cfg.AllowSelfDelete) throw new ApiException(400, "self", "You cannot delete yourself");
                if (target.Role == "admin" && _cfg.ProtectLastAdmin && _store.CountAdmins() <= 1)
                    throw new ApiException(400, "last-admin", "Cannot delete the last administrator");
                _store.SoftDeleteUser(target.Id);
                _sessions.RemoveForUser(target.Id, null);
                Log.Warn("ADMIN", "@" + ctx.User.Username + " soft-deleted @" + target.Username);
            }
            else if (action == "purge")
            {
                if (target.Id == ctx.User.Id && !_cfg.AllowSelfDelete) throw new ApiException(400, "self", "You cannot delete yourself");
                if (target.Role == "admin" && _cfg.ProtectLastAdmin && _store.CountAdmins() <= 1)
                    throw new ApiException(400, "last-admin", "Cannot delete the last administrator");
                string gone = "@" + target.Username;
                int records = _store.HardDeleteUser(target.Id);
                _sessions.RemoveForUser(target.Id, null);
                Log.Warn("ADMIN", "@" + ctx.User.Username + " purged " + gone + " (" + records + " records)");
                JsonBuilder p = new JsonBuilder(256);
                p.BeginObject();
                p.Prop("deleted", true);
                p.Prop("records", records);
                SendOk(ctx, p);
                return;
            }
            else if (action == "edit")
            {
                if (ctx.Body.ContainsKey("displayName"))
                    target.DisplayName = Util.DisplayName(BodyStr(ctx, "displayName", ""), target.Username, _cfg.MaxNameLength);
                if (ctx.Body.ContainsKey("status"))
                    target.Status = Util.SingleLine(BodyStr(ctx, "status", ""), _cfg.MaxStatusLength);
                if (ctx.Body.ContainsKey("email"))
                {
                    string email = BodyStr(ctx, "email", "").Trim();
                    if (email.Length > 0 && !Util.IsValidEmail(email))
                        throw new ApiException(400, "bad-email", "Bad email address");
                    target.Email = Util.SingleLine(email, 160);
                }
                if (ctx.Body.ContainsKey("persona"))
                {
                    string persona = BodyStr(ctx, "persona", "").Trim().ToLowerInvariant();
                    if (persona.Length > 0)
                    {
                        bool known = false;
                        for (int i = 0; i < AiEngine.Personas.Length; i++) if (AiEngine.Personas[i] == persona) known = true;
                        if (!known) throw new ApiException(400, "bad-persona", "Unknown persona");
                    }
                    target.Persona = persona;
                }
                if (ctx.Body.ContainsKey("mood"))
                {
                    string mood = BodyStr(ctx, "mood", "").Trim().ToLowerInvariant();
                    if (mood != "" && mood != "happy" && mood != "sad" && mood != "neutral")
                        throw new ApiException(400, "bad-mood", "Unknown mood");
                    target.Mood = mood;
                }
                if (ctx.Body.ContainsKey("hideOnline")) target.HideOnline = Json.GetBool(ctx.Body, "hideOnline", target.HideOnline);
                if (ctx.Body.ContainsKey("privacy"))
                {
                    string p = BodyStr(ctx, "privacy", "").Trim().ToLowerInvariant();
                    if (p == "public" || p == "friends" || p == "private") target.Privacy = p;
                }
                _store.MarkUserDirty(target);
                Log.Info("ADMIN", "@" + ctx.User.Username + " edited @" + target.Username);
            }
            else if (action == "reset")
            {
                string recovery = Crypto.NewRecoveryCode();
                _store.SetRecovery(target, recovery);
                _sessions.RemoveForUser(target.Id, null);
                JsonBuilder r = new JsonBuilder(256);
                r.BeginObject();
                r.Prop("recoveryCode", recovery);
                SendOk(ctx, r);
                Log.Warn("ADMIN", "@" + ctx.User.Username + " reset recovery code for @" + target.Username);
                return;
            }
            else if (action == "warn")
            {
                target.Warnings++;
                _store.MarkUserDirty(target);
                string note = Util.SingleLine(BodyStr(ctx, "reason", ""), 300);
                _store.Notify(target.Id, ctx.User.Id, "admin", "warning: " + note, "#/notifications");
                Log.Info("ADMIN", "@" + ctx.User.Username + " warned @" + target.Username + " (" + target.Warnings + ")");
            }
            else throw new ApiException(400, "bad-action", "Unknown user action");

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("user").Raw(AdminUserJson(target, ctx.User));
            SendOk(ctx, b);
        }

        // =========================================================== reports

        private void HandleAdminReports(ApiCtx ctx)
        {
            string status = GetQuery(ctx.Query, "status");
            if (status == null) status = "open";
            List<Report> reports = _store.Reports(status, 100);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("reports").BeginArray();
            for (int i = 0; i < reports.Count; i++) b.Raw(ReportJson(reports[i]));
            b.EndArray();
            SendOk(ctx, b);
        }

        private string ReportJson(Report r)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", r.Id);
            b.Prop("type", r.Type);
            b.Prop("target", r.Target);
            b.Prop("reason", r.Reason);
            b.Prop("status", r.Status);
            b.Prop("note", r.Note);
            b.Prop("created", Util.Iso(r.Created));
            b.Name("reporter").Raw(UserSummary(_store.FindUser(r.Reporter)));
            b.Prop("handledBy", r.HandledBy);
            b.Prop("handledAt", r.HandledAt == DateTime.MinValue ? "" : Util.Iso(r.HandledAt));
            if (r.Type == "post")
            {
                Post p = _store.FindPost(r.Target);
                if (p != null) b.Name("post").Raw(PostView(p, null));
            }
            else if (r.Type == "user")
            {
                User u = _store.FindUser(r.Target);
                if (u != null) b.Name("user").Raw(UserSummary(u));
            }
            b.EndObject();
            return b.ToString();
        }

        private void HandleAdminReport(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            string note = Util.SingleLine(BodyStr(ctx, "note", ""), 500);
            if (!_store.CloseReport(id, ctx.User.Id, note)) throw new ApiException(404, "no-report", "Report not found");
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        // ========================================================== feedback

        private void HandleAdminFeedback(ApiCtx ctx)
        {
            string status = GetQuery(ctx.Query, "status");
            if (status == null) status = "";
            List<Feedback> list = _store.FeedbackList(status, 200);

            JsonBuilder b = new JsonBuilder(16384);
            b.BeginObject();
            b.Name("items").BeginArray();
            for (int i = 0; i < list.Count; i++)
            {
                Feedback f = list[i];
                b.BeginObject()
                    .Prop("id", f.Id)
                    .Prop("kind", f.Kind)
                    .Prop("message", f.Message)
                    .Prop("contact", f.Contact)
                    .Prop("page", f.Page)
                    .Prop("app", f.App)
                    .Prop("created", Util.Iso(f.Created))
                    .Prop("status", f.Status)
                    .Prop("note", f.Note)
                    .Prop("handledBy", f.HandledBy)
                    .Prop("handledAt", f.HandledAt == DateTime.MinValue ? "" : Util.Iso(f.HandledAt))
                    .EndObject();
            }
            b.EndArray();
            b.Prop("newCount", _store.CountNewFeedback());
            SendOk(ctx, b);
        }

        private void HandleAdminFeedbackUpdate(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            string action = BodyStr(ctx, "action", "status").Trim().ToLowerInvariant();

            if (action == "delete")
            {
                if (!_store.DeleteFeedback(id)) throw new ApiException(404, "no-feedback", "Not found");
                Log.Warn("ADMIN", "@" + ctx.User.Username + " deleted feedback #" + id);
                JsonBuilder ok = new JsonBuilder(64);
                ok.BeginObject();
                SendOk(ctx, ok);
                return;
            }

            string status = BodyStr(ctx, "status", "").Trim().ToLowerInvariant();
            if (status != "" && status != "new" && status != "read" && status != "closed")
                throw new ApiException(400, "bad-status", "Unknown status");
            string note = BodyStr(ctx, "note", null);
            if (note != null) note = Util.CleanText(note, 1000);

            if (!_store.UpdateFeedback(id, status, note, ctx.User.Id))
                throw new ApiException(404, "no-feedback", "Not found");

            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            SendOk(ctx, b);
        }

        // ========================================================= broadcast

        private void HandleAdminBroadcast(ApiCtx ctx)
        {
            string text = Util.CleanText(BodyStr(ctx, "text", ""), 500);
            if (text.Length == 0) throw new ApiException(400, "empty", "Write something first");
            int n = _store.Broadcast(text, ctx.User.Id);
            Log.Info("ADMIN", "@" + ctx.User.Username + " broadcast to " + n + " users");
            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("delivered", n);
            SendOk(ctx, b);
        }

        // ============================================================== logs

        private void HandleAdminLogs(ApiCtx ctx)
        {
            long since = LongQuery(ctx, "since");
            int limit = LimitParam(ctx, 100, 500);
            string level = GetQuery(ctx.Query, "level");
            string category = GetQuery(ctx.Query, "category");
            LogLevel? min = null;
            if (!string.IsNullOrEmpty(level))
            {
                level = level.ToLowerInvariant();
                if (level == "error") min = LogLevel.Error;
                else if (level == "warn") min = LogLevel.Warn;
                else if (level == "info") min = LogLevel.Info;
                else if (level == "debug") min = LogLevel.Debug;
                else if (level == "verbose") min = LogLevel.Verbose;
            }
            List<LogEntry> entries = Log.Tail(since, limit, min, category);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("entries").BeginArray();
            for (int i = 0; i < entries.Count; i++) b.Raw(LogJson(entries[i]));
            b.EndArray();
            b.Prop("lastSeq", Log.LastSeq);
            SendOk(ctx, b);
        }

        private string LogJson(LogEntry e)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("seq", e.Seq);
            b.Prop("time", Util.Iso(e.Utc));
            b.Prop("level", Log.LevelLetter(e.Level));
            b.Prop("category", e.Category);
            b.Prop("message", e.Message);
            b.EndObject();
            return b.ToString();
        }

        // ==================================================== maintenance

        private void HandleAdminReload(ApiCtx ctx)
        {
            _cfg.Reload();
            Log.Configure(_cfg);
            Log.Info("ADMIN", "@" + ctx.User.Username + " reloaded config.ini");
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("warnings").BeginArray();
            for (int i = 0; i < _cfg.Warnings.Count; i++) b.Str(_cfg.Warnings[i]);
            b.EndArray();
            b.Prop("note", "Port, bind and web root changes need a restart.");
            SendOk(ctx, b);
        }

        private void HandleAdminBackup(ApiCtx ctx)
        {
            string dir = _store.CreateBackup();
            Log.Info("ADMIN", "@" + ctx.User.Username + " created backup " + dir);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("path", dir);
            SendOk(ctx, b);
        }

        private void HandleAdminRepair(ApiCtx ctx)
        {
            _store.ResetReadOnly();
            _store.SaveNow();
            Log.Warn("ADMIN", "@" + ctx.User.Username + " forced a storage repair");
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("readOnly", _store.IsReadOnly);
            b.Prop("saveError", _store.SaveError);
            b.Prop("lastSave", _store.LastSaveUtc == DateTime.MinValue ? "" : Util.Iso(_store.LastSaveUtc));
            SendOk(ctx, b);
        }
    }
}
