using System;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Altlast bis 0.12.8: Rage-Gesichter in 128x128 im Spielordner. 0.12.9 muss sie erkennen und auf 64x64 zurueckstellen.
static class FaceSizeCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static byte[] Big(byte[] orig)
    {
        var img = Dds.Read(orig);
        return Dds.Build(orig, img.Resize(new System.Drawing.RectangleF(0, 0, img.W, img.H), 128, 128));
    }

    static int Main(string[] a)
    {
        string game = a[0], bkRoot = a[1], bkRoot2 = a[2];
        var ws = Workspace.Open(game);
        var bk = new BackupService(ws.GameRoot, bkRoot);
        bk.EnsureBackups(ws.AllFiles, null);
        byte[] origE = (byte[])ws.AngEFace.Disk.Clone(), origS = (byte[])ws.AngSFace.Disk.Clone();
        Ok(origE.Length == 16512 && !RageFaces.DiskHasWrongSize(ws), "Ausgangslage: Original 64x64, nichts zu reparieren");

        // Zustand wie nach 0.12.8 herstellen
        File.WriteAllBytes(ws.AngEFace.FullPath, Big(origE));
        File.WriteAllBytes(ws.AngSFace.FullPath, Big(origS));

        // Fall 1: ohne eigenes Bild, mit Backup
        var ws1 = Workspace.Open(game);
        var bk1 = new BackupService(ws1.GameRoot, bkRoot); bk1.EnsureBackups(ws1.AllFiles, null);
        Ok(RageFaces.DiskHasWrongSize(ws1), "128x128 auf der Platte wird erkannt");
        var faces = new RageFaces(bk1.Dir);
        faces.WriteToGame(ws1, 3);
        Ok(Bin.Equal(ws1.AngEFace.Data, origE) && Bin.Equal(ws1.AngSFace.Data, origS), "ohne eigenes Bild: zurück auf Original, bytegleich");
        bk1.SaveDirty(ws1);
        Ok(bk1.LastFailures == 0, "gespeichert");
        var ws1b = Workspace.Open(game);
        Ok(!RageFaces.DiskHasWrongSize(ws1b) && Bin.Equal(ws1b.AngEFace.Disk, origE), "nach Neuladen: Platte = Original");

        // Fall 2: eigenes Bild (128 im Backup-Ordner gespeichert) -> 64x64, Kopf wie Original
        File.WriteAllBytes(ws.AngEFace.FullPath, Big(origE));
        var ws2 = Workspace.Open(game);
        var bk2 = new BackupService(ws2.GameRoot, bkRoot); bk2.EnsureBackups(ws2.AllFiles, null);
        var faces2 = new RageFaces(bk2.Dir);
        var src = Avatars.BuildGallery(ws2).First(g => g.CharId == 1).Image;
        faces2.SetCustom(3, true, RageFaces.Compose(ws2, 3, src));
        faces2.WriteToGame(ws2, 3);
        var d = ws2.AngEFace.Data;
        Ok(d.Length == 16512 && Dds.Check(d) == null && Dds.SizeOf(d).Width == 64, "eigenes Bild: 64x64, 16.512 Byte, gültig");
        Ok(d.Take(128).SequenceEqual(origE.Take(128)), "DDS-Kopf bytegleich mit Original");
        Ok(!Bin.Equal(d, origE), "Bildinhalt ist das eigene Bild");
        bk2.SaveDirty(ws2); faces2.Flush();
        Ok(bk2.LastFailures == 0 && new FileInfo(ws2.AngEFace.FullPath).Length == 16512, "gespeichert, Datei 16.512 Byte");

        // Fall 3: kein Backup vorhanden, Platte schon 128 -> trotzdem 64
        File.WriteAllBytes(ws.AngEFace.FullPath, Big(origE));
        var ws3 = Workspace.Open(game);
        Ok(ws3.AngEFace.Original == null, "Fall ohne Backup");
        new RageFaces(null).WriteToGame(ws3, 3);
        Ok(ws3.AngEFace.Data.Length == 16512 && Dds.SizeOf(ws3.AngEFace.Data).Width == 64 && ws3.AngEFace.Data.Take(128).SequenceEqual(origE.Take(128)), "ohne Backup: auf 64x64 zurückgerechnet, Kopf wie Original");

        Console.WriteLine(fails == 0 ? "ALLE PRÜFUNGEN BESTANDEN" : fails + " FEHLER");
        return fails == 0 ? 0 : 1;
    }
}
