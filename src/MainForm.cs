using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Another DLSS Tool")]
[assembly: System.Reflection.AssemblyProduct("Another DLSS Tool")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.6.0.0")]

namespace Dlss5Setup
{
    interface IFit { void FitWidth(int w); }

    // Lays children out top to bottom at a given width.
    class Stack : Panel, IFit
    {
        public Stack() { SetStyle(ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) BackColor = Parent.BackColor; }
        public void FitWidth(int w) { Width = w; Relayout(); }
        public void Relayout()
        {
            SuspendLayout();
            int y = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int cw = Width - c.Margin.Horizontal;
                var f = c as IFit;
                if (f != null) f.FitWidth(cw);
                else if ("stretch".Equals(c.Tag)) c.Width = cw;
                c.Location = new Point(c.Margin.Left, y + c.Margin.Top);
                y += c.Margin.Top + c.Height + c.Margin.Bottom;
            }
            Height = Math.Max(1, y);
            ResumeLayout(false);
        }
    }

    class FitWrap : Wrap, IFit
    {
        public FitWrap(string text, Font font, Color color) : base(text, font, color) { }
        void IFit.FitWidth(int w) { FitWidth(w); }
    }

    class FitNote : Note, IFit
    {
        public FitNote(string text, Color bar, Color back) : base(text, bar, back) { }
        void IFit.FitWidth(int w) { FitWidth(w); }
    }

    // A card that stacks its children.
    class StackCard : Card, IFit
    {
        public readonly Stack Body = new Stack();
        public StackCard() { Controls.Add(Body); Body.BackColor = Theme.Surface; Padding = new Padding(Ui.Px(22), Ui.Px(8), Ui.Px(22), Ui.Px(8)); }
        public void FitWidth(int w)
        {
            Width = w;
            Body.Location = new Point(Padding.Left, Padding.Top);
            Body.FitWidth(w - Padding.Horizontal);
            Height = Body.Height + Padding.Vertical;
        }
    }

    // Title and help on the left, a control on the right, a divider underneath.
    class Row : Panel, IFit
    {
        readonly Label title; readonly Wrap help; readonly Control right;
        public bool Divider = true;
        public Row(string titleText, string helpText, Control rightControl)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            title = new Label { Text = titleText, Font = Theme.Bold, AutoSize = true, UseMnemonic = false, ForeColor = Theme.Text };
            help = new Wrap(helpText ?? "", Theme.Body, Theme.Muted) { Visible = !string.IsNullOrEmpty(helpText) };
            right = rightControl;
            Controls.AddRange(new Control[] { title, help, right });
        }
        public void SetHelp(string t) { help.Text = t ?? ""; help.Visible = !string.IsNullOrEmpty(t); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) BackColor = Parent.BackColor; }
        public void FitWidth(int w)
        {
            Width = w;
            int pad = Ui.Px(16), gap = Ui.Px(24);
            int textW = Math.Max(Ui.Px(160), w - right.Width - gap);
            title.Location = new Point(0, pad);
            help.FitWidth(textW);
            help.Location = new Point(0, title.Bottom + Ui.Px(3));
            int leftH = (help.Visible ? help.Bottom : title.Bottom) - pad;
            int h = Math.Max(leftH, right.Height);
            right.Location = new Point(w - right.Width, pad + Math.Max(0, (Math.Min(h, Ui.Px(44)) - right.Height) / 2));
            Height = pad * 2 + h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Divider) using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    class ItemCard : Card, IFit
    {
        readonly Label title, by; readonly Wrap desc, meta;
        public readonly RoundButton Action;
        readonly LinkButton project;
        readonly string baseMeta; readonly bool featured;
        public readonly FlowLayoutPanel Extra = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Surface };
        public ThumbBox Thumb;
        public void EnableThumb(string link)
        {
            Thumb = new ThumbBox { Size = new Size(Ui.Px(208), Ui.Px(117)) };
            if (link != null) { Thumb.Cursor = Cursors.Hand; Thumb.Click += (s, e) => { try { Process.Start(link); } catch { } }; }
            Controls.Add(Thumb);
        }
        public ItemCard(string name, string author, string text, string metaText, bool isFeatured, string repo)
        {
            Radius = 10; Margin = new Padding(0, 0, 0, Ui.Px(10));
            baseMeta = metaText; featured = isFeatured;
            title = new Label { Text = name, Font = Theme.H3, AutoSize = true, UseMnemonic = false };
            by = new Label { Text = author != null ? "by " + author : "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, UseMnemonic = false };
            desc = new Wrap(text, Theme.Body, Theme.Text);
            meta = new Wrap(metaText + (featured ? "   \u00B7   Recommended" : ""), Theme.Small, Theme.Muted);
            Action = new RoundButton("Install", BtnStyle.Secondary) { Margin = new Padding(0) };
            project = new LinkButton("Project page") { Visible = repo != null };
            project.Click += (s, e) => { try { Process.Start(repo); } catch { } };
            Controls.AddRange(new Control[] { title, by, desc, meta, Action, project, Extra });
        }
        public void SetText(string text) { desc.Text = text; }
        public void SetState(bool installed, bool canInstall, string reason)
        {
            Action.Kind = installed ? BtnStyle.Danger : BtnStyle.Secondary;
            Action.Text = installed ? "Remove" : "Install";
            Action.Enabled = installed || canInstall;
            meta.Text = (installed ? "Installed   \u00B7   " : "") + (reason ?? baseMeta) + (featured ? "   \u00B7   Recommended" : "");
            meta.ForeColor = installed ? Theme.Accent : Theme.Muted;
        }
        public void FitWidth(int w)
        {
            Width = w;
            int p = Ui.Px(18), right = w - p;
            Action.Location = new Point(right - Action.Width, p);
            project.Location = new Point(right - project.Width, Action.Bottom + Ui.Px(6));
            int left = p;
            if (Thumb != null) { Thumb.Location = new Point(p, p); left = Thumb.Right + Ui.Px(18); }
            int textW = Action.Left - left - Ui.Px(20);
            title.Location = new Point(left, p - Ui.Px(2));
            title.MaximumSize = new Size(Math.Max(Ui.Px(80), textW), 0);
            by.Location = new Point(Math.Min(title.Right + Ui.Px(8), left + textW - by.Width), title.Top + (title.Height - by.Height) / 2 + Ui.Px(1));
            desc.FitWidth(textW); desc.Location = new Point(left, title.Bottom + Ui.Px(4));
            meta.FitWidth(textW); meta.Location = new Point(left, desc.Bottom + Ui.Px(8));
            int bottom = meta.Bottom;
            Extra.Visible = Extra.Controls.Count > 0;
            if (Thumb != null) bottom = Math.Max(bottom, Thumb.Bottom);
            if (Extra.Controls.Count > 0) { Extra.Size = Extra.PreferredSize; Extra.Location = new Point(left - Ui.Px(14), meta.Bottom + Ui.Px(8)); bottom = Math.Max(bottom, Extra.Bottom); }
            Height = Math.Max(bottom, project.Visible ? project.Bottom : Action.Bottom) + p;
        }
    }

    class Hero : Panel, IFit
    {
        public readonly Label Title = new Label { Font = Theme.Hero, AutoSize = true, UseMnemonic = false, ForeColor = Theme.Text };
        public readonly Pill Status = new Pill { Dot = true };
        public readonly Label Path = new Label { ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
        public readonly LinkButton ChangeExe = new LinkButton("Change .exe");
        public readonly LinkButton Remove = new LinkButton("Remove from list");
        public readonly FlowLayoutPanel Chips = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        public readonly RoundButton Install = new RoundButton("Install", BtnStyle.Primary) { Margin = new Padding(0) };
        public readonly ThumbBox Art = new ThumbBox { Size = new Size(Ui.Px(216), Ui.Px(101)) };
        public Hero()
        {
            Controls.AddRange(new Control[] { Art, Title, Status, Path, ChangeExe, Remove, Chips, Install });
            Remove.Color = Theme.Muted;
        }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) { BackColor = Parent.BackColor; Chips.BackColor = BackColor; } }
        public void FitWidth(int w)
        {
            Width = w;
            Install.Location = new Point(w - Install.Width, Ui.Px(4));
            Art.Location = new Point(0, Ui.Px(2));
            int x0 = Art.Right + Ui.Px(20);
            int textRight = Install.Left - Ui.Px(24);
            Title.MaximumSize = new Size(Math.Max(Ui.Px(120), textRight - x0 - Status.Width - Ui.Px(12)), 0);
            Title.Location = new Point(x0 - Ui.Px(2), 0);
            Status.Location = new Point(Title.Right + Ui.Px(12), Title.Top + (Title.Height - Status.Height) / 2 + Ui.Px(2));
            int y = Title.Bottom + Ui.Px(4);
            Remove.Location = new Point(textRight - Remove.Width, y);
            ChangeExe.Location = new Point(Remove.Left - ChangeExe.Width - Ui.Px(4), y);
            Path.Location = new Point(x0, y);
            Path.Size = new Size(Math.Max(Ui.Px(60), ChangeExe.Left - Ui.Px(12) - x0), ChangeExe.Height);
            Chips.Size = Chips.PreferredSize;
            Chips.Location = new Point(x0, Path.Bottom + Ui.Px(12));
            Height = Math.Max(Math.Max(Chips.Bottom, Install.Bottom), Art.Bottom) + Ui.Px(6);
        }
    }

    // ================================================================== main window

    partial class MainForm : Form
    {
        readonly List<GameEntry> games = new List<GameEntry>();
        AppSettings settings = Json.Read<AppSettings>(Paths.Settings) ?? new AppSettings();
        DllInfo dlss, nr; string dlssErr, nrErr;
        List<ShaderPack> packs = new List<ShaderPack>();
        bool loading, busy, showAllPacks;
        string gpu;

        // sidebar
        GameList list; Label gamesCaption; RoundButton bScan, bAdd; Panel filesRow; Label filesState; Pill filesDot;
        // main
        Panel main, emptyView, gameView, pageHost; Hero hero; TabStrip tabs; Label statusBar;
        Chip cApi, cBits, cRoute;
        Panel[] pages = new Panel[5]; Stack[] stacks = new Stack[4];
        Stack notesStack;
        Segmented optApi, optConsumer, optMv; Toggle optWatermark, optHelper;
        Row rowApi, rowConsumer, rowMv, rowWatermark, rowHelper, rowDfc;
        RoundButton bDfc;
        readonly Dictionary<string, Toggle> tweakToggles = new Dictionary<string, Toggle>();
        readonly Dictionary<string, Row> tweakRows = new Dictionary<string, Row>();
        FitWrap qualityIntro; RoundButton bQualityReset, bQualityGuide;
        RoundButton bTest, bVerify, bLog, bFolder, bUninstall;
        FitWrap shaderIntro; ItemCard rtgiCard; LinkButton rtgiPick, rtgiLook, packRefresh; Toggle packShowAll; Stack freeList; Label packTitle;
        List<string> rtgiFound = new List<string>(); bool rtgiSearching;
        TextBox logBox;

        static readonly string[] ApiChoices = { "Auto", "D3D9", "D3D8", "D3D" };

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "Another DLSS Tool";
            Font = Theme.Body; ForeColor = Theme.Text; BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.Px(1200), Ui.Px(800));
            MinimumSize = new Size(Ui.Px(980), Ui.Px(640));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build();
            Shown += async (s, e) => await StartUp();
            FormClosing += (s, e) =>
            {
                if (busy && MessageBox.Show(this, "Something is still running. Close the app anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
            };
        }

        // ------------------------------------------------------------ layout

        void Build()
        {
            SuspendLayout();

            // Sidebar --------------------------------------------------------
            var side = new Panel { Dock = DockStyle.Left, Width = Ui.Px(300), BackColor = Theme.Side, Padding = new Padding(Ui.Px(14), Ui.Px(18), Ui.Px(14), Ui.Px(16)) };
            var brand = new Brand { Dock = DockStyle.Top };
            var sp1 = new Panel { Dock = DockStyle.Top, Height = Ui.Px(20), BackColor = Theme.Side };
            gamesCaption = new Label { Text = "GAMES", Font = Theme.Caps, ForeColor = Theme.SideMuted, Dock = DockStyle.Top, Height = Ui.Px(26), Padding = new Padding(Ui.Px(4), 0, 0, 0) };
            list = new GameList { Dock = DockStyle.Fill };
            list.Art = g => ArtFor(g, Art.Portrait);

            var bottom = new Panel { Dock = DockStyle.Bottom, BackColor = Theme.Side, Height = Ui.Px(170) };
            bScan = new RoundButton("Find Steam games", BtnStyle.Dark) { Stretch = true };
            bAdd = new RoundButton("Add a game", BtnStyle.Dark) { Stretch = true };
            filesRow = new Panel { BackColor = Theme.Side, Cursor = Cursors.Hand, Height = Ui.Px(52) };
            var filesTitle = new Label { Text = "NVIDIA files", Font = Theme.Bold, ForeColor = Theme.SideText, AutoSize = true, Location = new Point(Ui.Px(4), Ui.Px(8)), Cursor = Cursors.Hand };
            filesState = new Label { Text = "Checking...", Font = Theme.Small, ForeColor = Theme.SideMuted, AutoSize = true, Location = new Point(Ui.Px(4), Ui.Px(28)), Cursor = Cursors.Hand };
            filesDot = new Pill { Dot = true, Back = Theme.SideHover, Fore = Theme.SideMuted, Cursor = Cursors.Hand };
            filesDot.Set("Manage", Theme.SideMuted, Theme.SideHover);
            filesRow.Controls.AddRange(new Control[] { filesTitle, filesState, filesDot });
            bottom.Controls.AddRange(new Control[] { bScan, bAdd, filesRow });
            bottom.Resize += (s, e) =>
            {
                int w = bottom.Width;
                bScan.Bounds = new Rectangle(0, Ui.Px(12), w, Ui.Px(38));
                bAdd.Bounds = new Rectangle(0, bScan.Bottom + Ui.Px(8), w, Ui.Px(38));
                filesRow.Bounds = new Rectangle(0, bAdd.Bottom + Ui.Px(14), w, Ui.Px(52));
                filesDot.Location = new Point(w - filesDot.Width - Ui.Px(2), Ui.Px(14));
            };
            bottom.Paint += (s, e) => { using (var p = new Pen(Theme.SideLine)) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0); };
            side.Controls.Add(list);
            side.Controls.Add(bottom);
            side.Controls.Add(gamesCaption);
            side.Controls.Add(sp1);
            side.Controls.Add(brand);

            // Main area ------------------------------------------------------
            main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(Ui.Px(36), Ui.Px(28), Ui.Px(36), Ui.Px(10)) };
            statusBar = new Label { Dock = DockStyle.Bottom, Height = Ui.Px(30), ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false, Text = "Ready" };

            emptyView = BuildEmpty();
            gameView = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            hero = new Hero { Dock = DockStyle.Top };
            cApi = new Chip(); cBits = new Chip(); cRoute = new Chip();
            hero.Chips.Controls.AddRange(new Control[] { cApi, cBits, cRoute });
            var sp2 = new Panel { Dock = DockStyle.Top, Height = Ui.Px(14), BackColor = Theme.Bg };
            tabs = new TabStrip("Overview", "Shaders", "Presets", "Tools", "Activity") { Dock = DockStyle.Top };
            pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            BuildPages();
            gameView.Controls.Add(pageHost);
            gameView.Controls.Add(tabs);
            gameView.Controls.Add(sp2);
            gameView.Controls.Add(hero);
            gameView.Resize += (s, e) => { hero.FitWidth(gameView.Width); };

            main.Controls.Add(gameView);
            main.Controls.Add(emptyView);
            main.Controls.Add(statusBar);

            Controls.Add(main);
            Controls.Add(side);
            Wire();
            ResumeLayout(true);
        }

        Panel BuildEmpty()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var logo = new LogoBox { Size = new Size(Ui.Px(88), Ui.Px(88)) };
            var t = new Label { Text = "Add your first game", Font = Theme.Hero, AutoSize = true, UseMnemonic = false };
            var d = new Wrap("Find your Steam games automatically, or pick any game's .exe. The app reads its graphics API and sets up the DLSS 5 feeder for it.", Theme.Body, Theme.Muted);
            var a = new RoundButton("Find Steam games", BtnStyle.Primary);
            var b = new RoundButton("Add a game", BtnStyle.Secondary);
            a.Click += async (s, e) => await ScanClick();
            b.Click += (s, e) => AddGameClick();
            p.Controls.AddRange(new Control[] { logo, t, d, a, b });
            p.Resize += (s, e) =>
            {
                int w = Math.Min(Ui.Px(520), p.Width);
                d.FitWidth(w);
                int total = logo.Height + Ui.Px(20) + t.Height + Ui.Px(10) + d.Height + Ui.Px(24) + a.Height;
                int y = Math.Max(0, (p.Height - total) / 2 - Ui.Px(30)), x = (p.Width - w) / 2;
                logo.Location = new Point(x, y);
                t.Location = new Point(x, logo.Bottom + Ui.Px(20)); d.Location = new Point(x, t.Bottom + Ui.Px(10));
                a.Location = new Point(x, d.Bottom + Ui.Px(24)); b.Location = new Point(a.Right + Ui.Px(10), a.Top);
            };
            return p;
        }

        Panel NewPage(out Stack stack)
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true, Visible = false };
            var st = new Stack { Location = new Point(0, Ui.Px(20)) };
            page.Controls.Add(st);
            var bottomPad = new Panel { Height = Ui.Px(24), BackColor = Theme.Bg };
            page.Controls.Add(bottomPad);
            Action fit = () =>
            {
                int w = page.Width - SystemInformation.VerticalScrollBarWidth - Ui.Px(6);
                st.FitWidth(w);
                st.Location = new Point(page.AutoScrollPosition.X, page.AutoScrollPosition.Y + Ui.Px(20));
                bottomPad.Bounds = new Rectangle(0, st.Bottom, 1, Ui.Px(24));
            };
            page.Resize += (s, e) => fit();
            page.Tag = fit;
            stack = st;
            return page;
        }

        void Refit(int i) { var f = pages[i].Tag as Action; if (f != null) f(); }

        void BuildPages()
        {
            // Overview
            Stack s0; pages[0] = NewPage(out s0); stacks[0] = s0;
            notesStack = new Stack { Margin = new Padding(0) };
            optApi = new Segmented("Auto", "Direct3D 9", "Direct3D 8", "Direct3D 10 to 12");
            optConsumer = new Segmented("Deep Fried Chicken", "RenoDX", "OptiScaler");
            optMv = new Segmented("Kernel", "QuantMotion");
            optWatermark = new Toggle(""); optHelper = new Toggle("");
            rowApi = new Row("Graphics API", "Auto uses what was detected in the .exe. Direct3D 8 and 9 go through dgVoodoo2.", optApi);
            rowConsumer = new Row("Neural rendering add-on", "Deep Fried Chicken is the one the project recommends for the best result. RenoDX downloads automatically; OptiScaler's DLSS-NR is the fallback. Reinstall after switching.", optConsumer);
            bDfc = new RoundButton("Pick zip", BtnStyle.Secondary) { Margin = new Padding(0) };
            rowDfc = new Row("Deep Fried Chicken files", "", bDfc);
            rowMv = new Row("Motion vectors", "Estimated by a LumeniteFX shader. Kernel is the recommended default.", optMv);
            rowWatermark = new Row("dgVoodoo watermark", "Shows a small dgVoodoo logo in-game so you can confirm it's active. Turn it off once things work.", optWatermark);
            rowHelper = new Row("Use the 64-bit helper", "For 64-bit games where DLSS won't run inside the game's own process.", optHelper) { Divider = false };
            var opts = new StackCard { Margin = new Padding(0, Ui.Px(4), 0, 0) };
            opts.Body.Controls.AddRange(new Control[] { rowApi, rowConsumer, rowDfc, rowMv, rowWatermark, rowHelper });
            var optTitle = new Label { Text = "Install options", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, Ui.Px(18), 0, Ui.Px(10)) };

            var qTitle = new Label { Text = "Image quality", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, Ui.Px(6), 0, Ui.Px(6)) };
            qualityIntro = new FitWrap("", Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, Ui.Px(10)) };
            var quality = new StackCard { Margin = new Padding(0) };
            foreach (var t in Quality.Tweaks)
            {
                var tg = new Toggle("");
                var tw = t;
                tg.CheckedChanged += async (s, e) => await TweakChanged(tw, tg);
                var row = new Row(t.Title, t.Help, tg);
                tweakToggles[t.Id] = tg; tweakRows[t.Id] = row;
                quality.Body.Controls.Add(row);
            }
            var qButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            bQualityGuide = new RoundButton("Open the guide", BtnStyle.Ghost) { Margin = new Padding(0, 0, Ui.Px(8), 0) };
            bQualityReset = new RoundButton("Reset to defaults", BtnStyle.Secondary) { Margin = new Padding(0) };
            qButtons.Controls.AddRange(new Control[] { bQualityGuide, bQualityReset });
            qButtons.Size = qButtons.PreferredSize;
            quality.Body.Controls.Add(new Row("Start over", "Puts the feeder's documented defaults back. Every change on this card applies while the game runs, except texture filtering.", qButtons) { Divider = false });
            s0.Controls.AddRange(new Control[] { notesStack, qTitle, qualityIntro, quality, optTitle, opts });

            // Shaders
            Stack sh; pages[1] = NewPage(out sh); stacks[1] = sh;
            shaderIntro = new FitWrap("", Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, Ui.Px(14)) };
            var rtTitle = new Label { Text = "Your RTGI", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.Px(10)) };
            rtgiCard = new ItemCard("RTGI", "Pascal Gilcher", "Looking for your copy...", "Paid shader, installed from your own files", false, "https://www.martysmods.com");
            rtgiPick = new LinkButton("Pick the file") { Margin = new Padding(Ui.Px(14), 0, 0, 0) };
            rtgiLook = new LinkButton("Look again");
            rtgiCard.Extra.Controls.AddRange(new Control[] { rtgiPick, rtgiLook });
            packTitle = new Label { Text = "Top shader packs", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, Ui.Px(10), 0, Ui.Px(8)) };
            var bar = new Panel { Height = Ui.Px(32), Tag = "stretch", Margin = new Padding(0, 0, 0, Ui.Px(10)) };
            packShowAll = new Toggle("Show every pack on ReShade's official list") { Location = new Point(0, Ui.Px(2)) };
            packRefresh = new LinkButton("Refresh list");
            bar.Controls.AddRange(new Control[] { packShowAll, packRefresh });
            bar.Resize += (s, e) => packRefresh.Location = new Point(bar.Width - packRefresh.Width, (bar.Height - packRefresh.Height) / 2);
            bar.ParentChanged += (s, e) => { if (bar.Parent != null) bar.BackColor = bar.Parent.BackColor; };
            freeList = new Stack { Margin = new Padding(0) };
            sh.Controls.AddRange(new Control[] { shaderIntro, rtTitle, rtgiCard, packTitle, bar, freeList });

            // Presets
            Stack sp; pages[2] = NewPage(out sp); stacks[2] = sp;
            BuildPresetsPage(sp);

            // Tools
            Stack s2; pages[3] = NewPage(out s2); stacks[3] = s2;
            bTest = new RoundButton("Run self-test", BtnStyle.Secondary) { Margin = new Padding(0) };
            bVerify = new RoundButton("Verify", BtnStyle.Secondary) { Margin = new Padding(0) };
            bLog = new RoundButton("Open logs", BtnStyle.Secondary) { Margin = new Padding(0) };
            bFolder = new RoundButton("Open folder", BtnStyle.Secondary) { Margin = new Padding(0) };
            bUninstall = new RoundButton("Uninstall", BtnStyle.Danger) { Margin = new Padding(0) };
            var check = new StackCard { Margin = new Padding(0, 0, 0, Ui.Px(16)) };
            check.Body.Controls.AddRange(new Control[]
            {
                new Row("Self-test", "Checks that your driver and DLSS files work together, without starting the game. 300/300 is a pass.", bTest),
                new Row("Verify", "Runs the project's checker against this game's folder and lists anything missing or misconfigured.", bVerify),
                new Row("Logs", "Opens the feeder, helper and ReShade logs. They appear after the first launch.", bLog),
                new Row("Game folder", "Opens the folder in Explorer.", bFolder) { Divider = false }
            });
            var danger = new StackCard { Margin = new Padding(0) };
            danger.Body.Controls.Add(new Row("Uninstall from this game", "Removes everything the install and any add-ons added, and restores the game's original files.", bUninstall) { Divider = false });
            var toolsTitle = new Label { Text = "Check and troubleshoot", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.Px(10)) };
            var dangerTitle = new Label { Text = "Remove", Font = Theme.H3, AutoSize = true, Margin = new Padding(0, Ui.Px(6), 0, Ui.Px(10)) };
            s2.Controls.AddRange(new Control[] { toolsTitle, check, dangerTitle, danger });

            // Activity
            pages[4] = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false, Padding = new Padding(0, Ui.Px(20), 0, Ui.Px(8)) };
            var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(Ui.Px(16), Ui.Px(12), Ui.Px(6), Ui.Px(12)) };
            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Font = Theme.Mono, BackColor = Color.White, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, WordWrap = true
            };
            logCard.Controls.Add(logBox);
            pages[4].Controls.Add(logCard);

            foreach (var p in pages) pageHost.Controls.Add(p);
            pages[0].Visible = true;
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            if (i < 4) Refit(i);
            if (i == 4) { logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); }
        }

        // ------------------------------------------------------------ events

        void Wire()
        {
            list.SelectedIndexChanged += (s, e) => ShowDetail();
            bAdd.Click += (s, e) => AddGameClick();
            bScan.Click += async (s, e) => await ScanClick();
            EventHandler files = (s, e) => OpenFiles();
            foreach (Control c in filesRow.Controls) c.Click += files;
            filesRow.Click += files;
            hero.Remove.Click += (s, e) => RemoveClick();
            hero.ChangeExe.Click += (s, e) => ChangeExeClick();
            hero.Install.Click += async (s, e) => await InstallClick();
            tabs.SelectedIndexChanged += (s, e) => ShowPage(tabs.SelectedIndex);
            EventHandler save = (s, e) => SaveOptions();
            optApi.SelectedIndexChanged += save; optConsumer.SelectedIndexChanged += save; optMv.SelectedIndexChanged += save;
            optWatermark.CheckedChanged += save; optHelper.CheckedChanged += save;
            packShowAll.CheckedChanged += (s, e) => { showAllPacks = packShowAll.Checked; ShowShaders(); };
            packRefresh.Click += async (s, e) => await LoadPacks(true);
            bDfc.Click += (s, e) => PickDfc();
            bQualityReset.Click += async (s, e) => await ResetQuality();
            bQualityGuide.Click += (s, e) => { try { Process.Start(Quality.DocsUrl); } catch { } };
            rtgiCard.Action.Click += async (s, e) => await RtgiClick();
            rtgiPick.Click += (s, e) => PickRtgi();
            rtgiLook.Click += async (s, e) => await FindRtgi();
            bTest.Click += async (s, e) => await SelfTestClick();
            bVerify.Click += async (s, e) => await VerifyClick();
            bLog.Click += (s, e) => OpenLogs();
            bFolder.Click += (s, e) => { var g = Selected; if (g != null && Directory.Exists(g.Folder)) Process.Start("explorer.exe", Env.Q(g.Folder)); };
            bUninstall.Click += async (s, e) => await UninstallClick();
        }

        GameEntry Selected { get { return list.SelectedItem as GameEntry; } }

        public void Log(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "   " + msg + Environment.NewLine);
            if (!busy) statusBar.Text = msg.Trim();
            else statusBar.Text = "Working  \u2014  " + msg.Trim();
        }

        void SetBusy(bool b, string what = null)
        {
            busy = b;
            foreach (var c in new Control[] { list, bScan, bAdd, hero.Install, hero.ChangeExe, hero.Remove, pages[0], pages[1], pages[2], pages[3], emptyView })
                c.Enabled = !b;
            UseWaitCursor = b;
            statusBar.Text = b ? "Working  \u2014  " + (what ?? "please wait") : "Ready";
            statusBar.ForeColor = b ? Theme.Accent : Theme.Muted;
        }

        // ------------------------------------------------------------ library

        void SaveLibrary() { Json.Write(Paths.Library, games.ToArray()); }

        void LoadLibrary()
        {
            var saved = Json.Read<GameEntry[]>(Paths.Library);
            if (saved != null) foreach (var g in saved) if (g != null && !string.IsNullOrEmpty(g.Exe)) { g.Normalize(); games.Add(g); }
            foreach (var old in new[] { "DeadSpace2", "Saboteur" })
            {
                var gj = Json.Read<GameJson>(Path.Combine(Paths.Root, old, "game.json"));
                if (gj == null || string.IsNullOrEmpty(gj.Exe) || !File.Exists(gj.Exe) || games.Any(x => x.Id == old)) continue;
                var e = Detect.NewEntry(gj.Exe, old == "Saboteur" ? "The Saboteur" : "Dead Space 2");
                e.Id = old;
                e.Installed = File.Exists(Path.Combine(Paths.Root, old, "installed.json"));
                e.LastExit = 0;
                if (!games.Any(x => x.Exe.Equals(e.Exe, StringComparison.OrdinalIgnoreCase))) games.Add(e);
            }
        }

        void RefreshList(GameEntry select = null)
        {
            var keep = select ?? Selected;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var g in games.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) list.Items.Add(g);
            list.EndUpdate();
            gamesCaption.Text = games.Count > 0 ? "GAMES   " + games.Count : "GAMES";
            if (keep != null && list.Items.Contains(keep)) list.SelectedItem = keep;
            else if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
            ShowDetail();
        }

        GameEntry AddGame(string exe, string name)
        {
            var existing = games.FirstOrDefault(g => g.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var e = Detect.NewEntry(exe, name);
            games.Add(e);
            return e;
        }

        // ------------------------------------------------------------ details

        void ShowDetail()
        {
            var g = Selected;
            emptyView.Visible = g == null;
            gameView.Visible = g != null;
            if (g == null) return;
            loading = true;
            gameView.SuspendLayout();

            hero.Title.Text = g.Name;
            hero.Art.SetImage(ArtFor(g, Art.Header));
            Color sc; string st = g.StatusText(out sc);
            hero.Status.Set(st, sc, sc == Theme.Accent ? Theme.AccentSoft : sc == Theme.Warn ? Theme.WarnSoft : sc == Theme.Fail ? Theme.FailSoft : Theme.Chip);
            hero.Path.Text = g.Exe;
            cApi.Text = g.ApiLabel; cBits.Text = g.Bits; cRoute.Text = g.Route;
            bool canInstall = g.Supported && g.ExeExists;
            hero.Install.Text = g.Installed ? "Reinstall" : "Install";
            hero.Install.Kind = g.Installed ? BtnStyle.Secondary : BtnStyle.Primary;
            hero.Install.Font = Theme.Bold;
            hero.Install.Enabled = canInstall && !busy;
            hero.FitWidth(gameView.Width);

            notesStack.Controls.Clear();
            if (!g.ExeExists) Note("The .exe is gone. Use Change .exe if the game moved.", Theme.Fail, Theme.FailSoft);
            if (g.AntiCheat != null)
                Note("This game ships anti-cheat (" + g.AntiCheat + "). Injecting DLLs can get an account banned. Only use this offline, in single-player modes, or not at all.", Theme.Fail, Theme.FailSoft);
            if (g.NativeDlss)
                Note("This game already has its own DLSS. The feeder is for games without it; here the RenoDX DLSS 5 add-on on its own is the simpler route (RenoDX Discord, #DLSS5).", Theme.Warn, Theme.WarnSoft);
            if (!g.Supported)
                Note(g.ApiLabel + " games aren't handled by this app. The official installer supports them; run it directly from " + Urls.Project + ".", Theme.Muted, Theme.InfoSoft);
            if (g.Api == "Unknown")
                Note("The graphics API couldn't be read from the .exe, which is common for games that load it at runtime. Leave it on Auto and the official installer will detect it, or pick it below.", Theme.Muted, Theme.InfoSoft);
            if (g.Bits == "32-bit")
                Note("Expect roughly half the usual frame rate: DLSS runs at native resolution in a separate 64-bit helper.", Theme.Muted, Theme.InfoSoft);
            if (nr != null && nr.Signed && gpu != null && Regex.IsMatch(gpu, @"RTX\s*[234]0\d\d"))
                Note("You're using NVIDIA's signed neural model. On your " + gpu + " the neural pass only runs with the RTX 40 build.", Theme.Warn, Theme.WarnSoft);
            if (g.Installed)
                Note("In-game: press Home for ReShade, then turn on neural rendering in the add-on's panel. Turn the game's own anti-aliasing off.", Theme.Accent, Theme.AccentSoft);
            else if (canInstall)
                Note("Install backs up the game folder first, then runs the official installer in its own window. Answer its questions there.", Theme.Muted, Theme.InfoSoft);

            optApi.SelectedIndex = Math.Max(0, Array.IndexOf(ApiChoices, g.ApiChoice));
            optConsumer.SelectedIndex = g.Consumer == "DFC" ? 0 : g.Consumer == "OptiScaler" ? 2 : 1;
            rowDfc.Visible = g.Consumer == "DFC";
            bool dfcOk = !string.IsNullOrEmpty(settings.DfcZip) && File.Exists(settings.DfcZip);
            rowDfc.SetHelp(dfcOk
                ? "Using " + settings.DfcZip
                : "Its files aren't published publicly. Get Deep-Fried-Chicken-*.zip (1.4.8 or newer) from its Discord, linked in the project's README, then pick it here.");
            bDfc.Text = dfcOk ? "Change" : "Pick zip";
            ShowQuality(g);
            optMv.SelectedIndex = g.Mv == 4 ? 1 : 0;
            optWatermark.Checked = g.Watermark; optWatermark.Enabled = g.UsesDgVoodoo;
            rowWatermark.SetHelp(g.UsesDgVoodoo
                ? "Shows a small dgVoodoo logo in-game so you can confirm it's active. Turn it off once things work."
                : "Only applies to Direct3D 8 and 9 games, which run through dgVoodoo2.");
            optHelper.Checked = g.Helper; optHelper.Enabled = g.Bits == "64-bit";
            rowHelper.SetHelp(g.Bits == "64-bit"
                ? "For 64-bit games where DLSS won't run inside the game's own process."
                : "32-bit games always use the helper.");

            bool has = Snapshot.Exists(g) || g.Installed;
            bUninstall.Enabled = has; bLog.Enabled = has; bVerify.Enabled = has; bTest.Enabled = has;
            bFolder.Enabled = g.ExeExists;

            ShowShaders();
            ShowPresets();
            gameView.ResumeLayout(true);
            for (int i = 0; i < 4; i++) Refit(i);
            loading = false;
        }

        void Note(string text, Color bar, Color back) { notesStack.Controls.Add(new FitNote(text, bar, back)); }

        void SaveOptions()
        {
            if (loading) return;
            var g = Selected; if (g == null) return;
            g.ApiChoice = ApiChoices[Math.Max(0, optApi.SelectedIndex)];
            g.Consumer = optConsumer.SelectedIndex == 0 ? "DFC" : optConsumer.SelectedIndex == 2 ? "OptiScaler" : "RenoDX";
            g.Mv = optMv.SelectedIndex == 1 ? 4 : 3;
            g.Watermark = optWatermark.Checked;
            g.Helper = optHelper.Checked;
            SaveLibrary();
            list.Invalidate();
            ShowDetail();
        }

        // ------------------------------------------------------------ image quality

        void ShowQuality(GameEntry g)
        {
            bool ready = g.Installed && Directory.Exists(g.Folder);
            qualityIntro.Text = ready
                ? "Fixes from the DLSS5-Feeder project's own settings guide, checked against its current version online before they're written. Biggest difference first: use Deep Fried Chicken as the neural add-on (below), keep the game at your monitor's native resolution, and turn the game's own anti-aliasing off."
                : "Available once the feeder is installed in this game.";
            foreach (var t in Quality.Tweaks)
            {
                var tg = tweakToggles[t.Id];
                bool usable = ready && (!t.DgVoodoo || (g.UsesDgVoodoo && Quality.HasDgVoodoo(g)));
                bool on = false;
                try { on = usable && Quality.IsOn(g, t); } catch { }
                tg.Checked = on;
                tg.Enabled = usable;
                if (t.DgVoodoo)
                    tweakRows[t.Id].SetHelp(g.UsesDgVoodoo ? t.Help : "Only for Direct3D 8 and 9 games, which run through dgVoodoo2. Modern games set this in their own options.");
            }
            bQualityReset.Enabled = ready;
        }

        async Task TweakChanged(Tweak t, Toggle tg)
        {
            if (loading) return;
            var g = Selected;
            if (g == null || !g.Installed) return;
            bool on = tg.Checked;
            SetBusy(true, t.Title.ToLowerInvariant());
            try
            {
                var skipped = await Task.Run(() => Quality.Apply(g, t, on, Log));
                if (skipped.Count > 0)
                    Log(t.Title + ": the project's guide no longer lists " + string.Join(", ", skipped) + ", so that part was left alone.");
                Log(g.Name + ": " + t.Title.ToLowerInvariant() + (on ? " on" : " off") +
                    (t.DgVoodoo ? ". Restart the game to see it." : ". Applies while the game runs."));
            }
            catch (Exception ex)
            {
                Log(t.Title + ": " + ex.Message);
                Warn(t.Title + " couldn't be changed:\n\n" + ex.Message);
            }
            SetBusy(false);
            loading = true; ShowQuality(g); loading = false;
        }

        async Task ResetQuality()
        {
            var g = Selected; if (g == null) return;
            SetBusy(true, "resetting image quality");
            try
            {
                await Task.Run(() => Quality.Reset(g, Log));
                Log(g.Name + ": image quality settings back to the feeder's defaults.");
            }
            catch (Exception ex) { Log("Reset failed: " + ex.Message); }
            SetBusy(false);
            loading = true; ShowQuality(g); loading = false;
        }

        void PickDfc()
        {
            using (var dlg = new OpenFileDialog { Title = "Pick the Deep-Fried-Chicken zip from its Discord", Filter = "Deep Fried Chicken zip|*.zip|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!Regex.IsMatch(Path.GetFileName(dlg.FileName), "chicken", RegexOptions.IgnoreCase) &&
                    MessageBox.Show(this, "That file name doesn't mention Deep Fried Chicken. Use it anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                settings.DfcZip = dlg.FileName;
            }
            Json.Write(Paths.Settings, settings);
            Log("Deep Fried Chicken zip set to " + settings.DfcZip);
            ShowDetail();
        }

        // ------------------------------------------------------------ shaders

        string ShaderBadge(GameEntry g)
        {
            if (!g.Installed) return null;
            int n = Shaders.InstalledCount(g);
            return n > 0 ? n.ToString() : null;
        }

        async Task LoadPacks(bool refresh)
        {
            packRefresh.Enabled = false;
            if (refresh) Log("Refreshing ReShade's shader list");
            packs = await Task.Run(() => Shaders.Load(refresh, Log));
            packRefresh.Enabled = true;
            if (refresh) Log("ReShade's shader list has " + packs.Count + " packs.");
            ShowShaders();
        }

        string RtgiSource
        {
            get
            {
                if (!string.IsNullOrEmpty(settings.RtgiPath) && File.Exists(settings.RtgiPath)) return settings.RtgiPath;
                return rtgiFound.FirstOrDefault();
            }
        }

        async Task FindRtgi()
        {
            if (rtgiSearching) return;
            rtgiSearching = true; rtgiLook.Enabled = false;
            ShowShaders();
            var known = games.ToList();
            rtgiFound = await Task.Run(() => { try { return Shaders.FindRtgi(known); } catch { return new List<string>(); } });
            rtgiSearching = false; rtgiLook.Enabled = true;
            Log(rtgiFound.Count == 0
                ? "Didn't find RTGI in Downloads, Desktop, Documents or your games' shader folders."
                : "Found RTGI on this PC: " + string.Join(";  ", rtgiFound.Take(3)) + (rtgiFound.Count > 3 ? "  (and " + (rtgiFound.Count - 3) + " more)" : ""));
            ShowShaders();
        }

        void PickRtgi()
        {
            using (var dlg = new OpenFileDialog { Title = "Pick your RTGI shader (.fx) or the archive it came in", Filter = "RTGI shader or archive|*.fx;*.zip;*.7z;*.rar|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.RtgiPath = dlg.FileName;
            }
            Json.Write(Paths.Settings, settings);
            Log("RTGI source set to " + settings.RtgiPath);
            ShowShaders();
        }

        void ShowShaders()
        {
            var g = Selected; if (g == null || rtgiCard == null) return;
            bool ready = g.Installed;
            shaderIntro.Text = ready
                ? "Shader packs from ReShade's official list, installed into this game. The lighting ones trace rays through the depth buffer the feeder already sets up, so they only see what's on screen, and they're expensive. After installing, press Home in-game, then Reload, and turn effects on."
                : "Install the feeder first. It sets up ReShade and depth access, which these shaders need.";
            shaderIntro.ForeColor = ready ? Theme.Muted : Theme.Warn;

            string src = RtgiSource;
            bool rtInst = ready && Shaders.IsInstalled(g, "RTGI");
            if (rtgiSearching) rtgiCard.SetText("Looking in Downloads, Desktop, Documents and your games' shader folders...");
            else if (src != null)
                rtgiCard.SetText("Found on this PC: " + src + (rtgiFound.Count > 1 && settings.RtgiPath == null ? "   (" + (rtgiFound.Count - 1) + " other copies found; Pick the file to choose another)" : ""));
            else rtgiCard.SetText("Not found in Downloads, Desktop, Documents or your games' shader folders. If it's somewhere else, pick the .fx file or the archive you downloaded from Patreon.");
            rtgiCard.SetState(rtInst, ready && src != null, src == null ? "Paid shader, installed from your own files" : null);

            freeList.SuspendLayout();
            foreach (Control c in freeList.Controls.Cast<Control>().ToList()) c.Dispose();
            freeList.Controls.Clear();
            var shown = packs.Where(p => showAllPacks || p.Featured || (ready && Shaders.IsInstalled(g, p.Name))).ToList();
            packTitle.Text = showAllPacks ? "All shader packs" : "Top shader packs";
            if (packs.Count == 0)
                freeList.Controls.Add(new FitWrap("ReShade's shader list hasn't loaded. Check your connection and press Refresh list.", Theme.Body, Theme.Muted));
            foreach (var p in shown)
            {
                var card = new ItemCard(p.Name, p.Author, p.Tip ?? p.Description ?? "", "Free, from GitHub", p.Featured && showAllPacks, p.Repo);
                card.SetState(ready && Shaders.IsInstalled(g, p.Name), ready, null);
                var pack = p;
                card.Action.Click += async (s, e) => await ShaderClick(pack);
                freeList.Controls.Add(card);
            }
            freeList.ResumeLayout(false);
            UpdateBadges(g);
            tabs.Invalidate();
            Refit(1);
        }

        async Task RtgiClick()
        {
            string src = RtgiSource;
            if (src == null && !Shaders.IsInstalled(Selected, "RTGI")) { PickRtgi(); return; }
            await ShaderClick(new ShaderPack { Name = "RTGI", LocalPath = src });
        }

        async Task ShaderClick(ShaderPack pack)
        {
            var g = Selected; if (g == null) return;
            bool installed = Shaders.IsInstalled(g, pack.Name);
            if (!Env.IsWritable(g.Folder)) { Warn("Windows won't let this app change " + g.Folder + ". Restart the app as administrator."); return; }
            SetBusy(true, (installed ? "removing " : "installing ") + pack.Name);
            try
            {
                if (installed)
                {
                    int n = await Task.Run(() => Shaders.Remove(g, pack.Name));
                    Log(g.Name + ": removed " + pack.Name + " (" + n + " files).");
                }
                else
                {
                    await Task.Run(() => Shaders.Install(g, pack, Log));
                    Log(g.Name + ": installed " + pack.Name + ". In-game, press Home, then Reload, and turn it on.");
                }
            }
            catch (Exception ex) { Log(pack.Name + ": " + ex.Message); Warn(pack.Name + " couldn't be " + (installed ? "removed" : "installed") + ":\n\n" + ex.Message); }
            SetBusy(false);
            ShowShaders();
        }

        // ------------------------------------------------------------ NVIDIA files

        async Task LoadDlls()
        {
            filesState.Text = "Checking...";
            var d = await Task.Run(() => Describe(settings.DlssPath, "nvngx_dlss.dll", "dlss"));
            var n = await Task.Run(() => Describe(settings.NrPath, "nvngx_dlssnr.dll", "dlssnr"));
            dlss = d.Item1; dlssErr = d.Item2; nr = n.Item1; nrErr = n.Item2;
            if (dlss == null || nr == null) { filesState.Text = "Not set up"; filesState.ForeColor = Theme.Fail; filesDot.Set("Fix", Theme.Fail, Theme.SideHover); }
            else if (!nr.Signed || !dlss.Signed) { filesState.Text = "Ready, neural model unsigned"; filesState.ForeColor = Theme.SideMuted; filesDot.Set("Manage", Theme.SideText, Theme.SideHover); }
            else { filesState.Text = "Ready, both signed"; filesState.ForeColor = Theme.SideMuted; filesDot.Set("Manage", Theme.SideText, Theme.SideHover); }
            filesDot.Location = new Point(filesRow.Width - filesDot.Width - Ui.Px(2), Ui.Px(14));
        }

        Tuple<DllInfo, string> Describe(string chosen, string dll, string sub)
        {
            string src = chosen;
            if (string.IsNullOrEmpty(src) || !File.Exists(src))
            {
                string b = Path.Combine(Paths.BundleDir, dll);
                src = File.Exists(b) ? b : null;
            }
            if (src == null) return Tuple.Create<DllInfo, string>(null, "Not set");
            try { return Tuple.Create(Dlls.Describe(Dlls.Resolve(src, dll, Path.Combine(Paths.Root, "files", sub)), src), (string)null); }
            catch (Exception ex) { return Tuple.Create<DllInfo, string>(null, ex.Message); }
        }

        void OpenFiles()
        {
            if (busy) return;
            using (var f = new FilesForm(this)) f.ShowDialog(this);
        }

        public DllInfo Dlss { get { return dlss; } }
        public DllInfo Nr { get { return nr; } }
        public string DlssErr { get { return dlssErr; } }
        public string NrErr { get { return nrErr; } }

        public async Task PickDll(bool isDlss, IWin32Window owner)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = isDlss ? "Pick nvngx_dlss.dll or the zip it came in" : "Pick nvngx_dlssnr.dll or the archive it came in";
                dlg.Filter = isDlss ? "DLSS file or archive|*.dll;*.zip|All files|*.*" : "Neural model or archive|*.dll;*.zip;*.rar|All files|*.*";
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                if (isDlss) settings.DlssPath = dlg.FileName; else settings.NrPath = dlg.FileName;
            }
            Json.Write(Paths.Settings, settings);
            SetBusy(true, "checking files");
            await LoadDlls();
            SetBusy(false);
            Log((isDlss ? "DLSS file" : "Neural model") + " set to " + (isDlss ? settings.DlssPath : settings.NrPath));
            ShowDetail();
        }

        public async Task UseBundled(bool isDlss)
        {
            if (isDlss) settings.DlssPath = null; else settings.NrPath = null;
            Json.Write(Paths.Settings, settings);
            SetBusy(true, "checking files");
            await LoadDlls();
            SetBusy(false);
            ShowDetail();
        }

        // ------------------------------------------------------------ actions

        void Warn(string text) { MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        string PickExe(string title)
        {
            using (var dlg = new OpenFileDialog { Title = title, Filter = "Programs (*.exe)|*.exe" })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        void AddGameClick()
        {
            string f = PickExe("Pick the game's main .exe (not a launcher)");
            if (f == null) return;
            try
            {
                var g = AddGame(f, null);
                SaveLibrary(); RefreshList(g);
                Log("Added " + g.Name + ": " + g.ApiLabel + ", " + g.Bits);
            }
            catch (Exception ex) { Log("Couldn't add that game: " + ex.Message); }
        }

        void ChangeExeClick()
        {
            var g = Selected; if (g == null) return;
            string f = PickExe("Pick " + g.Name + "'s main .exe");
            if (f == null) return;
            var fresh = Detect.NewEntry(f, g.Name);
            g.Exe = fresh.Exe; g.Api = fresh.Api; g.Bits = fresh.Bits; g.NativeDlss = fresh.NativeDlss; g.AntiCheat = fresh.AntiCheat;
            ForgetArt(g);
            SaveLibrary(); RefreshList(g);
            Log(g.Name + ": now using " + f + " (" + g.ApiLabel + ", " + g.Bits + ")");
        }

        void RemoveClick()
        {
            var g = Selected; if (g == null) return;
            string msg = "Remove " + g.Name + " from the list?";
            if (g.Installed) msg += "\n\nThe feeder stays installed in the game folder. Uninstall it from the Tools tab first if you want it gone.";
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            games.Remove(g); SaveLibrary(); RefreshList();
        }

        async Task ScanClick()
        {
            SetBusy(true, "scanning Steam libraries");
            var found = new List<ScanItem>();
            try
            {
                var known = games.Select(g => g.Exe).ToList();
                found = await Task.Run(() =>
                {
                    var res = new List<ScanItem>();
                    var steam = Detect.SteamGames();
                    Log(steam.Count == 0 ? "No Steam library found. Use Add a game instead." : "Checking " + steam.Count + " Steam games");
                    foreach (var s in steam)
                    {
                        if (known.Any(k => k.StartsWith(s.Folder, StringComparison.OrdinalIgnoreCase))) continue;
                        string exe = Detect.FindMainExe(s.Folder);
                        if (exe == null) continue;
                        string api, bits; Detect.Api(exe, out api, out bits);
                        if (api == "Vulkan" || api == "OpenGL") continue;
                        res.Add(new ScanItem { Name = s.Name, Exe = exe, Api = api, Bits = bits });
                    }
                    return res.OrderBy(r => r.Name).ToList();
                });
            }
            catch (Exception ex) { Log("Scan stopped: " + ex.Message); }
            SetBusy(false);
            if (found.Count == 0) { Log("No new Direct3D games found in your Steam libraries."); return; }
            using (var dlg = new ScanForm(found))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetBusy(true, "adding games");
                GameEntry last = null;
                foreach (var item in dlg.Picked)
                {
                    try
                    {
                        var it = item;
                        var e = await Task.Run(() => Detect.NewEntry(it.Exe, it.Name));
                        if (!games.Any(x => x.Exe.Equals(e.Exe, StringComparison.OrdinalIgnoreCase))) { games.Add(e); last = e; }
                        Log("Added " + item.Name);
                    }
                    catch (Exception ex) { Log("Couldn't add " + item.Name + ": " + ex.Message); }
                }
                SaveLibrary(); SetBusy(false); RefreshList(last);
            }
        }

        async Task InstallClick()
        {
            var g = Selected; if (g == null) return;
            if (!Env.IsWritable(g.Folder)) { Warn("Windows won't let this app change\n" + g.Folder + "\n\nClose the app and open it again as administrator."); return; }
            if (dlss == null || nr == null) { Warn("Set up both NVIDIA files first: click NVIDIA files at the bottom left."); return; }
            if (g.Consumer == "DFC" && (string.IsNullOrEmpty(settings.DfcZip) || !File.Exists(settings.DfcZip)))
            {
                Warn("Deep Fried Chicken's files aren't public, so pick the Deep-Fried-Chicken zip you downloaded from its Discord first.");
                PickDfc();
                if (string.IsNullOrEmpty(settings.DfcZip) || !File.Exists(settings.DfcZip)) return;
            }
            var parts = new List<string>();
            if (g.UsesDgVoodoo) parts.Add("dgVoodoo2");
            parts.AddRange(new[] { "ReShade", "the feeder", "LumeniteFX motion vectors", g.Consumer == "OptiScaler" ? "OptiScaler DLSS-NR" : g.Consumer == "DFC" ? "Deep Fried Chicken" : "the RenoDX DLSS 5 add-on" });
            var lines = new List<string>
            {
                (g.Installed ? "Reinstall" : "Install") + " the DLSS 5 feeder in " + g.Name + "?", "", "Folder: " + g.Folder,
                "Adds: " + string.Join(", ", parts) + ".", "The folder is backed up first, so Uninstall can undo this."
            };
            if (g.AntiCheat != null) { lines.Add(""); lines.Add("WARNING: this game has anti-cheat (" + g.AntiCheat + "). Only continue if you will play offline."); }
            if (g.NativeDlss) { lines.Add(""); lines.Add("Note: this game already has DLSS, so the feeder may not be needed."); }
            if (!nr.Signed) { lines.Add(""); lines.Add("The neural model is not signed by NVIDIA. SHA-256:\n" + nr.Hash); lines.Add("Compare it with the hash posted in the RenoDX Discord."); }
            if (MessageBox.Show(this, string.Join("\n", lines), Text, MessageBoxButtons.YesNo,
                    g.AntiCheat != null ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;

            SetBusy(true, "installing in " + g.Name);
            try
            {
                string installer = await Task.Run(() => Net.Download(Urls.Installer, "Install-DLSS5Feeder.ps1", Log));
                await Task.Run(() => Snapshot.TakeIfMissing(g, Log));
                var a = new List<string>
                {
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Env.Q(installer), Env.Q(g.Exe),
                    "-Api", g.EffectiveApi, "-Consumer", g.Consumer, "-MvProvider", g.Mv.ToString(),
                    "-DlssDll", Env.Q(dlss.Path), "-DlssNrDll", Env.Q(nr.Path)
                };
                if (g.Consumer == "OptiScaler") a.AddRange(new[] { "-OptiScalerFork", "wilsjo2" });
                if (g.Consumer == "DFC") a.AddRange(new[] { "-DfcZip", Env.Q(settings.DfcZip) });
                if (g.Bits == "64-bit") a.AddRange(new[] { "-HelperMode", g.Helper ? "Yes" : "No" });
                if (g.UsesDgVoodoo && g.Watermark) a.Add("-DgVoodooWatermark");
                Log(g.Name + ": the official installer is running in its own window");
                var p = Process.Start(new ProcessStartInfo("powershell.exe", string.Join(" ", a)) { UseShellExecute = true });
                int code = await Task.Run(() => { p.WaitForExit(); return p.ExitCode; });
                int added = 0, changed = 0;
                await Task.Run(() => Snapshot.RecordInstall(g, out added, out changed));
                g.Installed = true; g.LastExit = code;
                SaveLibrary();
                Log(code == 0
                    ? g.Name + ": installed. " + added + " files added, " + changed + " changed."
                    : g.Name + ": the installer reported problems (exit code " + code + "). Its window lists each failure and the fix.");
            }
            catch (Exception ex) { Log("Install stopped: " + ex.Message); }
            SetBusy(false);
            list.Invalidate(); ShowDetail();
        }

        async Task SelfTestClick()
        {
            var g = Selected; if (g == null) return;
            string host = Path.Combine(g.Folder, @"host64\dlss5-feed-host64.exe");
            if (!File.Exists(host))
                host = games.Select(x => Path.Combine(x.Folder ?? "", @"host64\dlss5-feed-host64.exe")).FirstOrDefault(File.Exists);
            if (host == null) { Log("The self-test needs the 64-bit helper, which comes with 32-bit games and helper mode. Use Verify for this game."); return; }
            SetBusy(true, "self-test");
            tabs.SelectedIndex = 4;
            Log("Running the DLSS self-test (no game needed)");
            try
            {
                string output = await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo(host, "--test")
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(host) };
                    using (var p = Process.Start(psi))
                    {
                        var err = p.StandardError.ReadToEndAsync();
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        return o + err.Result;
                    }
                });
                foreach (var line in output.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0).Reverse().Take(12).Reverse()) Log("    " + line);
                Log(Regex.IsMatch(output, @"300\s*/\s*300")
                    ? "Self-test passed: driver and DLSS files work together."
                    : "Self-test did not report 300/300. See the lines above and host64\\dlss5-feed-host.log.");
            }
            catch (Exception ex) { Log("Self-test failed to run: " + ex.Message); }
            SetBusy(false);
        }

        async Task VerifyClick()
        {
            var g = Selected; if (g == null) return;
            SetBusy(true, "verifying");
            try
            {
                string v = await Task.Run(() => Net.Download(Urls.Verifier, "Verify-DLSS5Feeder.ps1", Log));
                Log(g.Name + ": the verifier is running in its own window");
                var p = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -NoExit -File " + Env.Q(v) + " -GamePath " + Env.Q(g.Exe)) { UseShellExecute = true });
                await Task.Run(() => p.WaitForExit());
            }
            catch (Exception ex) { Log("Verify failed: " + ex.Message); }
            SetBusy(false);
        }

        void OpenLogs()
        {
            var g = Selected; if (g == null) return;
            var logs = new[] { "dlss5-feed.log", @"host64\dlss5-feed-host.log", "ReShade.log" }
                .Select(x => Path.Combine(g.Folder, x)).Where(File.Exists).ToList();
            if (logs.Count == 0) { Log("No logs yet. They appear after the first launch with the feeder installed."); return; }
            foreach (var l in logs) Process.Start("notepad.exe", Env.Q(l));
        }

        async Task UninstallClick()
        {
            var g = Selected; if (g == null) return;
            if (!Snapshot.Exists(g)) { Log(g.Name + ": no snapshot, so there is nothing to undo."); return; }
            if (MessageBox.Show(this, "Remove the feeder and any add-ons from " + g.Name + " and restore its original files?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!Env.IsWritable(g.Folder)) { Warn("This folder needs administrator rights. Open the app again as administrator."); return; }
            SetBusy(true, "uninstalling from " + g.Name);
            try
            {
                int removed = 0, restored = 0;
                await Task.Run(() => Snapshot.Undo(g, out removed, out restored));
                g.Installed = false; g.LastExit = null; SaveLibrary();
                Log(g.Name + ": removed " + removed + " files and restored " + restored + " originals.");
                Log("If you allowed a Windows Defender exclusion during install, remove it in Windows Security, Virus and threat protection, Exclusions.");
            }
            catch (Exception ex) { Log("Uninstall stopped: " + ex.Message); }
            SetBusy(false);
            list.Invalidate(); ShowDetail();
        }

        // ------------------------------------------------------------ start-up

        async Task StartUp()
        {
            Log("Every install step comes from the official DLSS5-Feeder project on GitHub.");
            if (Env.IsWindows && !Env.IsAdmin()) Log("Running without administrator rights. Games under Program Files can't be changed until you reopen the app as administrator.");
            SetBusy(true, "loading");
            try
            {
                gpu = await Task.Run(() => Program.NvidiaGpu());
                Log(gpu != null ? "GPU: " + gpu : "No NVIDIA GPU found. DLSS needs an RTX card.");
                await Task.Run(() => LoadLibrary());
                if (games.Count == 0)
                {
                    var added = await Task.Run(() => Program.FindDefaults());
                    foreach (var d in added) if (!games.Any(x => x.Exe.Equals(d.Exe, StringComparison.OrdinalIgnoreCase))) { games.Add(d); Log("Added " + d.Name); }
                }
                SaveLibrary();
                RefreshList();
                await LoadDlls();
                await LoadPacks(false);
                await ConnectNexus(null, false);
            }
            catch (Exception ex) { Log("Start-up problem: " + ex.Message); }
            await FindRtgi();
            SetBusy(false);
            RefreshList();
        }
    }

    // ================================================================== NVIDIA files dialog

    class FilesForm : Form
    {
        readonly MainForm owner;
        readonly Stack body = new Stack();
        readonly RoundButton close = new RoundButton("Done", BtnStyle.Primary);

        public FilesForm(MainForm o)
        {
            owner = o;
            AutoScaleMode = AutoScaleMode.None;
            Text = "NVIDIA files"; Font = Theme.Body; BackColor = Theme.Bg; ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(Ui.Px(640), Ui.Px(430));
            Padding = new Padding(Ui.Px(28), Ui.Px(24), Ui.Px(28), Ui.Px(20));
            close.Click += (s, e) => Close();
            Controls.Add(body); Controls.Add(close);
            Shown += (s, e) => Refresh2();
        }

        // Rebuilds the rows and sizes the dialog to fit them.
        void Refresh2()
        {
            Fill();
            body.Location = new Point(Padding.Left, Padding.Top);
            body.FitWidth(ClientSize.Width - Padding.Horizontal);
            int h = body.Bottom + Ui.Px(24) + close.Height + Padding.Bottom;
            if (ClientSize.Height != h) ClientSize = new Size(ClientSize.Width, h);
            close.Location = new Point(ClientSize.Width - Padding.Right - close.Width, ClientSize.Height - Padding.Bottom - close.Height);
        }

        void Fill()
        {
            body.Controls.Clear();
            body.Controls.Add(new Label { Text = "NVIDIA files", Font = Theme.Hero, AutoSize = true, Margin = new Padding(0, 0, 0, Ui.Px(4)) });
            body.Controls.Add(new FitWrap("Every install copies these two files into the game. The bundled ones are used unless you pick others.", Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, Ui.Px(16)) });
            var card = new StackCard();
            card.Body.Controls.Add(FileRow("DLSS", "nvngx_dlss.dll", owner.Dlss, owner.DlssErr, true));
            var nrRow = FileRow("Neural model", "nvngx_dlssnr.dll", owner.Nr, owner.NrErr, false);
            nrRow.Divider = false;
            card.Body.Controls.Add(nrRow);
            body.Controls.Add(card);
            if (owner.Nr != null && !owner.Nr.Signed)
                body.Controls.Add(new FitNote("The neural model isn't signed by NVIDIA, which is expected for the RTX 40 build. Copy its hash and compare it with the one posted in the RenoDX Discord.", Theme.Warn, Theme.WarnSoft) { Margin = new Padding(0, Ui.Px(14), 0, 0) });
        }

        Row FileRow(string title, string dll, DllInfo d, string err, bool isDlss)
        {
            var btns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            var change = new RoundButton("Change", BtnStyle.Secondary) { Margin = new Padding(Ui.Px(8), 0, 0, 0) };
            change.Click += async (s, e) => { await owner.PickDll(isDlss, this); Refresh2(); };
            if (d != null && !d.Bundled)
            {
                var bundled = new RoundButton("Use bundled", BtnStyle.Ghost) { Margin = new Padding(Ui.Px(8), 0, 0, 0) };
                bundled.Click += async (s, e) => { await owner.UseBundled(isDlss); Refresh2(); };
                btns.Controls.Add(bundled);
            }
            if (d != null)
            {
                var copy = new RoundButton("Copy hash", BtnStyle.Ghost) { Margin = new Padding(Ui.Px(8), 0, 0, 0) };
                copy.Click += (s, e) => { Clipboard.SetText(d.Hash); copy.Text = "Copied"; };
                btns.Controls.Add(copy);
            }
            btns.Controls.Add(change);
            btns.Size = btns.PreferredSize;
            string help = d == null
                ? dll + "\n" + (err ?? "Not set")
                : dll + ", version " + d.Version + "\n" + (d.Signed ? "Signed by " + d.Signer : "Not signed by NVIDIA") + ", " + (d.Bundled ? "bundled with the app" : d.Source);
            return new Row(title, help, btns);
        }
    }

    // ================================================================== Steam scan dialog

    class ScanItem
    {
        public string Name, Exe, Api, Bits;
        public override string ToString()
        {
            string label = Api == "DX11/12" ? "DirectX 11/12" : Api == "Unknown" ? "API unknown" : Api.Replace("DX", "DirectX ");
            return Name + "      " + label + ", " + Bits;
        }
    }

    class ScanForm : Form
    {
        readonly CheckedListBox box;
        public List<ScanItem> Picked = new List<ScanItem>();

        public ScanForm(List<ScanItem> items)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "Find Steam games"; Font = Theme.Body; BackColor = Theme.Bg; ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.Px(720), Ui.Px(560));
            Padding = new Padding(Ui.Px(28), Ui.Px(24), Ui.Px(28), Ui.Px(20));

            var title = new Label { Text = "Pick the games to add", Font = Theme.Hero, AutoSize = true };
            var sub = new Wrap("Found " + items.Count + " Direct3D games that aren't in your list yet. Games marked API unknown may still work; the installer checks them at install time.", Theme.Body, Theme.Muted);
            var card = new Card { Padding = new Padding(Ui.Px(14), Ui.Px(10), Ui.Px(6), Ui.Px(10)) };
            box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Color.White, Font = Theme.Body };
            foreach (var i in items) box.Items.Add(i, i.Api != "Unknown");
            card.Controls.Add(box);
            var ok = new RoundButton("Add selected", BtnStyle.Primary) { Margin = new Padding(0) };
            var cancel = new RoundButton("Cancel", BtnStyle.Secondary) { Margin = new Padding(0) };
            var all = new LinkButton("Select all"); var none = new LinkButton("Select none");
            all.Click += (s, e) => { for (int i = 0; i < box.Items.Count; i++) box.SetItemChecked(i, true); };
            none.Click += (s, e) => { for (int i = 0; i < box.Items.Count; i++) box.SetItemChecked(i, false); };
            ok.Click += (s, e) => { Picked = box.CheckedItems.Cast<ScanItem>().ToList(); DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            Controls.AddRange(new Control[] { title, sub, card, ok, cancel, all, none });
            Resize += (s, e) =>
            {
                int w = ClientSize.Width - Padding.Horizontal, x = Padding.Left;
                title.Location = new Point(x - Ui.Px(2), Padding.Top);
                sub.FitWidth(w); sub.Location = new Point(x, title.Bottom + Ui.Px(6));
                ok.Location = new Point(ClientSize.Width - Padding.Right - ok.Width, ClientSize.Height - Padding.Bottom - ok.Height);
                cancel.Location = new Point(ok.Left - cancel.Width - Ui.Px(10), ok.Top);
                all.Location = new Point(x - Ui.Px(14) + Ui.Px(14), ok.Top + (ok.Height - all.Height) / 2);
                none.Location = new Point(all.Right + Ui.Px(14), all.Top);
                card.Bounds = new Rectangle(x, sub.Bottom + Ui.Px(16), w, ok.Top - Ui.Px(16) - (sub.Bottom + Ui.Px(16)));
            };
            Shown += (s, e) => OnResize(EventArgs.Empty);
        }
    }

    // ================================================================== entry point

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            bool skipElevate = args.Contains("--no-elevate") || args.Contains("--elevated");
            if (Env.IsWindows && !Env.IsAdmin() && !skipElevate)
            {
                // Game folders under Program Files can only be changed with administrator rights.
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--elevated") { UseShellExecute = true, Verb = "runas" });
                    return 0;
                }
                catch (Win32Exception) { /* UAC declined: carry on without admin rights. */ }
            }

            Native.TrySetDpiAware();
            Ui.Init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(Paths.Root);
            Application.Run(new MainForm());
            return 0;
        }

        public static string NvidiaGpu()
        {
            if (!Env.IsWindows) return null;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (k == null) return null;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (var d = k.OpenSubKey(sub))
                            {
                                var name = d == null ? null : d.GetValue("DriverDesc") as string;
                                if (name != null && name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return name;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        public static List<GameEntry> FindDefaults()
        {
            var res = new List<GameEntry>();
            var wanted = new[] { new[] { "Dead Space 2", "deadspace2.exe" }, new[] { "The Saboteur", "Saboteur.exe" } };
            foreach (var w in wanted)
                foreach (var lib in Detect.SteamLibraries())
                {
                    string folder = Path.Combine(lib, "steamapps", "common", w[0]);
                    if (!Directory.Exists(folder)) continue;
                    string hit = Detect.Walk(folder, 3, false).FirstOrDefault(f => Path.GetFileName(f).Equals(w[1], StringComparison.OrdinalIgnoreCase));
                    if (hit != null) { res.Add(Detect.NewEntry(hit, w[0])); break; }
                }
            return res;
        }

        static void Crash(Exception ex)
        {
            string text = DateTime.Now + Environment.NewLine + (ex == null ? "Unknown error" : ex.ToString());
            try { Directory.CreateDirectory(Paths.Root); File.AppendAllText(Paths.CrashLog, text + Environment.NewLine + Environment.NewLine); } catch { }
            MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "Unknown error" : ex.Message) +
                "\n\nDetails were saved to:\n" + Paths.CrashLog, "Another DLSS Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
