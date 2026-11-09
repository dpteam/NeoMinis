using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Social.Core;

namespace Social.Web
{
    public enum WebRole
    {
        /// <summary>Public board: feed, profiles, media. /api/admin/ does not exist here.</summary>
        Public = 0,
        /// <summary>Admin board on its own port: full API including /api/admin/.</summary>
        Admin = 1
    }

    /// <summary>
    /// One HTTP listener: static SPA, media files and the encrypted /api/ surface.
    /// Custom HTTP/1.1 on top of TcpListener so no URL ACL (netsh) is needed on XP.
    /// Two instances run in one process: the public board and the admin board.
    /// </summary>
    public sealed class WebServer
    {
        public event EventHandler Failed;

        private readonly Config _cfg;
        private readonly WebRole _role;
        private readonly SocialStore _store;
        private readonly SessionManager _sessions;
        private readonly StatsStore _stats;
        private readonly CaptchaStore _captcha;
        private readonly MediaStore _media;
        private readonly AiEngine _ai;
        private readonly AiRunner _aiRunner;
        private readonly ApiRouter _api;

        private readonly object _rateLock = new object();
        private readonly Dictionary<string, RateWindow> _rate = new Dictionary<string, RateWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FailWindow> _authFails = new Dictionary<string, FailWindow>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _bannedIps = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private int _port;
        private string _bind = "0.0.0.0";        private string _root = "public";
        private string _basePath = "";
        private long _requests;
        private long _errors;
        private int _inflight;

        public WebServer(Config cfg, SocialStore store, SessionManager sessions, StatsStore stats,
                         CaptchaStore captcha, MediaStore media, AiEngine ai, AiRunner aiRunner, WebRole role)
        {
            _cfg = cfg;
            _store = store;
            _sessions = sessions;
            _stats = stats;
            _captcha = captcha;
            _media = media;
            _ai = ai;
            _aiRunner = aiRunner;
            _role = role;
            _root = cfg.WebRoot;
            _basePath = cfg.PublicBasePath ?? "";
            _bind = role == WebRole.Admin ? (cfg.AdminBind ?? "0.0.0.0") : (cfg.Bind ?? "0.0.0.0");
            _api = new ApiRouter(this, cfg, store, sessions, stats, captcha, media, ai, aiRunner);
        }

        public WebRole Role { get { return _role; } }
        public bool IsAdminBoard { get { return _role == WebRole.Admin; } }
        public string RoleName { get { return _role == WebRole.Admin ? "admin" : "public"; } }

        public bool IsRunning { get { return _running; } }
        public int Port { get { return _port; } }
        public string Bind { get { return _bind; } }
        public long Requests { get { return Interlocked.Read(ref _requests); } }
        public long Errors { get { return Interlocked.Read(ref _errors); } }
        public int Inflight { get { return Interlocked.CompareExchange(ref _inflight, 0, 0); } }
        public ApiRouter Api { get { return _api; } }

        // ============================================================== start

        public bool Start()
        {
            if (_running) return true;
            try
            {
                if (!Directory.Exists(_root))
                {
                    Log.Error("WEB", "web root not found: " + _root);
                    return false;
                }
                IPAddress address = ResolveBindAddress(_bind);
                int[] candidates = CandidatePorts();
                for (int i = 0; i < candidates.Length; i++)
                {
                    TcpListener listener = null;
                    try
                    {
                        listener = new TcpListener(address, candidates[i]);
                        listener.Start(_cfg.Backlog);
                        _listener = listener;
                        _port = candidates[i];
                        break;
                    }
                    catch (SocketException ex)
                    {
                        Log.Warn("WEB", "port " + candidates[i] + " is busy (" + ex.SocketErrorCode + "), trying next");
                        try { if (listener != null) listener.Stop(); } catch { }
                    }
                }

                if (_listener == null)
                {
                    Log.Error("WEB", "cannot bind any port: " + JoinPorts(candidates));
                    return false;
                }

                _running = true;
                _thread = new Thread(AcceptLoop);
                _thread.IsBackground = true;
                _thread.Name = "web-" + RoleName;
                _thread.Start();
                Log.Info("WEB", RoleName + " board listening on http://" + _bind + ":" + _port + _basePath + "/ (root " + _root + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("WEB", "start failed: " + ex.Message);
                _running = false;
                return false;
            }
        }

        public bool Restart()
        {
            Stop();
            Thread.Sleep(250);
            return Start();
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_thread != null && _thread.IsAlive) _thread.Join(3000); } catch { }
            _listener = null;
            _thread = null;
            Log.Info("WEB", "stopped");
        }

        private int[] CandidatePorts()
        {
            List<int> list = new List<int>();
            int primary = _role == WebRole.Admin ? _cfg.AdminPort : _cfg.Port;
            int[] fallbacks = _role == WebRole.Admin ? _cfg.AdminPortFallbacks : _cfg.PortFallbacks;
            list.Add(primary);
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++)
                    if (!list.Contains(fallbacks[i])) list.Add(fallbacks[i]);
            }
            return list.ToArray();
        }

        private static string JoinPorts(int[] ports)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ports.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ports[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static IPAddress ResolveBindAddress(string text)
        {
            if (string.IsNullOrEmpty(text) || text == "*" || text == "0.0.0.0") return IPAddress.Any;
            if (text == "::" || text == "[::]") return IPAddress.IPv6Any;
            if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
            IPAddress ip;
            if (IPAddress.TryParse(text, out ip)) return ip;
            return IPAddress.Any;
        }

        public string[] LocalAddresses()
        {
            List<string> list = new List<string>();
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                for (int i = 0; i < host.AddressList.Length; i++)
                {
                    string s = Util.Ip(host.AddressList[i]);
                    if (!list.Contains(s)) list.Add(s);
                }
            }
            catch { }
            return list.ToArray();
        }

        // ============================================================== accept

        private void AcceptLoop()
        {
            int failures = 0;
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    failures = 0;
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
                catch (SocketException sex)
                {
                    if (!_running) break;
                    failures++;
                    Log.Warn("WEB", "accept failed (" + sex.SocketErrorCode + "): " + sex.Message);
                    if (failures >= 25) { RaiseFailed(); break; }
                    Thread.Sleep(Math.Min(2000, 50 * failures));
                    continue;
                }
                catch (Exception ex)
                {
                    if (!_running) break;
                    failures++;
                    Log.Error("WEB", "unexpected accept error: " + ex.Message);
                    if (failures >= 25) { RaiseFailed(); break; }
                    Thread.Sleep(100);
                    continue;
                }

                if (client == null) continue;

                int now = Interlocked.Increment(ref _inflight);
                if (now > _cfg.MaxConcurrentRequests)
                {
                    Interlocked.Decrement(ref _inflight);
                    try
                    {
                        client.Client.Send(Encoding.ASCII.GetBytes(
                            "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\nRetry-After: 2\r\n\r\n"));
                    }
                    catch { }
                    try { client.Close(); } catch { }
                    continue;
                }

                try { ThreadPool.QueueUserWorkItem(ProcessConnection, client); }
                catch (Exception ex)
                {
                    Interlocked.Decrement(ref _inflight);
                    Log.Warn("WEB", "cannot queue connection: " + ex.Message);
                    try { client.Close(); } catch { }
                }
            }
            Log.Debug("WEB", "accept loop finished");
        }

        private void RaiseFailed()
        {
            try
            {
                EventHandler h = Failed;
                if (h != null) h(this, EventArgs.Empty);
            }
            catch { }
        }

        // ========================================================== connection

        private void ProcessConnection(object state)
        {
            TcpClient client = (TcpClient)state;
            HttpConnection conn = null;
            try
            {
                try { client.NoDelay = true; } catch { }
                try { client.ReceiveTimeout = _cfg.RequestTimeoutMs; } catch { }
                try { client.SendTimeout = _cfg.RequestTimeoutMs; } catch { }

                conn = new HttpConnection(client, _cfg.MaxBodyBytes + 65536);
                IPEndPoint ep = null;
                try { ep = client.Client.RemoteEndPoint as IPEndPoint; } catch { }
                if (ep != null)
                {
                    conn.RemoteIp = Util.Ip(ep.Address);
                    conn.RemotePort = ep.Port;
                }

                int served = 0;
                while (_running && served < _cfg.KeepAliveMax)
                {
                    HttpRequest req;
                    int errorStatus;
                    bool ok;
                    try
                    {
                        ok = conn.TryReadRequest(out req, out errorStatus);
                    }
                    catch (HttpProtocolException hpe)
                    {
                        try { conn.WriteSimple(hpe.Status, HttpResponse.Reason(hpe.Status), true); } catch { }
                        break;
                    }
                    catch (IOException) { break; }
                    catch (SocketException) { break; }

                    if (!ok)
                    {
                        if (errorStatus != 0)
                        {
                            try { conn.WriteSimple(errorStatus, HttpResponse.Reason(errorStatus), true); } catch { }
                        }
                        break;
                    }

                    served++;
                    Interlocked.Increment(ref _requests);

                    string clientIp = EffectiveClientIp(req);
                    req.IsHttps = IsHttps(req);
                    if (IsBanned(clientIp))
                    {
                        HttpResponse banned = new HttpResponse();
                        banned.Status = 403;
                        banned.ContentType = "text/plain; charset=utf-8";
                        banned.Body = Encoding.UTF8.GetBytes("Temporarily blocked.");
                        ApplySecurityHeaders(banned, req);
                        conn.WriteResponse(banned, false, false);
                        break;
                    }

                    if (!RateAllowed(clientIp))
                    {
                        HttpResponse busy = new HttpResponse();
                        busy.Status = 429;
                        busy.ContentType = "application/json; charset=utf-8";
                        busy.Body = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"rate-limit\",\"message\":\"Too many requests\"}");
                        busy.Set("Retry-After", "5");
                        ApplySecurityHeaders(busy, req);
                        conn.WriteResponse(busy, false, false);
                        break;
                    }

                    DateTime started = DateTime.UtcNow;
                    HttpResponse resp = new HttpResponse();
                    try
                    {
                        Route(req, resp, clientIp);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _errors);
                        _stats.OnApiError();
                        Log.Error("WEB", req.Method + " " + req.Target + " failed: " + ex);
                        resp = new HttpResponse();
                        resp.Status = 500;
                        resp.ContentType = "application/json; charset=utf-8";
                        resp.Body = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"internal\",\"message\":\"Internal server error\"}");
                        ApplySecurityHeaders(resp, req);
                    }

                    int ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                    Log.Api("WEB", req.Method + " " + req.Path + " -> " + resp.Status + " (" + ms + "ms, " +
                                    clientIp + ", " + (resp.Body == null ? 0 : resp.Body.Length) + "B)");

                    bool keep = req.KeepAlive && served < _cfg.KeepAliveMax;
                    conn.WriteResponse(resp, string.Equals(req.Method, "HEAD", StringComparison.OrdinalIgnoreCase), keep);
                    if (!keep) break;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errors);
                Log.Warn("WEB", "connection error: " + ex.Message);
            }
            finally
            {
                if (conn != null) conn.Dispose();
                else { try { client.Close(); } catch { } }
                Interlocked.Decrement(ref _inflight);
            }
        }

        // ============================================================ routing

        private void Route(HttpRequest req, HttpResponse resp, string clientIp)
        {
            string path = MapPath(req);
            req.Path = path;

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                // The admin surface only exists on the admin board. On the public port
                // it answers exactly like a typo, so its shape is not discoverable.
                if (!IsAdminBoard && path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Api("SEC", "admin API call on the public board from " + clientIp + " -> 404");
                    SendJson(req, resp, 404, "{\"ok\":false,\"error\":\"unknown-endpoint\",\"message\":\"Unknown endpoint\"}");
                    return;
                }

                if (IsAdminBoard && _cfg.AdminPrivateOnly && !Util.IsPrivateIpv4(clientIp))
                {
                    Log.Warn("SEC", "admin board from public address " + clientIp + " rejected");
                    SendJson(req, resp, 403, "{\"ok\":false,\"error\":\"admin-private-only\",\"message\":\"Admin board is LAN only\"}");
                    return;
                }

                if (!_api.Handle(req, resp, clientIp))
                {
                    SendJson(req, resp, 404, "{\"ok\":false,\"error\":\"unknown-endpoint\",\"message\":\"Unknown endpoint\"}");
                }
                return;
            }

            if (path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
            {
                ServeMedia(req, resp, path);
                return;
            }

            // optional brand palette generated from config.ini [branding]
            if (path.Equals("/brand.css", StringComparison.OrdinalIgnoreCase))
            {
                SendBytes(req, resp, 200, Encoding.UTF8.GetBytes(Branding.Css(_cfg)),
                          "text/css; charset=utf-8", true, false, null);
                return;
            }

            ServeStatic(req, resp, path);
        }

        public string MapPath(HttpRequest req)
        {
            string path = req.Path ?? "/";
            if (_basePath.Length > 0)
            {
                if (path.Equals(_basePath, StringComparison.OrdinalIgnoreCase)) path = "/";
                else if (path.StartsWith(_basePath + "/", StringComparison.OrdinalIgnoreCase))
                    path = path.Substring(_basePath.Length);
            }
            if (path.Length == 0) path = "/";
            return path;
        }

        // --------------------------------------------------------- static files

        private static readonly Dictionary<string, string> MimeTypes = BuildMimeTypes();

        private static Dictionary<string, string> BuildMimeTypes()
        {
            Dictionary<string, string> m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            m[".html"] = "text/html; charset=utf-8";
            m[".htm"] = "text/html; charset=utf-8";
            m[".css"] = "text/css; charset=utf-8";
            m[".js"] = "application/javascript; charset=utf-8";
            m[".json"] = "application/json; charset=utf-8";
            m[".svg"] = "image/svg+xml";
            m[".png"] = "image/png";
            m[".jpg"] = "image/jpeg";
            m[".jpeg"] = "image/jpeg";
            m[".gif"] = "image/gif";
            m[".webp"] = "image/webp";
            m[".ico"] = "image/x-icon";
            m[".woff"] = "font/woff";
            m[".woff2"] = "font/woff2";
            m[".ttf"] = "font/ttf";
            m[".txt"] = "text/plain; charset=utf-8";
            m[".map"] = "application/json; charset=utf-8";
            m[".xml"] = "application/xml; charset=utf-8";
            m[".webmanifest"] = "application/manifest+json";
            m[".webm"] = "audio/webm";
            m[".ogg"] = "audio/ogg";
            m[".oga"] = "audio/ogg";
            m[".mp3"] = "audio/mpeg";
            m[".m4a"] = "audio/mp4";
            m[".wav"] = "audio/wav";
            return m;
        }

        public static string MimeFor(string path)
        {
            string ext = Path.GetExtension(path);
            string type;
            return !string.IsNullOrEmpty(ext) && MimeTypes.TryGetValue(ext, out type) ? type : "application/octet-stream";
        }

        private void ServeStatic(HttpRequest req, HttpResponse resp, string urlPath)
        {
            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(req.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                SendJson(req, resp, 405, "{\"ok\":false,\"error\":\"method-not-allowed\"}");
                return;
            }

            string relative = urlPath;
            if (relative == "/" || relative.Length == 0) relative = "/index.html";

            if (relative.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                string svg = Path.Combine(_root, "assets", "img", "favicon.svg");
                if (File.Exists(svg)) { SendFile(req, resp, svg, "image/svg+xml", true); return; }
            }

            if (relative.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            {
                string robots = Path.Combine(_root, "robots.txt");
                if (File.Exists(robots)) { SendFile(req, resp, robots, "text/plain; charset=utf-8", false); return; }
            }

            string file = ResolveSafe(relative, _root);
            if (file == null)
            {
                Log.Warn("SEC", "path traversal attempt: " + req.Target);
                SendText(req, resp, 400, "Bad request");
                return;
            }

            if (Directory.Exists(file))
            {
                string index = Path.Combine(file, "index.html");
                if (File.Exists(index)) { SendFile(req, resp, index, MimeFor(index), false); return; }
                SendText(req, resp, 403, "Forbidden");
                return;
            }

            if (!File.Exists(file))
            {
                string notFound = Path.Combine(_root, "404.html");
                if (File.Exists(notFound) && AcceptsHtml(req))
                {
                    SendFileWithStatus(req, resp, notFound, MimeFor(notFound), false, 404);
                    return;
                }
                SendText(req, resp, 404, "Not found");
                return;
            }

            bool longCache = relative.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase);
            SendFile(req, resp, file, MimeFor(file), longCache);        }

        private void ServeMedia(HttpRequest req, HttpResponse resp, string urlPath)
        {
            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(req.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                SendJson(req, resp, 405, "{\"ok\":false,\"error\":\"method-not-allowed\"}");
                return;
            }

            string rel = urlPath.Substring("/media/".Length);
            string file = ResolveSafe("/" + rel, _cfg.MediaDir);
            if (file == null || !File.Exists(file))
            {
                SendText(req, resp, 404, "Not found");
                return;
            }
            resp.Set("X-Content-Type-Options", "nosniff");
            resp.Set("Content-Disposition", "inline");
            SendFile(req, resp, file, MimeFor(file), true);
        }

        private static string ResolveSafe(string urlPath, string root)
        {
            try
            {
                string clean = urlPath.Replace('\\', '/');
                if (clean.IndexOf('\0') >= 0) return null;
                if (clean.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
                clean = clean.TrimStart('/');
                string combined = Path.Combine(root, clean.Replace('/', Path.DirectorySeparatorChar));
                string full = Path.GetFullPath(combined);
                string rootFull = Path.GetFullPath(root);
                if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    rootFull += Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;
                return full;
            }
            catch { return null; }
        }

        private static bool AcceptsHtml(HttpRequest req)
        {
            string accept = req.Header("Accept") ?? "";
            return accept.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0 || accept.Length == 0;
        }

        public void SendFile(HttpRequest req, HttpResponse resp, string file, string contentType, bool longCache)
        {
            SendFileWithStatus(req, resp, file, contentType, longCache, 200);
        }

        private void SendFileWithStatus(HttpRequest req, HttpResponse resp, string file, string contentType, bool longCache, int status)
        {
            FileInfo fi = new FileInfo(file);
            if (!fi.Exists) { SendText(req, resp, 404, "Not found"); return; }

            DateTime lastWrite = fi.LastWriteTimeUtc;
            string etag = "\"" + fi.Length.ToString(CultureInfo.InvariantCulture) + "-" +
                          lastWrite.Ticks.ToString(CultureInfo.InvariantCulture) + "\"";

            if (status == 200 && CheckNotModified(req, etag, lastWrite))
            {
                resp.Status = 304;
                resp.ContentType = null;
                resp.Set("ETag", etag);
                resp.Set("Cache-Control", longCache ? "no-cache" : "no-cache");
                ApplySecurityHeaders(resp, req);
                return;
            }

            byte[] data = File.ReadAllBytes(file);
            bool compressible = contentType != null &&
                                (contentType.IndexOf("text/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 contentType.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 contentType.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0);
            bool gzip = compressible && data.Length >= 1024 &&
                        (req.Header("Accept-Encoding") ?? "").IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;

            resp.Status = status;
            resp.ContentType = contentType;
            resp.Set("ETag", etag);
            resp.Set("Last-Modified", lastWrite.ToString("R", CultureInfo.InvariantCulture));
            // assets revalidate every time (cheap 304 thanks to the ETag), so a deploy
            // never leaves stale JavaScript in a browser cache.
            resp.Set("Cache-Control", longCache ? "no-cache, must-revalidate" : "no-cache, must-revalidate");

            if (gzip)
            {
                resp.Body = Gzip(data);
                resp.Set("Content-Encoding", "gzip");
                resp.Set("Vary", "Accept-Encoding");
            }
            else
            {
                resp.Body = data;
            }
            ApplySecurityHeaders(resp, req);
        }

        private static bool CheckNotModified(HttpRequest req, string etag, DateTime lastWriteUtc)
        {
            string inm = req.Header("If-None-Match");
            if (!string.IsNullOrEmpty(inm))
            {
                if (inm.Trim() == "*") return true;
                string[] candidates = inm.Split(',');
                string wanted = NormalizeEtag(etag);
                for (int i = 0; i < candidates.Length; i++)
                    if (NormalizeEtag(candidates[i]) == wanted) return true;
            }
            string ims = req.Header("If-Modified-Since");
            if (!string.IsNullOrEmpty(ims))
            {
                DateTime since;
                if (DateTime.TryParse(ims, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out since))
                {
                    if (lastWriteUtc.AddSeconds(-1) <= since) return true;
                }
            }
            return false;
        }

        private static string NormalizeEtag(string value)
        {
            if (value == null) return "";
            string v = value.Trim();
            if (v.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) v = v.Substring(2).Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            return v;
        }

        // ============================================================= output

        public void ApplySecurityHeaders(HttpResponse resp, HttpRequest req)
        {
            if (resp == null) return;
            resp.Set("X-Content-Type-Options", "nosniff");
            resp.Set("X-Frame-Options", "DENY");
            resp.Set("Referrer-Policy", "strict-origin-when-cross-origin");
            resp.Set("Cross-Origin-Resource-Policy", "same-origin");
            resp.Set("Permissions-Policy", "camera=(), microphone=(self), geolocation=(), payment=(), usb=()");
            resp.Set("Content-Security-Policy",
                "default-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; " +
                "object-src 'none'; " +
                (_cfg.ExternalMedia
                    ? "img-src 'self' data: blob: https:; media-src 'self' https:; "
                    : "img-src 'self' data: blob:; media-src 'self'; ") +
                "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
                "font-src 'self' https://cdnjs.cloudflare.com data:; " +
                "script-src 'self'; connect-src 'self'");
            if (_cfg.Hsts && req != null && req.IsHttps)
                resp.Set("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        }

        public void SendText(HttpRequest req, HttpResponse resp, int status, string text)
        {
            SendBytes(req, resp, status, Encoding.UTF8.GetBytes(text ?? ""), "text/plain; charset=utf-8", false, false, null);
        }

        public void SendJson(HttpRequest req, HttpResponse resp, int status, string json)
        {
            SendBytes(req, resp, status, Encoding.UTF8.GetBytes(json ?? ""), "application/json; charset=utf-8", true, false, null);
        }

        public void SendBytes(HttpRequest req, HttpResponse resp, int status, byte[] data, string contentType,
                              bool gzipAllowed, bool longCache, string etag)
        {
            if (data == null) data = new byte[0];
            resp.Status = status;
            resp.ContentType = contentType;
            resp.Set("Cache-Control", longCache ? "public, max-age=3600" : "no-store, no-cache, must-revalidate");
            if (!string.IsNullOrEmpty(etag)) resp.Set("ETag", etag);

            bool gzip = gzipAllowed && data.Length >= 1024 &&
                        (req.Header("Accept-Encoding") ?? "").IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;
            if (gzip)
            {
                resp.Body = Gzip(data);
                resp.Set("Content-Encoding", "gzip");
                resp.Set("Vary", "Accept-Encoding");
            }
            else
            {
                resp.Body = data;
            }
            ApplySecurityHeaders(resp, req);
        }

        public static byte[] Gzip(byte[] input)
        {
            using (MemoryStream outStream = new MemoryStream(input.Length / 2 + 64))
            {
                using (GZipStream gz = new GZipStream(outStream, CompressionMode.Compress, true))
                {
                    gz.Write(input, 0, input.Length);
                }
                return outStream.ToArray();
            }
        }

        public static byte[] Gunzip(byte[] input)
        {
            using (MemoryStream inputStream = new MemoryStream(input))
            using (GZipStream gz = new GZipStream(inputStream, CompressionMode.Decompress))
            using (MemoryStream outStream = new MemoryStream(input.Length * 3 + 64))
            {
                byte[] buffer = new byte[8192];
                int n;
                while ((n = gz.Read(buffer, 0, buffer.Length)) > 0) outStream.Write(buffer, 0, n);
                return outStream.ToArray();
            }
        }

        // ============================================================== ip/rate

        public string EffectiveClientIp(HttpRequest req)
        {
            string peer = req.RemoteIp ?? "0.0.0.0";
            if (!_cfg.TrustProxyHeaders) return peer;
            if (!IsTrustedProxy(peer)) return peer;

            string real = req.Header("X-Real-IP");
            if (!string.IsNullOrEmpty(real)) return real.Trim();

            string fwd = req.Header("X-Forwarded-For");
            if (!string.IsNullOrEmpty(fwd))
            {
                int comma = fwd.IndexOf(',');
                string first = comma < 0 ? fwd : fwd.Substring(0, comma);
                first = first.Trim();
                if (first.Length > 0) return first;
            }
            return peer;
        }

        public bool IsHttps(HttpRequest req)
        {
            if (req == null) return false;
            if (_cfg.TrustProxyHeaders && IsTrustedProxy(req.RemoteIp))
            {
                string proto = req.Header("X-Forwarded-Proto");
                if (!string.IsNullOrEmpty(proto) && proto.Trim().Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
                string ssl = req.Header("X-Forwarded-Ssl");
                if (!string.IsNullOrEmpty(ssl) && ssl.Trim().Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool CookieSecure(HttpRequest req)
        {
            if (_cfg.CookieSecure.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (_cfg.CookieSecure.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return IsHttps(req);
        }

        public bool IsTrustedProxy(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            string[] parts = (_cfg.TrustedProxies ?? "").Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p.IndexOf('/') >= 0)
                {
                    if (Cidr.Contains(p, ip)) return true;
                }
                else if (p.Equals(ip, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool RateAllowed(string ip)
        {
            int limit = _cfg.RequestsPerMinute;
            if (limit <= 0) return true;
            DateTime now = DateTime.UtcNow;
            lock (_rateLock)
            {
                RateWindow w;
                if (!_rate.TryGetValue(ip, out w) || (now - w.StartUtc).TotalSeconds >= 60)
                {
                    w = new RateWindow();
                    w.StartUtc = now;
                    w.Count = 0;
                    _rate[ip] = w;
                }
                w.Count++;
                if (_rate.Count > 4000 && w.Count == 1) PruneRateLocked(now);
                return w.Count <= limit;
            }
        }

        private void PruneRateLocked(DateTime now)
        {
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, RateWindow> kv in _rate)
                if ((now - kv.Value.StartUtc).TotalSeconds > 180) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _rate.Remove(dead[i]);
        }

        public bool AuthBlocked(string ip)
        {
            lock (_rateLock)
            {
                FailWindow w;
                if (!_authFails.TryGetValue(ip, out w)) return false;
                return w.BlockedUntilUtc > DateTime.UtcNow;
            }
        }

        public int AuthFailureCount(string ip)
        {
            lock (_rateLock)
            {
                FailWindow w;
                return _authFails.TryGetValue(ip, out w) ? w.Count : 0;
            }
        }

        public void RegisterAuthFailure(string ip)
        {
            DateTime now = DateTime.UtcNow;
            lock (_rateLock)
            {
                FailWindow w;
                if (!_authFails.TryGetValue(ip, out w) || (now - w.StartUtc).TotalSeconds > 300)
                {
                    w = new FailWindow();
                    w.StartUtc = now;
                    w.Count = 0;
                    _authFails[ip] = w;
                }
                w.Count++;
                if (w.Count >= _cfg.MaxFailedLoginsPerIp)
                {
                    w.BlockedUntilUtc = now.AddSeconds(_cfg.LoginLockSeconds);
                    w.Count = 0;
                    w.StartUtc = now;
                    Log.Warn("SEC", "auth failures from " + ip + ": blocked for " + _cfg.LoginLockSeconds + "s");
                }
            }
        }

        public void ClearAuthFailures(string ip)
        {
            lock (_rateLock) { _authFails.Remove(ip); }
        }

        public void BanIp(string ip, int seconds)
        {
            if (string.IsNullOrEmpty(ip) || seconds <= 0) return;
            lock (_rateLock)
            {
                _bannedIps[ip] = DateTime.UtcNow.AddSeconds(seconds);
                if (_bannedIps.Count > 5000) PruneBansLocked();
            }
            Log.Warn("SEC", "IP " + ip + " banned for " + seconds + "s");
        }

        public bool IsBanned(string ip)
        {
            lock (_rateLock)
            {
                DateTime until;
                if (!_bannedIps.TryGetValue(ip, out until)) return false;
                if (until <= DateTime.UtcNow) { _bannedIps.Remove(ip); return false; }
                return true;
            }
        }

        private void PruneBansLocked()
        {
            DateTime now = DateTime.UtcNow;
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in _bannedIps)
                if (kv.Value <= now) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _bannedIps.Remove(dead[i]);
        }

        public string StatusLine()
        {
            return RoleName + " http://" + _bind + ":" + _port.ToString(CultureInfo.InvariantCulture) + _basePath + "/" +
                   " | requests " + Interlocked.Read(ref _requests).ToString(CultureInfo.InvariantCulture) +
                   " | errors " + Interlocked.Read(ref _errors).ToString(CultureInfo.InvariantCulture) +
                   " | inflight " + Inflight;
        }

        private sealed class RateWindow
        {
            public DateTime StartUtc;
            public int Count;
        }

        private sealed class FailWindow
        {
            public DateTime StartUtc;
            public int Count;
            public DateTime BlockedUntilUtc;
        }
    }

    /// <summary>Small CIDR matcher for IPv4/IPv6 prefixes in trustedProxies.</summary>
    internal static class Cidr
    {
        public static bool Contains(string cidr, string ip)
        {
            try
            {
                int slash = cidr.IndexOf('/');
                if (slash <= 0) return false;
                IPAddress network;
                if (!IPAddress.TryParse(cidr.Substring(0, slash), out network)) return false;
                IPAddress addr;
                if (!IPAddress.TryParse(ip, out addr)) return false;

                byte[] nb = network.GetAddressBytes();
                byte[] ab = addr.GetAddressBytes();
                if (nb.Length != ab.Length) return false;

                int bits;
                if (!int.TryParse(cidr.Substring(slash + 1), out bits)) return false;
                if (bits < 0 || bits > nb.Length * 8) return false;

                int full = bits / 8;
                int rem = bits % 8;
                for (int i = 0; i < full; i++) if (nb[i] != ab[i]) return false;
                if (rem > 0)
                {
                    int mask = 0xFF << (8 - rem);
                    if ((nb[full] & mask) != (ab[full] & mask)) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}

