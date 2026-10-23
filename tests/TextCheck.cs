using System;
using System.Linq;
using G2Modifier.Core;
using G2Modifier.UI;

// Prueft Item-Beschreibungen und sys_hardmode\ms_param.bin an den echten Dateien (nur im Speicher). Aufruf: TextCheck <Spielordner> <ae|hd>
static class TextCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] a)
    {
        var ws = Workspace.Open(a[0], a[1] == "ae" ? GameVersion.Anniversary : GameVersion.HdRemaster);
        Console.WriteLine(ws.Edition);

        var mt = ws.Table("MS_PARAM.BIN");
        foreach (var v in mt.Variants)
            Ok(v.Valid, v.Description + " gültig" + (v.Valid ? "" : ": " + v.Files[0].Errors[0]) + "  ·  " + string.Join(" | ", v.Files[0].Notes.ToArray()));
        var hard = mt.Variants.FirstOrDefault(v => v.Name == "sys_hardmode");
        if (hard != null) Console.WriteLine("    hardmode #1: " + hard.Records[1].Label + "   #5: " + hard.Records[5].Label);

        var t = ws.Table("ITEM.BIN");
        foreach (var v in t.Variants) v.Files[0].Original = (byte[])v.Files[0].Disk.Clone();
        var w = new FieldWriter(ws, t, () => true);
        var desc = Formats.Item.Get("Description");
        Ok(t.Variants[0].Name == "sys", "erste Kopie ist sys (Text, den das Spiel zeigt)");
        int item = 121;   // Beschreibung in xls_data anders als in sys ("Revives character |right to COM")
        foreach (var v in t.Variants) Console.WriteLine("    vorher " + v.Name + ": \"" + desc.Format(v.Files[0].Data, item * Formats.ItemSize + 18) + "\"");
        var rec = t.Variants[0].Records[item];
        foreach (var input in new[] { "Changed text", "A much longer description than the original!", "Exactly forty characters long text here.", "Short" })
        {
            string info;
            string err = w.Write(rec, 0, desc, input, out info);
            Console.WriteLine("    \"" + input + "\" (" + input.Length + ") -> " + (err ?? info));
            foreach (var v in t.Variants)
            {
                var f = v.Files[0];
                int abs = item * Formats.ItemSize + 18;
                if (err == null) Ok(desc.Format(f.Data, abs) == input, f.RelPath + " enthält den neuen Text");
            }
        }
        // Zahlen in fremden Kopien nur bei gleichem Vorwert, nichts ausser Beschreibung und Name geaendert
        foreach (var v in t.Variants)
        {
            var f = v.Files[0];
            int changed = Enumerable.Range(0, f.Data.Length).Count(i => f.Data[i] != f.Disk[i] && !(i >= item * Formats.ItemSize + 18 && i < item * Formats.ItemSize + 58));
            Ok(changed == 0, f.RelPath + ": sonst keine Bytes geändert");
        }

        // Move-Beschreibung: sys und xls_data bekommen den Text, Platzhalter in sys_hardmode bleiben
        var mw = new FieldWriter(ws, mt, () => true);
        var mdesc = Formats.Move.Get("Description");
        var hardBefore = hard == null ? null : (byte[])hard.Files[0].Data.Clone();
        string minfo;
        string merr = mw.Write(mt.Variants[0].Records[1], 0, mdesc, "Fire attack test", out minfo);
        Console.WriteLine("    Move 1: " + (merr ?? minfo));
        foreach (var v in mt.Variants.Where(x => x != hard)) Ok(mdesc.Format(v.Files[0].Data, 108 + 68) == "Fire attack test", v.Description + " hat neuen Move-Text");
        if (hard != null) Ok(hard.Files[0].Data.SequenceEqual(hardBefore), "sys_hardmode\\ms_param.bin unverändert (Platzhalter bleiben)");
        Console.WriteLine(fails == 0 ? "ALLES OK" : fails + " FEHLER");
        return fails;
    }
}
