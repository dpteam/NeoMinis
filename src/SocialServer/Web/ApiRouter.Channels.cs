using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ============================================================ channels

        /// <summary>
        /// A channel plus who is inside it right now. The occupancy is read from
        /// the live call registry, never from storage: a room is only "occupied"
        /// while browsers are actually connected, and pretending otherwise after a
        /// restart would send people into an empty room.
        /// </summary>
        private string ChannelJson(Channel c, User viewer)
        {
            List<LiveCall> live = CallRegistry.ByRoom(c.Name);
            List<User> inside = new List<User>();
            for (int i = 0; i < live.Count; i++)
            {
                LiveCall call = live[i];
                if (call.IsEnded) continue;
                for (int k = 0; k < call.Members.Count; k++)
                {
                    CallMember m = call.Members[k];
                    if (!m.Accepted) continue;
                    User u = _store.FindUser(m.User);
                    if (u == null || u.Deleted) continue;
                    bool seen = false;
                    for (int s = 0; s < inside.Count; s++) if (inside[s].Id == u.Id) { seen = true; break; }
                    if (!seen) inside.Add(u);
                }
            }

            bool mine = viewer != null && (c.CreatedBy == viewer.Id);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Prop("id", c.Id);
            b.Prop("name", c.Name);
            b.Prop("topic", c.Topic);
            b.Prop("createdBy", c.CreatedBy);
            b.Prop("created", Util.Iso(c.Created));
            b.Prop("position", c.Position);
            b.Prop("inside", inside.Count);
            b.Prop("canManage", mine || (viewer != null && viewer.IsAdmin));
            b.Name("members").BeginArray();
            for (int i = 0; i < inside.Count; i++) b.Raw(UserSummary(inside[i]));
            b.EndArray();
            b.EndObject();
            return b.ToString();
        }

        /// <summary>
        /// Channel names double as the room name the call registry uses, so they
        /// have to obey the same rules: lowercase, letters/digits/underscore.
        /// </summary>
        private static string CleanChannelName(string raw)
        {
            string name = Util.SingleLine(raw ?? "", 40).Trim().ToLowerInvariant();
            name = name.Replace(' ', '-');
            return name;
        }

        private void RequireChannelName(string name)
        {
            if (name.Length < 3) throw new ApiException(400, "bad-name", "A channel needs a name of 3+ characters");
            if (name.Length > 40) throw new ApiException(400, "bad-name", "A channel name is 40 characters at most");
            // A channel name doubles as the room name, so it is deliberately a
            // slug: lowercase letters, digits, hyphen, underscore and dot. Discord
            // does the same, and a slug can be pasted into a URL without escaping.
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                          c == '-' || c == '_' || c == '.';
                if (!ok) throw new ApiException(400, "bad-name",
                    "Use lowercase letters, digits, - _ or . for the channel name");
            }
            if (name[0] == '-' || name[0] == '.' || name[name.Length - 1] == '-' || name[name.Length - 1] == '.')
                throw new ApiException(400, "bad-name", "A channel name cannot start or end with - or .");
        }

        private void HandleChannels(ApiCtx ctx)
        {
            CallRegistry.Sweep();
            List<Channel> channels = _store.Channels();
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("channels").BeginArray();
            for (int i = 0; i < channels.Count; i++) b.Raw(ChannelJson(channels[i], ctx.User));
            b.EndArray();
            b.Prop("maxMembers", _cfg.MaxCallMembers);
            // the page can tell a first-time visitor what the place is for
            b.Prop("canCreate", _cfg.MaxCallMembers >= 2);
            SendOk(ctx, b);
        }

        private void HandleChannelCreate(ApiCtx ctx)
        {
            if (_cfg.MaxCallMembers < 2)
                throw new ApiException(403, "calls-off", "Calls are switched off here");
            string name = CleanChannelName(BodyStr(ctx, "name", ""));
            RequireChannelName(name);
            if (_store.FindChannel(name) != null)
                throw new ApiException(409, "name-taken", "A channel with that name already exists");
            string topic = Util.SingleLine(BodyStr(ctx, "topic", ""), 120);

            Channel c = _store.CreateChannel(ctx.User.Id, name, topic);
            if (c == null) throw new ApiException(503, "read-only", "Storage is read-only right now");
            Log.Info("CALL", "@" + ctx.User.Username + " created channel #" + c.Name);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("channel").Raw(ChannelJson(c, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleChannelEdit(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Channel c = _store.FindChannel(id);
            if (c == null) throw new ApiException(404, "no-channel", "Channel not found");
            if (c.CreatedBy != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-yours", "Only the creator or an administrator can edit this channel");

            string name = c.Name;
            if (ctx.Body.ContainsKey("name"))
            {
                name = CleanChannelName(BodyStr(ctx, "name", c.Name));
                RequireChannelName(name);
                Channel other = _store.FindChannel(name);
                if (other != null && other.Id != c.Id)
                    throw new ApiException(409, "name-taken", "A channel with that name already exists");
                if (name != c.Name && CallRegistry.ByRoom(c.Name).Count > 0)
                    throw new ApiException(409, "in-use", "Somebody is in this channel right now; rename it later");
            }

            string topic = ctx.Body.ContainsKey("topic")
                ? Util.SingleLine(BodyStr(ctx, "topic", ""), 120)
                : c.Topic;
            int position = ctx.Body.ContainsKey("position") ? Json.GetInt(ctx.Body, "position", c.Position) : -1;
            if (!_store.UpdateChannel(id, name, topic, position))
                throw new ApiException(404, "no-channel", "Channel not found");
            Log.Info("CALL", "@" + ctx.User.Username + " edited channel #" + name);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("channel").Raw(ChannelJson(c, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleChannelDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            Channel c = _store.FindChannel(id);
            if (c == null) throw new ApiException(404, "no-channel", "Channel not found");
            if (c.CreatedBy != ctx.User.Id && !ctx.User.IsAdmin)
                throw new ApiException(403, "not-yours", "Only the creator or an administrator can delete this channel");

            // People inside are not thrown out: the room they joined lives in the
            // call registry and keeps working until they leave. Deleting the row
            // only removes the sign from the wall.
            List<LiveCall> live = CallRegistry.ByRoom(c.Name);
            int inside = 0;
            for (int i = 0; i < live.Count; i++) if (!live[i].IsEnded) inside += live[i].AcceptedCount;

            if (!_store.DeleteChannel(id)) throw new ApiException(404, "no-channel", "Channel not found");
            Log.Info("CALL", "@" + ctx.User.Username + " deleted channel #" + c.Name +
                             (inside > 0 ? " (" + inside + " still inside)" : ""));

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("deleted", true);
            b.Prop("inside", inside);
            SendOk(ctx, b);
        }
    }
}
