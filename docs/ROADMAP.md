# Roadmap

Ideas that are not built yet, roughly in the order they make sense. Nothing here
is a promise — it is a parking lot so good ideas do not get lost.

## Next up

| Idea | Notes |
| --- | --- |
| **Flash games (Ruffle)** | Let people upload `.swf` games that run in the browser through [Ruffle](https://ruffle.rs) — the Flash emulator written in Rust/WASM, so no plugin is needed. Upload goes into the media library as a new `game` kind with its own size cap (`maxGameKB`), count cap (`maxGameFiles`) and a per-user switch, all editable in `config.ini` and Admin → Settings. Ruffle is loaded from `/assets/vendor/ruffle/` (self-hosted, no CDN, so the CSP stays strict) and only started on a dedicated `#/game/<id>` page or inside a post card. `.swf` is *never* served as an executable type: the file is handed to Ruffle as bytes, and the player runs in an iframe with `sandbox`, its own CSP and no access to the session cookie. Ratings, plays counter, comments under a game and a "Games" shelf mirror the music/video library. |
| **Playlists** | Group tracks and clips into named lists, play them in order, share a playlist link. |
| **Save to my library** | One-click "add to my music/video" from somebody else's shelf (the VK "добавить себе" flow), with the file reference counted so deleting one copy never breaks another. |
| **Gifts / stickers** | A small catalogue of images sent from a profile, shown in a "Gifts" block on the wall and in notifications. |

## Later

- Photo albums in the profile (named, with covers) instead of the flat gallery.
- Communities with roles (owner / editor / member) and moderation queue.
- Stories that expire after 24 hours.
- Full-text search across posts, comments and messages in one place, with filters.
- Push notifications in the browser (Service Worker) on top of the current long poll.
- WebDAV/S3-style offload of `/media/` for large instances.
- Import of other networks (VK export, Telegram chat export) next to the DeadVK importer.
- Two-factor authentication (TOTP) as an option per account.

## Done

Kept here so the list above stays honest.

- Feed with images, video links, polls, reactions, likes, reposts, pins, edits.
- Drafts and scheduled publication.
- Private messages: live delivery, typing, read receipts, replies, reactions,
  forwarding, search, pictures/GIFs, albums, voice messages, file attachments.
- Groups with editable settings, pictures and member management.
- Mentions, hashtags, tag pages, filtered post search.
- Notifications: live long poll, per-type mutes, filters, desktop notifications.
- Music and video library with uploads and configurable limits.
- Achievements, bookmarks, photo gallery, profiles with interests and moods.
- Admin board: users, roles, badges, reports, logs, AI panel, settings, backups.
- DeadVK import, operator CLI, self-test, CI script.
