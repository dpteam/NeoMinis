using System;
using System.Collections.Generic;

namespace Social.Core
{
    /// <summary>One game that ships with the site.</summary>
    public sealed class MiniGame
    {
        public string Id = "";          // "snake" - also the file name under assets/mini
        public string Name = "";
        public string Description = "";
        public string Icon = "";
        /// <summary>Rough length in seconds, shown on the card as an honest warning.</summary>
        public int Seconds;
        /// <summary>Higher is better (every game here scores that way).</summary>
        public bool HigherIsBetter = true;
    }

    /// <summary>
    /// The built-in mini-games. Each one is a real app: seeding creates an App row
    /// (type html, url /play/mini/&lt;id&gt;) so the leaderboard, the score table and
    /// the play session all come from the Applications API rather than a second
    /// parallel system. The games themselves are static files under
    /// public/assets/mini/, so editing a game is editing a file, not a rebuild.
    ///
    /// They are marked <c>Builtin</c> and hidden from the app catalogue: "install"
    /// is the wrong verb for a game that is already there, and nobody should be
    /// able to edit or delete one.
    /// </summary>
    public static class MiniGames
    {
        public static readonly MiniGame[] All = new MiniGame[]
        {
            Game("snake", "Snake", "Eat, grow, do not bite yourself.", "gamepad", 60),
            Game("2048", "2048", "Slide the tiles, make a 2048.", "hashtag", 300),
            Game("memory", "Memory", "Find every pair before the clock runs out.", "clone", 120),
            Game("mines", "Minesweeper", "Nine by nine, ten mines, no luck involved.", "bomb", 180)
        };

        private static MiniGame Game(string id, string name, string description, string icon, int seconds)
        {
            MiniGame g = new MiniGame();
            g.Id = id;
            g.Name = name;
            g.Description = description;
            g.Icon = icon;
            g.Seconds = seconds;
            return g;
        }

        public static MiniGame Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string v = id.Trim().ToLowerInvariant();
            for (int i = 0; i < All.Length; i++) if (All[i].Id == v) return All[i];
            return null;
        }

        /// <summary>
        /// Creates the App row for any game that does not have one yet. Called once
        /// after the store loads. Idempotent by construction: it looks for an app
        /// whose URL is this game's player page, so a restart does not create a
        /// second copy and a deleted row is quietly restored.
        /// </summary>
        public static int Seed(SocialStore store)
        {
            if (store == null) return 0;
            int made = 0;
            for (int i = 0; i < All.Length; i++)
            {
                MiniGame g = All[i];
                string url = "/play/mini/" + g.Id;
                if (store.FindBuiltinApp(url) != null) continue;

                App app = store.CreateApp(0, g.Name, g.Description, g.Icon, "html", url,
                                          "app_" + Crypto.RandomHex(8), "", AppScopes.Score);
                if (app == null) continue;   // storage is read-only right now
                store.MarkAppBuiltin(app);
                made++;
            }
            return made;
        }
    }
}
