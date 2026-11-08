using System;
using System.Diagnostics;
using System.Threading;
using Social.Core;
using Social.Web;

namespace Social
{
    /// <summary>Owns every long lived component and knows how to shut them down cleanly.</summary>
    public sealed class SocialApp
    {
        public readonly Config Config;
        public readonly SocialStore Store;
        public readonly SessionManager Sessions;
        public readonly StatsStore Stats;
        public readonly CaptchaStore Captcha;
        public readonly MediaStore Media;
        public readonly AiEngine Ai;
        public readonly AiRunner AiRunner;

        /// <summary>Public board (feed, profiles, media) — expose this one.</summary>
        public readonly WebServer WebPublic;

        /// <summary>Admin board on its own port — /api/admin/ only exists here.</summary>
        public readonly WebServer WebAdmin;

        public readonly DateTime StartedUtc = DateTime.UtcNow;
        private volatile bool _shuttingDown;
        private AppSupervisor _supervisor;

        public SocialApp(Config cfg)
        {
            Config = cfg;
            Store = new SocialStore(cfg);
            Sessions = new SessionManager(cfg);
            Stats = new StatsStore(cfg);
            Captcha = new CaptchaStore(5000, 300);
            Media = new MediaStore(cfg);
            Ai = new AiEngine(cfg, Store);
            AiRunner = new AiRunner(cfg, Store, Ai);
            WebPublic = new WebServer(cfg, Store, Sessions, Stats, Captcha, Media, Ai, AiRunner, WebRole.Public);
            WebAdmin = cfg.AdminEnabled
                ? new WebServer(cfg, Store, Sessions, Stats, Captcha, Media, Ai, AiRunner, WebRole.Admin)
                : null;
        }

        public bool ShuttingDown { get { return _shuttingDown; } }
        public long WorkingSetBytes
        {
            get
            {
                try { return Process.GetCurrentProcess().WorkingSet64; }
                catch { return 0; }
            }
        }

        public bool Start()
        {
            try
            {
                Store.Load();
                Stats.Load();
                Stats.OnStart();
                Stats.NotePeak(Store.CountOnline(Config.OnlineWindowSec));

                if (!WebPublic.Start())
                {
                    Log.Error("APP", "public board did not start");
                    return false;
                }

                if (WebAdmin != null && !WebAdmin.Start())
                {
                    // the public site keeps working; admin can be fixed and restarted
                    Log.Error("APP", "admin board did not start (public board is still up)");
                }

                _supervisor = new AppSupervisor(this);
                _supervisor.Start();
                AiRunner.Start();
                Log.Info("APP", Config.SiteName + " " + ApiRouter.Version + " started: public " + WebPublic.Port +
                                  (WebAdmin != null ? ", admin " + WebAdmin.Port : ", admin disabled") +
                                  " (users " + Store.CountUsers() + ", online " + Store.CountOnline(Config.OnlineWindowSec) + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("APP", "startup failed: " + ex);
                return false;
            }
        }

        public void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            Log.Info("APP", "shutdown requested");
            try { if (_supervisor != null) _supervisor.Stop(); } catch { }
            try { AiRunner.Stop(); } catch { }
            try { WebPublic.Stop(); } catch { }
            try { if (WebAdmin != null) WebAdmin.Stop(); } catch { }
            try { Store.SaveNow(); } catch (Exception ex) { Log.Error("APP", "final save failed: " + ex.Message); }
            try { Stats.Save(); } catch { }
            Log.Info("APP", "bye");
        }

        public string StatusLine()
        {
            return Config.SiteName + " | users " + Store.CountUsers() +
                   " | online " + Store.CountOnline(Config.OnlineWindowSec) +
                   " | posts " + Store.CountAllPosts() +
                   " | " + WebPublic.StatusLine() +
                   (WebAdmin != null ? " || " + WebAdmin.StatusLine() : "") +
                   " | mem " + (WorkingSetBytes / 1048576) + "MB" +
                   (Store.IsReadOnly ? " | READ-ONLY" : "");
        }
    }
}
