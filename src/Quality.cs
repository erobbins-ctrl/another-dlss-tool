using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Dlss5Setup
{
    // One switch on the Image quality card. Values come from the DLSS5-Feeder README's
    // Configuration and troubleshooting sections; Quality.Docs() re-checks them online.
    class Tweak
    {
        public string Id, Title, Help;
        public Dictionary<string, string> On, Off;   // dlss5-feed.cfg keys
        public bool DgVoodoo;                        // dgVoodoo.conf instead (Direct3D 8/9 only)
    }

    static class Quality
    {
        const string ReadmeUrl = "https://raw.githubusercontent.com/jlrouzies-fr/DLSS5-Feeder/main/README.md";
        public const string DocsUrl = "https://github.com/jlrouzies-fr/DLSS5-Feeder#configuration";

        public static readonly Tweak[] Tweaks =
        {
            new Tweak
            {
                Id = "hold", Title = "Steadier image",
                Help = "Stops still areas from slowly changing brightness after the camera stops. Uses the feeder's output stabiliser at 0.9, where the model's new look fades in over about ten frames.",
                On = new Dictionary<string, string> { { "hold_strength", "0.9" } },
                Off = new Dictionary<string, string> { { "hold_strength", "0" } }
            },
            new Tweak
            {
                Id = "preset", Title = "Less warping around fire, smoke and lights",
                Help = "Switches DLSS to the older CNN preset E, which clamps history harder. The project's fix for smearing around flames, flickering lights and transparent effects.",
                On = new Dictionary<string, string> { { "preset", "5" } },
                Off = new Dictionary<string, string> { { "preset", "0" } }
            },
            new Tweak
            {
                Id = "sharpen", Title = "Sharpen the result",
                Help = "Runs AMD's RCAS sharpening on the final image at strength 0.5. Direct3D 11 path only, which includes Direct3D 9 games through dgVoodoo2.",
                On = new Dictionary<string, string> { { "work_upscale", "1" }, { "work_sharpness", "0.5" }, { "work_resolution", "100" } },
                Off = new Dictionary<string, string> { { "work_upscale", "0" }, { "work_sharpness", "0.3" } }
            },
            new Tweak
            {
                Id = "aniso", Title = "Sharper textures (16x anisotropic filtering)", DgVoodoo = true,
                Help = "Has dgVoodoo2 force 16x anisotropic filtering, so floors and walls seen at an angle stay sharp. Direct3D 8 and 9 games only. Takes effect the next time the game starts.",
                On = new Dictionary<string, string> { { "Filtering", "16" } },
                Off = new Dictionary<string, string> { { "Filtering", "appdriven" } }
            },
        };

        public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "work_resolution", "100" }, { "work_upscale", "0" }, { "work_sharpness", "0.3" }, { "preset", "0" }, { "hold_strength", "0" }
        };

        static string Cfg(GameEntry g) { return Path.Combine(g.Folder, "dlss5-feed.cfg"); }
        static string DgConf(GameEntry g) { return Path.Combine(g.Folder, "dgVoodoo.conf"); }

        // ------------------------------------------------------------ online check

        static HashSet<string> docKeys;

        // Keys the project's README currently documents. Null when offline and nothing is cached.
        public static HashSet<string> Docs(Action<string> log)
        {
            if (docKeys != null) return docKeys;
            string cache = Path.Combine(Paths.Root, "feeder-README.md");
            string text = null;
            try
            {
                if (!File.Exists(cache) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(cache)).TotalHours > 12)
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (var wc = new WebClient()) { wc.Headers.Add("User-Agent", "Another-DLSS-Tool"); text = wc.DownloadString(ReadmeUrl); }
                    File.WriteAllText(cache, text);
                }
            }
            catch (Exception ex) { if (log != null) log("Couldn't fetch the project's settings guide: " + ex.Message); }
            if (text == null && File.Exists(cache)) text = File.ReadAllText(cache);
            if (text == null) return null;
            docKeys = new HashSet<string>(Regex.Matches(text, @"`([a-z_]+)`").Cast<Match>().Select(m => m.Groups[1].Value));
            return docKeys;
        }

        // ------------------------------------------------------------ dlss5-feed.cfg (key=value)

        public static Dictionary<string, string> ReadCfg(GameEntry g)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string p = Cfg(g);
            if (!File.Exists(p)) return map;
            foreach (var raw in File.ReadAllLines(p))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        public static void WriteCfg(GameEntry g, Dictionary<string, string> values)
        {
            string p = Cfg(g);
            var lines = File.Exists(p) ? File.ReadAllLines(p).ToList() : new List<string>();
            var left = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("#") || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string k = t.Substring(0, eq).Trim();
                string v;
                if (left.TryGetValue(k, out v)) { lines[i] = k + "=" + v; left.Remove(k); }
            }
            foreach (var kv in left) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(p, lines);
        }

        // ------------------------------------------------------------ dgVoodoo.conf ([Section] / Key = Value)

        public static bool HasDgVoodoo(GameEntry g) { return File.Exists(DgConf(g)); }

        public static string ReadDg(GameEntry g, string section, string key)
        {
            string p = DgConf(g); if (!File.Exists(p)) return null;
            string cur = null;
            foreach (var raw in File.ReadAllLines(p))
            {
                string t = raw.Trim();
                if (t.StartsWith("[")) { cur = t.Trim('[', ']'); continue; }
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(cur, section, StringComparison.OrdinalIgnoreCase) &&
                    t.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return t.Substring(eq + 1).Trim();
            }
            return null;
        }

        public static void WriteDg(GameEntry g, string section, string key, string value)
        {
            string p = DgConf(g); if (!File.Exists(p)) throw new FileNotFoundException("dgVoodoo.conf isn't in the game folder.");
            var lines = File.ReadAllLines(p).ToList();
            string cur = null; int sectionEnd = -1; bool done = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("["))
                {
                    if (string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) sectionEnd = i;
                    cur = t.Trim('[', ']'); continue;
                }
                int eq = lines[i].IndexOf('=');
                if (eq > 0 && string.Equals(cur, section, StringComparison.OrdinalIgnoreCase) &&
                    lines[i].Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = lines[i].Substring(0, eq + 1) + " " + value;   // keep dgVoodoo's alignment
                    done = true; break;
                }
            }
            if (!done)
            {
                if (string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) sectionEnd = lines.Count;
                if (sectionEnd < 0) { lines.Add(""); lines.Add("[" + section + "]"); sectionEnd = lines.Count; }
                lines.Insert(sectionEnd, key + " = " + value);
            }
            File.WriteAllLines(p, lines, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ tweaks

        public static bool IsOn(GameEntry g, Tweak t)
        {
            if (t.DgVoodoo)
            {
                var v = ReadDg(g, "DirectX", "Filtering");
                return v != null && v.Trim() == t.On["Filtering"];
            }
            var cfg = ReadCfg(g);
            return t.On.All(kv => { string v; return cfg.TryGetValue(kv.Key, out v) && Same(v, kv.Value); });
        }

        static bool Same(string a, string b)
        {
            double x, y;
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                return Math.Abs(x - y) < 1e-6;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Returns the keys skipped because the online guide no longer documents them.
        public static List<string> Apply(GameEntry g, Tweak t, bool on, Action<string> log)
        {
            var values = on ? t.On : t.Off;
            if (t.DgVoodoo)
            {
                foreach (var kv in values) WriteDg(g, "DirectX", kv.Key, kv.Value);
                return new List<string>();
            }
            var docs = Docs(log);
            var skipped = docs == null ? new List<string>() : values.Keys.Where(k => !docs.Contains(k)).ToList();
            var use = values.Where(kv => !skipped.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (use.Count > 0) WriteCfg(g, use);
            return skipped;
        }

        public static void Reset(GameEntry g, Action<string> log)
        {
            WriteCfg(g, Defaults);
            if (HasDgVoodoo(g)) WriteDg(g, "DirectX", "Filtering", "appdriven");
        }
    }
}
