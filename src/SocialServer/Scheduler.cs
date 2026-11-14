using System;
using System.Collections.Generic;
using System.Threading;
using Social.Core;

namespace Social
{
    /// <summary>
    /// Publishes posts that were scheduled for a later time. One thread, a cheap
    /// check every 20 seconds; it never touches the network and stops cleanly.
    /// </summary>
    public sealed class Scheduler
    {
        private readonly SocialStore _store;
        private readonly object _sync = new object();
        private Thread _thread;
        private volatile bool _stop;

        public Scheduler(SocialStore store)
        {
            _store = store;
        }

        public void Start()
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "social-scheduler";
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            Thread t;
            lock (_sync) { t = _thread; _thread = null; }
            if (t != null) { try { t.Join(1500); } catch { } }
        }

        private void Loop()
        {
            // publish whatever came due while the server was down, then keep watching
            Tick();
            while (!_stop)
            {
                for (int i = 0; i < 40 && !_stop; i++) Thread.Sleep(500);
                if (_stop) break;
                Tick();
            }
        }

        private void Tick()
        {
            try
            {
                List<long> published = _store.PublishDue();
                for (int i = 0; i < published.Count; i++)
                    Log.Info("DATA", "scheduled post #" + published[i] + " published");
            }
            catch (Exception ex)
            {
                Log.Error("DATA", "scheduler tick failed: " + ex.Message);
            }
        }
    }
}
