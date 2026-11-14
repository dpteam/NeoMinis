# Roadmap

Ideas that are not built yet, roughly in the order they make sense. Nothing here
is a promise — it is a parking lot so good ideas do not get lost.

## Next up

### Calls like Telegram / Discord

The current calls work (1:1 and named rooms, WebRTC media, signalling over the
long poll) but they are the minimum. To reach Telegram/Discord level:

| Piece | Notes |
| --- | --- |
| **Screen sharing** | `getDisplayMedia` plus a second track per peer; the button already exists in `Permissions-Policy` (`display-capture`). Needs renegotiation, because adding a track mid-call changes the SDP. |
| **Calls that survive a reload** | Store the call in `sessionStorage` and rejoin on `CallUI.init` — half done (`/api/call/active` exists), but the overlay has to restore the peer connections too. |
| **Ringing when the tab is closed** | Web Push (see below) with a `call` payload, plus a ringtone and an "answer" action in the notification. |
| **Group layout** | A grid that switches to speaker view when somebody is loud, mute-all, "raise hand", per-member volume. |
| **Noise suppression / echo cancel** | `getUserMedia({ audio: { echoCancellation, noiseSuppression, autoGainControl } })`, exposed as a toggle. |
| **Call link** | `#/call/<room>` with a copy button, so a room can be pasted into a chat. |
| **Recording** | `MediaRecorder` over a mixed stream, saved to the media library. Needs consent from every participant and a visible "recording" badge. |
| **Push-to-talk and per-app volume** | Keyboard hold to talk, ducking other audio. |
| **TURN server** | Without it, symmetric NATs cannot connect. `limits.callStun` already accepts a comma-separated list, so adding a TURN URL with credentials is a config change plus a documented `coturn` recipe. |
| **Call quality stats** | `pc.getStats()` → bitrate, packet loss and RTT in the overlay, so "it sounds bad" has a number. |

### Applications API (the big one)

The site should host small third-party apps the way VK and Telegram do, and the
DartPower mini-games should become the first real examples.

**Permission model** (partly built: `.swf` uploads already need `canGames`, granted
in Admin → Users; admins always may):

1. An app is registered by a developer and gets a `clientId` + secret.
2. The app asks for scopes (`profile`, `wall.post`, `messages.read`, `storage`).
3. The owner approves them on a consent screen; tokens can be revoked per app.
4. **HTML apps can only be published by administrators** — they run on our origin,
   so that is the only safe rule. `.swf` games are sandboxed in an iframe already
   and only need the `canGames` flag.

**What the API needs:**

| Endpoint | Purpose |
| --- | --- |
| `POST /api/apps/register` | developer registers an app (name, icon, type) |
| `GET /api/apps` | public catalogue, with install counters |
| `POST /api/apps/token` | exchange a one-time code for an access token |
| `POST /api/apps/revoke` | the user drops an app |
| `GET /api/apps/longpoll` | VK-style `LongPoll`: one blocking request that returns `{ts, updates[]}` — the same `EnterWait` budget the notifications use |
| `POST /api/apps/storage` | per-app key/value storage, quota per app |
| `POST /api/apps/score` | leaderboards for games |

**Migration of the existing mini-games** (`C:\Servers\WebServers\`): `joker`,
`dilema`, `dicer`, `pets`, `soccer`, `space`, `spore` are Node + SQLite. The plan is
one port at a time: read their `server.js` routes, keep the game logic, and replace
`fetch('/api/…')` with our own endpoints (`/api/apps/…` + `longpoll`). `spore` is
already a static Flash/HTML bundle and is the easiest first move; `dicer` and `pets`
carry the most state (inventory, levels) and will need a per-app storage table.

### Push notifications

Service Worker + Web Push so likes and messages arrive with the tab closed, on top
of the current long poll. Needs VAPID keys in `config.ini`, a subscription table,
and a "notifications" switch in Settings.

## Later

- **Import from VK GDPR dumps.** A user can request their data from VK and get a
  folder of JSON/HTML. Import posts, photos, friends and messages from that dump —
  much more reliable than scraping, and legal. Same shape as the DeadVK importer:
  a `--import-vk <dir>` command plus a dry-run report.
- **Two-factor authentication (TOTP)** as an option per account, with recovery codes.
- **WebDAV/S3 offload** of `/media/` for large instances, so the JSON tables stay
  small and files live elsewhere.
- **Full offline PWA**: cache the shell, queue posts while offline, sync on reconnect.
- **Video/audio transcoding** on upload (ffmpeg optional) to normalise everything to
  one container and generate thumbnails.

## Ideas per feature

Small things that would make what already exists noticeably better.

**Feed and posts**
- Edit history ("edited 3 times", show the old versions to the author).
- "Show less like this" per author, remembered per user.
- Draft autosave in `localStorage` so a closed tab does not lose text.
- Post preview card for external links (title + description, no images).

**Messages**
- Search inside one conversation (the global search already finds them).
- Message pins inside a chat.
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
- Profile covers (the cover strip is there but has no upload).

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
- A "storage" panel: biggest files, orphaned media, files without a record.
- Per-user quota overview.
- Scheduled maintenance mode with a countdown banner.

**Everywhere**
- Keyboard shortcuts (`g f` for feed, `/` for search) with a cheat sheet.
- Dark/OLED polish: the code highlight and the call overlay still assume the light
  palette in a couple of places.
- Accessibility: focus rings on every custom button, `aria-live` for toasts.

## Done

Kept here so the list above stays honest.

- Feed with images, video links, polls, reactions, likes, reposts, pins, edits.
- Drafts and scheduled publication.
- Private messages: live delivery, typing, read receipts, replies, reactions,
  forwarding, search, pictures/GIFs, albums, voice messages, clips, file attachments.
- Groups with editable settings, pictures and member management.
- Communities: owner / editor / member roles, join requests for closed groups and
  a moderation queue ("members post, editors approve").
- Mentions, hashtags, tag pages, filtered post search.
- Notifications: live long poll, per-type mutes, filters, desktop notifications,
  live unread counters for messages, notifications and friend requests.
- Music and video library with uploads, playlists, editable titles/descriptions.
- "Save to my library": one-click copy of somebody else's track or clip.
- Gifts: a built-in catalogue sent from a profile, anonymous or signed, with
  their own block on the wall, a received/sent page and notifications.
- Flash games: upload `.swf` (admins and approved authors only), play it through a
  self-hosted Ruffle build inside a sandboxed iframe, with a shelf, search, plays
  counter and likes.
- Voice and video calls plus named conference rooms over WebRTC, with history.
- Achievements, bookmarks, photo gallery, profiles with interests and moods.
- Photo albums: named, with a cover, filled from the wall, photos outlive the album.
- Stories: photos and clips above the feed that vanish on time, with a seen-by list.
- One search over people, posts, comments, your own messages, communities and games,
  with kind tabs and date/author filters.
- Mobile layout: chip rail navigation, stacked messenger, finger-sized controls.
- Admin board: users, roles, badges, reports, logs, AI panel, settings, backups.
- DeadVK import, operator CLI, self-test, CI script.
