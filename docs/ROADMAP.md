# Roadmap

Ideas that are not built yet, ordered by how much work they are: a quick win is a
day, medium is a few days, large is a sprint. Nothing here is a promise — it is a
parking lot so good ideas do not get lost.

## Quick wins

Small, self-contained, no new storage.

| Idea | Notes |
| --- | --- |
| **Paste a picture from the clipboard** | Every place that accepts an image should accept a `Ctrl+V`: the composer, the chat, the avatar/cover pickers, game previews, story upload. One shared helper on `paste` (`ClipboardEvent.clipboardData.items` → `getAsFile()`), then hand the `File` to the existing `UI.fileToDataUrl`. Cheap and it makes the site feel modern. |
| **Polish + finish the translations** | `i18n.t()` already falls back `lang → en → ru → key`, so a missing key is never a raw key on screen. What is missing is content: `uk` and `lt` only have 39 keys each (the shell and the auth forms, the rest falls back to English), and there is no `pl` at all. Add `pl`, then fill `uk`/`lt` from the `ru` text. Mechanical but long; needs a native eye for the funnier strings. |

## Medium

Needs a new field or a new check in several places, but no new subsystem.

| Idea | Notes |
| --- | --- |
| **Media compression before upload** | Everything is stored exactly as it arrives today, which is why a phone photo lands as a 4 MB PNG. Compress on the client before the upload, and keep the original only for administrators. **Photos:** re-encode to JPEG (or WebP where supported) on a canvas, cap the long edge (`compress.imageMaxEdge`, default 1600), quality `compress.imageQuality` (0.82). A PNG with transparency becomes WebP (JPEG would fill the alpha with black); a GIF is left alone so it keeps animating. **Voice messages:** the browser records webm/opus already, so re-encode by lowering `MediaRecorder` bitrate (`audioBitsPerSecond`, default 32 kbps mono) and keep the original for staff. **Clips and library audio/video:** optional server-side ffmpeg pass (`--ffmpeg` path in the config) that remuxes to mp4/h264 + aac at a target bitrate, with a "quality" selector on upload (Original / High / Balanced / Small). Without ffmpeg the file is stored as is. **Admins** get a "keep original" switch and skip every limit; a per-role quota table replaces the single `maxPostImageKB`. Report the saved bytes in the response so the UI can say "12 MB → 900 KB". |
| **Gift details** | Gifts are one-line rows today ("Crown from Anonymous, 2 min ago"). Add a gift page per item: who sent what, the full note, when, whether it was opened, and a per-kind counter ("3 hearts, 1 crown"). Let the recipient hide or keep a gift from that page too, not only from the list. |

## Large

New subsystem or a real chunk of work.

### Native clients: Android and Windows

**Committed, not speculative.** Two first-party clients for NeoMinis, and the point
of both is the same: a real app is faster than a tab, survives the phone's battery
optimiser, and can ring when a call arrives. Both must let the user **pick or type an
instance address**, because the whole project is self-hosted: there is no central
server to point at.

| | Android | Windows |
| --- | --- | --- |
| **Shape** | Native app (Kotlin, single activity + Compose) | Desktop app, tray-resident |
| **Minimum** | API 24 (Android 7) | Windows 7 SP1 x64, ideally XP-compatible like the server |
| **Why native** | Push, calls in the background, share-sheet, notifications that actually arrive | Tray, global hotkey, drag-and-drop upload, clipboard paste, multiple accounts |
| **Why not a WebView** | A WebView shell would inherit every browser limitation we already have and add nothing but an icon | Same |

#### Instance selection (the part that must exist on day one)

The first screen is not a login — it is **"which server?"**.

1. **Add instance** — type `https://social.example.com`, or scan a QR code from
   Admin → Settings (the server renders one for `site.baseUrl`).
2. **Discovery** — `GET /api/handshake` is unauthenticated and already returns
   `site.name`, `shortName`, `version` and `kdfIterations`, so the client can show
   the instance's real name and logo before a single credential is typed. If that
   call fails, the address is wrong and we say so immediately.
3. **A list of saved instances**, each with its name, its avatar/logo and the last
   account used. Switching does not log you out of the other one.
4. **Never assume a default.** No hardcoded hostname in the shipped build; a build-time
   default is allowed for internal builds only.

#### What each client does in the first release

**Both:**

- Login, logout, session persistence, and the same sealed-request protocol
  (`verifier = PBKDF2(password, "social.login.v1|username")`, HMAC-signed and
  encrypted envelopes). The crypto is mirrored, not re-invented — the test vectors in
  `--selftest` are the contract.
- Feed (read + like + comment), profiles, friends, messages with live delivery,
  notifications, and a settings screen for theme and language.
- A server switcher in the settings, and "this instance is unreachable" handled as a
  first-class state, not a spinner forever.

**Android specifically:**

- **Push** via the existing Web Push surface? No — Android needs FCM, and that means a
  relay. Decision to make: either a tiny push relay that the instance opts into, or
  foreground-service polling (worse battery, zero infrastructure). Recorded here so it
  is not discovered late.
- **Calls**: WebRTC is native on Android; the signalling is the same `/api/call/*`
  long poll. Incoming call = a full-screen intent with a ringtone.
- **Share sheet**: any image or link can be posted without opening the app.
- **Offline read**: cache the last feed page and open threads.

**Windows specifically:**

- Tray icon with unread count; close to tray, not to taskbar.
- Native notifications, and the same VAPID push path the browser already uses (the
  server side exists — `Core/WebPush.cs`).
- Drag-and-drop a file onto the window to upload it, and `Ctrl+V` for an image.
- A global hotkey to open the quick composer.
- Multiple accounts **across multiple instances** in one window — the natural end
  state of the instance picker.

#### Server work this needs

The API is already close, because the browser uses the same one. Gaps to close:

| Need | Status |
| --- | --- |
| `GET /api/handshake` with site name/version | Done |
| Sealed request protocol | Done, and mirrored in JS — needs a shared test-vector file so the clients cannot drift |
| `/api/call/*` signalling | Done |
| Web Push (VAPID) | Done |
| A stable `client` hint in the user agent / a `X-Client` header, so Admin → Users can say "Android" instead of "unknown browser" | Not done |
| A QR code for the instance address in Admin → Settings | Not done |
| A per-instance "app" section in the docs with the exact base URL | Not done |

#### Sequencing

1. Shared protocol test vectors + an `X-Client` header (small, server side).
2. QR + docs so an instance owner can hand out an address.
3. Android: instance picker → login → feed → messages. Calls and push after.
4. Windows: instance picker → login → feed → messages → tray + notifications.
5. Only then: offline caches, share sheet, global hotkey, multi-account.

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
| **Screen sharing** | Done: `getDisplayMedia` with `RTCRtpSender.replaceTrack`, so the switch is instant and needs no renegotiation; the camera track is kept (disabled) so stopping the share returns to it without a new permission prompt. The tile is letterboxed rather than cropped, because cropping a slide hides the slide. |
| **Calls that survive a reload** | Done: `/api/call/active` on boot plus a re-`answer`, which makes the other side re-offer. Verified in a browser — after a reload the overlay comes back with the room name and the local tile. If the browser cannot get the microphone back, it says so and leaves the room properly instead of sitting there silent. |
| **Discord-style call page** | Done: `#/calls` is a channel sidebar plus a room view, backed by `/api/channels`. Still open: drag-to-reorder channels. |
| **Noise suppression / echo cancel** | Done: `echoCancellation`, `noiseSuppression` and `autoGainControl` on by default, each switchable from a panel in the call; changing one re-captures the microphone and swaps the track in every peer without renegotiation. |
| **Ringing when the tab is closed** | Web Push with a `call` payload, a ringtone, and an "answer" action in the notification. |
| **Group layout** | Grid that switches to speaker view, mute-all, "raise hand", per-member volume. The people strip (avatars, count, a blue dot when somebody joins) and the minimised call window are in. |
| **Call link** | `#/call/<room>` with a copy button, so a room can be pasted into a chat. |
| **Recording** | `MediaRecorder` over a mixed stream into the media library, with a visible "recording" badge and consent from every participant. |

### Visual pass: retro 2008 × modern

**Shipped:** the template and effect layers. `public/assets/css/skins.css` carries
fifteen skins — Branded, Aero, Metro, Paper, Terminal, Soft, Brutal, Neon, Warm,
Slate, Matrix, Cyberpunk, New Year, Autumn and Horror — applied by `data-skin` on
`<html>`. `public/assets/css/effects.css` adds twelve atmosphere layers on `data-fx`
(grain, scanlines, VHS, matrix rain, glitch, snow, leaves, aurora, glow, sheen, print
offset), each skin with its own default, and each skin also has its **own synth voice**
in `sound.js`, so a theme changes what the interface sounds like as well as what it
looks like. A skin overrides tokens plus a little form, never markup, so the layout,
the features and the behaviour are identical in all fifteen. The choice is per account
(`user.skin`, `user.effect`), remembered per browser, defaulted by the admin
(`ui.skin`), lockable so only staff may change it (`ui.skinLocked`), and schedulable by
date for holidays (`ui.skinSchedule`) with no client release. The server validates
every id because it lands in a DOM attribute.

`GET /api/theme` is the public surface for all of it: unauthenticated, plain JSON,
just template ids and the active schedule, so a launcher, a kiosk or a script can
follow a holiday theme. This is the foundation the rest of the visual work stands on:
a new palette or a new atmosphere is a file, not a component rewrite.

What is left of the plan is in [DESIGN-RESEARCH.md](DESIGN-RESEARCH.md). The short
version: keep the 2008 chrome (three columns, blue bar, 1px borders, density, tab
underline), add modern craft (a spacing scale, one motion scale, skeletons, a mobile
bottom bar and sheets, shared-element image opens), and refuse the things that make an
interface look generated (glass, gradient text, 3D tilt, scroll animation on the feed).

Next phases, each shippable and reviewable on its own:

1. Tokens — spacing, type, motion, `:focus-visible`, `aria-live` toasts. No visual change intended.
2. Motion primitives — enter/exit/stagger helper, sliding tab indicator, number ticker, origin-aware popovers.
3. Mobile shell — bottom tab bar, bottom sheets, modals become sheets, pull-to-refresh.
4. Loading — skeletons for post, thread row and profile; an empty-state pass.
5. Character — dithered cover fallback, a 1px emboss pass, the music toggle, the call island morph.

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
- Scheduled messages ("send at 9:00").
- Sticker uploads: user-made packs on top of the built-in two, stored like any other
  media and moderated. The built-in packs are in.

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
- Profile cover upload, calls from a profile, game previews, a VIP label that grants
  nothing, and the badge list grown from 13 to 34 (auto-granted from activity plus
  manual awards).
- README and logo: a real project mark (SVG that reads at 16 px and at 512 px, plus
  a PNG rendered from the same geometry) and a README with screenshots. The
  screenshots come from standalone demo pages that use the real `app.css` with
  invented people and posts, so the README never shows a live instance's content.
- Stickers: two built-in packs (20 SVG files that ship with the site), a tabbed
  picker next to the emoji one, one-click sending, and a message that stores only
  the sticker id — so a sticker costs bytes, not a photo, and the server refuses
  any id it does not know.
- "Delete for everyone" with a time window, and pinned messages inside a chat.
  A revoke leaves a tombstone and deletes the files; a pin shows in a small bar
  above the conversation for both sides.
- Games and apps as one hub with tabs, so a game is an app that runs in a frame.
- Calls that feel less raw: a speaking ring driven by a real audio level meter, a
  microphone badge on muted tiles, the camera switchable on mid-call (with
  renegotiation), a tile layout that does not stretch one person across the screen,
  a participant count that ignores people who declined, a minimised floating call
  window that keeps the media flowing, avatars of who is on the line with a blue dot
  when somebody joins, and rooms that no longer die under the last person in them.
- Messages, sharpened: **search inside one conversation** with a jump to the hit (it
  pages back through the history until the message is on screen), **per-conversation
  drafts** that survive switching threads, a reload and closing the tab — and the
  thread list says "Draft:" instead of showing the last delivered message. Fixed a
  real bug on the way: "load older" paged with the *newest* id as its cursor, so it
  re-fetched the page already on screen and prepended duplicates; it now pages from
  the oldest id, skips anything already rendered, and has a test that pins the
  invariant.
- The feed tells you there is something new instead of silently going stale: a poll
  on `since=<newest id>` every 25 s, and a pill that adds the posts when clicked —
  nothing moves under the reader. Notifications group by type and target, so ten
  likes on one post are one row with stacked faces, and opening it marks the whole
  group read in a single request. Notifications also page now.
- Calls, finished: **screen share** through `replaceTrack` (instant, no
  renegotiation, the camera comes back when the share stops), **calls that survive a
  page reload**, and **audio processing toggles** (echo cancellation, noise
  suppression, auto-gain) that re-capture the microphone and swap the track in every
  peer.
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
