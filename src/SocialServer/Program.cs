using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Social.Core;

namespace Social
{
    public static class Program
    {
        private static SocialApp _app;
        private static readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);
        private static int _shutdownStarted;

        public static int Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = null;
            bool noConsole = false;

            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    return 0;
                }
                if (a.Equals("--version", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Social Server " + Web.ApiRouter.Version + " (.NET 4.0)");
                    return 0;
                }
                if (a.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    return SelfTest.Run();
                }
                if (a.Equals("--no-console", StringComparison.OrdinalIgnoreCase)) noConsole = true;
                else if (a.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    configPath = args[++i];
                else if (a.Equals("--data", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string data = args[++i];
                    if (Directory.Exists(data) || !Path.IsPathRooted(data)) baseDir = Path.GetFullPath(data);
                }
            }

            if (configPath != null)
            {
                try { baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath)); }
                catch { }
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            try { System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobserved; } catch { }

            Config cfg = Config.Load(baseDir);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int p;
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p > 0 && p <= 65535)
                        cfg.Port = p;
                }
                else if (a.Equals("--admin-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int p;
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p > 0 && p <= 65535)
                        cfg.AdminPort = p;
                }
                else if (a.Equals("--no-admin", StringComparison.OrdinalIgnoreCase))
                    cfg.AdminEnabled = false;
                else if (a.Equals("--bind", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    cfg.Bind = args[++i];
                else if (a.Equals("--reset-admin", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    Log.Configure(cfg);
                    return ResetAdmin(cfg, args[++i]);
                }
                else if (a.Equals("--import-deadvk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string dbDir = args[++i];
                    bool withImages = true;
                    for (int j = 0; j < args.Length; j++)
                        if (args[j].Equals("--no-images", StringComparison.OrdinalIgnoreCase)) withImages = false;

                    Log.Configure(cfg);
                    Console.WriteLine("Importing DeadVK database from " + dbDir + (withImages ? "" : " (images skipped)"));
                    try
                    {
                        DeadVkImport.Report report = DeadVkImport.Run(cfg, dbDir, withImages);
                        Console.WriteLine("Done: " + report.Summary());
                        Console.WriteLine("Took " + (report.Finished - report.Started).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
                        for (int w = 0; w < report.Warnings.Count && w < 10; w++) Console.WriteLine("  ! " + report.Warnings[w]);
                        Log.Shutdown();
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Import failed: " + ex.Message);
                        Log.Shutdown();
                        return 1;
                    }
                }
                else if (a.Equals("--backup", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Configure(cfg);
                    SocialStore store = new SocialStore(cfg);
                    store.Load();
                    string dir = store.CreateBackup();
                    Console.WriteLine(dir == null ? "backup failed" : "backup: " + dir);
                    return dir == null ? 1 : 0;
                }
                else if (a.Equals("--cmd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    // one-shot operator command; works while the server is stopped
                    string command = args[++i];
                    Log.Configure(cfg);
                    try
                    {
                        SocialStore store = new SocialStore(cfg);
                        store.Load();
                        AiEngine ai = new AiEngine(cfg, store);
                        AiRunner runner = new AiRunner(cfg, store, ai);
                        Cli cli = new Cli(cfg, store, ai, runner);
                        string output = cli.Execute(command);
                        if (output.Length > 0) Console.WriteLine(output);
                        store.SaveNow();
                        Log.Shutdown();
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("command failed: " + ex.Message);
                        Log.Shutdown();
                        return 1;
                    }
                }
            }

            Log.Configure(cfg);
            Log.Info("APP", "base=" + cfg.BaseDir + " config=" + cfg.ConfigPath);
            for (int i = 0; i < cfg.Warnings.Count; i++) Log.Warn("CFG", cfg.Warnings[i]);

            _app = new SocialApp(cfg);
            if (!_app.Start())
            {
                Log.Error("APP", "cannot start, see logs in " + cfg.LogDir);
                Log.Shutdown();
                return 1;
            }

            ConsoleUi ui = new ConsoleUi(_app, RequestShutdown, !noConsole);
            ui.Start();

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                RequestShutdown();
            };

            ExitEvent.WaitOne();
            ui.Stop();
            Shutdown();
            return 0;
        }

        private static void RequestShutdown()
        {
            try { ExitEvent.Set(); } catch { }
        }

        private static void Shutdown()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
            try
            {
                if (_app != null) _app.Shutdown();
            }
            catch (Exception ex)
            {
                try { Log.Error("APP", "shutdown error: " + ex); } catch { }
            }
            Log.Shutdown();
        }

        private static int ResetAdmin(Config cfg, string username)
        {
            try
            {
                SocialStore store = new SocialStore(cfg);
                store.Load();
                User u = store.FindUser(username);
                if (u == null)
                {
                    Console.WriteLine("User not found: " + username);
                    return 1;
                }
                store.SetRole(u.Id, "admin");
                store.SetBanned(u.Id, false, "", DateTime.MinValue);
                store.SaveNow();
                Console.WriteLine("@" + u.Username + " is now an administrator.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("reset failed: " + ex.Message);
                return 1;
            }
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("CRASH", ex == null ? "unknown unhandled exception" : ex.ToString());
                WriteCrash(ex);
                if (_app != null) _app.Shutdown();
                Log.Shutdown();
            }
            catch { }
        }

        private static void OnUnobserved(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Log.Error("CRASH", "unobserved task exception: " + e.Exception);
                e.SetObserved();
            }
            catch { }
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                File.WriteAllText(file, "=== CRASH " + Util.Iso(DateTime.UtcNow) + " ===\r\n" +
                                        (ex == null ? "unknown" : ex.ToString()), new UTF8Encoding(false));
                try
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FATAL: " + (ex == null ? "unknown" : ex.Message));
                    Console.WriteLine("Crash log: " + file);
                    Console.ResetColor();
                }
                catch { }
            }
            catch { }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Social Server " + Web.ApiRouter.Version + " - self-hosted social network");
            Console.WriteLine();
            Console.WriteLine("  --port <n>            override server.port (public board)");
            Console.WriteLine("  --admin-port <n>      override server.adminPort (admin board)");
            Console.WriteLine("  --no-admin            do not start the admin board");
            Console.WriteLine("  --bind <ip>           override server.bind");
            Console.WriteLine("  --config <path>       use another config.ini (its folder is the base)");
            Console.WriteLine("  --data <path>         base directory for config/data/logs/public");
            Console.WriteLine("  --reset-admin <user>  promote a user to administrator (offline)");
            Console.WriteLine("  --backup              create a data snapshot and exit");
            Console.WriteLine("  --cmd \"<command>\"      run one console command and exit (server may be stopped)");
            Console.WriteLine("                        e.g. --cmd \"user role 9 admin\"");
            Console.WriteLine("  --import-deadvk <dir> import a DeadVK database (users, posts, messages)");
            Console.WriteLine("  --no-images           with --import-deadvk: skip avatars and post images");
            Console.WriteLine("  --selftest            run crypto/storage tests and exit");
            Console.WriteLine("  --no-console          do not attach the TUI (services, CI)");
            Console.WriteLine("  --version             print version");
            Console.WriteLine("  --help                this help");
            Console.WriteLine();
            Console.WriteLine("Interactive console commands: help, status, user ..., post del ...,");
            Console.WriteLine("recovery export, import purge-bots, ai ..., broadcast, backup, reload, quit");
        }
    }
}
