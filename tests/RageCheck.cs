using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Prueft Rage-Patches und Avatar-Plaetze gegen eine KOPIE echter Spieldateien.
static class RageCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] args)
    {
        string game = args[0], backupRoot = args[1], outDir = args[2];
        var ws = Workspace.Open(game);
        Console.WriteLine("Dateien geladen: " + ws.AllFiles.Count + ", davon gesperrt: " + ws.AllFiles.Count(f => !f.Valid));
        foreach (var f in ws.AllFiles.Where(f => !f.Valid)) Console.WriteLine("   GESPERRT " + f.RelPath + ": " + f.Errors[0]);
        var bk = new BackupService(ws.GameRoot, backupRoot);
        Console.WriteLine("Neu gesichert: " + bk.EnsureBackups(ws.AllFiles, null));

        Console.WriteLine("\n== Rage lesen");
        Ok(RagePatch.Available(ws), "Exe erkannt, alle Stellen passen");
        string warn;
        var cur = RagePatch.Current(ws, out warn);
        Ok(warn == null, "keine Widersprüche (" + (warn ?? "-") + ")");
        Ok(cur.Enabled && cur.ElenaLock && cur.CharId == 3 && cur.Duration == 1200, "Original: an, Elena-Sperre, Millenia, 1200");

        Console.WriteLine("\n== Rage schreiben, nur erwartete Bytes");
        var exeOrig = (byte[])ws.Exe.Data.Clone();
        var allowed = new HashSet<int>();
        foreach (var s in RagePatch.IdSites) allowed.Add(s[0] + 3);
        allowed.Add(RagePatch.LockAtBattleStart + 6); allowed.Add(RagePatch.LockIfElena + 6);
        for (int i = 0; i < 4; i++) { allowed.Add(RagePatch.DurationSet + 6 + i); allowed.Add(RagePatch.DurationCmp + 6 + i); }
        for (int i = 0; i < 12; i++) { allowed.Add(RagePatch.StartIndex + i); allowed.Add(RagePatch.EndIndex + i); }
        foreach (var o in RagePatch.LockReadSites) for (int i = 2; i < 6; i++) allowed.Add(o + i);    // Sperre lesen: nur der Adress-Operand
        foreach (var o in RagePatch.ThresholdSites) for (int i = 3; i < 7; i++) allowed.Add(o + i);   // Wut-Schwelle: nur der Adress-Operand
        for (int i = 4; i < 8; i++) allowed.Add(RagePatch.StartWrathSite + i);                        // Wut bei Kampfbeginn
        foreach (var o in RagePatch.ChanceSites) for (int i = 4; i < 8; i++) allowed.Add(o + i);      // Ausloese-Chance
        foreach (var o in new[] { 0x16A8D, 0x16A98, 0x16A9A, 0x16A9B, 0x16A9C, 0x16A9D, 0x16A9E, 0x16A9F, 0x16AA0, 0x16AA1, 0x16AA2,
                                  0x1310F6, 0x1310F7, 0x1310F8, 0x1310F9, 0x1310FA, 0x1310FB, 0x1310FC }) allowed.Add(o);   // Mindestwut
        var cases = new List<RageSettings>
        {
            new RageSettings { Enabled = false, ElenaLock = true, CharId = 3, Duration = 1200 },
            new RageSettings { Enabled = true, ElenaLock = false, CharId = 3, Duration = 1200 },
            new RageSettings { Enabled = true, ElenaLock = false, CharId = 1, Duration = 2400 },
            new RageSettings { Enabled = true, ElenaLock = true, CharId = 7, Duration = 600 },
            new RageSettings { Enabled = false, ElenaLock = false, CharId = 5, Duration = 1200 },
            new RageSettings { Enabled = true, ElenaLock = false, CharId = 3, Duration = 1200, Threshold = 10, StartWrath = 2, Chance = 3, MinWrathOne = true },
            new RageSettings { Enabled = true, ElenaLock = true, CharId = 3, Duration = 1200, Threshold = 0, StartWrath = 1, Chance = 1 },
            new RageSettings { Enabled = true, ElenaLock = false, CharId = 3, Duration = 1200, Threshold = 50, StartWrath = 0, Chance = 2, MinWrathOne = true },
        };
        foreach (var c in cases)
        {
            RagePatch.Apply(ws, c);
            var diff = GameFile.DiffRanges(exeOrig, ws.Exe.Data).SelectMany(r => Enumerable.Range(r[0], r[1])).ToList();
            bool onlyAllowed = diff.All(allowed.Contains);
            var back = RagePatch.Current(ws, out warn);
            Ok(onlyAllowed && back.SameAs(c) && warn == null && RagePatch.Validate(ws.Exe.Data) == null,
               string.Format("an={0} elena={1} figur={2} dauer={3} schwelle={5} start={6} chance={7} min1={8}: {4} Bytes geändert, zurückgelesen gleich", c.Enabled, c.ElenaLock, c.CharId, c.Duration, diff.Count, c.Threshold, c.StartWrath, c.Chance, c.MinWrathOne));
            if (c.StartWrath == 2 && c.Chance == 3)
            {
                // Operanden direkt pruefen: Startwut zeigt auf 100.0, alle vier Chancen auf 1.0
                Func<int, float> fl = va => BitConverter.ToSingle(ws.Exe.Data, va - 0x401600);
                Ok(fl((int)Bin.U32(ws.Exe.Data, RagePatch.StartWrathSite + 4)) == 100f && RagePatch.ChanceSites.All(o => fl((int)Bin.U32(ws.Exe.Data, o + 4)) == 1f),
                   "   voll + 100 %: Operand Startwut -> 100.0, alle vier Chancen -> 1.0");
            }
            if (!c.Enabled) Ok(ws.Exe.Data[RagePatch.LockIfElena + 6] == 1, "   aus: Elena-Zeile bleibt 1");
            Ok(Bin.U32(ws.AngE.Data, RagePatch.GbixAngE) == 0xAA + c.CharId && Bin.U32(ws.AngS.Data, RagePatch.GbixAngS) == 0xAA + c.CharId, "   GBIX beider Party-Dateien = 0x" + (0xAA + c.CharId).ToString("X"));
            Ok(GameFile.DiffRanges(ws.AngE.Disk, ws.AngE.Data).Sum(r => r[1]) <= 1 && GameFile.DiffRanges(ws.AngS.Disk, ws.AngS.Data).Sum(r => r[1]) <= 1, "   Party-Dateien: höchstens 1 Byte geändert");
        }
        RagePatch.Apply(ws, new RageSettings());
        Ok(Bin.Equal(ws.Exe.Data, exeOrig) && !ws.AngE.Dirty && !ws.AngS.Dirty, "Zurück auf Original ergibt bytegleiche Dateien");

        Console.WriteLine("\n== Avatar-Plätze");
        var slots = Avatars.BuildSlots(ws);
        foreach (var s in slots)
        {
            if (s.Files.Count == 0) { Console.WriteLine("   " + (s.CharId > 0 ? Avatars.CharName(s.CharId) : "Rage") + " / " + s.Title + ": keine Dateien in dieser Kopie"); continue; }
            var o = AvatarSlot.OriginalOf(s.First);
            var r = Avatars.ContentRect(o, s.Framed ? Avatars.FrameMask(o) : null);
            Console.WriteLine("   " + (s.CharId > 0 ? Avatars.CharName(s.CharId) : "Rage") + " / " + s.Title + ": " + s.SizeText + ", Inhalt " + r.X + "," + r.Y + " " + r.Width + "x" + r.Height);
        }
        var gallery = Avatars.BuildGallery(ws);
        Console.WriteLine("   Bildauswahl: " + gallery.Count + " Bilder");

        // Sichtpruefung: Millenia-Plaetze mit Ryudo-Menübild, Rage-Plaetze mit Ryudo-Kampf groß
        var ryuMenu = gallery.First(g => g.CharId == 1 && g.Label == "Menü").Image;
        var tiles = new List<RgbaImage>();
        foreach (var s in slots.Where(x => x.Files.Count > 0 && x.CharId == 3))
        {
            var before = s.Current();
            s.Apply(ryuMenu);
            Ok(s.Dirty && s.Files.All(f => f.Data.Length == f.Disk.Length), s.Title + ": gesetzt, Dateigröße gleich");
            // Rahmen erhalten?
            if (s.Framed)
            {
                var mask = Avatars.FrameMask(before); var now = s.Current(); int diffFrame = 0;
                for (int i = 0; i < mask.Length; i++) if (mask[i] && !before.SamePixel(i * 4, now)) diffFrame++;
                Ok(diffFrame == 0, s.Title + ": Rahmenpixel unverändert");
            }
            tiles.Add(before); tiles.Add(s.Current());
            s.Reset();
            Ok(!s.Dirty, s.Title + ": Original wiederhergestellt");
        }
        SaveGrid(tiles, Path.Combine(outDir, "avatar_vorher_nachher.png"));

        Console.WriteLine("\n== Rage-Gesichter je Figur (geschrieben in 64x64)");
        var faces = new RageFaces(bk.Dir);
        var ryuBig = gallery.First(g => g.CharId == 1 && g.Label == "Kampf groß").Image;
        var millMenu = gallery.First(g => g.CharId == 3 && g.Label == "Menü").Image;
        RagePatch.Apply(ws, new RageSettings { Enabled = true, ElenaLock = false, CharId = 1, Duration = 1500 });
        faces.WriteToGame(ws, 1);
        Ok(Dds.SizeOf(ws.AngEFace.Data).Width == 64 && Dds.Check(ws.AngEFace.Data) == null, "Ryudo ohne eigenes Bild: Standard = Kampfporträt, 64x64, gültiges DDS");
        { var bp = Dds.Read(RageFaces.BattleFile(ws, 1).Data); Ok(Bin.Equal(Dds.Read(ws.AngEFace.Data).Px, bp.Resize(new System.Drawing.RectangleF(0, 0, bp.W, bp.H), 64, 64).Px), "   Pixel = Ryudos Kampfporträt (auf 64x64 verkleinert)"); }
        faces.SetCustom(1, true, RageFaces.Compose(ws, 1, ryuBig));
        faces.SetCustom(3, true, RageFaces.Compose(ws, 3, millMenu));
        faces.WriteToGame(ws, 1);
        var r1 = (byte[])ws.AngEFace.Data.Clone();
        faces.WriteToGame(ws, 3);
        var m1 = (byte[])ws.AngEFace.Data.Clone();
        Ok(!Bin.Equal(r1, m1), "Ryudo und Millenia haben verschiedene Rage-Gesichter");
        faces.Clear(3, true); faces.WriteToGame(ws, 3);
        Ok(Bin.Equal(ws.AngEFace.Data, ws.AngEFace.Original), "Millenia auf Standard = Originaldatei 64x64 bytegleich");
        faces.WriteToGame(ws, 1);
        Ok(Bin.Equal(ws.AngEFace.Data, r1), "zurück zu Ryudo: sein eigenes Bild ist noch da");
        var tiles2 = new List<RgbaImage> { Dds.Read(ws.AngEFace.Original), Dds.Read(r1), Dds.Read(m1), Dds.Read(RageFaces.BattleFile(ws, 1).Data) };
        SaveGrid(tiles2, Path.Combine(outDir, "rage_gesichter.png"));

        Console.WriteLine("\n== Speichern und neu laden");
        var rep = bk.SaveDirty(ws);
        foreach (var l in rep) Console.WriteLine("   " + l);
        Ok(bk.LastFailures == 0, "alles geschrieben, auch die größere DDS-Datei");
        faces.Flush();
        var ws2 = Workspace.Open(game);
        var again = RagePatch.Current(ws2, out warn);
        Ok(again.CharId == 1 && !again.ElenaLock && again.Duration == 1500 && warn == null, "nach Neuladen: Ryudo, keine Elena-Sperre, 1500");
        Ok(ws2.AngEFace != null && ws2.AngEFace.Valid && Dds.SizeOf(ws2.AngEFace.Disk).Width == 64 && ws2.AngEFace.Disk.Length == 16512, "Rage-Gesicht nach Neuladen: gültig, 64x64, 16.512 Byte");
        var faces2 = new RageFaces(bk.Dir);
        Ok(faces2.Custom(1, true) != null && faces2.Custom(3, true) == null && faces2.Problems.Count == 0, "gesicherte Rage-Gesichter wieder geladen (PNG im Backup-Ordner)");
        bk.EnsureBackups(ws2.AllFiles, null);
        int restored = 0;
        foreach (var f in ws2.AllFiles.Where(f => f.DiskState == "verändert")) { if (bk.Restore(f) == null) restored++; }
        var ws3 = Workspace.Open(game);
        bk.EnsureBackups(ws3.AllFiles, null);
        Ok(ws3.AllFiles.All(f => f.DiskState == "Original") && Dds.SizeOf(ws3.AngEFace.Disk).Width == 64, restored + " Dateien wiederhergestellt, alles wieder Original (Rage-Gesicht wieder 64x64)");

        Console.WriteLine(fails == 0 ? "\nALLE PRÜFUNGEN BESTANDEN" : "\n" + fails + " FEHLER");
        return fails == 0 ? 0 : 1;
    }

    static void SaveGrid(List<RgbaImage> imgs, string path)
    {
        int cell = 136, cols = 4, rows = (imgs.Count + 3) / 4;
        using (var bmp = new Bitmap(cols * cell, rows * cell))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(40, 40, 60));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            for (int i = 0; i < imgs.Count; i++)
                using (var b = imgs[i].ToBitmap())
                {
                    int x = (i % 4) * cell, y = (i / 4) * cell;
                    g.DrawImage(b, new Rectangle(x + 4, y + 4, 128, 128));
                }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
