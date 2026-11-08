using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Tiny SVG captcha: four digits drawn as seven-segment paths (no &lt;text&gt;, so the
    /// answer cannot be scraped out of the markup), each rotated a little, with a strike
    /// line and light noise. The answer never leaves the server; a challenge is single-use.
    /// </summary>
    public static class Captcha
    {
        private static readonly object Sync = new object();
        private static readonly Random Rnd = new Random(Environment.TickCount);

        public const int Length = 4;

        // seven-segment masks: bit0=a(top) bit1=b(top-right) bit2=c(bottom-right)
        //                    bit3=d(bottom) bit4=e(bottom-left) bit5=f(top-left) bit6=g(middle)
        private static readonly int[] DigitMask = new int[]
        {
            0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F
        };

        public static string NewCode()
        {
            byte[] raw = Crypto.RandomBytes(Length);
            StringBuilder sb = new StringBuilder(Length);
            for (int i = 0; i < Length; i++) sb.Append((char)('0' + (raw[i] % 10)));
            return sb.ToString();
        }

        public static string Svg(string code)
        {
            if (string.IsNullOrEmpty(code)) code = "0000";

            int width = 132, height = 46;
            StringBuilder sb = new StringBuilder(4096);
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width)
              .Append("\" height=\"").Append(height)
              .Append("\" viewBox=\"0 0 ").Append(width).Append(' ').Append(height)
              .Append("\" role=\"img\" aria-label=\"captcha\">");

            lock (Sync)
            {
                sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"currentColor\" opacity=\".05\" rx=\"4\"/>");

                for (int i = 0; i < 3; i++)
                {
                    sb.Append("<line x1=\"").Append(Rnd.Next(width)).Append("\" y1=\"").Append(Rnd.Next(height))
                      .Append("\" x2=\"").Append(Rnd.Next(width)).Append("\" y2=\"").Append(Rnd.Next(height))
                      .Append("\" stroke=\"currentColor\" stroke-width=\"1\" opacity=\".15\"/>");
                }

                int step = (width - 16) / Length;
                for (int i = 0; i < code.Length; i++)
                {
                    int digit = code[i] - '0';
                    if (digit < 0 || digit > 9) digit = 0;

                    int cx = 10 + i * step + step / 2;
                    int cy = height / 2 + Rnd.Next(-2, 3);
                    int angle = Rnd.Next(-18, 19);
                    double scale = 1.55 + Rnd.NextDouble() * 0.25;

                    sb.Append("<g transform=\"translate(").Append(cx).Append(' ').Append(cy)
                      .Append(") rotate(").Append(angle).Append(") scale(")
                      .Append(scale.ToString("0.##", CultureInfo.InvariantCulture)).Append(")\"")
                      .Append(" stroke=\"currentColor\" stroke-width=\"1.35\" stroke-linecap=\"round\"")
                      .Append(" fill=\"none\" opacity=\".88\">");

                    AppendDigit(sb, DigitMask[digit]);
                    sb.Append("</g>");

                    sb.Append("<line x1=\"").Append(cx - 11).Append("\" y1=\"").Append(cy - 2)
                      .Append("\" x2=\"").Append(cx + 11).Append("\" y2=\"").Append(cy + 2)
                      .Append("\" stroke=\"currentColor\" stroke-width=\"1.4\" opacity=\".45\"/>");
                }

                for (int i = 0; i < 10; i++)
                {
                    sb.Append("<circle cx=\"").Append(Rnd.Next(width)).Append("\" cy=\"").Append(Rnd.Next(height))
                      .Append("\" r=\"").Append(Rnd.Next(1, 3)).Append("\" fill=\"currentColor\" opacity=\".2\"/>");
                }
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static void AppendDigit(StringBuilder sb, int mask)
        {
            if ((mask & 0x01) != 0) Line(sb, -3.4, -8.4, 3.4, -8.4);
            if ((mask & 0x02) != 0) Line(sb, 3.4, -7.6, 3.4, -1.4);
            if ((mask & 0x04) != 0) Line(sb, 3.4, 1.4, 3.4, 7.6);
            if ((mask & 0x08) != 0) Line(sb, -3.4, 8.4, 3.4, 8.4);
            if ((mask & 0x10) != 0) Line(sb, -3.4, 1.4, -3.4, 7.6);
            if ((mask & 0x20) != 0) Line(sb, -3.4, -7.6, -3.4, -1.4);
            if ((mask & 0x40) != 0) Line(sb, -3.4, 0, 3.4, 0);
        }

        private static void Line(StringBuilder sb, double x1, double y1, double x2, double y2)
        {
            sb.Append("<line x1=\"").Append(N(x1)).Append("\" y1=\"").Append(N(y1))
              .Append("\" x2=\"").Append(N(x2)).Append("\" y2=\"").Append(N(y2)).Append("\"/>");
        }

        private static string N(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Normalize(string answer)
        {
            if (answer == null) return "";
            StringBuilder sb = new StringBuilder(answer.Length);
            for (int i = 0; i < answer.Length && sb.Length < 8; i++)
            {
                char c = answer[i];
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public sealed class CaptchaChallenge
    {
        public string Id = "";
        public string Code = "";
        public DateTime Expires = DateTime.UtcNow;
        public string Svg = "";
    }

    /// <summary>Single-use captcha challenges with a short lifetime and bounded storage.</summary>
    public sealed class CaptchaStore
    {
        private sealed class Entry
        {
            public string Code;
            public DateTime Expires;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _items = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int _max;
        private readonly int _ttlSeconds;

        public CaptchaStore(int max, int ttlSeconds)
        {
            _max = Math.Max(32, max);
            _ttlSeconds = Math.Max(30, ttlSeconds);
        }

        public CaptchaChallenge New()
        {
            CaptchaChallenge c = new CaptchaChallenge();
            c.Id = Crypto.RandomHex(12);
            c.Code = Captcha.NewCode();
            c.Expires = DateTime.UtcNow.AddSeconds(_ttlSeconds);
            c.Svg = Captcha.Svg(c.Code);

            lock (_sync)
            {
                PruneLocked();
                if (_items.Count >= _max) PruneLocked();
                Entry e = new Entry();
                e.Code = c.Code;
                e.Expires = c.Expires;
                _items[c.Id] = e;
            }
            return c;
        }

        public bool Validate(string id, string answer)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string normalized = Captcha.Normalize(answer);
            if (normalized.Length == 0) return false;

            lock (_sync)
            {
                Entry e;
                if (!_items.TryGetValue(id, out e)) return false;
                _items.Remove(id); // single use, even on failure
                if (e.Expires < DateTime.UtcNow) return false;
                return Crypto.FixedEquals(e.Code, normalized);
            }
        }

        public void Prune()
        {
            lock (_sync) { PruneLocked(); }
        }

        private void PruneLocked()
        {
            DateTime now = DateTime.UtcNow;
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, Entry> kv in _items)
                if (kv.Value.Expires < now) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _items.Remove(dead[i]);

            if (_items.Count > _max)
            {
                // drop arbitrary oldest-looking entries; the dictionary has no order,
                // so just clear half - captchas are cheap to issue again
                List<string> keys = new List<string>(_items.Keys);
                int drop = keys.Count / 2;
                for (int i = 0; i < drop; i++) _items.Remove(keys[i]);
            }
        }
    }
}
