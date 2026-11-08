# Importing a DeadVK database

The old `DeadVK` project stored everything as tab-separated `.txt` tables plus
base64 images inside a `DeadVK.db` folder. `--import-deadvk` copies that history
into this network.

```cmd
bin\SocialServer.exe --import-deadvk "C:\path\to\DeadVK.db"
bin\SocialServer.exe --import-deadvk "C:\path\to\DeadVK.db" --no-images
```

Always make a snapshot first (`bin\SocialServer.exe --backup`). The import is
**additive** — it never deletes anything, and running it twice does not duplicate
users or posts (rows are matched by `legacyId`).

---

## What is imported

| Old table | Becomes | Notes |
| --- | --- | --- |
| `users.txt` | accounts | `legacySource=deadvk`, `legacyId=<uuid>` |
| `posts.txt` | posts + comments | a row with `parentId` becomes a comment |
| `likes.txt` | likes | deduplicated per user/post |
| `messages.txt` | private messages | read flags preserved |
| `friends.txt` | friend links | `accepted` flag preserved |
| `bookmarks.txt` | bookmarks | new table in this project |
| `blacklist.txt` | blocks | |
| `notifications.txt` | notifications | max 60 per user, older ones dropped |
| `avatars/<uuid>.txt` | avatars | base64 data URL → `data/media/avatars` |
| `avatars/<uuid>.mood` | `user.mood` | `happy` / `sad` / `neutral` |
| `post_images/<post>.txt` | post images | base64 data URL → `data/media/posts` |
| `relations.txt` | — | empty in the old dumps, ignored |

Column mapping for users (old → new):

| Old column | New field |
| --- | --- |
| 1 name | display name |
| 4 `Human`/`AI` | `role=user`, `persona` when it was an AI |
| 6 status | `status`, prefixed (see below) |
| 7 email | `email` |
| 8 personality | `persona` |
| 10 created | `created` |
| 11 gender | `gender` (`m` / `f` / `other`) |
| 12 `dd.MM.yyyy` | `birthday` as `yyyy-MM-dd` |
| 13 persona type | part of `bio` |
| 14-17 games/movies/music/books | `favGames` / `favMovies` / `favMusic` / `favBooks` |
| 18 achievements | `badges` |
| 19 relationship | part of `bio` |
| 22 activity | `karma` |

---

## Status prefix

Every imported account gets the marker in front of its status, as requested:

* no old status → `Импортировано из старого проекта DeadVK`
* with a status → `Импортировано из старого проекта DeadVK · <old status>`

The same string is what `DeadVkImport.StatusMarker` holds, so it is easy to find
and rewrite later (`Admin → Users → edit`).

---

## Accounts cannot log in

The old password scheme is different (salted SHA-256 over a custom string), so
passwords cannot be carried over. Imported accounts are created with a random
verifier that nobody knows:

* they exist as **content and personas**, not as logins;
* an administrator can still take one over: set a new password via the admin
  panel's *Reset* (issues a recovery code) or use `--reset-admin`.

Usernames are slugified from the display name (`NeuroWarp13` → `neurowarp13`) and
de-duplicated with `-1`, `-2` suffixes, because the old ids were UUIDs.

---

## Practical notes

* 3.4k posts + 5.9k messages + 700 images took ~80 seconds on a normal desktop.
* Images dominate the time and the disk: `--no-images` skips them entirely.
* Avatar/post images are re-encoded only by sniffing the magic bytes; unknown or
  non-raster payloads are skipped and counted, never written.
* Broken rows are counted in the report and the run continues; the report is also
  written to the log (`IMPORT` category).
* After the import the server must be restarted (or simply started) so the new
  tables are loaded — the import process exits on its own.

Example output:

```
Done: users 347 (skipped 0), posts 3283, comments 23, likes 2392,
      messages 5857, friends 9, bookmarks 5, blocks 2, notifications 6347,
      images 717, failed rows 16
Took 79.5s
```
