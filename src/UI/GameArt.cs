using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>
    /// Grafiken direkt aus dem installierten Spiel (nur gelesen, nichts wird weitergegeben):
    /// title_data\f_win_bak.dds = Hintergrund der Menuefenster (128 x 128, kachelbar),
    /// title_data\f_icon_32.dds = Symbolatlas; die Elementsymbole liegen in Zeile y=156 im 52-Pixel-Raster.
    /// Fehlt eine Datei, zeichnet das Tool die einfarbige Ersatzflaeche.
    /// </summary>
    public static class GameArt
    {
        static Bitmap tile;
        static RgbaImage atlas;
        static readonly Dictionary<int, Bitmap> elements = new Dictionary<int, Bitmap>();

        public static bool HasTile { get { return tile != null; } }

        static Workspace art;
        static readonly Dictionary<string, KeyValuePair<byte[], Bitmap>> portraits = new Dictionary<string, KeyValuePair<byte[], Bitmap>>();

        public static void Load(Workspace ws)
        {
            art = ws;
            portraits.Clear();
            elements.Clear();
            status.Clear();
            tile = null; atlas = null;
            try
            {
                string dir = Workspace.FindDirIgnoreCase(ws.AfsRoot, "title_data");
                string bak = Workspace.FindFileIgnoreCase(dir, "f_win_bak.dds");
                if (bak != null)
                {
                    var img = Dds.Read(File.ReadAllBytes(bak));
                    // abdunkeln, damit weisse Schrift gut lesbar bleibt
                    for (int i = 0; i < img.Px.Length; i += 4)
                    {
                        img.Px[i] = (byte)(img.Px[i] * 0.55 + 14 * 0.45);
                        img.Px[i + 1] = (byte)(img.Px[i + 1] * 0.55 + 18 * 0.45);
                        img.Px[i + 2] = (byte)(img.Px[i + 2] * 0.55 + 40 * 0.45);
                        img.Px[i + 3] = 255;
                    }
                    tile = img.ToBitmap();
                }
            }
            catch { tile = null; }
            try
            {
                string ic = Workspace.FindFileIgnoreCase(Workspace.FindDirIgnoreCase(ws.AfsRoot, "title_data"), "f_icon_32.dds");
                if (ic != null) atlas = Dds.Read(File.ReadAllBytes(ic));
            }
            catch { atlas = null; }
            LoadLogo(ws);
        }

        // ------------------------------------------------------------ Logo und Fenstersymbol aus dem Titelbild

        public static Bitmap Logo { get; private set; }
        public static Icon AppIcon { get; private set; }

        /// <summary>
        /// Titelbild des Spiels: HD Remaster title_data\gr2title_us_hd.dds (1920 x 1080, RGBA),
        /// Anniversary Edition title_data\GR2TITLE_US_HD.dds bzw. GR2TITLE_US.dds (2160 x 1080, DXT1, Bild links 1350 Pixel breit).
        /// Ausschnitte (Logo, Schild fuer das Fenstersymbol) am Bild abgemessen. Andere Groessen: ganzes Bild bzw. Mitte.
        /// </summary>
        static void LoadLogo(Workspace ws)
        {
            Logo = null; AppIcon = null;
            try
            {
                string dir = Workspace.FindDirIgnoreCase(ws.AfsRoot, "title_data");
                string p = Workspace.FindFileIgnoreCase(dir, "gr2title_us_hd.dds") ?? Workspace.FindFileIgnoreCase(dir, "gr2title_us.dds");
                if (p == null) return;
                var img = Dds.Read(File.ReadAllBytes(p));
                Rectangle logo, shield;
                if (img.W == 2160 && img.H == 1080) { logo = new Rectangle(160, 0, 1030, 610); shield = new Rectangle(570, 335, 210, 210); }
                else if (img.W == 1920 && img.H == 1080) { logo = new Rectangle(445, 0, 1035, 615); shield = new Rectangle(852, 345, 210, 210); }
                else { logo = new Rectangle(0, 0, img.W, img.H); int s = Math.Min(img.W, img.H); shield = new Rectangle((img.W - s) / 2, (img.H - s) / 2, s, s); }
                int lw = 212, lh = logo.Height * lw / logo.Width;
                Logo = img.Resize(logo, lw, lh).ToBitmap();
                using (var ib = img.Resize(shield, 64, 64).ToBitmap()) AppIcon = Icon.FromHandle(ib.GetHicon());
            }
            catch { Logo = null; AppIcon = null; }
        }

        /// <summary>Rastergroesse im Symbolatlas: HD 512 x 512 mit 52er Raster, Anniversary Edition 256 x 256 mit 26er Raster (gleiche Anordnung).</summary>
        static int Cell { get { return atlas == null ? 0 : atlas.W >= 468 ? 52 : atlas.W >= 234 ? 26 : 0; } }

        /// <summary>Zeichnet den Spielhintergrund nahtlos (Kachelung am Bildschirm ausgerichtet).</summary>
        public static void PaintSidebar(Graphics g, Control c)
        {
            if (tile == null || !c.IsHandleCreated) { g.Clear(Theme.Sidebar); return; }
            var p = c.PointToScreen(Point.Empty);
            using (var b = new TextureBrush(tile, WrapMode.Tile))
            {
                b.TranslateTransform(-(((p.X % tile.Width) + tile.Width) % tile.Width), -(((p.Y % tile.Height) + tile.Height) % tile.Height));
                g.FillRectangle(b, c.ClientRectangle);
            }
        }

        /// <summary>Elementsymbol aus dem Spiel: 0 Feuer, 1 Wind, 2 Erde, 3 Blitz, 4 Blizzard, 5 Wasser, 6 Explosion, 7 Wald.</summary>
        public static Bitmap Element(int i)
        {
            int c = Cell;
            if (c == 0 || i < 0 || i > 7 || atlas.W < 9 * c || atlas.H < 4 * c) return null;
            Bitmap b;
            if (elements.TryGetValue(i, out b)) return b;
            using (var full = atlas.Crop(new Rectangle(c * (i + 1), 3 * c, c, c)).ToBitmap())
                b = new Bitmap(full, 28, 28);
            elements[i] = b;
            return b;
        }
    

        /// <summary>
        /// Menue-Portraet der Figur (title_data\f0X01.dds, aktueller Arbeitsstand, also mit eigenen Bildern).
        /// charId 1 Ryudo ... 7 Prinz Roan. null, wenn die Datei fehlt oder nicht lesbar ist.
        /// </summary>
        public static Bitmap Portrait(int charId, int size)
        {
            if (art == null || charId < 1 || charId > 7) return null;
            string tail = "\\title_data\\f0" + charId + "01.dds";
            GameFile f = null;
            foreach (var x in art.AllFiles)
                if (x.FormatKey == "DDS" && x.RelPath.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) { f = x; break; }
            if (f == null || !f.Valid) return null;
            string key = charId + "/" + size;
            KeyValuePair<byte[], Bitmap> hit;
            if (portraits.TryGetValue(key, out hit) && hit.Key == f.Data) return hit.Value;
            try
            {
                var img = Dds.Read(f.Data);
                var bmp = img.Resize(new RectangleF(0, 0, img.W, img.H), size, size).ToBitmap();
                portraits[key] = new KeyValuePair<byte[], Bitmap>(f.Data, bmp);
                return bmp;
            }
            catch { return null; }
        }

        static readonly Dictionary<int, Bitmap> status = new Dictionary<int, Bitmap>();
        // Zustandssymbole im Atlas: Zeile y=312, 52er Raster. Zuordnung nach Bildinhalt (Pilz = Gift, Blasen = Schlaf,
        // Blitz-Totenkopf = Lähmung, Spirale = Verwirrung, Totenkopf grün = Seuche, Faust durchgestrichen = Bewegungsblock,
        // Blitz durchgestrichen = Magieblock, Flügel mit Heiligenschein = Tod). Index = Bit des Zustandsfelds.
        static readonly int[] StatusCell = { 0, 1, 2, 3, 4, 6, 5, 7 };

        /// <summary>Zustandssymbol aus dem Spiel: 0 Gift, 1 Schlaf, 2 Lähmung, 3 Verwirrung, 4 Seuche, 5 Magieblock, 6 Bewegungsblock, 7 Tod.</summary>
        public static Bitmap Status(int bit)
        {
            int c = Cell;
            if (c == 0 || bit < 0 || bit > 7 || atlas.W < 8 * c || atlas.H < 7 * c) return null;
            Bitmap b;
            if (status.TryGetValue(bit, out b)) return b;
            using (var full = atlas.Crop(new Rectangle(c * StatusCell[bit], 6 * c, c, c)).ToBitmap())
                b = new Bitmap(full, 20, 20);
            status[bit] = b;
            return b;
        }

        /// <summary>Symbol fuer das Icon-Feld der Moves (G2DataGUI: 1 Feuer ... 8 Wald = Reihenfolge im Atlas).</summary>
        public static Bitmap MoveIcon(long icon) { return icon >= 1 && icon <= 8 ? Element((int)icon - 1) : null; }
    }
}
