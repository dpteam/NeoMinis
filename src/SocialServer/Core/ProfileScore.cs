using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>
    /// How filled-in a profile is, as a percentage. This is the old-VK joke: a
    /// number that means nothing, computed from the fields somebody bothered to
    /// fill, plus a separate counter for achievements.
    ///
    /// Nothing is stored. Every value is derived from the user row and the badge
    /// list, so the number can never drift out of sync with the profile it
    /// describes and there is no schema to migrate.
    /// </summary>
    public static class ProfileScore
    {
        /// <summary>One thing the profile could have, and what it is worth.</summary>
        public sealed class Item
        {
            public string Key;
            public int Weight;
            public bool Done;
            public Item(string key, int weight, bool done)
            {
                Key = key; Weight = weight; Done = done;
            }
        }

        private static bool Has(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Trim().Length > 0;
        }

        /// <summary>
        /// The full checklist. The weights add up to exactly 100, so the percent
        /// is a plain sum and never needs rounding tricks.
        /// </summary>
        public static List<Item> Checklist(User u, int postCount)
        {
            List<Item> list = new List<Item>(16);
            if (u == null) return list;

            list.Add(new Item("avatar", 15, Has(u.Avatar)));
            list.Add(new Item("cover", 5, Has(u.Cover)));
            list.Add(new Item("bio", 10, Has(u.Bio)));
            list.Add(new Item("status", 10, Has(u.Status)));
            list.Add(new Item("birthday", 10, Has(u.Birthday)));
            list.Add(new Item("city", 5, Has(u.City)));
            list.Add(new Item("country", 5, Has(u.Country)));
            list.Add(new Item("gender", 5, Has(u.Gender)));
            list.Add(new Item("website", 5, Has(u.Website)));
            list.Add(new Item("favGames", 5, Has(u.FavGames)));
            list.Add(new Item("favMovies", 5, Has(u.FavMovies)));
            list.Add(new Item("favMusic", 5, Has(u.FavMusic)));
            list.Add(new Item("favBooks", 5, Has(u.FavBooks)));
            list.Add(new Item("post", 10, postCount > 0));
            return list;
        }

        public static int Percent(List<Item> items)
        {
            if (items == null || items.Count == 0) return 0;
            int done = 0, total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                total += items[i].Weight;
                if (items[i].Done) done += items[i].Weight;
            }
            if (total <= 0) return 0;
            return (done * 100) / total;
        }

        /// <summary>The keys of everything still missing, in checklist order.</summary>
        public static List<string> Missing(List<Item> items)
        {
            List<string> list = new List<string>();
            if (items == null) return list;
            for (int i = 0; i < items.Count; i++)
                if (!items[i].Done) list.Add(items[i].Key);
            return list;
        }

        /// <summary>Comma separated badge ids counted as achievements.</summary>
        public static int BadgeCount(string badges)
        {
            if (string.IsNullOrEmpty(badges)) return 0;
            int n = 0;
            string[] parts = badges.Split(',');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Trim().Length > 0) n++;
            return n;
        }
    }
}
