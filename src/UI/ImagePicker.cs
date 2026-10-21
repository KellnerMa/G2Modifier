using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>Zeigt ein RgbaImage auf Schachbrett (Transparenz sichtbar), passend skaliert.</summary>
    public class PortraitBox : Control
    {
        Bitmap bmp;
        public string EmptyText = "";

        public PortraitBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(44, 46, 58);
        }

        public void SetImage(RgbaImage img)
        {
            if (bmp != null) bmp.Dispose();
            bmp = img == null ? null : img.ToBitmap();
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing && bmp != null) bmp.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (bmp == null)
            {
                TextRenderer.DrawText(g, EmptyText, Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            float scale = Math.Min((float)Width / bmp.Width, (float)Height / bmp.Height);
            if (scale >= 1f) scale = (float)Math.Floor(scale);   // ganzzahlig vergroessern, Pixel bleiben scharf
            int w = (int)(bmp.Width * scale), h = (int)(bmp.Height * scale);
            var r = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
            using (var a = new SolidBrush(Color.FromArgb(60, 62, 76)))
                for (int y = r.Top; y < r.Bottom; y += 8)
                    for (int x = r.Left + ((y - r.Top) / 8 % 2) * 8; x < r.Right; x += 16)
                        g.FillRectangle(a, x, y, Math.Min(8, r.Right - x), Math.Min(8, r.Bottom - y));
            g.InterpolationMode = scale >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, r);
        }
    }

    /// <summary>Bildauswahl: alle Portraets des Spiels (aus dem Original) oder eine eigene Datei.</summary>
    public class ImagePicker : Form
    {
        readonly List<GalleryItem> items;
        readonly Func<RgbaImage, RgbaImage> compose;
        readonly ComboBox filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        readonly ListView view = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(32, 34, 44), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
        readonly ImageList thumbs = new ImageList { ImageSize = new Size(96, 96), ColorDepth = ColorDepth.Depth32Bit };
        readonly PortraitBox preview = new PortraitBox { Size = new Size(256, 256) };
        readonly Label previewInfo = new Label { AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = Theme.Muted };
        readonly Button ok = Theme.Primary(L.T("Übernehmen"));
        GalleryItem own;

        public RgbaImage Chosen { get; private set; }
        public string ChosenLabel { get; private set; }

        /// <param name="compose">Setzt eine Quelle so zusammen, wie sie im Spiel landet (fuer die Vorschau).</param>
        public ImagePicker(List<GalleryItem> gallery, string title, string targetInfo, Func<RgbaImage, RgbaImage> compose, int preferChar)
        {
            items = gallery; this.compose = compose;
            Text = L.C(L.T("Bild wählen")) + title;
            Width = 1040; Height = 680; StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; ShowIcon = false;
            Font = Theme.Body; BackColor = Theme.Background;
            preview.EmptyText = L.T("Links ein Bild wählen");

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 10, 10, 0), WrapContents = false };
            top.Controls.Add(new Label { Text = L.T("Figur:"), AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            filter.Items.Add(L.T("Alle"));
            for (int c = 1; c <= 7; c++) filter.Items.Add(Avatars.CharName(c));
            filter.Items.Add(L.T("Sonstige"));
            top.Controls.Add(filter);
            var file = Theme.Secondary(L.T("Eigene Datei (PNG, JPG, BMP) ..."));
            file.Margin = new Padding(10, 0, 0, 0);
            file.Click += (s, e) => PickFile();
            top.Controls.Add(file);
            top.Controls.Add(new Label { Text = L.T("Quellen kommen aus dem Backup, eigene Änderungen werden also nicht weitergereicht."), AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 6, 0, 0) });

            view.LargeImageList = thumbs;
            view.ShowItemToolTips = true;
            view.SelectedIndexChanged += (s, e) => OnSelect();
            view.DoubleClick += (s, e) => { if (Chosen != null) { DialogResult = DialogResult.OK; Close(); } };

            var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 290, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 10, 10, 10) };
            right.Controls.Add(Theme.Strong(L.T("So landet es im Spiel")));
            right.Controls.Add(preview);
            right.Controls.Add(previewInfo);
            previewInfo.Text = targetInfo + "\r\n" + L.T("Passt das Seitenverhältnis nicht, wird mittig beschnitten.");

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            var cancel = Theme.Secondary(L.T("Abbrechen"));
            cancel.DialogResult = DialogResult.Cancel;
            ok.DialogResult = DialogResult.OK; ok.Enabled = false;
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            Controls.Add(view); Controls.Add(right); Controls.Add(bottom); Controls.Add(top);

            filter.SelectedIndexChanged += (s, e) => Fill();
            filter.SelectedIndex = preferChar >= 1 && preferChar <= 7 ? preferChar : 0;
        }

        void Fill()
        {
            view.BeginUpdate();
            view.Items.Clear();
            foreach (Image i in thumbs.Images) i.Dispose();
            thumbs.Images.Clear();
            int f = filter.SelectedIndex;
            var list = new List<GalleryItem>();
            if (own != null) list.Add(own);
            list.AddRange(items.Where(x => f == 0 || (f == 8 ? (x.CharId < 1 || x.CharId > 7) : x.CharId == f)));
            foreach (var it in list)
            {
                thumbs.Images.Add(Thumb(it.Image));
                var lvi = new ListViewItem((it == own ? "" : Avatars.CharName(it.CharId) + " · ") + it.Label, thumbs.Images.Count - 1) { Tag = it, ToolTipText = it.Source + "  (" + it.Image.W + "×" + it.Image.H + ")" };
                view.Items.Add(lvi);
            }
            view.EndUpdate();
            if (own != null && view.Items.Count > 0) view.Items[0].Selected = true;
        }

        static Bitmap Thumb(RgbaImage img)
        {
            var b = new Bitmap(96, 96);
            using (var g = Graphics.FromImage(b))
            using (var src = img.ToBitmap())
            {
                g.Clear(Color.FromArgb(44, 46, 58));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float s = Math.Min(96f / src.Width, 96f / src.Height);
                int w = (int)(src.Width * s), h = (int)(src.Height * s);
                g.DrawImage(src, (96 - w) / 2, (96 - h) / 2, w, h);
            }
            return b;
        }

        void OnSelect()
        {
            if (view.SelectedItems.Count == 0) return;
            var it = (GalleryItem)view.SelectedItems[0].Tag;
            Chosen = it.Image;
            ChosenLabel = it == own ? it.Label : Avatars.CharName(it.CharId) + " · " + it.Label;
            try { preview.SetImage(compose(it.Image)); ok.Enabled = true; }
            catch (Exception ex) { preview.SetImage(null); preview.EmptyText = ex.Message; ok.Enabled = false; }
        }

        void PickFile()
        {
            using (var d = new OpenFileDialog { Filter = L.T("Bilder") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" + L.T("Alle Dateien") + " (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var b = new Bitmap(d.FileName))
                    {
                        if (b.Width < 16 || b.Height < 16) throw new IOException(L.F("Bild ist zu klein ({0}×{1}).", b.Width, b.Height));
                        own = new GalleryItem { Label = L.C(L.T("Eigene Datei")) + Path.GetFileName(d.FileName), Source = d.FileName, Image = RgbaImage.FromBitmap(b) };
                    }
                    Fill();
                }
                catch (Exception ex) { MessageBox.Show(this, L.T("Bild konnte nicht gelesen werden:") + "\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}
