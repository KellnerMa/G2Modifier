using System;
using System.Drawing;

namespace G2Modifier.Core
{
    /// <summary>
    /// DDS lesen und schreiben. Unterstuetzt:
    ///  - unkomprimiert 32 bit mit beliebiger Byte-Reihenfolge (HD Remaster: RGBA ohne Mipmaps, Anniversary Edition: BGRA mit Mipmaps)
    ///  - DXT1, DXT3, DXT5 (Anniversary Edition: Porträts, Titel-Logo, Fensterhintergrund), mit oder ohne Mipmaps
    /// Die Dateigroesse muss exakt Kopf + alle Mipmap-Stufen sein, sonst wird abgelehnt und nie geschrieben.
    /// Schreiben: Kopf, Format, Mipmap-Zahl und Dateigroesse bleiben unveraendert. Bei DXT werden 4x4-Bloecke, deren Pixel sich
    /// nicht geaendert haben, bytegleich uebernommen (Rahmen und Rand bleiben exakt wie im Original). Mipmaps werden aus der
    /// neuen obersten Stufe neu berechnet.
    /// Der HD-Fall (RGBA, keine Mipmaps) arbeitet genau wie bis 0.12.9: Pixel werden 1:1 kopiert.
    /// </summary>
    public static class Dds
    {
        public const int HeaderSize = 128;
        public enum Kind { Rgba32, Dxt1, Dxt3, Dxt5 }

        public class Info
        {
            public int W, H, Mips;
            public Kind Kind;
            public int[] Shift = new int[4];        // Rgba32: Bitposition von R, G, B, A (A = -1: kein Alpha, immer 255)
            public int[] LevelOffset, LevelSize;
            public bool PlainRgba { get { return Kind == Kind.Rgba32 && Shift[0] == 0 && Shift[1] == 8 && Shift[2] == 16 && Shift[3] == 24 && Mips == 1; } }
            public string FormatName { get { return Kind == Kind.Rgba32 ? (PlainRgba ? "RGBA" : Shift[0] == 16 ? "BGRA" : "RGB32") : Kind.ToString().ToUpperInvariant(); } }
        }

        static int MaskShift(long m)
        {
            switch (m) { case 0xFF: return 0; case 0xFF00: return 8; case 0xFF0000: return 16; case 0xFF000000: return 24; default: return -2; }
        }

        static int LevelBytes(Kind k, int w, int h)
        {
            if (k == Kind.Rgba32) return w * h * 4;
            return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * (k == Kind.Dxt1 ? 8 : 16);
        }

        public static Info Parse(byte[] d, out string err)
        {
            err = null;
            if (d == null || d.Length < HeaderSize) { err = L.T("zu kurz für DDS"); return null; }
            if (d[0] != 'D' || d[1] != 'D' || d[2] != 'S' || d[3] != ' ') { err = L.T("keine DDS-Kennung"); return null; }
            if (Bin.U32(d, 4) != 124) { err = L.T("DDS-Headergröße unerwartet"); return null; }
            var inf = new Info { H = (int)Bin.U32(d, 12), W = (int)Bin.U32(d, 16) };
            if (inf.W <= 0 || inf.H <= 0 || inf.W > 8192 || inf.H > 8192) { err = L.F("unplausible Größe {0}x{1}", inf.W, inf.H); return null; }
            long flags = Bin.U32(d, 8), pf = Bin.U32(d, 80);
            inf.Mips = (flags & 0x20000) != 0 && Bin.U32(d, 28) > 1 ? (int)Bin.U32(d, 28) : 1;
            if (inf.Mips > 16) { err = L.T("DDS-Headergröße unerwartet"); return null; }
            if ((pf & 0x4) != 0)
            {
                string cc = System.Text.Encoding.ASCII.GetString(d, 84, 4);
                if (cc == "DXT1") inf.Kind = Kind.Dxt1; else if (cc == "DXT3") inf.Kind = Kind.Dxt3; else if (cc == "DXT5") inf.Kind = Kind.Dxt5;
                else { err = L.F("kein unkomprimiertes RGBA (Pixelformat-Flags 0x{0})", pf.ToString("X")) + " (" + cc.Trim('\0', ' ') + ")"; return null; }
            }
            else if ((pf & 0x40) != 0)
            {
                if (Bin.U32(d, 88) != 32) { err = L.T("nicht 32 bit"); return null; }
                inf.Kind = Kind.Rgba32;
                for (int i = 0; i < 3; i++) inf.Shift[i] = MaskShift(Bin.U32(d, 92 + i * 4));
                inf.Shift[3] = (pf & 0x1) != 0 ? MaskShift(Bin.U32(d, 104)) : -1;
                if (inf.Shift[0] < 0 || inf.Shift[1] < 0 || inf.Shift[2] < 0 || inf.Shift[3] == -2) { err = L.T("unerwartete Farbmasken"); return null; }
            }
            else { err = L.F("kein unkomprimiertes RGBA (Pixelformat-Flags 0x{0})", pf.ToString("X")); return null; }
            inf.LevelOffset = new int[inf.Mips]; inf.LevelSize = new int[inf.Mips];
            int o = HeaderSize, w = inf.W, h = inf.H;
            for (int i = 0; i < inf.Mips; i++)
            {
                inf.LevelOffset[i] = o; inf.LevelSize[i] = LevelBytes(inf.Kind, w, h); o += inf.LevelSize[i];
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            }
            if (d.Length != o) { err = L.F("Dateigröße passt nicht zu {0}x{1} (Mipmaps oder Zusatzdaten)", inf.W, inf.H); return null; }
            return inf;
        }

        public static string Check(byte[] d) { string e; Parse(d, out e); return e; }

        public static RgbaImage Read(byte[] d)
        {
            string e; var inf = Parse(d, out e);
            if (inf == null) throw new FormatException(e);
            return Decode(d, inf.LevelOffset[0], inf.Kind, inf.Shift, inf.W, inf.H);
        }

        /// <summary>Ersetzt die Pixeldaten (alle Mipmap-Stufen). Kopf, Format und Dateigroesse bleiben unveraendert.</summary>
        public static void WritePixels(byte[] d, RgbaImage img)
        {
            string e; var inf = Parse(d, out e);
            if (inf == null) throw new FormatException(e);
            if (img.W != inf.W || img.H != inf.H) throw new ArgumentException(L.F("Bildgröße {0}x{1} passt nicht zur Datei {2}x{3}", img.W, img.H, inf.W, inf.H));
            if (inf.PlainRgba) { Buffer.BlockCopy(img.Px, 0, d, HeaderSize, img.Px.Length); return; }   // HD Remaster, wie bisher
            EncodeLevels(d, inf, img, true);
        }

        /// <summary>
        /// Neue DDS-Datei in anderer Groesse, im Format der Vorlage (z. B. btl_sys1_N.dds). Kopf wird uebernommen,
        /// Hoehe, Breite, Zeilenlaenge und Mipmap-Zahl werden angepasst.
        /// </summary>
        public static byte[] Build(byte[] template, RgbaImage img)
        {
            string e; var t = Parse(template, out e);
            if (t == null) throw new FormatException(e);
            if (t.PlainRgba)
            {
                // HD Remaster: unveraendert wie bis 0.12.9
                var d0 = new byte[HeaderSize + img.W * img.H * 4];
                Buffer.BlockCopy(template, 0, d0, 0, HeaderSize);
                Bin.W32(d0, 12, (uint)img.H);
                Bin.W32(d0, 16, (uint)img.W);
                long fl = Bin.U32(template, 8);
                if ((fl & 0x8) != 0) Bin.W32(d0, 20, (uint)(img.W * 4));
                else if ((fl & 0x80000) != 0) Bin.W32(d0, 20, (uint)(img.W * img.H * 4));
                Buffer.BlockCopy(img.Px, 0, d0, HeaderSize, img.Px.Length);
                if (Check(d0) != null) throw new FormatException(Check(d0));
                return d0;
            }
            int mips = 1;
            if (t.Mips > 1) { int m = Math.Max(img.W, img.H); while (m > 1) { m /= 2; mips++; } }
            int total = HeaderSize, w = img.W, h = img.H;
            for (int i = 0; i < mips; i++) { total += LevelBytes(t.Kind, w, h); w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            var d = new byte[total];
            Buffer.BlockCopy(template, 0, d, 0, HeaderSize);
            Bin.W32(d, 12, (uint)img.H);
            Bin.W32(d, 16, (uint)img.W);
            long flags = Bin.U32(template, 8);
            if ((flags & 0x8) != 0) Bin.W32(d, 20, (uint)(t.Kind == Kind.Rgba32 ? img.W * 4 : LevelBytes(t.Kind, img.W, img.H)));
            else if ((flags & 0x80000) != 0) Bin.W32(d, 20, (uint)LevelBytes(t.Kind, img.W, img.H));
            if ((flags & 0x20000) != 0) Bin.W32(d, 28, (uint)mips);
            var inf = Parse(d, out e);
            if (inf == null) throw new FormatException(e);
            EncodeLevels(d, inf, img, false);
            return d;
        }

        public static Size SizeOf(byte[] d) { return new Size((int)Bin.U32(d, 16), (int)Bin.U32(d, 12)); }

        /// <summary>Formatname fuer Anzeigen (RGBA, BGRA, DXT1, DXT5 ...), null wenn nicht lesbar.</summary>
        public static string FormatOf(byte[] d) { string e; var i = Parse(d, out e); return i == null ? null : i.FormatName + (i.Mips > 1 ? " +" + (i.Mips - 1) + " Mipmaps" : ""); }

        // ------------------------------------------------------------ Kodieren

        static void EncodeLevels(byte[] d, Info inf, RgbaImage img, bool keepSameBlocks)
        {
            RgbaImage old = keepSameBlocks ? Decode(d, inf.LevelOffset[0], inf.Kind, inf.Shift, inf.W, inf.H) : null;
            if (old != null && Bin.Equal(old.Px, img.Px)) return;   // nichts geaendert: Datei bleibt bytegleich
            var level = img;
            for (int i = 0; i < inf.Mips; i++)
            {
                if (i > 0) level = level.Resize(new RectangleF(0, 0, level.W, level.H), Math.Max(1, level.W / 2), Math.Max(1, level.H / 2));
                Encode(d, inf.LevelOffset[i], inf.Kind, inf.Shift, level, i == 0 ? old : null);
            }
        }

        static void Encode(byte[] d, int o, Kind k, int[] shift, RgbaImage img, RgbaImage old)
        {
            if (k == Kind.Rgba32)
            {
                for (int i = 0; i < img.W * img.H; i++)
                {
                    uint v = 0;
                    for (int c = 0; c < 4; c++) if (shift[c] >= 0) v |= (uint)img.Px[i * 4 + c] << shift[c];
                    Bin.W32(d, o + i * 4, v);
                }
                return;
            }
            int bw = Math.Max(1, (img.W + 3) / 4), bh = Math.Max(1, (img.H + 3) / 4), bs = k == Kind.Dxt1 ? 8 : 16;
            var px = new byte[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int bo = o + (by * bw + bx) * bs;
                    bool same = old != null;
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            int sx = Math.Min(bx * 4 + x, img.W - 1), sy = Math.Min(by * 4 + y, img.H - 1), si = (sy * img.W + sx) * 4, di = (y * 4 + x) * 4;
                            for (int c = 0; c < 4; c++) px[di + c] = img.Px[si + c];
                            if (same) for (int c = 0; c < 4; c++) if (old.Px[si + c] != img.Px[si + c]) { same = false; break; }
                        }
                    if (same) continue;   // Block unveraendert: Originalbytes bleiben
                    if (k == Kind.Dxt1) EncodeColor(px, d, bo, true);
                    else
                    {
                        if (k == Kind.Dxt5) EncodeAlpha5(px, d, bo); else EncodeAlpha3(px, d, bo);
                        EncodeColor(px, d, bo + 8, false);
                    }
                }
        }

        static int To565(double r, double g, double b)
        {
            int R = Math.Max(0, Math.Min(31, (int)Math.Round(r * 31 / 255))), G = Math.Max(0, Math.Min(63, (int)Math.Round(g * 63 / 255))), B = Math.Max(0, Math.Min(31, (int)Math.Round(b * 31 / 255)));
            return (R << 11) | (G << 5) | B;
        }

        static void From565(int c, out int r, out int g, out int b)
        {
            r = (c >> 11) & 31; g = (c >> 5) & 63; b = c & 31;
            r = (r << 3) | (r >> 2); g = (g << 2) | (g >> 4); b = (b << 3) | (b >> 2);
        }

        /// <summary>Farbblock: Endpunkte entlang der Hauptachse der Farben, dann jeweils naechster Palettenwert.
        /// DXT1 mit transparenten Pixeln (Alpha &lt; 128): 3-Farben-Modus, Index 3 = transparent.</summary>
        static void EncodeColor(byte[] px, byte[] d, int o, bool dxt1)
        {
            bool transp = false;
            if (dxt1) for (int i = 0; i < 16; i++) if (px[i * 4 + 3] < 128) transp = true;
            double mr = 0, mg = 0, mb = 0; int n = 0;
            for (int i = 0; i < 16; i++) { if (transp && px[i * 4 + 3] < 128) continue; mr += px[i * 4]; mg += px[i * 4 + 1]; mb += px[i * 4 + 2]; n++; }
            int c0, c1;
            if (n == 0) { c0 = 0; c1 = 0; }
            else
            {
                mr /= n; mg /= n; mb /= n;
                double crr = 0, crg = 0, crb = 0, cgg = 0, cgb = 0, cbb = 0;
                for (int i = 0; i < 16; i++)
                {
                    if (transp && px[i * 4 + 3] < 128) continue;
                    double r = px[i * 4] - mr, g = px[i * 4 + 1] - mg, b = px[i * 4 + 2] - mb;
                    crr += r * r; crg += r * g; crb += r * b; cgg += g * g; cgb += g * b; cbb += b * b;
                }
                double ar = 1, ag = 1, ab = 1;
                for (int it = 0; it < 8; it++)
                {
                    double nr = crr * ar + crg * ag + crb * ab, ng = crg * ar + cgg * ag + cgb * ab, nb = crb * ar + cgb * ag + cbb * ab;
                    double len = Math.Sqrt(nr * nr + ng * ng + nb * nb);
                    if (len < 1e-9) break;
                    ar = nr / len; ag = ng / len; ab = nb / len;
                }
                double lo = double.MaxValue, hi = double.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    if (transp && px[i * 4 + 3] < 128) continue;
                    double t = (px[i * 4] - mr) * ar + (px[i * 4 + 1] - mg) * ag + (px[i * 4 + 2] - mb) * ab;
                    if (t < lo) lo = t; if (t > hi) hi = t;
                }
                c0 = To565(mr + ar * hi, mg + ag * hi, mb + ab * hi);
                c1 = To565(mr + ar * lo, mg + ag * lo, mb + ab * lo);
            }
            // 4-Farben-Modus braucht c0 > c1, 3-Farben-Modus c0 <= c1
            if (transp ? c0 > c1 : c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            if (!transp && c0 == c1) { if (c1 > 0) c1--; else c0++; }
            var pal = new int[4, 3];
            int r0, g0, b0, r1, g1, b1;
            From565(c0, out r0, out g0, out b0); From565(c1, out r1, out g1, out b1);
            pal[0, 0] = r0; pal[0, 1] = g0; pal[0, 2] = b0; pal[1, 0] = r1; pal[1, 1] = g1; pal[1, 2] = b1;
            int colors = transp ? 3 : 4;
            for (int c = 0; c < 3; c++)
            {
                int a = pal[0, c], b = pal[1, c];
                if (transp) pal[2, c] = (a + b) / 2;
                else { pal[2, c] = (2 * a + b) / 3; pal[3, c] = (a + 2 * b) / 3; }
            }
            uint idx = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0;
                if (transp && px[i * 4 + 3] < 128) best = 3;
                else
                {
                    int bd = int.MaxValue;
                    for (int p = 0; p < colors; p++)
                    {
                        int dr = px[i * 4] - pal[p, 0], dg = px[i * 4 + 1] - pal[p, 1], db = px[i * 4 + 2] - pal[p, 2];
                        int dist = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                        if (dist < bd) { bd = dist; best = p; }
                    }
                }
                idx |= (uint)best << (i * 2);
            }
            Bin.W16(d, o, c0); Bin.W16(d, o + 2, c1); Bin.W32(d, o + 4, idx);
        }

        static void EncodeAlpha5(byte[] px, byte[] d, int o)
        {
            int a0 = 0, a1 = 255;
            for (int i = 0; i < 16; i++) { int a = px[i * 4 + 3]; if (a > a0) a0 = a; if (a < a1) a1 = a; }
            d[o] = (byte)a0; d[o + 1] = (byte)a1;
            var pal = new int[8];
            pal[0] = a0; pal[1] = a1;
            if (a0 > a1) for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
            else { for (int i = 1; i < 5; i++) pal[i + 1] = ((5 - i) * a0 + i * a1) / 5; pal[6] = 0; pal[7] = 255; }
            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3], best = 0, bd = int.MaxValue;
                for (int p = 0; p < 8; p++) { int dist = Math.Abs(a - pal[p]); if (dist < bd) { bd = dist; best = p; } }
                bits |= (ulong)best << (i * 3);
            }
            for (int i = 0; i < 6; i++) d[o + 2 + i] = (byte)(bits >> (i * 8));
        }

        static void EncodeAlpha3(byte[] px, byte[] d, int o)
        {
            for (int i = 0; i < 8; i++)
            {
                int lo = (px[(i * 2) * 4 + 3] + 8) / 17, hi = (px[(i * 2 + 1) * 4 + 3] + 8) / 17;
                d[o + i] = (byte)(Math.Min(15, lo) | (Math.Min(15, hi) << 4));
            }
        }

        // ------------------------------------------------------------ Dekodieren

        static RgbaImage Decode(byte[] d, int o, Kind k, int[] shift, int w, int h)
        {
            var img = new RgbaImage(w, h);
            if (k == Kind.Rgba32)
            {
                for (int i = 0; i < w * h; i++)
                {
                    uint v = (uint)Bin.U32(d, o + i * 4);
                    for (int c = 0; c < 3; c++) img.Px[i * 4 + c] = (byte)(v >> shift[c]);
                    img.Px[i * 4 + 3] = shift[3] >= 0 ? (byte)(v >> shift[3]) : (byte)255;
                }
                return img;
            }
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4), bs = k == Kind.Dxt1 ? 8 : 16;
            var alpha = new int[16];
            var pal = new int[4, 4];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int bo = o + (by * bw + bx) * bs, co = bo;
                    if (k == Kind.Dxt5)
                    {
                        int a0 = d[bo], a1 = d[bo + 1];
                        var ap = new int[8]; ap[0] = a0; ap[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) ap[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                        else { for (int i = 1; i < 5; i++) ap[i + 1] = ((5 - i) * a0 + i * a1) / 5; ap[6] = 0; ap[7] = 255; }
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)d[bo + 2 + i] << (i * 8);
                        for (int i = 0; i < 16; i++) alpha[i] = ap[(int)((bits >> (i * 3)) & 7)];
                        co = bo + 8;
                    }
                    else if (k == Kind.Dxt3)
                    {
                        for (int i = 0; i < 16; i++) alpha[i] = ((d[bo + i / 2] >> ((i & 1) * 4)) & 15) * 17;
                        co = bo + 8;
                    }
                    int c0 = Bin.U16(d, co), c1 = Bin.U16(d, co + 2);
                    int r, g, b;
                    From565(c0, out r, out g, out b); pal[0, 0] = r; pal[0, 1] = g; pal[0, 2] = b; pal[0, 3] = 255;
                    From565(c1, out r, out g, out b); pal[1, 0] = r; pal[1, 1] = g; pal[1, 2] = b; pal[1, 3] = 255;
                    bool four = k != Kind.Dxt1 || c0 > c1;
                    for (int c = 0; c < 3; c++)
                    {
                        if (four) { pal[2, c] = (2 * pal[0, c] + pal[1, c]) / 3; pal[3, c] = (pal[0, c] + 2 * pal[1, c]) / 3; }
                        else { pal[2, c] = (pal[0, c] + pal[1, c]) / 2; pal[3, c] = 0; }
                    }
                    pal[2, 3] = 255; pal[3, 3] = four ? 255 : 0;
                    uint idx = (uint)Bin.U32(d, co + 4);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= w || y >= h) continue;
                        int p = (int)((idx >> (i * 2)) & 3), t = (y * w + x) * 4;
                        img.Px[t] = (byte)pal[p, 0]; img.Px[t + 1] = (byte)pal[p, 1]; img.Px[t + 2] = (byte)pal[p, 2];
                        img.Px[t + 3] = k == Kind.Dxt1 ? (byte)pal[p, 3] : (byte)alpha[i];
                    }
                }
            return img;
        }
    }
}
