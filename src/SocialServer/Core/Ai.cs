using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// AI "citizens" for imported bot accounts.
    ///
    /// Providers:
    ///   off    - the simulation does nothing
    ///   markov - a local bigram/trigram chain trained on existing posts, no network,
    ///            no dependencies, works on XP
    ///   openai - any OpenAI-compatible /chat/completions endpoint
    ///            (LM Studio, Ollama with /v1, llama.cpp server, OpenAI, OpenRouter...)
    ///
    /// Personas, moods and the anger level come from the old DeadVK database; they are
    /// stored per user and can be edited in the admin board.
    /// </summary>
    public sealed class AiEngine
    {
        private readonly Config _cfg;
        private readonly SocialStore _store;
        private readonly Random _rnd = new Random(Environment.TickCount);
        private readonly object _sync = new object();

        private readonly Dictionary<string, List<string>> _chain = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private DateTime _chainBuilt = DateTime.MinValue;
        private int _chainPosts;
        private string _lastError = "";
        private DateTime _lastErrorAt = DateTime.MinValue;
        private int _lastLatencyMs;
        private long _generated;
        private long _failed;

        public AiEngine(Config cfg, SocialStore store)
        {
            _cfg = cfg;
            _store = store;
            EnableModernTls();
        }

        /// <summary>
        /// .NET 4.0 defaults to SSL3/TLS1.0, which every current API rejects. TLS 1.2 is
        /// enum value 3072 there, so it has to be set numerically.
        /// </summary>
        public static void EnableModernTls()
        {
            try
            {
                const SecurityProtocolType tls12 = (SecurityProtocolType)3072;
                const SecurityProtocolType tls11 = (SecurityProtocolType)768;
                ServicePointManager.SecurityProtocol = tls12 | tls11 | SecurityProtocolType.Tls;
            }
            catch { }
        }

        public string LastError { get { lock (_sync) { return _lastError; } } }
        public DateTime LastErrorAt { get { lock (_sync) { return _lastErrorAt; } } }
        public int LastLatencyMs { get { lock (_sync) { return _lastLatencyMs; } } }
        public long GeneratedCount { get { lock (_sync) { return _generated; } } }
        public long FailedCount { get { lock (_sync) { return _failed; } } }
        public int ChainSize { get { lock (_sync) { return _chain.Count; } } }
        public int ChainPosts { get { lock (_sync) { return _chainPosts; } } }

        public bool IsReady
        {
            get
            {
                if (!_cfg.AiEnabled) return false;
                if (_cfg.AiProvider == "markov") return true;
                if (_cfg.AiProvider == "openai") return !string.IsNullOrEmpty(_cfg.AiEndpoint);
                return false;
            }
        }

        public string ProviderLabel
        {
            get
            {
                if (_cfg.AiProvider == "markov") return "Markov chain (local)";
                if (_cfg.AiProvider == "openai") return "OpenAI-compatible: " + _cfg.AiModel;
                return "disabled";
            }
        }

        // ============================================================ personas

        public static readonly string[] Personas = new string[]
        {
            "friendly", "troll", "philosopher", "gamer", "musician", "coder", "doomer", "hype"
        };

        private static string PersonaPrompt(string persona)
        {
            switch (persona)
            {
                case "troll":
                    return "Ты тролль. Подкалываешь, но без травли и без оскорблений по признакам. Коротко, с иронией.";
                case "philosopher":
                    return "Ты философ. Рассуждаешь о смысле, времени и людях. Спокойно, немного отстранённо.";
                case "gamer":
                    return "Ты геймер. Говоришь про игры, билды, тайминги и лаги. Живо, с жаргоном.";
                case "musician":
                    return "Ты меломан. Обсуждаешь треки, жанры, звук и наушники.";
                case "coder":
                    return "Ты программист. Говоришь про код, баги, дедлайны. Сухо и по делу.";
                case "doomer":
                    return "Ты уставший от всего человек. Пишешь коротко, с лёгкой тоской, но без нытья и без угроз.";
                case "hype":
                    return "Ты очень воодушевлённый человек. Восхищаешься мелочами, ставишь восклицательные знаки.";
                default:
                    return "Ты дружелюбный человек. Пишешь тепло, по-человечески, без пафоса.";
            }
        }

        private static string MoodPrompt(string mood)
        {
            if (mood == "happy") return " Настроение приподнятое.";
            if (mood == "sad") return " Настроение подавленное, но без драмы.";
            return "";
        }

        // ======================================================== generation

        /// <summary>Generates one short post for the given bot. Returns "" on failure.</summary>
        public string GeneratePost(User bot, out string error)
        {
            error = "";
            if (!IsReady) { error = "AI is disabled"; return ""; }
            string topic = PickTopic();
            string prompt = "Напиши короткую запись для соцсети (1-2 предложения, до 220 символов) на тему: " + topic +
                            ". Пиши по-русски, живым языком, без хештегов и без списков. " +
                            "Ответь только готовым текстом записи, без рассуждений и без пояснений.";
            return Generate(bot, prompt, out error);
        }

        /// <summary>Generates one short comment for a post.</summary>
        public string GenerateComment(User bot, Post post, out string error)
        {
            error = "";
            if (!IsReady) { error = "AI is disabled"; return ""; }
            string excerpt = Util.SingleLine(post.Body, 180);
            string prompt = "Прокомментируй запись одним коротким предложением (до 140 символов). " +
                            "Запись: \"" + excerpt + "\". Ответь по-русски, по существу, без хештегов. " +
                            "Только текст комментария, без рассуждений.";
            return Generate(bot, prompt, out error);
        }

        /// <summary>Generates a private message opener.</summary>
        public string GenerateMessage(User bot, User target, out string error)
        {
            error = "";
            if (!IsReady) { error = "AI is disabled"; return ""; }
            string prompt = "Напиши короткое личное сообщение (до 140 символов) пользователю по имени " +
                            Util.SingleLine(target.DisplayName, 40) + ". По-русски, дружелюбно, без вопросов о личных данных. " +
                            "Только текст сообщения.";
            return Generate(bot, prompt, out error);
        }

        private string Generate(User bot, string prompt, out string error)
        {
            error = "";
            string persona = string.IsNullOrEmpty(bot.Persona) ? "friendly" : bot.Persona.ToLowerInvariant();
            string system = PersonaPrompt(persona) + MoodPrompt(bot.Mood);
            if (!string.IsNullOrEmpty(_cfg.AiSystemPrompt)) system = _cfg.AiSystemPrompt + " " + system;

            string text = _cfg.AiProvider == "markov"
                ? GenerateMarkov()
                : GenerateRemote(system, prompt, out error);

            if (string.IsNullOrEmpty(text))
            {
                if (string.IsNullOrEmpty(error)) error = "empty response";
                lock (_sync)
                {
                    _failed++;
                    _lastError = error;
                    _lastErrorAt = DateTime.UtcNow;
                }
                Log.Warn("AI", "generation failed: " + error);
                return "";
            }

            text = CleanOutput(text, 600);
            if (text.Length == 0)
            {
                error = "empty after cleanup";
                return "";
            }

            lock (_sync)
            {
                _generated++;
                _lastError = "";
            }
            return text;
        }

        /// <summary>Strips wrapping quotes, hashtags, markdown noise and enforces a length cap.</summary>
        public static string CleanOutput(string raw, int maxLen)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim();

            // drop a leading "assistant:" style prefix
            int colon = s.IndexOf(':');
            if (colon > 0 && colon < 20)
            {
                string head = s.Substring(0, colon).ToLowerInvariant();
                if (head.Contains("assistant") || head.Contains("ответ")) s = s.Substring(colon + 1).Trim();
            }

            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            if (s.Length >= 2 && s[0] == '\u00ab' && s[s.Length - 1] == '\u00bb') s = s.Substring(1, s.Length - 2);

            s = s.Replace("**", "").Replace("```", "");
            while (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1).TrimStart();

            // remove hashtags
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '#')
                {
                    int j = i + 1;
                    while (j < s.Length && !char.IsWhiteSpace(s[j])) j++;
                    i = j - 1;
                    continue;
                }
                sb.Append(c);
            }
            s = sb.ToString();

            s = Util.CleanText(s, maxLen);
            // never let a generated message look like markup or a link farm
            s = s.Replace("<", "(").Replace(">", ")");
            return s.Trim();
        }

        /// <summary>Pulls the last quoted/final sentence out of a reasoning blob.</summary>
        private static string LastSentence(string reasoning)
        {
            if (string.IsNullOrEmpty(reasoning)) return "";
            string s = reasoning.Trim();
            // a quoted final answer is the best signal
            int open = s.LastIndexOf('"');
            if (open > 0)
            {
                int close = s.LastIndexOf('"', open - 1);
                if (close >= 0 && open - close > 12)
                {
                    string quoted = s.Substring(close + 1, open - close - 1).Trim();
                    if (quoted.Length >= 12) return quoted;
                }
            }
            // otherwise take the last sentence-ish chunk
            int cut = -1;
            for (int i = s.Length - 1; i > 0; i--)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n')
                {
                    if (s.Length - i > 15) { cut = i + 1; break; }
                }
            }
            if (cut > 0 && s.Length - cut > 12) return s.Substring(cut).Trim();
            return "";
        }

        private static readonly string[] Topics = new string[]        {
            "утро и кофе", "погода за окном", "любимая игра", "музыка в наушниках", "поездка в другой город",
            "старый компьютер", "бессонница", "новый фильм", "кот на клавиатуре", "дождь и окно",
            "работа над проектом", "выходные", "книга на ночь", "странная мысль", "плейлист дня",
            "разговор в чате", "очередь в магазине", "закат", "тренировка", "сон про будущее"
        };

        private string PickTopic()
        {
            lock (_sync) { return Topics[_rnd.Next(Topics.Length)]; }
        }

        // ---------------------------------------------------------- markov

        /// <summary>Rebuilds the word chain from recent posts. Cheap enough to do lazily.</summary>
        public void RebuildChain()
        {
            Dictionary<string, List<string>> chain = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<Post> posts = _store.RecentPosts(400);
            int used = 0;

            for (int i = 0; i < posts.Count; i++)
            {
                string body = posts[i].Body;
                if (string.IsNullOrEmpty(body)) continue;
                string[] words = Tokenize(body);
                if (words.Length < 4) continue;
                used++;

                string prev = "";
                for (int w = 0; w < words.Length; w++)
                {
                    List<string> next;
                    if (!chain.TryGetValue(prev, out next))
                    {
                        next = new List<string>(4);
                        chain[prev] = next;
                    }
                    if (next.Count < 64) next.Add(words[w]);
                    prev = words[w];
                }
            }

            lock (_sync)
            {
                _chain.Clear();
                foreach (KeyValuePair<string, List<string>> kv in chain) _chain[kv.Key] = kv.Value;
                _chainBuilt = DateTime.UtcNow;
                _chainPosts = used;
            }
            Log.Info("AI", "markov chain rebuilt from " + used + " posts, " + chain.Count + " states");
        }

        private static string[] Tokenize(string text)
        {
            List<string> words = new List<string>();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || c == '\'' || c == '-')
                {
                    sb.Append(c);
                    continue;
                }
                if (sb.Length > 0) { words.Add(sb.ToString().ToLowerInvariant()); sb.Length = 0; }
                if (c == '.' || c == '!' || c == '?')
                {
                    if (words.Count > 0 && words[words.Count - 1] != ".") words.Add(".");
                }
            }
            if (sb.Length > 0) words.Add(sb.ToString().ToLowerInvariant());
            return words.ToArray();
        }

        private string GenerateMarkov()
        {
            lock (_sync)
            {
                if (_chain.Count == 0 || (DateTime.UtcNow - _chainBuilt).TotalMinutes > 10) { }
            }
            if (_chain.Count == 0) RebuildChain();
            if (_chain.Count == 0) return "";

            StringBuilder sb = new StringBuilder(200);
            List<string> keys = new List<string>(_chain.Keys);
            lock (_sync)
            {
                string current = keys[_rnd.Next(keys.Count)];
                for (int step = 0; step < 40 && sb.Length < 200; step++)
                {
                    List<string> next;
                    if (!_chain.TryGetValue(current, out next) || next.Count == 0) break;
                    string word = next[_rnd.Next(next.Count)];
                    if (word == ".")
                    {
                        if (sb.Length > 24) { sb.Append('.'); break; }
                        continue;
                    }
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(word);
                    current = word;
                }
            }

            string result = sb.ToString().Trim();
            if (result.Length == 0) return "";
            result = char.ToUpperInvariant(result[0]) + (result.Length > 1 ? result.Substring(1) : "");
            if (!result.EndsWith(".", StringComparison.Ordinal) &&
                !result.EndsWith("!", StringComparison.Ordinal) &&
                !result.EndsWith("?", StringComparison.Ordinal))
                result += ".";
            return result;
        }

        // ------------------------------------------------------ openai http

        private string GenerateRemote(string system, string prompt, out string error)
        {
            error = "";
            string endpoint = _cfg.AiEndpoint;
            if (string.IsNullOrEmpty(endpoint)) { error = "no endpoint configured"; return ""; }

            StringBuilder payload = new StringBuilder(1024);
            payload.Append("{\"model\":").Append(Json.Quote(_cfg.AiModel));
            payload.Append(",\"temperature\":").Append(_cfg.AiTemperature.ToString("0.##", CultureInfo.InvariantCulture));
            payload.Append(",\"max_tokens\":").Append(_cfg.AiMaxTokens.ToString(CultureInfo.InvariantCulture));
            payload.Append(",\"stream\":false,\"messages\":[");
            payload.Append("{\"role\":\"system\",\"content\":").Append(Json.Quote(system)).Append("},");
            payload.Append("{\"role\":\"user\",\"content\":").Append(Json.Quote(prompt)).Append("}]}");

            DateTime started = DateTime.UtcNow;
            string body = PostJson(endpoint, payload.ToString(), out error);
            lock (_sync) { _lastLatencyMs = (int)(DateTime.UtcNow - started).TotalMilliseconds; }
            if (body == null) return "";

            try
            {
                Dictionary<string, object> root = Json.ParseObject(body, 32);
                object choicesObj = Json.Get(root, "choices");
                List<object> choices = choicesObj as List<object>;
                if (choices == null || choices.Count == 0)
                {
                    object errObj = Json.Get(root, "error");
                    Dictionary<string, object> err = errObj as Dictionary<string, object>;
                    error = err != null ? Json.GetString(err, "message", "provider error") : "no choices in response";
                    return "";
                }
                Dictionary<string, object> first = choices[0] as Dictionary<string, object>;
                if (first == null) { error = "malformed choice"; return ""; }

                // chat/completions
                object messageObj = Json.Get(first, "message");
                Dictionary<string, object> message = messageObj as Dictionary<string, object>;
                if (message != null)
                {
                    string content = Json.GetString(message, "content", "");
                    if (content.Trim().Length > 0) return content;

                    // Reasoning models (DeepSeek R-series, o-series, Qwen-thinking...)
                    // may spend the whole budget on reasoning_content and return an empty
                    // answer. Fall back to the tail of the reasoning instead of failing.
                    string reasoning = Json.GetString(message, "reasoning_content", "");
                    if (reasoning.Trim().Length > 0)
                    {
                        string tail = LastSentence(reasoning);
                        if (tail.Length > 0) return tail;
                    }
                }
                // legacy completions
                string text = Json.GetString(first, "text", "");
                if (text.Length > 0) return text;

                error = "empty content";
                return "";
            }
            catch (Exception ex)
            {
                error = "bad response: " + ex.Message;
                return "";
            }
        }

        private string PostJson(string url, string json, out string error)        {
            error = "";
            EnableModernTls();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                // charset matters: without it some providers decode the body as latin-1
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.Timeout = _cfg.AiTimeoutMs;
                req.ReadWriteTimeout = _cfg.AiTimeoutMs;
                req.KeepAlive = false;
                if (!string.IsNullOrEmpty(_cfg.AiApiKey))
                    req.Headers["Authorization"] = "Bearer " + _cfg.AiApiKey;

                byte[] bytes = Encoding.UTF8.GetBytes(json);
                req.ContentLength = bytes.Length;
                using (Stream stream = req.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                string detail = "";
                try
                {
                    if (wex.Response != null)
                    {
                        using (StreamReader reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                            detail = reader.ReadToEnd();
                    }
                }
                catch { }
                if (detail.Length > 300) detail = detail.Substring(0, 300);
                error = "HTTP " + (wex.Response == null ? "?" : ((int)((HttpWebResponse)wex.Response).StatusCode).ToString(CultureInfo.InvariantCulture)) +
                        ": " + (string.IsNullOrEmpty(detail) ? wex.Message : detail);
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>One-shot connectivity probe for the admin board.</summary>
        public bool Probe(out string error, out int latencyMs)
        {
            error = "";
            latencyMs = 0;
            if (_cfg.AiProvider != "openai")
            {
                if (_cfg.AiProvider == "markov")
                {
                    RebuildChain();
                    if (ChainSize == 0) { error = "no posts to learn from"; return false; }
                    return true;
                }
                error = "provider is off";
                return false;
            }

            DateTime started = DateTime.UtcNow;
            string probeError;
            string reply = GenerateRemote("Ты тестовый ответчик.", "Ответь одним словом: ок", out probeError);
            latencyMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (string.IsNullOrEmpty(reply))
            {
                error = string.IsNullOrEmpty(probeError) ? "empty reply" : probeError;
                return false;
            }
            return true;
        }
    }
}
