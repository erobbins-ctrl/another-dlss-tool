using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dlss5Setup
{
    static class Env
    {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        public static bool IsAdmin()
        {
            if (!IsWindows) return true;
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".dlss5setup-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string Q(string s) { return "\"" + s + "\""; }
    }

    static class Paths
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLSS5-Setup");
        public static string AppDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static string BundleDir { get { return Path.Combine(AppDir, "files"); } }
        public static string Library { get { return Path.Combine(Root, "library.json"); } }
        public static string Settings { get { return Path.Combine(Root, "settings.json"); } }
        public static string Tools { get { return Path.Combine(Root, "tools"); } }
        public static string CrashLog { get { return Path.Combine(Root, "crash.log"); } }
        public static string State(GameEntry g) { return Path.Combine(Root, g.Id); }
    }

    static class Urls
    {
        public const string RepoRaw = "https://raw.githubusercontent.com/jlrouzies-fr/DLSS5-Feeder/main/tools/";
        public const string Installer = RepoRaw + "Install-DLSS5Feeder.ps1";
        public const string Verifier = RepoRaw + "Verify-DLSS5Feeder.ps1";
        public const string Project = "https://github.com/jlrouzies-fr/DLSS5-Feeder";
    }

    // ------------------------------------------------------------------ JSON

    static class Json
    {
        static readonly DataContractJsonSerializerSettings Settings =
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                int skip = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
                using (var ms = new MemoryStream(bytes, skip, bytes.Length - skip))
                    return (T)new DataContractJsonSerializer(typeof(T), Settings).ReadObject(ms);
            }
            catch { return null; }
        }

        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T), Settings).WriteObject(ms, value);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }

    [DataContract]
    class GameEntry
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Exe;
        [DataMember] public string Api;
        [DataMember] public string Bits;
        [DataMember] public string ApiChoice;
        [DataMember] public string Consumer;
        [DataMember] public int Mv;
        [DataMember] public bool Watermark;
        [DataMember] public bool Helper;
        [DataMember] public bool NativeDlss;
        [DataMember] public string AntiCheat;
        [DataMember] public bool Installed;
        [DataMember] public int? LastExit;
        [DataMember] public string NexusDomain;
        [DataMember] public string SteamAppId;
        [DataMember] public bool ArtLookedUp;
        [DataMember] public string NexusName;

        public void Normalize()
        {
            if (string.IsNullOrEmpty(ApiChoice)) ApiChoice = "Auto";
            if (string.IsNullOrEmpty(Consumer)) Consumer = "RenoDX";
            if (Mv != 3 && Mv != 4) Mv = 3;
            if (string.IsNullOrEmpty(Api)) Api = "Unknown";
            if (string.IsNullOrEmpty(Bits)) Bits = "unknown";
        }

        public bool ExeExists { get { return !string.IsNullOrEmpty(Exe) && File.Exists(Exe); } }
        public string Folder { get { return Path.GetDirectoryName(Exe); } }

        public string ApiLabel
        {
            get
            {
                switch (Api)
                {
                    case "DX8": return "DirectX 8";
                    case "DX9": return "DirectX 9";
                    case "DX10": return "DirectX 10";
                    case "DX11": return "DirectX 11";
                    case "DX12": return "DirectX 12";
                    case "DX11/12": return "DirectX 11/12";
                    case "Unknown": return "API unknown";
                    default: return Api;
                }
            }
        }

        public bool Supported
        {
            get { return Api != "Vulkan" && Api != "OpenGL"; }
        }

        // The -Api value passed to the official installer.
        public string EffectiveApi
        {
            get
            {
                if (ApiChoice != "Auto") return ApiChoice;
                switch (Api)
                {
                    case "DX9": return "D3D9";
                    case "DX8": return "D3D8";
                    case "DX10": case "DX11": case "DX12": case "DX11/12": return "D3D";
                    default: return "Auto";
                }
            }
        }

        public bool UsesDgVoodoo { get { string a = EffectiveApi; return a == "D3D9" || a == "D3D8"; } }

        public string Route
        {
            get
            {
                if (UsesDgVoodoo) return "via dgVoodoo2";
                if (Bits == "32-bit" || Helper) return "via 64-bit helper";
                return "in-process";
            }
        }

        public string StatusText(out System.Drawing.Color color)
        {
            if (!ExeExists) { color = Theme.Fail; return "Missing"; }
            if (Installed && LastExit == 0) { color = Theme.Accent; return "Installed"; }
            if (Installed) { color = Theme.Warn; return "Installed with warnings"; }
            if (!Supported) { color = Theme.Muted; return "Not supported"; }
            color = Theme.Muted; return "Not installed";
        }
    }

    [DataContract]
    class AppSettings
    {
        [DataMember] public string DlssPath;
        [DataMember] public string NrPath;
        [DataMember] public string RtgiPath;
        [DataMember] public string DfcZip;
    }

    [DataContract]
    class InstallRecord
    {
        [DataMember] public string[] Added;
        [DataMember] public string[] Changed;
    }

    [DataContract]
    class GameJson
    {
        [DataMember] public string Exe;
    }

    // ------------------------------------------------------------------ PE and API detection

    class PeInfo
    {
        public string Bits = "unknown";
        public List<string> Imports = new List<string>();

        public static PeInfo Read(string path)
        {
            var info = new PeInfo();
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    fs.Position = 0x3C; int pe = br.ReadInt32();
                    if (pe <= 0 || pe > fs.Length - 24) return info;
                    fs.Position = pe; if (br.ReadUInt32() != 0x4550) return info;
                    ushort machine = br.ReadUInt16(); ushort nSec = br.ReadUInt16();
                    fs.Position = pe + 20; ushort optSize = br.ReadUInt16();
                    long opt = pe + 24;
                    fs.Position = opt; ushort magic = br.ReadUInt16();
                    bool is64 = magic == 0x20b;
                    info.Bits = machine == 0x14c ? "32-bit" : machine == 0x8664 ? "64-bit" : "unknown";
                    long imageBase, dd;
                    if (is64) { fs.Position = opt + 24; imageBase = (long)br.ReadUInt64(); dd = opt + 112; }
                    else { fs.Position = opt + 28; imageBase = br.ReadUInt32(); dd = opt + 96; }
                    fs.Position = dd + 8; uint impRva = br.ReadUInt32();
                    fs.Position = dd + 13 * 8; uint dlyRva = br.ReadUInt32();

                    var secs = new List<long[]>();
                    for (int i = 0; i < nSec; i++)
                    {
                        fs.Position = opt + optSize + i * 40 + 8;
                        uint vs = br.ReadUInt32(), va = br.ReadUInt32(), rs = br.ReadUInt32(), rp = br.ReadUInt32();
                        secs.Add(new long[] { va, Math.Max(vs, rs), rp });
                    }
                    Func<long, long> toOff = rva =>
                    {
                        foreach (var s in secs) if (rva >= s[0] && rva < s[0] + s[1]) return rva - s[0] + s[2];
                        return -1;
                    };
                    Func<long, string> readStr = off =>
                    {
                        if (off < 0 || off >= fs.Length) return null;
                        fs.Position = off; var sb = new StringBuilder();
                        for (int k = 0; k < 260; k++) { int c = fs.ReadByte(); if (c <= 0) break; sb.Append((char)c); }
                        return sb.ToString();
                    };
                    if (impRva != 0)
                    {
                        long off = toOff(impRva);
                        for (int n = 0; off >= 0 && n < 512; n++)
                        {
                            fs.Position = off + n * 20 + 12; uint nameRva = br.ReadUInt32();
                            if (nameRva == 0) break;
                            string s = readStr(toOff(nameRva)); if (!string.IsNullOrEmpty(s)) info.Imports.Add(s.ToLowerInvariant());
                        }
                    }
                    if (dlyRva != 0)
                    {
                        long off = toOff(dlyRva);
                        for (int n = 0; off >= 0 && n < 512; n++)
                        {
                            fs.Position = off + n * 32; uint attr = br.ReadUInt32(); long nameRef = br.ReadUInt32();
                            if (nameRef == 0) break;
                            if ((attr & 1) == 0) nameRef -= imageBase;   // old format stores a VA
                            string s = readStr(toOff(nameRef)); if (!string.IsNullOrEmpty(s)) info.Imports.Add(s.ToLowerInvariant());
                        }
                    }
                }
            }
            catch { }
            return info;
        }
    }

    static class Detect
    {
        static readonly Regex ExeSkip = new Regex(
            @"(unins|setup|install|launcher|crash|report|redist|vc_?redist|dxsetup|dotnet|helper|updater|" +
            @"easyanticheat|battleye|prereq|cefprocess|webhelper|activation|benchmark|server|editor|7z|" +
            @"dxwebsetup|physx|touchup|cleanup|overlay|vrsetup|config|settings|patcher|bootstrap|eac_|be_)",
            RegexOptions.IgnoreCase);
        static readonly Regex SkipDirs = new Regex(@"\\(_commonredist|redist|directx|support|tools?)\\", RegexOptions.IgnoreCase);
        static readonly Regex AntiCheat = new Regex(@"^(easyanticheat|battleye|beservice|vgk|pnkbstr|ricochet|xigncode)", RegexOptions.IgnoreCase);

        public static void Api(string exe, out string api, out string bits)
        {
            var pe = PeInfo.Read(exe);
            bits = pe.Bits;
            var imports = new HashSet<string>(pe.Imports);
            string dir = Path.GetDirectoryName(exe);
            foreach (var side in new[] { "UnityPlayer.dll", "Engine.dll", "Core.dll", "Renderer.dll", "GameAssembly.dll" })
            {
                string p = Path.Combine(dir, side);
                if (File.Exists(p)) foreach (var i in PeInfo.Read(p).Imports) imports.Add(i);
            }
            if (imports.Contains("d3d12.dll")) api = "DX12";
            else if (imports.Contains("d3d11.dll")) api = "DX11";
            else if (imports.Contains("d3d10.dll") || imports.Contains("d3d10_1.dll")) api = "DX10";
            else if (imports.Contains("d3d9.dll") || imports.Contains("d3dx9_43.dll")) api = "DX9";
            else if (imports.Contains("d3d8.dll")) api = "DX8";
            else if (imports.Contains("dxgi.dll")) api = "DX11/12";
            else if (imports.Contains("vulkan-1.dll")) api = "Vulkan";
            else if (imports.Contains("opengl32.dll")) api = "OpenGL";
            else api = "Unknown";
        }

        public static string GameRoot(string exe)
        {
            var m = Regex.Match(exe, @"^(.*\\steamapps\\common\\[^\\]+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            string dir = Path.GetDirectoryName(exe);
            string parent = Path.GetDirectoryName(dir);
            if (parent != null && Regex.IsMatch(dir, @"\\(bin|binaries|win32|win64|x64|x86|retail|game)$", RegexOptions.IgnoreCase))
                return parent;
            return dir;
        }

        // Walks a folder tree without throwing on folders Windows won't let us read.
        public static IEnumerable<string> Walk(string root, int maxDepth, bool includeDirs)
        {
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                string[] files = new string[0], dirs = new string[0];
                try { files = Directory.GetFiles(cur.Key); } catch { }
                try { dirs = Directory.GetDirectories(cur.Key); } catch { }
                foreach (var f in files) yield return f;
                foreach (var d in dirs)
                {
                    if (includeDirs) yield return d;
                    if (cur.Value < maxDepth) stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1));
                }
            }
        }

        public static void Flags(string exe, out bool nativeDlss, out string antiCheat)
        {
            nativeDlss = false; antiCheat = null;
            foreach (var p in Walk(GameRoot(exe), 4, true))
            {
                string name = Path.GetFileName(p);
                if (!nativeDlss && name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)) nativeDlss = true;
                if (antiCheat == null && AntiCheat.IsMatch(name)) antiCheat = name;
                if (nativeDlss && antiCheat != null) break;
            }
        }

        public static string FindMainExe(string folder)
        {
            var candidates = Walk(folder, 4, false)
                .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Where(f => !ExeSkip.IsMatch(Path.GetFileName(f)) && !SkipDirs.IsMatch(f))
                .Select(f => { long len = 0; try { len = new FileInfo(f).Length; } catch { } return new { f, len }; })
                .OrderByDescending(x => x.len).Take(6).Select(x => x.f).ToList();
            foreach (var c in candidates)
            {
                string api, bits; Api(c, out api, out bits);
                if (api != "Unknown") return c;
            }
            return candidates.FirstOrDefault();
        }

        public static List<string> SteamLibraries()
        {
            var libs = new List<string>();
            if (!Env.IsWindows) return libs;
            string steam = null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k != null) steam = k.GetValue("SteamPath") as string;
                if (steam == null)
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                        if (k != null) steam = k.GetValue("InstallPath") as string;
            }
            catch { }
            if (string.IsNullOrEmpty(steam)) return libs;
            steam = steam.Replace('/', '\\');
            libs.Add(steam);
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!libs.Any(l => l.Equals(lib, StringComparison.OrdinalIgnoreCase))) libs.Add(lib);
                }
            }
            return libs;
        }

        public class SteamGame { public string Name, Folder; }

        public static List<SteamGame> SteamGames()
        {
            var list = new List<SteamGame>();
            var skip = new Regex("(redistributable|proton|steam linux runtime|steamvr|soundtrack|dedicated server|sdk)", RegexOptions.IgnoreCase);
            foreach (var lib in SteamLibraries())
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] acfs;
                try { acfs = Directory.GetFiles(apps, "appmanifest_*.acf"); } catch { continue; }
                foreach (var acf in acfs)
                {
                    string t; try { t = File.ReadAllText(acf); } catch { continue; }
                    string name = Regex.Match(t, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    string dir = Regex.Match(t, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    if (name.Length == 0 || dir.Length == 0 || skip.IsMatch(name)) continue;
                    string full = Path.Combine(apps, "common", dir);
                    if (Directory.Exists(full)) list.Add(new SteamGame { Name = name, Folder = full });
                }
            }
            return list;
        }

        public static GameEntry NewEntry(string exe, string name)
        {
            string api, bits; Api(exe, out api, out bits);
            bool dlss; string ac; Flags(exe, out dlss, out ac);
            if (string.IsNullOrEmpty(name))
            {
                try { name = FileVersionInfo.GetVersionInfo(exe).ProductName; } catch { }
                if (string.IsNullOrEmpty(name) || name.Trim().Length < 2) name = Path.GetFileName(GameRoot(exe));
            }
            string hash;
            using (var sha = SHA1.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(exe.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant();
            string safe = Regex.Replace(name, "[^A-Za-z0-9]", "");
            if (safe.Length > 24) safe = safe.Substring(0, 24);
            var g = new GameEntry
            {
                Id = safe + "-" + hash.Substring(0, 8), Name = name.Trim(), Exe = exe, Api = api, Bits = bits,
                ApiChoice = "Auto", Consumer = "RenoDX", Mv = 3, Watermark = true, NativeDlss = dlss, AntiCheat = ac
            };
            return g;
        }
    }

    // ------------------------------------------------------------------ NVIDIA files

    class DllInfo
    {
        public string Path, Source, Hash, Version, Signer;
        public bool Signed;
        public bool Bundled;
    }

    static class Dlls
    {
        public static string Resolve(string source, string dllName, string dest)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) throw new FileNotFoundException("File not found: " + source);
            Directory.CreateDirectory(dest);
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".dll") return source;
            string outPath = Path.Combine(dest, dllName);
            var fi = new FileInfo(source);
            string key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            string keyFile = Path.Combine(dest, "source.txt");
            if (File.Exists(outPath) && File.Exists(keyFile) && File.ReadAllText(keyFile).Trim() == key) return outPath;
            if (ext == ".zip")
            {
                using (var zip = ZipFile.OpenRead(source))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(dllName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) throw new InvalidOperationException(dllName + " is not inside " + fi.Name + ".");
                    entry.ExtractToFile(outPath, true);
                }
            }
            else if (ext == ".rar")
            {
                if (File.Exists(outPath)) File.Delete(outPath);
                string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (File.Exists(tar))
                {
                    var psi = new ProcessStartInfo(tar, "-xf " + Env.Q(source) + " -C " + Env.Q(dest) + " " + dllName)
                    { UseShellExecute = false, CreateNoWindow = true };
                    using (var p = Process.Start(psi)) p.WaitForExit();
                }
                if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                    throw new InvalidOperationException("Windows couldn't open this .rar. Extract " + dllName + " with 7-Zip and pick the .dll.");
            }
            else throw new InvalidOperationException("Pick a .dll, .zip or .rar for " + dllName + ".");
            File.WriteAllText(keyFile, key);
            return outPath;
        }

        public static DllInfo Describe(string path, string source)
        {
            var d = new DllInfo { Path = path, Source = source };
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                d.Hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            try { d.Version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch { }
            if (string.IsNullOrEmpty(d.Version)) d.Version = "?";
            d.Version = Regex.Replace(d.Version, @",\s*", ".");
            d.Signed = Native.IsTrusted(path);
            if (d.Signed)
            {
                try { d.Signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false); }
                catch { d.Signer = "a trusted publisher"; }
            }
            d.Bundled = source.StartsWith(Paths.BundleDir, StringComparison.OrdinalIgnoreCase);
            return d;
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public static void TrySetDpiAware()
        {
            try { if (Env.IsWindows) SetProcessDPIAware(); } catch { }
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class FileInfoStruct
        {
            public int cbStruct = Marshal.SizeOf(typeof(FileInfoStruct));
            public string pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        class TrustData
        {
            public int cbStruct = Marshal.SizeOf(typeof(TrustData));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public int dwUIChoice = 2;            // WTD_UI_NONE
            public int fdwRevocationChecks = 0;   // WTD_REVOKE_NONE
            public int dwUnionChoice = 1;         // WTD_CHOICE_FILE
            public IntPtr pFile;
            public int dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public int dwProvFlags = 0x1000;      // WTD_CACHE_ONLY_URL_RETRIEVAL: never go online
            public int dwUIContext = 0;
        }

        public static bool IsTrusted(string path)
        {
            if (!Env.IsWindows) return false;
            var fi = new FileInfoStruct { pcwszFilePath = path };
            IntPtr pFi = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            IntPtr pTd = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(fi, pFi, false);
                var td = new TrustData { pFile = pFi };
                pTd = Marshal.AllocHGlobal(Marshal.SizeOf(td));
                Marshal.StructureToPtr(td, pTd, false);
                return WinVerifyTrust(IntPtr.Zero, new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), pTd) == 0;
            }
            catch { return false; }
            finally
            {
                if (pTd != IntPtr.Zero) Marshal.FreeHGlobal(pTd);
                Marshal.DestroyStructure(pFi, typeof(FileInfoStruct));
                Marshal.FreeHGlobal(pFi);
            }
        }
    }

    // ------------------------------------------------------------------ snapshots

    static class Snapshot
    {
        public static readonly string[] BackupExtensions = { ".dll", ".ini", ".conf", ".cfg", ".addon32", ".addon64", ".fx", ".asi" };
        static readonly Regex[] Cleanup = new[]
        {
            @"^host64\\", @"^reshade-shaders\\", @"^layer\\", @"^plugins\\", @"^dlss5-feed", @"^reshade", @"^renodx",
            @"^dgvoodoo", @"^nvngx", @"^optiscaler", @"^fakenvapi", @"^d3d9\.dll$", @"^d3d8\.dll$", @"^dxgi\.dll$",
            @"^winmm\.dll$", @"^version\.dll$", @"^d3dcompiler_47\.dll$", @"\.bak-", @"^verify-dlss5feeder", @"\.addon(32|64)?$"
        }.Select(p => new Regex(p, RegexOptions.IgnoreCase)).ToArray();

        public static Dictionary<string, string> Tree(string dir)
        {
            string baseDir = dir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Detect.Walk(dir, 64, false))
            {
                try
                {
                    var fi = new FileInfo(f);
                    map[f.Substring(baseDir.Length)] = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                }
                catch { }
            }
            return map;
        }

        public static void TakeIfMissing(GameEntry g, Action<string> log)
        {
            string state = Paths.State(g);
            string snapFile = Path.Combine(state, "snapshot.json");
            if (File.Exists(snapFile)) { log(g.Name + ": reinstalling; the original snapshot is kept"); return; }
            log(g.Name + ": snapshotting " + g.Folder);
            string bk = Path.Combine(state, "backup");
            Directory.CreateDirectory(bk);
            foreach (var f in Directory.GetFiles(g.Folder))
                if (BackupExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    File.Copy(f, Path.Combine(bk, Path.GetFileName(f)), true);
            Json.Write(snapFile, Tree(g.Folder));
            Json.Write(Path.Combine(state, "game.json"), new GameJson { Exe = g.Exe });
        }

        public static void RecordInstall(GameEntry g, out int added, out int changed)
        {
            string state = Paths.State(g);
            var snap = Json.Read<Dictionary<string, string>>(Path.Combine(state, "snapshot.json")) ?? new Dictionary<string, string>();
            var snapCi = new Dictionary<string, string>(snap, StringComparer.OrdinalIgnoreCase);
            var now = Tree(g.Folder);
            var a = now.Keys.Where(k => !snapCi.ContainsKey(k)).ToArray();
            var c = now.Keys.Where(k => snapCi.ContainsKey(k) && snapCi[k] != now[k]).ToArray();
            Json.Write(Path.Combine(state, "installed.json"), new InstallRecord { Added = a, Changed = c });
            added = a.Length; changed = c.Length;
        }

        public static bool Exists(GameEntry g) { return File.Exists(Path.Combine(Paths.State(g), "snapshot.json")); }

        public static void Undo(GameEntry g, out int removed, out int restored)
        {
            string state = Paths.State(g);
            string dir = g.Folder;
            Addons.RemoveAll(g);
            Shaders.RemoveAll(g);
            Presets.RemoveAll(g);
            var snap = new Dictionary<string, string>(
                Json.Read<Dictionary<string, string>>(Path.Combine(state, "snapshot.json")) ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            var inst = Json.Read<InstallRecord>(Path.Combine(state, "installed.json"));
            var remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (inst != null && inst.Added != null) foreach (var a in inst.Added) if (!string.IsNullOrEmpty(a)) remove.Add(a);
            foreach (var rel in Tree(dir).Keys)
            {
                if (snap.ContainsKey(rel)) continue;
                if (Cleanup.Any(r => r.IsMatch(rel))) remove.Add(rel);
            }
            removed = 0;
            foreach (var rel in remove)
            {
                string f = Path.Combine(dir, rel);
                if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); removed++; }
            }
            foreach (var sub in new[] { "host64", "reshade-shaders", "layer", "plugins" })
            {
                string d = Path.Combine(dir, sub);
                bool had = snap.Keys.Any(k => k.StartsWith(sub + "\\", StringComparison.OrdinalIgnoreCase));
                if (Directory.Exists(d) && !had) Directory.Delete(d, true);
            }
            restored = 0;
            string bk = Path.Combine(state, "backup");
            if (Directory.Exists(bk))
                foreach (var f in Directory.GetFiles(bk)) { File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true); restored++; }
            Directory.Delete(state, true);
        }
    }

    static class Net
    {
        public static string Download(string url, string name, Action<string> log)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
            Directory.CreateDirectory(Paths.Tools);
            string file = Path.Combine(Paths.Tools, name);
            log("Downloading " + name + " from the official DLSS5-Feeder GitHub");
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Another-DLSS-Tool");
                wc.DownloadFile(url, file);
            }
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
                log("  SHA-256 " + BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", ""));
            return file;
        }
    }
}
