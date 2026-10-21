using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace G2Modifier.Core
{
    public enum SlotKind { Menu, Dialog, BattleSmall, BattleBig, Victory }

    /// <summary>
    /// Ein Bildplatz im Spiel, der aus einer oder mehreren DDS-Dateien besteht (z. B. alle Kopien des Dialoggesichts).
    /// Beim Setzen eines Bildes wird jede Datei einzeln aus ihrem Original zusammengesetzt:
    /// Inhalt ersetzen, Rahmen, Rand und Fuellflaeche bleiben wie im Original.
    /// </summary>
    public class AvatarSlot
    {
        public SlotKind Kind;
        public int CharId;               // 0 bei den Rage-Plaetzen (gelten fuer die Rage-Figur)
        public string Title, Hint;
        public bool Framed;              // Achteck-Rahmen erhalten
        public List<GameFile> Files = new List<GameFile>();

        public GameFile First { get { return Files[0]; } }
        public bool Usable { get { return Files.Count > 0 && Files.All(f => f.Valid); } }
        public bool Dirty { get { return Files.Any(f => f.Dirty); } }
        public bool Modified { get { return Files.Any(f => f.Original != null && !Bin.Equal(f.Data, f.Original)); } }

        public string SizeText
        {
            get
            {
                var sizes = Files.Select(f => { var s = Dds.SizeOf(f.Data); return s.Width + "×" + s.Height; }).Distinct().ToArray();
                return (Files.Count == 1 ? L.T("1 Datei") : L.F("{0} Dateien", Files.Count)) + ", " + string.Join(" + ", sizes);
            }
        }

        public RgbaImage Current() { return Dds.Read(First.Data); }
        public static RgbaImage OriginalOf(GameFile f) { return Dds.Read(f.Original ?? f.Disk); }

        /// <summary>Setzt das Bild in alle Dateien des Platzes (nur im Speicher).</summary>
        public void Apply(RgbaImage src)
        {
            foreach (var f in Files)
            {
                var img = Avatars.Compose(OriginalOf(f), src, Framed);
                var data = (byte[])f.Data.Clone();
                Dds.WritePixels(data, img);
                f.Data = data;
            }
        }

        public RgbaImage Preview(RgbaImage src) { return Avatars.Compose(OriginalOf(First), src, Framed); }

        /// <summary>Zurueck auf den Stand des Backups (oder der Platte, solange kein Backup existiert).</summary>
        public void Reset()
        {
            foreach (var f in Files) f.Data = (byte[])(f.Original ?? f.Disk).Clone();
        }
    }

    /// <summary>Ein Bild, das in der Bildauswahl angeboten wird. Bereits auf den Inhaltsbereich zugeschnitten.</summary>
    public class GalleryItem
    {
        public string Label;
        public int CharId;               // 0 = keine Zuordnung
        public string Source;            // relativer Pfad oder Dateiname
        public RgbaImage Image;
    }

    public static class Avatars
    {
        public static readonly string[] CharNames = { "", "Ryudo", "Elena", "Millenia", "Roan", "Tio", "Mareg", "Prinz Roan" };
        public static string CharName(int id) { return id >= 1 && id < CharNames.Length ? L.T(CharNames[id]) : L.T("Sonstige"); }

        public const string AngFace = "0_2_2_face3s.pvr.dds";

        // ------------------------------------------------------------ Bildzusammensetzung

        /// <summary>
        /// Rahmenmaske: alles, was hoechstens Breite/16 Pixel (128er: 8, 64er: 4) von einem transparenten Pixel entfernt ist.
        /// Gemessen an btl_sys1_0..6 und den beiden c03ang-Gesichtern: Rand transparent, danach Rahmen genau dieser Breite.
        /// </summary>
        public static bool[] FrameMask(RgbaImage img)
        {
            int w = img.W, h = img.H, d = Math.Max(1, w / 16), d2 = d * d;
            // Nur Transparenz, die mit dem Bildrand verbunden ist, zaehlt als Aussenrand.
            // Transparente Stellen im Gesicht (z. B. Hintergrund bei Ryudo) duerfen nicht als Rahmen gelten.
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (outside[i] || img.Px[i * 4 + 3] != 0) continue;
                outside[i] = true;
                int x = i % w, y = i / w;
                if (x > 0) stack.Push(i - 1); if (x < w - 1) stack.Push(i + 1);
                if (y > 0) stack.Push(i - w); if (y < h - 1) stack.Push(i + w);
            }
            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool hit = false;
                    for (int dy = -d; dy <= d && !hit; dy++)
                        for (int dx = -d; dx <= d && !hit; dx++)
                        {
                            if (dx * dx + dy * dy > d2) continue;
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h || outside[yy * w + xx]) hit = true;
                        }
                    mask[y * w + x] = hit;
                }
            return mask;
        }

        /// <summary>
        /// Inhaltsbereich ohne Rahmen: Rechteck um alle Nicht-Rahmen-Pixel.
        /// Ohne Rahmen: Rechteck um alle Pixel, die sich von der Fuellfarbe (Pixel unten rechts) unterscheiden.
        /// title_data: 0..191 von 256, f_face_g: 0..95 von 128, grosse Kampfportraets: ganzes Bild.
        /// </summary>
        public static Rectangle ContentRect(RgbaImage img, bool[] mask)
        {
            int w = img.W, h = img.H, x0 = w, y0 = h, x1 = -1, y1 = -1;
            int fill = ((h - 1) * w + (w - 1)) * 4;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool content;
                    if (mask != null) content = !mask[i];
                    else
                    {
                        int k = i * 4;
                        bool bothClear = img.Px[k + 3] == 0 && img.Px[fill + 3] == 0;
                        content = !bothClear && (img.Px[k] != img.Px[fill] || img.Px[k + 1] != img.Px[fill + 1] || img.Px[k + 2] != img.Px[fill + 2] || img.Px[k + 3] != img.Px[fill + 3]);
                    }
                    if (!content) continue;
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            if (x1 < 0) return new Rectangle(0, 0, w, h);
            var r = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            // Fast das ganze Bild: dann ist das Bild selbst der Inhalt (Fuellfarbe war nur zufaellig ein Bildpixel)
            if (mask == null && r.Width >= w * 9 / 10 && r.Height >= h * 9 / 10) return new Rectangle(0, 0, w, h);
            // Inhalt beginnt immer oben links: auf Quadrat ab (0,0) erweitern, wenn die Fuellflaeche rechts/unten liegt
            if (mask == null && x0 <= 2 && y0 <= 2) { int s = Math.Max(r.Right, r.Bottom); r = new Rectangle(0, 0, Math.Min(s, w), Math.Min(s, h)); }
            return r;
        }

        /// <summary>Quelle mittig auf das Seitenverhaeltnis des Ziels zuschneiden (fuellend), skalieren, einsetzen.</summary>
        public static RgbaImage Compose(RgbaImage original, RgbaImage src, bool framed)
        {
            var mask = framed ? FrameMask(original) : null;
            var box = ContentRect(original, mask);
            double ta = (double)box.Width / box.Height, sa = (double)src.W / src.H;
            RectangleF crop;
            if (sa > ta) { float cw = (float)(src.H * ta); crop = new RectangleF((src.W - cw) / 2f, 0, cw, src.H); }
            else { float ch = (float)(src.W / ta); crop = new RectangleF(0, (src.H - ch) / 2f, src.W, ch); }
            var scaled = src.Resize(crop, box.Width, box.Height);
            var o = original.Clone();
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    int ox = box.X + x, oy = box.Y + y, i = oy * o.W + ox;
                    if (mask != null && mask[i]) continue;
                    Buffer.BlockCopy(scaled.Px, (y * box.Width + x) * 4, o.Px, i * 4, 4);
                }
            return o;
        }

        /// <summary>Inhaltsbereich eines Bildes als Quelle fuer die Bildauswahl.</summary>
        public static RgbaImage ExtractContent(RgbaImage img, bool framed)
        {
            return img.Crop(ContentRect(img, framed ? FrameMask(img) : null));
        }

        // ------------------------------------------------------------ Plaetze

        public static List<AvatarSlot> BuildSlots(Workspace ws)
        {
            var res = new List<AvatarSlot>();
            var dds = ws.AllFiles.Where(f => f.FormatKey == "DDS").ToList();
            string afsRel = Workspace.MakeRelative(ws.GameRoot, ws.AfsRoot);

            for (int c = 1; c <= 7; c++)
            {
                string fx = "f0" + c + "01.dds";
                var menu = new AvatarSlot { Kind = SlotKind.Menu, CharId = c, Title = L.T("Menü"),
                    Hint = L.T("title_data. Dass das Spiel diese Bilder im Hauptmenü zeigt, ist aus Größe und Ordner abgeleitet, nicht im Spiel geprüft.") };
                menu.Files.AddRange(dds.Where(f => InFolder(f, afsRel, "title_data") && NameIs(f, fx)));
                menu.Files.AddRange(dds.Where(f => InFolder(f, afsRel, "title_data\\face_99") && NameIs(f, fx)));
                res.Add(menu);

                var dialog = new AvatarSlot { Kind = SlotKind.Dialog, CharId = c, Title = L.T("Dialog (neutral)"),
                    Hint = L.T("Neutrales Gesicht in Textboxen. Alle Kopien in f_face_g werden gemeinsam geändert.") };
                dialog.Files.AddRange(dds.Where(f => f.RelPath.IndexOf("\\f_face_g\\", StringComparison.OrdinalIgnoreCase) >= 0 && NameIs(f, fx)));
                res.Add(dialog);

                var small = new AvatarSlot { Kind = SlotKind.BattleSmall, CharId = c, Title = L.T("Kampf"), Framed = true,
                    Hint = L.F("Kampfporträt mit Rahmen (btl_sys1_{0}). Während der Rage zeigt das Spiel an dieser Stelle das Rage-Gesicht.", c - 1) };
                small.Files.AddRange(dds.Where(f => NameIs(f, "btl_sys1_" + (c - 1) + ".dds")));
                res.Add(small);

                var big = new AvatarSlot { Kind = SlotKind.BattleBig, CharId = c, Title = L.T("Kampf groß"),
                    Hint = L.F("btl_sys1_{0}, ohne Rahmen. Wo das Spiel es zeigt, ist nicht belegt.", c + 6) };
                big.Files.AddRange(dds.Where(f => NameIs(f, "btl_sys1_" + (c + 6) + ".dds")));
                res.Add(big);

                // Siegbildschirm: sys\result_N.dds, lachende Gesichter im Ergebnisfenster (Level-up).
                // Reihenfolge laut Bildern und global_indices.csv: Millenia und Elena sind vertauscht.
                var win = new AvatarSlot { Kind = SlotKind.Victory, CharId = c, Title = L.T("Sieg und Level-up"),
                    Hint = L.F("sys\\result_{0}.dds. Lachendes Gesicht im Ergebnisfenster nach dem Kampf, z. B. beim Level-up.", ResultIndex(c)) };
                win.Files.AddRange(dds.Where(f => NameIs(f, "result_" + ResultIndex(c) + ".dds")));
                res.Add(win);
            }
            return res;
        }

        /// <summary>Figur -> Nummer von sys\result_N.dds (Elena 2, Millenia 1).</summary>
        public static int ResultIndex(int charId) { return charId == 2 ? 2 : charId == 3 ? 1 : charId - 1; }

        static bool NameIs(GameFile f, string name) { return string.Equals(Path.GetFileName(f.RelPath.Replace('\\', '/')), name, StringComparison.OrdinalIgnoreCase); }
        static bool InFolder(GameFile f, string afsRel, string sub)
        {
            string dir = Path.GetDirectoryName(f.RelPath.Replace('\\', '/')).Replace('/', '\\');
            return string.Equals(dir, afsRel + "\\" + sub, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------ Bildauswahl

        /// <summary>
        /// Alle Portraets als Quelle, immer aus dem Original (Backup), damit eigene Aenderungen nicht weitergereicht werden.
        /// Dialoggesichter: jede Datei aus f_face_g, gleicher Dateiname nur einmal (alle Kopien teilen sich einen Texturindex).
        /// </summary>
        public static List<GalleryItem> BuildGallery(Workspace ws)
        {
            var list = new List<GalleryItem>();
            var known = ws.AllFiles.Where(f => f.FormatKey == "DDS").ToDictionary(f => f.FullPath, f => f, StringComparer.OrdinalIgnoreCase);
            Func<string, byte[]> read = full =>
            {
                GameFile gf;
                if (known.TryGetValue(Path.GetFullPath(full), out gf)) return gf.Original ?? gf.Disk;
                return File.ReadAllBytes(full);
            };
            Action<string, string, int, bool> add = (full, label, cid, framed) =>
            {
                try
                {
                    var bytes = read(full);
                    if (Dds.Check(bytes) != null) return;
                    list.Add(new GalleryItem { Label = label, CharId = cid, Source = Workspace.MakeRelative(ws.GameRoot, full), Image = ExtractContent(Dds.Read(bytes), framed) });
                }
                catch { }
            };

            string title = Workspace.FindDirIgnoreCase(ws.AfsRoot, "title_data");
            string sys = Workspace.FindDirIgnoreCase(ws.AfsRoot, "sys");
            for (int c = 1; c <= 7; c++)
            {
                string p;
                if (title != null && (p = Workspace.FindFileIgnoreCase(title, "f0" + c + "01.dds")) != null) add(p, L.T("Menü"), c, false);
                if (sys != null && (p = Workspace.FindFileIgnoreCase(sys, "btl_sys1_" + (c + 6) + ".dds")) != null) add(p, L.T("Kampf groß"), c, false);
                if (sys != null && (p = Workspace.FindFileIgnoreCase(sys, "result_" + ResultIndex(c) + ".dds")) != null) add(p, L.T("Sieg und Level-up"), c, false);
                if (sys != null && (p = Workspace.FindFileIgnoreCase(sys, "btl_sys1_" + (c - 1) + ".dds")) != null) add(p, L.T("Kampf"), c, true);
            }
            string party = Workspace.FindDirIgnoreCase(ws.AfsRoot, "party");
            foreach (var pair in new[] { new[] { "c03ang_e", L.T("Rage-Gesicht (Original)") }, new[] { "c03ang_s", L.T("Nach der Rage (Original)") } })
            {
                string d = party == null ? null : Workspace.FindDirIgnoreCase(party, pair[0]);
                string p = d == null ? null : Workspace.FindFileIgnoreCase(d, AngFace);
                if (p != null) add(p, pair[1], 3, true);
            }
            string faces = Workspace.FindDirIgnoreCase(ws.AfsRoot, "f_face_g");
            if (faces != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var files = Directory.GetFiles(faces, "*.dds", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var p in files)
                {
                    string n = Path.GetFileNameWithoutExtension(p);
                    if (!seen.Add(n)) continue;
                    int cid = 0;
                    if (n.Length == 5 && (n[0] == 'f' || n[0] == 'F')) int.TryParse(n.Substring(1, 2), out cid);
                    add(p, L.T("Dialog") + " " + n.ToLowerInvariant(), cid, false);
                }
            }
            return list;
        }
    }
}
