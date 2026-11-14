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
            bool video = kind == "storyvideo";
            string ext = audio ? SniffAudio(data) : video ? SniffVideo(data) : Sniff(data);
            if (ext == null)
            {
                error = audio ? "unsupported-audio" : video ? "unsupported-video" : "unsupported-image";
                return null;
            }

            string sub = kind == "avatar" ? "avatars"
                : kind == "group" ? "groups"
                : kind == "message" ? "messages"
                : kind == "voice" ? "messages"
                : kind == "story" || kind == "storyvideo" ? "stories"
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

        /// <summary>
        /// Container sniffing for story clips. Only the two formats every browser
        /// can play back are accepted, and the extension decides the served type.
        /// </summary>
        public static string SniffVideo(byte[] data)
        {
            if (data == null || data.Length < 12) return null;
            // EBML header: webm / matroska
            if (data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3) return ".webm";
            // ISO base media: ftyp at offset 4 covers mp4 and m4v
            if (data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p') return ".mp4";
            // Ogg container
            if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') return ".ogv";
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
        /// Saves an arbitrary attachment. The extension is taken from a whitelist and
        /// the content type is derived from that extension, never from the client, so
        /// a renamed HTML file can never be served as a document that executes.
        /// </summary>
        public string SaveFile(string dataUrl, string fileName, int maxKb, out string error)
        {
            error = "";
            string mime;
            byte[] data = Util.ParseDataUrl(dataUrl, out mime);
            if (data == null) { error = "bad-file"; return null; }
            if (data.Length == 0) { error = "empty-file"; return null; }
            if (data.Length > (long)maxKb * 1024L) { error = "file-too-large"; return null; }

            string ext = SafeExtension(fileName);
            if (ext == null) { error = "unsupported-file"; return null; }

            string sub = "files";
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
                Log.Error("MEDIA", "cannot save file: " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return null;
            }

            Log.Debug("MEDIA", "file saved " + name + " (" + data.Length + " bytes)");
            return "/media/" + sub + "/" + name;
        }

        /// <summary>
        /// Stores a track or a clip for the media library. Only audio and video
        /// containers are accepted, and the kind decides which extensions are legal so
        /// a "music" upload can never smuggle in a document.
        /// </summary>
        public string SaveLibrary(string dataUrl, string fileName, string kind, int maxKb, out string error)
        {
            error = "";
            string mime;
            byte[] data = Util.ParseDataUrl(dataUrl, out mime);
            if (data == null) { error = "bad-file"; return null; }
            if (data.Length == 0) { error = "empty-file"; return null; }
            if (data.Length > (long)maxKb * 1024L) { error = "file-too-large"; return null; }

            string ext = LibraryExtension(fileName, kind);
            if (ext == null) { error = kind == "video" ? "unsupported-video" : "unsupported-audio"; return null; }

            string sub = "library";
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
                Log.Error("MEDIA", "cannot save library file: " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return null;
            }

            Log.Debug("MEDIA", "library " + kind + " saved " + name + " (" + data.Length + " bytes)");
            return "/media/" + sub + "/" + name;
        }

        /// <summary>
        /// Stores an uploaded Flash movie. The file is accepted only when its first
        /// three bytes are a real SWF signature (FWS/CWS/ZWS): the extension the
        /// browser reports is never trusted, so an .html or .svg renamed to .swf is
        /// rejected. The result is always stored as ".swf" and served as an opaque
        /// download, never as a Flash plugin type.
        /// </summary>
        public string SaveGame(string dataUrl, int maxKb, out string error)
        {
            error = "";
            string mime;
            byte[] data = Util.ParseDataUrl(dataUrl, out mime);
            if (data == null) { error = "bad-file"; return null; }
            if (data.Length < 8) { error = "empty-file"; return null; }
            if (data.Length > (long)maxKb * 1024L) { error = "file-too-large"; return null; }
            if (!IsSwf(data)) { error = "unsupported-game"; return null; }

            string sub = "games";
            string dir = Path.Combine(_cfg.MediaDir, sub);
            Directory.CreateDirectory(dir);

            string name = Crypto.RandomHex(16) + ".swf";
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
                Log.Error("MEDIA", "cannot save game: " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return null;
            }

            Log.Debug("MEDIA", "game saved " + name + " (" + data.Length + " bytes)");
            return "/media/" + sub + "/" + name;
        }

        /// <summary>True when the bytes start with an uncompressed or compressed SWF header.</summary>
        public static bool IsSwf(byte[] data)
        {
            if (data == null || data.Length < 8) return false;
            char a = (char)data[0], b = (char)data[1], c = (char)data[2];
            if (!((a == 'F' || a == 'C' || a == 'Z') && b == 'W' && c == 'S')) return false;
            // byte 3 is the Flash version, bytes 4..7 the uncompressed length
            if (data[3] < 1) return false;
            return true;
        }

        /// <summary>Audio containers every browser can play.</summary>
        public static string[] AudioExtensions()        {
            return new string[] { ".mp3", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".flac", ".weba" };
        }

        /// <summary>Video containers browsers can play (mkv/mov are hit and miss).</summary>
        public static string[] VideoExtensions()
        {
            return new string[] { ".mp4", ".m4v", ".webm", ".ogv", ".mov" };
        }

        public static bool IsAudioExtension(string ext)
        {
            string[] allowed = AudioExtensions();
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] == ext) return true;
            return false;
        }

        public static bool IsVideoExtension(string ext)
        {
            string[] allowed = VideoExtensions();
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] == ext) return true;
            return false;
        }

        /// <summary>Extension when it belongs to the requested kind, otherwise null.</summary>
        public static string LibraryExtension(string fileName, string kind)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) return null;
            ext = ext.ToLowerInvariant();
            for (int i = 1; i < ext.Length; i++)
            {
                char c = ext[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return null;
            }
            if (kind == "video") return IsVideoExtension(ext) ? ext : null;
            return IsAudioExtension(ext) ? ext : null;
        }

        /// <summary>Extensions we are willing to store and hand back.</summary>
        public static string[] FileExtensions()
        {
            return new string[]
            {
                ".pdf", ".txt", ".md", ".csv", ".json", ".xml", ".log",
                ".zip", ".7z", ".rar", ".tar", ".gz",
                ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods",
                ".mp3", ".m4a", ".ogg", ".wav", ".flac", ".opus",
                ".mp4", ".webm", ".mkv", ".mov",
                ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"
            };
        }

        /// <summary>Returns the lower-case extension when it is on the whitelist.</summary>
        public static string SafeExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) return null;
            ext = ext.ToLowerInvariant();
            if (ext.Length < 2 || ext.Length > 6) return null;
            for (int i = 1; i < ext.Length; i++)
            {
                char c = ext[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return null;
            }
            string[] allowed = FileExtensions();
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] == ext) return ext;
            return null;
        }

        public static string MimeForFile(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".pdf": return "application/pdf";
                case ".txt": case ".md": case ".log": return "text/plain; charset=utf-8";
                case ".csv": return "text/csv; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".xml": return "application/xml; charset=utf-8";
                case ".zip": return "application/zip";
                case ".7z": return "application/x-7z-compressed";
                case ".rar": return "application/vnd.rar";
                case ".tar": return "application/x-tar";
                case ".gz": return "application/gzip";
                case ".doc": return "application/msword";
                case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xls": return "application/vnd.ms-excel";
                case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".ppt": return "application/vnd.ms-powerpoint";
                case ".pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                case ".odt": return "application/vnd.oasis.opendocument.text";
                case ".ods": return "application/vnd.oasis.opendocument.spreadsheet";
                case ".mp3": return "audio/mpeg";
                case ".m4a": return "audio/mp4";
                case ".aac": return "audio/aac";
                case ".ogg": case ".opus": case ".oga": return "audio/ogg";
                case ".weba": return "audio/webm";
                case ".wav": return "audio/wav";
                case ".flac": return "audio/flac";
                case ".mp4": return "video/mp4";
                case ".m4v": return "video/mp4";
                case ".webm": return "video/webm";
                case ".ogv": return "video/ogg";
                case ".mkv": return "video/x-matroska";
                case ".mov": return "video/quicktime";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                default: return "application/octet-stream";
            }
        }

        /// <summary>Human readable size, used by the SPA and the admin list.</summary>
        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
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
