using System;
using System.Collections.Generic;
using System.Globalization;
using Social.Core;

namespace Social.Web
{
    public sealed partial class ApiRouter
    {
        // ================================================================ AI

        private void HandleAdminAi(ApiCtx ctx)
        {
            if (!IsMethod(ctx.Req, "GET")) { RequireMethod(ctx.Req.Method, "GET"); }

            List<User> all = _store.AllUsers();
            List<User> bots = new List<User>();
            for (int i = 0; i < all.Count; i++)
            {
                User u = all[i];
                if (u.Deleted) continue;
                if (string.IsNullOrEmpty(u.Persona) && u.Role != "ai" && u.Role != "bot") continue;
                bots.Add(u);
            }

            JsonBuilder b = new JsonBuilder(8192);
            b.BeginObject();
            b.Name("config").BeginObject()
                .Prop("enabled", _cfg.AiEnabled)
                .Prop("provider", _cfg.AiProvider)
                .Prop("endpoint", _cfg.AiEndpoint)
                .Prop("model", _cfg.AiModel)
                .Prop("hasKey", !string.IsNullOrEmpty(_cfg.AiApiKey))
                .Prop("timeoutMs", _cfg.AiTimeoutMs)
                .Prop("temperature", _cfg.AiTemperature)
                .Prop("maxTokens", _cfg.AiMaxTokens)
                .Prop("intervalSec", _cfg.AiIntervalSec)
                .Prop("allowPosts", _cfg.AiAllowPosts)
                .Prop("allowComments", _cfg.AiAllowComments)
                .Prop("allowLikes", _cfg.AiAllowLikes)
                .Prop("allowMessages", _cfg.AiAllowMessages)
                .Prop("maxPostsPerHour", _cfg.AiMaxPostsPerHour)
                .Prop("maxCommentsPerHour", _cfg.AiMaxCommentsPerHour)
                .Prop("maxLikesPerHour", _cfg.AiMaxLikesPerHour)
                .Prop("maxMessagesPerHour", _cfg.AiMaxMessagesPerHour)
                .Prop("humanTargetPercent", _cfg.AiHumanTargetPercent)
                .Prop("systemPrompt", _cfg.AiSystemPrompt)
                .Prop("statusSuffix", _cfg.AiStatusSuffix)
                .EndObject();

            b.Name("status").BeginObject()
                .Prop("ready", _ai.IsReady)
                .Prop("providerLabel", _ai.ProviderLabel)
                .Prop("lastError", _ai.LastError)
                .Prop("lastErrorAt", _ai.LastErrorAt == DateTime.MinValue ? "" : Util.Iso(_ai.LastErrorAt))
                .Prop("latencyMs", _ai.LastLatencyMs)
                .Prop("generated", _ai.GeneratedCount)
                .Prop("failed", _ai.FailedCount)
                .Prop("chainSize", _ai.ChainSize)
                .Prop("chainPosts", _ai.ChainPosts)
                .Prop("actions", _aiRunner.Actions)
                .Prop("skipped", _aiRunner.Skipped)
                .Prop("consecutiveFailures", _aiRunner.ConsecutiveFailures)
                .Prop("lastAction", _aiRunner.LastAction)
                .Prop("lastActionAt", _aiRunner.LastActionAt == DateTime.MinValue ? "" : Util.Iso(_aiRunner.LastActionAt))
                .Prop("nextRun", Util.Iso(_aiRunner.NextRunUtc))
                .Prop("postsLastHour", _aiRunner.PostsLastHour)
                .Prop("commentsLastHour", _aiRunner.CommentsLastHour)
                .Prop("likesLastHour", _aiRunner.LikesLastHour)
                .Prop("messagesLastHour", _aiRunner.MessagesLastHour)
                .Prop("bots", bots.Count)
                .Prop("imported", _store.CountImported())
                .EndObject();

            b.Name("personas").BeginArray();
            for (int i = 0; i < AiEngine.Personas.Length; i++) b.Str(AiEngine.Personas[i]);
            b.EndArray();

            b.Name("bots").BeginArray();
            for (int i = 0; i < bots.Count && i < 200; i++)
            {
                User u = bots[i];
                b.BeginObject();
                b.Prop("id", u.Id);
                b.Prop("username", u.Username);
                b.Prop("displayName", u.DisplayName);
                b.Prop("avatar", u.Avatar);
                b.Prop("persona", u.Persona);
                b.Prop("mood", u.Mood);
                b.Prop("status", u.Status);
                b.Prop("legacy", u.LegacySource);
                b.Prop("posts", _store.CountPosts(u.Id));
                b.Prop("lastPost", u.LastPostAt == DateTime.MinValue ? "" : Util.Iso(u.LastPostAt));
                b.EndObject();
            }
            b.EndArray();
            SendOk(ctx, b);
        }

        private void HandleAdminAiConfig(ApiCtx ctx)
        {
            if (ctx.Body.ContainsKey("enabled")) _cfg.AiEnabled = Json.GetBool(ctx.Body, "enabled", _cfg.AiEnabled);
            if (ctx.Body.ContainsKey("provider"))
            {
                string p = BodyStr(ctx, "provider", "").Trim().ToLowerInvariant();
                if (p != "off" && p != "markov" && p != "openai")
                    throw new ApiException(400, "bad-provider", "Unknown provider");
                _cfg.AiProvider = p;
            }
            if (ctx.Body.ContainsKey("endpoint")) _cfg.AiEndpoint = BodyStr(ctx, "endpoint", _cfg.AiEndpoint).Trim();
            if (ctx.Body.ContainsKey("model")) _cfg.AiModel = BodyStr(ctx, "model", _cfg.AiModel).Trim();
            if (ctx.Body.ContainsKey("apiKey")) _cfg.AiApiKey = BodyStr(ctx, "apiKey", _cfg.AiApiKey).Trim();
            if (ctx.Body.ContainsKey("timeoutMs")) _cfg.AiTimeoutMs = Json.GetInt(ctx.Body, "timeoutMs", _cfg.AiTimeoutMs);
            if (ctx.Body.ContainsKey("temperature")) _cfg.AiTemperature = Json.GetDouble(ctx.Body, "temperature", _cfg.AiTemperature);
            if (ctx.Body.ContainsKey("maxTokens")) _cfg.AiMaxTokens = Json.GetInt(ctx.Body, "maxTokens", _cfg.AiMaxTokens);
            if (ctx.Body.ContainsKey("intervalSec")) _cfg.AiIntervalSec = Json.GetInt(ctx.Body, "intervalSec", _cfg.AiIntervalSec);
            if (ctx.Body.ContainsKey("allowPosts")) _cfg.AiAllowPosts = Json.GetBool(ctx.Body, "allowPosts", _cfg.AiAllowPosts);
            if (ctx.Body.ContainsKey("allowComments")) _cfg.AiAllowComments = Json.GetBool(ctx.Body, "allowComments", _cfg.AiAllowComments);
            if (ctx.Body.ContainsKey("allowLikes")) _cfg.AiAllowLikes = Json.GetBool(ctx.Body, "allowLikes", _cfg.AiAllowLikes);
            if (ctx.Body.ContainsKey("allowMessages")) _cfg.AiAllowMessages = Json.GetBool(ctx.Body, "allowMessages", _cfg.AiAllowMessages);
            if (ctx.Body.ContainsKey("maxPostsPerHour")) _cfg.AiMaxPostsPerHour = Json.GetInt(ctx.Body, "maxPostsPerHour", _cfg.AiMaxPostsPerHour);
            if (ctx.Body.ContainsKey("maxCommentsPerHour")) _cfg.AiMaxCommentsPerHour = Json.GetInt(ctx.Body, "maxCommentsPerHour", _cfg.AiMaxCommentsPerHour);
            if (ctx.Body.ContainsKey("maxLikesPerHour")) _cfg.AiMaxLikesPerHour = Json.GetInt(ctx.Body, "maxLikesPerHour", _cfg.AiMaxLikesPerHour);
            if (ctx.Body.ContainsKey("maxMessagesPerHour")) _cfg.AiMaxMessagesPerHour = Json.GetInt(ctx.Body, "maxMessagesPerHour", _cfg.AiMaxMessagesPerHour);
            if (ctx.Body.ContainsKey("humanTargetPercent")) _cfg.AiHumanTargetPercent = Json.GetInt(ctx.Body, "humanTargetPercent", _cfg.AiHumanTargetPercent);
            if (ctx.Body.ContainsKey("systemPrompt")) _cfg.AiSystemPrompt = Util.CleanText(BodyStr(ctx, "systemPrompt", ""), 500);
            if (ctx.Body.ContainsKey("statusSuffix")) _cfg.AiStatusSuffix = Util.SingleLine(BodyStr(ctx, "statusSuffix", ""), 60);

            // persist so a restart keeps the settings
            _cfg.SetIniValue("ai", "enabled", _cfg.AiEnabled ? "true" : "false");
            _cfg.SetIniValue("ai", "provider", _cfg.AiProvider);
            _cfg.SetIniValue("ai", "endpoint", _cfg.AiEndpoint);
            _cfg.SetIniValue("ai", "model", _cfg.AiModel);
            if (ctx.Body.ContainsKey("apiKey")) _cfg.SetIniValue("ai", "apiKey", _cfg.AiApiKey);
            _cfg.SetIniValue("ai", "timeoutMs", _cfg.AiTimeoutMs.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "temperature", _cfg.AiTemperature.ToString("0.##", CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "maxTokens", _cfg.AiMaxTokens.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "intervalSec", _cfg.AiIntervalSec.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "allowPosts", _cfg.AiAllowPosts ? "true" : "false");
            _cfg.SetIniValue("ai", "allowComments", _cfg.AiAllowComments ? "true" : "false");
            _cfg.SetIniValue("ai", "allowLikes", _cfg.AiAllowLikes ? "true" : "false");
            _cfg.SetIniValue("ai", "allowMessages", _cfg.AiAllowMessages ? "true" : "false");
            _cfg.SetIniValue("ai", "maxPostsPerHour", _cfg.AiMaxPostsPerHour.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "maxCommentsPerHour", _cfg.AiMaxCommentsPerHour.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "maxLikesPerHour", _cfg.AiMaxLikesPerHour.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "maxMessagesPerHour", _cfg.AiMaxMessagesPerHour.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "humanTargetPercent", _cfg.AiHumanTargetPercent.ToString(CultureInfo.InvariantCulture));
            _cfg.SetIniValue("ai", "systemPrompt", _cfg.AiSystemPrompt);
            _cfg.SetIniValue("ai", "statusSuffix", _cfg.AiStatusSuffix);

            Log.Info("AI", "@" + ctx.User.Username + " updated AI settings: " + _cfg.AiProvider +
                            " enabled=" + _cfg.AiEnabled + " interval=" + _cfg.AiIntervalSec + "s");

            JsonBuilder b = new JsonBuilder(256);
            b.BeginObject();
            b.Prop("provider", _cfg.AiProvider);
            b.Prop("enabled", _cfg.AiEnabled);
            b.Prop("ready", _ai.IsReady);
            SendOk(ctx, b);
        }

        private void HandleAdminAiTest(ApiCtx ctx)
        {
            string error;
            int latency;
            bool ok = _ai.Probe(out error, out latency);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("ok", ok);
            b.Prop("latencyMs", latency);
            b.Prop("error", error);
            b.Prop("provider", _cfg.AiProvider);
            b.Prop("chainSize", _ai.ChainSize);
            b.EndObject();
            SendSealed(ctx, ok ? 200 : 502, b.ToString());
        }

        private void HandleAdminAiStep(ApiCtx ctx)
        {
            string result = _aiRunner.StepOnce();
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("result", result);
            b.Prop("actions", _aiRunner.Actions);
            b.Prop("skipped", _aiRunner.Skipped);
            SendOk(ctx, b);
        }

        private void HandleAdminAiPersona(ApiCtx ctx)
        {
            long id = BodyLong(ctx, "id", 0);
            User target = RequireUser(id);

            if (ctx.Body.ContainsKey("persona"))
            {
                string p = BodyStr(ctx, "persona", "").Trim().ToLowerInvariant();
                if (p.Length > 0)
                {
                    bool known = false;
                    for (int i = 0; i < AiEngine.Personas.Length; i++) if (AiEngine.Personas[i] == p) known = true;
                    if (!known) throw new ApiException(400, "bad-persona", "Unknown persona");
                }
                target.Persona = p;
            }
            if (ctx.Body.ContainsKey("mood"))
            {
                string m = BodyStr(ctx, "mood", "").Trim().ToLowerInvariant();
                if (m != "" && m != "happy" && m != "sad" && m != "neutral")
                    throw new ApiException(400, "bad-mood", "Unknown mood");
                target.Mood = m;
            }
            if (ctx.Body.ContainsKey("status"))
                target.Status = Util.SingleLine(BodyStr(ctx, "status", ""), _cfg.MaxStatusLength);
            _store.MarkUserDirty(target);

            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Name("bot").BeginObject()
                .Prop("id", target.Id)
                .Prop("persona", target.Persona)
                .Prop("mood", target.Mood)
                .Prop("status", target.Status)
                .EndObject();
            SendOk(ctx, b);
        }

        private void HandleAdminImport(ApiCtx ctx)
        {
            // import is a CLI operation; the admin endpoint only reports the state
            JsonBuilder b = new JsonBuilder(512);
            b.BeginObject();
            b.Prop("imported", _store.CountImported());
            b.Prop("note", "Run: SocialServer.exe --import-deadvk <db folder>");
            b.Prop("provider", _cfg.AiProvider);
            SendOk(ctx, b);
        }
    }
}

