using System;
using System.Linq;
using G2Modifier.Core;
using G2Modifier.UI;

// Prueft das Bearbeiten von sys_hardmode\ms_param.bin (Platzhalter 30 F0) an den echten Dateien, nur im Speicher.
// Aufruf: HardCheck <Spielordner> <ae|hd>
static class HardCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] a)
    {
        var ws = Workspace.Open(a[0], a[1] == "ae" ? GameVersion.Anniversary : GameVersion.HdRemaster);
        Console.WriteLine(ws.Edition);
        var t = ws.Table("MS_PARAM.BIN");
        var sys = t.Variants.First(v => v.Name == "sys");
        var hard = t.Variants.First(v => v.Name == "sys_hardmode");
        foreach (var v in t.Variants) v.Files[0].Original = (byte[])v.Files[0].Disk.Clone();
        var hf = hard.Files[0]; var sf = sys.Files[0];
        Func<string, FieldDef> F = n => Formats.Move.Get(n);
        var cost = F("Stats.Cost"); var name = F("Name"); var desc = F("Description");
        var teff = F("Stats.TargetEffect"); var ttyp = F("Stats.TargetType");

        // Anzeige: kein Platzhalter sichtbar
        int shownFiller = 0;
        foreach (var r in hard.Records)
            foreach (var f in Formats.Move.Fields)
            {
                string s = r.ViewFormat(0, f);
                if (s.Contains("{F0}") || s == "-4048") shownFiller++;
                if (f.SourceName == "Stats.Animation" && r.TextFrom != null && s != r.TextFrom.ViewFormat(0, f)) shownFiller++;
            }
        Ok(shownFiller == 0, "keine Platzhalter in der Anzeige (" + shownFiller + ")");
        Ok(hard.Records.All(r => !r.Label.Contains("Platzhalter") && !r.Label.Contains("{F0}")), "Listennamen ohne Platzhalter");
        var bf = hard.Records[2];
        Ok(bf.UsesNormal(0, cost) && bf.ViewFormat(0, cost) == sys.Records[2].ViewFormat(0, cost), "Burnflame Kosten zeigt Normal-Wert " + bf.ViewFormat(0, cost));
        Ok(Layouts.Name(bf) == Layouts.Name(sys.Records[2]), "Burnflame Name in Hard = " + Layouts.Name(bf));

        // 1) Hard-Wert aendern ohne Kopien: nur Hard-Datei, echter Wert, Nachbarn unveraendert angezeigt
        var wHard = new FieldWriter(ws, t, () => false);
        string info;
        string before = bf.ViewFormat(0, cost);
        var snapView = Formats.Move.Fields.ToDictionary(f => f.SourceName, f => bf.ViewFormat(0, f));
        Ok(wHard.Write(bf, 0, cost, "77", out info) == null && bf.ViewFormat(0, cost) == "77" && !bf.UsesNormal(0, cost), "Hard Kosten 77 geschrieben (" + info + ")");
        Ok(Bin.S16(hf.Data, 2 * 108 + 20) == 77, "Bytes in sys_hardmode = 77");
        Ok(sf.Data.SequenceEqual(sf.Disk), "sys unverändert (Kopien aus)");
        Ok(Formats.Move.Fields.Where(f => f != cost).All(f => bf.ViewFormat(0, f) == snapView[f.SourceName]), "alle anderen Felder zeigen weiter dasselbe");

        // 2) U8-Paar (Zielwirkung + Zieltyp teilen ein 30-F0-Paar)
        var pr = hard.Records.FirstOrDefault(r => r.UsesNormal(0, teff) && r.UsesNormal(0, ttyp) && r.TextFrom != null);
        if (pr != null)
        {
            string effBefore = pr.ViewFormat(0, teff);
            string newType = pr.ViewFormat(0, ttyp) == "4" ? "5" : "4";
            Ok(wHard.Write(pr, 0, ttyp, newType, out info) == null && pr.ViewFormat(0, ttyp) == newType, pr.Label + ": Zieltyp " + newType);
            Ok(pr.ViewFormat(0, teff) == effBefore && !pr.UsesNormal(0, teff), "Zielwirkung zeigt weiter " + effBefore + " (Normal-Wert eingesetzt, nicht 48)");
        }
        else Console.WriteLine("    (kein Datensatz mit Platzhalter-Paar Zielwirkung/Zieltyp)");

        // 3) Gleicher Wert wie angezeigt: nichts aendern, Platzhalter bleibt
        var r3 = hard.Records.First(r => r != bf && r != pr && r.UsesNormal(0, cost));
        var keep = (byte[])hf.Data.Clone();
        Ok(wHard.Write(r3, 0, cost, r3.ViewFormat(0, cost), out info) == null && info == null && hf.Data.SequenceEqual(keep), "gleicher Wert: Datei unverändert");
        Ok(wHard.Write(r3, 0, cost, "0" + r3.ViewFormat(0, cost), out info) == null && hf.Data.SequenceEqual(keep), "gleicher Wert mit führender 0: Datei unverändert");
        Ok(wHard.Write(r3, 0, cost, "abc", out info) != null && hf.Data.SequenceEqual(keep), "ungültige Eingabe: Fehler, Datei unverändert");

        // 4) Name in Hard aendern (Kopien an): Hard bekommt echten Text, sys und xls_data auch
        var wAll = new FieldWriter(ws, t, () => true);
        Ok(wAll.Write(bf, 0, name, "Burnflame X", out info) == null, "Name in Hard geändert (" + info + ")");
        Ok(t.Variants.All(v => v.Records[2].ViewFormat(0, name) == "Burnflame X"), "alle Kopien zeigen \"Burnflame X\"");
        Ok(!Bin.IsFillerText(hf.Data, 2 * 108 + 2, 18), "sys_hardmode enthält jetzt echten Namen");
        Ok(wAll.Write(bf, 0, desc, "Hard mode text", out info) == null && bf.ViewFormat(0, desc) == "Hard mode text" && sys.Records[2].ViewFormat(0, desc) == "Hard mode text", "Beschreibung in Hard geändert, sys mitgezogen");

        // 5) In sys aendern (Kopien an): Hard-Platzhalter bleiben Bytes, zeigen aber den neuen Wert
        var r5 = sys.Records.First(r => hard.Records[r.Index].UsesNormal(0, cost) && hard.Records[r.Index] != r3);
        var h5 = hard.Records[r5.Index];
        var hardBytes = (byte[])hf.Data.Clone();
        Ok(wAll.Write(r5, 0, cost, "123", out info) == null, "sys " + r5.Label + " Kosten 123 (" + info + ")");
        Ok(hf.Data.SequenceEqual(hardBytes), "sys_hardmode-Bytes unverändert");
        Ok(h5.ViewFormat(0, cost) == "123", "Hard zeigt 123 (übernimmt Normal-Wert)");
        Ok(info != null && !info.Contains("wichen"), "keine Meldung \"wichen ab\" für übernommene Werte");

        // 6) Hard-Wert, der wirklich abweicht, bleibt beim Aendern in sys
        var r6 = Enumerable.Range(0, sys.Records.Count).FirstOrDefault(i => !hard.Records[i].UsesNormal(0, F("Stats.IpDamage")) && hard.Records[i].ViewFormat(0, F("Stats.IpDamage")) != sys.Records[i].ViewFormat(0, F("Stats.IpDamage")));
        if (r6 > 0)
        {
            string hv = hard.Records[r6].ViewFormat(0, F("Stats.IpDamage"));
            wAll.Write(sys.Records[r6], 0, F("Stats.IpDamage"), "999", out info);
            Ok(hard.Records[r6].ViewFormat(0, F("Stats.IpDamage")) == hv, "eigener Hard-Wert IP-Schaden bleibt " + hv + " (" + info + ")");
        }

        // 6b) Animation mit F0-Markierung: zeigt Normal-Wert, Aendern schreibt echten Wert
        var anim = F("Stats.Animation");
        var ra = hard.Records.FirstOrDefault(r => r.UsesNormal(0, anim) && Bin.U16(r.File.Data, r.Sections[0].Base + 36) >> 8 == 0xF0);
        if (ra != null)
        {
            Ok(ra.ViewFormat(0, anim) == ra.TextFrom.ViewFormat(0, anim), ra.Label + ": Animation zeigt " + ra.ViewFormat(0, anim) + " (wie Normal)");
            Ok(wHard.Write(ra, 0, anim, "7", out info) == null && ra.ViewFormat(0, anim) == "7" && Bin.U16(ra.File.Data, ra.Sections[0].Base + 36) == 7, "Animation 7 geschrieben, Bytes 07 00");
        }
        else Ok(false, "Datensatz mit Animation-Markierung F0 gefunden");

        // 7) Zuruecksetzen auf Original: bytegleich
        foreach (var v in t.Variants) Buffer.BlockCopy(v.Files[0].Original, 0, v.Files[0].Data, 0, v.Files[0].Data.Length);
        Ok(t.Variants.All(v => v.Files[0].Data.SequenceEqual(v.Files[0].Disk)) && bf.UsesNormal(0, cost) && bf.UsesNormal(0, name), "nach Zurücksetzen bytegleich, Platzhalter wieder da");

        Console.WriteLine(fails == 0 ? "ALLES OK" : fails + " FEHLER");
        return fails;
    }
}
