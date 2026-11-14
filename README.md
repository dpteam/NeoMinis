# Social Server

A small, fast, **self-hosted social network** in a single `.exe`.
.NET Framework 4.0 / C# 4, BCL only, no NuGet, no MSBuild, no database server —
runs on Windows XP SP3 and anything newer, including behind Nginx Proxy Manager.

Everything is branding-neutral: rename the site, change ports, languages and
limits from `config.ini` and ship your own network.

---

## Features

| Area | What you get |
| --- | --- |
| **Feed** | home / everything / friends-only tabs, posts with images, video links (YouTube previews, inline mp4/webm) and polls, likes (with a "who liked" list), emoji reactions, comments, **reposts to your own wall**, edit and pin your own posts, **drafts and scheduled publication**, visibility (`public` / `friends` / `private`), delete with cascade |
| **Profiles** | avatar, status, bio, city/country, birthday, gender, website, interests (games / films / music / books), mood, achievements, photo gallery, privacy level, friends wall |
| **Friends** | requests, accept/decline, remove, block, incoming/outgoing lists |
| **Messages** | private conversations with live delivery (long poll), typing indicator, read receipts with "read up to", unread divider, replies with quotes, emoji reactions, forwarding, message search, picture/GIF attachments, **photo albums**, **voice messages**, **file attachments**, inline image and video links, emoji picker, Enter to send, per-side delete, thread list with unread counters |
| **Groups** | public/private groups, membership, group feed, owner role, editable name/description/picture and member management (kick) |
| **Notifications** | likes, comments, reactions, reposts, friend requests, messages, mentions, badges, admin broadcasts, reports — delivered **live** over a long poll (instant toast, sound, tab title counter, optional desktop notification), with per-type switches in Settings and All / Unread / Mentions filters |
| **Search** | people and posts, plus a top-bar quick search; the post search has filters (type: picture / video / poll / text, author, tag, dates) and sorting (newest / oldest / popular) |
| **Media library** | personal music and video shelves with upload, inline players, "save to my library" from somebody else's shelf, playlists, and size/count limits from `config.ini` / Admin → Settings |
| **Feedback** | anonymous "message the developers" form, reachable from the login screen and the sidebar; lands in an admin tab with kinds (bug / idea / other), statuses, internal notes, honeypot and per-IP rate limit |
| **Roadmap** | planned features (Flash games through Ruffle, playlists, gifts, stories…) live in [docs/ROADMAP.md](docs/ROADMAP.md) |
| **Tags** | `#hashtags` become tag pages, trending tags in the right column |
| **Mentions** | `@username` with autocomplete in the composer, links in posts and a notification for every mentioned person |
| **Bookmarks** | toggle "save" on any post, a dedicated list with paging |
| **Achievements** | 13 badges, auto-granted from activity (posts, comments, likes, friends, messages, bookmarks, age) plus manual awards; admin can grant/revoke any badge |
| **AI citizens** | imported personas that post / comment / like on their own — Markov chain or any OpenAI-compatible endpoint (LM Studio, Ollama, OpenAI, OpenRouter). Off by default. See [docs/AI.md](docs/AI.md) |
| **Admin** | **separate board on its own port** — dashboard, users (ban / unban / grant or revoke admin & moderator / warn / reset / badges / delete), reports queue, live log tail, broadcast, backup, storage repair, config reload, AI panel, and a Settings tab that writes straight back to `config.ini` |
| **Themes** | `light` / `dark` / `oled`, instant switch, remembered per user |
| **Sounds** | WebAudio synth (no audio files): click, post, like, comment, message, notification, friend, login, error, success, theme — with per-event toggles and volume |
| **i18n** | Russian, English, Ukrainian, Lithuanian; auto-detect + selector |
| **Security** | PBKDF2-HMAC-SHA256 logins, HMAC-signed and encrypted `/api/`, single-use SVG captcha, rate limits, bans, CSP/XFO/nosniff |
| **Resilience** | supervisor with exponential backoff, port fallbacks, atomic JSON writes, `.bak` recovery, read-only degraded mode, snapshots |
| **Logging** | `Trace` with one rolling file listener in `logs/` (`social-YYYYMMDD.log`), levels, retention — no `App.config`, no console duplication |

---

## Quick start

```cmd
build.cmd        :: compiles with the stock csc.exe, copies web assets to bin\
run.cmd          :: starts the server (bin\config.ini is created on first run)
```

Then open <http://localhost:55150/>. **The first registered account becomes an
administrator** (`site.firstUserAdmin=true`).

The **admin board** runs on its own port in the same process — by default
<http://localhost:55160/>. `/api/admin/` does not exist on the public port at all
(it answers 404), so the admin surface is never reachable through the reverse
proxy unless you deliberately publish it.

Console is a full operator CLI: type `help` at the `social>` prompt, or run one
command offline with `SocialServer.exe --cmd "user role 9 admin"`.
See **[docs/CLI.md](docs/CLI.md)**.

### Two boards, one process

| Board | Default port | Contents |
| --- | --- | --- |
| public | `55150` (+ fallbacks) | SPA, `/api/…` for normal users, `/media/…` |
| admin | `55160` (+ fallbacks) | the same SPA plus `/api/admin/…` |

Both are configurable (`server.port` / `server.adminPort`, separate `bind`s and
fallback lists), and the admin board can be switched off entirely with
`server.adminEnabled=false` or `--no-admin`.

### Windows Firewall

```cmd
tools\firewall.cmd              :: LAN / private sources (default, for a reverse proxy)
tools\firewall.cmd -PublicAny   :: allow the ports from anywhere (direct expose)
tools\firewall.cmd -Remove      :: delete the "Social Server" rules
```

The script reads both port lists from `bin\config.ini` (public and admin,
including fallbacks), asks for UAC and scopes rules to `SocialServer.exe`.

### Behind Nginx Proxy Manager

1. Point NPM at `http://<host>:55150`.
2. Keep `server.trustProxyHeaders=true`; the NPM host must be inside
   `server.trustedProxies` (private ranges are listed by default).
3. `cookieSecure=auto` sets the Secure flag when NPM sends `X-Forwarded-Proto: https`.
4. Set `site.baseUrl=https://your.domain` so links and the invite box are correct.
5. Optional sub-path: `server.publicBasePath=/social`.
6. Enable `security.hsts=true` only when TLS really terminates in front.
7. Do **not** publish the admin port through NPM unless you want it on the internet;
   if you do, give it its own hostname (e.g. `admin.your.domain`) and keep
   `security.adminPrivateOnly=false` only while you really need remote access.

No `netsh http add urlacl` is needed: HTTP/1.1 is implemented on top of
`TcpListener`, which also keeps it working on XP.

---

## Layout

```
build.cmd                stock csc.exe build, no NuGet/MSBuild
run.cmd                  start from bin\
config.example.ini       every setting, documented
src/SocialServer/
  Program.cs             CLI, crash handler, --selftest / --backup / --import-deadvk
  SocialApp.cs           owns store, sessions, stats, captcha, media, AI, web
  AppSupervisor.cs       watchdog + debounced saves + snapshots + cleanup
  ConsoleUi.cs           TUI: ASCII banner, operator prompt, no log echo
  SelfTest.cs            crypto vectors, envelope tampering, JSON, captcha, storage
  Core/                  Config, Log, Crypto, Json, Models, SocialStore, Sessions,
                         Captcha, Media, StatsStore, Store (atomic IO), Util,
                         Branding, Ai + AiRunner, DeadVkImport, Cli
  Web/                   HttpCore (HTTP/1.1), WebServer (static/media/routing),
                         ApiRouter (auth, views, social, admin, ai)
public/                  SPA: index.html, 404/403, assets/css, assets/js
tools/                   firewall.ps1/.cmd, test-web.mjs, ci-test.ps1, rename-user.mjs
docs/                    ARCHITECTURE, API, SECURITY, I18N, AI, IMPORT, CLI
```

Runtime folders (git-ignored): `bin/`, `data/`, `logs/`, `config.ini`.

---

## Configuration

All settings live in `config.ini` (UTF-8, INI). `config.example.ini` documents
every key; the important ones:

```ini
[site]
name=Social
shortName=S
tagline=Your own social network
defaultLanguage=ru          ; ru | en | uk | lt
defaultTheme=light          ; light | dark | oled
registration=true
inviteOnly=false
firstUserAdmin=true

[server]
port=55150
portFallbacks=55151,55152,55153
bind=0.0.0.0

; admin board: own listener, own port, /api/admin/ lives only here
adminEnabled=true
adminPort=55160
adminPortFallbacks=55161,55162,55163
adminBind=0.0.0.0
publicBasePath=             ; e.g. /social behind a proxy
trustProxyHeaders=true
trustedProxies=127.0.0.1,::1,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16
cookieSecure=auto
sessionHours=168
sessionMaxHours=720

[security]
serverSecret=               ; generated on first run
cookieSalt=                 ; generated on first run
kdfIterations=12000
encryptApi=true
requireCaptcha=true
; admin board: refuse non-private client addresses
adminPrivateOnly=false

[limits]
requestsPerMinute=900
registrationsPerHour=20
postsPer10Min=20
messagesPerMin=40
maxPostLength=8000
maxAvatarKB=512
maxPostImageKB=4096

[storage]
backupIntervalMin=30
backupKeep=24
readOnlyOnError=true

[logging]
level=Info                  ; Error | Warn | Info | Debug | Verbose
apiTrace=true
maxFileMB=8
retentionDays=14

[ai]
provider=off                ; off | markov | openai
enabled=false
; any OpenAI-compatible /chat/completions endpoint
endpoint=http://localhost:1234/v1/chat/completions
model=local-model
intervalSec=180
```

Changing `port`, `bind`, `adminPort`, `webRoot` or `dataDir` needs a restart;
everything else can be re-read with `R` in the console or `POST /api/admin/reload`.

CLI overrides: `--port N`, `--admin-port N`, `--no-admin`, `--bind IP`,
`--config PATH`, `--data DIR`, `--no-console`.

---

## API

Passwords and tokens never travel over the wire, and no plaintext body does either.
Details: **[docs/API.md](docs/API.md)**, **[docs/SECURITY.md](docs/SECURITY.md)**.

```text
verifier = PBKDF2-SHA256(password, "social.login.v1|username", N, 32)
K        = PBKDF2-SHA256(verifier, sessionSalt, N, 32)
request  = METHOD \n path?query \n timestamp \n nonce \n sha256(body)
signature= HMAC-SHA256(HMAC(K,"social.v1.mac"), request)
body     = base64(iv16 | HMAC-CTR(HMAC(K,"social.v1.enc"), iv) XOR json | tag16)
```

Pure BCL (`HMACSHA256`, `RNGCryptoServiceProvider`), mirrored bit-for-bit in
`public/assets/js/crypto.js`.

---

## Tests

```cmd
bin\SocialServer.exe --selftest                  :: crypto vectors, sealing, JSON, captcha, storage
node tools\test-web.mjs http://127.0.0.1:55150 http://127.0.0.1:55160
powershell -ExecutionPolicy Bypass -File tools\ci-test.ps1
```

`ci-test.ps1` builds, runs the self-test, starts a clean instance in a temp folder,
runs the Node suite against **both boards**, restarts the server and verifies that
data survived.

`tools/test-web.mjs` speaks the same crypto as the browser: registration, login,
signature enforcement, replay rejection, encryption enforcement, posts, likes,
comments, friends, messages, groups, notifications, admin actions, recovery, logout.

Admin checks need an administrator's credentials and the admin board address:

```powershell
$env:SOCIAL_ADMIN_USER="admin"; $env:SOCIAL_ADMIN_PASSWORD="..."
node tools\test-web.mjs http://127.0.0.1:55150 http://127.0.0.1:55160
```

On a fresh instance the first account created by the suite is already the
administrator, so the admin block runs automatically there.

### Maintenance

```cmd
bin\SocialServer.exe --backup                     :: snapshot data/ and exit
bin\SocialServer.exe --cmd "user list humans"     :: any console command, server may be stopped
bin\SocialServer.exe --cmd "user role 9 admin"    :: promote an account
bin\SocialServer.exe --reset-admin NAME           :: same thing, older spelling
bin\SocialServer.exe --import-deadvk <db folder>  :: import an old DeadVK history
node tools\rename-user.mjs old new "New Name" [password]
```

`--cmd` accepts every command of the interactive console (`help`, `status`,
`user …`, `post del …`, `recovery export`, `import purge-bots`, `ai …`,
`broadcast`, `backup`, `reload`). Full reference: **[docs/CLI.md](docs/CLI.md)**.

`--import-deadvk` copies users, posts, comments, likes, messages, friends,
bookmarks, blocks, notifications, avatars and post images from the old project and
prefixes every imported status with *"Импортировано из старого проекта DeadVK"*.
Imported accounts cannot log in (their password scheme is different) — they are
content and AI personas. Details: **[docs/IMPORT.md](docs/IMPORT.md)**.

`rename-user.mjs` re-derives the login verifier, because it is bound to the
username (`social.login.v1|username`) — without that the account would be locked out.

---

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `[ERROR] csc.exe ... not found` | install .NET Framework 4.x (ships with Windows 8+) |
| `port 55150 is busy` | another instance or service; the server tries `portFallbacks` and logs the port it took |
| Everyone is logged out after a restart | by design: session keys live in memory only |
| `Storage is read-only` | a write failed (disk full / permissions). Fix it, then Admin → *Repair storage* |
| "Wrong username or password" but it is correct | username is case-insensitive, password is exact; `security.kdfIterations` must not change after registration |
| Behind a proxy everyone looks like `127.0.0.1` | add the proxy to `trustedProxies`, keep `trustProxyHeaders=true` |
| Admin menu is missing | you are on the public board: open the admin port (`server.adminPort`), the SPA tells you where it is |
| AI posts nothing | check Admin → AI: provider/enabled, then *Test connection*. Reasoning models need `maxTokens` around 1000+ |
| AI writes nonsense | that is the Markov provider learning from old posts; switch to `openai` or raise `temperature`/`intervalSec` |
| Secure cookie not set behind HTTPS | set `cookieSecure=true` or make the proxy send `X-Forwarded-Proto: https` |
| Captcha unreadable | click it for a new code; disable with `security.requireCaptcha=false` |
| `data/*.json` corrupt after a crash | the server restores `.bak` automatically and quarantines the broken file as `*.bad-YYYYMMDD-HHMMSS` |

---

## License

Use it, fork it, rebrand it. Attribution appreciated, not required.
