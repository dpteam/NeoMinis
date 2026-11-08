# API reference

Base: `http(s)://host[:port]` (default public port `55150`, admin board `55160`),
optional `server.publicBasePath`.

Two kinds of endpoints:

* **pre-auth** — `handshake`, `health`, `register`, `login`, `recover`
* **session** — everything else under `/api/`

Responses are JSON. Session endpoints are **signed** and their bodies **sealed**
(see [SECURITY.md](SECURITY.md)); errors on the auth layer are plain JSON with
`{"ok":false,"error":"code","message":"..."}` and an appropriate status.

---

## Pre-auth

### `GET /api/handshake`

Public parameters needed before any crypto work.

```json
{
  "ok": true, "version": "1.0.0",
  "scheme": "hmac-sha256+ctr", "kdf": "pbkdf2-sha256", "kdfIterations": 12000,
  "serverTimeUnix": 1794155543, "encryptApi": true, "captchaRequired": true,
  "userCount": 8, "firstUserAdmin": true,
  "site": { "name": "Social", "shortName": "S", "tagline": "...", "registration": true,
            "inviteOnly": false, "adminBoard": false, "adminPort": 55160,
            "defaultLanguage": "ru", "defaultTheme": "light",
            "themes": ["light","dark","oled"], "languages": ["ru","en","uk","lt"],
            "maxPostLength": 8000, "maxAvatarKB": 512, "refreshMs": 5000, "...": "..." },
  "captcha": { "id": "…", "svg": "<svg…>", "ttl": 300 }
}
```

The captcha is a fresh single-use challenge; its answer never appears in the payload
(digits are drawn as seven-segment `<line>` paths, there is no `<text>` element).

### `GET /api/health`

```json
{ "ok": true, "status": "ok|degraded", "version": "1.0.0", "uptimeSec": 64,
  "users": 8, "online": 1, "storageReadOnly": false, "lastSave": "2026-11-08T…Z" }
```

`degraded` means storage writes are failing; reads still work.

### `POST /api/register`

Headers: `X-Auth-User`, `X-Auth-Verifier` (64 hex), `X-Auth-Salt` (32 hex),
`X-Api-Ts`, `X-Api-Nonce`, `X-Api-Sig`, `X-Api-Enc: 1`.
Body (sealed): `{ "displayName": "...", "captchaId": "...", "captcha": "1234", "invite": "" }`.

`200` → `{ ok, sessionSalt, recoveryCode, user, me, site }` and a `Set-Cookie` session.
Errors: `400 bad-username|bad-verifier|bad-salt|captcha`, `403 registration-closed|invite`,
`409 username-taken`, `429 locked|rate-limit`, `503 read-only`.

### `POST /api/login`

Same headers, body may be empty. `200` → same shape as register (without
`recoveryCode`). Errors: `401 bad-credentials`, `403 banned`, `429 locked`.

### `POST /api/recover`

Headers: `X-Auth-User`, `X-Auth-Recover` (recovery code), `X-Auth-Salt`, plus the
usual signature headers signed with a key derived from the recovery code.
Body: `{ "newVerifier": "…64 hex…" }`.
`200` → `{ ok, recoveryCode }` (a **new** code; the old one is dead, all sessions dropped).

---

## Session endpoints

Common requirements: cookie `sid`, headers `X-Api-Ts`, `X-Api-Nonce`, `X-Api-Sig`,
`X-Api-Enc: 1`, sealed body for `POST`. All bodies below are the **plaintext**
inside the envelope.

Status codes used throughout: `400` validation, `401` auth/signature, `403`
permission, `404` missing, `409` conflict, `413` too large, `429` rate limit,
`503` read-only storage.

### Account

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/me` | — | `{ me, site, notifications[5], threads[5] }` |
| POST | `/api/logout` | — | drops the session |
| GET | `/api/settings` | — | `{ user, site }` |
| POST | `/api/settings` | `theme`, `lang`, `sound`, `hideOnline`, `privacy`, `status` | `{ user }` |
| POST | `/api/password` | `oldVerifier`, `newVerifier` | `{ sessionSalt }` (all other sessions dropped) |
| POST | `/api/avatar` | `data` (data URL, png/jpg/gif/webp) | `{ avatar }` |

### Feed and posts

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/feed` | `tab=home|global|friends`, `before`, `limit` | `{ tab, posts[], hasMore, next }` |
| GET | `/api/wall` | `user`, `before`, `limit` | `{ user, posts[], hasMore, next, canPost }` |
| GET | `/api/post` | `id` | `{ post, comments[] }` |
| POST | `/api/post` | `body`, `image`, `visibility`, `wall`, `group` | `{ post }` |
| POST | `/api/post/delete` | `id` | `{ ok }` |
| POST | `/api/like` | `id` | `{ liked, likes }` |
| GET | `/api/comments` | `post`, `before`, `limit` | `{ comments[], hasMore, next }` |
| POST | `/api/comment` | `post`, `body` | `{ comment, comments }` |
| POST | `/api/comment/delete` | `id` | `{ ok }` |

`visibility`: `public` (default) · `friends` · `private`.
`wall`: post to someone else's wall (friends only). `group`: post into a group you
belong to (wins over `wall`).

### Profiles, people, search

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/profile` | `user` or `username` | `{ user, posts[], friends[], groups[], canPost }` |
| POST | `/api/profile` | `displayName`, `bio`, `city`, `country`, `birthday`, `gender`, `website`, `email`, `status` | `{ user }` |
| GET | `/api/people` | `q`, `offset`, `limit` | `{ users[], total, offset, hasMore }` |
| GET | `/api/search` | `q` (≥ 2 chars) | `{ users[], posts[] }` |

`birthday` must be `YYYY-MM-DD`; `gender` is `m` / `f` / `other` / empty.

### Friends

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/friends` | `user` (optional) | `{ user, friends[], total, incoming[], outgoing[] }` |
| POST | `/api/friend` | `user`, `action` | `{ status, user }` |

`action`: `request` · `accept` · `decline` · `remove` · `block` · `unblock`.
`status` mirrors the resulting state: `pending`, `accepted`, `declined`, `removed`,
`blocked`, `unblocked`, `already`, `self`, `notfound`.

### Messages

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/messages/threads` | — | `{ threads[], unread }` |
| GET | `/api/messages` | `with`, `before`, `limit` | `{ other, messages[], hasMore, next }` |
| POST | `/api/messages/send` | `to`, `body` | `{ message }` |
| POST | `/api/messages/read` | `with` | `{ unread }` |
| POST | `/api/messages/delete` | `id` | `{ ok }` (per-side delete) |

Reading a conversation marks it read. Blocked users cannot exchange messages.

### Notifications

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/notifications` | `before`, `limit` | `{ notifications[], unread, hasMore, next }` |
| POST | `/api/notifications/read` | `all` or `id` | `{ unread }` |

Types: `like`, `comment`, `friend`, `message`, `wall`, `group`, `admin`, `report`, `system`.

### Groups

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/groups` | — | `{ groups[] }` |
| POST | `/api/groups` | `name`, `description`, `visibility` | `{ group }` (same as create) |
| GET | `/api/group` | `id` | `{ group, posts[], members[] }` |
| POST | `/api/group/create` | `name`, `description`, `visibility` | `{ group }` |
| POST | `/api/group/join` | `id` | `{ group }` |
| POST | `/api/group/leave` | `id` | `{ group }` (owner cannot leave) |
| GET | `/api/group/feed` | `id`, `before`, `limit` | `{ group, posts[], hasMore, next }` |

### Misc

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/online` | `{ users[], online, total }` |
| GET | `/api/stats` | counters + `days[]` (7-day visit graph) |
| POST | `/api/report` | `{ id }` — body `type` (`post|comment|user|message`), `target`, `reason` |

---

## Admin endpoints

**They exist only on the admin board** (`server.adminPort`, default `55160`).
On the public port `/api/admin/…` answers `404 unknown-endpoint`, exactly like a
typo — the shape of the admin surface is not discoverable from outside.

Moderator: `overview`, `users`, `reports`, `report`, `logs`.
Administrator: additionally `user` (destructive actions), `broadcast`, `reload`,
`backup`, `repair`.

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/admin/overview` | — | counts, storage, uptime, `recentUsers`, `topUsers`, `reports`, `logs`, `config` |
| GET | `/api/admin/users` | `q`, `offset`, `limit` | `{ users[], total, offset, hasMore }` |
| POST | `/api/admin/user` | `id`, `action`, `reason`, `days`, `role` | `{ user }` or `{ recoveryCode }` |
| GET | `/api/admin/reports` | `status=open|closed` | `{ reports[] }` |
| POST | `/api/admin/report` | `id`, `note` | `{ ok }` |
| POST | `/api/admin/broadcast` | `text` | `{ delivered }` |
| GET | `/api/admin/logs` | `since`, `limit`, `level`, `category` | `{ entries[], lastSeq }` |
| POST | `/api/admin/reload` | — | `{ warnings[] }` (port/bind/root need a restart) |
| POST | `/api/admin/backup` | — | `{ path }` |
| POST | `/api/admin/repair` | — | storage state after a forced save |

`action`: `ban` (`days` 0 = forever) · `unban` · `role` (`user|moderator|admin`) ·
`warn` · `reset` (new recovery code) · `delete` (soft delete).

Guards: the last administrator cannot be banned, demoted or deleted; nobody can
ban or delete themselves.

---

## Static surface

| Path | Notes |
| --- | --- |
| `/` | `public/index.html` |
| `/assets/…` | CSS/JS/SVG, ETag + gzip, `no-cache` revalidation |
| `/media/avatars/…`, `/media/posts/…` | uploaded images, `nosniff`, `inline` |
| `/robots.txt`, `/manifest.webmanifest`, `/favicon.ico` | small extras |
| anything else | `404.html` for `Accept: text/html`, otherwise `404` text |

There is no SPA fallback: routes live in the URL hash, so the proxy never has to
rewrite deep links.
