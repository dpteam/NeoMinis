using System;
using System.IO;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Avatar and post image storage. Images arrive as base64 data URLs inside the
    /// encrypted API body; only raster formats are accepted (SVG would be an XSS vector),
    /// files get random names and are served with nosniff.
    /// </summary>
    public sealed class MediaStore
    {
        private readonly Config _cfg;

        public MediaStore(Config cfg)
        {
            _cfg = cfg;
        }

        public string SaveDataUrl(string dataUrl, string kind, int maxKb, out string error)
        {
            error = "";
            string mime;
            byte[] data = Util.ParseDataUrl(dataUrl, out mime);
            if (data == null) { error = "bad-image"; return null; }
            if (data.Length == 0) { error = "empty-image"; return null; }
            if (data.Length > (long)maxKb * 1024L) { error = "image-too-large"; return null; }

            bool audio = kind == "voice";
            string ext = audio ? SniffAudio(data) : Sniff(data);
            if (ext == null) { error = audio ? "unsupported-audio" : "unsupported-image"; return null; }

            string sub = kind == "avatar" ? "avatars"
                : kind == "group" ? "groups"
                : kind == "message" ? "messages"
                : kind == "voice" ? "messages"
                : "posts";
            string dir = Path.Combine(_cfg.MediaDir, sub);
            Directory.CreateDirectory(dir);

            string name = Crypto.RandomHex(16) + ext;
            string path = Path.Combine(dir, name);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                error = "save-failed";
                Log.Error("MEDIA", "cannot save " + kind + ": " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return null;
            }

            Log.Debug("MEDIA", kind + " saved " + name + " (" + data.Length + " bytes)");
            return "/media/" + sub + "/" + name;
        }

        public static string Sniff(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return ".png";
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
            if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' &&
                (data[4] == '7' || data[4] == '9') && data[5] == 'a') return ".gif";
            if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return ".webp";
            return null;
        }

        /// <summary>
        /// Container sniffing for voice messages. Only formats every browser can
        /// play back are accepted; the extension decides the served MIME type.
        /// </summary>
        public static string SniffAudio(byte[] data)
        {
            if (data == null || data.Length < 12) return null;
            // RIFF....WAVE
            if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E') return ".wav";
            // EBML (webm / matroska, what Chrome and Edge record)
            if (data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3) return ".webm";
            // Ogg (Firefox records audio/ogg)
            if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') return ".ogg";
            // MPEG audio frame or an ID3 tag
            if (data[0] == 'I' && data[1] == 'D' && data[2] == '3') return ".mp3";
            if (data[0] == 0xFF && (data[1] == 0xFB || data[1] == 0xF3 || data[1] == 0xF2 || data[1] == 0xFA)) return ".mp3";
            // ISO base media (m4a / mp4 audio)
            if (data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p') return ".m4a";
            return null;
        }

        public static string MimeForExtension(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".webm": return "audio/webm";
                case ".ogg": return "audio/ogg";
                case ".mp3": return "audio/mpeg";
                case ".m4a": return "audio/mp4";
                case ".wav": return "audio/wav";
                default: return "application/octet-stream";
            }
        }

        /// <summary>
        /// Copies a stored media file to a fresh name. Forwarded messages get their
        /// own copy so deleting one of them can never break the other.
        /// </summary>
        public string Duplicate(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/media/", StringComparison.Ordinal)) return url;
            try
            {
                string rel = url.Substring("/media/".Length).Replace('/', Path.DirectorySeparatorChar);
                if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) return "";
                string source = Path.GetFullPath(Path.Combine(_cfg.MediaDir, rel));
                string root = Path.GetFullPath(_cfg.MediaDir);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    root += Path.DirectorySeparatorChar;
                if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "";
                if (!File.Exists(source)) return "";

                string dir = Path.GetDirectoryName(source);
                string ext = Path.GetExtension(source);
                string copy = Path.Combine(dir, Crypto.RandomHex(16) + ext);
                File.Copy(source, copy, true);
                string sub = dir.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
                return "/media/" + sub + "/" + Path.GetFileName(copy);
            }
            catch (Exception ex)
            {
                Log.Warn("MEDIA", "cannot duplicate " + url + ": " + ex.Message);
                return "";
            }
        }

        public void Delete(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/media/", StringComparison.Ordinal)) return;
            try
            {
                string rel = url.Substring("/media/".Length).Replace('/', Path.DirectorySeparatorChar);
                if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) return;
                string path = Path.GetFullPath(Path.Combine(_cfg.MediaDir, rel));
                string root = Path.GetFullPath(_cfg.MediaDir);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    root += Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
