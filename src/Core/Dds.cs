using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace G2Modifier.Core
{
    /// <summary>Bild im Speicher, 4 Byte je Pixel in der Reihenfolge R, G, B, A.</summary>
    public class RgbaImage
    {
        public readonly int W, H;
        public readonly byte[] Px;
        public RgbaImage(int w, int h) { W = w; H = h; Px = new byte[w * h * 4]; }
        public RgbaImage(int w, int h, byte[] px) { W = w; H = h; Px = px; }
        public RgbaImage Clone() { return new RgbaImage(W, H, (byte[])Px.Clone()); }

        public RgbaImage Crop(Rectangle r)
        {
            var o = new RgbaImage(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(Px, ((r.Y + y) * W + r.X) * 4, o.Px, y * r.Width * 4, r.Width * 4);
            return o;
        }

        /// <summary>Pixel gleich? Zwei voll transparente Pixel gelten immer als gleich.</summary>
        public bool SamePixel(int i, RgbaImage other)
        {
            if (Px[i + 3] == 0 && other.Px[i + 3] == 0) return true;
            return Px[i] == other.Px[i] && Px[i + 1] == other.Px[i + 1] && Px[i + 2] == other.Px[i + 2] && Px[i + 3] == other.Px[i + 3];
        }

        /// <summary>
        /// Skaliert einen Ausschnitt auf w x h. Jedes Zielpixel mittelt ein Raster bilinearer Stichproben
        /// (Verkleinern ohne Treppen, Vergroessern weich). Alpha wird vormultipliziert gerechnet.
        /// </summary>
        public RgbaImage Resize(RectangleF src, int w, int h)
        {
            var o = new RgbaImage(w, h);
            double sx = src.Width / w, sy = src.Height / h;
            int kx = Math.Max(1, (int)Math.Ceiling(sx)), ky = Math.Max(1, (int)Math.Ceiling(sy));
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < ky; j++)
                        for (int i = 0; i < kx; i++)
                        {
                            double fx = src.X + (x + (i + 0.5) / kx) * sx - 0.5;
                            double fy = src.Y + (y + (j + 0.5) / ky) * sy - 0.5;
                            double pr, pg, pb, pa;
                            Bilinear(fx, fy, out pr, out pg, out pb, out pa);
                            r += pr; g += pg; b += pb; a += pa;
                        }
                    int n = kx * ky, k = (y * w + x) * 4;
                    a /= n;
                    if (a > 0.0001) { o.Px[k] = Clamp(r / n / a); o.Px[k + 1] = Clamp(g / n / a); o.Px[k + 2] = Clamp(b / n / a); }
                    o.Px[k + 3] = Clamp(a * 255.0);
                }
            return o;
        }

        void Bilinear(double fx, double fy, out double r, out double g, out double b, out double a)
        {
            if (fx < 0) fx = 0; if (fy < 0) fy = 0;
            if (fx > W - 1) fx = W - 1; if (fy > H - 1) fy = H - 1;
            int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, W - 1), y1 = Math.Min(y0 + 1, H - 1);
            double tx = fx - x0, ty = fy - y0;
            r = g = b = a = 0;
            Acc(x0, y0, (1 - tx) * (1 - ty), ref r, ref g, ref b, ref a);
            Acc(x1, y0, tx * (1 - ty), ref r, ref g, ref b, ref a);
            Acc(x0, y1, (1 - tx) * ty, ref r, ref g, ref b, ref a);
            Acc(x1, y1, tx * ty, ref r, ref g, ref b, ref a);
        }

        void Acc(int x, int y, double wgt, ref double r, ref double g, ref double b, ref double a)
        {
            int k = (y * W + x) * 4;
            double al = Px[k + 3] / 255.0 * wgt;
            r += Px[k] * al; g += Px[k + 1] * al; b += Px[k + 2] * al; a += al;
        }

        static byte Clamp(double v) { return v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5); }

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new byte[W * 4];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int k = (y * W + x) * 4, t = x * 4;
                    row[t] = Px[k + 2]; row[t + 1] = Px[k + 1]; row[t + 2] = Px[k]; row[t + 3] = Px[k + 3];
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, W * 4);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }

        public static RgbaImage FromBitmap(Bitmap src)
        {
            using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                var img = new RgbaImage(bmp.Width, bmp.Height);
                var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var row = new byte[bmp.Width * 4];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, bmp.Width * 4);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int k = (y * bmp.Width + x) * 4, t = x * 4;
                        img.Px[k] = row[t + 2]; img.Px[k + 1] = row[t + 1]; img.Px[k + 2] = row[t]; img.Px[k + 3] = row[t + 3];
                    }
                }
                bmp.UnlockBits(bd);
                return img;
            }
        }
    }

}
