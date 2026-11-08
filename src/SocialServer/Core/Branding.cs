using System;
using System.Globalization;
using System.Text;

namespace Social.Core
{
    /// <summary>
    /// Optional brand palette. The repository ships a neutral theme; a deployment can
    /// override accent and surface colours from the [branding] section of config.ini.
    /// The result is served as /brand.css, so nothing in public/ has to be edited and
    /// the source stays branding-neutral.
    /// </summary>
    public static class Branding
    {
        public static string Css(Config cfg)
        {
            if (cfg == null || !cfg.HasBranding) return "/* neutral palette: config.ini [branding] is empty */\n";

            StringBuilder sb = new StringBuilder(2048);
            sb.Append("/* generated from config.ini [branding] - do not edit by hand */\n");

            AppendTheme(sb, "light", false,
                cfg.BrandAccent, cfg.BrandAccentHi, cfg.BrandAccentLo,
                cfg.BrandLightBg, "", "");

            AppendTheme(sb, "dark", true,
                First(cfg.BrandDarkAccent, cfg.BrandAccent),
                First(cfg.BrandDarkAccentHi, cfg.BrandAccentHi),
                First(cfg.BrandDarkAccentLo, cfg.BrandAccentLo),
                cfg.BrandDarkBg, cfg.BrandDarkPanel, cfg.BrandDarkPanel2);

            AppendTheme(sb, "oled", true,
                First(cfg.BrandOledAccent, cfg.BrandDarkAccent, cfg.BrandAccent),
                First(cfg.BrandOledAccentHi, cfg.BrandDarkAccentHi, cfg.BrandAccentHi),
                First(cfg.BrandOledAccentLo, cfg.BrandDarkAccentLo, cfg.BrandAccentLo),
                cfg.BrandOledBg, cfg.BrandOledPanel, "");

            return sb.ToString();
        }

        private static string First(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrEmpty(values[i])) return values[i];
            return "";
        }

        private static void AppendTheme(StringBuilder sb, string theme, bool dark,
                                        string accent, string hi, string lo,
                                        string bg, string panel, string panel2)
        {
            string a = Config.NormalizeColor(accent);
            string b = Config.NormalizeColor(bg);
            string p = Config.NormalizeColor(panel);
            string p2 = Config.NormalizeColor(panel2);
            if (a.Length == 0 && b.Length == 0 && p.Length == 0) return;

            StringBuilder body = new StringBuilder(768);
            if (a.Length > 0)
            {
                string h = Config.NormalizeColor(hi);
                string l = Config.NormalizeColor(lo);
                if (h.Length == 0) h = Mix(a, "#ffffff", 0.22);
                if (l.Length == 0) l = Mix(a, "#000000", 0.28);

                body.Append("--accent:").Append(a).Append(';');
                body.Append("--accent-hi:").Append(h).Append(';');
                body.Append("--accent-lo:").Append(l).Append(';');
                body.Append("--link:").Append(dark ? h : a).Append(';');
                body.Append("--link-hi:").Append(dark ? Mix(h, "#ffffff", 0.25) : Mix(a, "#000000", 0.18)).Append(';');
                body.Append("--accent-soft:").Append(Rgba(a, dark ? 0.16 : 0.11)).Append(';');
                if (dark)
                {
                    // dark chrome: a deep navy bar with the accent only as a glow,
                    // otherwise a saturated bar overwhelms the whole layout
                    string bar = Mix(l, "#000000", 0.55);
                    string barHi = Mix(a, "#000000", 0.62);
                    body.Append("--topbar:linear-gradient(").Append(barHi).Append(',').Append(bar).Append(");");
                    body.Append("--topbar-text:#dce9f8;");
                    body.Append("--head-strong:linear-gradient(").Append(Mix(a, "#000000", 0.25)).Append(',')
                        .Append(Mix(l, "#000000", 0.2)).Append(");");
                }
                else
                {
                    body.Append("--topbar:linear-gradient(").Append(h).Append(',').Append(a).Append(" 55%,").Append(l).Append(");");
                    body.Append("--head-strong:linear-gradient(").Append(h).Append(',').Append(a).Append(" 55%,").Append(l).Append(");");
                }
                body.Append("--cover:linear-gradient(120deg,").Append(Mix(h, "#000000", dark ? 0.72 : 0)).Append(',')
                    .Append(Mix(a, "#000000", dark ? 0.8 : 0)).Append(" 45%,")
                    .Append(Mix(l, "#000000", dark ? 0.78 : 0)).Append(");");
                body.Append("--boot:linear-gradient(").Append(h).Append(',').Append(a).Append(" 60%,").Append(l).Append(");");
            }

            if (b.Length > 0) body.Append("--bg:").Append(b).Append(';');

            if (p.Length > 0)
            {
                body.Append("--panel:").Append(p).Append(';');
                body.Append("--head:linear-gradient(")
                    .Append(Mix(p, dark ? "#ffffff" : "#000000", 0.06)).Append(',')
                    .Append(Mix(p, "#000000", 0.04)).Append(");");
                if (p2.Length == 0) body.Append("--panel-2:").Append(Mix(p, dark ? "#ffffff" : "#000000", 0.05)).Append(';');
            }

            if (p2.Length > 0)
            {
                body.Append("--panel-2:").Append(p2).Append(';');
                body.Append("--panel-3:").Append(Mix(p2, "#000000", 0.12)).Append(';');
                body.Append("--hover:").Append(Mix(p2, dark ? "#ffffff" : "#000000", 0.07)).Append(';');
            }

            if (body.Length == 0) return;
            sb.Append("html[data-theme=\"").Append(theme).Append("\"]{").Append(body).Append("}\n");
        }

        private static string Rgba(string hex, double alpha)
        {
            int r, g, b;
            if (!Parse(hex, out r, out g, out b)) return "transparent";
            return "rgba(" + r.ToString(CultureInfo.InvariantCulture) + "," +
                   g.ToString(CultureInfo.InvariantCulture) + "," +
                   b.ToString(CultureInfo.InvariantCulture) + "," +
                   alpha.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>Linear mix towards target by amount (0..1).</summary>
        private static string Mix(string from, string target, double amount)
        {
            int r1, g1, b1, r2, g2, b2;
            if (!Parse(from, out r1, out g1, out b1)) return from;
            if (!Parse(target, out r2, out g2, out b2)) return from;
            if (amount < 0) amount = 0;
            if (amount > 1) amount = 1;
            int r = (int)Math.Round(r1 + (r2 - r1) * amount);
            int g = (int)Math.Round(g1 + (g2 - g1) * amount);
            int b = (int)Math.Round(b1 + (b2 - b1) * amount);
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture) +
                         g.ToString("x2", CultureInfo.InvariantCulture) +
                         b.ToString("x2", CultureInfo.InvariantCulture);
        }

        private static bool Parse(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            string v = Config.NormalizeColor(hex);
            if (v.Length != 7) return false;
            r = Convert.ToInt32(v.Substring(1, 2), 16);
            g = Convert.ToInt32(v.Substring(3, 2), 16);
            b = Convert.ToInt32(v.Substring(5, 2), 16);
            return true;
        }
    }
}
