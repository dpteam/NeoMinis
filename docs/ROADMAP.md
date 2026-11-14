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
| **Paste a picture from the clipboard** | Every place that accepts an image should accept a `Ctrl+V`: the composer, the chat, the avatar/cover pickers, game previews, story upload. One shared helper on `paste` (`ClipboardEvent.clipboardData.items` → `getAsFile()`), then hand the `File` to the existing `UI.fileToDataUrl`. Cheap and it makes the site feel modern. |
| **Polish + finish the translations** | `i18n.t()` already falls back `lang → en → ru → key`, so a missing key is never a raw key on screen. What is missing is content: `uk` and `lt` only have ~15 keys each (they fall back to English), and there is no `pl` at all. Add `pl`, then fill `uk`/`lt` from the `ru` text. Mechanical but long; needs a native eye for the funnier strings. |

## Medium

Needs a new field or a new check in several places, but no new subsystem.

| Idea | Notes |
| --- | --- |
| **Privacy settings** | Today only two knobs exist: profile visibility (`public`/`friends`/`private`) and "hide online". Extend to per-area switches on the user: who may write first, who may call, who sees friends, who sees gifts, who sees albums, who may invite to a community, who sees the wall. Store as one flat string (`privacy.write=friends,privacy.call=all`), check it in `CanMessageUser`, `HandleCallStart`, `CanViewProfile` and the gift/album endpoints, and put the switches in Settings. Default everything to `all` so nothing changes for existing accounts. |
| **Media compression before upload** | Everything is stored exactly as it arrives today, which is why a phone photo lands as a 4 MB PNG. Compress on the client before the upload, and keep the original only for administrators. **Photos:** re-encode to JPEG (or WebP where supported) on a canvas, cap the long edge (`compress.imageMaxEdge`, default 1600), quality `compress.imageQuality` (0.82). A PNG with transparency becomes WebP (JPEG would fill the alpha with black); a GIF is left alone so it keeps animating. **Voice messages:** the browser records webm/opus already, so re-encode by lowering `MediaRecorder` bitrate (`audioBitsPerSecond`, default 32 kbps mono) and keep the original for staff. **Clips and library audio/video:** optional server-side ffmpeg pass (`--ffmpeg` path in the config) that remuxes to mp4/h264 + aac at a target bitrate, with a "quality" selector on upload (Original / High / Balanced / Small). Without ffmpeg the file is stored as is. **Admins** get a "keep original" switch and skip every limit; a per-role quota table replaces the single `maxPostImageKB`. Report the saved bytes in the response so the UI can say "12 MB → 900 KB". |
| **Gift details** | Gifts are one-line rows today ("Crown from Anonymous, 2 min ago"). Add a gift page per item: who sent what, the full note, when, whether it was opened, and a per-kind counter ("3 hearts, 1 crown"). Let the recipient hide or keep a gift from that page too, not only from the list. |
| **README and a logo for NeoMinis** | The repo is the engine (`NeoMinis`), the instance is `DPT.Minis`. Write a proper README: what it is, screenshots, the feature list, a 60-second quick start (`build.cmd` → `run.cmd` → open the port), the config map, how to add a language, how to run the tests, and the architecture in one picture. Plus a real logo: an SVG mark that works at 16 px as a favicon and at 512 px in the README, in the light/dark variants the site already supports. |

## Large

New subsystem or a real chunk of work.

### File transfer in DMs (a Send-Anywhere / TransFerry for our own site)

**Shipped:** the HTTP relay tier. `Core/Transfers.cs` holds chunks in memory,
`ApiRouter.Transfers.cs` exposes `/api/transfer*`, and the panel above the chat
composer (`public/assets/js/transfers.js`) does the pairing, the consent step,
the progress bar and the hash check. Friends only, 8-digit code, nothing stored,
payload dropped the moment the transfer ends.

What is left, in the order that makes sense:

1. **Direct WebRTC tier.** `RTCDataChannel` between the two browsers when they can
   reach each other, falling back to the relay above. This is the piece that makes
   big files cheap; the relay already works, so this is an optimisation with a
   clean fallback.
2. **NAT hairpin.** If both sides sit behind the same NAT (the DPTDOOM2 problem),
   the browser cannot connect to its own public address, so force the relay when the
   detected public address matches.
3. **A message in the chat when it finishes.** Today the panel says "delivered";
   the conversation should also get a normal row with the file name, size and hash,
   so the history explains what happened a week later.
4. **Disk cap for the relay** (`maxRelayBytes`) if it ever grows a tier that spools
   to disk — the in-memory version needs no such knob.

The reference implementation is `C:\Servers\WebServers\send.dartpower.xyz`:
three tiers (WebRTC P2P → WebSocket relay → plain HTTP PUT/GET), 8-digit codes,
256 KB chunks and SHA-256 on both sides.

### Calls like Telegram / Discord

The current calls work (1:1 and named rooms, WebRTC media, signalling over the long
poll) but they are the minimum.

| Piece | Notes |
| --- | --- |
| **Screen sharing** | `getDisplayMedia` plus a second track per peer; `display-capture` is already allowed in `Permissions-Policy`. Needs renegotiation, because adding a track mid-call changes the SDP. |
| **Calls that survive a reload** | `/api/call/active` exists; the overlay has to restore the peer connections from `sessionStorage` too. |
| **Discord-style call page** | Done: `#/calls` is a channel sidebar plus a room view, backed by `/api/channels`. Still open from the original idea: a Web Audio level meter so the avatar of whoever is talking gets a green ring, and drag-to-reorder channels. |
| **Ringing when the tab is closed** | Web Push with a `call` payload, a ringtone, and an "answer" action in the notification. |
| **Group layout** | Grid that switches to speaker view, mute-all, "raise hand", per-member volume. |
| **Noise suppression / echo cancel** | `getUserMedia({ audio: { echoCancellation, noiseSuppression, autoGainControl } })` as toggles. |
| **Call link** | `#/call/<room>` with a copy button, so a room can be pasted into a chat. |
| **Recording** | `MediaRecorder` over a mixed stream into the media library, with a visible "recording" badge and consent from every participant. |

### Applications API (the big one)

**Shipped:** registration with a one-time secret, five named scopes, the consent
screen, code → token exchange, per-user app storage with a quota, leaderboards, and
a plain-JSON token surface an external server can actually call. See the
Applications API section in `API.md`. HTML apps stay administrator-only because
they run on our origin.

What is left:

1. **Migrate the DartPower mini-games** (`C:\Servers\WebServers\`: `joker`,
   `dilema`, `dicer`, `pets`, `soccer`, `space`, `spore` are Node + SQLite). One at
   a time: read `server.js`, keep the game logic, replace `fetch('/api/…')` with the
   app endpoints. `spore` is already a static bundle and is the easiest first move;
   `dicer` and `pets` carry the most state and exercise the storage scope properly.
2. **A VK-style long poll for apps** (`/api/apps/longpoll`) so a game can be told
   about an event instead of polling, sharing the `EnterWait` budget.
3. **Signed requests from the app** (HMAC with the client secret) for the cases
   where a bearer token in a header is not enough.
4. **A consent screen for HTML apps** that shows the origin they will run on,
   because that is the part a user should be suspicious of.

### Push notifications

**Shipped:** VAPID signing with a P-256 key generated on first start, subscriptions
stored per browser, a service worker at `/sw.js`, and a settings switch that
degrades honestly (no support, blocked permission, unsupported platform).

Because .NET 4.0 has no AES-GCM, the server sends an **empty tickle** rather than an
encrypted payload, and the worker fetches `/api/push/pending`. That is a deliberate
trade: same user-visible result, no home-made crypto.

What is left:

1. **Encrypted payloads** if the server ever moves to a framework with AES-GCM, so
   the push service never learns even the fact that something happened.
2. **Per-type preferences** for push, separate from the in-app notification mutes.
3. **A badge count** on the app icon (`navigator.setAppBadge`) from the same pending
   call.

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
- Sticker packs next to the emoji picker.
- Scheduled messages ("send at 9:00").

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
- Guest / kiosk mode: a signed read-only session with no account, a whitelist of
  readable endpoints and a hard 403 on every write; a kiosk variant with no login
  form and an idle return to the feed.
- Janitors: granular moderator rights (posts, reports, warn, mute, feedback, users,
  logs, games) with a checkbox panel in the user card, plus a chat mute with its own
  duration. A moderator with an empty set keeps every right, as before.
- Profile rating: a derived percentage from how filled the profile is (avatar, cover,
  bio, status, birthday, city, country, gender, website, the four interests, a first
  post) plus a separate achievement counter. Nothing is stored, so it cannot drift.
- Peer file transfer between friends: an 8-digit code, consent before a byte moves,
  SHA-256 checked by the receiver, chunks relayed in memory and dropped the moment
  the transfer ends. 32 MB cap, no extension blocklist, nothing written to disk.
- Calls that say why they fail: several STUN servers, an optional TURN relay with
  `callTurn` / `callIcePolicy`, a 22-second watchdog because Chromium does not
  always fire `failed`, the selected candidate pair and live bitrate/RTT/loss under
  each tile, a "Reconnect" button that rebuilds the peer connection, and a glare
  rule so two simultaneous offers cannot deadlock at "connecting".
- Voice channels: a Discord-style sidebar on `#/calls`, named rooms in
  `channels.json`, live occupancy read from the call registry, create/edit/delete
  with the creator or an admin in charge, and a delete that does not throw out the
  people already talking.
- Applications API: registration with a one-time secret, five named scopes
  (`profile`, `wall.post`, `messages.read`, `storage`, `score`), a consent screen
  where the user ticks a subset, a one-time code exchanged for a bearer token, a
  plain-JSON token surface an external app can call, per-user app storage with a
  quota, and leaderboards. HTML apps stay administrator-only.
- Push notifications: VAPID signing with a generated P-256 key, per-browser
  subscriptions, a service worker that shows what is waiting, and a settings switch
  that explains itself when the browser blocks notifications.
- "Delete for everyone" with a time window, and pinned messages inside a chat.
  A revoke leaves a tombstone and deletes the files; a pin shows in a small bar
  above the conversation for both sides.
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
