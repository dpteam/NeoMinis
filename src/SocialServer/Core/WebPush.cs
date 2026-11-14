using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Social.Core
{
    /// <summary>
    /// Web Push, the honest way for .NET 4.0.
    ///
    /// A push message with an encrypted payload needs ECDH P-256 plus AES-128-GCM,
    /// and GCM does not exist in this framework (AesManaged is CBC only). Writing a
    /// GCM by hand is exactly the kind of thing that looks fine and then leaks, so
    /// this sends a <em>tickle</em> instead: a correctly signed, empty push. The
    /// service worker wakes up, fetches what is waiting over its own authenticated
    /// connection, and shows the notification. Same user-visible result, no
    /// home-made crypto.
    ///
    /// What it does need is VAPID: an ES256 JWT signed with a P-256 key, which
    /// <see cref="ECDsaCng"/> can do. On a platform without CNG (XP) the feature
    /// simply reports itself unavailable instead of failing at the first message.
    /// </summary>
    public static class WebPush
    {
        private static Config _cfg;
        private static SocialStore _store;
        private static readonly object Sync = new object();
        private static readonly Queue<long> Pending = new Queue<long>();
        private static Thread _worker;
        private static bool _stopping;

        /// <summary>True when this platform can actually sign a VAPID token.</summary>
        public static bool Supported
        {
            get
            {
                if (!_cfg.PushEnabled) return false;
                if (string.IsNullOrEmpty(_cfg.PushPrivateKey)) return false;
                return PlatformOk;
            }
        }

        private static bool _platformChecked;
        private static bool _platformOk;

        private static bool PlatformOk
        {
            get
            {
                if (_platformChecked) return _platformOk;
                _platformChecked = true;
                try
                {
                    // CNG needs Windows Vista or later; XP throws here, which is
                    // exactly what we want to find out before promising anything.
                    byte[] probe = CngKey.Import(Convert.FromBase64String(_cfg.PushPrivateKey),
                                                 CngKeyBlobFormat.EccPrivateBlob).Export(CngKeyBlobFormat.EccPublicBlob);
                    _platformOk = probe != null && probe.Length > 8;
                }
                catch { _platformOk = false; }
                return _platformOk;
            }
        }

        public static void Configure(Config cfg, SocialStore store)
        {
            _cfg = cfg;
            _store = store;
            if (cfg.PushEnabled && string.IsNullOrEmpty(cfg.PushPrivateKey))
            {
                string priv;
                if (GenerateKeys(out priv))
                {
                    // SetIniValue writes the file; the running process needs the
                    // field too, or every check below sees an empty key
                    cfg.PushPrivateKey = priv;
                    cfg.SetIniValue("push", "privateKey", priv);
                    Log.Info("PUSH", "generated a VAPID key pair");
                }
                else
                {
                    Log.Warn("PUSH", "this platform cannot create a P-256 key; push stays off");
                }
            }
        }

        /// <summary>
        /// Creates a P-256 key pair and stores the private half as a CNG blob in
        /// base64. The public half is derived from it on demand, so there is only
        /// one secret to keep and one thing that can go missing.
        /// </summary>
        public static bool GenerateKeys(out string privateBase64)
        {
            privateBase64 = "";
            try
            {
                CngKeyCreationParameters p = new CngKeyCreationParameters();
                p.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
                using (CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, null, p))
                {
                    privateBase64 = Convert.ToBase64String(key.Export(CngKeyBlobFormat.EccPrivateBlob));
                }
                return privateBase64.Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// The uncompressed public point (0x04 || X || Y), base64url. This is the
        /// `applicationServerKey` the browser subscribes with, so it has to match
        /// the key that signs the tokens.
        /// </summary>
        public static string PublicKeyBase64Url()
        {
            if (_cfg == null || string.IsNullOrEmpty(_cfg.PushPrivateKey)) return "";
            try
            {
                byte[] priv = Convert.FromBase64String(_cfg.PushPrivateKey);
                using (CngKey key = CngKey.Import(priv, CngKeyBlobFormat.EccPrivateBlob))
                {
                    byte[] blob = key.Export(CngKeyBlobFormat.EccPublicBlob);
                    // BCRYPT_ECCKEY_BLOB: 4-byte magic, 4-byte cbKey, then X and Y
                    if (blob.Length <= 8) return "";
                    int n = blob.Length - 8;
                    byte[] point = new byte[n + 1];
                    point[0] = 0x04;
                    Buffer.BlockCopy(blob, 8, point, 1, n);
                    return Base64Url(point);
                }
            }
            catch { return ""; }
        }

        /// <summary>The origin a subscription belongs to: the "aud" of the token.</summary>
        private static string Audience(string endpoint)
        {
            try
            {
                Uri uri = new Uri(endpoint);
                return uri.Scheme + "://" + uri.Host;
            }
            catch { return ""; }
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Builds the Authorization header: a short-lived ES256 JWT plus the public
        /// key, exactly as RFC 8292 wants it.
        /// </summary>
        public static string VapidHeader(string endpoint)
        {
            string aud = Audience(endpoint);
            if (aud.Length == 0) return "";
            long exp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + 12 * 3600;

            string header = "{\"typ\":\"JWT\",\"alg\":\"ES256\"}";
            string payload = "{\"aud\":\"" + aud + "\",\"exp\":" + exp.ToString(CultureInfo.InvariantCulture) +
                             ",\"sub\":\"" + JsonEscape(string.IsNullOrEmpty(_cfg.PushSubject) ? "mailto:admin@example.com" : _cfg.PushSubject) + "\"}";
            string signingInput = Base64Url(Encoding.UTF8.GetBytes(header)) + "." +
                                  Base64Url(Encoding.UTF8.GetBytes(payload));

            byte[] sig = Sign(signingInput);
            if (sig == null) return "";
            return "vapid t=" + signingInput + "." + Base64Url(sig) + ", k=" + PublicKeyBase64Url();
        }

        private static byte[] Sign(string input)
        {
            try
            {
                byte[] priv = Convert.FromBase64String(_cfg.PushPrivateKey);
                using (CngKey key = CngKey.Import(priv, CngKeyBlobFormat.EccPrivateBlob))
                using (ECDsaCng ecdsa = new ECDsaCng(key))
                {
                    byte[] hash = Crypto.Sha256(Encoding.UTF8.GetBytes(input));
                    byte[] sig = ecdsa.SignHash(hash);
                    // CNG hands back the raw R||S pair; some runtimes wrap it in a
                    // DER SEQUENCE instead. Accept either rather than guess.
                    if (sig != null && sig.Length == 64) return sig;
                    return DerToRaw(sig);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// ES256 wants the signature as R||S, 32 bytes each. .NET hands back a DER
        /// SEQUENCE of two INTEGERs, which may carry a leading zero when the top bit
        /// is set, so this has to strip and re-pad rather than copy.
        /// </summary>
        public static byte[] DerToRaw(byte[] der)
        {
            if (der == null || der.Length < 8 || der[0] != 0x30) return null;
            int pos = 2;
            // long-form length: not expected for P-256, but do not fall over
            if ((der[1] & 0x80) != 0)
            {
                int n = der[1] & 0x7f;
                pos = 2 + n;
            }
            byte[] r = ReadInt(der, ref pos);
            byte[] s = ReadInt(der, ref pos);
            if (r == null || s == null) return null;

            byte[] raw = new byte[64];
            Buffer.BlockCopy(Pad(r), 0, raw, 0, 32);
            Buffer.BlockCopy(Pad(s), 0, raw, 32, 32);
            return raw;
        }

        private static byte[] ReadInt(byte[] der, ref int pos)
        {
            if (pos + 2 > der.Length || der[pos] != 0x02) return null;
            int len = der[pos + 1];
            pos += 2;
            if (len <= 0 || pos + len > der.Length) return null;
            byte[] value = new byte[len];
            Buffer.BlockCopy(der, pos, value, 0, len);
            pos += len;
            // drop the sign byte
            int start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            byte[] trimmed = new byte[value.Length - start];
            Buffer.BlockCopy(value, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>Left-pads a big-endian integer to 32 bytes.</summary>
        private static byte[] Pad(byte[] value)
        {
            byte[] outBytes = new byte[32];
            if (value.Length >= 32)
            {
                Buffer.BlockCopy(value, value.Length - 32, outBytes, 0, 32);
                return outBytes;
            }
            Buffer.BlockCopy(value, 0, outBytes, 32 - value.Length, value.Length);
            return outBytes;
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') { sb.Append('\\'); sb.Append(c); }
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------- sending

        /// <summary>
        /// Queues one tickle for a user. Called from Notify, so it must never block
        /// and never throw: a notification that cannot be pushed is still a
        /// notification in the list.
        /// </summary>
        public static void Enqueue(long userId)
        {
            if (_store == null || _cfg == null) return;
            if (!_cfg.PushEnabled || userId <= 0) return;
            lock (Sync)
            {
                if (_stopping) return;
                if (Pending.Count > 500) return;   // a storm must not grow without bound
                Pending.Enqueue(userId);
                if (_worker == null)
                {
                    _worker = new Thread(Loop);
                    _worker.IsBackground = true;
                    _worker.Name = "webpush";
                    _worker.Start();
                }
                Monitor.Pulse(Sync);
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                _stopping = true;
                Monitor.Pulse(Sync);
            }
        }

        private static void Loop()
        {
            while (true)
            {
                long userId;
                lock (Sync)
                {
                    if (_stopping) return;
                    if (Pending.Count == 0) { Monitor.Wait(Sync, 5000); continue; }
                    userId = Pending.Dequeue();
                }
                try { SendToUser(userId); }
                catch (Exception ex) { Log.Warn("PUSH", "delivery failed for #" + userId + ": " + ex.Message); }
            }
        }

        /// <summary>One tickle to every browser this user has subscribed.</summary>
        public static int SendToUser(long userId)
        {
            if (!Supported) return 0;
            List<PushSub> subs = _store.PushSubsFor(userId);
            int sent = 0;
            for (int i = 0; i < subs.Count; i++)
            {
                PushSub sub = subs[i];
                int status = Send(sub.Endpoint);
                if (status >= 200 && status < 300)
                {
                    _store.MarkPushResult(sub.Id, true);
                    sent++;
                }
                else if (status == 404 || status == 410)
                {
                    // the browser threw the subscription away: forget it
                    _store.RemovePushSubById(sub.Id);
                    Log.Info("PUSH", "dropped a dead subscription for #" + userId);
                }
                else
                {
                    _store.MarkPushResult(sub.Id, false);
                    if (sub.Fails >= 12) _store.RemovePushSubById(sub.Id);
                }
            }
            return sent;
        }

        /// <summary>
        /// POSTs an empty push. 201 means accepted, 404/410 means the subscription
        /// is gone, anything else is a temporary problem worth retrying later.
        /// </summary>
        public static int Send(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return 0;
            string auth = VapidHeader(endpoint);
            if (auth.Length == 0) return 0;

            HttpWebRequest req = null;
            try
            {
                // .NET 4.0 predates the TLS 1.2 enum, but the number still works
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

                req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.Method = "POST";
                req.ContentLength = 0;
                req.UserAgent = "SocialServer/" + "1.0";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.Headers["Authorization"] = auth;
                req.Headers["TTL"] = (_cfg.PushTtlSec > 0 ? _cfg.PushTtlSec : 3600).ToString(CultureInfo.InvariantCulture);
                req.Headers["Urgency"] = "normal";

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    return (int)res.StatusCode;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                if (res != null)
                {
                    int code = (int)res.StatusCode;
                    res.Close();
                    return code;
                }
                return 0;
            }
            catch { return 0; }
            finally
            {
                if (req != null) { try { req.Abort(); } catch { } }
            }
        }
    }
}
