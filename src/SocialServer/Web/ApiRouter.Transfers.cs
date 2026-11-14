using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // =========================================================== transfers

        /// <summary>
        /// A transfer as the client sees it. The code is only ever handed to the
        /// two people in the transfer, and the chunk payload never appears here.
        /// </summary>
        private string TransferJson(Transfer t, User viewer)
        {
            bool sender = viewer != null && viewer.Id == t.From;
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("id", t.Id);
            b.Prop("from", t.From);
            b.Prop("to", t.To);
            b.Prop("fromName", OwnerName(t.From));
            b.Prop("toName", OwnerName(t.To));
            b.Prop("fileName", t.FileName);
            b.Prop("size", t.Size);
            b.Prop("sha256", t.Sha256);
            b.Prop("state", t.State);
            b.Prop("created", Util.Iso(t.Created));
            b.Prop("expires", Util.Iso(t.Expires));
            b.Prop("finished", t.Finished == DateTime.MinValue ? "" : Util.Iso(t.Finished));
            b.Prop("chunkSize", t.ChunkSize);
            b.Prop("chunkCount", t.ChunkCount);
            b.Prop("received", t.Received);
            b.Prop("bytes", t.Bytes);
            b.Prop("error", t.Error);
            b.Prop("mine", sender);
            // the code is what the receiver has to type, so only the sender keeps it
            b.Prop("code", sender ? TransferRegistry.Pretty(t.Code) : "");
            b.EndObject();
            return b.ToString();
        }

        private void RequireTransfers()
        {
            if (_cfg.MaxTransferMB <= 0)
                throw new ApiException(403, "transfers-off", "File transfer is switched off here");
        }

        private Transfer RequireTransfer(long id, User viewer)
        {
            Transfer t = TransferRegistry.Find(id);
            if (t == null) throw new ApiException(404, "no-transfer", "Transfer not found");
            if (t.From != viewer.Id && t.To != viewer.Id)
                throw new ApiException(403, "not-yours", "That transfer is not yours");
            return t;
        }

        private void HandleTransferStart(ApiCtx ctx)
        {
            RequireTransfers();
            long to = BodyLong(ctx, "to", 0);
            User target = RequireUser(to);
            if (target.Id == ctx.User.Id) throw new ApiException(400, "self", "You cannot send to yourself");
            // friends only: a code alone must never be enough to reach a stranger
            if (!_store.AreFriends(ctx.User.Id, target.Id))
                throw new ApiException(403, "not-friend", "You can only send files to friends - add them first");
            if (!CanMessageUser(ctx.User, target))
                throw new ApiException(403, "cannot-message", "You cannot send to this user");

            string name = Util.SingleLine(BodyStr(ctx, "name", ""), 160);
            if (name.Length == 0) throw new ApiException(400, "bad-name", "The file needs a name");
            long size = BodyLong(ctx, "size", 0);
            long maxBytes = (long)_cfg.MaxTransferMB * 1024 * 1024;
            if (size <= 0) throw new ApiException(400, "bad-size", "Unknown file size");
            if (size > maxBytes)
                throw new ApiException(400, "too-large", "The file is larger than " + _cfg.MaxTransferMB + " MB");

            string sha = BodyStr(ctx, "sha256", "").Trim().ToLowerInvariant();
            if (sha.Length != 0 && !Crypto.IsHex(sha, 64, 64))
                throw new ApiException(400, "bad-hash", "Bad SHA-256");

            Transfer t = TransferRegistry.Create(ctx.User.Id, target.Id, name, size, sha,
                                                 _cfg.TransferTtlMin, maxBytes);
            if (t == null) throw new ApiException(429, "busy", "Too many open transfers, finish one first");

            _store.Notify(target.Id, ctx.User.Id, "transfer",
                "wants to send you " + name, "#/im/" + ctx.User.Id);
            Log.Info("XFER", "@" + ctx.User.Username + " opened transfer #" + t.Id + " to @" + target.Username);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleTransferList(ApiCtx ctx)
        {
            RequireTransfers();
            List<Transfer> list = TransferRegistry.ForUser(ctx.User.Id, 20);
            JsonBuilder b = new JsonBuilder(4096);
            b.BeginObject();
            b.Name("transfers").BeginArray();
            for (int i = 0; i < list.Count; i++) b.Raw(TransferJson(list[i], ctx.User));
            b.EndArray();
            // offers waiting for the code, so the chat can show a hint
            List<Transfer> offers = TransferRegistry.OffersFor(ctx.User.Id);
            b.Prop("offers", offers.Count);
            SendOk(ctx, b);
        }

        private void HandleTransferGet(ApiCtx ctx)
        {
            RequireTransfers();
            long id = LongQuery(ctx, "id");
            Transfer t = RequireTransfer(id, ctx.User);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        /// <summary>The receiver types the code the sender read out loud.</summary>
        private void HandleTransferClaim(ApiCtx ctx)
        {
            RequireTransfers();
            // a code is eight digits, so guessing is cheap for an attacker and
            // pointless for a user; cap it well above any honest retry
            if (!Allow(ctx.User.Id, "xfer-claim", 20, 600))
                throw new ApiException(429, "slow-down", "Too many attempts, try later");
            string code = BodyStr(ctx, "code", "");
            Transfer t = TransferRegistry.ByCodeFor(code, ctx.User.Id);
            if (t == null)
            {
                // Tell the sender they typed their own code - they own it, so that
                // leaks nothing. Everybody else gets the same flat "no such code":
                // a different answer for a real-but-foreign code would let somebody
                // fishing for codes learn which ones exist.
                string why = TransferRegistry.WhyCode(code, ctx.User.Id);
                if (why == "own")
                    throw new ApiException(400, "own-code", "That is your own code - give it to your friend, they type it");
                throw new ApiException(404, "bad-code", "No open transfer with that code");
            }
            if (!TransferRegistry.Claim(t.Id, ctx.User.Id))
                throw new ApiException(400, "already-claimed", "That transfer was already opened");
            Log.Info("XFER", "@" + ctx.User.Username + " claimed transfer #" + t.Id);

            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleTransferAccept(ApiCtx ctx)
        {
            RequireTransfers();
            long id = BodyLong(ctx, "id", 0);
            Transfer t = RequireTransfer(id, ctx.User);
            if (t.To != ctx.User.Id) throw new ApiException(403, "not-yours", "That transfer is not yours");

            int chunkSize = _cfg.TransferChunkKB * 1024;
            if (chunkSize <= 0) chunkSize = 256 * 1024;
            int count = (int)((t.Size + chunkSize - 1) / chunkSize);
            if (count <= 0) count = 1;
            if (!TransferRegistry.Accept(id, ctx.User.Id, chunkSize, count))
                throw new ApiException(400, "bad-state", "That transfer cannot be accepted now");

            Log.Info("XFER", "@" + ctx.User.Username + " accepted transfer #" + id);
            JsonBuilder b = new JsonBuilder(1024);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleTransferDecline(ApiCtx ctx)
        {
            RequireTransfers();
            long id = BodyLong(ctx, "id", 0);
            Transfer t = RequireTransfer(id, ctx.User);
            if (!TransferRegistry.Decline(id, ctx.User.Id))
                throw new ApiException(400, "bad-state", "That transfer cannot be refused now");
            _store.Notify(t.From, ctx.User.Id, "transfer", "refused your file " + t.FileName,
                          "#/im/" + ctx.User.Id);
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        private void HandleTransferCancel(ApiCtx ctx)
        {
            RequireTransfers();
            long id = BodyLong(ctx, "id", 0);
            Transfer t = RequireTransfer(id, ctx.User);
            if (!TransferRegistry.Cancel(id, ctx.User.Id))
                throw new ApiException(400, "bad-state", "That transfer is already over");
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }

        /// <summary>The sender pushes one chunk as base64.</summary>
        private void HandleTransferChunkPut(ApiCtx ctx)
        {
            RequireTransfers();
            long id = BodyLong(ctx, "id", 0);
            Transfer t = RequireTransfer(id, ctx.User);
            if (t.From != ctx.User.Id) throw new ApiException(403, "not-yours", "Only the sender uploads");
            int index = (int)BodyLong(ctx, "index", -1);
            if (index < 0) throw new ApiException(400, "bad-index", "Missing chunk index");

            string data = BodyStr(ctx, "data", "");
            if (data.Length == 0) throw new ApiException(400, "empty", "Empty chunk");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(data); }
            catch (FormatException) { throw new ApiException(400, "bad-chunk", "Chunk is not base64"); }

            long maxBytes = (long)_cfg.MaxTransferMB * 1024 * 1024;
            if (!TransferRegistry.Put(id, ctx.User.Id, index, bytes, maxBytes))
                throw new ApiException(400, "rejected", "Chunk refused");

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("received", t.Received);
            b.Prop("chunkCount", t.ChunkCount);
            SendOk(ctx, b);
        }

        /// <summary>The receiver pulls one chunk as base64.</summary>
        private void HandleTransferChunkGet(ApiCtx ctx)
        {
            RequireTransfers();
            long id = LongQuery(ctx, "id");
            Transfer t = RequireTransfer(id, ctx.User);
            if (t.To != ctx.User.Id) throw new ApiException(403, "not-yours", "Only the receiver downloads");
            int index = (int)LongQuery(ctx, "index");
            byte[] data = TransferRegistry.Take(id, ctx.User.Id, index);
            if (data == null) throw new ApiException(404, "no-chunk", "That chunk is not here yet");

            JsonBuilder b = new JsonBuilder(data.Length * 2 + 128);
            b.BeginObject();
            b.Prop("index", index);
            b.Prop("data", Convert.ToBase64String(data));
            SendOk(ctx, b);
        }

        /// <summary>
        /// The receiver reports the outcome: the hash matched or it did not. Either
        /// way the payload is dropped, which is the whole promise of the feature.
        /// </summary>
        private void HandleTransferFinish(ApiCtx ctx)
        {
            RequireTransfers();
            long id = BodyLong(ctx, "id", 0);
            Transfer t = RequireTransfer(id, ctx.User);
            bool ok = BodyBool(ctx, "ok", true);
            string error = Util.SingleLine(BodyStr(ctx, "error", ""), 200);
            if (!TransferRegistry.Finish(id, ctx.User.Id, ok, error))
                throw new ApiException(400, "bad-state", "That transfer is already over");

            if (ok) _store.Notify(t.From, ctx.User.Id, "transfer", "received your file " + t.FileName,
                                  "#/im/" + ctx.User.Id);
            Log.Info("XFER", "@" + ctx.User.Username + " finished transfer #" + id + (ok ? " ok" : " failed"));
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("transfer").Raw(TransferJson(t, ctx.User));
            SendOk(ctx, b);
        }
    }
}
