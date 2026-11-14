using System;
using System.Threading;
using Social.Core;
using Social.Web;

namespace Social
{
    /// <summary>
    /// Watchdog and maintenance loop: restarts a dead listener with exponential backoff,
    /// flushes debounced writes, expires sessions/captchas, makes snapshots and keeps
    /// the "dynamic fault tolerance" promises in one place.
    /// </summary>
    public sealed class AppSupervisor
    {
        private sealed class Watched
        {
            public string Name;
            public Func<bool> IsRunning;
            public Func<bool> Restart;
            public DateTime BackoffUntil = DateTime.MinValue;
            public int Restarts;
        }

        private readonly SocialApp _app;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly Watched _public;
        private readonly Watched _admin;
        private Thread _thread;
        private DateTime _lastBackup = DateTime.UtcNow;
        private DateTime _lastHealthLog = DateTime.UtcNow;
        private DateTime _lastStatsSave = DateTime.UtcNow;
        private DateTime _lastSaveCheck = DateTime.UtcNow;
        private bool _readOnlyLogged;

        public AppSupervisor(SocialApp app)
        {
            _app = app;
            _public = new Watched
            {
                Name = "web-public",
                IsRunning = delegate { return app.WebPublic != null && app.WebPublic.IsRunning; },
                Restart = delegate { return app.WebPublic != null && app.WebPublic.Restart(); }
            };
            _admin = new Watched
            {
                Name = "web-admin",
                IsRunning = delegate { return app.WebAdmin != null && app.WebAdmin.IsRunning; },
                Restart = delegate { return app.WebAdmin != null && app.WebAdmin.Restart(); }
            };
        }

        public int PublicRestarts { get { return _public.Restarts; } }
        public int AdminRestarts { get { return _admin.Restarts; } }

        public void Start()
        {
            if (_app.WebPublic != null) _app.WebPublic.Failed += OnPublicFailed;
            if (_app.WebAdmin != null) _app.WebAdmin.Failed += OnAdminFailed;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "supervisor";
            _thread.Start();
            Log.Debug("APP", "supervisor started");
        }

        public void Stop()
        {
            try { _stop.Set(); } catch { }
            try { if (_thread != null && _thread.IsAlive) _thread.Join(3000); } catch { }
            try { if (_app.WebPublic != null) _app.WebPublic.Failed -= OnPublicFailed; } catch { }
            try { if (_app.WebAdmin != null) _app.WebAdmin.Failed -= OnAdminFailed; } catch { }
        }

        private void OnPublicFailed(object sender, EventArgs e)
        {
            Log.Error("APP", "supervisor: public board reported a failure");
        }

        private void OnAdminFailed(object sender, EventArgs e)
        {
            Log.Error("APP", "supervisor: admin board reported a failure");
        }

        private void Loop()
        {
            while (!_stop.WaitOne(2000))
            {
                if (_app.ShuttingDown) return;
                try
                {
                    DateTime now = DateTime.UtcNow;
                    Check(_public, now);
                    if (_app.WebAdmin != null) Check(_admin, now);

                    // debounced persistence
                    if ((now - _lastSaveCheck).TotalMilliseconds >= _app.Config.SaveDebounceMs)
                    {
                        _lastSaveCheck = now;
                        if (_app.Store.HasDirty) _app.Store.SaveIfDirty();
                    }

                    // expire sessions and captchas
                    _app.Sessions.Cleanup();
                    _app.Captcha.Prune();

                    // stories really do disappear on time, files included
                    if (_app.WebPublic != null && _app.WebPublic.Api != null)
                        _app.WebPublic.Api.SweepStories();

                    // peak online
                    _app.Stats.NotePeak(_app.Store.CountOnline(_app.Config.OnlineWindowSec));

                    // stats every minute
                    if ((now - _lastStatsSave).TotalMinutes >= 1)
                    {
                        _lastStatsSave = now;
                        if (_app.Stats.HasDirty) _app.Stats.Save();
                    }

                    // snapshots
                    if (_app.Config.BackupIntervalMin > 0 &&
                        (now - _lastBackup).TotalMinutes >= _app.Config.BackupIntervalMin)
                    {
                        _lastBackup = now;
                        string dir = _app.Store.CreateBackup();
                        if (dir != null) Log.Info("APP", "automatic backup: " + dir);
                    }

                    if (_app.Store.IsReadOnly && !_readOnlyLogged)
                    {
                        _readOnlyLogged = true;
                        Log.Error("APP", "storage is READ-ONLY (" + _app.Store.SaveError + "). Reads still work; fix the disk and use Admin -> Repair.");
                    }
                    else if (!_app.Store.IsReadOnly) _readOnlyLogged = false;

                    if ((now - _lastHealthLog).TotalMinutes >= 10)
                    {
                        _lastHealthLog = now;
                        Log.Info("APP", "health: " + _app.StatusLine());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("APP", "supervisor loop: " + ex.Message);
                }
            }
        }

        private void Check(Watched w, DateTime now)
        {
            bool running = false;
            try { running = w.IsRunning(); } catch { running = false; }
            if (running) return;
            if (now < w.BackoffUntil) return;

            w.Restarts++;
            int delay = BackoffSeconds(w.Restarts);
            w.BackoffUntil = now.AddSeconds(delay);
            Log.Warn("APP", "supervisor: restarting " + w.Name + " (attempt " + w.Restarts +
                            ", next retry not before " + delay + "s)");
            bool ok = false;
            try { ok = w.Restart(); } catch (Exception ex) { Log.Error("APP", w.Name + " restart threw: " + ex.Message); }
            Log.Warn("APP", "supervisor: " + w.Name + " restart " + (ok ? "succeeded" : "failed"));
        }

        private static int BackoffSeconds(int attempt)
        {
            int v = 1 << Math.Min(attempt, 5);
            return Math.Min(30, v);
        }
    }
}
