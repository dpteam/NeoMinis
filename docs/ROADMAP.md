# Roadmap

Ideas that are not built yet, ordered by how much work they are: a quick win is a
day, medium is a few days, large is a sprint. Nothing here is a promise — it is a
parking lot so good ideas do not get lost.

## Quick wins

Small, self-contained, no new storage.

| Idea | Notes |
| --- | --- |
| **Call from the profile** | The chat header already has phone and camera buttons; the profile only has "Message" and "Remove friend". Add the same two buttons for friends (and keep the "can they be called" check in one place). ~30 lines. |
| **VIP badge** | Cosmetic only, exactly like the old VK joke: a `vip` flag on the user, a golden label next to the name on the wall, in the chat and in comments, granted and revoked by an admin in Admin → Users. Grants nothing. |
| **Game previews** | `MediaItem` already has a cover slot for albums; games need their own `cover` image (upload, `maxAvatarKB` limits, shown in the shelf grid and on the game page instead of the gamepad placeholder). |
| **Profile cover upload** | `.profile-cover` is an empty div today. Add `POST /api/profile/cover`, store under `/media/avatars/`, crop with `object-fit`, fall back to the gradient. |
| **More achievements** | Pure data: `Badges.All` has 13 entries and the granting rules are a switch. Add ~20: first photo, first album, first story, first gift sent/received, first game uploaded, 100 messages, 10 communities, night owl (posting after 3am), polyglot (changed language), collector of gifts, and a few manual ones for admins. |
| **Paste a picture from the clipboard** | Every place that accepts an image should accept a `Ctrl+V`: the composer, the chat, the avatar/cover pickers, game previews, story upload. One shared helper on `paste` (`ClipboardEvent.clipboardData.items` → `getAsFile()`), then hand the `File` to the existing `UI.fileToDataUrl`. Cheap and it makes the site feel modern. || **Polish + finish the translations** | `i18n.t()` already falls back `lang → en → ru → key`, so a missing key is never a raw key on screen. What is missing is content: `uk` and `lt` only have ~15 keys each (they fall back to English), and there is no `pl` at all. Add `pl`, then fill `uk`/`lt` from the `ru` text. Mechanical but long; needs a native eye for the funnier strings. |

## Medium

Needs a new field or a new check in several places, but no new subsystem.

| Idea | Notes |
| --- | --- |
| **Privacy settings** | Today only two knobs exist: profile visibility (`public`/`friends`/`private`) and "hide online". Extend to per-area switches on the user: who may write first, who may call, who sees friends, who sees gifts, who sees albums, who may invite to a community, who sees the wall. Store as one flat string (`privacy.write=friends,privacy.call=all`), check it in `CanMessageUser`, `HandleCallStart`, `CanViewProfile` and the gift/album endpoints, and put the switches in Settings. Default everything to `all` so nothing changes for existing accounts. |
| **Guest / demo / kiosk mode** | Today every `/api/` call needs a session. Add a read-only anonymous viewer: a `[site] guestMode` flag (plus `guestPassword` for kiosk), a `viewer = null` path through the read endpoints (feed, post, profile, group, tags, search, media, games), and a hard 403 on every write. Kiosk variant: no login form, a "demo" banner, auto-return to the feed after a few idle minutes, no admin entry. Careful with the rate limiter and with `UserView` (an anonymous viewer must not see e-mails or IP hashes). |
| **Janitors (granular moderator rights)** | Right now `moderator` is all-or-nothing. Give a user a set of permission flags (`canDeletePosts`, `canCloseReports`, `canWarn`, `canMuteInChat`, `canSeeFeedback`) and check those instead of `IsModerator` in the admin endpoints. A "janitor" is just a user with two of them. Admin → Users gets a checkbox panel. Existing moderators keep every flag. |
| **Profile rating** | The old-VK joke: a percentage from how filled the profile is (avatar, cover, bio, city, birthday, status, interests, first post) up to 100%, then the achievements add on top as a separate counter. Show it on the profile as a small meter with a tooltip listing what is missing. Pure computation, no storage — derived from the user row and the badge list. |
| **Media compression before upload** | Everything is stored exactly as it arrives today, which is why a phone photo lands as a 4 MB PNG. Compress on the client before the upload, and keep the original only for administrators. **Photos:** re-encode to JPEG (or WebP where supported) on a canvas, cap the long edge (`compress.imageMaxEdge`, default 1600), quality `compress.imageQuality` (0.82). A PNG with transparency becomes WebP (JPEG would fill the alpha with black); a GIF is left alone so it keeps animating. **Voice messages:** the browser records webm/opus already, so re-encode by lowering `MediaRecorder` bitrate (`audioBitsPerSecond`, default 32 kbps mono) and keep the original for staff. **Clips and library audio/video:** optional server-side ffmpeg pass (`--ffmpeg` path in the config) that remuxes to mp4/h264 + aac at a target bitrate, with a "quality" selector on upload (Original / High / Balanced / Small). Without ffmpeg the file is stored as is. **Admins** get a "keep original" switch and skip every limit; a per-role quota table replaces the single `maxPostImageKB`. Report the saved bytes in the response so the UI can say "12 MB → 900 KB". |
| **Gift details** | Gifts are one-line rows today ("Crown from Anonymous, 2 min ago"). Add a gift page per item: who sent what, the full note, when, whether it was opened, and a per-kind counter ("3 hearts, 1 crown"). Let the recipient hide or keep a gift from that page too, not only from the list. |
| **README and a logo for NeoMinis** | The repo is the engine (`NeoMinis`), the instance is `DPT.Minis`. Write a proper README: what it is, screenshots, the feature list, a 60-second quick start (`build.cmd` → `run.cmd` → open the port), the config map, how to add a language, how to run the tests, and the architecture in one picture. Plus a real logo: an SVG mark that works at 16 px as a favicon and at 512 px in the README, in the light/dark variants the site already supports. |

## Large

New subsystem or a real chunk of work.

### File transfer in DMs (a Send-Anywhere / TransFerry for our own site)

`C:\Servers\WebServers\send.dartpower.xyz` already does this and is the reference:
a three-tier transfer (WebRTC P2P → WebSocket relay → plain HTTP(S) PUT/GET), an
8-digit code to pair two people, 256 KB chunks, SHA-256 checked on both sides, rooms
with a TTL, and a relay that only ever stores ciphertext. What it does not do is tie
into accounts, which is exactly what we need.

Our version, as a panel inside a conversation:

1. **Pairing.** The sender starts a transfer, gets an 8-digit code (valid ~30
   minutes), the recipient types it in the chat. **Friends only** — a stranger can
   never receive, so the code alone is not enough.
2. **Consent before a byte moves.** The recipient sees the file name, size, type and
   the sender, and can accept or refuse. Refusing tells the sender, and nothing is
   ever written to disk.
3. **No extension limits.** The EXE/APK ban exists because files live on our origin
   and get served back; a transfer only moves bytes between two browsers, so the
   blocklist does not apply. Files are hashed, never executed, never stored in
   `data/media/` for more than the transfer's lifetime.
4. **Three tiers, same as TransFerry.** Direct WebRTC when both sides are reachable,
   the WebSocket relay when they are not, plain chunked PUT/GET when even WebSocket
   is blocked by a proxy. The client picks automatically and says which one it uses.
5. **Relay mode for NAT hairpin.** If two people sit behind the same NAT (or the
   same host publishes an app), the browser cannot connect to its own public address.
   That is the DPTDOOM2 problem: force the transfer through the relay when the
   detected public address matches, or let the user pick "relay only".
6. **Housekeeping.** Rooms expire, the relay has a disk cap (`maxRelayBytes`) and a
   per-user concurrency cap; a finished transfer leaves a normal message in the chat
   with the file name, size and hash.

### Calls like Telegram / Discord

The current calls work (1:1 and named rooms, WebRTC media, signalling over the long
poll) but they are the minimum.

| Piece | Notes |
| --- | --- |
| **Screen sharing** | `getDisplayMedia` plus a second track per peer; `display-capture` is already allowed in `Permissions-Policy`. Needs renegotiation, because adding a track mid-call changes the SDP. |
| **Calls that survive a reload** | `/api/call/active` exists; the overlay has to restore the peer connections from `sessionStorage` too. |
| **Fix "connection failed" first** | In testing the peers stayed in ICE state `new` and the call showed "не удалось соединиться". Most likely cause: only a public STUN server is configured (`limits.callStun`), so any symmetric NAT or a hairpin between two machines behind the same router cannot connect. Needs (a) a real TURN server in the config recipe, (b) the overlay printing the ICE state and the candidate pair so the failure is diagnosable, (c) a fallback to the relay when direct fails. This is the first thing to do before any of the fancy call features. |
| **Discord-style call page** | Replace the single "room name" box with a Discord-like sidebar: a list of channels (text-like names, e.g. `general`, `games`, `afk`) that anybody can create, a "join" button per channel, a list of who is inside each channel right now, and the active call filling the rest of the page. Voice channels get a speaker icon, the number of people inside, and a green ring around avatars of people currently talking (Web Audio level meter per peer). Rooms become persistent channel records (name, created by, order) instead of free-typed names, with an admin-editable list. |
| **Ringing when the tab is closed** | Web Push with a `call` payload, a ringtone, and an "answer" action in the notification. |
| **Group layout** | Grid that switches to speaker view, mute-all, "raise hand", per-member volume. |
| **Noise suppression / echo cancel** | `getUserMedia({ audio: { echoCancellation, noiseSuppression, autoGainControl } })` as toggles. |
| **Call link** | `#/call/<room>` with a copy button, so a room can be pasted into a chat. |
| **Recording** | `MediaRecorder` over a mixed stream into the media library, with a visible "recording" badge and consent from every participant. |
| **TURN server** | Symmetric NATs cannot connect without it. `limits.callStun` already takes a list, so this is a config change plus a documented `coturn` recipe. |
| **Call quality stats** | `pc.getStats()` → bitrate, packet loss, RTT in the overlay, so "it sounds bad" has a number. |

### Applications API (the big one)

The site should host small third-party apps the way VK and Telegram do, and the
DartPower mini-games should become the first real examples.

**Permission model** (partly built: `.swf` uploads already need `canGames`, granted in
Admin → Users; admins always may):

1. An app is registered by a developer and gets a `clientId` + secret.
2. The app asks for scopes (`profile`, `wall.post`, `messages.read`, `storage`).
3. The owner approves them on a consent screen; tokens can be revoked per app.
4. **HTML apps can only be published by administrators** — they run on our origin, so
   that is the only safe rule. `.swf` games are sandboxed in an iframe already and
   only need the `canGames` flag.

| Endpoint | Purpose |
| --- | --- |
| `POST /api/apps/register` | developer registers an app (name, icon, type) |
| `GET /api/apps` | public catalogue, with install counters |
| `POST /api/apps/token` | exchange a one-time code for an access token |
| `POST /api/apps/revoke` | the user drops an app |
| `GET /api/apps/longpoll` | VK-style `LongPoll`: one blocking request returning `{ts, updates[]}` — same `EnterWait` budget as notifications |
| `POST /api/apps/storage` | per-app key/value storage, quota per app |
| `POST /api/apps/score` | leaderboards for games |

**Migration of the existing mini-games** (`C:\Servers\WebServers\`): `joker`, `dilema`,
`dicer`, `pets`, `soccer`, `space`, `spore` are Node + SQLite. One port at a time: read
their `server.js` routes, keep the game logic, replace `fetch('/api/…')` with our
endpoints. `spore` is already a static bundle and is the easiest first move; `dicer`
and `pets` carry the most state (inventory, levels) and need per-app storage.

### Push notifications

Service Worker + Web Push so likes and messages arrive with the tab closed, on top of
the current long poll. Needs VAPID keys in `config.ini`, a subscription table, and a
switch in Settings.

## Later

- **Import from VK GDPR dumps.** A user can request their data from VK and gets a
  folder of JSON/HTML. Import posts, photos, friends and messages from that dump —
  much more reliable than scraping, and legal. Same shape as the DeadVK importer:
  `--import-vk <dir>` plus a dry-run report.
- **Two-factor authentication (TOTP)** per account, with recovery codes.
- **WebDAV/S3 offload** of `/media/` for large instances.
- **Full offline PWA**: cache the shell, queue posts offline, sync on reconnect.
- **Transcoding on upload** (optional ffmpeg) to normalise containers and build
  thumbnails.

## Ideas per feature

Small things that would make what already exists noticeably better.

**Feed and posts**
- Edit history ("edited 3 times", old versions visible to the author).
- "Show less like this" per author, remembered per user.
- Draft autosave in `localStorage` so a closed tab does not lose text.
- Link preview cards (title + description, no images).

**Messages**
- Search inside one conversation (the global search already finds them).
- Pinned messages inside a chat.
- "Delete for everyone" with a time window.
- Scheduled messages ("send at 9:00").
- Sticker packs next to the emoji picker.

**Communities**
- Per-community rules shown before joining.
- A "moderation log" tab: who approved, rejected, kicked, and when.
- Scheduled posts inside a community.

**Profiles and albums**
- Drag photos between albums.
- Album sharing with a token link for people without an account.
- Profile covers (see the quick wins above).

**Games**
- Comments under a game, and a "plays today" counter.
- Score submission and a leaderboard per game (needs the apps API above).
- "Continue where you left off" for `.swf` that support save data.

**Stories**
- Text-only stories on a coloured background (no upload needed).
- Replies: swipe up to answer a story, landing in the messenger as a quote.
- "Mute this person's stories" per author.

**Admin and operations**
- Bulk actions in the user list (ban several at once).
- A "storage" panel: biggest files, orphaned media, records without a file.
- Per-user quota overview.
- Maintenance mode with a countdown banner.

**Everywhere**
- Keyboard shortcuts (`g f` for feed, `/` for search) with a cheat sheet.
- Accessibility: focus rings on every custom button, `aria-live` for toasts.

## Done

Kept here so the list above stays honest.

- Feed with images, video links, polls, reactions, likes, reposts, pins, edits.
- Drafts and scheduled publication.
- Private messages: live delivery, typing, read receipts, replies, reactions,
  forwarding, search, pictures/GIFs, albums, voice messages, clips, file attachments
  with an executable blocklist.
- Per-area privacy: who may write first, call, post on the wall, send gifts, see
  albums and see the friend list ("anyone / friends only / nobody").
- Groups with editable settings, pictures and member management.
- Communities: owner / editor / member roles, join requests for closed groups and a
  moderation queue ("members post, editors approve").
- Mentions, hashtags, tag pages, filtered post search.
- Notifications: live long poll, per-type mutes, filters, desktop notifications, live
  unread counters for messages, notifications and friend requests.
- Music and video library with uploads, playlists, editable titles/descriptions, and
  a "My library" tab that really shows only yours (the shared shelf is separate).
- "Save to my library": one-click copy of somebody else's track or clip.
- Gifts: a built-in catalogue sent from a profile, anonymous or signed, with their own
  block on the wall, a received/sent page and notifications.
- Flash games: upload `.swf` (admins and approved authors only), played through a
  self-hosted Ruffle build inside a sandboxed iframe, with a shelf, search, plays
  counter and likes.
- Voice and video calls plus named conference rooms over WebRTC, with history.
- Achievements, bookmarks, photo gallery, profiles with interests and moods.
- Profile cover, calls from the profile, game preview images, a cosmetic VIP label,
  and 23 achievements instead of 13.
- Photo albums: named, with a cover, filled from the wall, photos outlive the album.
- Stories: photos and clips above the feed that vanish on time, with a seen-by list.
- One search over people, posts, comments, your own messages, communities and games,
  with kind tabs and date/author filters.
- Mobile layout: chip-rail navigation, stacked messenger, finger-sized controls.
- Admin board: users, roles, badges, reports, logs, AI panel, settings, backups.
- DeadVK import, operator CLI, self-test, CI script.
