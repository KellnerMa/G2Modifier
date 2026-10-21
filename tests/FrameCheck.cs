using System;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Installation ohne sys\btl_sys1_2.dds: Rage-Gesichter muessen trotzdem funktionieren.
static class FrameCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] a)
    {
        string game = a[0], bkRoot = a[1];
        var ws = Workspace.Open(game);
        var bk = new BackupService(ws.GameRoot, bkRoot);
        bk.EnsureBackups(ws.AllFiles, null);
        Ok(RageFaces.BattleFile(ws, 3) == null, "Ausgangslage: sys\\btl_sys1_2.dds fehlt");
        var fr = RageFaces.FrameFor(ws, 3);
        Ok(fr != null && fr.Fallback && fr.Image.W == 128, "Rahmen trotzdem vorhanden: " + (fr == null ? "-" : fr.Source));
        var faces = new RageFaces(bk.Dir);
        var pv = faces.Preview(ws, 3, true);
        Ok(pv != null && pv.W == 64, "Vorschau Original-Rage-Gesicht (64x64)");
        var src = Avatars.BuildGallery(ws).First(g => g.CharId == 1).Image;
        faces.SetCustom(3, true, RageFaces.Compose(ws, 3, src));
        faces.WriteToGame(ws, 3);
        Ok(ws.AngEFace.Dirty && Dds.Check(ws.AngEFace.Data) == null && Dds.SizeOf(ws.AngEFace.Data).Width == 64 && ws.AngEFace.Data.Length == ws.AngEFace.Original.Length, "eigenes Bild: gültige 64x64-DDS, Größe wie Original");
        Ok(ws.AngEFace.Data.Take(128).SequenceEqual(ws.AngEFace.Original.Take(128)), "DDS-Kopf bytegleich mit Original");
        var rep = bk.SaveDirty(ws); faces.Flush();
        Ok(bk.LastFailures == 0, "gespeichert: " + string.Join(" | ", rep.ToArray()));
        var ws2 = Workspace.Open(game);
        new BackupService(ws2.GameRoot, bkRoot).EnsureBackups(ws2.AllFiles, null);
        Ok(ws2.AngEFace.Valid && Dds.SizeOf(ws2.AngEFace.Disk).Width == 64, "nach Neuladen gültig, 64x64");
        faces.Clear(3, true); faces.WriteToGame(ws2, 3);
        Ok(Bin.Equal(ws2.AngEFace.Data, ws2.AngEFace.Original), "Standard = Originaldatei bytegleich");
        Console.WriteLine(fails == 0 ? "ALLE PRÜFUNGEN BESTANDEN" : fails + " FEHLER");
        return fails == 0 ? 0 : 1;
    }
}
