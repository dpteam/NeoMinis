using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace Social.Core
{
    public static class Util
    {
        public static string Iso(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        public static DateTime ParseIso(string s, DateTime def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            DateTime parsed;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed;
            return def;
        }

        public static string Ip(IPAddress address)
        {
            if (address == null) return "0.0.0.0";
            byte[] b = address.GetAddressBytes();
            if (b.Length == 16)
            {
                bool mapped = true;
                for (int i = 0; i < 10; i++) if (b[i] != 0) { mapped = false; break; }
                if (mapped && b[10] == 0xff && b[11] == 0xff)
                {
                    byte[] v4 = new byte[4];
                    Array.Copy(b, 12, v4, 0, 4);
                    return Ip(new IPAddress(v4));
                }
            }
            return address.ToString();
        }

        public static string CleanText(string s, int maxLen)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(Math.Min(s.Length, maxLen));
            bool lastSpace = false;
            for (int i = 0; i < s.Length && sb.Length < maxLen; i++)
            {
                char c = s[i];
                if (c == '\0') break;
                if (c == '\r')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n') continue;
                    c = '\n';
                }
                if (c == '\t') c = ' ';
                if (c < 32 && c != '\n')
                {
                    if (!lastSpace) { sb.Append(' '); lastSpace = true; }
                    continue;
                }
                if (c == '\n')
                {
                    // collapse more than two blank lines
                    int nl = 0;
                    for (int j = sb.Length - 1; j >= 0 && sb[j] == '\n'; j--) nl++;
                    if (nl >= 2) continue;
                    sb.Append('\n');
                    lastSpace = false;
                    continue;
                }
                if (c == 127) continue;
                sb.Append(c);
                lastSpace = (c == ' ');
            }
            return sb.ToString().Trim();
        }

        public static string SingleLine(string s, int maxLen)
        {
            string t = CleanText(s, maxLen);
            return t.Replace('\n', ' ').Replace("  ", " ").Trim();
        }

        public static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default:
                        if (c < 32 && c != '\n') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Attr(string s)
        {
            return HtmlEncode(s).Replace("\n", " ");
        }

        public static string Base64Encode(byte[] data)
        {
            return data == null ? "" : Convert.ToBase64String(data);
        }

        public static byte[] Base64Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];
            try { return Convert.FromBase64String(text); }
            catch { return null; }
        }

        /// <summary>Parses "data:image/png;base64,...." and returns the bytes, or null.</summary>
        public static byte[] ParseDataUrl(string dataUrl, out string mime)
        {
            mime = "";
            if (string.IsNullOrEmpty(dataUrl)) return null;
            int comma = dataUrl.IndexOf(',');
            if (comma <= 0) return null;
            string head = dataUrl.Substring(0, comma);
            if (head.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) < 0) return null;
            int colon = head.IndexOf(':');
            int semi = head.IndexOf(';');
            if (colon < 0 || semi <= colon) return null;
            mime = head.Substring(colon + 1, semi - colon - 1).Trim().ToLowerInvariant();
            try { return Convert.FromBase64String(dataUrl.Substring(comma + 1)); }
            catch { return null; }
        }

        public static bool IsValidUsername(string username, int maxLen)
        {
            if (string.IsNullOrEmpty(username)) return false;
            if (username.Length < 3 || username.Length > maxLen) return false;
            for (int i = 0; i < username.Length; i++)
            {
                char c = username[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '_' || c == '.';
                if (!ok) return false;
            }
            if (username[0] == '.' || username[username.Length - 1] == '.') return false;
            if (username.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        public static string NormalizeUsername(string username)
        {
            return (username ?? "").Trim().ToLowerInvariant();
        }

        public static string DisplayName(string value, string fallback, int maxLen)
        {
            string s = SingleLine(value, maxLen);
            if (s.Length == 0) s = fallback ?? "";
            return s;
        }

        public static string Slug(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder(maxLen);
            bool dash = false;
            for (int i = 0; i < value.Length && sb.Length < maxLen; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (ok) { sb.Append(c); dash = false; }
                else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            }
            return sb.ToString().Trim('-');
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || email.Length > 160) return false;
            int at = email.IndexOf('@');
            if (at <= 0 || at == email.Length - 1) return false;
            if (email.IndexOf(' ') >= 0) return false;
            int dot = email.LastIndexOf('.');
            return dot > at + 1 && dot < email.Length - 1;
        }

        public static bool IsHttpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            if (u.Length < 8 || u.Length > 2000) return false;
            if (u.IndexOf(' ') >= 0) return false;
            return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            double v = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int u = -1;
            do { v /= 1024.0; u++; } while (v >= 1024.0 && u < units.Length - 1);
            return v.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[u];
        }

        public static string HumanDuration(TimeSpan span)
        {
            if (span.TotalSeconds < 1) return "0s";
            if (span.TotalMinutes < 1) return ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s";
            if (span.TotalHours < 1)
                return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m " +
                       span.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
            if (span.TotalDays < 1)
                return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " +
                       span.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + "d " +
                   span.Hours.ToString(CultureInfo.InvariantCulture) + "h";
        }

        public static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static DateTime FromUnix(long seconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
        }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_') sb.Append(c);
            }
            string s = sb.ToString();
            return s.Length == 0 ? "file" : s;
        }

        public static bool IsPrivateIpv4(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            if (ip == "::1" || ip.StartsWith("127.", StringComparison.Ordinal)) return true;
            IPAddress addr;
            if (!IPAddress.TryParse(ip, out addr)) return false;
            byte[] b = addr.GetAddressBytes();
            if (b.Length != 4) return ip.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("fc", StringComparison.OrdinalIgnoreCase);
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }
    }
}
