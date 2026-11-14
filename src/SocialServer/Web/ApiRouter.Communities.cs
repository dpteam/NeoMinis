using System;
using System.Collections.Generic;
using Social.Core;

namespace Social.Web
{
    /// <summary>
    /// Communities: a group with roles (owner / editor / member), join requests
    /// for closed groups and a moderation queue for member posts.
    /// </summary>
    public sealed partial class ApiRouter
    {
        private Group RequireGroup(long id)
        {
            Group g = _store.FindGroup(id);
            if (g == null) throw new ApiException(404, "no-group", "Community not found");
            return g;
        }

        private void RequireModerator(Group g, User user)
        {
            if (!SocialStore.CanModerate(g, user))
                throw new ApiException(403, "not-moderator", "Only the community editors can do that");
        }

        private void RequireOwner(Group g, User user)
        {
            if (g.Owner != user.Id && !user.IsAdmin)
                throw new ApiException(403, "not-owner", "Only the owner can do that");
        }

        /// <summary>Tells the owner and every editor about something in the queue.</summary>
        private void NotifyGroupModerators(Group g, User actor, string text, string link)
        {
            if (g == null || actor == null) return;
            _store.Notify(g.Owner, actor.Id, "group", text, link);
            for (int i = 0; i < g.Editors.Count; i++)
            {
                if (g.Editors[i] == actor.Id) continue;
                _store.Notify(g.Editors[i], actor.Id, "group", text, link);
            }
        }

        // ------------------------------------------------------------- roles

        private void HandleGroupRole(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            long userId = BodyLong(ctx, "user", 0);
            string role = BodyStr(ctx, "role", "member").Trim().ToLowerInvariant();
            Group g = RequireGroup(id);

            if (role != "editor" && role != "member")
                throw new ApiException(400, "bad-role", "Role must be editor or member");
            if (userId <= 0) throw new ApiException(400, "bad-user", "Pick a member");
            if (userId == g.Owner) throw new ApiException(400, "owner", "The owner keeps that role");

            // an editor may promote others, but only the owner may demote an editor
            string mine = SocialStore.RoleIn(g, ctx.User.Id);
            bool staff = Perms.Has(ctx.User, Perms.Posts);
            if (!staff && mine != "owner")
            {
                if (mine != "editor") throw new ApiException(403, "not-moderator", "Only the community editors can do that");
                if (role == "member" && g.Editors.Contains(userId))
                    throw new ApiException(403, "owner-only", "Only the owner can remove an editor");
            }

            if (!g.Members.Contains(userId))
                throw new ApiException(400, "not-member", "That user is not a member");

            bool changed = _store.SetEditor(g.Id, userId, role == "editor");
            if (changed)
            {
                string text = role == "editor" ? "made you an editor in " : "removed your editor role in ";
                _store.Notify(userId, ctx.User.Id, "group", text + g.Name, "#/group/" + g.Id);
                Log.Info("GROUP", "@" + ctx.User.Username + " set #" + userId + " to " + role + " in " + g.Name);
            }

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            b.Name("members").BeginArray();
            AppendGroupMembers(b, g, ctx.User);
            b.EndArray();
            SendOk(ctx, b);
        }

        // ---------------------------------------------------------- requests

        private void HandleGroupRequest(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Group g = RequireGroup(id);
            bool cancel = BodyBool(ctx, "cancel", false);

            if (cancel)
            {
                _store.CancelJoinRequest(g.Id, ctx.User.Id);
            }
            else
            {
                if (g.Visibility != "private")
                    throw new ApiException(400, "open-group", "This community is open, just join");
                if (!_store.RequestJoin(g.Id, ctx.User.Id))
                    throw new ApiException(400, "already", "You are already a member");
                NotifyGroupModerators(g, ctx.User, "wants to join " + g.Name, "#/group/" + g.Id);
                Log.Info("GROUP", "@" + ctx.User.Username + " asked to join " + g.Name);
            }

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleGroupRequests(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            Group g = RequireGroup(id);
            RequireModerator(g, ctx.User);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("requests").BeginArray();
            for (int i = 0; i < g.Requests.Count && i < 100; i++)
            {
                User u = _store.FindUser(g.Requests[i]);
                if (u == null) continue;
                b.BeginObject();
                b.Prop("user", u.Id);
                b.Name("account").Raw(UserSummary(u));
                b.EndObject();
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleGroupAnswer(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            long userId = BodyLong(ctx, "user", 0);
            bool accept = BodyBool(ctx, "accept", false);
            Group g = RequireGroup(id);
            RequireModerator(g, ctx.User);

            if (!_store.AnswerJoinRequest(g.Id, userId, accept))
                throw new ApiException(404, "no-request", "No such request");

            _store.Notify(userId, ctx.User.Id, "group",
                (accept ? "accepted you into " : "declined your request to join ") + g.Name, "#/group/" + g.Id);
            Log.Info("GROUP", "@" + ctx.User.Username + (accept ? " accepted " : " declined ") + "#" + userId +
                     " in " + g.Name);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            SendOk(ctx, b);
        }

        // -------------------------------------------------------- moderation

        private void HandleGroupPending(ApiCtx ctx)
        {
            long id = LongQuery(ctx, "id");
            Group g = RequireGroup(id);
            RequireModerator(g, ctx.User);

            List<Post> posts = _store.PendingPosts(g.Id, 50);
            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("group").Raw(GroupView(g, ctx.User));
            AppendPosts(b, "posts", posts, ctx.User);
            SendOk(ctx, b);
        }

        private void HandleGroupReview(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            long postId = BodyLong(ctx, "post", 0);
            bool approve = BodyBool(ctx, "approve", true);
            Group g = RequireGroup(id);
            RequireModerator(g, ctx.User);

            Post p = _store.FindPost(postId);
            if (p == null || p.Group != g.Id) throw new ApiException(404, "no-post", "Post not found in this community");
            if (!p.Pending) throw new ApiException(400, "not-pending", "That post is not waiting for review");

            _store.ReviewPost(postId, approve);
            if (!approve && !string.IsNullOrEmpty(p.Image)) _media.Delete(p.Image);

            _store.Notify(p.Author, ctx.User.Id, "group",
                (approve ? "approved your post in " : "rejected your post in ") + g.Name,
                approve ? "#/post/" + p.Id : "#/group/" + g.Id);
            Log.Info("GROUP", "@" + ctx.User.Username + (approve ? " approved" : " rejected") +
                     " post #" + postId + " in " + g.Name);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("approved", approve);
            b.Prop("pending", _store.CountPending(g.Id));
            SendOk(ctx, b);
        }

        /// <summary>Member list with roles attached, used by the community page.</summary>
        private void AppendGroupMembers(JsonBuilder b, Group g, User viewer)
        {
            int shown = 0;
            for (int i = 0; i < g.Members.Count && shown < 60; i++)
            {
                User u = _store.FindUser(g.Members[i]);
                if (u == null) continue;
                b.Raw(UserView(u, viewer, false));
                shown++;
            }
        }
    }
}
