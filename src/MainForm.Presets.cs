using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Dlss5Setup
{
    partial class MainForm
    {
        string nexusKey; NexusUser nexusUser; List<NexusGame> nexusGames;
        readonly Dictionary<string, List<NexusMod>> nexusResults = new Dictionary<string, List<NexusMod>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> nexusErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string nexusSearching;
        readonly Dictionary<string, Image> thumbs = new Dictionary<string, Image>();
        readonly HashSet<string> thumbsLoading = new HashSet<string>();

        FitWrap presetIntro; Row rowAccount, rowNexusGame, rowLink;
        FlowLayoutPanel accountControls; TextBox keyBox, linkBox;
        RoundButton bConnect, bDisconnect, bNexusGame, bLookUp;
        Label resultsTitle, installedTitle; LinkButton lRefreshNexus, lOpenNexus, lFromFile, lFromFolder;
        Stack resultsList, installedList; Panel resultsBar;

        void BuildPresetsPage(Stack sp)
        {
            presetIntro = new FitWrap("", Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, Ui.Px(14)) };

            keyBox = new TextBox { Width = Ui.Px(230), UseSystemPasswordChar = true, Font = Theme.Body, Margin = new Padding(0, Ui.Px(6), Ui.Px(8), 0) };
            bConnect = new RoundButton("Connect", BtnStyle.Primary) { Margin = new Padding(0) };
            bDisconnect = new RoundButton("Disconnect", BtnStyle.Secondary) { Margin = new Padding(0) };
            var getKey = new LinkButton("Get your key") { Margin = new Padding(Ui.Px(10), Ui.Px(8), 0, 0) };
            getKey.Click += (s, e) => { try { Process.Start(Nexus.KeyPage); } catch { } };
            accountControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            accountControls.Controls.AddRange(new Control[] { keyBox, bConnect, getKey, bDisconnect });
            rowAccount = new Row("Nexus Mods account", "", accountControls);
            bNexusGame = new RoundButton("Change", BtnStyle.Secondary) { Margin = new Padding(0) };
            rowNexusGame = new Row("Game on Nexus", "", bNexusGame);
            linkBox = new TextBox { Width = Ui.Px(300), Font = Theme.Body, Margin = new Padding(0, Ui.Px(6), Ui.Px(8), 0) };
            bLookUp = new RoundButton("Look up", BtnStyle.Secondary) { Margin = new Padding(0) };
            var linkControls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            linkControls.Controls.AddRange(new Control[] { linkBox, bLookUp });
            linkControls.Size = linkControls.PreferredSize;
            rowLink = new Row("Have a link?", "Paste a Nexus mod page link to install that preset.", linkControls) { Divider = false };
            var account = new StackCard { Margin = new Padding(0, 0, 0, Ui.Px(18)) };
            account.Body.Controls.AddRange(new Control[] { rowAccount, rowNexusGame, rowLink });

            resultsTitle = new Label { Text = "Top presets", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.Px(6)) };
            var bar = resultsBar = new Panel { Height = Ui.Px(30), Tag = "stretch", Margin = new Padding(0, 0, 0, Ui.Px(8)) };
            lRefreshNexus = new LinkButton("Refresh") { Margin = new Padding(0) };
            lOpenNexus = new LinkButton("Browse on Nexus") { Margin = new Padding(0) };
            bar.Controls.AddRange(new Control[] { lRefreshNexus, lOpenNexus });
            bar.Resize += (s, e) => { lRefreshNexus.Location = new Point(0, Ui.Px(4)); lOpenNexus.Location = new Point(lRefreshNexus.Right + Ui.Px(18), Ui.Px(4)); };
            bar.ParentChanged += (s, e) => { if (bar.Parent != null) bar.BackColor = bar.Parent.BackColor; };
            resultsList = new Stack { Margin = new Padding(0) };

            installedTitle = new Label { Text = "Installed in this game", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, Ui.Px(14), 0, Ui.Px(6)) };
            var bar2 = new Panel { Height = Ui.Px(30), Tag = "stretch", Margin = new Padding(0, 0, 0, Ui.Px(8)) };
            lFromFile = new LinkButton("Install from a file") { Margin = new Padding(0) };
            lFromFolder = new LinkButton("Install from a folder") { Margin = new Padding(0) };
            bar2.Controls.AddRange(new Control[] { lFromFile, lFromFolder });
            bar2.Resize += (s, e) => { lFromFile.Location = new Point(0, Ui.Px(4)); lFromFolder.Location = new Point(lFromFile.Right + Ui.Px(18), Ui.Px(4)); };
            bar2.ParentChanged += (s, e) => { if (bar2.Parent != null) bar2.BackColor = bar2.Parent.BackColor; };
            installedList = new Stack { Margin = new Padding(0) };

            sp.Controls.AddRange(new Control[] { presetIntro, account, resultsTitle, bar, resultsList, installedTitle, bar2, installedList });

            bConnect.Click += async (s, e) => await ConnectNexus(keyBox.Text, true);
            keyBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ConnectNexus(keyBox.Text, true); } };
            bDisconnect.Click += (s, e) =>
            {
                Nexus.ForgetKey(); nexusKey = null; nexusUser = null; nexusResults.Clear();
                Log("Disconnected from Nexus Mods. The key was deleted from this PC.");
                ShowPresets();
            };
            bNexusGame.Click += async (s, e) => await PickNexusGame();
            bLookUp.Click += async (s, e) => await LookUpLink();
            linkBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LookUpLink(); } };
            lRefreshNexus.Click += async (s, e) => { var g = Selected; if (g != null && g.NexusDomain != null) { nexusResults.Remove(g.NexusDomain); await SearchNexus(g); } };
            lOpenNexus.Click += (s, e) => { var g = Selected; if (g != null && g.NexusDomain != null) try { Process.Start(Nexus.SearchUrl(g.NexusDomain)); } catch { } };
            lFromFile.Click += async (s, e) => await InstallFromDisk(false);
            lFromFolder.Click += async (s, e) => await InstallFromDisk(true);
        }

        void UpdateBadges(GameEntry g)
        {
            string shaders = ShaderBadge(g);
            int presets = g.Installed ? Presets.Record(g).Items.Count : 0;
            tabs.Badges = new[] { null, shaders, presets > 0 ? presets.ToString() : null, null, null };
            tabs.Invalidate();
        }

        // ------------------------------------------------------------ account

        async Task ConnectNexus(string key, bool interactive)
        {
            key = string.IsNullOrWhiteSpace(key) ? Nexus.LoadKey() : key.Trim();
            if (string.IsNullOrEmpty(key)) { ShowPresets(); return; }
            if (interactive) SetBusy(true, "connecting to Nexus Mods");
            try
            {
                var user = await Task.Run(() => Nexus.Validate(key));
                nexusKey = key; nexusUser = user;
                if (interactive) { Nexus.SaveKey(key); keyBox.Text = ""; }
                Log("Connected to Nexus Mods as " + user.Name + (user.Premium ? " (Premium)." : ". One-click downloads need Premium; without it, presets open on Nexus instead."));
            }
            catch (Exception ex)
            {
                if (interactive) { Log("Nexus: " + ex.Message); Warn(ex.Message); }
                else Log("Nexus: " + ex.Message);
                if (ex is NexusException && ((NexusException)ex).Status == 401) Nexus.ForgetKey();
            }
            if (interactive) SetBusy(false);
            ShowPresets();
        }

        async Task EnsureNexusGame(GameEntry g)
        {
            if (g.NexusDomain != null || nexusKey == null) return;
            try
            {
                if (nexusGames == null) nexusGames = await Task.Run(() => Nexus.Games(nexusKey));
                var m = Nexus.Match(nexusGames, g.Name);
                if (m != null)
                {
                    g.NexusDomain = m.Domain; g.NexusName = m.Name; SaveLibrary();
                    Log(g.Name + ": matched to " + m.Name + " on Nexus Mods.");
                }
            }
            catch (Exception ex) { Log("Nexus: " + ex.Message); }
        }

        async Task PickNexusGame()
        {
            var g = Selected; if (g == null || nexusKey == null) return;
            SetBusy(true, "loading Nexus game list");
            try { if (nexusGames == null) nexusGames = await Task.Run(() => Nexus.Games(nexusKey)); }
            catch (Exception ex) { Log("Nexus: " + ex.Message); }
            SetBusy(false);
            if (nexusGames == null) return;
            using (var dlg = new PickForm("Which game is this on Nexus?", "Type to filter Nexus Mods' game list.",
                       nexusGames.OrderBy(x => x.Name).Select(x => new PickItem { Text = x.Name, Detail = x.Domain, Value = x }).ToList(), true, g.Name))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Picked == null) return;
                var ng = (NexusGame)dlg.Picked.Value;
                g.NexusDomain = ng.Domain; g.NexusName = ng.Name; SaveLibrary();
            }
            Log(g.Name + ": using " + g.NexusName + " on Nexus Mods.");
            ShowPresets();
        }

        // ------------------------------------------------------------ search and look-up

        async Task SearchNexus(GameEntry g)
        {
            if (nexusKey == null || g.NexusDomain == null || nexusSearching != null) return;
            nexusSearching = g.NexusDomain;
            ShowPresets();
            string domain = g.NexusDomain;
            try
            {
                if (nexusGames == null) { try { nexusGames = await Task.Run(() => Nexus.Games(nexusKey)); } catch { } }
                var ng = nexusGames == null ? null : nexusGames.FirstOrDefault(x => x.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));
                int gameId = ng != null ? ng.Id : 0;
                var mods = await Task.Run(() => Nexus.Search(nexusKey, domain, gameId, Log));
                nexusResults[domain] = mods.Where(m => !m.Adult).Take(25).ToList();
                if (nexusResults[domain].Count > 0 && nexusResults[domain].All(m => string.IsNullOrEmpty(m.Image)))
                    ThumbProblem("Nexus didn't send image links with the search results.");
                nexusErrors.Remove(domain);
                Log("Nexus: found " + nexusResults[domain].Count + " ReShade presets for " + (g.NexusName ?? domain) + ".");
            }
            catch (Exception ex)
            {
                nexusErrors[domain] = ex.Message;
                nexusResults[domain] = new List<NexusMod>();
                Log("Nexus: " + ex.Message);
            }
            nexusSearching = null;
            ShowPresets();
        }

        async Task LookUpLink()
        {
            var g = Selected; if (g == null) return;
            string domain; int modId;
            if (!Nexus.ParseLink(linkBox.Text, out domain, out modId)) { Warn("That doesn't look like a Nexus mod link. It should look like nexusmods.com/<game>/mods/12345."); return; }
            if (nexusKey == null) { Warn("Connect your Nexus Mods account first."); return; }
            SetBusy(true, "looking up the mod");
            NexusMod mod = null;
            try { mod = await Task.Run(() => Nexus.Mod(nexusKey, domain, modId)); }
            catch (Exception ex) { Log("Nexus: " + ex.Message); Warn(ex.Message); }
            SetBusy(false);
            if (mod == null) return;
            linkBox.Text = "";
            if (!nexusResults.ContainsKey(domain)) nexusResults[domain] = new List<NexusMod>();
            nexusResults[domain].RemoveAll(m => m.ModId == mod.ModId);
            nexusResults[domain].Insert(0, mod);
            if (g.NexusDomain == null) { g.NexusDomain = domain; SaveLibrary(); }
            ShowPresets();
            await InstallNexus(mod);
        }

        // ------------------------------------------------------------ rendering

        void ShowPresets()
        {
            var g = Selected; if (g == null || presetIntro == null) return;
            bool ready = g.Installed;
            bool connected = nexusKey != null && nexusUser != null;
            presetIntro.Text = ready
                ? "ReShade presets from Nexus Mods for this game, most endorsed first. Installing one adds its preset and shaders, fetches any shader packs it needs from ReShade's official list, and makes it the preset ReShade loads. Most presets were made without neural rendering, so expect to adjust them."
                : "Install the feeder first. It sets up ReShade, which presets need.";
            presetIntro.ForeColor = ready ? Theme.Muted : Theme.Warn;

            keyBox.Visible = bConnect.Visible = !connected;
            accountControls.Controls[2].Visible = !connected;
            bDisconnect.Visible = connected;
            accountControls.Size = accountControls.PreferredSize;
            rowAccount.SetHelp(connected
                ? "Connected as " + nexusUser.Name + (nexusUser.Premium ? ". Premium, so installs are one click." : ". Not Premium: installs open the mod page so you can download there.")
                : "Paste your personal API key from your Nexus account (Site preferences, API keys). It's stored on this PC only, encrypted for your Windows account.");
            rowNexusGame.Visible = connected;
            rowNexusGame.SetHelp(g.NexusDomain != null ? (g.NexusName ?? g.NexusDomain) + "   (" + g.NexusDomain + ")" : "Not matched yet. Press Change to pick it.");
            rowLink.Visible = connected;
            bLookUp.Enabled = ready;

            resultsList.SuspendLayout();
            foreach (Control c in resultsList.Controls.Cast<Control>().ToList()) c.Dispose();
            resultsList.Controls.Clear();
            resultsTitle.Text = "Top presets" + (g.NexusName != null ? " for " + g.NexusName : "");
            lRefreshNexus.Visible = lOpenNexus.Visible = connected && g.NexusDomain != null;
            resultsBar.Visible = connected && g.NexusDomain != null;
            rowAccount.Divider = connected;
            if (!connected)
                resultsList.Controls.Add(new FitWrap("Connect your Nexus Mods account above to see presets here.", Theme.Body, Theme.Muted));
            else if (g.NexusDomain == null)
            {
                resultsList.Controls.Add(new FitWrap("Matching this game on Nexus...", Theme.Body, Theme.Muted));
                var _ = MatchThenSearch(g);
            }
            else if (nexusSearching == g.NexusDomain)
                resultsList.Controls.Add(new FitWrap("Searching Nexus Mods...", Theme.Body, Theme.Muted));
            else if (!nexusResults.ContainsKey(g.NexusDomain))
            {
                resultsList.Controls.Add(new FitWrap("Searching Nexus Mods...", Theme.Body, Theme.Muted));
                var _ = SearchNexus(g);
            }
            else
            {
                string err;
                if (nexusErrors.TryGetValue(g.NexusDomain, out err))
                    resultsList.Controls.Add(new FitNote(err + " You can still use Browse on Nexus, copy a preset's link, and paste it above.", Theme.Warn, Theme.WarnSoft));
                var mods = nexusResults[g.NexusDomain];
                if (mods.Count == 0 && err == null)
                    resultsList.Controls.Add(new FitWrap("No ReShade presets found for this game. Try Browse on Nexus, or paste a link above.", Theme.Body, Theme.Muted));
                foreach (var m in mods) resultsList.Controls.Add(ModCard(g, m, ready));
            }
            resultsList.ResumeLayout(false);

            installedList.SuspendLayout();
            foreach (Control c in installedList.Controls.Cast<Control>().ToList()) c.Dispose();
            installedList.Controls.Clear();
            var items = ready ? Presets.Record(g).Items : new List<PresetEntry>();
            lFromFile.Enabled = lFromFolder.Enabled = ready;
            if (items.Count == 0)
                installedList.Controls.Add(new FitWrap(ready ? "No presets installed yet." : "", Theme.Body, Theme.Muted));
            foreach (var e in items) installedList.Controls.Add(InstalledCard(g, e));
            installedList.ResumeLayout(false);

            UpdateBadges(g);
            Refit(2);
        }

        async Task MatchThenSearch(GameEntry g)
        {
            await EnsureNexusGame(g);
            if (Selected == g) ShowPresets();
        }

        ItemCard ModCard(GameEntry g, NexusMod m, bool ready)
        {
            string meta = m.Endorsements.ToString("N0") + " endorsements   \u00B7   " + m.Downloads.ToString("N0") + " downloads";
            var card = new ItemCard(m.Name, m.Author, m.Summary ?? "", meta, false, Nexus.ModUrl(m.Domain, m.ModId));
            card.EnableThumb(Nexus.ModUrl(m.Domain, m.ModId));
            ShowThumb(card.Thumb, m.Image);
            var installed = ready ? Presets.ForMod(g, m.Domain, m.ModId) : null;
            card.SetState(installed != null, ready, null);
            if (installed == null)
            {
                card.Action.Kind = BtnStyle.Primary;
                card.Action.Text = nexusUser != null && nexusUser.Premium ? "Install" : "Open page";
                card.Action.Click += async (s, e) => await InstallNexus(m);
            }
            else card.Action.Click += async (s, e) => await RemovePreset(installed);
            return card;
        }

        readonly HashSet<string> thumbProblems = new HashSet<string>();

        void ThumbProblem(string msg)
        {
            if (thumbProblems.Add(msg)) Log("Preset images: " + msg);
        }

        void ShowThumb(ThumbBox box, string url)
        {
            box.Tag = url;
            if (string.IsNullOrEmpty(url)) { box.SetImage(null); return; }
            Image img;
            if (thumbs.TryGetValue(url, out img)) { box.SetImage(img); return; }
            if (!thumbsLoading.Add(url)) return;
            Task.Run(() =>
            {
                string ct = null; string err = null; byte[] b = null;
                try { b = Nexus.ThumbnailBytes(url, out ct); } catch (Exception ex) { err = ex.Message; }
                return Tuple.Create(b, ct, err);
            }).ContinueWith(t =>
            {
                thumbsLoading.Remove(url);
                var r = t.Result;
                Image result = null;
                if (r.Item3 != null) ThumbProblem("couldn't download (" + r.Item3 + ").");
                else
                {
                    result = Nexus.Decode(r.Item1);
                    if (result == null)
                    {
                        ThumbProblem("Windows couldn't read the image format (" + Nexus.Kind(r.Item1, r.Item2) + ").");
                        Nexus.ForgetThumbnail(url);
                    }
                }
                thumbs[url] = result;
                // The card may have been rebuilt meanwhile; update any card showing this image.
                foreach (var b in resultsList.Controls.OfType<ItemCard>().Select(c => c.Thumb).Concat(installedList.Controls.OfType<ItemCard>().Select(c => c.Thumb)))
                    if (b != null && !b.IsDisposed && url.Equals(b.Tag as string)) b.SetImage(result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        ItemCard InstalledCard(GameEntry g, PresetEntry e)
        {
            bool active = Presets.IsActive(g, e);
            string from = e.ModId > 0 ? "From Nexus Mods" : "From " + (e.Source ?? "a file");
            var card = new ItemCard(e.Name, null, string.Join(", ", e.Inis ?? new string[0]), (active ? "Active preset   \u00B7   " : "") + from, false,
                                    e.ModId > 0 ? Nexus.ModUrl(e.Domain, e.ModId) : null);
            card.SetState(true, true, (active ? "Active preset   \u00B7   " : "") + from);
            if (e.Image != null) { card.EnableThumb(e.ModId > 0 ? Nexus.ModUrl(e.Domain, e.ModId) : null); ShowThumb(card.Thumb, e.Image); }
            card.Action.Click += async (s, ev) => await RemovePreset(e);
            if (!active && e.Inis != null && e.Inis.Length > 0)
            {
                var make = new LinkButton("Make it the active preset");
                make.Click += (s, ev) =>
                {
                    try { Presets.MakeActive(g, e, e.Inis[0]); Log(g.Name + ": " + e.Name + " is now the active preset. Restart the game or pick it in ReShade's preset list."); }
                    catch (Exception ex) { Warn(ex.Message); }
                    ShowPresets();
                };
                card.Extra.Controls.Add(make);
            }
            return card;
        }

        // ------------------------------------------------------------ install / remove

        async Task InstallNexus(NexusMod m)
        {
            var g = Selected; if (g == null) return;
            if (!g.Installed) { Warn("Install the feeder in this game first."); return; }
            if (nexusUser == null || !nexusUser.Premium)
            {
                try { Process.Start(Nexus.ModUrl(m.Domain, m.ModId) + "?tab=files"); } catch { }
                Log("Opened " + m.Name + " on Nexus. Download it there, then use Install from a file.");
                return;
            }
            if (!Env.IsWritable(g.Folder)) { Warn("Windows won't let this app change " + g.Folder + ". Restart the app as administrator."); return; }

            SetBusy(true, "checking " + m.Name);
            List<NexusFile> files = null;
            try { files = Nexus.Installable(await Task.Run(() => Nexus.Files(nexusKey, m.Domain, m.ModId))); }
            catch (Exception ex) { Log("Nexus: " + ex.Message); Warn(ex.Message); }
            SetBusy(false);
            if (files == null) return;
            if (files.Count == 0) { Warn(m.Name + " has no downloadable files right now."); return; }

            NexusFile pick = files[0];
            if (files.Count > 1)
            {
                var opts = files.Select(f => new PickItem
                {
                    Text = f.Name + (string.IsNullOrEmpty(f.Version) ? "" : "   v" + f.Version),
                    Detail = (f.Primary ? "Main file, " : (f.Category ?? "").ToLowerInvariant() + ", ") + (f.SizeKb >= 1024 ? (f.SizeKb / 1024.0).ToString("0.0") + " MB" : f.SizeKb + " KB"),
                    Value = f
                }).ToList();
                using (var dlg = new PickForm("Which file of " + m.Name + "?", "Presets often come in several versions. The main file is listed first.", opts, false, null))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Picked == null) return;
                    pick = (NexusFile)dlg.Picked.Value;
                }
            }

            SetBusy(true, "installing " + m.Name);
            string tmp = Path.Combine(Path.GetTempPath(), "dlss5-preset-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                var f = pick;
                string file = Path.Combine(tmp, string.IsNullOrEmpty(f.FileName) ? "preset.zip" : Path.GetFileName(f.FileName));
                await Task.Run(() =>
                {
                    string url = Nexus.DownloadLink(nexusKey, m.Domain, m.ModId, f.FileId);
                    Log("Downloading " + f.Name + " from Nexus Mods");
                    Nexus.Download(nexusKey, url, file);
                });
                await InstallArchive(g, file, tmp, m.Name, "Nexus Mods", m.Domain, m.ModId, f.FileId);
                Presets.SetImage(g, m.Name, m.Image);
            }
            catch (Exception ex) { Log(m.Name + ": " + ex.Message); Warn(m.Name + " couldn't be installed:\n\n" + ex.Message); }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            SetBusy(false);
            ShowPresets(); ShowShaders();
        }

        async Task InstallFromDisk(bool folder)
        {
            var g = Selected; if (g == null || !g.Installed) return;
            string path = null;
            if (folder)
                using (var dlg = new FolderBrowserDialog { Description = "Pick the folder with the preset (.ini) and its shaders" })
                { if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.SelectedPath; }
            else
                using (var dlg = new OpenFileDialog { Title = "Pick a preset download", Filter = "Preset downloads|*.zip;*.7z;*.rar;*.ini|All files|*.*" })
                { if (dlg.ShowDialog(this) == DialogResult.OK) path = dlg.FileName; }
            if (path == null) return;
            if (!Env.IsWritable(g.Folder)) { Warn("Windows won't let this app change " + g.Folder + ". Restart the app as administrator."); return; }
            SetBusy(true, "installing a preset");
            string tmp = Path.Combine(Path.GetTempPath(), "dlss5-preset-" + Guid.NewGuid().ToString("N"));
            try
            {
                string name = folder ? Path.GetFileName(path.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(path);
                if (folder) await InstallFromRoot(g, path, name, path, null, 0, 0);
                else { Directory.CreateDirectory(tmp); await InstallArchive(g, path, tmp, name, Path.GetFileName(path), null, 0, 0); }
            }
            catch (Exception ex) { Log("Preset: " + ex.Message); Warn("The preset couldn't be installed:\n\n" + ex.Message); }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
            SetBusy(false);
            ShowPresets(); ShowShaders();
        }

        async Task InstallArchive(GameEntry g, string file, string tmp, string name, string source, string domain, int modId, int fileId)
        {
            string root = await Task.Run(() => Presets.Extract(file, Path.Combine(tmp, "x")));
            await InstallFromRoot(g, root, name, source, domain, modId, fileId);
        }

        async Task InstallFromRoot(GameEntry g, string root, string name, string source, string domain, int modId, int fileId)
        {
            if (packs.Count == 0) packs = await Task.Run(() => Shaders.Load(false, Log));
            var catalog = packs;
            Func<List<string>, bool> ask = clashing => (bool)Invoke(new Func<bool>(() =>
                MessageBox.Show(this,
                    name + " turns on effects that tend to fight neural rendering:\n\n" + string.Join("\n", clashing.Take(12)) +
                    (clashing.Count > 12 ? "\n...and " + (clashing.Count - 12) + " more" : "") +
                    "\n\nTurn them off in this preset? You can turn them back on in ReShade any time.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes));
            var res = await Task.Run(() => Presets.Install(g, root, name, source, domain, modId, fileId, catalog, ask, Log));
            var lines = new List<string> { g.Name + ": installed preset " + res.Name + (res.Active != null ? " and made " + res.Active + " the active preset." : ".") };
            if (res.AddedPacks.Count > 0) lines.Add("  Also installed the shader packs it needs: " + string.Join(", ", res.AddedPacks) + ".");
            if (res.TurnedOff.Count > 0) lines.Add("  Turned off " + res.TurnedOff.Count + " effects that clash with neural rendering.");
            if (res.Missing.Count > 0) lines.Add("  Still missing (not on ReShade's list; check the preset's description): " + string.Join(", ", res.Missing) + ".");
            foreach (var l in lines) Log(l);
            if (res.Missing.Count > 0)
                Warn(res.Name + " is installed, but it uses effects that couldn't be found automatically:\n\n" + string.Join("\n", res.Missing) +
                     "\n\nThe preset's Nexus page usually says where to get them. ReShade will skip them until they're in reshade-shaders.");
        }

        async Task RemovePreset(PresetEntry e)
        {
            var g = Selected; if (g == null) return;
            if (MessageBox.Show(this, "Remove the preset " + e.Name + " from " + g.Name + "?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true, "removing " + e.Name);
            try
            {
                int n = await Task.Run(() => Presets.Remove(g, e, Log));
                Log(g.Name + ": removed preset " + e.Name + " (" + n + " files). Shader packs it needed stay; remove them on the Shaders tab if you like.");
            }
            catch (Exception ex) { Log(e.Name + ": " + ex.Message); }
            SetBusy(false);
            ShowPresets(); ShowShaders();
        }
    }

    // ================================================================== a simple pick-one dialog

    class PickItem
    {
        public string Text, Detail; public object Value;
        public override string ToString() { return Detail == null ? Text : Text + "      " + Detail; }
    }

    class PickForm : Form
    {
        public PickItem Picked;
        readonly ListBox box; readonly TextBox filter; readonly List<PickItem> all;

        public PickForm(string title, string subtitle, List<PickItem> items, bool withFilter, string initialFilter)
        {
            all = items;
            AutoScaleMode = AutoScaleMode.None;
            Text = title; Font = Theme.Body; BackColor = Theme.Bg; ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.Px(640), Ui.Px(withFilter ? 560 : 420));
            Padding = new Padding(Ui.Px(28), Ui.Px(24), Ui.Px(28), Ui.Px(20));

            var t = new Label { Text = title, Font = Theme.Hero, AutoSize = true };
            var sub = new Wrap(subtitle, Theme.Body, Theme.Muted);
            filter = new TextBox { Font = Theme.Body, Visible = withFilter, Text = withFilter ? initialFilter ?? "" : "" };
            var card = new Card { Padding = new Padding(Ui.Px(12), Ui.Px(8), Ui.Px(6), Ui.Px(8)) };
            box = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Theme.Body, BackColor = Color.White };
            card.Controls.Add(box);
            var ok = new RoundButton("Choose", BtnStyle.Primary) { Margin = new Padding(0) };
            var cancel = new RoundButton("Cancel", BtnStyle.Secondary) { Margin = new Padding(0) };
            ok.Click += (s, e) => { Picked = box.SelectedItem as PickItem; if (Picked != null) DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            box.DoubleClick += (s, e) => { Picked = box.SelectedItem as PickItem; if (Picked != null) DialogResult = DialogResult.OK; };
            filter.TextChanged += (s, e) => Fill();
            Controls.AddRange(new Control[] { t, sub, filter, card, ok, cancel });
            Resize += (s, e) =>
            {
                int w = ClientSize.Width - Padding.Horizontal, x = Padding.Left;
                t.Location = new Point(x - Ui.Px(2), Padding.Top);
                sub.FitWidth(w); sub.Location = new Point(x, t.Bottom + Ui.Px(6));
                int y = sub.Bottom + Ui.Px(14);
                if (filter.Visible) { filter.Bounds = new Rectangle(x, y, w, filter.Height); y = filter.Bottom + Ui.Px(10); }
                ok.Location = new Point(ClientSize.Width - Padding.Right - ok.Width, ClientSize.Height - Padding.Bottom - ok.Height);
                cancel.Location = new Point(ok.Left - cancel.Width - Ui.Px(10), ok.Top);
                card.Bounds = new Rectangle(x, y, w, ok.Top - Ui.Px(16) - y);
            };
            Shown += (s, e) => { Fill(); OnResize(EventArgs.Empty); if (filter.Visible) filter.Focus(); else box.Focus(); };
        }

        void Fill()
        {
            string q = filter.Text.Trim();
            box.BeginUpdate();
            box.Items.Clear();
            foreach (var i in all.Where(i => q.Length == 0 || i.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(300)) box.Items.Add(i);
            box.EndUpdate();
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }
    }
}
