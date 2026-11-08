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

            string ext = Sniff(data);
            if (ext == null) { error = "unsupported-image"; return null; }

            string sub = kind == "avatar" ? "avatars" : kind == "group" ? "groups" : "posts";
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

        public static string MimeForExtension(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                default: return "application/octet-stream";
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
