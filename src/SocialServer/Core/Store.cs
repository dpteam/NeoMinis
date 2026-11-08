using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Crash tolerant JSON file helpers: atomic replace, .bak fallback, quarantine of
    /// unreadable files and timestamped snapshots. No database server, no NuGet.
    /// </summary>
    public static class JsonFile
    {
        public static string Read(string path, out bool fromBackup, out string error)
        {
            fromBackup = false;
            error = "";
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) { error = ex.Message; }

            string bak = path + ".bak";
            try
            {
                if (File.Exists(bak))
                {
                    fromBackup = true;
                    error = "";
                    return File.ReadAllText(bak, Encoding.UTF8);
                }
            }
            catch (Exception ex) { error = ex.Message; }

            return null;
        }

        public static void WriteAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? "");

            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
                try { fs.Flush(true); } catch { }
            }

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                // keep a baseline .bak so a corrupt first write is still recoverable
                try { File.Copy(path, path + ".bak", true); } catch { }
                return;
            }

            string bak = path + ".bak";
            try
            {
                File.Replace(tmp, path, bak, true);
                return;
            }
            catch { }

            try
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
                File.Move(tmp, path);
                return;
            }
            catch { }

            // last resort: overwrite in place, still better than losing the update
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        public static void Quarantine(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string bad = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Move(path, bad);
            }
            catch { }
        }

        public static string Snapshot(string dataDir, string backupDir, int keep)
        {
            try
            {
                Directory.CreateDirectory(backupDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string dir = Path.Combine(backupDir, stamp);
                Directory.CreateDirectory(dir);

                string[] files = Directory.GetFiles(dataDir, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Copy(files[i], Path.Combine(dir, name), true); } catch { }
                }

                // meta snapshot of what this backup contains
                File.WriteAllText(Path.Combine(dir, "backup.txt"),
                    "Created: " + Util.Iso(DateTime.UtcNow) + Environment.NewLine +
                    "Files: " + files.Length.ToString(CultureInfo.InvariantCulture) + Environment.NewLine,
                    new UTF8Encoding(false));

                Prune(backupDir, keep);
                return dir;
            }
            catch (Exception ex)
            {
                Log.Warn("DATA", "snapshot failed: " + ex.Message);
                return null;
            }
        }

        private static void Prune(string backupDir, int keep)
        {
            if (keep < 1) keep = 1;
            try
            {
                string[] dirs = Directory.GetDirectories(backupDir);
                Array.Sort(dirs, StringComparer.Ordinal);
                int remove = dirs.Length - keep;
                for (int i = 0; i < remove; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
        }
    }

    public sealed class StoreReadOnlyException : Exception
    {
        public StoreReadOnlyException(string message) : base(message) { }
    }
}
