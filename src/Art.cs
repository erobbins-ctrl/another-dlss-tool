using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Dlss5Setup
{
    // Cover art for games: Steam's own local cache first, then Steam's CDN, then the game's icon.
    static class Art
    {
        public const string Portrait = "library_600x900";
        public const string Header = "header";

        static string Dir { get { return Path.Combine(Paths.Root, "art"); } }

        // The Steam app id from the game's steamapps folder, if it's a Steam install.
        public static string SteamAppIdFromPath(string exe)
        {
            var m = Regex.Match(exe ?? "", @"^(.*\\steamapps)\\common\\([^\\]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string apps = m.Groups[1].Value, dir = m.Groups[2].Value;
            try
            {
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string t = File.ReadAllText(acf);
                    if (Regex.Match(t, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value.Equals(dir, StringComparison.OrdinalIgnoreCase))
                        return Regex.Match(t, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant().Replace("\u2122", "").Replace("\u00AE", "");
            s = Regex.Replace(s, @"^the\s+", "");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }

        // For games bought elsewhere: look the name up on the Steam store and accept only an exact match.
        public static string SteamAppIdFromStore(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string json;
                using (var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 })
                {
                    wc.Headers.Add("User-Agent", "Another-DLSS-Tool/2.6.0");
                    json = wc.DownloadString("https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(name));
                }
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                object items; if (root == null || !root.TryGetValue("items", out items)) return null;
                string want = Norm(name);
                foreach (var it in ((object[])items).OfType<Dictionary<string, object>>())
                {
                    object n, id;
                    if (it.TryGetValue("name", out n) && Norm(Convert.ToString(n)) == want && it.TryGetValue("id", out id))
                        return Convert.ToString(id);
                }
            }
            catch { }
            return null;
        }

        static string SteamRoot()
        {
            var libs = Detect.SteamLibraries();
            return libs.Count > 0 ? libs[0] : null;
        }

        // Steam keeps library art locally: old clients as <id>_library_600x900.jpg, newer ones in a folder per game.
        static string LocalSteam(string appId, string kind)
        {
            string root = SteamRoot();
            if (root == null) return null;
            string cache = Path.Combine(root, "appcache", "librarycache");
            if (!Directory.Exists(cache)) return null;
            string old = Path.Combine(cache, appId + "_" + kind + ".jpg");
            if (File.Exists(old)) return old;
            string folder = Path.Combine(cache, appId);
            if (!Directory.Exists(folder)) return null;
            try
            {
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileNameWithoutExtension(f).Equals(kind, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch { return null; }
        }

        static byte[] FromCdn(string appId, string kind)
        {
            string file = Path.Combine(Dir, appId + "_" + kind + ".jpg");
            string miss = file + ".missing";
            if (File.Exists(file)) return File.ReadAllBytes(file);
            if (File.Exists(miss) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(miss)).TotalDays < 7) return null;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            foreach (var host in new[]
            {
                "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/",
                "https://cdn.cloudflare.steamstatic.com/steam/apps/",
                "https://steamcdn-a.akamaihd.net/steam/apps/"
            })
            {
                try
                {
                    byte[] b;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "Another-DLSS-Tool/2.6.0");
                        b = wc.DownloadData(host + appId + "/" + kind + ".jpg");
                    }
                    if (b != null && b.Length > 1000)
                    {
                        Directory.CreateDirectory(Dir);
                        File.WriteAllBytes(file, b);
                        return b;
                    }
                }
                catch { }
            }
            try { Directory.CreateDirectory(Dir); File.WriteAllText(miss, ""); } catch { }
            return null;
        }

        static Image Decode(byte[] b, int maxWidth)
        {
            if (b == null) return null;
            try
            {
                using (var raw = Image.FromStream(new MemoryStream(b)))
                {
                    int w = Math.Min(raw.Width, maxWidth), h = Math.Max(1, raw.Height * w / Math.Max(1, raw.Width));
                    return new Bitmap(raw, w, h);
                }
            }
            catch { return null; }
        }

        public static Image Icon(string exe)
        {
            try { using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(exe)) return ic == null ? null : ic.ToBitmap(); }
            catch { return null; }
        }

        // kind is Portrait or Header. Falls back to the other kind, then to the exe's icon.
        public static Image Load(string appId, string exe, string kind)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                foreach (var k in new[] { kind, kind == Portrait ? Header : Portrait })
                {
                    string local = LocalSteam(appId, k);
                    byte[] bytes = null;
                    if (local != null) { try { bytes = File.ReadAllBytes(local); } catch { } }
                    if (bytes == null) bytes = FromCdn(appId, k);
                    var img = Decode(bytes, k == Portrait ? 240 : 460);
                    if (img != null) return img;
                }
            }
            return Icon(exe);
        }
    }
}
