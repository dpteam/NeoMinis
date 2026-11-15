using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>One built-in sticker pack: an id, a name and the sticker ids in it.</summary>
    public sealed class StickerPack
    {
        public string Id = "";
        public string Name = "";
        public string[] Stickers = new string[0];
    }

    /// <summary>
    /// The built-in sticker set. Stickers are files that ship with the site
    /// (<c>public/assets/stickers/&lt;pack&gt;/&lt;id&gt;.svg</c>), not uploads: a message
    /// carries only the id, so a sticker costs a few bytes and nothing is written to
    /// <c>data/media</c>. The catalogue lives here rather than being read from the
    /// manifest at runtime, because the server must be able to reject an id it does
    /// not know — otherwise a client could point a message at any path it likes.
    /// </summary>
    public static class Stickers
    {
        private static readonly StickerPack[] All = new StickerPack[]
        {
            Pack("faces", "Faces", new string[]
            {
                "smile", "laugh", "love", "wow", "sad", "cry",
                "angry", "sleepy", "cool", "think", "kiss", "wink"
            }),
            Pack("things", "Things", new string[]
            {
                "heart", "flame", "coffee", "party",
                "star", "rocket", "book", "moon"
            })
        };

        private static StickerPack Pack(string id, string name, string[] stickers)
        {
            StickerPack p = new StickerPack();
            p.Id = id;
            p.Name = name;
            p.Stickers = stickers;
            return p;
        }

        public static StickerPack[] Packs { get { return All; } }

        public static int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < All.Length; i++) n += All[i].Stickers.Length;
                return n;
            }
        }

        /// <summary>
        /// Normalises "faces/smile" and returns it only when that sticker really
        /// exists; anything else becomes an empty string, so an unknown or hostile id
        /// is simply dropped instead of being stored and rendered later.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string v = raw.Trim().ToLowerInvariant();
            int slash = v.IndexOf('/');
            if (slash <= 0 || slash == v.Length - 1) return "";
            string packId = v.Substring(0, slash);
            string id = v.Substring(slash + 1);
            if (id.IndexOf('/') >= 0) return "";
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id != packId) continue;
                for (int k = 0; k < All[i].Stickers.Length; k++)
                    if (All[i].Stickers[k] == id) return packId + "/" + id;
                return "";
            }
            return "";
        }

        /// <summary>The URL the browser loads for a sticker id.</summary>
        public static string Url(string sticker)
        {
            string v = Normalize(sticker);
            if (v.Length == 0) return "";
            return "/assets/stickers/" + v + ".svg";
        }
    }
}
