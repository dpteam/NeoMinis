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
| POST | `/api/settings` | `theme`, `lang`, `sound`, `hideOnline`, `privacy`, `status`, `notifyMute[]` | `{ user }` |
| POST | `/api/password` | `oldVerifier`, `newVerifier` | `{ sessionSalt }` (all other sessions dropped) |
| POST | `/api/avatar` | `data` (data URL, png/jpg/gif/webp) | `{ avatar }` |

### Feed and posts

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/feed` | `tab=home|global|friends`, `before`, `limit` | `{ tab, posts[], hasMore, next }` |
| GET | `/api/wall` | `user`, `before`, `limit` | `{ user, posts[], hasMore, next, canPost }` |
| GET | `/api/post` | `id` | `{ post, comments[] }` |
| POST | `/api/post` | `body`, `image`, `video`, `poll`, `visibility`, `wall`, `group` | `{ post }` |
| POST | `/api/post/delete` | `id` | `{ ok }` |
| POST | `/api/post/edit` | `id`, `body`, `video`, `visibility`, `imageAction` (`keep|remove|replace`), `image` | `{ post }` |
| POST | `/api/post/pin` | `id`, `on` | `{ pinned }` |
| POST | `/api/like` | `id` | `{ liked, likes }` |
| GET | `/api/comments` | `post`, `before`, `limit` | `{ comments[], hasMore, next }` |
| POST | `/api/comment` | `post`, `body` | `{ comment, comments }` |
| POST | `/api/comment/delete` | `id` | `{ ok }` |
| GET | `/api/bookmarks` | `before`, `limit` | `{ posts[], hasMore, next, total }` |
| POST | `/api/bookmark` | `id` | `{ bookmarked, total }` |
| GET | `/api/likes` | `post`, `limit` | `{ users[], total }` |
| POST | `/api/poll` | `post`, `option` | `{ post }` (with the fresh poll state) |
| GET | `/api/photos` | `user`, `before`, `limit` | `{ user, photos[{ id, image, created, likes, comments }], hasMore, next }` |

`visibility`: `public` (default) · `friends` · `private`.
`wall`: post to someone else's wall (friends only). `group`: post into a group you
belong to (wins over `wall`). `video`: an `http(s)` link; YouTube links get a
thumbnail card, `.mp4`/`.webm`/`.ogv` files play inline, everything else becomes a
link card.

Bookmarks are per user and private: `POST /api/bookmark` toggles the saved state
of a post and the reply tells you the new state and the total saved count.

`POST /api/post/edit` is limited to the author and the staff. When
`limits.editWindowMin` is greater than zero, an ordinary user can only edit a post
inside that window (moderators are never limited). Editing sets `post.edited`.

`poll` in `/api/post` is `{ question, options[] }`: 2–6 options, 80 chars each,
160 for the question. A poll alone is a valid post. Votes are single choice and
private-safe: `POST /api/poll` with `post` and `option` sets or changes the vote,
clicking the same option again withdraws it (`myVote` becomes `-1`). Every post
with a poll carries `poll: { question, total, myVote, options[{ text, votes, percent, mine }] }`.

`POST /api/post/pin` pins a post to the top of a wall or group feed; the post
author, the wall owner, the group owner and the staff may pin. Only one post per
wall/group stays pinned. `canEdit` / `canPin` come back with every post so the SPA
can render the buttons without guessing.

### Profiles, people, search

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/profile` | `user` or `username` | `{ user, posts[], friends[], groups[], photos[], canPost }` |
| POST | `/api/profile` | `displayName`, `bio`, `city`, `country`, `birthday`, `gender`, `website`, `email`, `status`, `favGames`, `favMovies`, `favMusic`, `favBooks`, `mood` | `{ user }` |
| GET | `/api/people` | `q`, `offset`, `limit` | `{ users[], total, offset, hasMore }` |
| GET | `/api/search` | `q` (≥ 2 chars) | `{ users[], posts[] }` |

`birthday` must be `YYYY-MM-DD`; `gender` is `m` / `f` / `other` / empty.
`mood` is `happy` / `sad` / `neutral` / empty. The four `fav*` fields are free text
(≤ 200 chars) and, like the rest of the private block, are only returned when the
viewer is the owner, staff, a friend, or the profile is public.

### Achievements

`site.badges` (handshake) lists the catalogue: `{ id, icon, manual }`. Badges live
on the user as a comma separated string (`user.badges`), the same format DeadVK
used, so imported profiles keep theirs. Auto badges are granted by the server as
soon as the counter is reached; manual ones only by an administrator.

| Id | Icon | Rule |
| --- | --- | --- |
| `first_post` | `pen-nib` | 1 post |
| `author` | `feather` | 10 posts |
| `writer` | `book-open` | 100 posts |
| `commenter` | `comment-dots` | 10 comments |
| `liker` | `heart-circle-plus` | 50 likes given |
| `lover` | `heart` | 10 likes received |
| `social` | `user-group` | 5 friends |
| `popular` | `fire` | 25 friends |
| `collector` | `bookmark` | 10 bookmarks |
| `chatter` | `comments` | 100 messages sent |
| `veteran` | `clock-rotate-left` | account older than 365 days |
| `verified` | `circle-check` | manual |
| `admin_grant` | `user-shield` | manual |

`GET /api/admin/user?id=` returns `badges`; `POST /api/admin/user` with
`action=badge`, `badge=<id>`, `on=true|false` grants or revokes one.
`POST /api/admin/badges/recalc` walks every account and grants whatever has been
earned (useful after an import or a rule change).

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
| POST | `/api/messages/send` | `to`, `body`, `image`, `replyTo` | `{ message }` |
| GET | `/api/messages/wait` | `with`, `after`, `timeout` | `{ messages[], latest, typing, read, unread, live }` |
| POST | `/api/messages/typing` | `to` | `{ ok }` |
| POST | `/api/messages/read` | `with` | `{ unread }` |
| POST | `/api/messages/delete` | `id` | `{ ok }` (per-side delete) |

Reading a conversation marks it read. Blocked users cannot exchange messages.

`image` is a data URL (png/jpeg/gif/webp, up to `limits.maxMessageImageKB`) stored
under `/media/messages/`; animated GIFs keep animating in the chat. `replyTo` is
the id of a message from the same conversation and comes back as a `replyTo`
object (`id`, `from`, `body`, `image`, `mine`).

`/api/messages/wait` is the chat long poll: it holds the request (max 25s,
`ui.notifyWaitSec` by default) and answers as soon as a message newer than
`after` arrives, the peer starts or stops typing, or the read state of our last
message changes. `read` is the read receipt for our newest message; `typing`
comes from an in-memory registry (a few seconds, never persisted). When
`ui.liveNotifications=false` it answers immediately and the SPA polls instead.

### Notifications

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/notifications` | `before`, `limit` | `{ notifications[], unread, hasMore, next }` |
| GET | `/api/notifications/wait` | `after`, `timeout` | `{ notifications[], unread, latest, live }` |
| POST | `/api/notifications/read` | `all` or `id` | `{ unread }` |

`/api/notifications/wait` is a long poll: it holds the request until a
notification newer than `after` appears or `timeout` seconds pass (capped at 25,
default `ui.notifyWaitSec`). The SPA keeps a single loop open so likes, comments
and mentions pop up instantly. When `ui.liveNotifications=false` the endpoint
answers immediately and the SPA goes back to plain polling.

`notifyMute` in `/api/settings` is the list of notification types the user does
not want: `like`, `comment`, `friend`, `message`, `wall`, `group`, `mention`,
`badge`. `admin` and `system` can never be muted.

Types: `like`, `comment`, `friend`, `message`, `wall`, `group`, `mention`, `badge`, `admin`, `report`, `system`.

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
| POST | `/api/group/edit` | `id`, `name`, `description`, `visibility` | `{ group }` |
| POST | `/api/group/avatar` | `id`, `data` (data URL) | `{ avatar }` |
| POST | `/api/group/kick` | `id`, `user` | `{ group }` |

Only the group owner (or an administrator) may edit a group, change its picture or
remove a member; the owner can never be removed.

### Tags

| Method | Path | Body / query | Result |
| --- | --- | --- | --- |
| GET | `/api/tag` | `tag`, `before`, `limit` | `{ tag, posts[], hasMore, next }` |
| GET | `/api/tags` | — | `{ tags[{ tag, count }] }` (top 20 public tags) |

`#hashtags` in a post body are extracted server side; `#tag` matches only the exact
tag, not a longer word that starts with it. `@username` mentions create a `mention`
notification for every mentioned user (at most 10 per post/comment, never yourself).

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
| GET | `/api/admin/user` | `id` | user, `sessions`, `bookmarks`, role policy flags |
| GET | `/api/admin/settings` | — | the whole config, grouped for the Settings tab |
| POST | `/api/admin/settings/save` | grouped config | `{ warnings[] }` (written back to `config.ini`) |
| POST | `/api/admin/badges/recalc` | — | `{ granted, users }` |
| POST | `/api/admin/notify-test` | — | `{ id }` (sends a system notification to yourself, handy to verify live delivery) |

`action`: `ban` (`days` 0 = forever) · `unban` · `role` (`user|moderator|admin`) ·
`grant-admin` · `revoke-admin` · `grant-mod` · `badge` (`badge`, `on`) ·
`warn` · `reset` (new recovery code) · `delete` (soft delete) · `purge` (hard delete).

Guards: the last administrator cannot be banned, demoted or deleted; nobody can
ban or delete themselves; role changes respect `allowRoleChange`, `allowSelfDemote`
and `protectLastAdmin` from the config.

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
