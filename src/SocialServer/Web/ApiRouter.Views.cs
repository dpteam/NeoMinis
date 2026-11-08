using System;
using System.Collections.Generic;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ============================================================== views

        private string UserSummary(User u)
        {
            if (u == null) return "null";
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("id", u.Id);
            b.Prop("username", u.Username);
            b.Prop("displayName", u.DisplayName);
            b.Prop("avatar", u.Avatar);
            b.Prop("role", u.Role);
            b.Prop("status", Util.SingleLine(u.Status, _cfg.MaxStatusLength));
            b.Prop("online", !u.HideOnline && (DateTime.UtcNow - u.LastSeen).TotalSeconds <= _cfg.OnlineWindowSec);
            b.EndObject();
            return b.ToString();
        }

        private string UserView(User u, User viewer, bool full)
        {
            if (u == null) return "null";
            bool self = viewer != null && viewer.Id == u.Id;
            bool staff = viewer != null && viewer.IsModerator;
            bool friend = viewer != null && !self && _store.AreFriends(viewer.Id, u.Id);
            FriendLink link = viewer != null && !self ? _store.Friendship(viewer.Id, u.Id) : null;
            bool online = !u.HideOnline && (DateTime.UtcNow - u.LastSeen).TotalSeconds <= _cfg.OnlineWindowSec;

            JsonBuilder b = new JsonBuilder(full ? 1024 : 512);
            b.BeginObject();
            b.Prop("id", u.Id);
            b.Prop("username", u.Username);
            b.Prop("displayName", u.DisplayName);
            b.Prop("avatar", u.Avatar);
            b.Prop("role", u.Role);
            b.Prop("status", Util.SingleLine(u.Status, _cfg.MaxStatusLength));
            b.Prop("online", self || online);
            b.Prop("isFriend", friend);
            b.Prop("isSelf", self);
            b.Prop("requestPending", viewer != null && link != null && link.Status == "pending" && link.From == viewer.Id);
            b.Prop("incomingRequest", viewer != null && link != null && link.Status == "pending" && link.To == viewer.Id);
            b.Prop("blocked", link != null && link.Status == "blocked");
            b.Prop("privacy", u.Privacy);
            b.Prop("friends", _store.CountFriends(u.Id));
            b.Prop("posts", _store.CountPosts(u.Id));
            b.Prop("canMessage", CanMessageUser(viewer, u));
            b.Prop("mood", u.Mood);
            b.Prop("legacySource", u.LegacySource);
            if (!string.IsNullOrEmpty(u.Badges)) b.Prop("badges", u.Badges);
            if (self || staff) b.Prop("lastSeen", Util.Iso(u.LastSeen));
            else if (!u.HideOnline) b.Prop("lastSeen", Util.Iso(u.LastSeen));
            else b.Prop("lastSeen", "");

            if (full)
            {
                bool showPrivate = self || staff || friend || u.Privacy == "public";
                b.Prop("bio", showPrivate ? u.Bio : "");
                b.Prop("city", showPrivate ? u.City : "");
                b.Prop("country", showPrivate ? u.Country : "");
                b.Prop("birthday", showPrivate ? u.Birthday : "");
                b.Prop("gender", showPrivate ? u.Gender : "");
                b.Prop("website", showPrivate ? u.Website : "");
                b.Prop("favGames", showPrivate ? u.FavGames : "");
                b.Prop("favMovies", showPrivate ? u.FavMovies : "");
                b.Prop("favMusic", showPrivate ? u.FavMusic : "");
                b.Prop("favBooks", showPrivate ? u.FavBooks : "");
                b.Prop("persona", showPrivate ? u.Persona : "");
                b.Prop("created", Util.Iso(u.Created));
                b.Prop("karma", u.Karma);
                b.Prop("likes", _store.CountLikesGiven(u.Id));
                b.Prop("theme", u.Theme);
                b.Prop("lang", u.Lang);
                b.Prop("sound", u.Sound);
                b.Prop("hideOnline", u.HideOnline);
                if (self || staff)
                {
                    b.Prop("email", u.Email);
                    b.Prop("banned", u.Banned);
                    b.Prop("banReason", u.BanReason);
                    b.Prop("bannedUntil", u.BannedUntil == DateTime.MinValue ? "" : Util.Iso(u.BannedUntil));
                    b.Prop("warnings", u.Warnings);
                }
            }
            b.EndObject();
            return b.ToString();
        }

        private string MeView(User u, Session s)
        {
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("user").Raw(UserView(u, u, true));
            b.Prop("unreadNotifications", _store.UnreadNotifications(u.Id));
            b.Prop("unreadMessages", _store.UnreadMessages(u.Id));
            b.Prop("friendRequests", _store.PendingRequests(u.Id).Count);
            b.Prop("sessions", _sessions.CountForUser(u.Id));
            b.Prop("isAdmin", u.Role == "admin");
            b.Prop("isModerator", u.IsModerator);
            b.Prop("storageReadOnly", _store.IsReadOnly);
            b.Prop("lastSave", _store.LastSaveUtc == DateTime.MinValue ? "" : Util.Iso(_store.LastSaveUtc));
            b.Prop("online", _store.CountOnline(_cfg.OnlineWindowSec));
            b.Prop("sessionCreated", Util.Iso(s == null ? DateTime.UtcNow : s.Created));
            b.EndObject();
            return b.ToString();
        }

        private string PostView(Post p, User viewer)
        {
            if (p == null) return "null";
            User author = _store.FindUser(p.Author);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", p.Id);
            b.Name("author").Raw(UserSummary(author));
            b.Prop("wall", p.Wall);
            b.Prop("group", p.Group);
            b.Prop("body", p.Body);
            b.Prop("image", p.Image);
            b.Prop("video", p.Video);
            b.Prop("created", Util.Iso(p.Created));
            b.Prop("edited", p.Edited == DateTime.MinValue ? "" : Util.Iso(p.Edited));
            b.Prop("visibility", p.Visibility);
            b.Prop("likes", p.Likes.Count);
            b.Prop("liked", viewer != null && p.Likes.Contains(viewer.Id));
            b.Prop("comments", p.Comments);
            b.Prop("canDelete", CanDeletePost(viewer, p));
            b.Prop("canEdit", CanEditPost(viewer, p));
            b.Prop("canPin", CanPinPost(viewer, p));
            b.Prop("canComment", viewer != null && _store.VisibleTo(viewer.Id, p));
            b.Prop("pinned", p.Pinned);
            b.Prop("bookmarked", viewer != null && _store.IsBookmarked(viewer.Id, p.Id));
            b.Prop("legacySource", p.LegacySource);
            if (p.Wall > 0)
            {
                User wallUser = _store.FindUser(p.Wall);
                if (wallUser != null) b.Name("wallUser").Raw(UserSummary(wallUser));
            }
            b.EndObject();
            return b.ToString();
        }

        private string CommentView(Comment c, User viewer)
        {
            if (c == null) return "null";
            User author = _store.FindUser(c.Author);
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", c.Id);
            b.Prop("post", c.Post);
            b.Name("author").Raw(UserSummary(author));
            b.Prop("body", c.Body);
            b.Prop("created", Util.Iso(c.Created));
            b.Prop("canDelete", CanDeleteComment(viewer, c));
            b.EndObject();
            return b.ToString();
        }

        private string MessageView(Message m, User viewer)
        {
            if (m == null) return "null";
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", m.Id);
            b.Prop("from", m.From);
            b.Prop("to", m.To);
            b.Prop("body", m.Body);
            b.Prop("created", Util.Iso(m.Created));
            b.Prop("read", m.Read);
            b.Prop("mine", viewer != null && m.From == viewer.Id);
            b.EndObject();
            return b.ToString();
        }

        private string NotificationView(Notification n, User viewer)
        {
            if (n == null) return "null";
            User actor = _store.FindUser(n.Actor);
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", n.Id);
            b.Prop("type", n.Type);
            b.Name("actor").Raw(UserSummary(actor));
            b.Prop("text", n.Text);
            b.Prop("link", n.Link);
            b.Prop("created", Util.Iso(n.Created));
            b.Prop("read", n.Read);
            b.EndObject();
            return b.ToString();
        }

        private string GroupView(Group g, User viewer)
        {
            if (g == null) return "null";
            bool member = viewer != null && (g.Members.Contains(viewer.Id) || g.Owner == viewer.Id);
            JsonBuilder b = new JsonBuilder(768);
            b.BeginObject();
            b.Prop("id", g.Id);
            b.Prop("name", g.Name);
            b.Prop("slug", g.Slug);
            b.Prop("description", g.Description);
            b.Prop("avatar", g.Avatar);
            b.Name("owner").Raw(UserSummary(_store.FindUser(g.Owner)));
            b.Prop("members", g.Members.Count);
            b.Prop("isMember", member);
            b.Prop("isOwner", viewer != null && g.Owner == viewer.Id);
            b.Prop("canManage", viewer != null && (g.Owner == viewer.Id || viewer.IsAdmin));
            b.Prop("visibility", g.Visibility);
            b.Prop("created", Util.Iso(g.Created));
            b.Prop("canPost", member && !g.Deleted);
            b.EndObject();
            return b.ToString();
        }

        private string ThreadView(ThreadInfo t, User viewer)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("other").Raw(UserSummary(t.Other));
            b.Name("last").Raw(MessageView(t.Last, viewer));
            b.Prop("unread", t.Unread);
            b.EndObject();
            return b.ToString();
        }

        // ========================================================= permissions

        private bool CanViewProfile(User viewer, User target)
        {
            if (target == null || target.Deleted) return false;
            if (viewer != null && (viewer.Id == target.Id || viewer.IsModerator)) return true;
            if (target.Privacy == "public" || string.IsNullOrEmpty(target.Privacy)) return true;
            if (viewer != null && _store.AreFriends(viewer.Id, target.Id)) return true;
            return false;
        }

        private bool CanDeletePost(User viewer, Post p)
        {
            if (viewer == null || p == null) return false;
            if (viewer.IsModerator) return true;
            if (viewer.Id == p.Author) return true;
            if (p.Wall > 0 && viewer.Id == p.Wall) return true;
            return false;
        }

        private bool CanEditPost(User viewer, Post p)
        {
            if (viewer == null || p == null || p.Deleted) return false;
            return viewer.IsModerator || viewer.Id == p.Author;
        }

        private bool CanPinPost(User viewer, Post p)
        {
            if (viewer == null || p == null || p.Deleted) return false;
            if (viewer.IsModerator || viewer.Id == p.Author) return true;
            if (p.Wall > 0 && viewer.Id == p.Wall) return true;
            if (p.Group > 0)
            {
                Group g = _store.FindGroup(p.Group);
                if (g != null && g.Owner == viewer.Id) return true;
            }
            return false;
        }

        private bool CanDeleteComment(User viewer, Comment c)
        {
            if (viewer == null || c == null) return false;
            if (viewer.IsModerator) return true;
            if (viewer.Id == c.Author) return true;
            Post p = _store.FindPost(c.Post);
            if (p == null) return false;
            if (viewer.Id == p.Author) return true;
            if (p.Wall > 0 && viewer.Id == p.Wall) return true;
            return false;
        }

        private bool CanMessageUser(User viewer, User target)
        {
            if (viewer == null || target == null || target.Deleted || target.Banned) return false;
            if (viewer.Id == target.Id) return false;
            if (_store.IsBlocked(target.Id, viewer.Id) || _store.IsBlocked(viewer.Id, target.Id)) return false;
            if (target.Privacy == "public" || string.IsNullOrEmpty(target.Privacy)) return true;
            return _store.AreFriends(viewer.Id, target.Id);
        }

        private User RequireUser(long id)
        {
            User u = _store.FindUser(id);
            if (u == null || u.Deleted) throw new ApiException(404, "no-user", "User not found");
            return u;
        }

        private Post RequirePost(long id)
        {
            Post p = _store.FindPost(id);
            if (p == null || p.Deleted) throw new ApiException(404, "no-post", "Post not found");
            return p;
        }

        private string EmptyListJson(string name)
        {
            JsonBuilder b = new JsonBuilder(64);
            b.BeginObject();
            b.Name(name).BeginArray().EndArray();
            b.EndObject();
            return b.ToString();
        }
    }
}
