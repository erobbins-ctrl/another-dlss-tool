using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace Dlss5Setup
{
    [DataContract]
    class PresetEntry
    {
        [DataMember] public string Name, Source, Domain, PrevPresetPath, Image;
        [DataMember] public string[] Inis;
        [DataMember] public int ModId, FileId;
    }

    [DataContract]
    class PresetRecord
    {
        [DataMember] public List<PresetEntry> Items = new List<PresetEntry>();
    }

    class PresetResult
    {
        public string Name, Active;
        public List<string> Inis = new List<string>(), AddedPacks = new List<string>(), Missing = new List<string>(), TurnedOff = new List<string>();
        public int ShaderFiles;
    }

    static class Presets
    {
        // Effects that fight neural rendering: anti-aliasing, sharpening and grain.
        static readonly Regex Clash = new Regex(@"(smaa|fxaa|dlaa|(^|[^a-z])taa|sharp|(^|[^a-z])cas([^a-z]|$)|grain)", RegexOptions.IgnoreCase);

        static string RecordFile(GameEntry g) { return Path.Combine(Paths.State(g), "presets.json"); }

        public static PresetRecord Record(GameEntry g)
        {
            var r = Json.Read<PresetRecord>(RecordFile(g)) ?? new PresetRecord();
            if (r.Items == null) r.Items = new List<PresetEntry>();
            return r;
        }

        static void Save(GameEntry g, PresetRecord r) { Json.Write(RecordFile(g), r); }

        public static PresetEntry ForMod(GameEntry g, string domain, int modId)
        {
            return Record(g).Items.FirstOrDefault(e => e.ModId == modId && string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------ archives

        public static string Extract(string archive, string dest)
        {
            Directory.CreateDirectory(dest);
            string ext = Path.GetExtension(archive).ToLowerInvariant();
            if (ext == ".zip") { ZipFile.ExtractToDirectory(archive, dest); return dest; }
            if (ext == ".ini" || ext == ".txt") { File.Copy(archive, Path.Combine(dest, Path.GetFileName(archive))); return dest; }
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (File.Exists(tar))
                using (var p = Process.Start(new ProcessStartInfo(tar, "-xf " + Env.Q(archive) + " -C " + Env.Q(dest)) { UseShellExecute = false, CreateNoWindow = true }))
                    p.WaitForExit();
            if (!Directory.EnumerateFileSystemEntries(dest).Any())
                throw new InvalidOperationException("Windows couldn't open this " + ext + " file. Extract it with 7-Zip, then use Install from a folder.");
            return dest;
        }

        static bool IsPreset(string file)
        {
            string name = Path.GetFileName(file);
            if (Regex.IsMatch(name, @"^(reshade|dgvoodoo|dlss5-feed|optiscaler)", RegexOptions.IgnoreCase) && !name.ToLowerInvariant().Contains("preset")) return false;
            try
            {
                string text = File.ReadAllText(file);
                return Regex.IsMatch(text, @"^\s*Techniques\s*=", RegexOptions.Multiline) && !Regex.IsMatch(text, @"^\s*EffectSearchPaths\s*=", RegexOptions.Multiline);
            }
            catch { return false; }
        }

        public static List<string> FindInis(string root)
        {
            return Directory.GetFiles(root, "*.ini", SearchOption.AllDirectories).Where(IsPreset).OrderBy(f => f.Length).ThenBy(f => f).ToList();
        }

        // Effect files a preset turns on (Techniques=Name@File.fx,...).
        static List<string> Techniques(string ini)
        {
            var list = new List<string>();
            foreach (var line in File.ReadAllLines(ini))
            {
                var m = Regex.Match(line, @"^\s*Techniques\s*=(.*)$");
                if (m.Success) list.AddRange(m.Groups[1].Value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0));
            }
            return list;
        }

        static string FileOf(string technique)
        {
            int at = technique.IndexOf('@');
            return at >= 0 ? technique.Substring(at + 1).Trim() : null;
        }

        static void TurnOff(string ini, HashSet<string> off)
        {
            var lines = File.ReadAllLines(ini);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^(\s*Techniques\s*=)(.*)$");
                if (!m.Success) continue;
                var keep = m.Groups[2].Value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0 && !off.Contains(t));
                lines[i] = m.Groups[1].Value + string.Join(",", keep);
            }
            File.WriteAllLines(ini, lines, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ ReShade.ini PresetPath

        static string ReShadeIni(GameEntry g) { return Path.Combine(g.Folder, "ReShade.ini"); }

        public static string ActivePreset(GameEntry g)
        {
            string ini = ReShadeIni(g);
            if (!File.Exists(ini)) return null;
            var line = File.ReadAllLines(ini).FirstOrDefault(l => l.TrimStart().StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase));
            return line == null ? null : line.Substring(line.IndexOf('=') + 1).Trim();
        }

        static bool Points(string presetPath, string iniName)
        {
            return presetPath != null && Path.GetFileName(presetPath.Replace('\\', Path.DirectorySeparatorChar)).Equals(iniName, StringComparison.OrdinalIgnoreCase);
        }

        public static void SetActive(GameEntry g, string value)
        {
            string ini = ReShadeIni(g);
            if (!File.Exists(ini)) throw new InvalidOperationException("ReShade.ini isn't in the game folder yet. Start the game once with the feeder installed, then try again.");
            var lines = File.ReadAllLines(ini).ToList();
            int idx = lines.FindIndex(l => l.TrimStart().StartsWith("PresetPath=", StringComparison.OrdinalIgnoreCase));
            if (value == null) { if (idx >= 0) lines.RemoveAt(idx); }
            else if (idx >= 0) lines[idx] = "PresetPath=" + value;
            else
            {
                int general = lines.FindIndex(l => l.Trim().Equals("[GENERAL]", StringComparison.OrdinalIgnoreCase));
                if (general < 0) { lines.Insert(0, "[GENERAL]"); general = 0; }
                lines.Insert(general + 1, "PresetPath=" + value);
            }
            File.WriteAllLines(ini, lines, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ install / remove

        public static PresetResult Install(GameEntry g, string root, string name, string source, string domain, int modId, int fileId,
                                           List<ShaderPack> catalog, Func<List<string>, bool> askTurnOff, Action<string> log)
        {
            var inis = FindInis(root);
            if (inis.Count == 0) throw new InvalidOperationException("There's no ReShade preset in this download (no .ini with a Techniques= line).");
            var res = new PresetResult { Name = name };
            var rec = Record(g);
            var existing = rec.Items.FirstOrDefault(e => e.Name == name);
            if (existing != null) Remove(g, existing, s => { });
            rec = Record(g);

            // Shaders and textures bundled with the preset.
            res.ShaderFiles = Shaders.InstallFolder(g, Shaders.PresetPrefix + name, root, log);

            // The preset files themselves go next to ReShade.ini.
            var owned = new HashSet<string>(rec.Items.SelectMany(e => e.Inis ?? new string[0]), StringComparer.OrdinalIgnoreCase);
            foreach (var src in inis)
            {
                string fn = Path.GetFileName(src);
                if (File.Exists(Path.Combine(g.Folder, fn)) && !owned.Contains(fn))
                    fn = Path.GetFileNameWithoutExtension(fn) + " (preset).ini";
                File.Copy(src, Path.Combine(g.Folder, fn), true);
                res.Inis.Add(fn);
                log("  Added preset " + fn);
            }

            // Effects the preset uses but the game doesn't have: install their packs from ReShade's list.
            Func<HashSet<string>> present = () =>
            {
                string sd = Path.Combine(g.Folder, "reshade-shaders");
                return new HashSet<string>(Directory.Exists(sd)
                    ? Directory.GetFiles(sd, "*.fx", SearchOption.AllDirectories).Select(Path.GetFileName)
                    : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            };
            var techniques = res.Inis.SelectMany(i => Techniques(Path.Combine(g.Folder, i))).Distinct().ToList();
            var needed = techniques.Select(FileOf).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var have = present();
            var missing = needed.Where(f => !have.Contains(f)).ToList();
            foreach (var pack in catalog.Where(p => p.EffectFiles.Any(e => missing.Contains(e, StringComparer.OrdinalIgnoreCase))))
            {
                if (Shaders.IsInstalled(g, pack.Name)) continue;
                try { Shaders.Install(g, pack, log); res.AddedPacks.Add(pack.Name); }
                catch (Exception ex) { log("  Couldn't add " + pack.Name + ": " + ex.Message); }
            }
            have = present();
            res.Missing = needed.Where(f => !have.Contains(f)).ToList();

            // Offer to switch off effects that clash with neural rendering.
            var clashing = techniques.Where(t => Clash.IsMatch(t)).ToList();
            if (clashing.Count > 0 && askTurnOff != null && askTurnOff(clashing))
            {
                var off = new HashSet<string>(clashing);
                foreach (var i in res.Inis) TurnOff(Path.Combine(g.Folder, i), off);
                res.TurnedOff = clashing;
            }

            // Make it the preset ReShade loads.
            var entry = new PresetEntry { Name = name, Source = source, Domain = domain, ModId = modId, FileId = fileId, Inis = res.Inis.ToArray() };
            try
            {
                string prev = ActivePreset(g);
                bool prevIsOurs = rec.Items.Any(e => (e.Inis ?? new string[0]).Any(i => Points(prev, i)));
                entry.PrevPresetPath = prevIsOurs ? rec.Items.First(e => (e.Inis ?? new string[0]).Any(i => Points(prev, i))).PrevPresetPath : prev;
                SetActive(g, ".\\" + res.Inis[0]);
                res.Active = res.Inis[0];
            }
            catch (Exception ex) { log("  " + ex.Message); }
            rec.Items.Add(entry);
            Save(g, rec);
            return res;
        }

        public static void SetImage(GameEntry g, string name, string url)
        {
            var rec = Record(g);
            var e = rec.Items.FirstOrDefault(x => x.Name == name);
            if (e != null) { e.Image = url; Save(g, rec); }
        }

        public static void MakeActive(GameEntry g, PresetEntry e, string ini)
        {
            var rec = Record(g);
            string prev = ActivePreset(g);
            bool prevIsOurs = rec.Items.Any(x => (x.Inis ?? new string[0]).Any(i => Points(prev, i)));
            if (!prevIsOurs)
            {
                var mine = rec.Items.FirstOrDefault(x => x.Name == e.Name);
                if (mine != null) { mine.PrevPresetPath = prev; Save(g, rec); }
            }
            SetActive(g, ".\\" + ini);
        }

        public static bool IsActive(GameEntry g, PresetEntry e)
        {
            string a = ActivePreset(g);
            return (e.Inis ?? new string[0]).Any(i => Points(a, i));
        }

        public static int Remove(GameEntry g, PresetEntry e, Action<string> log)
        {
            var rec = Record(g);
            int n = 0;
            bool wasActive = IsActive(g, e);
            foreach (var i in e.Inis ?? new string[0])
            {
                string p = Path.Combine(g.Folder, i);
                if (File.Exists(p)) { File.Delete(p); n++; }
            }
            n += Shaders.Remove(g, Shaders.PresetPrefix + e.Name);
            if (wasActive)
            {
                try { SetActive(g, e.PrevPresetPath); } catch { }
            }
            rec.Items.RemoveAll(x => x.Name == e.Name);
            Save(g, rec);
            return n;
        }

        public static void RemoveAll(GameEntry g)
        {
            foreach (var e in Record(g).Items)
                foreach (var i in e.Inis ?? new string[0])
                {
                    string p = Path.Combine(g.Folder, i);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
        }
    }
}
