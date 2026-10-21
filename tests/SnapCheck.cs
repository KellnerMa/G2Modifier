using System;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Prueft die optionalen Zwischenstaende gegen eine KOPIE echter Spieldateien.
static class SnapCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] a)
    {
        string game = a[0], bkRoot = a[1];
        var ws = Workspace.Open(game);
        var bk = new BackupService(ws.GameRoot, bkRoot);
        bk.EnsureBackups(ws.AllFiles, null);
        var exe = ws.Exe;
        var A = (byte[])exe.Disk.Clone();

        Console.WriteLine("== aus");
        RagePatch.Apply(ws, new RageSettings { Chance = 1 });
        bk.SaveDirty(ws);
        Ok(!Directory.Exists(bk.SnapshotRoot) && bk.LastFailures == 0, "Haken aus: kein Ordner zwischenstaende, gespeichert");
        var B = (byte[])exe.Disk.Clone();

        Console.WriteLine("== an");
        bk.KeepSnapshots = true;
        RagePatch.Apply(ws, new RageSettings { Chance = 2 });
        var r1 = bk.SaveDirty(ws);
        var C = (byte[])exe.Disk.Clone();
        var snaps = bk.ListSnapshots();
        Ok(snaps.Count == 1 && snaps[0].Entries.Count == 1 && snaps[0].Entries[0][0] == exe.RelPath, "1 Zwischenstand mit genau der gespeicherten Datei (" + string.Join(" | ", r1.ToArray()) + ")");
        var inSnap = File.ReadAllBytes(Path.Combine(snaps[0].Path, exe.RelPath.Replace('\\', Path.DirectorySeparatorChar)));
        Ok(Bin.Equal(inSnap, B) && !Bin.Equal(inSnap, C), "Inhalt = Stand VOR dem Speichern");
        Ok(Bin.Equal(File.ReadAllBytes(exe.FullPath), C), "Spieldatei = neuer Stand");
        Ok(Bin.Equal(File.ReadAllBytes(Path.Combine(bk.Dir, exe.RelPath.Replace('\\', Path.DirectorySeparatorChar))), A), "Original-Backup unverändert");

        System.Threading.Thread.Sleep(1100);
        RagePatch.Apply(ws, new RageSettings { Chance = 3 });
        bk.SaveDirty(ws);
        var D = (byte[])exe.Disk.Clone();
        snaps = bk.ListSnapshots();
        Ok(snaps.Count == 2 && snaps[0].Name.CompareTo(snaps[1].Name) > 0, "2 Zwischenstände, neuester oben: " + string.Join(", ", snaps.Select(s => s.Name).ToArray()));

        Console.WriteLine("== zurückspielen");
        int f;
        var rep = bk.RestoreSnapshot(ws, snaps[0], out f);   // Stand vor dem letzten Speichern = C
        Ok(f == 0 && Bin.Equal(File.ReadAllBytes(exe.FullPath), C) && Bin.Equal(exe.Data, C) && !exe.Dirty, "Platte und Tool wieder auf Stand vor dem letzten Speichern");
        var after = bk.ListSnapshots();
        var pre = File.ReadAllBytes(Path.Combine(after[0].Path, exe.RelPath.Replace('\\', Path.DirectorySeparatorChar)));
        Ok(after.Count == 3 && Bin.Equal(pre, D), "vorher wurde der aktuelle Stand selbst gesichert (Zurückspielen umkehrbar)");
        string w;
        Ok(RagePatch.Current(ws, out w).Chance == 2, "RagePatch liest den zurückgespielten Stand (Chance 75 %)");

        Console.WriteLine("== Grenze");
        Directory.CreateDirectory(Path.Combine(bk.SnapshotRoot, "eigener-ordner"));
        File.WriteAllText(Path.Combine(bk.SnapshotRoot, "eigener-ordner", "x.txt"), "bleibt");
        bk.SnapshotLimit = 3;
        for (int i = 0; i < 4; i++) { System.Threading.Thread.Sleep(1100); RagePatch.Apply(ws, new RageSettings { Chance = i % 2 == 0 ? 0 : 1 }); bk.SaveDirty(ws); }
        Ok(bk.ListSnapshots().Count == 3, "nur die letzten 3 behalten");
        Ok(File.Exists(Path.Combine(bk.SnapshotRoot, "eigener-ordner", "x.txt")), "fremder Ordner ohne zwischenstand.tsv bleibt unangetastet");

        Console.WriteLine("== Fehlerfall");
        var before = File.ReadAllBytes(exe.FullPath);
        var root = bk.SnapshotRoot;
        Directory.Move(root, root + "_weg");
        File.WriteAllText(root, "blockiert");          // Ordner kann nicht angelegt werden
        RagePatch.Apply(ws, new RageSettings { Chance = 3 });
        var rf = bk.SaveDirty(ws);
        Ok(bk.LastFailures > 0 && Bin.Equal(File.ReadAllBytes(exe.FullPath), before) && exe.Dirty, "Zwischenstand scheitert: nichts geschrieben, Änderung bleibt offen (" + rf.Last() + ")");
        File.Delete(root); Directory.Move(root + "_weg", root);
        rf = bk.SaveDirty(ws);
        Ok(bk.LastFailures == 0 && !exe.Dirty, "danach normal gespeichert");

        Console.WriteLine("== Original wiederherstellen");
        Ok(bk.Restore(exe) == null && Bin.Equal(File.ReadAllBytes(exe.FullPath), A), "Original wieder bytegleich");
        Console.WriteLine(File.ReadAllText(bk.LogPath).Split('\n').Where(l => l.Contains("ZWISCHENSTAND") || l.Contains("ZURÜCK")).Take(3).Aggregate("", (x, y) => x + "   " + y.Trim() + "\n"));
        Console.WriteLine(fails == 0 ? "ALLE PRÜFUNGEN BESTANDEN" : fails + " FEHLER");
        return fails == 0 ? 0 : 1;
    }
}
