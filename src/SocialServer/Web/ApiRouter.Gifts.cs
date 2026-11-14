using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // =============================================================== gifts

        /// <summary>
        /// One gift as the client sees it. An anonymous gift carries no sender for
        /// anybody except the person who sent it, so even the recipient cannot
        /// work out who it came from.
        /// </summary>
        private string GiftJson(Gift g, User viewer)
        {
            GiftKind kind = GiftCatalog.Find(g.Kind);
            bool self = viewer != null && viewer.Id == g.From;

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", g.Id);
            b.Prop("kind", g.Kind);
            b.Prop("icon", kind == null ? "gift" : kind.Icon);
            b.Prop("color", kind == null ? "#7c8cff" : kind.Color);
            b.Prop("message", g.Message);
            b.Prop("anonymous", g.Anonymous);
            b.Prop("created", Util.Iso(g.Created));
            b.Prop("seen", g.Seen);
            b.Name("from").Raw(g.Anonymous && !self ? "null" : UserSummary(_store.FindUser(g.From)));
            b.Name("to").Raw(UserSummary(_store.FindUser(g.To)));
            b.EndObject();
            return b.ToString();
        }

        /// <summary>Hourly cap on gifts, per sender.</summary>
        private bool AllowGift(long userId)
        {
            int limit = _cfg.GiftPerHour;
            if (limit <= 0) return true;
            DateTime now = DateTime.UtcNow;
            lock (_actionLock)
            {
                ActionWindow w;
                string key = "gift:" + userId.ToString(CultureInfo.InvariantCulture);
                if (!_giftRate.TryGetValue(key, out w) || (now - w.StartUtc).TotalHours >= 1)
                {
                    w = new ActionWindow();
                    w.StartUtc = now;
                    w.Count = 0;
                    _giftRate[key] = w;
                }
                w.Count++;
                return w.Count <= limit;
            }
        }

        private void HandleGifts(ApiCtx ctx)
        {
            long userId = LongQuery(ctx, "user");
            User target = userId > 0 ? RequireUser(userId) : ctx.User;
            if (target == null || target.Deleted) throw new ApiException(404, "no-user", "User not found");
            if (!CanViewProfile(ctx.User, target)) throw new ApiException(403, "private", "This profile is private");

            long before = LongQuery(ctx, "before");
            int limit = LimitParam(ctx, 24, 60);
            bool hasMore;
            List<Gift> gifts = _store.GiftsTo(target.Id, before, limit, out hasMore);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("gifts").BeginArray();
            for (int i = 0; i < gifts.Count; i++) b.Raw(GiftJson(gifts[i], ctx.User));
            b.EndArray();
            b.Prop("total", _store.CountGifts(target.Id));
            b.Prop("unseen", target.Id == ctx.User.Id ? _store.CountUnseenGifts(target.Id) : 0);
            b.Prop("hasMore", hasMore);
            b.Prop("next", gifts.Count > 0 ? gifts[gifts.Count - 1].Id : 0);
            b.Prop("canSend", _cfg.GiftsEnabled && target.Id != ctx.User.Id &&
                              !_store.IsBlocked(target.Id, ctx.User.Id) && CanViewProfile(ctx.User, target) &&
                              Privacy.Allows(target, ctx.User, "gifts", _store.AreFriends(ctx.User.Id, target.Id)));
            SendOk(ctx, b);
        }

        private void HandleGiftsSent(ApiCtx ctx)
        {
            long before = LongQuery(ctx, "before");
            int limit = LimitParam(ctx, 24, 60);
            bool hasMore;
            List<Gift> gifts = _store.GiftsFrom(ctx.User.Id, before, limit, out hasMore);

            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("gifts").BeginArray();
            for (int i = 0; i < gifts.Count; i++) b.Raw(GiftJson(gifts[i], ctx.User));
            b.EndArray();
            b.Prop("hasMore", hasMore);
            b.Prop("next", gifts.Count > 0 ? gifts[gifts.Count - 1].Id : 0);
            SendOk(ctx, b);
        }

        private void HandleGiftSend(ApiCtx ctx)
        {
            if (!_cfg.GiftsEnabled) throw new ApiException(403, "gifts-off", "Gifts are switched off here");

            long to = BodyLong(ctx, "to", 0);
            User target = RequireUser(to);
            if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "You cannot gift yourself");
            if (target.Deleted) throw new ApiException(404, "no-user", "User not found");
            if (!CanViewProfile(ctx.User, target))
                throw new ApiException(403, "private", "This profile is private");
            if (!Privacy.Allows(target, ctx.User, "gifts", _store.AreFriends(ctx.User.Id, target.Id)))
                throw new ApiException(403, "gifts-closed", "This user does not accept gifts");
            if (_store.IsBlocked(target.Id, ctx.User.Id))
                throw new ApiException(403, "blocked", "This user does not accept gifts from you");

            string kind = Util.SingleLine(BodyStr(ctx, "kind", ""), 32).ToLowerInvariant();
            if (!GiftCatalog.IsKnown(kind)) throw new ApiException(400, "bad-gift", "Unknown gift");
            if (!AllowGift(ctx.User.Id))
                throw new ApiException(429, "slow-down", "That is a lot of gifts for one hour");

            string message = Util.CleanText(BodyStr(ctx, "message", ""), _cfg.MaxGiftMessage);
            bool anonymous = BodyBool(ctx, "anonymous", false);

            Gift gift = _store.AddGift(ctx.User.Id, target.Id, kind, message, anonymous);
            _store.Notify(target.Id, anonymous ? 0 : ctx.User.Id, "gift", "sent you a gift", "#/gifts");
            Log.Info("GIFT", "@" + ctx.User.Username + " -> @" + target.Username + " (" + kind + ")");

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("gift").Raw(GiftJson(gift, ctx.User));
            SendOk(ctx, b);
        }

        /// <summary>Marks one gift (or every gift) as seen; id 0 means "all".</summary>
        private void HandleGiftSeen(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            _store.MarkGiftSeen(ctx.User.Id, id);

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("unseen", _store.CountUnseenGifts(ctx.User.Id));
            SendOk(ctx, b);
        }

        /// <summary>The recipient takes a gift off their own wall.</summary>
        private void HandleGiftDelete(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            if (!_store.HideGift(ctx.User.Id, id))
                throw new ApiException(404, "no-gift", "Gift not found");

            JsonBuilder b = new JsonBuilder(128);
            b.BeginObject();
            b.Prop("total", _store.CountGifts(ctx.User.Id));
            SendOk(ctx, b);
        }
    }
}
