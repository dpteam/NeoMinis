using System;
using System.Collections.Generic;

namespace Social.Core
{
    public sealed class Session
    {
        public string Id = "";
        public long UserId;
        // A guest session carries no account: the router swaps in a throwaway
        // read-only viewer and refuses every write. See Config.GuestMode.
        public bool IsGuest;
        public byte[] Key;
        public DateTime Created = DateTime.UtcNow;
        public DateTime LastSeen = DateTime.UtcNow;
        public DateTime LastApi = DateTime.UtcNow;
        public string IpHash = "";
        public string UserAgentHash = "";
        public string LoginIp = "";
    }

    /// <summary>
    /// In-memory sessions. The session id lives in an HttpOnly cookie, the master key
    /// lives only in this process, so a restart simply asks everybody to log in again.
    /// </summary>
    public sealed class SessionManager
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Session> _byId = new Dictionary<string, Session>(StringComparer.Ordinal);
        private readonly Dictionary<long, List<Session>> _byUser = new Dictionary<long, List<Session>>();
        private readonly NonceCache _nonces = new NonceCache(TimeSpan.FromMinutes(10), 50000);
        private readonly Config _cfg;

        public SessionManager(Config cfg)
        {
            _cfg = cfg;
        }

        public int Count { get { lock (_sync) { return _byId.Count; } } }

        public Session Create(long userId, byte[] key, string ipHash, string uaHash, string loginIp)
        {
            return Create(userId, key, ipHash, uaHash, loginIp, false);
        }

        /// <summary>
        /// A read-only session with no account behind it. The key is derived from the
        /// shared guest secret, so the transport is signed and sealed exactly like a
        /// real login while the router keeps the viewer anonymous.
        /// </summary>
        public Session CreateGuest(byte[] key, string ipHash, string uaHash, string loginIp)
        {
            return Create(0, key, ipHash, uaHash, loginIp, true);
        }

        private Session Create(long userId, byte[] key, string ipHash, string uaHash, string loginIp, bool guest)
        {
            lock (_sync)
            {
                Session s = new Session();
                s.Id = Crypto.NewSessionId();
                s.UserId = userId;
                s.IsGuest = guest;
                s.Key = key;
                s.IpHash = ipHash ?? "";
                s.UserAgentHash = uaHash ?? "";
                s.LoginIp = loginIp ?? "";
                s.Created = DateTime.UtcNow;
                s.LastSeen = DateTime.UtcNow;
                s.LastApi = DateTime.UtcNow;

                _byId[s.Id] = s;
                List<Session> list;
                if (!_byUser.TryGetValue(userId, out list))
                {
                    list = new List<Session>();
                    _byUser[userId] = list;
                }
                list.Add(s);
                return s;
            }
        }

        public Session Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_sync)
            {
                Session s;
                if (!_byId.TryGetValue(id, out s)) return null;

                DateTime now = DateTime.UtcNow;
                bool expired = (now - s.LastSeen).TotalHours > _cfg.SessionHours ||
                               (now - s.Created).TotalHours > _cfg.SessionMaxHours;
                if (expired)
                {
                    RemoveLocked(s);
                    return null;
                }
                return s;
            }
        }

        public void Touch(Session s, string ipHash, string uaHash)
        {
            if (s == null) return;
            lock (_sync)
            {
                s.LastSeen = DateTime.UtcNow;
                s.LastApi = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(ipHash)) s.IpHash = ipHash;
                if (!string.IsNullOrEmpty(uaHash)) s.UserAgentHash = uaHash;
            }
        }

        public void Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_sync)
            {
                Session s;
                if (_byId.TryGetValue(id, out s)) RemoveLocked(s);
            }
        }

        public int RemoveForUser(long userId, string exceptId)
        {
            lock (_sync)
            {
                List<Session> list;
                if (!_byUser.TryGetValue(userId, out list)) return 0;
                List<Session> copy = new List<Session>(list);
                int n = 0;
                for (int i = 0; i < copy.Count; i++)
                {
                    if (copy[i].Id == exceptId) continue;
                    RemoveLocked(copy[i]);
                    n++;
                }
                return n;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _byId.Clear();
                _byUser.Clear();
            }
        }

        public bool UseNonce(Session s, string nonce)
        {
            if (s == null || string.IsNullOrEmpty(nonce)) return false;
            return _nonces.TryUse(s.Id + ":" + nonce);
        }

        public int CountForUser(long userId)
        {
            lock (_sync)
            {
                List<Session> list;
                return _byUser.TryGetValue(userId, out list) ? list.Count : 0;
            }
        }

        public void Cleanup()
        {
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                List<Session> dead = new List<Session>();
                foreach (KeyValuePair<string, Session> kv in _byId)
                {
                    Session s = kv.Value;
                    if ((now - s.LastSeen).TotalHours > _cfg.SessionHours ||
                        (now - s.Created).TotalHours > _cfg.SessionMaxHours)
                        dead.Add(s);
                }
                for (int i = 0; i < dead.Count; i++) RemoveLocked(dead[i]);
                if (dead.Count > 0) Log.Debug("AUTH", "expired sessions removed: " + dead.Count);
            }
        }

        private void RemoveLocked(Session s)
        {
            _byId.Remove(s.Id);
            List<Session> list;
            if (_byUser.TryGetValue(s.UserId, out list))
            {
                list.Remove(s);
                if (list.Count == 0) _byUser.Remove(s.UserId);
            }
        }
    }
}
