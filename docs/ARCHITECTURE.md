# Architecture

```
                        ┌──────────────────────────────────────────┐
   browser (SPA)  ────► │  WebServer "public"  (port 55150)        │
   public/assets/js     │   ├─ static  (public/, ETag + gzip)      │
                        │   ├─ media   (/media/avatars|posts)      │
                        │   └─ /api/   → ApiRouter (signed+sealed) │
                        │        └─ /api/admin/… → 404 (absent)    │
                        └───────────────┬──────────────────────────┘
                                        │
                        ┌───────────────┴──────────────────────────┐
   operator ──────────► │  WebServer "admin"   (port 55160)        │
                        │   └─ /api/ incl. /api/admin/…            │
                        └───────────────┬──────────────────────────┘
                                        │
              ┌─────────────────────────┼──────────────────────────┐
              │                         │                          │
      SocialStore (in-memory)     SessionManager            StatsStore / CaptchaStore
      JSON tables + atomic IO     session key = PBKDF2       lifetime counters, SVG codes
              │
      data/*.json  (15 tables + meta + stats)
```

Both listeners share one `SocialStore`, one `SessionManager`, one stats store and
one captcha store, so a session opened on the public board is visible on the admin
board too — but a cookie set for `host:55150` is not sent to `host:55160` by the
browser, so the operator signs in once per board. That is deliberate: the admin
board never trusts a cookie minted by a public-facing port.

## Process model

One `SocialServer.exe`:

| Thread | Responsibility |
| --- | --- |
| main | config, startup, waits for shutdown event |
| `web-public` | accept loop on `server.port` (fallbacks on `SocketException`) |
| `web-admin` | accept loop on `server.adminPort`; `/api/admin/` exists only here |
| thread pool | one work item per HTTP connection (keep-alive up to `keepAliveMax`) |
| `supervisor` | 2 s tick: watchdog, debounced saves, session/captcha expiry, snapshots, health log |
| `console` | optional TUI: ASCII banner, status line, hotkeys. **Never** prints log lines |

Logs go through `Trace` to exactly one listener that owns its `StreamWriter`
(`logs/social-YYYYMMDD.log`, size and day rollover, retention). `Trace.Listeners`
is cleared once in `Log.Configure`; nothing is written to the console from
inside `Trace`, which avoids the classic "double output / dead listener" bug.

## Storage

No database server, no SQLite, no NuGet. Fifteen small JSON tables plus
`meta.json` (id counters, install id, last backup) and `stats.json` (visits). Guarantees:

* **atomic replace** — write `*.tmp`, `File.Replace` (or move dance), keep `.bak`
* **self-healing load** — main file → `.bak` → quarantine `*.bad-*` and start empty
* **debounced save** — supervisor flushes at most every `storage.saveDebounceMs`
* **degraded mode** — if a write fails, the process keeps serving reads and
  switches to read-only until a save succeeds (or Admin → Repair)
* **snapshots** — `data/backups/YYYYMMDD-HHMMSS/` every `backupIntervalMin`,
  pruned to `backupKeep`

All mutations happen under one lock (`SocialStore.Sync`); reads that touch several
tables use the same lock, so no torn views. Content is kept newest-first in memory
to make feeds cheap.

Live calls are the one thing that is *not* persisted: `CallRegistry` keeps them in
memory together with a small per-user event queue, because a call only exists while
the browsers are connected. Only the finished record (who, when, how long) lands in
`calls.json` for the history list.

Flash games are media rows with `kind=game`; the `.swf` sits in `data/media/games/`.
The player itself (Ruffle, ~28 MB of wasm) is deliberately *not* in git: it is
fetched by `tools\fetch-ruffle.ps1` into `public/assets/vendor/ruffle/`, served from
our own origin, and only ever started inside the sandboxed `/play/<id>` iframe.

Communities extend the group row rather than adding a table: `editors` and
`requests` are id lists, `postPolicy` is a single string, and a queued post is just
`pending: true` on the post itself. That keeps the storage flat and means an old
`groups.json` from before this feature loads unchanged (`postPolicy` defaults to
`all`, the lists to empty). Visibility of a community post is decided by the
community, not by the post's own `visibility` flag - see `SocialStore.VisibleTo`.

## Request pipeline

1. `HttpConnection` parses the request (limits: 8 KB request line, 64 KB headers,
   `security.maxBodyBytes` body, chunked supported, `Expect: 100-continue`).
2. `WebServer` applies: IP ban list → per-IP rate window → route. The role decides
   the surface: `WebRole.Public` refuses `/api/admin/…` with 404, `WebRole.Admin`
   serves it (and can enforce `security.adminPrivateOnly`).
3. `/api/…` → `ApiRouter`:
   * pre-auth routes (`handshake`, `health`, `register`, `login`, `recover`) use a
     key derived from the user's verifier, so even a wrong password learns nothing;
   * everything else requires the session cookie → signature → freshness → nonce →
     optional body decryption, then `Dispatch`.
4. Static files: path traversal guard (`..`, NUL, root prefix check), ETag/304,
   gzip for text assets, `no-cache` so a deploy never leaves stale JS.
5. Media: only raster formats (sniffed magic bytes), random names, `nosniff`.

## Views and permissions

`ApiRouter.Views.cs` holds every JSON shape (`UserSummary`, `UserView`, `PostView`,
…) and the permission helpers (`CanViewProfile`, `CanViewPost`, `CanDeletePost`,
`CanMessageUser`). Privacy is enforced server-side: a private profile returns
fields as empty strings, not as missing keys, so the SPA stays simple.

## Fault tolerance ("dynamic resilience")

| Failure | Reaction |
| --- | --- |
| port busy | next candidate from `portFallbacks` / `adminPortFallbacks`, logged |
| accept loop dies | `Failed` event → supervisor restarts that board with 1,2,4,…30 s backoff |
| admin board cannot start | the public site keeps running; the console and log say so, the supervisor retries |
| request handler throws | 500 JSON, error logged, connection closed, server keeps running |
| disk write fails | read-only mode, banner in Admin, retry on next save |
| corrupt JSON | `.bak` restore, quarantine, empty table, warning in log |
| unhandled exception | crash log in `logs/crash-*.log`, graceful shutdown, data flushed |
| slow/naughty client | receive/send timeouts, max concurrent requests (503), rate limit (429) |

## Frontend

Plain ES5, no framework, no build step. Files are loaded in order:

| File | Role |
| --- | --- |
| `crypto.js` | SHA-256, HMAC-SHA256, PBKDF2, HMAC-CTR seal/open, request signing |
| `i18n.js` | dictionaries (ru/en/uk/lt), `t()`, detection, `data-i18n` |
| `sound.js` | WebAudio presets, per-event toggles, volume, persistence |
| `ui.js` | DOM helpers, escaping, toasts, modals, themes, file→dataURL |
| `api.js` | transport: handshake, login/register/recover, signed+sealed calls |
| `app.js` | shell, router, feed, post, profile, composer, comment/like logic |
| `app.more.js` | friends, people, messages, groups, notifications, settings, admin, search |

Routing is hash based (`#/feed`, `#/u/name`, `#/post/12`, `#/admin/logs`), so the
server only ever serves `index.html` and assets — no SPA fallback rules needed in
the reverse proxy.

Rendering uses string templates with `UI.esc()` / `UI.attr()` for every value that
came from a user; post bodies additionally go through `UI.linkify()` which escapes
first and only then turns `http(s)://` into links. Images are restricted to
`UI.safeUrl()` (same-origin paths or http/https).

## Extension points

* **new endpoint** — add a branch in `ApiRouter.Dispatch`, a handler in
  `ApiRouter.Social.cs`, and a view helper in `ApiRouter.Views.cs`.
* **new table** — add a list + `ToArray`/`FromDict` pair in `SocialStore` and a
  `LoadTable` call in `Load`.
* **new language** — one object in `i18n.js` plus a code in `Config.Validate`.
* **new theme** — a `html[data-theme="x"]` block in `app.css` and the name in
  `ui.themes` / `Config.Themes()`.
