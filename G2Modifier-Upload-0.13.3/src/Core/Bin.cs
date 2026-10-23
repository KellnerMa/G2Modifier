using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace G2Modifier.Core
{
    /// <summary>Little-Endian-Zugriffe. Grandia II PC speichert alle Zahlen little-endian (Beleg: G2DataGUI StreamExtensions).</summary>
    public static class Bin
    {
        public static int U16(byte[] d, int o) { return d[o] | (d[o + 1] << 8); }
        public static int S16(byte[] d, int o) { return (short)(d[o] | (d[o + 1] << 8)); }
        public static long U32(byte[] d, int o) { return (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)); }
        public static int S32(byte[] d, int o) { return d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24); }
        public static void W16(byte[] d, int o, int v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); }
        public static void W32(byte[] d, int o, uint v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24); }

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(data);
                var sb = new StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Sha256File(string path) { return Sha256(File.ReadAllBytes(path)); }

        public static string Hex(byte[] d, int o, int len)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++) { if (i > 0) sb.Append(' '); sb.Append(d[o + i].ToString("X2")); }
            return sb.ToString();
        }

        public static bool IsPrintableText(byte[] d, int o, int len)
        {
            for (int i = 0; i < len; i++)
            {
                byte b = d[o + i];
                if (b != 0 && (b < 0x20 || b > 0x7E)) return false;
            }
            return true;
        }

        /// <summary>
        /// Platzhalter statt Text: Paare 30 F0, davor hoechstens Leerzeichen/0x00. So stehen Namen und Beschreibungen
        /// in sys_hardmode\ms_param.bin (HD Remaster und Anniversary Edition, Originaldatei). Das Spiel nimmt die Texte aus einer anderen Kopie.
        /// </summary>
        public static bool IsFillerText(byte[] d, int o, int len)
        {
            int i = 0;
            while (i < len && (d[o + i] == 0x20 || d[o + i] == 0)) i++;
            if (len - i < 2 || ((len - i) & 1) != 0) return false;
            for (; i < len; i += 2)
                if (d[o + i] != 0x30 || d[o + i + 1] != 0xF0) return false;
            return true;
        }
    }
}
