using System;
using System.Collections.Generic;
using System.IO;

namespace Social.Core
{
    /// <summary>One media file on disk, as the audit sees it.</summary>
    public sealed class StorageFile
    {
        public string Path = "";     // relative to the media root, e.g. avatars/ab12.jpg
        public long Bytes;
        public DateTime Modified = DateTime.MinValue;
    }

    /// <summary>A reference from a record to a media path that does not exist.</summary>
    public sealed class StorageMissing
    {
        public string Path = "";
        public string Where = "";    // "post #12", "message #99", ...
    }

    /// <summary>
    /// The result of a storage audit: what is on disk, what is referenced, and the
    /// three things an operator actually wants to know - how big each folder is, what
    /// is big, and what is broken (files nothing points at, and records pointing at
    /// files that are gone).
    ///
    /// Deliberately read-only. Deleting is a decision, and an audit that also deletes
    /// is an audit nobody runs on a live server.
    /// </summary>
    public sealed class StorageAudit
    {
        public long TotalBytes;
        public int TotalFiles;
        public long JsonBytes;
        public int JsonFiles;

        public List<KeyValuePair<string, long>> Folders = new List<KeyValuePair<string, long>>();
        public List<StorageFile> Biggest = new List<StorageFile>();
        public List<StorageFile> Orphans = new List<StorageFile>();
        public List<StorageMissing> Missing = new List<StorageMissing>();

        public long OrphanBytes
        {
            get
            {
                long n = 0;
                for (int i = 0; i < Orphans.Count; i++) n += Orphans[i].Bytes;
                return n;
            }
        }
    }

    /// <summary>
    /// Walks the media folder once and compares it with every record that can point
    /// at a file. Written as a separate class because it needs only a list of
    /// referenced paths and the config, not the whole store - which keeps the store
    /// from growing another 200 lines of filesystem code.
    /// </summary>
    public static class StorageScan
    {
        /// <summary>
        /// Builds the audit. <paramref name="referenced"/> is the set of paths that
        /// records claim to use, relative to the media root with forward slashes.
        /// <paramref name="missing"/> comes from the caller, because only the store
        /// knows which record pointed where.
        /// </summary>
        public static StorageAudit Run(Config cfg, HashSet<string> referenced,
                                       List<StorageMissing> missing, int topCount)
        {
            StorageAudit audit = new StorageAudit();
            if (topCount < 1) topCount = 20;

            Dictionary<string, long> folderBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            List<StorageFile> all = new List<StorageFile>();

            try
            {
                string root = cfg.MediaDir;
                if (Directory.Exists(root))
                {
                    string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string full = files[i];
                        // a half-written temp file is not media and not an orphan
                        if (full.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        FileInfo fi;
                        try { fi = new FileInfo(full); } catch { continue; }

                        string rel = Relative(root, full);
                        StorageFile sf = new StorageFile();
                        sf.Path = rel;
                        sf.Bytes = fi.Length;
                        try { sf.Modified = fi.LastWriteTimeUtc; } catch { }
                        all.Add(sf);

                        audit.TotalBytes += fi.Length;
                        audit.TotalFiles++;

                        int slash = rel.IndexOf('/');
                        string folder = slash > 0 ? rel.Substring(0, slash) : "(root)";
                        long have;
                        folderBytes.TryGetValue(folder, out have);
                        folderBytes[folder] = have + fi.Length;
                    }
                }

                string dataDir = cfg.DataDir;
                if (Directory.Exists(dataDir))
                {
                    string[] json = Directory.GetFiles(dataDir, "*.json");
                    for (int i = 0; i < json.Length; i++)
                    {
                        try
                        {
                            audit.JsonBytes += new FileInfo(json[i]).Length;
                            audit.JsonFiles++;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("STORAGE", "audit failed to walk the media folder: " + ex.Message);
            }

            // folders, biggest first
            List<KeyValuePair<string, long>> folders = new List<KeyValuePair<string, long>>(folderBytes);
            folders.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            audit.Folders = folders;

            // biggest files
            all.Sort(delegate(StorageFile a, StorageFile b) { return b.Bytes.CompareTo(a.Bytes); });
            for (int i = 0; i < all.Count && i < topCount; i++) audit.Biggest.Add(all[i]);

            // orphans: a file nothing points at. The JSON tables are excluded by
            // construction (they are not under the media root), so this is exactly
            // "media that no record mentions".
            if (referenced != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (referenced.Contains(all[i].Path)) continue;
                    audit.Orphans.Add(all[i]);
                }
                audit.Orphans.Sort(delegate(StorageFile a, StorageFile b) { return b.Bytes.CompareTo(a.Bytes); });
            }

            if (missing != null) audit.Missing = missing;
            return audit;
        }

        /// <summary>
        /// Turns a stored URL ("/media/avatars/x.jpg") into the path the audit uses
        /// ("avatars/x.jpg"). Anything that is not a local media URL - an external
        /// image, an empty field - returns "" and is ignored.
        /// </summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string v = url.Trim();
            const string prefix = "/media/";
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) v = v.Substring(prefix.Length);
            else if (v.StartsWith("media/", StringComparison.OrdinalIgnoreCase)) v = v.Substring(6);
            else if (v.IndexOf("://", StringComparison.Ordinal) > 0) return "";   // external
            else if (!v.StartsWith("/", StringComparison.Ordinal)) return "";
            v = v.Replace('\\', '/').TrimStart('/');
            // a path with .. is not ours to reason about
            if (v.IndexOf("..", StringComparison.Ordinal) >= 0) return "";
            return v;
        }

        private static string Relative(string root, string full)
        {
            string r = root.Replace('\\', '/').TrimEnd('/');
            string f = full.Replace('\\', '/');
            if (f.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)) f = f.Substring(r.Length + 1);
            return f;
        }
    }
}
