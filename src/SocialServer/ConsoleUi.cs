using System;
using System.Globalization;
using System.Threading;
using Social.Core;

namespace Social
{
    /// <summary>
    /// Small TUI: ASCII banner, live status line, a few hotkeys.
    /// Logs never echo here (Trace has a file listener only); this is the cockpit.
    /// </summary>
    public sealed class ConsoleUi
    {
        private readonly SocialApp _app;
        private readonly Action _shutdown;
        private readonly bool _enabled;
        private Thread _thread;
        private volatile bool _running;
        private bool _noKeys;

        public ConsoleUi(SocialApp app, Action shutdown, bool enabled)
        {
            _app = app;
            _shutdown = shutdown;
            _enabled = enabled;
        }

        public void Start()
        {
            if (!_enabled) return;
            try
            {
                Console.Title = _app.Config.SiteName + " - public " + _app.WebPublic.Port +
                                (_app.WebAdmin != null ? " / admin " + _app.WebAdmin.Port : " / admin off");
            }
            catch { }

            Banner();
            PrintHints();

            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "console";
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { if (_thread != null && _thread.IsAlive) _thread.Join(1000); } catch { }
        }

        // ------------------------------------------------------------- banner

        private void Banner()
        {
            Config cfg = _app.Config;
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine();
                Console.WriteLine("   ____             _       _   ");
                Console.WriteLine("  / ___|  ___   ___(_) __ _| |  ");
                Console.WriteLine("  \\___ \\ / _ \\ / __| |/ _` | |  ");
                Console.WriteLine("   ___) | (_) | (__| | (_| | |  ");
                Console.WriteLine("  |____/ \\___/ \\___|_|\\__,_|_|  ");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("  " + cfg.SiteName + " - self-hosted social network");
                Console.WriteLine("  public " + _app.WebPublic.Port +
                                  (_app.WebAdmin != null ? " | admin " + _app.WebAdmin.Port : " | admin off") +
                                  " | log " + cfg.LogLevel);
                Console.WriteLine("  .NET 4.0 / C# 4 - Windows XP SP3 ready - no external libraries");
                Console.WriteLine();
            }
            catch { }
        }

        private void PrintHints()
        {
            try
            {
                Console.WriteLine("  public board : http://localhost:" + _app.WebPublic.Port + _app.Config.PublicBasePath + "/");
                if (_app.WebAdmin != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  admin board  : http://localhost:" + _app.WebAdmin.Port + "/");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("  admin board  : disabled (server.adminEnabled=false)");
                }
                Console.WriteLine("  data         : " + _app.Config.DataDir);
                Console.WriteLine("  logs         : " + _app.Config.LogDir);
                Console.WriteLine("  first account registered becomes administrator");
                Console.WriteLine();
                Console.WriteLine("  [S] save  [B] backup  [R] reload config  [L] last logs  [H] help  [Q] quit");
                Console.WriteLine();
            }
            catch { }
        }

        // --------------------------------------------------------------- loop

        private static int SafeWidth()
        {
            try
            {
                int w = Console.WindowWidth;
                return w < 40 ? 80 : w;
            }
            catch { return 100; }
        }

        private void Loop()
        {
            int width = SafeWidth();
            while (_running)
            {
                try
                {
                    DrawStatus(width);
                    Thread.Sleep(1000);

                    if (_noKeys) continue;
                    bool keyAvailable;
                    try { keyAvailable = Console.KeyAvailable; }
                    catch (InvalidOperationException) { _noKeys = true; continue; }

                    if (!keyAvailable) continue;

                    ConsoleKeyInfo key = Console.ReadKey(true);
                    ClearLine(width);
                    switch (key.Key)
                    {
                        case ConsoleKey.Q:
                        case ConsoleKey.Escape:
                            _shutdown();
                            return;
                        case ConsoleKey.S:
                            _app.Store.SaveNow();
                            _app.Stats.Save();
                            WriteLine("saved", ConsoleColor.Green);
                            break;
                        case ConsoleKey.B:
                            string dir = _app.Store.CreateBackup();
                            WriteLine(dir == null ? "backup failed" : "backup: " + dir, ConsoleColor.Green);
                            break;
                        case ConsoleKey.R:
                            _app.Config.Reload();
                            Log.Configure(_app.Config);
                            WriteLine("config reloaded (port/bind/root need a restart)", ConsoleColor.Yellow);
                            break;
                        case ConsoleKey.L:
                            WriteLine(Log.TailText(20), ConsoleColor.Gray);
                            break;
                        case ConsoleKey.H:
                            WriteLine("[S] save [B] backup [R] reload [L] logs [Q] quit", ConsoleColor.Gray);
                            break;
                    }
                }
                catch (InvalidOperationException)
                {
                    _noKeys = true;
                    Thread.Sleep(2000);
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        }

        private void DrawStatus(int width)
        {
            if (_noKeys) return;
            string line = " online " + _app.Store.CountOnline(_app.Config.OnlineWindowSec) +
                          "/" + _app.Store.CountUsers() +
                          " | posts " + _app.Store.CountAllPosts() +
                          " | groups " + _app.Store.CountGroups() +
                          " | sessions " + _app.Sessions.Count +
                          " | public " + _app.WebPublic.Port +
                          (_app.WebAdmin != null ? " | admin " + _app.WebAdmin.Port : "") +
                          " | up " + Util.HumanDuration(DateTime.UtcNow - _app.StartedUtc) +
                          " | mem " + (_app.WorkingSetBytes / 1048576).ToString(CultureInfo.InvariantCulture) + "MB" +
                          (_app.Store.IsReadOnly ? " | READ-ONLY" : "") + " ";
            if (line.Length > width - 1) line = line.Substring(0, width - 1);

            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("\r" + line.PadRight(Math.Max(0, width - 1)));
                Console.ResetColor();
            }
            catch { }
        }

        private static void ClearLine(int width)
        {
            try { Console.Write("\r" + new string(' ', Math.Max(0, width - 1)) + "\r"); }
            catch { }
        }

        private static void WriteLine(string text, ConsoleColor color)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ResetColor();
            }
            catch { }
        }
    }
}
