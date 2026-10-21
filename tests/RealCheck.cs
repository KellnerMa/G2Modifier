using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using G2Modifier.Core;

// Prueft das Tool gegen eine KOPIE echter Spieldateien: Laden, Strukturpruefung, Roundtrip, Patch, Backup, Restore.
static class RealCheck
{
    static int Main(string[] args)
    {
        string game = args[0], backup = args[1];
        var ws = Workspace.Open(game);
        Console.WriteLine("Edition: " + ws.Edition + "   Dateien: " + ws.AllFiles.Count);
        foreach (var m in ws.Messages) Console.WriteLine("  Hinweis: " + m);
        foreach (var t in ws.Tables)
        {
            Console.WriteLine("== " + t.Title + " (" + t.Key + ")");
            foreach (var v in t.Variants)
            {
                int bad = v.Files.Count(f => !f.Valid);
                Console.WriteLine("   " + v.Description.PadRight(32) + " Dateien " + v.Files.Count + "  Einträge " + v.Records.Count + "  gesperrt " + bad);
                foreach (var f in v.Files.Where(f => !f.Valid).Take(3)) Console.WriteLine("      GESPERRT " + f.RelPath + ": " + f.Errors[0]);
                foreach (var f in v.Files.Take(1)) foreach (var n in f.Notes) Console.WriteLine("      Info: " + n);
            }
        }

        // Stichproben ueber die Tool-Schemata (nicht ueber Python)
        var items = ws.Table("ITEM.BIN").Variants.First(v => v.Name == "xls_data");
        var ice = items.Records[400];
        Console.WriteLine("\nStichprobe " + ice.Label);
        foreach (var s in ice.Sections)
            foreach (var f in s.Def.Fields.Where(x => x.Known).Take(12))
                Console.WriteLine("   [" + s.Title + "] " + f.Label + " = " + f.Format(ice.File.Data, s.Base + f.Offset) + "  " + f.Meaning(ice.File.Data, s.Base + f.Offset, ws));
        var enemy = ws.Table("ENEMY").Variants[0].Records.First(r => r.Label.StartsWith("Naga Queen"));
        Console.WriteLine("\nStichprobe " + enemy.Label);
        foreach (var src in new[] { "Level", "Health", "Vitality", "Experience", "GoldCoins", "Item1Offset", "Item2Offset", "Item1Chance" })
        {
            var f = Formats.EnemyStats.Get(src); var s = enemy.Sections[0];
            Console.WriteLine("   " + f.Label + " = " + f.Format(enemy.File.Data, s.Base + f.Offset) + "  " + f.Meaning(enemy.File.Data, s.Base + f.Offset, ws));
        }
        var hardNaga = ws.Table("ENEMY").Variants[1].Records.First(r => r.Label.StartsWith("Naga Queen"));
        Console.WriteLine("   Hard-Modus HP = " + Formats.EnemyStats.Get("Health").Format(hardNaga.File.Data, hardNaga.Sections[0].Base + 24));

        // Text-Roundtrip: jeder Name/Text decodieren und wieder encodieren muss bytegleich sein
        int textFields = 0, textBad = 0;
        foreach (var t in ws.Tables) foreach (var v in t.Variants) foreach (var r in v.Records) foreach (var s in r.Sections)
            foreach (var f in s.Def.Fields.Where(x => x.IsText))
            {
                int abs = s.Base + f.Offset; textFields++;
                byte[] enc;
                string err = FixedText.EncodeFresh(f.Format(r.File.Data, abs), r.File.Data, abs, f.Length, out enc);
                bool same = err == null;
                for (int i = 0; same && i < f.Length; i++) if (enc[i] != r.File.Data[abs + i]) same = false;
                if (!same) { textBad++; if (textBad <= 5) Console.WriteLine("   Text-Roundtrip abweichend: " + r.File.RelPath + " @0x" + abs.ToString("X") + " '" + f.Format(r.File.Data, abs) + "' " + err); }
            }
        // Echte Textaenderung: Ice Pick -> Ice Pick+ (rechtsbuendig, Leerzeichen davor)
        {
            var nf = Formats.Item.Get("Name"); var r = items.Records[400]; int abs = r.Sections[0].Base;
            var tmp = (byte[])r.File.Data.Clone();
            nf.TryWrite(tmp, abs, "Ice Pick+");
            Console.WriteLine("   Umbenennung Ice Pick -> Ice Pick+: " + Bin.Hex(tmp, abs, 18));
            var df = Formats.Item.Get("Description");
            df.TryWrite(tmp, abs + 18, "+60 ATK  Blizzard effect");
            Console.WriteLine("   Neue Beschreibung: " + Bin.Hex(tmp, abs + 18, 40));
        }
        Console.WriteLine("\nText-Roundtrip: " + textFields + " Textfelder, " + textBad + " abweichend");

        // Zahlen-Roundtrip: jedes Zahlenfeld lesen und unveraendert zurueckschreiben -> Datei identisch
        int numFields = 0;
        foreach (var f in ws.AllFiles.Where(x => x.Valid))
        {
            var before = (byte[])f.Data.Clone();
            foreach (var t in ws.Tables) foreach (var v in t.Variants) foreach (var r in v.Records.Where(r => r.File == f)) foreach (var s in r.Sections)
                foreach (var fd in s.Def.Fields.Where(x => !x.IsText)) { fd.TryWrite(f.Data, s.Base + fd.Offset, fd.Format(f.Data, s.Base + fd.Offset)); numFields++; }
            if (!Bin.Equal(before, f.Data)) Console.WriteLine("   Zahlen-Roundtrip verändert " + f.RelPath);
        }
        Console.WriteLine("Zahlen-Roundtrip: " + numFields + " Felder geschrieben, Dateien verändert: " + ws.DirtyFiles.Count());

        // Ende-zu-Ende: Backup, Preis Ice Pick in allen Kopien +1, Naga HP x2, speichern, pruefen, wiederherstellen
        var bk = new BackupService(game, backup);
        int created = bk.EnsureBackups(ws.AllFiles, null);
        Console.WriteLine("\nBackups angelegt: " + created + ", Probleme: " + bk.Problems.Count);
        var price = Formats.Item.Get("Stats.Price");
        foreach (var v in ws.Table("ITEM.BIN").Variants)
        {
            var r = v.Records[400]; int abs = r.Sections[0].Base + price.Offset;
            price.TryWrite(r.File.Data, abs, (price.ReadNumber(r.File.Data, abs) + 1).ToString());
        }
        Batch.Scale(new[] { enemy }, new List<Batch.ScaleRule> { new Batch.ScaleRule(Formats.EnemyStats, "Health", 2.0) });
        var rep = bk.SaveDirty(ws);
        foreach (var l in rep) Console.WriteLine("   " + l);
        foreach (var f in ws.AllFiles.Where(x => !Bin.Equal(x.Original, File.ReadAllBytes(x.FullPath))))
        {
            var diffs = GameFile.DiffRanges(f.Original, File.ReadAllBytes(f.FullPath));
            Console.WriteLine("   Platte vs Original " + f.RelPath + ": " + string.Join(", ", diffs.Select(d => "0x" + d[0].ToString("X") + "+" + d[1]).ToArray()));
        }
        int restored = 0;
        foreach (var f in ws.AllFiles.Where(x => x.DiskState == "verändert")) { if (bk.Restore(f) == null) restored++; }
        bool allOrig = ws.AllFiles.All(f => Bin.Equal(File.ReadAllBytes(f.FullPath), f.Original));
        Console.WriteLine("Wiederhergestellt: " + restored + ", alles bytegleich Original: " + allOrig);
        return allOrig && textBad == 0 ? 0 : 1;
    }
}
