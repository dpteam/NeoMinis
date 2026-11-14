using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ================================================================ push

        private string PushSubJson(PushSub s)
        {
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", s.Id);
            b.Prop("agent", s.Agent);
            b.Prop("created", Util.Iso(s.Created));
            b.Prop("lastOk", s.LastOk == DateTime.MinValue ? "" : Util.Iso(s.LastOk));
            b.Prop("fails", s.Fails);
            // the endpoint itself is a capability: never hand it back
            b.EndObject();
            return b.ToString();
        }

        /// <summary>What the SPA needs to subscribe, and what the user already has.</summary>
        private void HandlePush(ApiCtx ctx)
        {
            List<PushSub> subs = _store.PushSubsFor(ctx.User.Id);
            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Prop("enabled", _cfg.PushEnabled);
            // an empty key means this server cannot sign a VAPID token at all
            b.Prop("supported", WebPush.Supported);
            b.Prop("publicKey", WebPush.Supported ? WebPush.PublicKeyBase64Url() : "");
            b.Name("subscriptions").BeginArray();
            for (int i = 0; i < subs.Count; i++) b.Raw(PushSubJson(subs[i]));
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandlePushSubscribe(ApiCtx ctx)
        {
            if (!_cfg.PushEnabled) throw new ApiException(403, "push-off", "Push notifications are switched off here");
            if (!WebPush.Supported)
                throw new ApiException(503, "push-unsupported", "This server cannot sign push messages");

            string endpoint = BodyStr(ctx, "endpoint", "").Trim();
            string p256dh = BodyStr(ctx, "p256dh", "").Trim();
            string auth = BodyStr(ctx, "auth", "").Trim();
            if (endpoint.Length == 0) throw new ApiException(400, "bad-endpoint", "Missing subscription endpoint");
            if (!Util.IsHttpUrl(endpoint)) throw new ApiException(400, "bad-endpoint", "The endpoint must be a URL");
            if (endpoint.Length > 1000) throw new ApiException(400, "bad-endpoint", "That endpoint is too long");

            List<PushSub> mine = _store.PushSubsFor(ctx.User.Id);
            if (mine.Count >= _cfg.PushMaxPerUser)
                throw new ApiException(400, "too-many", "This account already has " + _cfg.PushMaxPerUser + " browsers");

            string agent = Util.SingleLine(BodyStr(ctx, "agent", ""), 80);
            PushSub sub = _store.SavePushSub(ctx.User.Id, endpoint, p256dh, auth, agent);
            if (sub == null) throw new ApiException(503, "read-only", "Storage is read-only right now");
            Log.Info("PUSH", "@" + ctx.User.Username + " subscribed a browser (#" + sub.Id + ")");

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("subscription").Raw(PushSubJson(sub));
            SendOk(ctx, b);
        }

        private void HandlePushUnsubscribe(ApiCtx ctx)
        {
            string endpoint = BodyStr(ctx, "endpoint", "").Trim();
            bool any = _store.RemovePushSub(ctx.User.Id, endpoint);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("removed", any);
            SendOk(ctx, b);
        }

        /// <summary>Sends one tickle to this account's browsers, so a user can check it works.</summary>
        private void HandlePushTest(ApiCtx ctx)
        {
            if (!_cfg.PushEnabled) throw new ApiException(403, "push-off", "Push notifications are switched off here");
            if (!WebPush.Supported)
                throw new ApiException(503, "push-unsupported", "This server cannot sign push messages");
            List<PushSub> subs = _store.PushSubsFor(ctx.User.Id);
            if (subs.Count == 0) throw new ApiException(400, "no-subs", "This account has no browser subscribed yet");

            int sent = WebPush.SendToUser(ctx.User.Id);
            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("sent", sent);
            b.Prop("total", subs.Count);
            SendOk(ctx, b);
        }

        /// <summary>
        /// What the service worker picks up after a tickle. It arrives with the
        /// session cookie, so there is no separate token to leak. Only unread
        /// notifications are returned, and the worker skips anything it is already
        /// showing by tag, so a repeated tickle cannot stack duplicates.
        /// </summary>
        private void HandlePushPending(ApiCtx ctx)
        {
            int limit = LimitParam(ctx, 5, 20);
            List<Notification> list = _store.NotificationsFor(ctx.User.Id, 0, 40);
            JsonBuilder b = new JsonBuilder(2048);
            b.BeginObject();
            b.Name("items").BeginArray();
            int count = 0;
            for (int i = 0; i < list.Count && count < limit; i++)
            {
                Notification n = list[i];
                if (n.Read) continue;
                User actor = n.Actor > 0 ? _store.FindUser(n.Actor) : null;
                b.BeginObject()
                    .Prop("id", n.Id)
                    .Prop("type", n.Type)
                    .Prop("text", n.Text)
                    .Prop("link", n.Link)
                    .Prop("created", Util.Iso(n.Created))
                    .Name("actor").Raw(UserSummary(actor))
                    .EndObject();
                count++;
            }
            b.EndArray();
            b.Prop("unread", _store.UnreadNotifications(ctx.User.Id));
            SendOk(ctx, b);
        }
    }
}
