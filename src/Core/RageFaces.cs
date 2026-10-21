using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace G2Modifier.Core
{
    /// <summary>
    /// Rage-Gesichter je Figur.
    /// Das Spiel kennt genau EIN Paar (party\c03ang_e und party\c03ang_s). Das Tool merkt sich deshalb fuer jede Figur
    /// ein eigenes Paar und schreibt immer das Paar der aktuellen Rage-Figur ins Spiel.
    /// Gespeichert werden die fertigen Bilder (128x128, mit Rahmen) als PNG im Backup-Ordner, Unterordner rage-gesichter.
    /// Geschrieben wird erst beim Speichern (Strg+S), wie alles andere.
    ///
    /// Groesse: Das Original ist 64x64 und wird im Spiel auf den 128er-Platz des Kampfportraets gestreckt.
    /// Gebaut wird in 128x128 (Rahmen des Kampfportraets), geschrieben wird in 64x64 mit dem Original-Kopf.
    /// 128x128 im Spiel (bis 0.12.8) fuehrt zum Absturz.
    /// </summary>
    public class RageFaces
    {
        public const string FolderName = "rage-gesichter";
        public const int Size = 128;
        readonly string dir;
        readonly Dictionary<string, RgbaImage> saved = new Dictionary<string, RgbaImage>();
        readonly Dictionary<string, RgbaImage> pending = new Dictionary<string, RgbaImage>();   // null = auf Standard zuruecksetzen
        public readonly List<string> Problems = new List<string>();

        static string Key(int id, bool during) { return id + (during ? "_waehrend" : "_nach"); }

        public RageFaces(string backupDir)
        {
            dir = backupDir == null ? null : Path.Combine(backupDir, FolderName);
            if (dir == null || !Directory.Exists(dir)) return;
            foreach (var p in Directory.GetFiles(dir, "*.png"))
            {
                try
                {
                    using (var b = new Bitmap(p))
                    {
                        var img = RgbaImage.FromBitmap(b);
                        if (img.W == Size && img.H == Size) saved[Path.GetFileNameWithoutExtension(p)] = img;
                        else Problems.Add(L.F("{0}: unerwartete Größe {1}x{2}, ignoriert.", Path.GetFileName(p), img.W, img.H));
                    }
                }
                catch (Exception ex) { Problems.Add(Path.GetFileName(p) + ": " + ex.Message); }
            }
        }

        public bool Dirty { get { return pending.Count > 0; } }

        /// <summary>Eigenes Bild der Figur (ungespeicherter Stand zuerst), sonst null.</summary>
        public RgbaImage Custom(int id, bool during)
        {
            RgbaImage img;
            string k = Key(id, during);
            if (pending.TryGetValue(k, out img)) return img;
            return saved.TryGetValue(k, out img) ? img : null;
        }

        public bool IsPending(int id, bool during) { return pending.ContainsKey(Key(id, during)); }

        public void SetCustom(int id, bool during, RgbaImage composed)
        {
            if (composed.W != Size || composed.H != Size) throw new ArgumentException("128x128");
            pending[Key(id, during)] = composed;
        }

        public void Clear(int id, bool during)
        {
            string k = Key(id, during);
            if (saved.ContainsKey(k)) pending[k] = null; else pending.Remove(k);
        }

        public void DiscardPending() { pending.Clear(); }

        /// <summary>Nach erfolgreichem Speichern: PNGs schreiben bzw. loeschen.</summary>
        public void Flush()
        {
            if (dir == null || pending.Count == 0) return;
            Directory.CreateDirectory(dir);
            foreach (var kv in pending.ToList())
            {
                string path = Path.Combine(dir, kv.Key + ".png");
                if (kv.Value == null) { if (File.Exists(path)) File.Delete(path); saved.Remove(kv.Key); }
                else { using (var b = kv.Value.ToBitmap()) b.Save(path, ImageFormat.Png); saved[kv.Key] = kv.Value; }
            }
            pending.Clear();
        }

        // ------------------------------------------------------------ Spiel

        /// <summary>Kampfportraet (sys\btl_sys1_{id-1}.dds) der Figur, liefert Rahmen und DDS-Kopf.</summary>
        public static GameFile BattleFile(Workspace ws, int id)
        {
            string name = "btl_sys1_" + (id - 1) + ".dds";
            return ws.AllFiles.FirstOrDefault(f => f.FormatKey == "DDS" && f.Valid
                && string.Equals(Path.GetFileName(f.RelPath.Replace('\\', '/')), name, StringComparison.OrdinalIgnoreCase)
                && f.RelPath.IndexOf("\\sys\\", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static GameFile PortraitIn(Workspace ws, string folder, int id)
        {
            string name = "btl_sys1_" + (id - 1) + ".dds";
            return ws.AllFiles.FirstOrDefault(f => f.FormatKey == "DDS" && f.Valid
                && string.Equals(Path.GetFileName(f.RelPath.Replace('\\', '/')), name, StringComparison.OrdinalIgnoreCase)
                && f.RelPath.IndexOf("\\" + folder + "\\", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Rahmen und DDS-Vorlage fuer eigene Rage-Gesichter. Reihenfolge: sys\btl_sys1_(X-1).dds, sys_hardmode\btl_sys1_(X-1).dds,
        /// bei Millenia zuletzt ihr Original-Rage-Gesicht (64x64, auf 128x128 vergroessert, gleicher Rahmen).
        /// Manche Installationen haben sys\btl_sys1_2.dds nicht. Ohne diese Kette war die Rage-Seite dann tot.
        /// </summary>
        public class Frame { public RgbaImage Image; public byte[] Template; public string Source; public bool Fallback; }

        public static Frame FrameFor(Workspace ws, int id)
        {
            foreach (var folder in new[] { "sys", "sys_hardmode" })
            {
                var bf = PortraitIn(ws, folder, id);
                if (bf != null) return new Frame { Image = AvatarSlot.OriginalOf(bf), Template = bf.Data, Source = bf.RelPath, Fallback = folder != "sys" };
            }
            if (id == 3 && ws.AngEFace != null && ws.AngEFace.Valid)
            {
                var orig = ws.AngEFace.Original ?? ws.AngEFace.Disk;
                var img = Dds.Read(orig);
                if (img.W != Size || img.H != Size) img = img.Resize(new RectangleF(0, 0, img.W, img.H), Size, Size);
                return new Frame { Image = img, Template = orig, Source = ws.AngEFace.RelPath, Fallback = true };
            }
            return null;
        }

        /// <summary>Quelle in den Rahmen des Kampfportraets der Figur setzen (128x128).</summary>
        public static RgbaImage Compose(Workspace ws, int id, RgbaImage src)
        {
            var fr = FrameFor(ws, id);
            if (fr == null) throw new InvalidOperationException(L.F("Kampfporträt von {0} fehlt.", Avatars.CharName(id)));
            var img = Avatars.Compose(fr.Image, src, true);
            if (img.W != Size || img.H != Size) img = img.Resize(new RectangleF(0, 0, img.W, img.H), Size, Size);
            return img;
        }

        /// <summary>
        /// Was das Spiel fuer diese Figur zeigen soll. null = Originaldatei des Spiels (nur Millenia).
        /// Ohne eigenes Bild bekommt jede andere Figur ihr aktuelles Kampfportraet, nie Millenias Gesicht.
        /// </summary>
        public RgbaImage Effective(Workspace ws, int id, bool during)
        {
            var c = Custom(id, during);
            if (c != null) return c;
            if (id == 3) return null;
            var bf = BattleFile(ws, id);
            if (bf == null) return null;
            var img = Dds.Read(bf.Data);
            return img.W == Size && img.H == Size ? img : img.Resize(new RectangleF(0, 0, img.W, img.H), Size, Size);
        }

        /// <summary>Vorschau fuer die Karte: Effective oder das Originalbild aus dem Spiel.</summary>
        public RgbaImage Preview(Workspace ws, int id, bool during)
        {
            var e = Effective(ws, id, during);
            if (e != null) return e;
            var f = during ? ws.AngEFace : ws.AngSFace;
            return f == null ? null : Dds.Read(f.Original ?? f.Disk);
        }

        /// <summary>
        /// Groesse, in der das Spiel die Rage-Gesichter erwartet: 64x64, wie das Original.
        /// Bis 0.12.8 schrieb das Tool 128x128 (65.664 statt 16.512 Byte). Das bringt das Spiel zum Absturz.
        /// Das Bild wird intern weiter in 128x128 gebaut und erst beim Schreiben verkleinert.
        /// </summary>
        public const int GameSize = 64;

        /// <summary>Schreibt das Paar der Figur in die beiden Spieldateien (nur im Speicher). Kopf und Groesse bleiben wie im Original.</summary>
        public void WriteToGame(Workspace ws, int rageId)
        {
            foreach (var during in new[] { true, false })
            {
                var f = during ? ws.AngEFace : ws.AngSFace;
                if (f == null || !f.Valid) continue;
                var orig = f.Original ?? f.Disk;
                var img = Effective(ws, rageId, during);
                if (img == null)
                {
                    if (Dds.SizeOf(orig).Width == GameSize && Dds.SizeOf(orig).Height == GameSize) { if (!Bin.Equal(orig, f.Data)) f.Data = (byte[])orig.Clone(); }
                    else
                    {
                        // Backup fehlt oder enthaelt schon die zu grosse Datei: auf 64x64 zurueckrechnen.
                        var cur = Dds.Read(orig);
                        var small = cur.Resize(new RectangleF(0, 0, cur.W, cur.H), GameSize, GameSize);
                        f.Data = Dds.Build(orig, small);
                    }
                    continue;
                }
                if (img.W != GameSize || img.H != GameSize) img = img.Resize(new RectangleF(0, 0, img.W, img.H), GameSize, GameSize);
                var data = Dds.Build(orig, img);
                if (!Bin.Equal(data, f.Data)) f.Data = data;
            }
        }

        /// <summary>True, wenn eine der beiden Rage-Gesicht-Dateien auf der Platte nicht 64x64 ist (Altlast bis 0.12.8).</summary>
        public static bool DiskHasWrongSize(Workspace ws)
        {
            foreach (var f in new[] { ws.AngEFace, ws.AngSFace })
            {
                if (f == null || !f.Valid || f.Disk == null) continue;
                var sz = Dds.SizeOf(f.Disk);
                if (sz.Width != GameSize || sz.Height != GameSize) return true;
            }
            return false;
        }
    }
}
