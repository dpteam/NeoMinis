using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// INI configuration. No App.config, no XML, no dependencies: the file is parsed
    /// by hand, unknown keys are ignored, bad values fall back to safe defaults and a
    /// broken file is quarantined instead of stopping the server.
    /// </summary>
    public sealed class Config
    {
        // ------------------------------------------------------------ paths
        public string BaseDir = ".";
        public string ConfigPath = "config.ini";
        public string DataDir = "data";
        public string WebRoot = "public";
        public string LogDir = "logs";
        public string MediaDir = "data/media";
        public string BackupDir = "data/backups";

        // ------------------------------------------------------------ [site]
        public string SiteName = "Social";
        public string SiteShort = "S";
        public string SiteTagline = "Your own social network";
        public string SiteDescription = "";
        public string BaseUrl = "";
        public string DefaultLanguage = "ru";
        public string DefaultTheme = "light";
        public bool DefaultSound = true;
        public bool Registration = true;
        public bool InviteOnly = false;
        public string InviteCode = "";
        public bool FirstUserAdmin = true;
        public string Motd = "";
        public string Footer = "";
        public string SupportEmail = "";

        // -------------------------------------------------------- [branding]
        // Optional palette overrides. Empty values keep the neutral palette
        // from public/assets/css/app.css, so a fresh install stays debranded.
        public string BrandAccent = "";
        public string BrandAccentHi = "";
        public string BrandAccentLo = "";
        public string BrandDarkAccent = "";
        public string BrandDarkAccentHi = "";
        public string BrandDarkAccentLo = "";
        public string BrandOledAccent = "";
        public string BrandOledAccentHi = "";
        public string BrandOledAccentLo = "";
        public string BrandLightBg = "";
        public string BrandDarkBg = "";
        public string BrandDarkPanel = "";
        public string BrandDarkPanel2 = "";
        public string BrandOledBg = "";
        public string BrandOledPanel = "";

        // -------------------------------------------------------------- [ai]
        // "off" | "markov" | "openai"
        public string AiProvider = "off";
        public bool AiEnabled = false;
        // OpenAI-compatible endpoint. Works with LM Studio, Ollama (/v1), OpenAI,
        // OpenRouter, llama.cpp server and anything else that speaks /chat/completions.
        public string AiEndpoint = "http://localhost:1234/v1/chat/completions";
        public string AiApiKey = "";
        public string AiModel = "local-model";
        public int AiTimeoutMs = 30000;
        public double AiTemperature = 0.9;
        public int AiMaxTokens = 1200;
        // How often the simulation may act (seconds), and the per-run caps.
        public int AiIntervalSec = 240;
        public int AiMaxPostsPerHour = 12;
        public int AiMaxCommentsPerHour = 20;
        public int AiMaxLikesPerHour = 30;
        public int AiMaxMessagesPerHour = 6;
        public bool AiAllowPosts = true;
        public bool AiAllowComments = true;
        public bool AiAllowLikes = true;
        public bool AiAllowMessages = false;
        // Share of actions that target a random human user instead of another bot.
        public int AiHumanTargetPercent = 60;
        public string AiSystemPrompt = "";
        public string AiStatusSuffix = "";

        // ---------------------------------------------------------- [server]
        public int Port = 8080;
        public int[] PortFallbacks = new int[] { 8081, 8082, 8083, 8084 };
        public string Bind = "0.0.0.0";

        // Admin board: its own listener so /api/admin/ simply does not exist on
        // the public port. Bind it to 127.0.0.1 or keep it on the LAN.
        public int AdminPort = 8090;
        public int[] AdminPortFallbacks = new int[] { 8091, 8092, 8093 };
        public string AdminBind = "0.0.0.0";
        public bool AdminEnabled = true;
        public int Backlog = 128;
        public int MaxConcurrentRequests = 48;
        public int RequestTimeoutMs = 20000;
        public int KeepAliveMax = 100;
        public string ShutdownMessage = "THE SERVER IS SHUTTING DOWN.";
        public string PublicBasePath = "";
        public bool TrustProxyHeaders = true;
        public string TrustedProxies = "127.0.0.1,::1,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16";
        public string CookieSecure = "auto";
        public string CookieName = "sid";
        public int SessionHours = 168;
        public int SessionMaxHours = 720;

        // -------------------------------------------------------- [security]
        public string ServerSecret = "";
        public string CookieSalt = "";
        public int KdfIterations = 12000;
        public bool EncryptApi = true;
        public bool RequireCaptcha = true;
        public int MaxFailedLoginsPerIp = 20;
        public int MaxFailedLoginsPerUser = 8;
        public int LoginLockSeconds = 180;
        public int BanSeconds = 600;
        public int MaxBodyBytes = 8 * 1024 * 1024;
        public int MaxJsonDepth = 64;
        public bool AdminPrivateOnly = false;
        public bool Hsts = false;
        // allow https images/media from other hosts (video links, link previews)
        public bool ExternalMedia = true;

        // role management policy
        public bool AllowRoleChange = true;
        public bool AllowSelfDemote = false;
        public bool ProtectLastAdmin = true;
        public bool AllowSelfDelete = false;

        // ---------------------------------------------------------- [limits]
        public int RequestsPerMinute = 900;
        public int RegistrationsPerHour = 20;
        public int PostsPer10Min = 20;
        public int CommentsPer10Min = 60;
        public int MessagesPerMin = 40;
        public int LikesPerMin = 120;
        public int SearchPerMin = 60;
        public int ReportsPerHour = 10;
        public int MaxPostLength = 8000;
        public int MaxCommentLength = 2000;
        public int MaxMessageLength = 4000;
        public int MaxStatusLength = 140;
        public int MaxBioLength = 2000;
        public int MaxNameLength = 40;
        public int MaxUsernameLength = 24;
        public int MaxAvatarKB = 512;
        public int MaxPostImageKB = 4096;
        public int HistorySize = 200;
        public int LogRingSize = 1500;
        public int NotificationsPerUser = 200;
        public int MessagesPerThread = 500;

        // --------------------------------------------------------- [storage]
        public string DataDirRaw = "data";
        public string WebRootRaw = "public";
        public string MediaDirRaw = "data/media";
        public int BackupIntervalMin = 30;
        public int BackupKeep = 24;
        public int SaveDebounceMs = 1500;
        public bool ReadOnlyOnError = true;

        // --------------------------------------------------------- [logging]
        public LogLevel LogLevel = LogLevel.Info;
        public bool ApiTrace = true;
        public int LogMaxFileMB = 8;
        public int LogRetentionDays = 14;

        // -------------------------------------------------------------- [ui]
        public int RefreshMs = 5000;
        public int OnlineWindowSec = 300;
        public string AllowThemes = "light,dark,oled";
        public bool AllowSounds = true;

        public bool Degraded;
        public string DegradedReason = "";
        public readonly List<string> Warnings = new List<string>();

        // ============================================================== load

        public static Config Load(string baseDir)
        {
            Config cfg = new Config();
            try
            {
                cfg.BaseDir = Path.GetFullPath(string.IsNullOrEmpty(baseDir) ? "." : baseDir);
                cfg.ConfigPath = Path.Combine(cfg.BaseDir, "config.ini");

                if (!File.Exists(cfg.ConfigPath)) cfg.CreateDefaultFile();
                cfg.ParseFile(cfg.ConfigPath);
                cfg.GenerateSecretsIfMissing();
                cfg.Validate();
                cfg.ResolvePaths();
                cfg.ParseFile(cfg.ConfigPath); // re-read after secrets were appended
                cfg.ResolvePaths();
            }
            catch (Exception ex)
            {
                cfg.Degraded = true;
                cfg.DegradedReason = "config: " + ex.Message;
                cfg.Warnings.Add(cfg.DegradedReason);
                cfg.ResolvePaths();
            }
            return cfg;
        }

        public void Reload()
        {
            try
            {
                ParseFile(ConfigPath);
                GenerateSecretsIfMissing();
                Validate();
                ResolvePaths();
            }
            catch (Exception ex)
            {
                Warnings.Add("reload: " + ex.Message);
            }
        }

        private void ResolvePaths()
        {
            DataDir = Full(DataDirRaw);
            WebRoot = Full(WebRootRaw);
            LogDir = Full(LogDir);
            MediaDir = Full(MediaDirRaw);
            BackupDir = Path.Combine(DataDir, "backups");
        }

        private string Full(string path)
        {
            if (string.IsNullOrEmpty(path)) return BaseDir;
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(BaseDir, path));
        }

        private void CreateDefaultFile()
        {
            try
            {
                string example = Path.Combine(BaseDir, "config.example.ini");
                if (File.Exists(example))
                {
                    File.Copy(example, ConfigPath, false);
                    return;
                }
            }
            catch { }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Social Server - generated configuration");
            sb.AppendLine("[site]");
            sb.AppendLine("name=Social");
            sb.AppendLine("shortName=S");
            sb.AppendLine("tagline=Your own social network");
            sb.AppendLine("defaultLanguage=ru");
            sb.AppendLine("defaultTheme=light");
            sb.AppendLine("registration=true");
            sb.AppendLine();
            sb.AppendLine("[server]");
            sb.AppendLine("port=8080");
            sb.AppendLine("portFallbacks=8081,8082,8083,8084");
            sb.AppendLine("bind=0.0.0.0");
            sb.AppendLine();
            sb.AppendLine("[security]");
            sb.AppendLine("serverSecret=");
            sb.AppendLine("cookieSalt=");
            sb.AppendLine();
            File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }

        private void ParseFile(string path)
        {
            if (!File.Exists(path)) return;
            string section = "";
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    section = end > 0 ? line.Substring(1, end - 1).Trim().ToLowerInvariant() : "";
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                Apply(section, key, val);
            }
        }

        private void Apply(string section, string key, string val)
        {
            switch (section)
            {
                case "site":
                    if (key == "name") SiteName = val;
                    else if (key == "shortname") SiteShort = val;
                    else if (key == "tagline") SiteTagline = val;
                    else if (key == "description") SiteDescription = val;
                    else if (key == "baseurl") BaseUrl = val;
                    else if (key == "defaultlanguage") DefaultLanguage = val;
                    else if (key == "defaulttheme") DefaultTheme = val;
                    else if (key == "defaultsound") DefaultSound = Bool(val, DefaultSound);
                    else if (key == "registration") Registration = Bool(val, Registration);
                    else if (key == "inviteonly") InviteOnly = Bool(val, InviteOnly);
                    else if (key == "invitecode") InviteCode = val;
                    else if (key == "firstuseradmin") FirstUserAdmin = Bool(val, FirstUserAdmin);
                    else if (key == "motd") Motd = val;
                    else if (key == "footer") Footer = val;
                    else if (key == "supportemail") SupportEmail = val;
                    break;

                case "ai":
                    if (key == "provider") AiProvider = val.Trim().ToLowerInvariant();
                    else if (key == "enabled") AiEnabled = Bool(val, AiEnabled);
                    else if (key == "endpoint") AiEndpoint = val;
                    else if (key == "apikey") AiApiKey = val;
                    else if (key == "model") AiModel = val;
                    else if (key == "timeoutms") AiTimeoutMs = Int(val, AiTimeoutMs);
                    else if (key == "temperature") AiTemperature = Double(val, AiTemperature);
                    else if (key == "maxtokens") AiMaxTokens = Int(val, AiMaxTokens);
                    else if (key == "intervalsec") AiIntervalSec = Int(val, AiIntervalSec);
                    else if (key == "maxpostsperhour") AiMaxPostsPerHour = Int(val, AiMaxPostsPerHour);
                    else if (key == "maxcommentsperhour") AiMaxCommentsPerHour = Int(val, AiMaxCommentsPerHour);
                    else if (key == "maxlikesperhour") AiMaxLikesPerHour = Int(val, AiMaxLikesPerHour);
                    else if (key == "maxmessagesperhour") AiMaxMessagesPerHour = Int(val, AiMaxMessagesPerHour);
                    else if (key == "allowposts") AiAllowPosts = Bool(val, AiAllowPosts);
                    else if (key == "allowcomments") AiAllowComments = Bool(val, AiAllowComments);
                    else if (key == "allowlikes") AiAllowLikes = Bool(val, AiAllowLikes);
                    else if (key == "allowmessages") AiAllowMessages = Bool(val, AiAllowMessages);
                    else if (key == "humantargetpercent") AiHumanTargetPercent = Int(val, AiHumanTargetPercent);
                    else if (key == "systemprompt") AiSystemPrompt = val;
                    else if (key == "statussuffix") AiStatusSuffix = val;
                    break;

                case "branding":                    if (key == "accent") BrandAccent = val;
                    else if (key == "accenthi") BrandAccentHi = val;
                    else if (key == "accentlo") BrandAccentLo = val;
                    else if (key == "darkaccent") BrandDarkAccent = val;
                    else if (key == "darkaccenthi") BrandDarkAccentHi = val;
                    else if (key == "darkaccentlo") BrandDarkAccentLo = val;
                    else if (key == "oledaccent") BrandOledAccent = val;
                    else if (key == "oledaccenthi") BrandOledAccentHi = val;
                    else if (key == "oledaccentlo") BrandOledAccentLo = val;
                    else if (key == "lightbg") BrandLightBg = val;
                    else if (key == "darkbg") BrandDarkBg = val;
                    else if (key == "darkpanel") BrandDarkPanel = val;
                    else if (key == "darkpanel2") BrandDarkPanel2 = val;
                    else if (key == "oledbg") BrandOledBg = val;
                    else if (key == "oledpanel") BrandOledPanel = val;
                    break;

                case "server":                    if (key == "port") Port = Int(val, Port);
                    else if (key == "portfallbacks") PortFallbacks = IntList(val, PortFallbacks);
                    else if (key == "bind") Bind = val;
                    else if (key == "adminport") AdminPort = Int(val, AdminPort);
                    else if (key == "adminportfallbacks") AdminPortFallbacks = IntList(val, AdminPortFallbacks);
                    else if (key == "adminbind") AdminBind = val;
                    else if (key == "adminenabled") AdminEnabled = Bool(val, AdminEnabled);
                    else if (key == "backlog") Backlog = Int(val, Backlog);
                    else if (key == "maxconcurrentrequests") MaxConcurrentRequests = Int(val, MaxConcurrentRequests);
                    else if (key == "requesttimeoutms") RequestTimeoutMs = Int(val, RequestTimeoutMs);
                    else if (key == "keepalivemax") KeepAliveMax = Int(val, KeepAliveMax);
                    else if (key == "shutdownmessage") ShutdownMessage = val;
                    else if (key == "publicbasepath") PublicBasePath = val;
                    else if (key == "trustproxyheaders") TrustProxyHeaders = Bool(val, TrustProxyHeaders);
                    else if (key == "trustedproxies") TrustedProxies = val;
                    else if (key == "cookiesecure") CookieSecure = val;
                    else if (key == "cookiename") CookieName = val;
                    else if (key == "sessionhours") SessionHours = Int(val, SessionHours);
                    else if (key == "sessionmaxhours") SessionMaxHours = Int(val, SessionMaxHours);
                    break;

                case "security":
                    if (key == "serversecret") ServerSecret = val;
                    else if (key == "cookiesalt") CookieSalt = val;
                    else if (key == "kdfiterations") KdfIterations = Int(val, KdfIterations);
                    else if (key == "encryptapi") EncryptApi = Bool(val, EncryptApi);
                    else if (key == "requirecaptcha") RequireCaptcha = Bool(val, RequireCaptcha);
                    else if (key == "maxfailedloginsperip") MaxFailedLoginsPerIp = Int(val, MaxFailedLoginsPerIp);
                    else if (key == "maxfailedloginsperuser") MaxFailedLoginsPerUser = Int(val, MaxFailedLoginsPerUser);
                    else if (key == "loginlockseconds") LoginLockSeconds = Int(val, LoginLockSeconds);
                    else if (key == "banseconds") BanSeconds = Int(val, BanSeconds);
                    else if (key == "maxbodybytes") MaxBodyBytes = Int(val, MaxBodyBytes);
                    else if (key == "maxjsondepth") MaxJsonDepth = Int(val, MaxJsonDepth);
                    else if (key == "adminprivateonly") AdminPrivateOnly = Bool(val, AdminPrivateOnly);
                    else if (key == "hsts") Hsts = Bool(val, Hsts);
                    else if (key == "externalmedia") ExternalMedia = Bool(val, ExternalMedia);
                    else if (key == "allowrolechange") AllowRoleChange = Bool(val, AllowRoleChange);
                    else if (key == "allowselfdemote") AllowSelfDemote = Bool(val, AllowSelfDemote);
                    else if (key == "protectlastadmin") ProtectLastAdmin = Bool(val, ProtectLastAdmin);
                    else if (key == "allowselfdelete") AllowSelfDelete = Bool(val, AllowSelfDelete);
                    break;

                case "limits":
                    if (key == "requestsperminute") RequestsPerMinute = Int(val, RequestsPerMinute);
                    else if (key == "registrationsperhour") RegistrationsPerHour = Int(val, RegistrationsPerHour);
                    else if (key == "postsper10min") PostsPer10Min = Int(val, PostsPer10Min);
                    else if (key == "commentsper10min") CommentsPer10Min = Int(val, CommentsPer10Min);
                    else if (key == "messagespermin") MessagesPerMin = Int(val, MessagesPerMin);
                    else if (key == "likespermin") LikesPerMin = Int(val, LikesPerMin);
                    else if (key == "searchpermin") SearchPerMin = Int(val, SearchPerMin);
                    else if (key == "reportsperhour") ReportsPerHour = Int(val, ReportsPerHour);
                    else if (key == "maxpostlength") MaxPostLength = Int(val, MaxPostLength);
                    else if (key == "maxcommentlength") MaxCommentLength = Int(val, MaxCommentLength);
                    else if (key == "maxmessagelength") MaxMessageLength = Int(val, MaxMessageLength);
                    else if (key == "maxstatuslength") MaxStatusLength = Int(val, MaxStatusLength);
                    else if (key == "maxbiolength") MaxBioLength = Int(val, MaxBioLength);
                    else if (key == "maxnamelength") MaxNameLength = Int(val, MaxNameLength);
                    else if (key == "maxusernamelength") MaxUsernameLength = Int(val, MaxUsernameLength);
                    else if (key == "maxavatarkb") MaxAvatarKB = Int(val, MaxAvatarKB);
                    else if (key == "maxpostimagekb") MaxPostImageKB = Int(val, MaxPostImageKB);
                    else if (key == "historysize") HistorySize = Int(val, HistorySize);
                    else if (key == "logringsize") LogRingSize = Int(val, LogRingSize);
                    else if (key == "notificationsperuser") NotificationsPerUser = Int(val, NotificationsPerUser);
                    else if (key == "messagesperthread") MessagesPerThread = Int(val, MessagesPerThread);
                    break;

                case "storage":
                    if (key == "datadir") DataDirRaw = val;
                    else if (key == "webroot") WebRootRaw = val;
                    else if (key == "mediadir") MediaDirRaw = val;
                    else if (key == "backupintervalmin") BackupIntervalMin = Int(val, BackupIntervalMin);
                    else if (key == "backupkeep") BackupKeep = Int(val, BackupKeep);
                    else if (key == "savedebouncms") SaveDebounceMs = Int(val, SaveDebounceMs);
                    else if (key == "savedebouncems") SaveDebounceMs = Int(val, SaveDebounceMs);
                    else if (key == "readonlyonerror") ReadOnlyOnError = Bool(val, ReadOnlyOnError);
                    break;

                case "logging":
                    if (key == "dir") LogDir = val;
                    else if (key == "level") LogLevel = ParseLevel(val, LogLevel);
                    else if (key == "apitrace") ApiTrace = Bool(val, ApiTrace);
                    else if (key == "maxfilemb") LogMaxFileMB = Int(val, LogMaxFileMB);
                    else if (key == "retentiondays") LogRetentionDays = Int(val, LogRetentionDays);
                    break;

                case "ui":
                    if (key == "refreshms") RefreshMs = Int(val, RefreshMs);
                    else if (key == "onlinewindowsec") OnlineWindowSec = Int(val, OnlineWindowSec);
                    else if (key == "allowthemes") AllowThemes = val;
                    else if (key == "allowsounds") AllowSounds = Bool(val, AllowSounds);
                    break;
            }
        }

        private void Validate()
        {
            if (Port < 1 || Port > 65535) { Warnings.Add("server.port out of range, using 8080"); Port = 8080; }
            if (PortFallbacks == null) PortFallbacks = new int[0];
            if (Bind == null || Bind.Length == 0) Bind = "0.0.0.0";
            if (AdminPort < 1 || AdminPort > 65535) AdminPort = Port + 10;
            if (AdminPortFallbacks == null) AdminPortFallbacks = new int[0];
            if (AdminBind == null || AdminBind.Length == 0) AdminBind = "0.0.0.0";
            if (AdminPort == Port) { Warnings.Add("server.adminPort equals server.port, using port+10"); AdminPort = Port + 10; }
            if (MaxConcurrentRequests < 4) MaxConcurrentRequests = 4;
            if (MaxConcurrentRequests > 512) MaxConcurrentRequests = 512;
            if (RequestTimeoutMs < 2000) RequestTimeoutMs = 2000;
            if (SessionHours < 1) SessionHours = 1;
            if (SessionMaxHours < SessionHours) SessionMaxHours = SessionHours;
            if (KdfIterations < 1000) KdfIterations = 1000;
            if (KdfIterations > 200000) KdfIterations = 200000;
            if (MaxBodyBytes < 65536) MaxBodyBytes = 65536;
            if (MaxBodyBytes > 64 * 1024 * 1024) MaxBodyBytes = 64 * 1024 * 1024;
            if (RefreshMs < 1000) RefreshMs = 1000;
            if (OnlineWindowSec < 30) OnlineWindowSec = 30;
            if (MaxAvatarKB < 16) MaxAvatarKB = 16;
            if (MaxPostImageKB < 64) MaxPostImageKB = 64;
            if (BackupKeep < 1) BackupKeep = 1;
            if (SaveDebounceMs < 100) SaveDebounceMs = 100;
            if (string.IsNullOrEmpty(CookieName)) CookieName = "sid";
            if (PublicBasePath == "/") PublicBasePath = "";
            if (PublicBasePath.Length > 0 && !PublicBasePath.StartsWith("/", StringComparison.Ordinal))
                PublicBasePath = "/" + PublicBasePath;
            if (PublicBasePath.EndsWith("/", StringComparison.Ordinal))
                PublicBasePath = PublicBasePath.Substring(0, PublicBasePath.Length - 1);

            string[] themes = (AllowThemes ?? "").Split(',');
            bool hasLight = false;
            for (int i = 0; i < themes.Length; i++)
                if (themes[i].Trim().Equals("light", StringComparison.OrdinalIgnoreCase)) hasLight = true;
            if (!hasLight) AllowThemes = "light," + (AllowThemes ?? "");

            if (DefaultTheme != "light" && DefaultTheme != "dark" && DefaultTheme != "oled")
                DefaultTheme = "light";
            if (DefaultLanguage != "ru" && DefaultLanguage != "en" && DefaultLanguage != "uk" && DefaultLanguage != "lt")
                DefaultLanguage = "en";

            if (AiProvider != "markov" && AiProvider != "openai" && AiProvider != "off")
                AiProvider = "off";
            if (AiTimeoutMs < 2000) AiTimeoutMs = 2000;
            if (AiTimeoutMs > 180000) AiTimeoutMs = 180000;
            if (AiIntervalSec < 30) AiIntervalSec = 30;
            if (AiMaxTokens < 16) AiMaxTokens = 16;
            if (AiMaxTokens > 2048) AiMaxTokens = 2048;
            if (AiTemperature < 0) AiTemperature = 0;
            if (AiTemperature > 2) AiTemperature = 2;
            if (AiHumanTargetPercent < 0) AiHumanTargetPercent = 0;
            if (AiHumanTargetPercent > 100) AiHumanTargetPercent = 100;
        }

        private void GenerateSecretsIfMissing()
        {
            bool changed = false;
            if (string.IsNullOrEmpty(ServerSecret) || ServerSecret.Length < 32)
            {
                ServerSecret = Crypto.RandomHex(32);
                SetIniValue("security", "serverSecret", ServerSecret);
                changed = true;
            }
            if (string.IsNullOrEmpty(CookieSalt) || CookieSalt.Length < 16)
            {
                CookieSalt = Crypto.RandomHex(16);
                SetIniValue("security", "cookieSalt", CookieSalt);
                changed = true;
            }
            if (changed) Warnings.Add("generated missing security secrets");
        }

        /// <summary>Replaces key=... inside [section], preserving comments and layout.</summary>
        public void SetIniValue(string section, string key, string value)
        {
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(ConfigPath)) lines.AddRange(File.ReadAllLines(ConfigPath, Encoding.UTF8));

                string current = "";
                bool done = false;
                int sectionStart = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("[", StringComparison.Ordinal))
                    {
                        int end = t.IndexOf(']');
                        string name = end > 0 ? t.Substring(1, end - 1).Trim().ToLowerInvariant() : "";
                        current = name;
                        if (name == section.ToLowerInvariant()) sectionStart = i;
                        continue;
                    }
                    if (current != section.ToLowerInvariant()) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    if (t.Substring(0, eq).Trim().ToLowerInvariant() == key.ToLowerInvariant())
                    {
                        lines[i] = key + "=" + value;
                        done = true;
                        break;
                    }
                }

                if (!done)
                {
                    if (sectionStart < 0)
                    {
                        if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                        lines.Add("[" + section + "]");
                        lines.Add(key + "=" + value);
                    }
                    else
                    {
                        // find the real end of the section (next header)
                        int insertAt = lines.Count;
                        for (int i = sectionStart + 1; i < lines.Count; i++)
                        {
                            if (lines[i].Trim().StartsWith("[", StringComparison.Ordinal)) { insertAt = i; break; }
                        }
                        lines.Insert(insertAt, key + "=" + value);
                    }
                }

                File.WriteAllLines(ConfigPath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Warnings.Add("cannot write config: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ helpers

        private static int Int(string s, int def)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static bool Bool(string s, bool def)        {
            if (s == null) return def;
            s = s.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "on") return true;
            if (s == "0" || s == "false" || s == "no" || s == "off") return false;
            return def;
        }

        private static double Double(string s, double def)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static int[] IntList(string s, int[] def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            List<int> list = new List<int>();
            string[] parts = s.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) &&
                    v > 0 && v <= 65535 && !list.Contains(v))
                    list.Add(v);
            }
            return list.Count == 0 ? def : list.ToArray();
        }

        private static LogLevel ParseLevel(string s, LogLevel def)
        {
            if (s == null) return def;
            s = s.Trim().ToLowerInvariant();
            if (s == "error") return LogLevel.Error;
            if (s == "warn" || s == "warning") return LogLevel.Warn;
            if (s == "info") return LogLevel.Info;
            if (s == "debug") return LogLevel.Debug;
            if (s == "verbose") return LogLevel.Verbose;
            return def;
        }

        public bool HasBranding
        {
            get
            {
                return BrandAccent.Length > 0 || BrandDarkAccent.Length > 0 || BrandOledAccent.Length > 0 ||
                       BrandLightBg.Length > 0 || BrandDarkBg.Length > 0 || BrandOledBg.Length > 0 ||
                       BrandDarkPanel.Length > 0 || BrandOledPanel.Length > 0;
            }
        }

        /// <summary>Accepts #rgb / #rrggbb and returns #rrggbb, or "" when invalid/empty.</summary>
        public static string NormalizeColor(string value)
        {
            if (value == null) return "";
            string v = value.Trim();
            if (v.Length == 0) return "";
            if (!v.StartsWith("#", StringComparison.Ordinal)) v = "#" + v;
            string hex = v.Substring(1);
            if (hex.Length == 3)
                hex = new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            if (hex.Length != 6) return "";
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return "";
            }
            return "#" + hex.ToLowerInvariant();
        }

        public string[] Themes()
        {
            List<string> list = new List<string>();
            string[] parts = (AllowThemes ?? "light,dark,oled").Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim().ToLowerInvariant();
                if ((t == "light" || t == "dark" || t == "oled") && !list.Contains(t)) list.Add(t);
            }
            if (list.Count == 0) list.Add("light");
            return list.ToArray();
        }

        public string CookiePath()
        {
            return string.IsNullOrEmpty(PublicBasePath) ? "/" : PublicBasePath + "/";
        }
    }
}
