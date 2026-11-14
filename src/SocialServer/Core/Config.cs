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
        // How many live accounts one address may create. 0 disables the check.
        // The point is not to punish a household sharing a router, only to stop a
        // loop that registers a hundred throwaways: three is a main account plus
        // two twins, which is as much as an honest person ever needs.
        public int AccountsPerIp = 3;
        // Guest / kiosk browsing. guestMode lets a visitor read the public side of
        // the site without an account; kioskMode is the same thing with no login
        // form at all and an idle timer that returns to the feed.
        public bool GuestMode = false;
        public string GuestPassword = "";
        public bool KioskMode = false;
        public int KioskIdleSec = 120;
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
        public int AdminPort = 8090;        public int[] AdminPortFallbacks = new int[] { 8091, 8092, 8093 };
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
        // A file travels as base64 inside the encrypted request, so it grows twice:
        // once into a data URL (4/3) and again when the sealed envelope is encoded
        // (another 4/3). A 10 MB clip therefore needs ~18.6 MB of body.
        public int MaxBodyBytes = 20 * 1024 * 1024;
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
        public int MaxMessageImageKB = 2048;
        public int MaxVoiceKB = 4096;
        public int MaxVoiceSec = 300;
        // generic file attachments in private messages
        public int MaxFileKB = 4096;

        // Third-party apps. A token grants exactly the scopes the user approved;
        // HTML apps run on our own origin, so only staff may publish them.
        public bool AppsEnabled = true;
        public int AppStorageKeys = 100;
        public int AppValueBytes = 4096;

        // Peer file transfer between friends: the server only relays chunks in        // memory, so nothing is ever written to data/media and nothing survives a
        // restart. 0 disables the whole feature.
        public int MaxTransferMB = 32;
        public int TransferChunkKB = 256;
        public int TransferTtlMin = 30;

        // anonymous feedback to the operators
        public int FeedbackPerHour = 5;

        // gifts people send each other
        public int GiftPerHour = 30;
        public int MaxGiftMessage = 300;

        // voice and video calls
        public int MaxCallMembers = 8;
        // Comma separated ICE servers; empty = LAN only, no external lookups.
        // Several STUN servers, because one being unreachable is common and a
        // second opinion costs nothing.
        public string CallStun = "stun:stun.l.google.com:19302,stun:stun1.l.google.com:19302,stun:stun.cloudflare.com:3478";
        // TURN relay. This is the only thing that works through a symmetric NAT,
        // which is what "the call never connects" usually means. Empty = no relay,
        // and the UI says so when a connection fails. A comma separated list is
        // accepted for turn: and turns: URLs.
        public string CallTurn = "";
        public string CallTurnUser = "";
        public string CallTurnPass = "";
        // "all" tries direct first and falls back to the relay; "relay" never
        // leaves the TURN server, which is what a locked-down network needs.
        public string CallIcePolicy = "all";

        // personal media library (music / clips)
        public int MaxAudioKB = 4096;
        public int MaxVideoKB = 10240;
        public int MaxAudioFiles = 200;
        public int MaxVideoFiles = 50;

        // Flash games (Ruffle): uploaded .swf files
        public int MaxGameKB = 8192;
        public int MaxGameFiles = 20;

        // stories: how long one lives, how big it may be, how many a day
        public int StoryHours = 24;
        public int MaxStoryKB = 4096;
        public int MaxStoryVideoKB = 10240;
        public int StoryPerDay = 20;
        public int MaxStoryCaption = 200;

        // short clips inside private messages
        public int MaxMessageVideoKB = 10240;
        public int MaxStatusLength = 140;
        public int MaxBioLength = 2000;
        public int MaxNameLength = 40;
        public int MaxUsernameLength = 24;
        public int MaxAvatarKB = 512;
        public int MaxPostImageKB = 4096;
        // how long an ordinary user may still edit a post, 0 = forever
        public int EditWindowMin = 0;
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
        // long-poll notifications: on/off and how long a single wait may block
        public bool LiveNotifications = true;
        public int NotifyWaitSec = 20;
        // the gift shop on profiles
        public bool GiftsEnabled = true;
        // Flash games shelf (needs public/assets/vendor/ruffle)
        public bool GamesEnabled = true;
        // stories: the bar of circles above the feed
        public bool StoriesEnabled = true;

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
                // Re-read so the secrets that were just appended are in memory, then
                // validate once. Validating before the second read would let the file
                // overwrite anything the checks corrected, which is exactly how a
                // raised maxBodyBytes used to snap back to the value in the file.
                cfg.ParseFile(cfg.ConfigPath);
                cfg.Validate();
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
                    else if (key == "accountsperip") AccountsPerIp = Int(val, AccountsPerIp);
                    else if (key == "guestmode") GuestMode = Bool(val, GuestMode);
                    else if (key == "guestpassword") GuestPassword = val;
                    else if (key == "kioskmode") KioskMode = Bool(val, KioskMode);
                    else if (key == "kioskidlesec") KioskIdleSec = Int(val, KioskIdleSec);
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
                    else if (key == "editwindowmin") EditWindowMin = Int(val, EditWindowMin);
                    else if (key == "maxcommentlength") MaxCommentLength = Int(val, MaxCommentLength);
                    else if (key == "maxmessagelength") MaxMessageLength = Int(val, MaxMessageLength);
                    else if (key == "maxmessageimagekb") MaxMessageImageKB = Int(val, MaxMessageImageKB);
                    else if (key == "maxvoicekb") MaxVoiceKB = Int(val, MaxVoiceKB);
                    else if (key == "maxvoicesec") MaxVoiceSec = Int(val, MaxVoiceSec);
                    else if (key == "maxfilekb") MaxFileKB = Int(val, MaxFileKB);
                    else if (key == "maxtransfermb") MaxTransferMB = Int(val, MaxTransferMB);
                    else if (key == "appsenabled") AppsEnabled = Bool(val, AppsEnabled);
                    else if (key == "appstoragekeys") AppStorageKeys = Int(val, AppStorageKeys);
                    else if (key == "appvaluebytes") AppValueBytes = Int(val, AppValueBytes);                    else if (key == "transferchunkkb") TransferChunkKB = Int(val, TransferChunkKB);
                    else if (key == "transferttlmin") TransferTtlMin = Int(val, TransferTtlMin);
                    else if (key == "feedbackperhour") FeedbackPerHour = Int(val, FeedbackPerHour);
                    else if (key == "giftperhour") GiftPerHour = Int(val, GiftPerHour);
                    else if (key == "maxgiftmessage") MaxGiftMessage = Int(val, MaxGiftMessage);
                    else if (key == "maxcallmembers") MaxCallMembers = Int(val, MaxCallMembers);
                    else if (key == "callstun") CallStun = val;
                    else if (key == "callturn") CallTurn = val;
                    else if (key == "callturnuser") CallTurnUser = val;
                    else if (key == "callturnpass") CallTurnPass = val;
                    else if (key == "callicepolicy") CallIcePolicy = val;
                    else if (key == "maxaudiokb") MaxAudioKB = Int(val, MaxAudioKB);
                    else if (key == "maxvideokb") MaxVideoKB = Int(val, MaxVideoKB);
                    else if (key == "maxaudiofiles") MaxAudioFiles = Int(val, MaxAudioFiles);
                    else if (key == "maxvideofiles") MaxVideoFiles = Int(val, MaxVideoFiles);
                    else if (key == "maxgamekb") MaxGameKB = Int(val, MaxGameKB);
                    else if (key == "maxgamefiles") MaxGameFiles = Int(val, MaxGameFiles);
                    else if (key == "storyhours") StoryHours = Int(val, StoryHours);
                    else if (key == "maxstorykb") MaxStoryKB = Int(val, MaxStoryKB);
                    else if (key == "maxstoryvideokb") MaxStoryVideoKB = Int(val, MaxStoryVideoKB);
                    else if (key == "storyperday") StoryPerDay = Int(val, StoryPerDay);
                    else if (key == "maxstorycaption") MaxStoryCaption = Int(val, MaxStoryCaption);
                    else if (key == "maxmessagevideokb") MaxMessageVideoKB = Int(val, MaxMessageVideoKB);
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

                case "apps":
                    if (key == "enabled") AppsEnabled = Bool(val, AppsEnabled);
                    else if (key == "storagekeys") AppStorageKeys = Int(val, AppStorageKeys);
                    else if (key == "valuebytes") AppValueBytes = Int(val, AppValueBytes);
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
                    else if (key == "livenotifications") LiveNotifications = Bool(val, LiveNotifications);
                    else if (key == "notifywaitsec") NotifyWaitSec = Int(val, NotifyWaitSec);
                    else if (key == "giftsenabled") GiftsEnabled = Bool(val, GiftsEnabled);
                    else if (key == "gamesenabled") GamesEnabled = Bool(val, GamesEnabled);
                    else if (key == "storiesenabled") StoriesEnabled = Bool(val, StoriesEnabled);
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
            if (NotifyWaitSec < 1) NotifyWaitSec = 1;
            if (NotifyWaitSec > 25) NotifyWaitSec = 25;
            if (MaxAvatarKB < 16) MaxAvatarKB = 16;
            if (CallIcePolicy != "relay") CallIcePolicy = "all";
            if (CallTurn == null) CallTurn = "";
            if (AccountsPerIp < 0) AccountsPerIp = 0;
            if (AccountsPerIp > 100) AccountsPerIp = 100;
            if (MaxPostImageKB < 64) MaxPostImageKB = 64;
            if (GiftPerHour < 0) GiftPerHour = 0;
            if (MaxGiftMessage < 0) MaxGiftMessage = 0;
            if (MaxGiftMessage > 1000) MaxGiftMessage = 1000;
            if (MaxGameKB < 64) MaxGameKB = 64;
            if (MaxGameFiles < 0) MaxGameFiles = 0;
            if (StoryHours < 1) StoryHours = 1;
            if (StoryHours > 168) StoryHours = 168;
            if (MaxStoryKB < 64) MaxStoryKB = 64;
            if (MaxStoryVideoKB < 64) MaxStoryVideoKB = 64;
            if (StoryPerDay < 0) StoryPerDay = 0;
            if (MaxStoryCaption < 0) MaxStoryCaption = 0;
            if (MaxStoryCaption > 500) MaxStoryCaption = 500;
            if (MaxMessageVideoKB < 64) MaxMessageVideoKB = 64;

            // A file travels as base64 inside the sealed request and grows twice:
            // once into a data URL (4/3) and again when the envelope is encoded
            // (another 4/3). Raising a size cap without raising this one is the
            // trap that makes uploads die mid-request, so make sure the body is
            // always big enough for the largest cap the config allows.
            int biggestKb = MaxVideoKB;
            if (MaxStoryVideoKB > biggestKb) biggestKb = MaxStoryVideoKB;
            if (MaxMessageVideoKB > biggestKb) biggestKb = MaxMessageVideoKB;
            if (MaxAudioKB > biggestKb) biggestKb = MaxAudioKB;
            if (MaxFileKB > biggestKb) biggestKb = MaxFileKB;
            if (MaxGameKB > biggestKb) biggestKb = MaxGameKB;
            if (MaxVoiceKB > biggestKb) biggestKb = MaxVoiceKB;
            if (MaxPostImageKB > biggestKb) biggestKb = MaxPostImageKB;
            if (MaxMessageImageKB > biggestKb) biggestKb = MaxMessageImageKB;
            long needed = (long)biggestKb * 1024L * 16L / 9L + 1024L * 1024L;
            if (needed > 64L * 1024L * 1024L) needed = 64L * 1024L * 1024L;
            if (MaxBodyBytes < needed)
            {
                Warnings.Add("maxBodyBytes raised to " + ((needed + 1024L * 1024L - 1) / (1024L * 1024L)) +
                             " MB to fit the largest upload cap");
                MaxBodyBytes = (int)needed;
            }
            if (MaxTransferMB < 1) MaxTransferMB = 1;
            if (AppStorageKeys < 1) AppStorageKeys = 1;
            if (AppStorageKeys > 10000) AppStorageKeys = 10000;
            if (AppValueBytes < 64) AppValueBytes = 64;
            if (AppValueBytes > 65536) AppValueBytes = 65536;            if (MaxTransferMB > 512) MaxTransferMB = 512;
            if (TransferChunkKB < 32) TransferChunkKB = 32;
            if (TransferChunkKB > 2048) TransferChunkKB = 2048;
            if (TransferTtlMin < 1) TransferTtlMin = 1;
            if (TransferTtlMin > 240) TransferTtlMin = 240;
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

        /// <summary>Kiosk is guest mode with the training wheels off.</summary>
        public bool GuestEnabled { get { return GuestMode || KioskMode; } }

        /// <summary>
        /// True when this board serves guests. The admin board never does: it is
        /// where a kiosk gets unlocked, so it must keep its login form.
        /// </summary>
        public bool GuestOnBoard(bool isAdminBoard)
        {
            return GuestEnabled && !isAdminBoard;
        }

        /// <summary>A kiosk never asks for a password, or it would lock itself out.</summary>
        public bool GuestPasswordRequired
        {
            get { return GuestEnabled && !KioskMode && !string.IsNullOrEmpty(GuestPassword); }
        }
    }
}
