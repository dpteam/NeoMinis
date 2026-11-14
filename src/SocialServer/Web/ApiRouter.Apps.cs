using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ================================================================ apps

        /// <summary>
        /// One app as its owner sees it: the client id is public by design, the
        /// secret never appears here because only its hash is stored.
        /// </summary>
        private string AppJson(App a, User viewer, bool mine)
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", a.Id);
            b.Prop("name", a.Name);
            b.Prop("description", a.Description);
            b.Prop("icon", a.Icon);
            b.Prop("type", a.Type);
            b.Prop("url", a.Url);
            b.Prop("clientId", a.ClientId);
            b.Prop("scopes", a.Scopes);
            b.Prop("published", a.Published);
            b.Prop("installs", a.Installs);
            b.Prop("created", Util.Iso(a.Created));
            b.Prop("ownerName", OwnerName(a.Owner));
            b.Prop("mine", viewer != null && viewer.Id == a.Owner);
            if (mine)
            {
                // the owner needs to know who installed it, and whether they are in
                AppGrant grant = viewer == null ? null : _store.GrantFor(a.Id, viewer.Id);
                b.Prop("installed", grant != null);
                b.Prop("grantedScopes", grant == null ? "" : grant.Scopes);
            }
            b.EndObject();
            return b.ToString();
        }

        /// <summary>A short, unguessable token. High entropy, so one hash is enough.</summary>
        private string AppSecretHash(string value)
        {
            return Crypto.HmacHex(Encoding.UTF8.GetBytes(_cfg.ServerSecret), value ?? "");
        }

        private App RequireApp(long id)
        {
            App a = _store.FindApp(id);
            if (a == null) throw new ApiException(404, "no-app", "App not found");
            return a;
        }

        private void RequireAppOwner(App a, User user)
        {
            if (a.Owner != user.Id && !user.IsAdmin)
                throw new ApiException(403, "not-owner", "Only the developer or an administrator can change this app");
        }

        // ------------------------------------------------------------ catalogue

        private void HandleApps(ApiCtx ctx)
        {
            List<App> published = _store.Apps(true);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("apps").BeginArray();
            for (int i = 0; i < published.Count; i++) b.Raw(AppJson(published[i], ctx.User, false));
            b.EndArray();
            b.Name("scopes").BeginArray();
            for (int i = 0; i < AppScopes.All.Length; i++) b.Str(AppScopes.All[i]);
            b.EndArray();
            // who may publish what: an html app runs on our own origin, so only staff
            b.Prop("canPublishHtml", ctx.User.IsModerator);
            SendOk(ctx, b);
        }

        private void HandleAppsMine(ApiCtx ctx)
        {
            List<App> mine = _store.AppsByOwner(ctx.User.Id);
            List<AppGrant> grants = _store.GrantsFor(ctx.User.Id);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("apps").BeginArray();
            for (int i = 0; i < mine.Count; i++) b.Raw(AppJson(mine[i], ctx.User, true));
            b.EndArray();
            // the apps this user approved, whoever wrote them
            b.Name("grants").BeginArray();
            for (int i = 0; i < grants.Count; i++)
            {
                AppGrant g = grants[i];
                App a = _store.FindApp(g.AppId);
                if (a == null) continue;
                b.BeginObject()
                    .Prop("appId", a.Id)
                    .Prop("name", a.Name)
                    .Prop("icon", a.Icon)
                    .Prop("type", a.Type)
                    .Prop("scopes", g.Scopes)
                    .Prop("created", Util.Iso(g.Created))
                    .Prop("lastUsed", g.LastUsed == DateTime.MinValue ? "" : Util.Iso(g.LastUsed))
                    .EndObject();
            }
            b.EndArray();
            b.Name("scopes").BeginArray();
            for (int i = 0; i < AppScopes.All.Length; i++) b.Str(AppScopes.All[i]);
            b.EndArray();
            b.Prop("canPublishHtml", ctx.User.IsModerator);
            SendOk(ctx, b);
        }

        /// <summary>
        /// Reads a scope set from the body. An array is what a browser sends, a
        /// comma separated string is what curl and the CLI send; both are honest
        /// ways to say the same thing.
        /// </summary>
        private List<string> BodyScopes(ApiCtx ctx, string key)
        {
            List<object> list = Json.Get(ctx.Body, key) as List<object>;
            if (list != null)
            {
                List<string> raw = new List<string>();
                for (int i = 0; i < list.Count; i++)
                    raw.Add(Convert.ToString(list[i], CultureInfo.InvariantCulture));
                return AppScopes.Parse(string.Join(",", raw.ToArray()));
            }
            return AppScopes.Parse(BodyStr(ctx, key, ""));
        }

        private void HandleAppRegister(ApiCtx ctx)
        {
            if (!_cfg.AppsEnabled) throw new ApiException(403, "apps-off", "Apps are switched off here");

            string name = Util.SingleLine(BodyStr(ctx, "name", ""), 60).Trim();
            if (name.Length < 2) throw new ApiException(400, "bad-name", "An app needs a name of 2+ characters");
            string type = BodyStr(ctx, "type", "external").Trim().ToLowerInvariant();
            if (!AppScopes.KnownType(type)) throw new ApiException(400, "bad-type", "Unknown app type");
            if (type == "html" && !ctx.User.IsModerator)
                throw new ApiException(403, "html-admin-only",
                    "Only staff may publish an HTML app: it runs on this site's own origin");

            string url = BodyStr(ctx, "url", "").Trim();
            if (url.Length > 0 && !Util.IsHttpUrl(url))
                throw new ApiException(400, "bad-url", "The app URL must start with http:// or https://");
            if (type == "html" && url.Length == 0)
                throw new ApiException(400, "bad-url", "An HTML app needs a URL");

            string scopes = AppScopes.Serialize(BodyScopes(ctx, "scopes"));
            string icon = Util.SingleLine(BodyStr(ctx, "icon", ""), 40);
            string description = Util.CleanText(BodyStr(ctx, "description", ""), 500);

            string clientId = "app_" + Crypto.RandomHex(8);
            string secret = Crypto.RandomHex(24);

            App app = _store.CreateApp(ctx.User.Id, name, description, icon, type, url,
                                       clientId, AppSecretHash(secret), scopes);
            if (app == null) throw new ApiException(503, "read-only", "Storage is read-only right now");
            Log.Info("APPS", "@" + ctx.User.Username + " registered app #" + app.Id + " " + app.Name);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("app").Raw(AppJson(app, ctx.User, true));
            // the one and only time the secret is visible
            b.Prop("secret", secret);
            SendOk(ctx, b);
        }

        private void HandleAppUpdate(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            App app = RequireApp(id);
            RequireAppOwner(app, ctx.User);

            string name = ctx.Body.ContainsKey("name") ? Util.SingleLine(BodyStr(ctx, "name", app.Name), 60) : app.Name;
            if (name.Length < 2) throw new ApiException(400, "bad-name", "An app needs a name of 2+ characters");
            string description = ctx.Body.ContainsKey("description")
                ? Util.CleanText(BodyStr(ctx, "description", ""), 500) : app.Description;
            string icon = ctx.Body.ContainsKey("icon") ? Util.SingleLine(BodyStr(ctx, "icon", ""), 40) : app.Icon;
            string url = ctx.Body.ContainsKey("url") ? BodyStr(ctx, "url", "").Trim() : app.Url;
            if (url.Length > 0 && !Util.IsHttpUrl(url))
                throw new ApiException(400, "bad-url", "The app URL must start with http:// or https://");
            string scopes = ctx.Body.ContainsKey("scopes")
                ? AppScopes.Serialize(BodyScopes(ctx, "scopes")) : null;

            bool? published = null;
            if (ctx.Body.ContainsKey("published"))
            {
                bool want = BodyBool(ctx, "published", app.Published);
                // an html app is code on our origin: only staff may switch it on
                if (want && app.Type == "html" && !ctx.User.IsModerator)
                    throw new ApiException(403, "html-admin-only", "Only staff may publish an HTML app");
                published = want;
            }

            if (!_store.UpdateApp(id, name, description, icon, url, scopes, published))
                throw new ApiException(404, "no-app", "App not found");
            Log.Info("APPS", "@" + ctx.User.Username + " updated app #" + id);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("app").Raw(AppJson(app, ctx.User, true));
            SendOk(ctx, b);
        }

        private void HandleAppDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            App app = RequireApp(id);
            RequireAppOwner(app, ctx.User);

            int removed;
            if (!_store.DeleteApp(id, out removed)) throw new ApiException(404, "no-app", "App not found");
            Log.Warn("APPS", "@" + ctx.User.Username + " deleted app #" + id + " (" + removed + " rows)");

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("deleted", true);
            b.Prop("records", removed);
            SendOk(ctx, b);
        }

        // -------------------------------------------------------------- consent

        /// <summary>
        /// The user approves a set of scopes. What comes back is a one-time code
        /// the app exchanges for a token, which is how OAuth-style consent works
        /// and keeps the token out of the browser history.
        /// </summary>
        private void HandleAppAuthorize(ApiCtx ctx)
        {
            if (!_cfg.AppsEnabled) throw new ApiException(403, "apps-off", "Apps are switched off here");
            long id = BodyLong(ctx, "id", 0);
            App app = RequireApp(id);
            if (!app.Published && app.Owner != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-published", "This app is not published yet");

            // the approved set must be a subset of what the app asked for
            List<string> requested = AppScopes.Parse(app.Scopes);
            List<string> wanted = ctx.Body.ContainsKey("scopes")
                ? BodyScopes(ctx, "scopes")
                : new List<string>(requested);
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!requested.Contains(wanted[i]))
                    throw new ApiException(400, "scope-not-offered",
                        "The app did not ask for \"" + wanted[i] + "\"");
            }
            if (wanted.Count == 0)
                throw new ApiException(400, "no-scopes", "Approve at least one permission");

            string code = Crypto.RandomHex(16);
            DateTime expires = DateTime.UtcNow.AddMinutes(10);
            _store.SaveGrant(app.Id, ctx.User.Id, AppScopes.Serialize(wanted), code, expires);
            Log.Info("APPS", "@" + ctx.User.Username + " approved app #" + app.Id + " [" +
                             AppScopes.Serialize(wanted) + "]");

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("code", code);
            b.Prop("expires", Util.Iso(expires));
            b.Prop("scopes", AppScopes.Serialize(wanted));
            SendOk(ctx, b);
        }

        private void HandleAppRevoke(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            App app = RequireApp(id);
            bool any = _store.RevokeGrant(id, ctx.User.Id);
            Log.Info("APPS", "@" + ctx.User.Username + " revoked app #" + id);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("revoked", any);
            SendOk(ctx, b);
        }

        /// <summary>
        /// Exchanges the one-time code for a token. Unauthenticated by design: the
        /// app proves itself with its client id and secret, exactly like OAuth.
        /// </summary>
        private void HandleAppToken(HttpRequest req, HttpResponse resp)
        {
            if (!IsMethod(req, "POST")) throw new ApiException(405, "method", "POST only");
            if (!_cfg.AppsEnabled) throw new ApiException(403, "apps-off", "Apps are switched off here");

            Dictionary<string, object> body = Json.ParseObject(req.BodyText(), _cfg.MaxJsonDepth);
            string clientId = Json.GetString(body, "clientId", "").Trim();
            string secret = Json.GetString(body, "clientSecret", "").Trim();
            string code = Json.GetString(body, "code", "").Trim();
            if (clientId.Length == 0 || secret.Length == 0 || code.Length == 0)
                throw new ApiException(400, "missing", "clientId, clientSecret and code are required");

            App app = _store.FindAppByClientId(clientId);
            if (app == null) throw new ApiException(401, "bad-client", "Unknown client id");
            if (!Crypto.FixedEquals(AppSecretHash(secret), app.SecretHash))
                throw new ApiException(401, "bad-secret", "Wrong client secret");

            AppGrant grant = _store.FindGrantByCode(code);
            if (grant == null || grant.AppId != app.Id)
                throw new ApiException(400, "bad-code", "That code is not valid for this app");
            if (grant.CodeExpires != DateTime.MinValue && grant.CodeExpires <= DateTime.UtcNow)
                throw new ApiException(400, "code-expired", "That code has expired, ask the user again");

            string token = Crypto.RandomHex(32);
            _store.SetGrantToken(grant.Id, AppSecretHash(token));
            _store.BumpAppInstalls(app.Id);
            User user = _store.FindUser(grant.User);
            Log.Info("APPS", "app #" + app.Id + " exchanged a code for a token for @" +
                             (user == null ? "?" : user.Username));

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("token", token);
            b.Prop("scopes", grant.Scopes);
            b.Prop("userId", grant.User);
            b.Name("user").Raw(UserSummary(user));
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }

        // ----------------------------------------------------------- app scores

        private void HandleAppScores(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            App app = RequireApp(id);
            int limit = LimitParam(ctx, 20, 100);
            List<AppScore> top = _store.TopScores(id, limit);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Prop("app", app.Name);
            b.Name("scores").BeginArray();
            for (int i = 0; i < top.Count; i++)
            {
                AppScore s = top[i];
                User u = _store.FindUser(s.User);
                b.BeginObject()
                    .Prop("rank", i + 1)
                    .Prop("score", s.Score)
                    .Prop("user", s.User)
                    .Prop("name", u == null ? "?" : (u.DisplayName.Length > 0 ? u.DisplayName : u.Username))
                    .Prop("updated", Util.Iso(s.Updated))
                    .EndObject();
            }
            b.EndArray();
            AppScore mine = _store.ScoreFor(id, ctx.User.Id);
            b.Prop("mine", mine == null ? 0 : mine.Score);
            SendOk(ctx, b);
        }

        private void HandleAppScoreSubmit(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            App app = RequireApp(id);
            long score = BodyLong(ctx, "score", 0);
            if (score < 0) score = 0;
            if (score > 1000000000L) score = 1000000000L;

            bool improved;
            int rank = _store.SubmitScore(id, ctx.User.Id, score, out improved);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("score", score);
            b.Prop("rank", rank);
            b.Prop("improved", improved);
            SendOk(ctx, b);
        }

        // ------------------------------------------------------- token dispatch

        /// <summary>
        /// Everything an app can do with a token. Separate from the session router
        /// on purpose: the transport is plain JSON, because a third-party server
        /// cannot produce our HMAC signatures.
        /// </summary>
        public bool HandleAppRequest(HttpRequest req, HttpResponse resp, string ip, string route)
        {
            try
            {
                if (!_cfg.AppsEnabled) throw new ApiException(403, "apps-off", "Apps are switched off here");
                string token = req.Header("X-App-Token");
                if (string.IsNullOrEmpty(token))
                    throw new ApiException(401, "token-required", "Send the token in X-App-Token");

                AppGrant grant = _store.FindGrantByToken(AppSecretHash(token));
                if (grant == null) throw new ApiException(401, "bad-token", "Unknown or revoked token");
                App app = _store.FindApp(grant.AppId);
                if (app == null) throw new ApiException(401, "bad-token", "The app is gone");
                User user = _store.FindUser(grant.User);
                if (user == null || user.Deleted || user.Banned)
                    throw new ApiException(401, "bad-token", "The account behind this token is not usable");

                _store.TouchGrant(grant.Id);
                string sub = route.Substring(4);   // strip "app/"

                if (sub == "profile") { AppProfile(resp, app, user, grant); return true; }
                if (sub == "storage") { AppStorage(req, resp, app, user, grant); return true; }
                if (sub == "scores") { AppScoreBoard(req, resp, app, user, grant); return true; }
                if (sub == "score") { AppScorePost(req, resp, app, user, grant); return true; }
                if (sub == "post") { AppPost(req, resp, app, user, grant); return true; }
                if (sub == "messages") { AppMessages(req, resp, app, user, grant); return true; }

                throw new ApiException(404, "unknown-endpoint", "Unknown app endpoint");
            }
            catch (ApiException ex)
            {
                FailPlain(resp, ex.Status, ex.Code, ex.Message, 0);
                return true;
            }
            catch (JsonException ex)
            {
                FailPlain(resp, 400, "bad-json", ex.Message, 0);
                return true;
            }
            catch (Exception ex)
            {
                _stats.OnApiError();
                Log.Error("APPS", route + " crashed: " + ex);
                FailPlain(resp, 500, "internal", "Internal server error", 0);
                return true;
            }
        }

        private static void RequireScope(AppGrant grant, string scope)
        {
            if (!AppScopes.Has(grant.Scopes, scope))
                throw new ApiException(403, "scope-missing", "This token does not have the \"" + scope + "\" permission");
        }

        private static void SendAppJson(HttpResponse resp, int status, string json)
        {
            resp.Status = status;
            resp.ContentType = "application/json; charset=utf-8";
            resp.Body = Encoding.UTF8.GetBytes(json ?? "");
            resp.Set("Cache-Control", "no-store");
        }

        private void AppProfile(HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.Profile);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("app").BeginObject().Prop("id", app.Id).Prop("name", app.Name).EndObject();
            b.Prop("scopes", grant.Scopes);
            b.Name("user").BeginObject()
                .Prop("id", user.Id)
                .Prop("username", user.Username)
                .Prop("displayName", user.DisplayName)
                .Prop("avatar", user.Avatar)
                .EndObject();
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }

        private void AppStorage(HttpRequest req, HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.Storage);

            if (IsMethod(req, "GET"))
            {
                Dictionary<string, string> query = HttpRequest.ParseQuery(req.Query);
                string key = "";
                query.TryGetValue("key", out key);
                if (string.IsNullOrEmpty(key))
                {
                    // listing keys is handy and cheap; values are not listed
                    List<string> keys = _store.AppKeys(app.Id, user.Id, _cfg.AppStorageKeys);
                    JsonBuilder lb = new JsonBuilder(2048);
                    lb.BeginObject();
                    lb.Prop("keys", keys.Count);
                    lb.Name("list").BeginArray();
                    for (int i = 0; i < keys.Count; i++) lb.Str(keys[i]);
                    lb.EndArray();
                    lb.EndObject();
                    SendAppJson(resp, 200, lb.ToString());
                    return;
                }
                string stored = _store.GetAppValue(app.Id, user.Id, key);
                JsonBuilder vb = new JsonBuilder(1024);
                vb.BeginObject();
                vb.Prop("key", key);
                vb.Prop("value", stored);
                vb.EndObject();
                SendAppJson(resp, 200, vb.ToString());
                return;
            }

            RequireMethod(req.Method, "POST");
            Dictionary<string, object> body = Json.ParseObject(req.BodyText(), _cfg.MaxJsonDepth);
            string name = Util.SingleLine(Json.GetString(body, "key", ""), 64).Trim();
            if (name.Length == 0) throw new ApiException(400, "bad-key", "A key is required");

            if (Json.GetBool(body, "delete", false))
            {
                bool gone = _store.DeleteAppValue(app.Id, user.Id, name);
                JsonBuilder db = new JsonBuilder(128);
                db.BeginObject();
                db.Prop("deleted", gone);
                db.EndObject();
                SendAppJson(resp, 200, db.ToString());
                return;
            }

            string value = Json.GetString(body, "value", "");
            if (value.Length > _cfg.AppValueBytes)
                throw new ApiException(400, "too-large", "A value is " + _cfg.AppValueBytes + " bytes at most");
            if (_store.GetAppValue(app.Id, user.Id, name) == null &&
                _store.CountAppKeys(app.Id, user.Id) >= _cfg.AppStorageKeys)
                throw new ApiException(400, "quota", "This app already holds " + _cfg.AppStorageKeys + " keys for you");

            _store.SetAppValue(app.Id, user.Id, name, value);
            JsonBuilder ok = new JsonBuilder(128);
            ok.BeginObject();
            ok.Prop("key", name);
            ok.Prop("saved", true);
            ok.EndObject();
            SendAppJson(resp, 200, ok.ToString());
        }

        private void AppScoreBoard(HttpRequest req, HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.Score);
            List<AppScore> top = _store.TopScores(app.Id, 20);
            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Name("scores").BeginArray();
            for (int i = 0; i < top.Count; i++)
            {
                User u = _store.FindUser(top[i].User);
                b.BeginObject()
                    .Prop("rank", i + 1)
                    .Prop("score", top[i].Score)
                    .Prop("name", u == null ? "?" : (u.DisplayName.Length > 0 ? u.DisplayName : u.Username))
                    .EndObject();
            }
            b.EndArray();
            AppScore mine = _store.ScoreFor(app.Id, user.Id);
            b.Prop("mine", mine == null ? 0 : mine.Score);
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }

        private void AppScorePost(HttpRequest req, HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.Score);
            RequireMethod(req.Method, "POST");
            Dictionary<string, object> body = Json.ParseObject(req.BodyText(), _cfg.MaxJsonDepth);
            long score = Json.GetLong(body, "score", 0);
            if (score < 0) score = 0;
            if (score > 1000000000L) score = 1000000000L;

            bool improved;
            int rank = _store.SubmitScore(app.Id, user.Id, score, out improved);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("score", score);
            b.Prop("rank", rank);
            b.Prop("improved", improved);
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }

        private void AppPost(HttpRequest req, HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.WallPost);
            RequireMethod(req.Method, "POST");
            if (!Allow(user.Id, "app-post", _cfg.PostsPer10Min, 600))
                throw new ApiException(429, "slow-down", "Posting too fast");

            Dictionary<string, object> body = Json.ParseObject(req.BodyText(), _cfg.MaxJsonDepth);
            string text = Util.CleanText(Json.GetString(body, "body", ""), _cfg.MaxPostLength);
            if (text.Length == 0) throw new ApiException(400, "empty", "Write something first");

            Post post = _store.CreatePost(user.Id, 0, 0, text, "", "public");
            if (post == null) throw new ApiException(503, "read-only", "Storage is read-only right now");
            GrantBadges(user);
            NotifyMentions(user, text, "#/post/" + post.Id);
            Log.Info("APPS", "app #" + app.Id + " posted as @" + user.Username);

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("id", post.Id);
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }

        private void AppMessages(HttpRequest req, HttpResponse resp, App app, User user, AppGrant grant)
        {
            RequireScope(grant, AppScopes.MessagesRead);
            Dictionary<string, string> query = HttpRequest.ParseQuery(req.Query);
            string raw = "";
            query.TryGetValue("with", out raw);
            long with;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out with) || with <= 0)
                throw new ApiException(400, "bad-user", "Pass the other side as ?with=<id>");
            User other = _store.FindUser(with);
            if (other == null || other.Deleted)
                throw new ApiException(404, "no-user", "User not found");

            List<Message> list = _store.Conversation(user.Id, other.Id, 0, 50);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("with").BeginObject()
                .Prop("id", other.Id)
                .Prop("username", other.Username)
                .Prop("displayName", other.DisplayName)
                .EndObject();
            b.Name("messages").BeginArray();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Message m = list[i];
                b.BeginObject()
                    .Prop("id", m.Id)
                    .Prop("from", m.From)
                    .Prop("to", m.To)
                    .Prop("body", m.Body)
                    .Prop("created", Util.Iso(m.Created))
                    .EndObject();
            }
            b.EndArray();
            b.EndObject();
            SendAppJson(resp, 200, b.ToString());
        }
    }
}
