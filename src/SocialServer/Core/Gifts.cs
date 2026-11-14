using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>One entry of the built-in gift catalogue.</summary>
    public sealed class GiftKind
    {
        public string Key = "";
        public string Icon = "gift";   // Font Awesome solid name, drawn by the client
        public string Color = "#7c8cff";
    }

    /// <summary>
    /// The gifts people can send each other. Deliberately kept in code: no uploads,
    /// no files to lose on a backup restore, and the whole set is a few hundred
    /// bytes. The client renders the tile from <see cref="Icon"/> and
    /// <see cref="Color"/> and translates "gift.&lt;key&gt;" for the label, so
    /// adding a gift is one line here plus one line per language.
    /// </summary>
    public static class GiftCatalog
    {
        private static readonly GiftKind[] All = new GiftKind[]
        {
            Make("heart",    "heart",              "#e0547a"),
            Make("flower",   "spa",                "#d46bb0"),
            Make("hug",      "hand-holding-heart", "#e08a5a"),
            Make("star",     "star",               "#e8b23a"),
            Make("crown",    "crown",              "#c9a227"),
            Make("cake",     "cake-candles",       "#e8735c"),
            Make("gift",     "gift",               "#7c8cff"),
            Make("coffee",   "mug-hot",            "#a9825c"),
            Make("beer",     "beer-mug-empty",     "#d9a520"),
            Make("icecream", "ice-cream",          "#5bb7d6"),
            Make("rocket",   "rocket",             "#5a8ce0"),
            Make("gem",      "gem",                "#54c3a6"),
            Make("fire",     "fire",               "#e2663c"),
            Make("book",     "book-open",          "#8a7bd6")
        };

        private static GiftKind Make(string key, string icon, string color)
        {
            GiftKind k = new GiftKind();
            k.Key = key;
            k.Icon = icon;
            k.Color = color;
            return k;
        }

        public static List<GiftKind> List()
        {
            return new List<GiftKind>(All);
        }

        public static int Count { get { return All.Length; } }

        /// <summary>Returns the catalogue entry or null for an unknown key.</summary>
        public static GiftKind Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string needle = key.Trim().ToLowerInvariant();
            for (int i = 0; i < All.Length; i++) if (All[i].Key == needle) return All[i];
            return null;
        }

        public static bool IsKnown(string key)
        {
            return Find(key) != null;
        }
    }
}
