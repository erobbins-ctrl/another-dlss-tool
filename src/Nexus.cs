using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Dlss5Setup
{
    class NexusUser { public string Name; public bool Premium; }
    class NexusGame { public string Name, Domain; public int Id; }
    class NexusMod { public string Domain, Name, Summary, Author, Image; public int ModId; public long Endorsements, Downloads; public bool Adult; }
    class NexusFile { public int FileId; public string Name, Version, Category, FileName, Description; public long SizeKb, Uploaded; public bool Primary; }

    class NexusException : Exception
    {
        public int Status;
        public NexusException(string msg, int status) : base(msg) { Status = status; }
    }

    // Nexus Mods API, used with the person's own personal API key.
    static class Nexus
    {
        const string Api = "https://api.nexusmods.com/v1/";
        const string GraphQl = "https://api.nexusmods.com/v2/graphql";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // ------------------------------------------------------------ key storage (encrypted for this Windows account)

        static string KeyFile { get { return Path.Combine(Paths.Root, "nexus.key"); } }

        public static string LoadKey()
        {
            try
            {
                if (!File.Exists(KeyFile)) return null;
                byte[] data = File.ReadAllBytes(KeyFile);
                byte[] plain = Env.IsWindows ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser) : data;
                return Encoding.UTF8.GetString(plain).Trim();
            }
            catch { return null; }
        }

        public static void SaveKey(string key)
        {
            Directory.CreateDirectory(Paths.Root);
            byte[] plain = Encoding.UTF8.GetBytes(key.Trim());
            File.WriteAllBytes(KeyFile, Env.IsWindows ? ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser) : plain);
        }

        public static void ForgetKey() { try { File.Delete(KeyFile); } catch { } }

        // ------------------------------------------------------------ HTTP

        static WebClient Client(string key)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers.Add("apikey", key);
            wc.Headers.Add("Application-Name", "Another-DLSS-Tool");
            wc.Headers.Add("Application-Version", "2.6.0");
            wc.Headers.Add("User-Agent", "Another-DLSS-Tool/2.6.0");
            wc.Headers.Add("Accept", "application/json");
            return wc;
        }

        static object Get(string key, string path)
        {
            try { using (var wc = Client(key)) return Js.DeserializeObject(wc.DownloadString(Api + path)); }
            catch (WebException ex) { throw Explain(ex); }
        }

        static NexusException Explain(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            int status = resp != null ? (int)resp.StatusCode : 0;
            string detail = null;
            try
            {
                if (resp != null)
                    using (var r = new StreamReader(resp.GetResponseStream()))
                    {
                        var o = Js.DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
                        if (o != null && o.ContainsKey("message")) detail = Convert.ToString(o["message"]);
                    }
            }
            catch { }
            string msg;
            switch (status)
            {
                case 401: msg = "Nexus didn't accept the API key. Paste it again from your Nexus account's API keys page."; break;
                case 403: msg = detail ?? "Nexus refused the request. Direct downloads need a Premium membership."; break;
                case 404: msg = "Nexus couldn't find that mod or file. It may have been hidden or removed."; break;
                case 429: msg = "Nexus is rate-limiting requests. Wait a minute and try again."; break;
                default: msg = detail ?? ex.Message; break;
            }
            return new NexusException(msg, status);
        }

        static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : null; }
        static long L(Dictionary<string, object> d, string k) { object v; long n; return d != null && d.TryGetValue(k, out v) && v != null && long.TryParse(Convert.ToString(v), out n) ? n : 0; }
        static bool B(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }

        // ------------------------------------------------------------ endpoints

        public static NexusUser Validate(string key)
        {
            var d = Get(key, "users/validate.json") as Dictionary<string, object>;
            return new NexusUser { Name = S(d, "name"), Premium = B(d, "is_premium") };
        }

        public static List<NexusGame> Games(string key)
        {
            string cache = Path.Combine(Paths.Root, "nexus-games.json");
            string text = null;
            if (File.Exists(cache) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cache)).TotalDays < 7) text = File.ReadAllText(cache);
            if (text == null)
            {
                try { using (var wc = Client(key)) text = wc.DownloadString(Api + "games.json"); }
                catch (WebException ex) { if (File.Exists(cache)) text = File.ReadAllText(cache); else throw Explain(ex); }
                File.WriteAllText(cache, text);
            }
            return ParseGames(text);
        }

        public static List<NexusGame> ParseGames(string json)
        {
            var arr = Js.DeserializeObject(json) as object[];
            if (arr == null) return new List<NexusGame>();
            return arr.OfType<Dictionary<string, object>>()
                      .Select(d => new NexusGame { Name = S(d, "name"), Domain = S(d, "domain_name"), Id = (int)L(d, "id") })
                      .Where(g => g.Name != null && g.Domain != null).ToList();
        }

        static string Norm(string s)
        {
            s = (s ?? "").ToLowerInvariant().Replace("\u2122", "").Replace("\u00AE", "");
            s = Regex.Replace(s, @"^the\s+", "");
            return Regex.Replace(s, "[^a-z0-9]", "");
        }

        public static NexusGame Match(List<NexusGame> games, string name)
        {
            string n = Norm(name);
            if (n.Length == 0) return null;
            return games.FirstOrDefault(g => Norm(g.Name) == n)
                ?? games.Where(g => Norm(g.Name).StartsWith(n) || n.StartsWith(Norm(g.Name)))
                        .OrderBy(g => Math.Abs(Norm(g.Name).Length - n.Length)).FirstOrDefault();
        }

        public static NexusMod Mod(string key, string domain, int modId)
        {
            var d = Get(key, "games/" + domain + "/mods/" + modId + ".json") as Dictionary<string, object>;
            return new NexusMod
            {
                Domain = domain, ModId = modId, Name = S(d, "name"), Summary = Clean(S(d, "summary")), Author = S(d, "author"),
                Endorsements = L(d, "endorsement_count"), Downloads = L(d, "mod_downloads"), Adult = B(d, "contains_adult_content"),
                Image = S(d, "picture_url")
            };
        }

        public static List<NexusFile> Files(string key, string domain, int modId)
        {
            var d = Get(key, "games/" + domain + "/mods/" + modId + "/files.json") as Dictionary<string, object>;
            object fl; var arr = d != null && d.TryGetValue("files", out fl) ? fl as object[] : null;
            if (arr == null) return new List<NexusFile>();
            return arr.OfType<Dictionary<string, object>>().Select(f => new NexusFile
            {
                FileId = (int)L(f, "file_id"), Name = S(f, "name"), Version = S(f, "version"), Category = S(f, "category_name"),
                FileName = S(f, "file_name"), Description = Clean(S(f, "description")), SizeKb = L(f, "size_kb"),
                Uploaded = L(f, "uploaded_timestamp"), Primary = B(f, "is_primary")
            }).ToList();
        }

        // Main files first, newest first; old versions and archived files are left out.
        public static List<NexusFile> Installable(List<NexusFile> files)
        {
            return files.Where(f => f.Category == null || !Regex.IsMatch(f.Category, "^(OLD_VERSION|ARCHIVED|DELETED|REMOVED)$", RegexOptions.IgnoreCase))
                        .OrderByDescending(f => f.Primary)
                        .ThenBy(f => string.Equals(f.Category, "MAIN", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenByDescending(f => f.Uploaded).ToList();
        }

        public static string DownloadLink(string key, string domain, int modId, int fileId)
        {
            var arr = Get(key, "games/" + domain + "/mods/" + modId + "/files/" + fileId + "/download_link.json") as object[];
            var first = arr == null ? null : arr.OfType<Dictionary<string, object>>().FirstOrDefault();
            string uri = S(first, "URI");
            if (uri == null) throw new NexusException("Nexus didn't return a download link.", 0);
            return uri;
        }

        public static void Download(string key, string url, string file)
        {
            try { using (var wc = Client(key)) wc.DownloadFile(url, file); }
            catch (WebException ex) { throw Explain(ex); }
        }

        static string Clean(string s)
        {
            if (s == null) return null;
            s = Regex.Replace(s, @"\[/?[a-z]+(=[^\]]*)?\]", "", RegexOptions.IgnoreCase);   // BBCode
            s = Regex.Replace(s, "<[^>]+>", "");
            return WebUtility.HtmlDecode(s).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // ------------------------------------------------------------ search (GraphQL v2)

        const string SearchQuery =
            "query Mods($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) { " +
            "mods(filter: $filter, sort: $sort, count: $count, offset: $offset) { totalCount nodesFilter nodes { modId name summary endorsements downloads author adultContent thumbnailLargeUrl thumbnailUrl pictureUrl } } }";

        [ThreadStatic] static long lastTotal;
        [ThreadStatic] static string lastFilter;

        static readonly System.Text.RegularExpressions.Regex PresetWords =
            new System.Text.RegularExpressions.Regex(@"(reshade|re-shade|preset)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static Dictionary<string, object> F(string value, string op) { return new Dictionary<string, object> { { "value", value }, { "op", op } }; }

        static List<NexusMod> Query(string key, Dictionary<string, object> filter, int count, int offset, bool sorted, string domain, out string error)
        {
            var vars = new Dictionary<string, object> { { "filter", filter }, { "count", count }, { "offset", offset } };
            if (sorted) vars["sort"] = new[] { new Dictionary<string, object> { { "endorsements", new Dictionary<string, object> { { "direction", "DESC" } } } } };
            string body = Js.Serialize(new Dictionary<string, object> { { "query", SearchQuery }, { "variables", vars } });
            try
            {
                string resp;
                using (var wc = Client(key))
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    resp = wc.UploadString(GraphQl, "POST", body);
                }
                return ParseSearch(resp, domain, out error);
            }
            catch (WebException ex) { error = Explain(ex).Message; return null; }
        }

        // Returns ReShade presets for a game, most endorsed first. Tries several ways of asking,
        // because Nexus's search API isn't fully documented; logs what each one returned.
        public static List<NexusMod> Search(string key, string domain, int gameId, Action<string> log)
        {
            var errors = new List<string>();
            var gameFilters = new List<KeyValuePair<string, Dictionary<string, object>>>
            {
                new KeyValuePair<string, Dictionary<string, object>>("game name", new Dictionary<string, object> { { "gameDomainName", new[] { F(domain, "EQUALS") } } })
            };
            if (gameId > 0)
                gameFilters.Add(new KeyValuePair<string, Dictionary<string, object>>("game id",
                    new Dictionary<string, object> { { "gameId", new[] { F(gameId.ToString(), "EQUALS") } } }));

            Func<List<NexusMod>, List<NexusMod>> keep = l => l.Where(m => PresetWords.IsMatch(m.Name ?? "") || PresetWords.IsMatch(m.Summary ?? ""))
                                                            .GroupBy(m => m.ModId).Select(x => x.First())
                                                            .OrderByDescending(m => m.Endorsements).ToList();

            // 1. Ask Nexus for mods mentioning ReShade, following the filter types in Nexus's published schema:
            //    name takes WILDCARD, nameStemmed takes MATCHES or WILDCARD, description takes MATCHES.
            foreach (var gf in gameFilters)
            {
                var variants = new List<KeyValuePair<string, Dictionary<string, object>>>
                {
                    new KeyValuePair<string, Dictionary<string, object>>("name or description mentions reshade", new Dictionary<string, object>(gf.Value) {
                        { "filter", new[] { new Dictionary<string, object> { { "op", "OR" },
                            { "nameStemmed", new[] { F("reshade", "MATCHES") } }, { "description", new[] { F("reshade", "MATCHES") } } } } } }),
                    new KeyValuePair<string, Dictionary<string, object>>("name wildcard reshade", new Dictionary<string, object>(gf.Value) { { "name", new[] { F("reshade", "WILDCARD") } } }),
                    new KeyValuePair<string, Dictionary<string, object>>("stemmed name reshade", new Dictionary<string, object>(gf.Value) { { "nameStemmed", new[] { F("reshade", "WILDCARD") } } }),
                };
                foreach (var v in variants)
                {
                    string err;
                    var res = Query(key, v.Value, 50, 0, true, domain, out err);
                    if (res == null) { if (err != null && !errors.Contains(err)) errors.Add(err); if (log != null) log("  Nexus search (" + gf.Key + ", " + v.Key + "): error " + err); continue; }
                    var hits = keep(res);
                    if (log != null) log("  Nexus search (" + gf.Key + ", " + v.Key + "): " + lastTotal + " total, " + hits.Count + " presets" + (res.Count == 0 && lastFilter != null ? "   [Nexus read the filter as: " + lastFilter + "]" : ""));
                    if (hits.Count > 0) return hits;
                }
            }

            // 2. Fall back to the game's most endorsed mods and pick out the presets here.
            foreach (var gf in gameFilters)
            {
                var all = new List<NexusMod>();
                string err = null;
                for (int offset = 0; offset < 400; offset += 100)
                {
                    var page = Query(key, gf.Value, 100, offset, true, domain, out err);
                    if (page == null) break;
                    all.AddRange(page);
                    if (page.Count < 100) break;
                }
                if (err != null && !errors.Contains(err)) errors.Add(err);
                var hits = keep(all);
                if (log != null) log("  Nexus search (" + gf.Key + ", top mods): " + all.Count + " mods checked of " + lastTotal + ", " + hits.Count + " presets" + (err != null ? ", error " + err : "") + (all.Count == 0 && lastFilter != null ? "   [Nexus read the filter as: " + lastFilter + "]" : ""));
                if (all.Count > 0) return hits;
            }
            if (errors.Count > 0) throw new NexusException("Nexus search didn't work from this app (" + string.Join("; ", errors.Take(2)) + ").", 0);
            return new List<NexusMod>();
        }

        public static List<NexusMod> ParseSearch(string json, string domain, out string error)
        {
            error = null;
            var root = Js.DeserializeObject(json) as Dictionary<string, object>;
            object errs;
            if (root != null && root.TryGetValue("errors", out errs) && errs is object[] && ((object[])errs).Length > 0)
            {
                error = S(((object[])errs)[0] as Dictionary<string, object>, "message");
                return null;
            }
            object data, mods, nodes;
            if (root == null || !root.TryGetValue("data", out data) || !(data is Dictionary<string, object>) ||
                !((Dictionary<string, object>)data).TryGetValue("mods", out mods) || !(mods is Dictionary<string, object>) ||
                !((Dictionary<string, object>)mods).TryGetValue("nodes", out nodes) || !(nodes is object[]))
            { error = "unexpected reply"; return null; }
            var md = (Dictionary<string, object>)mods;
            lastTotal = L(md, "totalCount"); lastFilter = S(md, "nodesFilter");
            if (lastFilter != null && lastFilter.Length > 300) lastFilter = lastFilter.Substring(0, 300) + "...";
            return ((object[])nodes).OfType<Dictionary<string, object>>().Select(n => new NexusMod
            {
                Domain = domain, ModId = (int)L(n, "modId"), Name = S(n, "name"), Summary = Clean(S(n, "summary")), Author = S(n, "author"),
                Endorsements = L(n, "endorsements"), Downloads = L(n, "downloads"), Adult = B(n, "adultContent"),
                Image = S(n, "thumbnailLargeUrl") ?? S(n, "thumbnailUrl") ?? S(n, "pictureUrl")
            }).Where(m => m.ModId > 0 && m.Name != null).ToList();
        }

        // Mod image bytes, cached on disk so lists open instantly next time. Decoding happens on the UI thread.
        public static byte[] ThumbnailBytes(string url, out string contentType)
        {
            contentType = null;
            if (string.IsNullOrEmpty(url)) return null;
            if (url.StartsWith("//")) url = "https:" + url;
            string dir = Path.Combine(Paths.Root, "thumbs");
            Directory.CreateDirectory(dir);
            string name;
            using (var sha = SHA1.Create()) name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "").Substring(0, 24);
            string file = Path.Combine(dir, name);
            if (File.Exists(file)) { try { return File.ReadAllBytes(file); } catch { } }
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            byte[] bytes;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Another-DLSS-Tool/2.6.0");
                    // Ask for formats Windows can always decode.
                    wc.Headers.Add("Accept", "image/jpeg,image/png,image/gif;q=0.9,image/*;q=0.5");
                    bytes = wc.DownloadData(url);
                    contentType = wc.ResponseHeaders != null ? wc.ResponseHeaders["Content-Type"] : null;
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                throw new Exception((resp != null ? "HTTP " + (int)resp.StatusCode : ex.Status.ToString()) + " from " + new Uri(url).Host);
            }
            try { File.WriteAllBytes(file, bytes); } catch { }
            return bytes;
        }

        public static void ForgetThumbnail(string url)
        {
            try
            {
                if (url.StartsWith("//")) url = "https:" + url;
                string name;
                using (var sha = SHA1.Create()) name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "").Substring(0, 24);
                File.Delete(Path.Combine(Paths.Root, "thumbs", name));
            }
            catch { }
        }

        // GDI+ first; then Windows' own image codecs (WIC, via WPF), which read WebP and more where installed.
        public static System.Drawing.Image Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            System.Drawing.Image raw = null;
            try { raw = System.Drawing.Image.FromStream(new MemoryStream(bytes)); } catch { }
            if (raw == null) raw = DecodeWithWic(bytes);
            if (raw == null) return null;
            using (raw)
            {
                int w = Math.Min(raw.Width, 480), h = Math.Max(1, raw.Height * w / Math.Max(1, raw.Width));
                return new System.Drawing.Bitmap(raw, w, h);
            }
        }

        static System.Drawing.Image DecodeWithWic(byte[] bytes)
        {
            if (!Env.IsWindows) return null;
            try
            {
                var pc = System.Reflection.Assembly.Load("PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                var decoderT = pc.GetType("System.Windows.Media.Imaging.BitmapDecoder");
                var createT = pc.GetType("System.Windows.Media.Imaging.BitmapCreateOptions");
                var cacheT = pc.GetType("System.Windows.Media.Imaging.BitmapCacheOption");
                var sourceT = pc.GetType("System.Windows.Media.Imaging.BitmapSource");
                var frameT = pc.GetType("System.Windows.Media.Imaging.BitmapFrame");
                var encT = pc.GetType("System.Windows.Media.Imaging.PngBitmapEncoder");
                using (var ms = new MemoryStream(bytes))
                {
                    var dec = decoderT.GetMethod("Create", new[] { typeof(Stream), createT, cacheT })
                                      .Invoke(null, new object[] { ms, Enum.Parse(createT, "None"), Enum.Parse(cacheT, "OnLoad") });
                    var frames = (System.Collections.IList)decoderT.GetProperty("Frames").GetValue(dec, null);
                    if (frames.Count == 0) return null;
                    var enc = Activator.CreateInstance(encT);
                    var encFrames = (System.Collections.IList)encT.GetProperty("Frames").GetValue(enc, null);
                    encFrames.Add(frameT.GetMethod("Create", new[] { sourceT }).Invoke(null, new[] { frames[0] }));
                    var outMs = new MemoryStream();
                    encT.GetMethod("Save", new[] { typeof(Stream) }).Invoke(enc, new object[] { outMs });
                    outMs.Position = 0;
                    return System.Drawing.Image.FromStream(outMs);
                }
            }
            catch { return null; }
        }

        public static string Kind(byte[] b, string contentType)
        {
            if (b == null || b.Length < 12) return contentType ?? "empty";
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "WebP";
            if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70) return "AVIF/HEIF";
            if (b[0] == '<' || (b[0] == 0xEF && b[3] == '<')) return "a web page instead of an image";
            return contentType ?? "unknown";
        }

        public static bool ParseLink(string text, out string domain, out int modId)
        {
            domain = null; modId = 0;
            var m = Regex.Match(text ?? "", @"nexusmods\.com/(?:games/)?([a-z0-9]+)/mods/(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            domain = m.Groups[1].Value.ToLowerInvariant();
            return int.TryParse(m.Groups[2].Value, out modId);
        }

        public static string ModUrl(string domain, int modId) { return "https://www.nexusmods.com/" + domain + "/mods/" + modId; }
        public static string SearchUrl(string domain) { return "https://www.nexusmods.com/" + domain + "/search/?gsearch=reshade&gsearchtype=mods"; }
        public const string KeyPage = "https://www.nexusmods.com/users/myaccount?tab=api";
    }
}
