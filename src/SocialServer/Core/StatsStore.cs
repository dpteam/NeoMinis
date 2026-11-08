using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Social.Core
{
    public sealed class DayStat
    {
        public int Views;
        public int Unique;
    }

    /// <summary>
    /// Lifetime counters that survive restarts: starts, peak online, registrations,
    /// content totals and a 90 day visit graph. Only aggregate numbers are stored.
    /// </summary>
    public sealed class StatsStore
    {
        private readonly object _sync = new object();
        private readonly Config _cfg;
        private readonly Dictionary<string, DayStat> _days = new Dictionary<string, DayStat>(StringComparer.Ordinal);
        private readonly HashSet<string> _todayUnique = new HashSet<string>(StringComparer.Ordinal);

        public long Starts;
        public DateTime FirstStart = DateTime.MinValue;
        public DateTime LastStart = DateTime.MinValue;
        public long VisitsTotal;
        public long UniqueTotal;
        public int PeakOnline;
        public long Registrations;
        public long Posts;
        public long Comments;
        public long Likes;
        public long Messages;
        public long Reports;
        public long Logins;
        public long ApiRequests;
        public long ApiErrors;
        public string LastDay = "";

        private bool _dirty;
        private DateTime _lastSave = DateTime.MinValue;

        public StatsStore(Config cfg)
        {
            _cfg = cfg;
        }

        public bool HasDirty { get { lock (_sync) { return _dirty; } } }
        public DateTime LastSaveUtc { get { lock (_sync) { return _lastSave; } } }

        public void Load()
        {
            lock (_sync)
            {
                string path = Path.Combine(_cfg.DataDir, "stats.json");
                if (!File.Exists(path)) return;
                try
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    Dictionary<string, object> d = Json.ParseObject(text, 16);
                    Starts = Json.GetLong(d, "starts", 0);
                    FirstStart = Json.GetDate(d, "firstStart", DateTime.MinValue);
                    LastStart = Json.GetDate(d, "lastStart", DateTime.MinValue);
                    VisitsTotal = Json.GetLong(d, "visitsTotal", 0);
                    UniqueTotal = Json.GetLong(d, "uniqueTotal", 0);
                    PeakOnline = Json.GetInt(d, "peakOnline", 0);
                    Registrations = Json.GetLong(d, "registrations", 0);
                    Posts = Json.GetLong(d, "posts", 0);
                    Comments = Json.GetLong(d, "comments", 0);
                    Likes = Json.GetLong(d, "likes", 0);
                    Messages = Json.GetLong(d, "messages", 0);
                    Reports = Json.GetLong(d, "reports", 0);
                    Logins = Json.GetLong(d, "logins", 0);
                    ApiRequests = Json.GetLong(d, "apiRequests", 0);
                    ApiErrors = Json.GetLong(d, "apiErrors", 0);
                    LastDay = Json.GetString(d, "lastDay", "");

                    object days = Json.Get(d, "days");
                    List<object> arr = days as List<object>;
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            Dictionary<string, object> dd = arr[i] as Dictionary<string, object>;
                            if (dd == null) continue;
                            string day = Json.GetString(dd, "day", "");
                            if (day.Length != 10) continue;
                            DayStat ds = new DayStat();
                            ds.Views = Json.GetInt(dd, "views", 0);
                            ds.Unique = Json.GetInt(dd, "unique", 0);
                            _days[day] = ds;
                        }
                    }
                    Log.Info("DATA", "stats loaded: starts=" + Starts + " visits=" + VisitsTotal + " peak=" + PeakOnline);
                }
                catch (Exception ex)
                {
                    Log.Warn("DATA", "stats.json is broken: " + ex.Message);
                }
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(_cfg.DataDir);
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["starts"] = Starts;
                    d["firstStart"] = FirstStart;
                    d["lastStart"] = LastStart;
                    d["visitsTotal"] = VisitsTotal;
                    d["uniqueTotal"] = UniqueTotal;
                    d["peakOnline"] = PeakOnline;
                    d["registrations"] = Registrations;
                    d["posts"] = Posts;
                    d["comments"] = Comments;
                    d["likes"] = Likes;
                    d["messages"] = Messages;
                    d["reports"] = Reports;
                    d["logins"] = Logins;
                    d["apiRequests"] = ApiRequests;
                    d["apiErrors"] = ApiErrors;
                    d["lastDay"] = LastDay;

                    List<object> arr = new List<object>();
                    List<string> keys = new List<string>(_days.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        DayStat ds = _days[keys[i]];
                        Dictionary<string, object> dd = new Dictionary<string, object>();
                        dd["day"] = keys[i];
                        dd["views"] = ds.Views;
                        dd["unique"] = ds.Unique;
                        arr.Add(dd);
                    }
                    d["days"] = arr;

                    JsonFile.WriteAtomic(Path.Combine(_cfg.DataDir, "stats.json"), Json.Stringify(d));
                    _dirty = false;
                    _lastSave = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Log.Warn("DATA", "stats save failed: " + ex.Message);
                }
            }
        }

        public void SaveIfDirty()
        {
            lock (_sync) { if (!_dirty) return; }
            Save();
        }

        public void OnStart()
        {
            lock (_sync)
            {
                Starts++;
                DateTime now = DateTime.UtcNow;
                if (FirstStart == DateTime.MinValue) FirstStart = now;
                LastStart = now;
                _dirty = true;
            }
        }

        public void OnRegistration()
        {
            lock (_sync) { Registrations++; _dirty = true; }
        }

        public void OnLogin()
        {
            lock (_sync) { Logins++; _dirty = true; }
        }

        public void OnPost()
        {
            lock (_sync) { Posts++; _dirty = true; }
        }

        public void OnComment()
        {
            lock (_sync) { Comments++; _dirty = true; }
        }

        public void OnLike()
        {
            lock (_sync) { Likes++; _dirty = true; }
        }

        public void OnMessage()
        {
            lock (_sync) { Messages++; _dirty = true; }
        }

        public void OnReport()
        {
            lock (_sync) { Reports++; _dirty = true; }
        }

        public void OnApiRequest()
        {
            lock (_sync) { ApiRequests++; }
        }

        public void OnApiError()
        {
            lock (_sync) { ApiErrors++; }
        }

        public void NotePeak(int online)
        {
            lock (_sync)
            {
                if (online > PeakOnline) { PeakOnline = online; _dirty = true; }
            }
        }

        public bool Visit(string visitorHash)
        {
            lock (_sync)
            {
                string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                if (LastDay != today)
                {
                    LastDay = today;
                    _todayUnique.Clear();
                }

                DayStat ds;
                if (!_days.TryGetValue(today, out ds))
                {
                    ds = new DayStat();
                    _days[today] = ds;
                }
                ds.Views++;
                VisitsTotal++;
                bool unique = _todayUnique.Add(visitorHash);
                if (unique)
                {
                    ds.Unique++;
                    UniqueTotal++;
                }
                _dirty = true;

                if (_days.Count > 120) PruneDaysLocked();
                return unique;
            }
        }

        private void PruneDaysLocked()
        {
            List<string> keys = new List<string>(_days.Keys);
            keys.Sort(StringComparer.Ordinal);
            int remove = keys.Count - 90;
            for (int i = 0; i < remove; i++) _days.Remove(keys[i]);
        }

        public int ViewsToday()
        {
            lock (_sync)
            {
                DayStat ds;
                return _days.TryGetValue(DateTime.UtcNow.ToString("yyyy-MM-dd"), out ds) ? ds.Views : 0;
            }
        }

        public int UniqueToday()
        {
            lock (_sync)
            {
                DayStat ds;
                return _days.TryGetValue(DateTime.UtcNow.ToString("yyyy-MM-dd"), out ds) ? ds.Unique : 0;
            }
        }

        public List<KeyValuePair<string, DayStat>> RecentDays(int count)
        {
            List<KeyValuePair<string, DayStat>> list = new List<KeyValuePair<string, DayStat>>();
            lock (_sync)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    string day = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd");
                    DayStat ds;
                    if (!_days.TryGetValue(day, out ds)) ds = new DayStat();
                    list.Add(new KeyValuePair<string, DayStat>(day, ds));
                }
            }
            return list;
        }
    }
}
