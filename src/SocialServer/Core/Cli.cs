using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Operator console. The same interpreter is used by the live console prompt and by
    /// one-shot commands (<c>SocialServer.exe --cmd "user del 12"</c>), so anything you can
    /// type interactively can also be scripted.
    ///
    /// It never touches HTTP: it works directly on the store, which means the one-shot
    /// mode also works while the server is stopped.
    /// </summary>
    public sealed class Cli
    {
        private readonly Config _cfg;
        private readonly SocialStore _store;
        private readonly AiEngine _ai;
        private readonly AiRunner _runner;
        private readonly StringBuilder _out = new StringBuilder(4096);

        public Cli(Config cfg, SocialStore store, AiEngine ai, AiRunner runner)
        {
            _cfg = cfg;
            _store = store;
            _ai = ai;
            _runner = runner;
        }

        public bool ShouldQuit { get; private set; }

        /// <summary>Executes one command line and returns the text to print.</summary>
        public string Execute(string line)
        {
            _out.Length = 0;
            ShouldQuit = false;
            if (line == null) return "";
            string trimmed = line.Trim();
            if (trimmed.Length == 0) return "";
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) return "";

            string[] parts = Split(trimmed);
            string cmd = parts[0].ToLowerInvariant();
            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);

            try
            {
                switch (cmd)
                {
                    case "help":
                    case "?":
                        Help(args.Length > 0 ? args[0] : "");
                        break;
                    case "status":
                        Status();
                        break;
                    case "user":
                    case "users":
                        UserCommand(args);
                        break;
                    case "post":
                    case "posts":
                        PostCommand(args);
                        break;
                    case "recovery":
                        RecoveryCommand(args);
                        break;
                    case "import":
                        ImportCommand(args);
                        break;
                    case "ai":
                        AiCommand(args);
                        break;
                    case "broadcast":
                        Broadcast(Join(args));
                        break;
                    case "backup":
                        Backup();
                        break;
                    case "reload":
                        Reload();
                        break;
                    case "save":
                        _store.SaveNow();
                        Say("saved");
                        break;
                    case "quit":
                    case "exit":
                    case "q":
                        ShouldQuit = true;
                        break;
                    default:
                        Say("unknown command: " + cmd + " (try 'help')");
                        break;
                }
            }
            catch (StoreReadOnlyException ex)
            {
                Say("storage is read-only: " + ex.Message);
            }
            catch (Exception ex)
            {
                Say("error: " + ex.Message);
            }

            return _out.ToString();
        }

        // ============================================================ commands

        private void Help(string topic)
        {
            if (topic == "user" || topic == "users")
            {
                Say("user list [all|legacy|bots|humans]   list accounts");
                Say("user info <id|@name>                show one account");
                Say("user role <id|@name> <role>         role: user | moderator | admin");
                Say("user ban <id|@name> [days] [reason] ban (0 days = forever)");
                Say("user unban <id|@name>               lift a ban");
                Say("user del <id|@name>                 delete the account and its content");
                Say("user persona <id|@name> <name|->   make it an AI account (or ' -' to stop)");
                Say("user recovery <id|@name>            issue a new recovery code");
                return;
            }
            if (topic == "ai")
            {
                Say("ai status                 provider, counters, next run");
                Say("ai on | ai off            enable/disable the simulation");
                Say("ai provider <name>        off | markov | openai");
                Say("ai step                   force one action now (server must run)");
                return;
            }
            if (topic == "import")
            {
                Say("import purge-bots [--dry] delete imported AI accounts and their content");
                return;
            }

            Say("commands:");
            Say("  status                    counts and storage state");
            Say("  user ...                  list / info / role / ban / unban / del / recovery");
            Say("  post del <id>             delete a post");
            Say("  recovery export [file]    new recovery codes for imported accounts -> bin\\temp");
            Say("  import purge-bots [--dry] remove imported AI accounts");
            Say("  ai ...                    status / on / off / provider / step");
            Say("  broadcast <text>          notification to every account");
            Say("  backup                    snapshot data/");
            Say("  reload                    re-read config.ini");
            Say("  save                      flush pending writes");
            Say("  quit                      stop the server (interactive only)");
            Say("");
            Say("help user | help ai | help import   for details");
        }

        private void Status()
        {
            Say(_cfg.SiteName + " " + Web.ApiRouter.Version);
            Say("  users      : " + _store.CountUsers() + " (imported " + _store.CountImported() + ")");
            Say("  online     : " + _store.CountOnline(_cfg.OnlineWindowSec));
            Say("  posts      : " + _store.CountAllPosts() + "   comments: " + _store.CountAllComments() +
                "   likes: " + _store.CountAllLikes());
            Say("  messages   : " + _store.CountAllMessages() + "   groups: " + _store.CountGroups());
            Say("  storage    : " + Util.HumanSize(_store.TotalStorageBytes()) +
                (_store.IsReadOnly ? "   READ-ONLY: " + _store.SaveError : ""));
            Say("  last save  : " + (_store.LastSaveUtc == DateTime.MinValue ? "never" : _store.LastSaveUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            Say("  public     : port " + _cfg.Port + "   admin: " + (_cfg.AdminEnabled ? _cfg.AdminPort.ToString(CultureInfo.InvariantCulture) : "off"));
            Say("  ai         : " + (_cfg.AiEnabled ? _ai.ProviderLabel : "disabled") +
                (_cfg.AiEnabled ? "   actions " + _runner.Actions : ""));
        }

        private void UserCommand(string[] args)
        {
            if (args.Length == 0) { Help("user"); return; }
            string sub = args[0].ToLowerInvariant();

            if (sub == "list")
            {
                string filter = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
                int limit = 100;
                if (args.Length > 2) int.TryParse(args[2], out limit);

                List<User> users = _store.AllUsers();
                users.Sort(delegate(User a, User b) { return a.Id.CompareTo(b.Id); });

                int shown = 0;
                for (int i = 0; i < users.Count; i++)
                {
                    User u = users[i];
                    bool legacy = !string.IsNullOrEmpty(u.LegacySource);
                    bool bot = legacy && !string.IsNullOrEmpty(u.Persona);
                    bool human = legacy && !bot;

                    if (filter == "legacy" && !legacy) continue;
                    if (filter == "bots" && !bot) continue;
                    if (filter == "humans" && !human) continue;
                    if (filter == "admins" && u.Role != "admin") continue;
                    if (filter == "deleted")
                    {
                        if (!u.Deleted) continue;
                    }
                    else if (u.Deleted) continue;

                    if (shown >= limit) { Say("... (use a higher limit: user list " + filter + " 500)"); break; }
                    shown++;
                    Say(Pad(u.Id.ToString(CultureInfo.InvariantCulture), 5) + Pad("@" + u.Username, 22) +
                        Pad(Short(u.DisplayName, 18), 19) + Pad(u.Role, 10) +
                        (bot ? "bot " : human ? "legacy " : "") +
                        (u.Banned ? "BANNED " : "") +
                        (u.Persona.Length > 0 ? "[" + u.Persona + "] " : "") +
                        Short(u.Status, 40));
                }
                Say(shown + " shown, " + users.Count + " total");
                return;
            }

            if (sub == "info")
            {
                User u = Resolve(args.Length > 1 ? args[1] : "");
                if (u == null) return;
                Say("#" + u.Id + "  @" + u.Username + "  " + u.DisplayName);
                Say("  role     : " + u.Role + (u.Banned ? "  BANNED: " + u.BanReason : ""));
                Say("  created  : " + u.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                Say("  last seen: " + u.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                Say("  legacy   : " + (string.IsNullOrEmpty(u.LegacySource) ? "no" : u.LegacySource + " / " + u.LegacyId));
                if (u.Persona.Length > 0) Say("  persona  : " + u.Persona + (u.Mood.Length > 0 ? " (" + u.Mood + ")" : ""));
                Say("  posts    : " + _store.CountPosts(u.Id) + "   friends: " + _store.CountFriends(u.Id));
                if (u.Status.Length > 0) Say("  status   : " + u.Status);
                return;
            }

            if (sub == "role")
            {
                if (args.Length < 3) { Say("usage: user role <id|@name> <user|moderator|admin>"); return; }
                User u = Resolve(args[1]);
                if (u == null) return;
                string role = args[2].ToLowerInvariant();
                if (role != "user" && role != "moderator" && role != "admin")
                {
                    Say("role must be user, moderator or admin");
                    return;
                }
                if (u.Role == "admin" && role != "admin" && _store.CountAdmins() <= 1)
                {
                    Say("refused: that is the last administrator");
                    return;
                }
                _store.SetRole(u.Id, role);
                _store.SaveNow();
                Say("@" + u.Username + " is now " + role);
                Log.Info("CLI", "role of @" + u.Username + " set to " + role);
                return;
            }

            if (sub == "ban" || sub == "unban")
            {
                if (args.Length < 2) { Say("usage: user " + sub + " <id|@name> [days] [reason]"); return; }
                User u = Resolve(args[1]);
                if (u == null) return;

                if (sub == "unban")
                {
                    _store.SetBanned(u.Id, false, "", DateTime.MinValue);
                    _store.SaveNow();
                    Say("@" + u.Username + " unbanned");
                    return;
                }

                int days = 0;
                if (args.Length > 2) int.TryParse(args[2], out days);
                string reason = args.Length > 3 ? Join(args, 3) : "";
                DateTime until = days <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddDays(days);
                _store.SetBanned(u.Id, true, reason, until);
                _store.SaveNow();
                Say("@" + u.Username + " banned" + (days <= 0 ? " forever" : " for " + days + " day(s)") +
                    (reason.Length > 0 ? ": " + reason : ""));
                Log.Warn("CLI", "banned @" + u.Username + " via console");
                return;
            }

            if (sub == "del" || sub == "delete")
            {
                if (args.Length < 2) { Say("usage: user del <id|@name>"); return; }
                User u = Resolve(args[1]);
                if (u == null) return;
                if (u.Role == "admin" && _store.CountAdmins() <= 1)
                {
                    Say("refused: that is the last administrator");
                    return;
                }
                string name = "@" + u.Username;
                int removed = _store.HardDeleteUser(u.Id);
                _store.SaveNow();
                Say("deleted " + name + " and " + (removed - 1) + " related records");
                Log.Warn("CLI", "hard-deleted " + name + " via console");
                return;
            }

            if (sub == "persona")
            {
                if (args.Length < 3) { Say("usage: user persona <id|@name> <friendly|troll|philosopher|gamer|musician|coder|doomer|hype|->"); return; }
                User u = Resolve(args[1]);
                if (u == null) return;
                string persona = args[2].ToLowerInvariant();
                if (persona == "-" || persona == "none") persona = "";
                if (persona.Length > 0)
                {
                    bool known = false;
                    for (int i = 0; i < AiEngine.Personas.Length; i++) if (AiEngine.Personas[i] == persona) known = true;
                    if (!known) { Say("unknown persona (try: " + string.Join(", ", AiEngine.Personas) + ")"); return; }
                }
                u.Persona = persona;
                _store.MarkUserDirty(u);
                _store.SaveNow();
                Say("@" + u.Username + (persona.Length == 0 ? " is no longer an AI account" : " is now a " + persona + " AI account"));
                return;
            }

            if (sub == "recovery")
            {
                if (args.Length < 2) { Say("usage: user recovery <id|@name>"); return; }
                User u = Resolve(args[1]);
                if (u == null) return;
                string code = Crypto.NewRecoveryCode();
                _store.SetRecovery(u, code);
                _store.SaveNow();
                Say("@" + u.Username + " recovery code: " + code);
                Log.Info("CLI", "issued a recovery code for @" + u.Username);
                return;
            }

            Say("unknown: user " + sub + " (try 'help user')");
        }

        private void PostCommand(string[] args)
        {
            if (args.Length >= 2 && (args[0] == "del" || args[0] == "delete"))
            {
                long id;
                if (!long.TryParse(args[1], out id)) { Say("usage: post del <id>"); return; }
                Post p = _store.FindPost(id);
                if (p == null) { Say("post not found: " + id); return; }
                _store.DeletePost(id, true);
                _store.SaveNow();
                Say("post #" + id + " deleted");
                Log.Warn("CLI", "post #" + id + " deleted via console");
                return;
            }
            Say("usage: post del <id>");
        }

        private void RecoveryCommand(string[] args)
        {
            if (args.Length == 0 || args[0] != "export")
            {
                Say("usage: recovery export [file]");
                return;
            }

            string path = args.Length > 1 ? args[1] : DefaultRecoveryPath();
            List<User> users = _store.AllUsers();
            users.Sort(delegate(User a, User b) { return a.Id.CompareTo(b.Id); });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Recovery codes generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# id\tusername\tdisplay_name\trole\tcode");
            int count = 0;

            for (int i = 0; i < users.Count; i++)
            {
                User u = users[i];
                if (string.IsNullOrEmpty(u.LegacySource) || u.Deleted) continue;
                if (!string.IsNullOrEmpty(u.Persona)) continue; // bots are removed anyway
                string code = Crypto.NewRecoveryCode();
                _store.SetRecovery(u, code);
                sb.Append(u.Id).Append('\t').Append(u.Username).Append('\t').Append(u.DisplayName)
                  .Append('\t').Append(u.Role).Append('\t').Append(code).AppendLine();
                count++;
            }

            _store.SaveNow();

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Say("wrote " + count + " recovery codes to " + path);
                Log.Info("CLI", "exported " + count + " recovery codes to " + path);
            }
            catch (Exception ex)
            {
                Say("cannot write " + path + ": " + ex.Message);
                Say(sb.ToString());
            }
        }

        private string DefaultRecoveryPath()
        {
            return Path.Combine(_cfg.BaseDir, "temp",
                "recovery-codes-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        }

        private void ImportCommand(string[] args)
        {
            if (args.Length == 0 || args[0] != "purge-bots")
            {
                Say("usage: import purge-bots [--dry]");
                return;
            }
            bool dry = Array.IndexOf(args, "--dry") >= 0;

            List<User> users = _store.AllUsers();
            List<User> victims = new List<User>();
            for (int i = 0; i < users.Count; i++)
            {
                User u = users[i];
                if (string.IsNullOrEmpty(u.LegacySource)) continue;
                if (string.IsNullOrEmpty(u.Persona)) continue;
                victims.Add(u);
            }

            if (dry)
            {
                Say("would delete " + victims.Count + " imported AI accounts and their content");
                for (int i = 0; i < victims.Count && i < 10; i++) Say("  @" + victims[i].Username);
                if (victims.Count > 10) Say("  ... and " + (victims.Count - 10) + " more");
                return;
            }

            int records = 0;
            for (int i = 0; i < victims.Count; i++)
            {
                records += _store.HardDeleteUser(victims[i].Id);
                if ((i + 1) % 50 == 0) Say("  deleted " + (i + 1) + "/" + victims.Count + "...");
            }
            _store.SaveNow();
            Say("deleted " + victims.Count + " accounts, " + records + " records total");
            Log.Warn("CLI", "purged " + victims.Count + " imported AI accounts via console");
        }

        private void AiCommand(string[] args)
        {
            if (args.Length == 0 || args[0] == "status")
            {
                Say("  enabled  : " + _cfg.AiEnabled);
                Say("  provider : " + _cfg.AiProvider + "  (" + _ai.ProviderLabel + ")");
                if (_cfg.AiProvider == "openai") Say("  endpoint : " + _cfg.AiEndpoint + "  model: " + _cfg.AiModel);
                Say("  ready    : " + _ai.IsReady);
                Say("  actions  : " + _runner.Actions + "   skipped: " + _runner.Skipped +
                    "   failures: " + _runner.ConsecutiveFailures);
                Say("  last     : " + (_runner.LastAction.Length > 0 ? _runner.LastAction : "(nothing yet)"));
                if (_ai.LastError.Length > 0) Say("  error    : " + _ai.LastError);
                return;
            }

            if (args[0] == "on" || args[0] == "off")
            {
                _cfg.AiEnabled = args[0] == "on";
                _cfg.SetIniValue("ai", "enabled", _cfg.AiEnabled ? "true" : "false");
                Say("AI simulation " + (_cfg.AiEnabled ? "enabled" : "disabled") + " (saved to config.ini)");
                Log.Info("CLI", "AI simulation " + (_cfg.AiEnabled ? "enabled" : "disabled") + " via console");
                return;
            }

            if (args[0] == "provider")
            {
                if (args.Length < 2) { Say("usage: ai provider <off|markov|openai>"); return; }
                string p = args[1].ToLowerInvariant();
                if (p != "off" && p != "markov" && p != "openai")
                {
                    Say("provider must be off, markov or openai");
                    return;
                }
                _cfg.AiProvider = p;
                _cfg.SetIniValue("ai", "provider", p);
                Say("AI provider is now " + p + " (saved to config.ini)");
                return;
            }

            if (args[0] == "step")
            {
                Say(_runner.StepOnce());
                return;
            }

            Say("usage: ai status | ai on | ai off | ai provider <name> | ai step");
        }

        private void Broadcast(string text)
        {
            if (text.Length == 0) { Say("usage: broadcast <text>"); return; }
            int n = 0;
            List<User> users = _store.AllUsers();
            for (int i = 0; i < users.Count; i++)
            {
                User u = users[i];
                if (u.Deleted || u.Banned) continue;
                _store.Notify(u.Id, 0, "admin", Util.CleanText(text, 500), "#/notifications");
                n++;
            }
            _store.SaveNow();
            Say("delivered to " + n + " accounts");
            Log.Info("CLI", "broadcast to " + n + " accounts via console");
        }

        private void Backup()
        {
            string dir = _store.CreateBackup();
            Say(dir == null ? "backup failed (see the log)" : "backup: " + dir);
        }

        private void Reload()
        {
            _cfg.Reload();
            Say("config reloaded (port/bind/root need a restart)");
        }

        // ============================================================= helpers

        private User Resolve(string token)
        {
            if (string.IsNullOrEmpty(token)) { Say("missing user id or @name"); return null; }
            string t = token.Trim();
            if (t.StartsWith("@", StringComparison.Ordinal)) t = t.Substring(1);

            long id;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                User byId = _store.FindUser(id);
                if (byId != null) return byId;
            }
            User byName = _store.FindUser(t);
            if (byName != null) return byName;
            Say("user not found: " + token);
            return null;
        }

        private void Say(string text)
        {
            if (_out.Length > 0) _out.AppendLine();
            _out.Append(text ?? "");
        }

        private static string Pad(string value, int width)
        {
            string s = value ?? "";
            if (s.Length >= width) return width <= 1 ? "" : s.Substring(0, width - 1) + " ";
            return s + new string(' ', width - s.Length);
        }

        private static string Short(string value, int max)
        {
            string s = Util.SingleLine(value, max);
            return s;
        }

        private static string Join(string[] parts, int from)
        {
            if (parts == null || from >= parts.Length) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < parts.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        private static string Join(string[] parts) { return Join(parts, 0); }

        /// <summary>Splits on whitespace but keeps "quoted phrases" together.</summary>
        public static string[] Split(string line)
        {
            List<string> list = new List<string>();
            StringBuilder sb = new StringBuilder();
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    else sb.Append(c);
                    continue;
                }
                if (c == '"' || c == '\'') { quote = c; continue; }
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) { list.Add(sb.ToString()); sb.Length = 0; }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list.ToArray();
        }
    }
}
