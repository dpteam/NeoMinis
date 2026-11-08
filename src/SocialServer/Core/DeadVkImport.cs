using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Imports a DeadVK database (tab separated .txt tables + base64 images) into this
    /// network. It is a one-way history copy: passwords cannot be reused (the old
    /// scheme is different), so imported accounts are marked as legacy and cannot log
    /// in — they exist as content and as AI personas.
    ///
    /// Everything is best effort: a broken row is counted and skipped, never fatal.
    /// </summary>
    public static class DeadVkImport
    {
        public sealed class Report
        {
            public int Users, UsersSkipped;
            public int Posts, Comments, Likes, Messages, Friends, Bookmarks, Blocks, Notifications, Images;
            public int RowsFailed;
            public readonly List<string> Warnings = new List<string>();
            public DateTime Started = DateTime.UtcNow;
            public DateTime Finished = DateTime.UtcNow;

            public string Summary()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("users ").Append(Users).Append(" (skipped ").Append(UsersSkipped).Append(")");
                sb.Append(", posts ").Append(Posts);
                sb.Append(", comments ").Append(Comments);
                sb.Append(", likes ").Append(Likes);
                sb.Append(", messages ").Append(Messages);
                sb.Append(", friends ").Append(Friends);
                sb.Append(", bookmarks ").Append(Bookmarks);
                sb.Append(", blocks ").Append(Blocks);
                sb.Append(", notifications ").Append(Notifications);
                sb.Append(", images ").Append(Images);
                if (RowsFailed > 0) sb.Append(", failed rows ").Append(RowsFailed);
                return sb.ToString();
            }
        }

        public const string Source = "deadvk";

        private sealed class Row
        {
            public string[] Fields;
            public string At(int index)
            {
                return index < Fields.Length ? Fields[index] : "";
            }
        }

        public static Report Run(Config cfg, string dbDir, bool withImages)
        {
            Report report = new Report();
            report.Started = DateTime.UtcNow;

            if (string.IsNullOrEmpty(dbDir) || !Directory.Exists(dbDir))
                throw new DirectoryNotFoundException("DeadVK database folder not found: " + dbDir);

            SocialStore store = new SocialStore(cfg);
            store.Load();
            MediaStore media = new MediaStore(cfg);

            // legacy id -> new id
            Dictionary<string, long> userMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, long> postMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // ---------------------------------------------------------- users
            List<Row> userRows = ReadTable(Path.Combine(dbDir, "users.txt"), report);
            for (int i = 0; i < userRows.Count; i++)
            {
                Row row = userRows[i];
                string legacyId = row.At(0).Trim();
                string name = Util.SingleLine(row.At(1), 40);
                if (legacyId.Length == 0 || name.Length == 0) { report.RowsFailed++; continue; }

                User existing = store.FindUserByLegacy(Source, legacyId);
                if (existing != null)
                {
                    userMap[legacyId] = existing.Id;
                    report.UsersSkipped++;
                    continue;
                }

                string username = MakeUsername(store, name);
                string status = Util.SingleLine(row.At(6), 120);
                string legacyRole = row.At(4).Trim();
                string personality = row.At(8).Trim();
                string personaType = row.At(13).Trim();

                string persona = MapPersona(personality);
                bool isBot = legacyRole.Equals("AI", StringComparison.OrdinalIgnoreCase) || persona.Length > 0;

                // imported accounts get an unusable random verifier: they cannot log in,
                // they are content and (optionally) AI personas
                string dummyVerifier = Crypto.RandomHex(32);
                string recovery = Crypto.NewRecoveryCode();

                User user;
                try
                {
                    user = store.CreateUser(username, name, dummyVerifier, recovery, "user", "legacy");
                }
                catch (Exception ex)
                {
                    report.RowsFailed++;
                    report.Warnings.Add("user " + name + ": " + ex.Message);
                    continue;
                }
                if (user == null) { report.UsersSkipped++; continue; }

                user.LegacySource = Source;
                user.LegacyId = legacyId;
                user.Status = ImportStatus(status);
                user.Email = Util.SingleLine(row.At(7), 160);
                user.Gender = MapGender(row.At(11));
                user.Birthday = MapBirthday(row.At(12));
                user.FavGames = Util.SingleLine(row.At(14), 200);
                user.FavMovies = Util.SingleLine(row.At(15), 200);
                user.FavMusic = Util.SingleLine(row.At(16), 200);
                user.FavBooks = Util.SingleLine(row.At(17), 200);
                user.Badges = Util.SingleLine(row.At(18), 300);
                user.Persona = isBot ? (persona.Length > 0 ? persona : "friendly") : "";
                user.Karma = ParseInt(row.At(22));
                user.Mood = ReadMood(Path.Combine(dbDir, "avatars"), legacyId);
                user.Privacy = "public";
                user.Created = ParseDate(row.At(10), DateTime.UtcNow);
                user.LastSeen = user.Created;

                string bio = BuildBio(personaType, row.At(19));
                if (bio.Length > 0) user.Bio = bio;

                if (withImages) user.Avatar = ImportAvatar(media, Path.Combine(dbDir, "avatars"), legacyId, report);

                store.MarkUserDirty(user);
                userMap[legacyId] = user.Id;
                report.Users++;
            }

            // ---------------------------------------------------------- posts
            List<Row> postRows = ReadTable(Path.Combine(dbDir, "posts.txt"), report);
            List<Row> commentRows = new List<Row>();

            for (int i = 0; i < postRows.Count; i++)
            {
                Row row = postRows[i];
                string legacyId = row.At(0).Trim();
                string authorLegacy = row.At(1).Trim();
                string parentLegacy = row.At(5).Trim();
                if (legacyId.Length == 0) { report.RowsFailed++; continue; }

                long authorId;
                if (!userMap.TryGetValue(authorLegacy, out authorId)) { report.RowsFailed++; continue; }

                string body = DecodeBody(row.At(3));
                string video = row.At(7).Trim();
                bool hasImage = row.At(9).Trim() == "1";
                DateTime created = ParseDate(row.At(4), DateTime.UtcNow);

                if (parentLegacy.Length > 0) { commentRows.Add(row); continue; }

                Post post;
                try
                {
                    post = store.CreatePost(authorId, 0, 0, body, "", "public");
                }
                catch (Exception ex)
                {
                    report.RowsFailed++;
                    report.Warnings.Add("post " + legacyId + ": " + ex.Message);
                    continue;
                }
                if (post == null) { report.RowsFailed++; continue; }

                post.Created = created;
                post.LegacySource = Source;
                post.LegacyId = legacyId;
                if (Util.IsHttpUrl(video)) post.Video = Util.SingleLine(video, 500);

                if (withImages && hasImage)
                {
                    string image = ImportPostImage(media, Path.Combine(dbDir, "post_images"), legacyId, report);
                    if (image != null) post.Image = image;
                }

                // DeadVK stored the like count on the post; the real pairs come from likes.txt
                postMap[legacyId] = post.Id;
                report.Posts++;
            }

            // ------------------------------------------------------- comments
            for (int i = 0; i < commentRows.Count; i++)
            {
                Row row = commentRows[i];
                string authorLegacy = row.At(1).Trim();
                string parentLegacy = row.At(5).Trim();
                long authorId, postId;
                if (!userMap.TryGetValue(authorLegacy, out authorId)) { report.RowsFailed++; continue; }
                if (!postMap.TryGetValue(parentLegacy, out postId)) { report.RowsFailed++; continue; }

                Comment c;
                try
                {
                    c = store.AddComment(postId, authorId, DecodeBody(row.At(3)));
                }
                catch { report.RowsFailed++; continue; }
                if (c == null) { report.RowsFailed++; continue; }

                c.Created = ParseDate(row.At(4), DateTime.UtcNow);
                report.Comments++;
            }

            // ---------------------------------------------------------- likes
            List<Row> likeRows = ReadTable(Path.Combine(dbDir, "likes.txt"), report);
            HashSet<string> seenLike = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < likeRows.Count; i++)
            {
                Row row = likeRows[i];
                long userId, postId;
                if (!userMap.TryGetValue(row.At(0).Trim(), out userId)) continue;
                if (!postMap.TryGetValue(row.At(1).Trim(), out postId)) continue;
                if (!seenLike.Add(userId + ":" + postId)) continue;

                Post p = store.FindPost(postId);
                if (p == null) continue;
                if (!p.Likes.Contains(userId))
                {
                    store.ToggleLike(postId, userId);
                    report.Likes++;
                }
            }

            // ------------------------------------------------------- messages
            List<Row> messageRows = ReadTable(Path.Combine(dbDir, "messages.txt"), report);
            for (int i = 0; i < messageRows.Count; i++)
            {
                Row row = messageRows[i];
                long from, to;
                if (!userMap.TryGetValue(row.At(1).Trim(), out from)) { report.RowsFailed++; continue; }
                if (!userMap.TryGetValue(row.At(2).Trim(), out to)) { report.RowsFailed++; continue; }

                Message m;
                try
                {
                    m = store.CreateMessage(from, to, DecodeBody(row.At(3)));
                }
                catch { report.RowsFailed++; continue; }
                if (m == null) { report.RowsFailed++; continue; }

                m.Created = ParseDate(row.At(4), DateTime.UtcNow);
                m.Read = row.At(5).Trim() == "1";
                report.Messages++;
            }

            // -------------------------------------------------------- friends
            List<Row> friendRows = ReadTable(Path.Combine(dbDir, "friends.txt"), report);
            for (int i = 0; i < friendRows.Count; i++)
            {
                Row row = friendRows[i];
                long from, to;
                if (!userMap.TryGetValue(row.At(0).Trim(), out from)) continue;
                if (!userMap.TryGetValue(row.At(1).Trim(), out to)) continue;
                bool accepted = row.At(3).Trim() == "1";

                try
                {
                    store.SendFriendRequest(from, to);
                    if (accepted) store.AcceptFriend(from, to);
                    report.Friends++;
                }
                catch { report.RowsFailed++; }
            }

            // ------------------------------------------------------ bookmarks
            List<Row> bookmarkRows = ReadTable(Path.Combine(dbDir, "bookmarks.txt"), report);
            for (int i = 0; i < bookmarkRows.Count; i++)
            {
                Row row = bookmarkRows[i];
                long userId, postId;
                if (!userMap.TryGetValue(row.At(0).Trim(), out userId)) continue;
                if (!postMap.TryGetValue(row.At(1).Trim(), out postId)) continue;
                if (store.ToggleBookmark(userId, postId)) report.Bookmarks++;
            }

            // ---------------------------------------------------- blacklists
            List<Row> blockRows = ReadTable(Path.Combine(dbDir, "blacklist.txt"), report);
            for (int i = 0; i < blockRows.Count; i++)
            {
                Row row = blockRows[i];
                long blocker, blocked;
                if (!userMap.TryGetValue(row.At(0).Trim(), out blocker)) continue;
                if (!userMap.TryGetValue(row.At(1).Trim(), out blocked)) continue;
                if (store.BlockUser(blocker, blocked)) report.Blocks++;
            }

            // -------------------------------------------------- notifications
            List<Row> notificationRows = ReadTable(Path.Combine(dbDir, "notifications.txt"), report);
            Dictionary<long, int> perUser = new Dictionary<long, int>();
            for (int i = 0; i < notificationRows.Count; i++)
            {
                Row row = notificationRows[i];
                long userId;
                if (!userMap.TryGetValue(row.At(1).Trim(), out userId)) continue;

                int count;
                perUser.TryGetValue(userId, out count);
                if (count >= 60) continue;   // history, not a backlog

                string text = Util.SingleLine(row.At(2), 200);
                if (text.Length == 0) continue;

                Notification n = store.Notify(userId, 0, "legacy", text, "#/notifications");
                if (n == null) continue;
                n.Created = ParseDate(row.At(4), DateTime.UtcNow);
                n.Read = row.At(5).Trim() == "1";
                perUser[userId] = count + 1;
                report.Notifications++;
            }

            store.SaveNow();
            report.Finished = DateTime.UtcNow;
            Log.Info("IMPORT", "DeadVK import finished: " + report.Summary());
            for (int i = 0; i < report.Warnings.Count && i < 20; i++) Log.Warn("IMPORT", report.Warnings[i]);
            return report;
        }

        // ============================================================ helpers

        public const string StatusMarker = "Импортировано из старого проекта DeadVK";

        /// <summary>Prefixes the legacy status, or uses the marker alone when there is none.</summary>
        public static string ImportStatus(string legacyStatus)
        {
            string s = Util.SingleLine(legacyStatus, 100);
            if (s.Length == 0) return StatusMarker;
            return StatusMarker + " · " + s;
        }

        private static string MakeUsername(SocialStore store, string displayName)
        {
            string slug = Util.Slug(displayName, 24);
            if (slug.Length < 3) slug = "user-" + Crypto.RandomHex(3);
            string candidate = slug;
            int n = 1;
            while (store.UsernameExists(candidate))
            {
                candidate = slug + "-" + n.ToString(CultureInfo.InvariantCulture);
                n++;
                if (n > 500) { candidate = slug + "-" + Crypto.RandomHex(3); break; }
            }
            return candidate;
        }

        private static string MapPersona(string personality)
        {
            string p = (personality ?? "").Trim().ToLowerInvariant();
            if (p.Length == 0) return "";
            if (p == "friendly" || p == "troll" || p == "philosopher" || p == "gamer" ||
                p == "musician" || p == "coder" || p == "doomer" || p == "hype") return p;
            if (p.Contains("troll")) return "troll";
            if (p.Contains("philos")) return "philosopher";
            if (p.Contains("gam")) return "gamer";
            if (p.Contains("music")) return "musician";
            if (p.Contains("cod")) return "coder";
            return "friendly";
        }

        private static string MapGender(string legacy)
        {
            string g = (legacy ?? "").Trim().ToLowerInvariant();
            if (g.StartsWith("муж") || g.StartsWith("m")) return "m";
            if (g.StartsWith("жен") || g.StartsWith("f")) return "f";
            if (g.Length > 0) return "other";
            return "";
        }

        private static string MapBirthday(string legacy)
        {
            // dd.MM.yyyy -> yyyy-MM-dd
            string s = (legacy ?? "").Trim();
            if (s.Length == 0) return "";
            DateTime parsed;
            if (DateTime.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return s;
            return "";
        }

        private static string BuildBio(string personaType, string relationship)
        {
            StringBuilder sb = new StringBuilder();
            string p = Util.SingleLine(personaType, 60);
            string r = Util.SingleLine(relationship, 60);
            if (p.Length > 0) sb.Append("Характер: ").Append(p);
            if (r.Length > 0 && !r.Equals("Не указано", StringComparison.OrdinalIgnoreCase))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("Отношения: ").Append(r);
            }
            return sb.ToString();
        }

        private static string DecodeBody(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Replace("\\t", "\t");
            s = s.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
            return Util.CleanText(s, 8000);
        }

        private static string ReadMood(string avatarDir, string legacyId)
        {
            try
            {
                string path = Path.Combine(avatarDir, legacyId + ".mood");
                if (!File.Exists(path)) return "";
                string mood = File.ReadAllText(path, Encoding.UTF8).Trim().ToLowerInvariant();
                if (mood == "sad" || mood == "happy" || mood == "neutral") return mood;
                return "";
            }
            catch { return ""; }
        }

        private static string ImportAvatar(MediaStore media, string avatarDir, string legacyId, Report report)
        {
            try
            {
                string path = Path.Combine(avatarDir, legacyId + ".txt");
                if (!File.Exists(path)) return "";
                string dataUrl = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (dataUrl.Length < 64) return "";
                string error;
                string url = media.SaveDataUrl(dataUrl, "avatar", 4096, out error);
                if (url == null) return "";
                report.Images++;
                return url;
            }
            catch { return ""; }
        }

        private static string ImportPostImage(MediaStore media, string imageDir, string legacyPostId, Report report)
        {
            try
            {
                string path = Path.Combine(imageDir, legacyPostId + ".txt");
                if (!File.Exists(path)) return null;
                string dataUrl = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (dataUrl.Length < 64) return null;
                string error;
                string url = media.SaveDataUrl(dataUrl, "post", 8192, out error);
                if (url == null) return null;
                report.Images++;
                return url;
            }
            catch { return null; }
        }

        private static List<Row> ReadTable(string path, Report report)
        {
            List<Row> rows = new List<Row>();
            if (!File.Exists(path))
            {
                report.Warnings.Add("missing table: " + Path.GetFileName(path));
                return rows;
            }
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim().Length == 0) continue;
                    Row row = new Row();
                    row.Fields = lines[i].Split('\t');
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add("cannot read " + Path.GetFileName(path) + ": " + ex.Message);
            }
            return rows;
        }

        private static DateTime ParseDate(string value, DateTime fallback)
        {
            return Util.ParseIso(value, fallback);
        }

        private static int ParseInt(string value)
        {
            int v;
            return int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
