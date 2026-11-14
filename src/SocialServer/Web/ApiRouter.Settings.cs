using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ========================================================== settings

        private void HandleAdminSettings(ApiCtx ctx)
        {
            if (!IsMethod(ctx.Req, "GET")) { RequireMethod(ctx.Req.Method, "GET"); }

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();

            b.Name("site").BeginObject()
                .Prop("name", _cfg.SiteName)
                .Prop("shortName", _cfg.SiteShort)
                .Prop("tagline", _cfg.SiteTagline)
                .Prop("description", _cfg.SiteDescription)
                .Prop("baseUrl", _cfg.BaseUrl)
                .Prop("motd", _cfg.Motd)
                .Prop("footer", _cfg.Footer)
                .Prop("supportEmail", _cfg.SupportEmail)
                .Prop("defaultLanguage", _cfg.DefaultLanguage)
                .Prop("defaultTheme", _cfg.DefaultTheme)
                .Prop("registration", _cfg.Registration)
                .Prop("inviteOnly", _cfg.InviteOnly)
                .Prop("firstUserAdmin", _cfg.FirstUserAdmin)
                .EndObject();

            b.Name("security").BeginObject()
                .Prop("requireCaptcha", _cfg.RequireCaptcha)
                .Prop("encryptApi", _cfg.EncryptApi)
                .Prop("adminPrivateOnly", _cfg.AdminPrivateOnly)
                .Prop("hsts", _cfg.Hsts)
                .Prop("externalMedia", _cfg.ExternalMedia)
                .Prop("allowRoleChange", _cfg.AllowRoleChange)
                .Prop("allowSelfDemote", _cfg.AllowSelfDemote)
                .Prop("allowSelfDelete", _cfg.AllowSelfDelete)
                .Prop("protectLastAdmin", _cfg.ProtectLastAdmin)
                .Prop("sessionHours", _cfg.SessionHours)
                .Prop("maxFailedLoginsPerIp", _cfg.MaxFailedLoginsPerIp)
                .Prop("loginLockSeconds", _cfg.LoginLockSeconds)
                .EndObject();

            b.Name("limits").BeginObject()
                .Prop("requestsPerMinute", _cfg.RequestsPerMinute)
                .Prop("registrationsPerHour", _cfg.RegistrationsPerHour)
                .Prop("postsPer10Min", _cfg.PostsPer10Min)
                .Prop("commentsPer10Min", _cfg.CommentsPer10Min)
                .Prop("messagesPerMin", _cfg.MessagesPerMin)
                .Prop("maxPostLength", _cfg.MaxPostLength)
                .Prop("maxCommentLength", _cfg.MaxCommentLength)
                .Prop("maxMessageLength", _cfg.MaxMessageLength)
                .Prop("maxMessageImageKB", _cfg.MaxMessageImageKB)
                .Prop("maxVoiceKB", _cfg.MaxVoiceKB)
                .Prop("maxVoiceSec", _cfg.MaxVoiceSec)
                .Prop("maxFileKB", _cfg.MaxFileKB)
                .Prop("maxAudioKB", _cfg.MaxAudioKB)
                .Prop("maxVideoKB", _cfg.MaxVideoKB)
                .Prop("maxAudioFiles", _cfg.MaxAudioFiles)
                .Prop("maxVideoFiles", _cfg.MaxVideoFiles)
                .Prop("feedbackPerHour", _cfg.FeedbackPerHour)
                .Prop("maxAvatarKB", _cfg.MaxAvatarKB)
                .Prop("maxPostImageKB", _cfg.MaxPostImageKB)
                .Prop("editWindowMin", _cfg.EditWindowMin)
                .EndObject();

            b.Name("server").BeginObject()
                .Prop("port", _cfg.Port)
                .Prop("bind", _cfg.Bind)
                .Prop("adminPort", _cfg.AdminPort)
                .Prop("adminBind", _cfg.AdminBind)
                .Prop("adminEnabled", _cfg.AdminEnabled)
                .Prop("publicBasePath", _cfg.PublicBasePath)
                .Prop("cookieSecure", _cfg.CookieSecure)
                .EndObject();

            b.Name("ui").BeginObject()
                .Prop("refreshMs", _cfg.RefreshMs)
                .Prop("onlineWindowSec", _cfg.OnlineWindowSec)
                .Prop("allowThemes", _cfg.AllowThemes)
                .Prop("allowSounds", _cfg.AllowSounds)
                .Prop("liveNotifications", _cfg.LiveNotifications)
                .Prop("notifyWaitSec", _cfg.NotifyWaitSec)
                .EndObject();

            b.Name("logging").BeginObject()
                .Prop("level", _cfg.LogLevel.ToString())
                .Prop("apiTrace", _cfg.ApiTrace)
                .Prop("maxFileMB", _cfg.LogMaxFileMB)
                .Prop("retentionDays", _cfg.LogRetentionDays)
                .EndObject();

            b.Name("storage").BeginObject()
                .Prop("backupIntervalMin", _cfg.BackupIntervalMin)
                .Prop("backupKeep", _cfg.BackupKeep)
                .Prop("saveDebounceMs", _cfg.SaveDebounceMs)
                .Prop("readOnlyOnError", _cfg.ReadOnlyOnError)
                .EndObject();

            b.Prop("configPath", _cfg.ConfigPath);
            b.Prop("note", "Port, bind, web root and data dir need a restart.");
            SendOk(ctx, b);
        }

        private void HandleAdminSettingsSave(ApiCtx ctx)
        {
            Dictionary<string, object> site = Json.Get(ctx.Body, "site") as Dictionary<string, object>;
            Dictionary<string, object> security = Json.Get(ctx.Body, "security") as Dictionary<string, object>;
            Dictionary<string, object> limits = Json.Get(ctx.Body, "limits") as Dictionary<string, object>;
            Dictionary<string, object> ui = Json.Get(ctx.Body, "ui") as Dictionary<string, object>;
            Dictionary<string, object> logging = Json.Get(ctx.Body, "logging") as Dictionary<string, object>;
            Dictionary<string, object> storage = Json.Get(ctx.Body, "storage") as Dictionary<string, object>;
            Dictionary<string, object> server = Json.Get(ctx.Body, "server") as Dictionary<string, object>;

            List<string> warnings = new List<string>();

            // ------------------------------------------------------------- site
            if (site != null)
            {
                if (site.ContainsKey("name"))
                {
                    string name = Util.SingleLine(Json.GetString(site, "name", _cfg.SiteName), 60);
                    if (name.Length == 0) throw new ApiException(400, "bad-name", "Site name cannot be empty");
                    _cfg.SiteName = name;
                    _cfg.SetIniValue("site", "name", name);
                }
                if (site.ContainsKey("shortName"))
                {
                    string shortName = Util.SingleLine(Json.GetString(site, "shortName", _cfg.SiteShort), 3);
                    if (shortName.Length == 0) shortName = "S";
                    _cfg.SiteShort = shortName;
                    _cfg.SetIniValue("site", "shortName", shortName);
                }
                if (site.ContainsKey("tagline"))
                {
                    _cfg.SiteTagline = Util.SingleLine(Json.GetString(site, "tagline", ""), 120);
                    _cfg.SetIniValue("site", "tagline", _cfg.SiteTagline);
                }
                if (site.ContainsKey("description"))
                {
                    _cfg.SiteDescription = Util.SingleLine(Json.GetString(site, "description", ""), 300);
                    _cfg.SetIniValue("site", "description", _cfg.SiteDescription);
                }
                if (site.ContainsKey("baseUrl"))
                {
                    string url = Json.GetString(site, "baseUrl", "").Trim();
                    if (url.Length > 0 && !Util.IsHttpUrl(url))
                        throw new ApiException(400, "bad-base-url", "baseUrl must start with http:// or https://");
                    _cfg.BaseUrl = url;
                    _cfg.SetIniValue("site", "baseUrl", url);
                }
                if (site.ContainsKey("motd"))
                {
                    _cfg.Motd = Util.CleanText(Json.GetString(site, "motd", ""), 500);
                    _cfg.SetIniValue("site", "motd", _cfg.Motd);
                }
                if (site.ContainsKey("footer"))
                {
                    _cfg.Footer = Util.SingleLine(Json.GetString(site, "footer", ""), 200);
                    _cfg.SetIniValue("site", "footer", _cfg.Footer);
                }
                if (site.ContainsKey("supportEmail"))
                {
                    string email = Json.GetString(site, "supportEmail", "").Trim();
                    if (email.Length > 0 && !Util.IsValidEmail(email))
                        throw new ApiException(400, "bad-email", "Bad support email");
                    _cfg.SupportEmail = email;
                    _cfg.SetIniValue("site", "supportEmail", email);
                }
                if (site.ContainsKey("defaultLanguage"))
                {
                    string lang = Json.GetString(site, "defaultLanguage", "").Trim().ToLowerInvariant();
                    if (lang != "ru" && lang != "en" && lang != "uk" && lang != "lt")
                        throw new ApiException(400, "bad-lang", "Unknown language");
                    _cfg.DefaultLanguage = lang;
                    _cfg.SetIniValue("site", "defaultLanguage", lang);
                }
                if (site.ContainsKey("defaultTheme"))
                {
                    string theme = Json.GetString(site, "defaultTheme", "").Trim().ToLowerInvariant();
                    if (theme != "light" && theme != "dark" && theme != "oled")
                        throw new ApiException(400, "bad-theme", "Unknown theme");
                    _cfg.DefaultTheme = theme;
                    _cfg.SetIniValue("site", "defaultTheme", theme);
                }
                if (site.ContainsKey("registration"))
                {
                    _cfg.Registration = Json.GetBool(site, "registration", _cfg.Registration);
                    _cfg.SetIniValue("site", "registration", _cfg.Registration ? "true" : "false");
                }
                if (site.ContainsKey("inviteOnly"))
                {
                    _cfg.InviteOnly = Json.GetBool(site, "inviteOnly", _cfg.InviteOnly);
                    _cfg.SetIniValue("site", "inviteOnly", _cfg.InviteOnly ? "true" : "false");
                }
                if (site.ContainsKey("firstUserAdmin"))
                {
                    _cfg.FirstUserAdmin = Json.GetBool(site, "firstUserAdmin", _cfg.FirstUserAdmin);
                    _cfg.SetIniValue("site", "firstUserAdmin", _cfg.FirstUserAdmin ? "true" : "false");
                }
            }

            // --------------------------------------------------------- security
            if (security != null)
            {
                if (security.ContainsKey("requireCaptcha"))
                {
                    _cfg.RequireCaptcha = Json.GetBool(security, "requireCaptcha", _cfg.RequireCaptcha);
                    _cfg.SetIniValue("security", "requireCaptcha", _cfg.RequireCaptcha ? "true" : "false");
                }
                if (security.ContainsKey("encryptApi"))
                {
                    _cfg.EncryptApi = Json.GetBool(security, "encryptApi", _cfg.EncryptApi);
                    _cfg.SetIniValue("security", "encryptApi", _cfg.EncryptApi ? "true" : "false");
                    warnings.Add("encryptApi changes take effect for new requests immediately");
                }
                if (security.ContainsKey("adminPrivateOnly"))
                {
                    _cfg.AdminPrivateOnly = Json.GetBool(security, "adminPrivateOnly", _cfg.AdminPrivateOnly);
                    _cfg.SetIniValue("security", "adminPrivateOnly", _cfg.AdminPrivateOnly ? "true" : "false");
                }
                if (security.ContainsKey("hsts"))
                {
                    _cfg.Hsts = Json.GetBool(security, "hsts", _cfg.Hsts);
                    _cfg.SetIniValue("security", "hsts", _cfg.Hsts ? "true" : "false");
                }
                if (security.ContainsKey("externalMedia"))
                {
                    _cfg.ExternalMedia = Json.GetBool(security, "externalMedia", _cfg.ExternalMedia);
                    _cfg.SetIniValue("security", "externalMedia", _cfg.ExternalMedia ? "true" : "false");
                }
                if (security.ContainsKey("allowRoleChange"))
                {
                    _cfg.AllowRoleChange = Json.GetBool(security, "allowRoleChange", _cfg.AllowRoleChange);
                    _cfg.SetIniValue("security", "allowRoleChange", _cfg.AllowRoleChange ? "true" : "false");
                }
                if (security.ContainsKey("allowSelfDemote"))
                {
                    _cfg.AllowSelfDemote = Json.GetBool(security, "allowSelfDemote", _cfg.AllowSelfDemote);
                    _cfg.SetIniValue("security", "allowSelfDemote", _cfg.AllowSelfDemote ? "true" : "false");
                }
                if (security.ContainsKey("allowSelfDelete"))
                {
                    _cfg.AllowSelfDelete = Json.GetBool(security, "allowSelfDelete", _cfg.AllowSelfDelete);
                    _cfg.SetIniValue("security", "allowSelfDelete", _cfg.AllowSelfDelete ? "true" : "false");
                }
                if (security.ContainsKey("protectLastAdmin"))
                {
                    _cfg.ProtectLastAdmin = Json.GetBool(security, "protectLastAdmin", _cfg.ProtectLastAdmin);
                    _cfg.SetIniValue("security", "protectLastAdmin", _cfg.ProtectLastAdmin ? "true" : "false");
                }
                if (security.ContainsKey("sessionHours"))
                {
                    int hours = Json.GetInt(security, "sessionHours", _cfg.SessionHours);
                    if (hours < 1 || hours > 8760) throw new ApiException(400, "bad-session", "Session hours must be 1..8760");
                    _cfg.SessionHours = hours;
                    _cfg.SetIniValue("server", "sessionHours", hours.ToString(CultureInfo.InvariantCulture));
                }
                if (security.ContainsKey("maxFailedLoginsPerIp"))
                {
                    int v = Json.GetInt(security, "maxFailedLoginsPerIp", _cfg.MaxFailedLoginsPerIp);
                    if (v < 3) v = 3;
                    _cfg.MaxFailedLoginsPerIp = v;
                    _cfg.SetIniValue("security", "maxFailedLoginsPerIp", v.ToString(CultureInfo.InvariantCulture));
                }
                if (security.ContainsKey("loginLockSeconds"))
                {
                    int v = Json.GetInt(security, "loginLockSeconds", _cfg.LoginLockSeconds);
                    if (v < 10) v = 10;
                    _cfg.LoginLockSeconds = v;
                    _cfg.SetIniValue("security", "loginLockSeconds", v.ToString(CultureInfo.InvariantCulture));
                }
            }

            // ----------------------------------------------------------- limits
            if (limits != null)
            {
                ApplyLimit(limits, "requestsPerMinute", _cfg.RequestsPerMinute, 10, delegate(int v) { _cfg.RequestsPerMinute = v; });
                ApplyLimit(limits, "registrationsPerHour", _cfg.RegistrationsPerHour, 1, delegate(int v) { _cfg.RegistrationsPerHour = v; });
                ApplyLimit(limits, "postsPer10Min", _cfg.PostsPer10Min, 1, delegate(int v) { _cfg.PostsPer10Min = v; });
                ApplyLimit(limits, "commentsPer10Min", _cfg.CommentsPer10Min, 1, delegate(int v) { _cfg.CommentsPer10Min = v; });
                ApplyLimit(limits, "messagesPerMin", _cfg.MessagesPerMin, 1, delegate(int v) { _cfg.MessagesPerMin = v; });
                ApplyLimit(limits, "maxPostLength", _cfg.MaxPostLength, 200, delegate(int v) { _cfg.MaxPostLength = v; });
                ApplyLimit(limits, "maxCommentLength", _cfg.MaxCommentLength, 100, delegate(int v) { _cfg.MaxCommentLength = v; });
                ApplyLimit(limits, "maxMessageLength", _cfg.MaxMessageLength, 100, delegate(int v) { _cfg.MaxMessageLength = v; });
                ApplyLimit(limits, "maxMessageImageKB", _cfg.MaxMessageImageKB, 64, delegate(int v) { _cfg.MaxMessageImageKB = v; });
                ApplyLimit(limits, "maxVoiceKB", _cfg.MaxVoiceKB, 64, delegate(int v) { _cfg.MaxVoiceKB = v; });
                ApplyLimit(limits, "maxVoiceSec", _cfg.MaxVoiceSec, 5, delegate(int v) { _cfg.MaxVoiceSec = v; });
                ApplyLimit(limits, "maxFileKB", _cfg.MaxFileKB, 64, delegate(int v) { _cfg.MaxFileKB = v; });
                ApplyLimit(limits, "maxAudioKB", _cfg.MaxAudioKB, 256, delegate(int v) { _cfg.MaxAudioKB = v; });
                ApplyLimit(limits, "maxVideoKB", _cfg.MaxVideoKB, 256, delegate(int v) { _cfg.MaxVideoKB = v; });
                ApplyLimit(limits, "maxAudioFiles", _cfg.MaxAudioFiles, 1, delegate(int v) { _cfg.MaxAudioFiles = v; });
                ApplyLimit(limits, "maxVideoFiles", _cfg.MaxVideoFiles, 1, delegate(int v) { _cfg.MaxVideoFiles = v; });
                ApplyLimit(limits, "feedbackPerHour", _cfg.FeedbackPerHour, 1, delegate(int v) { _cfg.FeedbackPerHour = v; });
                ApplyLimit(limits, "maxAvatarKB", _cfg.MaxAvatarKB, 16, delegate(int v) { _cfg.MaxAvatarKB = v; });
                ApplyLimit(limits, "maxPostImageKB", _cfg.MaxPostImageKB, 64, delegate(int v) { _cfg.MaxPostImageKB = v; });
                ApplyLimit(limits, "editWindowMin", _cfg.EditWindowMin, 0, delegate(int v) { _cfg.EditWindowMin = v; });
            }

            // --------------------------------------------------------------- ui
            if (ui != null)
            {
                if (ui.ContainsKey("refreshMs"))
                {
                    int v = Json.GetInt(ui, "refreshMs", _cfg.RefreshMs);
                    if (v < 1000) v = 1000;
                    if (v > 60000) v = 60000;
                    _cfg.RefreshMs = v;
                    _cfg.SetIniValue("ui", "refreshMs", v.ToString(CultureInfo.InvariantCulture));
                }
                if (ui.ContainsKey("onlineWindowSec"))
                {
                    int v = Json.GetInt(ui, "onlineWindowSec", _cfg.OnlineWindowSec);
                    if (v < 30) v = 30;
                    _cfg.OnlineWindowSec = v;
                    _cfg.SetIniValue("ui", "onlineWindowSec", v.ToString(CultureInfo.InvariantCulture));
                }
                if (ui.ContainsKey("allowThemes"))
                {
                    _cfg.AllowThemes = Util.SingleLine(Json.GetString(ui, "allowThemes", _cfg.AllowThemes), 60);
                    _cfg.SetIniValue("ui", "allowThemes", _cfg.AllowThemes);
                }
                if (ui.ContainsKey("allowSounds"))
                {
                    _cfg.AllowSounds = Json.GetBool(ui, "allowSounds", _cfg.AllowSounds);
                    _cfg.SetIniValue("ui", "allowSounds", _cfg.AllowSounds ? "true" : "false");
                }
                if (ui.ContainsKey("liveNotifications"))
                {
                    _cfg.LiveNotifications = Json.GetBool(ui, "liveNotifications", _cfg.LiveNotifications);
                    _cfg.SetIniValue("ui", "liveNotifications", _cfg.LiveNotifications ? "true" : "false");
                }
                if (ui.ContainsKey("notifyWaitSec"))
                {
                    int v = Json.GetInt(ui, "notifyWaitSec", _cfg.NotifyWaitSec);
                    if (v < 1) v = 1;
                    if (v > 25) v = 25;
                    _cfg.NotifyWaitSec = v;
                    _cfg.SetIniValue("ui", "notifyWaitSec", v.ToString(CultureInfo.InvariantCulture));
                }
            }

            // ---------------------------------------------------------- logging
            if (logging != null)
            {
                if (logging.ContainsKey("level"))
                {
                    string level = Json.GetString(logging, "level", "Info").Trim().ToLowerInvariant();
                    LogLevel parsed = LogLevel.Info;
                    if (level == "error") parsed = LogLevel.Error;
                    else if (level == "warn") parsed = LogLevel.Warn;
                    else if (level == "info") parsed = LogLevel.Info;
                    else if (level == "debug") parsed = LogLevel.Debug;
                    else if (level == "verbose") parsed = LogLevel.Verbose;
                    else throw new ApiException(400, "bad-level", "Unknown log level");
                    _cfg.LogLevel = parsed;
                    _cfg.SetIniValue("logging", "level", parsed.ToString());
                    Log.Configure(_cfg);
                }
                if (logging.ContainsKey("apiTrace"))
                {
                    _cfg.ApiTrace = Json.GetBool(logging, "apiTrace", _cfg.ApiTrace);
                    _cfg.SetIniValue("logging", "apiTrace", _cfg.ApiTrace ? "true" : "false");
                    Log.Configure(_cfg);
                }
                if (logging.ContainsKey("maxFileMB"))
                {
                    int v = Json.GetInt(logging, "maxFileMB", _cfg.LogMaxFileMB);
                    if (v < 1) v = 1;
                    _cfg.LogMaxFileMB = v;
                    _cfg.SetIniValue("logging", "maxFileMB", v.ToString(CultureInfo.InvariantCulture));
                }
                if (logging.ContainsKey("retentionDays"))
                {
                    int v = Json.GetInt(logging, "retentionDays", _cfg.LogRetentionDays);
                    if (v < 0) v = 0;
                    _cfg.LogRetentionDays = v;
                    _cfg.SetIniValue("logging", "retentionDays", v.ToString(CultureInfo.InvariantCulture));
                }
            }

            // ---------------------------------------------------------- storage
            if (storage != null)
            {
                if (storage.ContainsKey("backupIntervalMin"))
                {
                    int v = Json.GetInt(storage, "backupIntervalMin", _cfg.BackupIntervalMin);
                    if (v < 0) v = 0;
                    _cfg.BackupIntervalMin = v;
                    _cfg.SetIniValue("storage", "backupIntervalMin", v.ToString(CultureInfo.InvariantCulture));
                }
                if (storage.ContainsKey("backupKeep"))
                {
                    int v = Json.GetInt(storage, "backupKeep", _cfg.BackupKeep);
                    if (v < 1) v = 1;
                    _cfg.BackupKeep = v;
                    _cfg.SetIniValue("storage", "backupKeep", v.ToString(CultureInfo.InvariantCulture));
                }
                if (storage.ContainsKey("saveDebounceMs"))
                {
                    int v = Json.GetInt(storage, "saveDebounceMs", _cfg.SaveDebounceMs);
                    if (v < 100) v = 100;
                    _cfg.SaveDebounceMs = v;
                    _cfg.SetIniValue("storage", "saveDebounceMs", v.ToString(CultureInfo.InvariantCulture));
                }
                if (storage.ContainsKey("readOnlyOnError"))
                {
                    _cfg.ReadOnlyOnError = Json.GetBool(storage, "readOnlyOnError", _cfg.ReadOnlyOnError);
                    _cfg.SetIniValue("storage", "readOnlyOnError", _cfg.ReadOnlyOnError ? "true" : "false");
                }
            }

            // ----------------------------------------------------------- server
            if (server != null)
            {
                if (server.ContainsKey("publicBasePath"))
                {
                    string p = Json.GetString(server, "publicBasePath", "").Trim();
                    if (p.Length > 0 && !p.StartsWith("/", StringComparison.Ordinal)) p = "/" + p;
                    if (p.EndsWith("/", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
                    _cfg.PublicBasePath = p;
                    _cfg.SetIniValue("server", "publicBasePath", p);
                    warnings.Add("publicBasePath needs a restart");
                }
                if (server.ContainsKey("cookieSecure"))
                {
                    string v = Json.GetString(server, "cookieSecure", "auto").Trim().ToLowerInvariant();
                    if (v != "auto" && v != "true" && v != "false")
                        throw new ApiException(400, "bad-cookie", "cookieSecure must be auto, true or false");
                    _cfg.CookieSecure = v;
                    _cfg.SetIniValue("server", "cookieSecure", v);
                }
                if (server.ContainsKey("port") || server.ContainsKey("bind") ||
                    server.ContainsKey("adminPort") || server.ContainsKey("adminBind"))
                {
                    warnings.Add("port / bind / adminPort need a restart");
                }
            }

            Log.Info("ADMIN", "@" + ctx.User.Username + " updated site settings");

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("warnings").BeginArray();
            for (int i = 0; i < warnings.Count; i++) b.Str(warnings[i]);
            b.EndArray();
            SendOk(ctx, b);
        }

        private void ApplyLimit(Dictionary<string, object> src, string key, int current, int minimum, Action<int> setter)
        {
            if (!src.ContainsKey(key)) return;
            int v = Json.GetInt(src, key, current);
            if (v < minimum) v = minimum;
            setter(v);
            _cfg.SetIniValue("limits", key, v.ToString(CultureInfo.InvariantCulture));
        }
    }
}
