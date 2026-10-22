using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Dlss5Setup
{
    static class Ui
    {
        public static float S = 1f;
        public static int Px(float v) { return (int)Math.Round(v * S); }
        public static void Init()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f; } catch { S = 1f; }
        }
    }

    static class Theme
    {
        static Color Hex(string h) { return ColorTranslator.FromHtml(h); }

        // Sidebar
        public static readonly Color Side = Hex("#17212A");
        public static readonly Color SideHover = Hex("#1F2B36");
        public static readonly Color SideSel = Hex("#26343F");
        public static readonly Color SideText = Hex("#E8EDF1");
        public static readonly Color SideMuted = Hex("#8E9CA9");
        public static readonly Color SideLine = Hex("#2A3642");

        // Content
        public static readonly Color Bg = Hex("#F3F5F7");
        public static readonly Color Surface = Color.White;
        public static readonly Color Line = Hex("#E1E6EB");
        public static readonly Color LineStrong = Hex("#C6CFD8");
        public static readonly Color Text = Hex("#17212A");
        public static readonly Color Muted = Hex("#5F6C79");
        public static readonly Color Chip = Hex("#EDF0F3");

        public static readonly Color Accent = Hex("#2E7D4F");
        public static readonly Color AccentHover = Hex("#276B44");
        public static readonly Color AccentSoft = Hex("#E4F1E9");
        public static readonly Color AccentOnDark = Hex("#5CC28A");
        public static readonly Color Warn = Hex("#A8691A");
        public static readonly Color WarnSoft = Hex("#FBF0DC");
        public static readonly Color Fail = Hex("#B83A2E");
        public static readonly Color FailSoft = Hex("#FBE6E3");
        public static readonly Color InfoSoft = Hex("#EDF1F5");

        static string Pick(params string[] names)
        {
            using (var fonts = new InstalledFontCollection())
                foreach (var n in names)
                    if (fonts.Families.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) return n;
            return names[names.Length - 1];
        }

        static readonly string Family = Pick("Segoe UI Variable Text", "Segoe UI");
        static readonly string FamilySemibold = Pick("Segoe UI Variable Display Semib", "Segoe UI Semibold");

        public static readonly Font Body = new Font(Family, 9.75f);
        public static readonly Font Bold = new Font(FamilySemibold, 9.75f);
        public static readonly Font Small = new Font(Family, 8.75f);
        public static readonly Font SmallBold = new Font(FamilySemibold, 8.75f);
        public static readonly Font Caps = new Font(FamilySemibold, 8f);
        public static readonly Font Brand = new Font(FamilySemibold, 12.5f);
        public static readonly Font Hero = new Font(FamilySemibold, 20f);
        public static readonly Font H3 = new Font(FamilySemibold, 11.5f);
        public static readonly Font Mono = new Font("Consolas", 9f);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color fill, Color? border = null, float width = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue) using (var pen = new Pen(border.Value, width)) g.DrawPath(pen, path);
            }
        }

        // Draws an image cropped to fill a rounded box; small images (icons) sit centred on a tinted tile instead.
        public static void Cover(Graphics g, Image img, RectangleF r, float radius, Color tile)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = Round(r, radius))
            {
                if (img == null || img.Width <= 64)
                {
                    using (var b = new SolidBrush(tile)) g.FillPath(b, path);
                    if (img != null)
                    {
                        float s = Math.Min(Math.Min(r.Width, r.Height) * 0.6f, img.Width * 2f);
                        g.DrawImage(img, r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
                    }
                    return;
                }
                float scale = Math.Max(r.Width / img.Width, r.Height / img.Height);
                float w = img.Width * scale, h = img.Height * scale;
                var state = g.Save();
                g.SetClip(path);
                g.DrawImage(img, new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
                g.Restore(state);
            }
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    // Base for owner-drawn controls that sit on a solid parent colour.
    abstract class Drawn : Control
    {
        protected bool Hover, Down;
        protected Drawn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            // Clicks are raised from OnMouseUp below so they behave the same everywhere.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            Font = Theme.Body;
        }
        protected Color ParentColor { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }
        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool wasDown = Down; Down = false; Invalidate();
            base.OnMouseUp(e);
            if (wasDown && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Enabled) { OnPressed(e.Location); }
        }
        protected virtual void OnPressed(Point p) { OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
        protected virtual void Fit() { }
        protected void Clear(Graphics g) { g.Clear(ParentColor); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; }
        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            var fr = RectangleF.Inflate(r, -1.5f, -1.5f);
            using (var path = Theme.Round(fr, radius))
            using (var pen = new Pen(Color.FromArgb(160, Theme.Accent), 2f)) g.DrawPath(pen, path);
        }
    }

    enum BtnStyle { Primary, Secondary, Danger, Ghost, Dark }

    class RoundButton : Drawn
    {
        public BtnStyle Kind;
        public bool Stretch;
        public RoundButton(string text, BtnStyle kind)
        {
            Kind = kind; Cursor = Cursors.Hand; TabStop = true;
            Font = kind == BtnStyle.Primary ? Theme.Bold : Theme.Body;
            Margin = new Padding(0, 0, Ui.Px(8), Ui.Px(8));
            Text = text;
        }
        protected override void Fit()
        {
            if (Stretch) { Height = Ui.Px(38); return; }
            var sz = TextRenderer.MeasureText(Text ?? "", Font);
            Size = new Size(sz.Width + Ui.Px(Kind == BtnStyle.Primary ? 40 : 30), Ui.Px(Kind == BtnStyle.Primary ? 40 : 36));
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, text, border;
            switch (Kind)
            {
                case BtnStyle.Primary:
                    fill = Down ? Theme.AccentHover : Hover ? Theme.AccentHover : Theme.Accent; text = Color.White; border = fill; break;
                case BtnStyle.Danger:
                    fill = Down ? Theme.FailSoft : Hover ? Theme.Blend(Color.White, Theme.FailSoft, .6f) : Color.White;
                    text = Theme.Fail; border = Hover ? Theme.Fail : Theme.LineStrong; break;
                case BtnStyle.Ghost:
                    fill = Down ? Theme.Chip : Hover ? Theme.Bg : ParentColor; text = Theme.Accent; border = fill; break;
                case BtnStyle.Dark:
                    fill = Down ? Theme.SideSel : Hover ? Theme.SideSel : Theme.SideHover; text = Theme.SideText; border = Hover ? Theme.SideLine : fill; break;
                default:
                    fill = Down ? Theme.Chip : Hover ? Theme.Bg : Color.White; text = Theme.Text; border = Hover ? Theme.Muted : Theme.LineStrong; break;
            }
            if (!Enabled)
            {
                fill = Kind == BtnStyle.Dark ? Theme.SideHover : Kind == BtnStyle.Primary ? Theme.Blend(Theme.Accent, Color.White, .55f) : Theme.Bg;
                text = Kind == BtnStyle.Dark ? Theme.SideMuted : Kind == BtnStyle.Primary ? Color.White : Theme.LineStrong;
                border = Kind == BtnStyle.Primary || Kind == BtnStyle.Dark ? fill : Theme.Line;
            }
            Theme.Fill(g, r, Ui.Px(8), fill, border);
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            FocusRing(g, r, Ui.Px(8));
        }
    }

    class LinkButton : Drawn
    {
        public Color Color = Theme.Accent;
        public LinkButton(string text) { Cursor = Cursors.Hand; TabStop = true; Margin = new Padding(Ui.Px(14), 0, 0, 0); Text = text; }
        protected override void Fit() { var s = TextRenderer.MeasureText(Text ?? "", Font); Size = new Size(s.Width + Ui.Px(4), s.Height + Ui.Px(6)); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            var c = !Enabled ? Theme.LineStrong : Down ? Theme.AccentHover : Color;
            var f = Hover && Enabled ? new Font(Font, FontStyle.Underline) : Font;
            TextRenderer.DrawText(g, Text, f, ClientRectangle, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (f != Font) f.Dispose();
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }
    }

    class Toggle : Drawn
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked { get { return on; } set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        public Toggle(string text) { Cursor = Cursors.Hand; TabStop = true; Text = text; Fit(); }
        protected override void Fit() { var s = TextRenderer.MeasureText(Text ?? "", Font); Size = new Size(Ui.Px(46) + s.Width + Ui.Px(4), Ui.Px(28)); }
        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            float h = Ui.Px(20), w = Ui.Px(36), y = (Height - h) / 2f;
            var track = new RectangleF(1, y, w, h);
            Color tc = on ? Theme.Accent : Theme.LineStrong;
            if (!Enabled) tc = Theme.Blend(tc, Color.White, .55f);
            else if (Hover) tc = on ? Theme.AccentHover : Theme.Muted;
            Theme.Fill(g, track, h / 2, tc);
            float k = h - Ui.Px(6);
            float kx = on ? track.Right - k - Ui.Px(3) : track.X + Ui.Px(3);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, y + Ui.Px(3), k, k);
            var tr = new Rectangle((int)(w + Ui.Px(10)), 0, Width - (int)(w + Ui.Px(10)), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.LineStrong, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) using (var path = Theme.Round(RectangleF.Inflate(track, 2, 2), h / 2 + 2)) using (var p = new Pen(Color.FromArgb(150, Theme.Accent), 2)) g.DrawPath(p, path);
        }
    }

    class Segmented : Drawn
    {
        string[] options = new string[0];
        int sel;
        int hoverIndex = -1;
        public event EventHandler SelectedIndexChanged;
        public Segmented(params string[] opts) { options = opts; TabStop = true; Cursor = Cursors.Hand; Fit(); }
        public int SelectedIndex
        {
            get { return sel; }
            set { value = Math.Max(0, Math.Min(options.Length - 1, value)); if (sel == value) return; sel = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        int[] Widths()
        {
            return options.Select(o => TextRenderer.MeasureText(o, Theme.Bold).Width + Ui.Px(28)).ToArray();
        }
        protected override void Fit() { if (options == null) return; Size = new Size(Widths().Sum() + Ui.Px(8), Ui.Px(36)); }
        int IndexAt(int x)
        {
            int acc = Ui.Px(4); var w = Widths();
            for (int i = 0; i < w.Length; i++) { if (x < acc + w[i]) return i; acc += w[i]; }
            return w.Length - 1;
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.X); if (i != hoverIndex) { hoverIndex = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }
        protected override void OnPressed(Point p) { SelectedIndex = IndexAt(p.X); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) SelectedIndex = sel - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = sel + 1;
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, outer, Ui.Px(9), Theme.Chip, Theme.Line);
            var w = Widths(); int x = Ui.Px(4);
            for (int i = 0; i < options.Length; i++)
            {
                var r = new RectangleF(x, Ui.Px(4), w[i], Height - Ui.Px(8) - 1);
                bool s = i == sel;
                if (s) Theme.Fill(g, r, Ui.Px(6), Color.White, Theme.Line);
                else if (i == hoverIndex && Enabled) Theme.Fill(g, r, Ui.Px(6), Theme.Blend(Theme.Chip, Color.White, .5f));
                Color c = !Enabled ? Theme.LineStrong : s ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, options[i], s ? Theme.Bold : Theme.Body, Rectangle.Round(r), c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += w[i];
            }
            FocusRing(g, outer, Ui.Px(9));
        }
    }

    class TabStrip : Drawn
    {
        readonly string[] tabs;
        int sel, hoverIndex = -1;
        public event EventHandler SelectedIndexChanged;
        public string[] Badges;
        public TabStrip(params string[] t) { tabs = t; Height = Ui.Px(44); TabStop = true; Cursor = Cursors.Hand; }
        public int SelectedIndex { get { return sel; } set { if (sel == value) return; sel = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); } }
        RectangleF[] Rects(Graphics g)
        {
            var res = new RectangleF[tabs.Length]; float x = 0;
            for (int i = 0; i < tabs.Length; i++)
            {
                float w = TextRenderer.MeasureText(Label(i), Theme.Bold).Width + Ui.Px(8);
                res[i] = new RectangleF(x, 0, w, Height); x += w + Ui.Px(24);
            }
            return res;
        }
        string Label(int i) { return Badges != null && i < Badges.Length && !string.IsNullOrEmpty(Badges[i]) ? tabs[i] + "  " + Badges[i] : tabs[i]; }
        int IndexAt(Point p) { using (var g = CreateGraphics()) { var r = Rects(g); for (int i = 0; i < r.Length; i++) if (r[i].Contains(p)) return i; } return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != hoverIndex) { hoverIndex = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }
        protected override void OnPressed(Point p) { int i = IndexAt(p); if (i >= 0) SelectedIndex = i; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && sel > 0) SelectedIndex = sel - 1;
            if (e.KeyCode == Keys.Right && sel < tabs.Length - 1) SelectedIndex = sel + 1;
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            var rs = Rects(g);
            for (int i = 0; i < tabs.Length; i++)
            {
                bool s = i == sel;
                var c = s ? Theme.Text : i == hoverIndex ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, Label(i), s ? Theme.Bold : Theme.Body, Rectangle.Round(rs[i]), c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (s) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, rs[i].X, Height - Ui.Px(3), rs[i].Width, Ui.Px(3));
            }
            if (Focused && ShowFocusCues && sel < rs.Length) ControlPaint.DrawFocusRectangle(g, Rectangle.Round(RectangleF.Inflate(rs[sel], 0, -Ui.Px(8))));
        }
    }

    class Pill : Drawn
    {
        public Color Fore = Theme.Muted, Back = Theme.Chip;
        public bool Dot;
        public Pill() { Font = Theme.SmallBold; }
        protected override void Fit() { var s = TextRenderer.MeasureText(Text ?? "", Font); Size = new Size(s.Width + Ui.Px(Dot ? 30 : 18), Ui.Px(24)); }
        public void Set(string text, Color fore, Color back) { Fore = fore; Back = back; Text = text; Fit(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, Height / 2f, Back);
            int x = Ui.Px(9);
            if (Dot)
            {
                float d = Ui.Px(7);
                using (var b = new SolidBrush(Fore)) g.FillEllipse(b, x, (Height - d) / 2f, d, d);
                x += Ui.Px(12);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), Fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // A surface with rounded corners; children get the surface colour.
    class Card : Panel
    {
        public Color Fill = Theme.Surface, Border = Theme.Line;
        public int Radius = 12;
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(Ui.Px(20));
            BackColor = Theme.Surface;   // children inherit the surface colour; corners are painted with the parent's
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.Fill(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Px(Radius), Fill, Border);
        }
    }

    class Note : Control
    {
        public Color Bar, Back;
        public Note(string text, Color bar, Color back)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text; Bar = bar; Back = back; Font = Theme.Body;
            Margin = new Padding(0, 0, 0, Ui.Px(10));
        }
        public void FitWidth(int w)
        {
            var s = TextRenderer.MeasureText(Text, Font, new Size(w - Ui.Px(36), int.MaxValue), TextFormatFlags.WordBreak);
            Size = new Size(w, s.Height + Ui.Px(22));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            Theme.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), Ui.Px(8), Back);
            using (var b = new SolidBrush(Bar)) g.FillRectangle(b, 0, Ui.Px(8), Ui.Px(3), Height - Ui.Px(16));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Ui.Px(18), Ui.Px(11), Width - Ui.Px(32), Height - Ui.Px(16)), Theme.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    // A label that wraps to a width you set.
    class Wrap : Label
    {
        public Wrap(string text, Font font, Color color)
        {
            Text = text; Font = font; ForeColor = color; AutoSize = true; Margin = new Padding(0); UseMnemonic = false;
        }
        public void FitWidth(int w)
        {
            // Measure explicitly: AutoSize with MaximumSize doesn't always re-wrap in time for layout.
            AutoSize = false;
            w = Math.Max(50, w);
            var s = TextRenderer.MeasureText(Text ?? "", Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size = new Size(Math.Min(w, s.Width + 2), Math.Max(s.Height, Font.Height));
        }
    }

    class Chip : Drawn
    {
        public Chip() { Font = Theme.Small; Margin = new Padding(0, 0, Ui.Px(6), 0); }
        protected override void Fit() { var s = TextRenderer.MeasureText(Text ?? "", Font); Size = new Size(s.Width + Ui.Px(14), Ui.Px(24)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Clear(g);
            Theme.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.Px(6), Theme.Chip);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Sidebar list of games.
    class GameList : ListBox
    {
        int hoverIndex = -1;
        public Func<GameEntry, Image> Art;
        public GameList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; BorderStyle = BorderStyle.None; BackColor = Theme.Side;
            IntegralHeight = false; ItemHeight = Ui.Px(62);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i != hoverIndex) { int old = hoverIndex; hoverIndex = i; InvalidateItem(old); InvalidateItem(i); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { int old = hoverIndex; hoverIndex = -1; InvalidateItem(old); base.OnMouseLeave(e); }
        void InvalidateItem(int i) { if (i >= 0 && i < Items.Count) Invalidate(GetItemRectangle(i)); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Theme.Side)) g.FillRectangle(bg, e.Bounds);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var game = (GameEntry)Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var r = new RectangleF(e.Bounds.X, e.Bounds.Y + Ui.Px(2), e.Bounds.Width - 1, e.Bounds.Height - Ui.Px(4));
            if (selected) Theme.Fill(g, r, Ui.Px(8), Theme.SideSel);
            else if (e.Index == hoverIndex) Theme.Fill(g, r, Ui.Px(8), Theme.SideHover);
            if (selected) using (var b = new SolidBrush(Theme.AccentOnDark)) g.FillRectangle(b, r.X, r.Y + Ui.Px(12), Ui.Px(3), r.Height - Ui.Px(24));

            Color sc; string st = game.StatusText(out sc);
            Color dot = sc == Theme.Accent ? Theme.AccentOnDark : sc == Theme.Muted ? Theme.SideMuted : sc;
            var art = new RectangleF(r.X + Ui.Px(12), r.Y + (r.Height - Ui.Px(48)) / 2f, Ui.Px(34), Ui.Px(48));
            Theme.Cover(g, Art != null ? Art(game) : null, art, Ui.Px(5), Theme.SideHover);
            int x = (int)art.Right + Ui.Px(12), right = (int)r.Right - Ui.Px(12);
            var nameRect = new Rectangle(x, (int)r.Y + Ui.Px(10), right - x - Ui.Px(14), Ui.Px(22));
            TextRenderer.DrawText(g, game.Name, Theme.Bold, nameRect, Theme.SideText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            float d = Ui.Px(8);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, right - d, nameRect.Y + (nameRect.Height - d) / 2f, d, d);
            string sub = game.ApiLabel + ",  " + game.Bits + "  \u2014  " + st;
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(x, nameRect.Bottom, right - x, Ui.Px(20)), Theme.SideMuted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        }
    }

    // A rounded image, cropped to fill, with a placeholder while it loads.
    class ThumbBox : Control
    {
        Image img; bool failed;
        public ThumbBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SetImage(Image i) { img = i; failed = i == null; Invalidate(); }   // images are cached elsewhere; not disposed here
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Ui.Px(8)))
            {
                if (img == null)
                {
                    using (var b = new SolidBrush(Theme.Chip)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, failed ? "No image" : "Loading...", Theme.Small, ClientRectangle, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }
                Theme.Cover(g, img, r, Ui.Px(8), Theme.Chip);
                using (var pen = new Pen(Color.FromArgb(30, 0, 0, 0))) g.DrawPath(pen, path);
            }
        }
    }

    static class Logo
    {
        static Image img;
        // The app logo, embedded in the exe as logo.png.
        public static Image Image
        {
            get
            {
                if (img != null) return img;
                try
                {
                    using (var st = typeof(Logo).Assembly.GetManifestResourceStream("logo.png"))
                        if (st != null) img = new Bitmap(System.Drawing.Image.FromStream(st));
                }
                catch { }
                return img;
            }
        }
        public static void Draw(Graphics g, RectangleF r)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (Image != null) g.DrawImage(Image, r);
            else Theme.Fill(g, r, r.Width * .22f, Theme.Accent);
        }
    }

    class LogoBox : Control
    {
        public LogoBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Logo.Draw(e.Graphics, new RectangleF(0, 0, Width, Height));
        }
    }

    class Brand : Control
    {
        public Brand()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.Px(56); BackColor = Theme.Side;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Ui.Px(38), y = (Height - s) / 2f;
            Logo.Draw(g, new RectangleF(0, y, s, s));
            int tx = (int)s + Ui.Px(12);
            TextRenderer.DrawText(g, "Another DLSS Tool", Theme.Brand, new Rectangle(tx, (int)y, Width - tx, Ui.Px(22)), Theme.SideText, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "DLSS 5 for Direct3D 9, 11 and 12", Theme.Small, new Rectangle(tx, (int)y + Ui.Px(21), Width - tx, Ui.Px(18)), Theme.SideMuted, TextFormatFlags.NoPrefix);
        }
    }
}
