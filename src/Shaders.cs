using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace Dlss5Setup
{
    class ShaderPack
    {
        public string Name, Author, Description, Tip, ZipUrl, Repo;
        public string ShaderPath = Path.Combine("reshade-shaders", "Shaders"), TexturePath = Path.Combine("reshade-shaders", "Textures");
        public string[] Deny = new string[0];
        public string[] EffectFiles = new string[0];
        public int Rank = 1000;
        public string LocalPath;   // set for a pack found on this PC
        public bool Local { get { return LocalPath != null; } }
        public bool Featured { get { return Rank < 1000; } }
    }

    [DataContract]
    class ShaderRecord
    {
        [DataMember] public Dictionary<string, string[]> Installed = new Dictionary<string, string[]>();
    }

    static class Shaders
    {
        public const string HeadersName = "ReShade standard headers";
        public const string PresetPrefix = "Preset: ";
        public const string ListUrl = "https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini";
        const string HeadersRaw = "https://raw.githubusercontent.com/crosire/reshade-shaders/slim/Shaders/";
        static string Cache { get { return Path.Combine(Paths.Root, "EffectPackages.ini"); } }

        // The top picks, in display order. A null note keeps the pack's own description.
        static readonly string[][] Picks =
        {
            new[] { "iMMERSE", "MXAO ambient occlusion with screen-space bounce lighting, SOLARIS bloom, SMAA and sharpening. Enable iMMERSE LAUNCHPAD and drag it to the top of the effect list; MXAO needs it." },
            new[] { "NiceGuy Shaders", "NGLighting adds screen-space global illumination and reflections. Also volumetric fog, rim light and lamps." },
            new[] { "qUINT", "The predecessor to iMMERSE: MXAO, Lightroom colour grading, ADOF depth of field, screen-space reflections and bloom." },
            new[] { "METEOR", null },
            new[] { "ZenteonFX Shaders", null },
            new[] { "SweetFX", "The classics: Vibrance, Curves, Tonemap, Levels, LumaSharpen and SMAA." },
            new[] { "Color effects", "A full colour-grading suite: levels, curves, LUTs, white balance and film grain." },
            new[] { "AstrayFX", null },
            new[] { "OtisFX", null },
            new[] { "Standard effects", "Small utilities that many presets expect. DisplayDepth shows whether ReShade sees the depth buffer, which the feeder relies on." },
        };

        // Not on ReShade's list, added by hand.
        static readonly ShaderPack NiceGuy = new ShaderPack
        {
            Name = "NiceGuy Shaders", Author = "mj-ehsan",
            ZipUrl = "https://codeload.github.com/mj-ehsan/NiceGuy-Shaders/zip/refs/heads/main",
            Repo = "https://github.com/mj-ehsan/NiceGuy-Shaders"
        };

        // LumeniteFX is installed and managed by the feeder itself.
        static readonly Regex Excluded = new Regex(@"^LumeniteFX", RegexOptions.IgnoreCase);

        static readonly Regex ShaderExt = new Regex(@"\.(fx|fxh)$", RegexOptions.IgnoreCase);
        static readonly Regex TextureExt = new Regex(@"\.(png|jpg|jpeg|dds|bmp|tga)$", RegexOptions.IgnoreCase);
        static readonly Regex RtgiName = new Regex(@"(rtgi|immerse[\s_\-]*(pro|ultimate))", RegexOptions.IgnoreCase);

        // ------------------------------------------------------------ catalogue

        public static List<ShaderPack> Load(bool refresh, Action<string> log)
        {
            string text = null;
            bool stale = !File.Exists(Cache) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(Cache)).TotalHours > 24;
            if (refresh || stale)
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "Another-DLSS-Tool");
                        text = wc.DownloadString(ListUrl);
                    }
                    Directory.CreateDirectory(Paths.Root);
                    File.WriteAllText(Cache, text);
                }
                catch (Exception ex) { if (log != null) log("Couldn't refresh ReShade's shader list: " + ex.Message); }
            }
            if (text == null && File.Exists(Cache)) text = File.ReadAllText(Cache);
            var list = text == null ? new List<ShaderPack>() : Parse(text);
            if (!list.Any(p => p.Name.Equals(NiceGuy.Name, StringComparison.OrdinalIgnoreCase))) list.Add(NiceGuy);
            foreach (var p in list)
                for (int i = 0; i < Picks.Length; i++)
                    if (p.Name.Equals(Picks[i][0], StringComparison.OrdinalIgnoreCase)) { p.Rank = i; p.Tip = Picks[i][1]; break; }
            return list.OrderBy(p => p.Rank).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static string RelPath(string v)
        {
            v = v.Replace('/', '\\');
            if (v.StartsWith(".\\")) v = v.Substring(2);
            return v.TrimEnd('\\').Replace('\\', Path.DirectorySeparatorChar);
        }

        public static List<ShaderPack> Parse(string ini)
        {
            var list = new List<ShaderPack>();
            ShaderPack cur = null;
            foreach (var raw in ini.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[")) { cur = new ShaderPack(); list.Add(cur); continue; }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "PackageName":
                        int by = v.IndexOf(" by ", StringComparison.Ordinal);
                        cur.Name = by > 0 ? v.Substring(0, by).Trim() : v;
                        cur.Author = by > 0 ? v.Substring(by + 4).Trim() : null;
                        break;
                    case "PackageDescription": cur.Description = v; break;
                    case "DownloadUrl": cur.ZipUrl = v; break;
                    case "RepositoryUrl": cur.Repo = v; break;
                    case "InstallPath": if (v.Length > 0) cur.ShaderPath = RelPath(v); break;
                    case "TextureInstallPath": if (v.Length > 0) cur.TexturePath = RelPath(v); break;
                    case "EffectFiles": cur.EffectFiles = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); break;
                    case "DenyEffectFiles": cur.Deny = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); break;
                }
            }
            return list.Where(p => p.Name != null && p.ZipUrl != null && !Excluded.IsMatch(p.Name)).ToList();
        }

        // ------------------------------------------------------------ records

        static string RecordFile(GameEntry g) { return Path.Combine(Paths.State(g), "shaders.json"); }

        public static ShaderRecord Record(GameEntry g)
        {
            var r = Json.Read<ShaderRecord>(RecordFile(g)) ?? new ShaderRecord();
            if (r.Installed == null) r.Installed = new Dictionary<string, string[]>();
            return r;
        }

        public static bool IsInstalled(GameEntry g, string name)
        {
            string[] files;
            return Record(g).Installed.TryGetValue(name, out files) && files.Any(f => File.Exists(Path.Combine(g.Folder, f)));
        }

        public static int InstalledCount(GameEntry g)
        {
            return Record(g).Installed.Keys.Count(k => k != HeadersName && !k.StartsWith(PresetPrefix) && IsInstalled(g, k));
        }

        // ------------------------------------------------------------ finding a paid RTGI copy

        public static List<string> FindRtgi(IEnumerable<GameEntry> games)
        {
            var roots = new List<KeyValuePair<string, int>>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var sub in new[] { "Downloads", "Desktop", "Documents", "OneDrive", @"OneDrive\Desktop", @"OneDrive\Documents" })
                roots.Add(new KeyValuePair<string, int>(Path.Combine(home, sub), 5));
            foreach (var g in games)
                if (g.Folder != null) roots.Add(new KeyValuePair<string, int>(Path.Combine(g.Folder, "reshade-shaders"), 3));

            var hits = new List<string>();
            foreach (var r in roots.Where(r => Directory.Exists(r.Key)))
                foreach (var f in Detect.Walk(r.Key, r.Value, false))
                {
                    string n = Path.GetFileName(f);
                    if (!RtgiName.IsMatch(n) || !Regex.IsMatch(n, @"\.(fx|zip|7z|rar)$", RegexOptions.IgnoreCase)) continue;
                    if (f.IndexOf(Paths.Root, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!hits.Any(h => h.Equals(f, StringComparison.OrdinalIgnoreCase))) hits.Add(f);
                }
            return hits.OrderBy(h => h.IndexOf("reshade-shaders", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                       .ThenByDescending(h => { try { return File.GetLastWriteTimeUtc(h); } catch { return DateTime.MinValue; } })
                       .ToList();
        }

        // ------------------------------------------------------------ collecting files

        class Item { public string Source, Rel; }

        static bool IsUnder(string path, string ancestor)
        {
            return path.Length > ancestor.Length && path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase) &&
                   (path[ancestor.Length] == '\\' || path[ancestor.Length] == '/');
        }

        static List<Item> FromFolder(string root, ShaderPack pack)
        {
            var items = new List<Item>();
            var deny = new HashSet<string>(pack.Deny, StringComparer.OrdinalIgnoreCase);
            // Only the outermost "Shaders" folders; nested ones are part of them.
            var all = Directory.GetDirectories(root, "Shaders", SearchOption.AllDirectories);
            var shaderDirs = all.Where(d => !all.Any(o => o != d && IsUnder(d, o))).ToList();
            if (shaderDirs.Count > 0)
            {
                foreach (var sd in shaderDirs)
                {
                    foreach (var f in Directory.GetFiles(sd, "*", SearchOption.AllDirectories))
                    {
                        if (deny.Contains(Path.GetFileName(f))) continue;
                        if (ShaderExt.IsMatch(f) || TextureExt.IsMatch(f))
                            items.Add(new Item { Source = f, Rel = Path.Combine(pack.ShaderPath, f.Substring(sd.Length).TrimStart('\\', '/')) });
                    }
                    string td = Path.Combine(Path.GetDirectoryName(sd), "Textures");
                    if (Directory.Exists(td))
                        foreach (var f in Directory.GetFiles(td, "*", SearchOption.AllDirectories).Where(f => TextureExt.IsMatch(f)))
                            items.Add(new Item { Source = f, Rel = Path.Combine(pack.TexturePath, f.Substring(td.Length).TrimStart('\\', '/')) });
                }
            }
            else
            {
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (deny.Contains(Path.GetFileName(f))) continue;
                    if (ShaderExt.IsMatch(f)) items.Add(new Item { Source = f, Rel = Path.Combine(pack.ShaderPath, Path.GetFileName(f)) });
                    else if (TextureExt.IsMatch(f) && Regex.IsMatch(f, @"[\\/]Textures[\\/]", RegexOptions.IgnoreCase))
                        items.Add(new Item { Source = f, Rel = Path.Combine(pack.TexturePath, Path.GetFileName(f)) });
                }
            }
            return items;
        }

        // A single .fx: the file, its MartysMods/qUINT header folder, and matching textures beside it.
        static List<Item> FromFx(string fx)
        {
            var items = new List<Item> { new Item { Source = fx, Rel = Path.Combine("reshade-shaders", "Shaders", Path.GetFileName(fx)) } };
            string dir = Path.GetDirectoryName(fx);
            foreach (var sub in new[] { "MartysMods", "qUINT" })
            {
                string hd = Path.Combine(dir, sub);
                if (Directory.Exists(hd))
                    foreach (var f in Directory.GetFiles(hd, "*", SearchOption.AllDirectories).Where(f => ShaderExt.IsMatch(f)))
                        items.Add(new Item { Source = f, Rel = Path.Combine("reshade-shaders", "Shaders", sub, f.Substring(hd.Length).TrimStart('\\', '/')) });
            }
            foreach (var f in Directory.GetFiles(dir).Where(f => Regex.IsMatch(Path.GetFileName(f), @"^(qUINT_common|MartysMods_common)", RegexOptions.IgnoreCase) && ShaderExt.IsMatch(f)))
                items.Add(new Item { Source = f, Rel = Path.Combine("reshade-shaders", "Shaders", Path.GetFileName(f)) });
            string td = Path.Combine(Path.GetDirectoryName(dir) ?? dir, "Textures");
            if (Directory.Exists(td))
                foreach (var f in Directory.GetFiles(td).Where(f => TextureExt.IsMatch(f) && Regex.IsMatch(Path.GetFileName(f), @"(immerse|bluenoise|rtgi|mmx|quint)", RegexOptions.IgnoreCase)))
                    items.Add(new Item { Source = f, Rel = Path.Combine("reshade-shaders", "Textures", Path.GetFileName(f)) });
            return items;
        }

        static void Download(string url, string file, Action<string> log, string what)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            log("Downloading " + what + " from " + new Uri(url).Host);
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Another-DLSS-Tool");
                wc.DownloadFile(url, file);
            }
        }

        // ------------------------------------------------------------ install / remove

        public static int Install(GameEntry g, ShaderPack pack, Action<string> log)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dlss5-shaders-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                List<Item> items;
                if (!pack.Local)
                {
                    string zip = Path.Combine(tmp, "pack.zip");
                    Download(pack.ZipUrl, zip, log, pack.Name);
                    string dir = Path.Combine(tmp, "x");
                    ZipFile.ExtractToDirectory(zip, dir);
                    items = FromFolder(dir, pack);
                }
                else
                {
                    string src = pack.LocalPath, ext = Path.GetExtension(src).ToLowerInvariant();
                    if (!File.Exists(src)) throw new FileNotFoundException("The file isn't there any more: " + src);
                    log("Using your copy: " + src);
                    if (ext == ".fx") items = FromFx(src);
                    else
                    {
                        string dir = Path.Combine(tmp, "x");
                        Directory.CreateDirectory(dir);
                        if (ext == ".zip") ZipFile.ExtractToDirectory(src, dir);
                        else
                        {
                            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                            if (!File.Exists(tar)) throw new InvalidOperationException("Windows can't open " + ext + " files here. Extract it, then pick the RTGI .fx file.");
                            using (var p = Process.Start(new ProcessStartInfo(tar, "-xf " + Env.Q(src) + " -C " + Env.Q(dir)) { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
                        }
                        items = FromFolder(dir, pack);
                    }
                }
                if (items.Count == 0) throw new InvalidOperationException("No shader files were found in " + pack.Name + ".");
                int n = Place(g, pack.Name, items, log);
                EnsureHeaders(g, log);
                EnsureSearchPaths(g, log);
                return n;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        static int Place(GameEntry g, string packName, List<Item> items, Action<string> log, bool allowEmpty = false)
        {
            var rec = Record(g);
            var owned = new HashSet<string>(rec.Installed.Values.SelectMany(x => x), StringComparer.OrdinalIgnoreCase);
            // Your paid RTGI copy ships newer shared headers; a free pack must not overwrite them.
            string[] rtgiFiles;
            var protect = packName != "RTGI" && rec.Installed.TryGetValue("RTGI", out rtgiFiles)
                ? new HashSet<string>(rtgiFiles, StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
            var written = new List<string>();
            int skipped = 0;
            foreach (var it in items.GroupBy(i => i.Rel, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
            {
                string target = Path.Combine(g.Folder, it.Rel);
                if (File.Exists(target) && (!owned.Contains(it.Rel) || protect.Contains(it.Rel))) { skipped++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(it.Source, target, true);
                written.Add(it.Rel);
            }
            if (written.Count == 0)
            {
                if (allowEmpty) { log("  Its shader files are already in this game."); return 0; }
                throw new InvalidOperationException("Every file in " + packName + " is already in this game from somewhere else, so nothing was changed.");
            }
            rec.Installed[packName] = written.ToArray();
            Json.Write(RecordFile(g), rec);
            log("  Added " + written.Count + " files to reshade-shaders" + (skipped > 0 ? " (kept " + skipped + " that were already there)" : ""));
            return written.Count;
        }

        // ReShade.fxh and ReShadeUI.fxh are needed by most packs; fetch them if the game lacks them.
        static void EnsureHeaders(GameEntry g, Action<string> log)
        {
            string shaders = Path.Combine(g.Folder, "reshade-shaders", "Shaders");
            var need = new[] { "ReShade.fxh", "ReShadeUI.fxh" }.Where(h => !File.Exists(Path.Combine(shaders, h))).ToList();
            if (need.Count == 0) return;
            string tmp = Path.Combine(Path.GetTempPath(), "dlss5-hdr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var rec = Record(g);
                string[] prev;
                var all = rec.Installed.TryGetValue(HeadersName, out prev) ? prev.ToList() : new List<string>();
                foreach (var h in need)
                {
                    string f = Path.Combine(tmp, h);
                    Download(HeadersRaw + h, f, log, h);
                    string rel = Path.Combine("reshade-shaders", "Shaders", h);
                    Directory.CreateDirectory(shaders);
                    File.Copy(f, Path.Combine(g.Folder, rel), true);
                    if (!all.Contains(rel)) all.Add(rel);
                }
                rec.Installed[HeadersName] = all.ToArray();
                Json.Write(RecordFile(g), rec);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Packs that install into their own subfolder need ReShade to search subfolders.
        static void EnsureSearchPaths(GameEntry g, Action<string> log)
        {
            string ini = Path.Combine(g.Folder, "ReShade.ini");
            if (!File.Exists(ini)) { log("  ReShade.ini isn't there yet; launch the game once if effects don't show up."); return; }
            var lines = File.ReadAllLines(ini).ToList();
            bool changed = false;
            changed |= AddPath(lines, "EffectSearchPaths", @".\reshade-shaders\Shaders\**");
            changed |= AddPath(lines, "TextureSearchPaths", @".\reshade-shaders\Textures\**");
            if (changed)
            {
                File.WriteAllLines(ini, lines, new UTF8Encoding(false));
                log("  Told ReShade to also look in reshade-shaders' subfolders.");
            }
        }

        static bool AddPath(List<string> lines, string key, string path)
        {
            int general = lines.FindIndex(l => l.Trim().Equals("[GENERAL]", StringComparison.OrdinalIgnoreCase));
            int idx = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                string value = lines[idx].Substring(lines[idx].IndexOf('=') + 1);
                var parts = value.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                string bare = path.TrimEnd('*').TrimEnd('\\');
                if (parts.Any(p => p.TrimEnd('*').TrimEnd('\\').Equals(bare, StringComparison.OrdinalIgnoreCase) && p.EndsWith("**"))) return false;
                parts.RemoveAll(p => p.TrimEnd('*').TrimEnd('\\').Equals(bare, StringComparison.OrdinalIgnoreCase));
                parts.Add(path);
                lines[idx] = key + "=" + string.Join(",", parts);
                return true;
            }
            if (general < 0) { lines.Insert(0, "[GENERAL]"); general = 0; }
            lines.Insert(general + 1, key + "=" + path);
            return true;
        }

        // Shader and texture files that came inside a downloaded preset.
        public static int InstallFolder(GameEntry g, string packName, string root, Action<string> log)
        {
            var items = FromFolder(root, new ShaderPack { Name = packName });
            if (items.Count == 0) return 0;
            int n = Place(g, packName, items, log, true);
            EnsureHeaders(g, log);
            EnsureSearchPaths(g, log);
            return n;
        }

        public static int Remove(GameEntry g, string packName)
        {
            var rec = Record(g);
            string[] files;
            if (!rec.Installed.TryGetValue(packName, out files)) return 0;
            rec.Installed.Remove(packName);
            // Files another pack also installed stay.
            var stillUsed = new HashSet<string>(rec.Installed.Values.SelectMany(x => x), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var f in files.Where(f => !stillUsed.Contains(f)))
            {
                string p = Path.Combine(g.Folder, f);
                if (File.Exists(p)) { File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); n++; }
            }
            if (rec.Installed.Keys.All(k => k == HeadersName))
            {
                // Nothing left needs the headers this app downloaded.
                foreach (var f in rec.Installed.Values.SelectMany(x => x))
                {
                    string p = Path.Combine(g.Folder, f);
                    try { if (File.Exists(p)) { File.Delete(p); n++; } } catch { }
                }
                rec.Installed.Clear();
            }
            Json.Write(RecordFile(g), rec);
            return n;
        }

        public static void RemoveAll(GameEntry g)
        {
            foreach (var files in Record(g).Installed.Values)
                foreach (var f in files)
                {
                    string p = Path.Combine(g.Folder, f);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
        }
    }
}
