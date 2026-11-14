using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // =============================================================== games

        /// <summary>
        /// Ruffle is vendored, not bundled: <c>tools\fetch-ruffle.ps1</c> drops
        /// ~28 MB of wasm into <c>public/assets/vendor/ruffle</c>. Without it the
        /// shelf would render an empty frame, so the whole feature turns itself
        /// off and the admin board says why.
        /// </summary>
        public bool RufflePresent()
        {
            try
            {
                string dir = Path.Combine(_cfg.WebRoot, "assets", "vendor", "ruffle");
                return File.Exists(Path.Combine(dir, "ruffle.js"));
            }
            catch { return false; }
        }

        public bool GamesAvailable()
        {
            return _cfg.GamesEnabled && RufflePresent();
        }

        private string GameJson(MediaItem m, User viewer)
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", m.Id);
            b.Prop("kind", "game");
            b.Prop("title", m.Title);
            b.Prop("description", m.Description);
            b.Prop("url", m.Url);
            b.Prop("size", m.Size);
            b.Prop("plays", m.Plays);
            b.Prop("likes", m.Likes.Count);
            b.Prop("liked", viewer != null && m.Likes.Contains(viewer.Id));
            b.Prop("created", Util.Iso(m.Created));
            b.Prop("mine", viewer != null && m.Owner == viewer.Id);
            b.Name("owner").Raw(UserSummary(_store.FindUser(m.Owner)));
            b.EndObject();
            return b.ToString();
        }

        private MediaItem RequireGame(long id)
        {
            MediaItem m = _store.FindMedia(id);
            if (m == null || m.Kind != "game") throw new ApiException(404, "no-game", "Game not found");
            return m;
        }

        private void HandleGames(ApiCtx ctx)
        {
            if (!GamesAvailable()) throw new ApiException(403, "games-off", "Games are switched off here");

            long owner = LongQuery(ctx, "user");
            string query = GetQuery(ctx.Query, "q");
            long before = LongQuery(ctx, "before");
            int limit = LimitParam(ctx, 24, 60);

            bool hasMore;
            List<MediaItem> games = _store.Games(owner, query, before, limit, out hasMore);

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("games").BeginArray();
            for (int i = 0; i < games.Count; i++) b.Raw(GameJson(games[i], ctx.User));
            b.EndArray();
            b.Prop("total", _store.CountGames());
            b.Prop("hasMore", hasMore);
            b.Prop("canUpload", ctx.User.IsAdmin || ctx.User.CanGames);
            b.Prop("next", games.Count > 0 ? games[games.Count - 1].Id : 0);
            SendOk(ctx, b);
        }

        private void HandleGame(ApiCtx ctx)
        {
            if (!GamesAvailable()) throw new ApiException(403, "games-off", "Games are switched off here");
            MediaItem m = RequireGame(LongQuery(ctx, "id"));

            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Name("game").Raw(GameJson(m, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGameUpload(ApiCtx ctx)
        {
            if (!GamesAvailable()) throw new ApiException(403, "games-off", "Games are switched off here");
            // uploading a .swf is a privilege granted in the admin board
            if (!ctx.User.IsAdmin && !ctx.User.CanGames)
                throw new ApiException(403, "no-permission",
                    "Only approved authors may upload games. Ask an administrator for permission.");

            int limit = _cfg.MaxGameFiles;
            if (limit > 0 && _store.CountMedia(ctx.User.Id, "game") >= limit)
                throw new ApiException(400, "too-many-files", "Your games shelf is full (" + limit + " files)");

            string data = BodyStr(ctx, "data", "");
            if (data.Length == 0) throw new ApiException(400, "bad-file", "The file is empty");
            if (data.Length > _cfg.MaxGameKB * 1024 * 4 / 3 + 1024)
                throw new ApiException(400, "too-large", "File is larger than " + _cfg.MaxGameKB + " KB");

            string err;
            string url = _media.SaveGame(data, _cfg.MaxGameKB, out err);
            if (url == null)
            {
                if (err == "file-too-large")
                    throw new ApiException(400, "too-large", "File is larger than " + _cfg.MaxGameKB + " KB");
                if (err == "unsupported-game")
                    throw new ApiException(400, "bad-swf", "That file is not a Flash movie (.swf)");
                throw new ApiException(400, err, "File could not be saved");
            }

            string mime;
            byte[] raw = Util.ParseDataUrl(data, out mime);
            long size = raw == null ? 0 : raw.Length;

            string title = Util.SingleLine(BodyStr(ctx, "title", ""), 120);
            if (title.Length == 0) title = "Untitled game";
            string description = Util.CleanText(BodyStr(ctx, "description", ""), 1000);

            MediaItem item = _store.AddGame(ctx.User.Id, title, description, url, size);
            Log.Info("GAME", "@" + ctx.User.Username + " uploaded game #" + item.Id + " (" + size + " bytes)");

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("game").Raw(GameJson(item, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGameEdit(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            MediaItem m = RequireGame(id);
            if (m.Owner != ctx.User.Id) throw new ApiException(403, "not-mine", "That is not your game");

            _store.UpdateGame(id, ctx.User.Id, BodyStr(ctx, "title", m.Title), BodyStr(ctx, "description", m.Description));

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("game").Raw(GameJson(m, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGameDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            MediaItem m = RequireGame(id);
            if (m.Owner != ctx.User.Id && !ctx.User.IsModerator)
                throw new ApiException(403, "not-mine", "That is not your game");

            string url = _store.RemoveMedia(id, m.Owner);
            if (!string.IsNullOrEmpty(url) && !_store.IsMediaUrlUsed(url)) _media.Delete(url);
            Log.Info("GAME", "@" + ctx.User.Username + " deleted game #" + id);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("id", id);
            SendOk(ctx, b);
        }

        private void HandleGamePlay(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            int plays = _store.AddGamePlay(id);
            if (plays < 0) throw new ApiException(404, "no-game", "Game not found");

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("plays", plays);
            SendOk(ctx, b);
        }

        private void HandleGameLike(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            bool liked;
            int count = _store.ToggleGameLike(id, ctx.User.Id, out liked);
            if (count < 0) throw new ApiException(404, "no-game", "Game not found");

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("likes", count);
            b.Prop("liked", liked);
            SendOk(ctx, b);
        }

        // ------------------------------------------------------------ player

        /// <summary>
        /// Serves <c>/play/&lt;id&gt;</c>: a tiny standalone page that hosts Ruffle.
        /// The SPA embeds it in <c>&lt;iframe sandbox="allow-scripts"&gt;</c>, so the
        /// Flash movie runs on an opaque origin with no access to the session, the
        /// cookies or the parent document - even if it calls out through
        /// ExternalInterface. That is also why the CSP below names the origin
        /// explicitly instead of relying on 'self', which never matches an opaque
        /// origin.
        /// </summary>
        public bool ServePlayer(HttpRequest req, HttpResponse resp, string path)
        {
            if (!path.StartsWith("/play/", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(req.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                _web.SendJson(req, resp, 405, "{\"ok\":false,\"error\":\"method-not-allowed\"}");
                return true;
            }

            string raw = path.Substring("/play/".Length);
            long id;
            if (!long.TryParse(raw, out id)) id = 0;

            MediaItem game = id > 0 ? _store.FindMedia(id) : null;
            if (game == null || game.Kind != "game" || !GamesAvailable())
            {
                resp.Set("Content-Security-Policy", "default-src 'none'; frame-ancestors 'self'");
                _web.SendText(req, resp, 404, "Game not found");
                return true;
            }

            string origin = OriginOf(req);
            string csp = "default-src 'none'; base-uri 'none'; form-action 'none'; " +
                         "frame-ancestors 'self'; " +
                         "script-src " + origin + " 'wasm-unsafe-eval'; " +
                         "connect-src " + origin + "; " +
                         "img-src " + origin + " data: blob:; " +
                         "media-src " + origin + " blob:; " +
                         "font-src " + origin + " data:; " +
                         "worker-src " + origin + " blob:; " +
                         "style-src 'unsafe-inline'";
            resp.Set("Content-Security-Policy", csp);
            resp.Set("Referrer-Policy", "no-referrer");
            resp.Set("X-Robots-Tag", "noindex");
            // the SPA frames this page; the iframe is sandboxed, so its origin is
            // opaque and a "same-origin" resource policy would refuse to render it
            resp.Set("X-Frame-Options", "SAMEORIGIN");
            resp.Set("Cross-Origin-Resource-Policy", "cross-origin");

            string html = PlayerHtml(game);
            _web.SendBytes(req, resp, 200, Encoding.UTF8.GetBytes(html),
                           "text/html; charset=utf-8", true, false, null);
            return true;
        }

        /// <summary>
        /// "http://host:port" for the CSP, taken from the request. Only host
        /// characters are allowed through; anything else falls back to 'self'.
        /// </summary>
        private static string OriginOf(HttpRequest req)
        {
            string host = req.Header("Host") ?? "";
            if (host.Length == 0 || host.Length > 128) return "'self'";
            for (int i = 0; i < host.Length; i++)
            {
                char c = host[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '.' || c == '-' || c == ':' || c == '[' || c == ']';
                if (!ok) return "'self'";
            }
            return (req.IsHttps ? "https://" : "http://") + host;
        }

        private string PlayerHtml(MediaItem game)
        {
            string basePath = _cfg.PublicBasePath ?? "";
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>").Append(Html(game.Title)).Append("</title>");
            sb.Append("<style>");
            sb.Append("html,body{margin:0;height:100%;background:#0b1119;overflow:hidden}");
            sb.Append("#stage{position:absolute;inset:0;display:flex;align-items:center;justify-content:center}");
            sb.Append("#stage ruffle-player{display:block;width:100%;height:100%}");
            sb.Append("#msg{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;");
            sb.Append("color:#93a4b8;font:13px/1.5 system-ui,-apple-system,Segoe UI,sans-serif;text-align:center;padding:24px}");
            sb.Append("</style></head><body>");
            sb.Append("<div id=\"stage\" data-swf=\"").Append(Html(basePath + game.Url)).Append("\" data-base=\"")
              .Append(Html(basePath + "/assets/vendor/ruffle/")).Append("\"></div>");
            sb.Append("<div id=\"msg\">Ruffle...</div>");
            sb.Append("<script src=\"").Append(basePath).Append("/assets/vendor/ruffle/ruffle.js\"></script>");
            sb.Append("<script src=\"").Append(basePath).Append("/assets/js/player.js\"></script>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string Html(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
