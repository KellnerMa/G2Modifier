using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace G2Modifier.UI
{
    /// <summary>
    /// Ein Design-System fuer das ganze Tool: drei Grundfarben (Akzent, Flaeche, Text) plus Statusfarben,
    /// zwei Schriftgroessen, ein Knopfstil. Alle Seiten bauen ihre Bedienelemente nur ueber diese Fabriken.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(219, 234, 254);
        public static readonly Color Background = Color.FromArgb(244, 245, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(223, 226, 232);
        public static readonly Color Text = Color.FromArgb(24, 28, 38);
        public static readonly Color Muted = Color.FromArgb(100, 108, 122);
        public static readonly Color Sidebar = Color.FromArgb(22, 27, 38);
        public static readonly Color SidebarHover = Color.FromArgb(36, 43, 58);
        public static readonly Color SidebarText = Color.FromArgb(203, 210, 222);
        public static readonly Color Success = Color.FromArgb(22, 128, 82);
        public static readonly Color Warning = Color.FromArgb(180, 95, 6);
        public static readonly Color WarningSoft = Color.FromArgb(255, 245, 225);
        public static readonly Color Danger = Color.FromArgb(185, 28, 28);
        public static readonly Color Changed = Color.FromArgb(37, 99, 235);

        static Font body, bodyBold, title, small;
        static string Family
        {
            get
            {
                foreach (var f in FontFamily.Families) if (f.Name == "Segoe UI") return "Segoe UI";
                return SystemFonts.MessageBoxFont.FontFamily.Name;
            }
        }
        public static Font Body { get { return body ?? (body = new Font(Family, 9.75f)); } }
        public static Font BodyBold { get { return bodyBold ?? (bodyBold = new Font(Family, 9.75f, FontStyle.Bold)); } }
        public static Font Title { get { return title ?? (title = new Font(Family, 15f, FontStyle.Bold)); } }
        public static Font Small { get { return small ?? (small = new Font(Family, 8.25f)); } }

        public static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = Title, ForeColor = Text, Margin = new Padding(0, 0, 0, 4) };
        }

        public static Label Hint(string text, int maxWidth)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(maxWidth, 0), Font = Body, ForeColor = Muted, Margin = new Padding(0, 0, 0, 10) };
        }

        public static Label Strong(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = BodyBold, ForeColor = Text, Margin = new Padding(0, 8, 0, 2) };
        }

        /// <summary>Hauptaktion: gefuellt in Akzentfarbe. Pro Bildschirm hoechstens eine.</summary>
        public static Button Primary(string text)
        {
            var b = Base(text);
            b.BackColor = Accent; b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentHover;
            return b;
        }

        /// <summary>Nebenaktion: weiss mit Rahmen.</summary>
        public static Button Secondary(string text)
        {
            var b = Base(text);
            b.BackColor = Surface; b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Background;
            b.FlatAppearance.MouseDownBackColor = Border;
            return b;
        }

        static Button Base(string text)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Font = Body, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 32, Padding = new Padding(10, 2, 10, 2), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        public static LinkLabel Link(string text)
        {
            return new LinkLabel { Text = text, AutoSize = true, Font = Body, LinkColor = Accent, ActiveLinkColor = AccentHover, LinkBehavior = LinkBehavior.HoverUnderline };
        }

        /// <summary>Weisse Karte mit duennem Rahmen.</summary>
        public static Panel Card()
        {
            var p = new CardPanel { BackColor = Surface, Padding = new Padding(14) };
            return p;
        }

        public static void Style(Control root)
        {
            root.Font = Body;
            root.ForeColor = Text;
        }
    }

    /// <summary>Panel mit 1px-Rahmen in Theme.Border, ohne Flackern.</summary>
    public class CardPanel : Panel
    {
        public CardPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Kachel auf der Startseite: Titel, Beschreibung, Kennzahl. Ganze Flaeche klickbar.</summary>
    public class Tile : Control
    {
        public string Caption, Description, Figure;
        bool hover;
        public Tile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Size = new Size(236, 112); Margin = new Padding(0, 0, 12, 12); Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Round(r, 8))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(hover ? Theme.Accent : Theme.Border, hover ? 1.5f : 1f))
            {
                g.FillPath(fill, path); g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Caption, Theme.BodyBold, new Rectangle(14, 12, Width - 28, 22), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(14, 36, Width - 28, 44), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Figure))
                TextRenderer.DrawText(g, Figure, Theme.Small, new Rectangle(14, Height - 26, Width - 28, 18), hover ? Theme.Accent : Theme.Muted, TextFormatFlags.Left);
        }
        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    /// <summary>Eintrag in der Seitenleiste.</summary>
    public class NavItem : Control
    {
        public bool Active;
        public bool Marked;   // ungespeicherte Aenderungen auf dieser Seite
        bool hover;
        public NavItem(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 32; Cursor = Cursors.Hand; Font = Theme.Body;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            GameArt.PaintSidebar(g, this);
            if (Active || hover) using (var b = new SolidBrush(Active ? Color.FromArgb(150, 10, 14, 40) : Color.FromArgb(90, 10, 14, 40))) g.FillRectangle(b, ClientRectangle);
            if (Active) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 6, 3, Height - 12);
            TextRenderer.DrawText(g, Text, Active ? Theme.BodyBold : Theme.Body, new Rectangle(20, 0, Width - 40, Height), Active ? Color.White : Theme.SidebarText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (Marked) using (var b = new SolidBrush(Color.FromArgb(96, 165, 250))) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(b, Width - 22, Height / 2 - 4, 8, 8); }
        }
    }

    /// <summary>Flaeche der Seitenleiste: Fensterhintergrund aus dem Spiel (title_data\\f_win_bak.dds), nahtlos gekachelt.</summary>
    public class SidebarPanel : Panel
    {
        public SidebarPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e) { GameArt.PaintSidebar(e.Graphics, this); }
    }

    public class SidebarFlow : FlowLayoutPanel
    {
        public SidebarFlow() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e) { GameArt.PaintSidebar(e.Graphics, this); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
    }

    /// <summary>Gruppenueberschrift in der Seitenleiste.</summary>
    public class NavHeader : Control
    {
        public NavHeader(string text)
        {
            Text = text.ToUpperInvariant();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Height = 28; Font = Theme.Small;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            GameArt.PaintSidebar(e.Graphics, this);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(20, 10, Width - 24, 18), Color.FromArgb(170, 185, 215), TextFormatFlags.Left);
        }
    }
}
