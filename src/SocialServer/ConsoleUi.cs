using System;
using System.Globalization;
using System.Threading;
using Social.Core;

namespace Social
{
    /// <summary>
    /// Small TUI: ASCII banner, a live window title and an operator prompt.
    /// Type 'help' for commands. Logs never echo here (Trace has a file listener only).
    /// </summary>
    public sealed class ConsoleUi
    {
        private readonly SocialApp _app;
        private readonly Action _shutdown;
        private readonly bool _enabled;
        private readonly Cli _cli;
        private Thread _thread;
        private Thread _title;
        private volatile bool _running;

        public ConsoleUi(SocialApp app, Action shutdown, bool enabled)
        {
            _app = app;
            _shutdown = shutdown;
            _enabled = enabled;
            _cli = new Cli(app.Config, app.Store, app.Ai, app.AiRunner);
        }

        public void Start()
        {
            if (!_enabled) return;
            Banner();
            PrintHints();
            _running = true;

            _title = new Thread(TitleLoop);
            _title.IsBackground = true;
            _title.Name = "console-title";
            _title.Start();

            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "console";
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { if (_thread != null && _thread.IsAlive) _thread.Join(500); } catch { }
            try { if (_title != null && _title.IsAlive) _title.Join(500); } catch { }
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
                Console.WriteLine("  Type 'help' for commands (the web UI is the fancy part).");
                Console.WriteLine();
            }
            catch { }
        }

        // --------------------------------------------------------------- loop

        private void TitleLoop()
        {
            while (_running)
            {
                try
                {
                    Console.Title = _app.StatusLine();
                }
                catch { }
                Thread.Sleep(2000);
            }
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    Console.Write("social> ");
                    string line = Console.ReadLine();
                    if (line == null)
                    {
                        // no interactive input (service, redirected stdin): stay quiet
                        Thread.Sleep(2000);
                        continue;
                    }

                    string output = _cli.Execute(line);
                    if (output.Length > 0) Console.WriteLine(output);
                    if (_cli.ShouldQuit)
                    {
                        _shutdown();
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(2000);
                }
                catch (Exception ex)
                {
                    try { Console.WriteLine("console: " + ex.Message); } catch { }
                    Thread.Sleep(500);
                }
            }
        }
    }
}
