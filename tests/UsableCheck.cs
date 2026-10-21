using System;
using System.Linq;
using G2Modifier.Core;

// Prueft ItemUsable an den echten item.bin (nur im Speicher). Aufruf: UsableCheck <Spielordner> <ae|hd>
static class UsableCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] a)
    {
        var ws = Workspace.Open(a[0], a[1] == "ae" ? GameVersion.Anniversary : GameVersion.HdRemaster);
        var t = ws.Table("ITEM.BIN");
        var files = t.Variants.Where(v => v.Valid).Select(v => v.Files[0]).ToList();
        foreach (var f in files) f.Original = (byte[])f.Disk.Clone();   // Test ohne Backup: Plattenstand gilt als Original
        var orig = files.Select(f => (byte[])f.Data.Clone()).ToList();
        Func<string, int> idx = n => t.Variants[0].Records.First(r => FixedText.Decode(r.File.Data, r.Index * Formats.ItemSize, 18).Trim() == n).Index;
        // erste drei Waffen (Typ 4) ohne eigene Verwendung
        var weapons = Enumerable.Range(0, Formats.ItemCount).Where(i => files[0].Data[i * Formats.ItemSize + 58] == 4 && ItemUsable.CanAssign(files[0], i)).Take(3).ToArray();
        int w1 = weapons[0], w2 = weapons[1], w3 = weapons[2];
        Console.WriteLine("Kopien: " + files.Count + ", Testwaffen " + string.Join(", ", weapons.Select(i => ItemUsable.DonorName(files[0], i)).ToArray()));

        Ok(ItemUsable.FreeDonors(ws).Count == 2, "2 freie Blöcke");
        Ok(ItemUsable.CanAssign(files[0], w1) && !ItemUsable.CanAssign(files[0], idx("Flame Staff")) && !ItemUsable.CanAssign(files[0], 1), "Waffe zuweisbar, Flame Staff (hat schon) und Debug Item nicht");
        Console.WriteLine("    " + ItemUsable.Assign(ws, w1));
        Console.WriteLine("    " + ItemUsable.Assign(ws, w2));
        Ok(ItemUsable.FreeDonors(ws).Count == 0, "danach 0 frei");
        Console.WriteLine("    " + ItemUsable.Assign(ws, w3));
        Ok(files.All(f => Bin.S32(f.Data, w3 * Formats.ItemSize + 72) == -1), "dritte Waffe bekommt keinen Block");
        foreach (var f in files)
        {
            var r1 = t.Variants.First(v => v.Files[0] == f).Records[w1];
            Ok(r1.Sections.Any(s => s.Def == Formats.Usable), f.RelPath + ": erste Testwaffe hat jetzt Abschnitt Verwendung");
            Ok(Bin.S32(f.Data, 1 * Formats.ItemSize + 72) == -1 && Bin.S32(f.Data, 2 * Formats.ItemSize + 72) == -1, f.RelPath + ": Debug-Items auf -1");
            Ok(f.Data.Length == f.Disk.Length && Formats.BuildTable(new GameFile { Data = f.Data, Disk = f.Data, FormatKey = "ITEM.BIN", RelPath = "x" }).Count == Formats.ItemCount, f.RelPath + ": Größe gleich, Datei weiter gültig");
            int changed = Enumerable.Range(0, f.Data.Length).Count(i => f.Data[i] != f.Disk[i]);
            Console.WriteLine("    geänderte Bytes: " + changed);
        }
        Console.WriteLine("    " + ItemUsable.Release(ws, w1));
        Console.WriteLine("    " + ItemUsable.Release(ws, w2));
        for (int i = 0; i < files.Count; i++) Ok(files[i].Data.SequenceEqual(orig[i]), files[i].RelPath + ": nach Entfernen bytegleich wie vorher");
        Ok(ItemUsable.FreeDonors(ws).Count == 2, "wieder 2 frei");
        Console.WriteLine(fails == 0 ? "ALLES OK" : fails + " FEHLER");
        return fails;
    }
}
