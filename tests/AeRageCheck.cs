using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Prueft RagePatchAE gegen eine echte grandia2.exe der Anniversary Edition (nur im Speicher, schreibt nichts).
// Aufruf: AeRageCheck <grandia2.exe>
//  - alle Stellen erkannt, Original wird richtig gelesen
//  - jede Einstellung (und Kombinationen) wird gelesen wie geschrieben, nur erwartete Bytes aendern sich
//  - zurueck auf Original ergibt bytegleiche Exe
//  - ASLR: an jeder Relocation steht nach dem Patch weiter eine Adresse im Image
static class AeRageCheck
{
    static int fails = 0;
    static void Ok(bool c, string what) { Console.WriteLine((c ? "  OK    " : "  FEHLER ") + what); if (!c) fails++; }

    static int Main(string[] args)
    {
        var orig = File.ReadAllBytes(args[0]);
        RagePatch.Anniversary = true;

        Console.WriteLine("== Erkennen");
        string err = RagePatch.Validate(orig);
        Ok(err == null, "alle Stellen passen (" + (err ?? "-") + ")");
        if (err != null) return 1;
        string warn;
        var cur = RagePatch.Read(orig, out warn);
        Ok(warn == null, "keine Widersprüche (" + (warn ?? "-") + ")");
        Ok(cur.SameAs(new RageSettings()) && cur.ElenaLock, "Original: an, Elena-Sperre, Millenia, 1200, Schwelle 100, Wut aus Spielstand, Chance Original, keine Mindestwut");

        var allowed = new HashSet<int>();
        foreach (var s in RagePatchAE.IdSites) allowed.Add(s[0] + 3);
        allowed.Add(RagePatchAE.LockInit); allowed.Add(RagePatchAE.LockInit + 1); allowed.Add(RagePatchAE.ElenaImm);
        for (int i = 0; i < 4; i++) { allowed.Add(RagePatchAE.DurationSet + 6 + i); allowed.Add(RagePatchAE.DurationCmp + 6 + i); allowed.Add(RagePatchAE.StartWrathSite + 4 + i); }
        for (int i = 0; i < 9; i++) { allowed.Add(RagePatchAE.StartIndex + i); allowed.Add(RagePatchAE.EndIndex + i); }
        foreach (var o in RagePatchAE.LockReadSites) for (int i = 2; i < 6; i++) allowed.Add(o + i);
        foreach (var o in RagePatchAE.ThresholdSites) for (int i = 3; i < 7; i++) allowed.Add(o + i);
        foreach (var o in RagePatchAE.ChanceSites) for (int i = 6; i < 10; i++) allowed.Add(o + i);
        for (int i = 0; i < 2; i++) allowed.Add(RagePatchAE.MinStartJne + i);
        for (int i = 0; i < RagePatchAE.MinBodySize; i++) allowed.Add(RagePatchAE.MinStartBody + i);
        for (int i = 0; i < 5; i++) allowed.Add(RagePatchAE.MinEnd + i);
        for (int i = 0; i < RagePatchAE.CaveSize; i++) allowed.Add(RagePatchAE.Cave + i);

        var relocs = Relocs(orig);
        Console.WriteLine("Relocations: " + relocs.Count);

        var cases = new List<RageSettings>();
        foreach (var en in new[] { true, false })
            foreach (var el in new[] { true, false })
                cases.Add(new RageSettings { Enabled = en, ElenaLock = el });
        foreach (var t in RagePatch.ThresholdValues) cases.Add(new RageSettings { Threshold = t });
        for (int i = 0; i < 3; i++) cases.Add(new RageSettings { StartWrath = i });
        for (int i = 0; i < 4; i++) cases.Add(new RageSettings { Chance = i });
        cases.Add(new RageSettings { MinWrathOne = true });
        cases.Add(new RageSettings { Duration = 3600 });
        cases.Add(new RageSettings { Enabled = true, ElenaLock = false, Threshold = 0, StartWrath = 2, Chance = 3, MinWrathOne = true, Duration = 2400 });
        cases.Add(new RageSettings { Enabled = true, ElenaLock = false, Threshold = 30, StartWrath = 1, Chance = 1, MinWrathOne = true });
        cases.Add(new RageSettings { CharId = 1 });

        Console.WriteLine("\n== Schreiben und lesen (" + cases.Count + " Fälle)");
        foreach (var s in cases)
        {
            var exe = (byte[])orig.Clone();
            RagePatch.Write(exe, s);
            string name = string.Format("an={0} elena={1} figur={2} dauer={3} schwelle={4} start={5} chance={6} min1={7}", s.Enabled, s.ElenaLock, s.CharId, s.Duration, s.Threshold, s.StartWrath, s.Chance, s.MinWrathOne);
            Ok(RagePatch.Validate(exe) == null, name + ": gültig");
            string w; var back = RagePatch.Read(exe, out w);
            Ok(back.SameAs(s) && w == null, name + ": gelesen wie geschrieben" + (w != null ? " (" + w.Trim() + ")" : ""));
            var bad = Enumerable.Range(0, exe.Length).Where(i => exe[i] != orig[i] && !allowed.Contains(i)).ToList();
            Ok(bad.Count == 0, name + ": nur erwartete Bytes" + (bad.Count > 0 ? " (fremd: 0x" + bad[0].ToString("X") + ")" : ""));
            int relocBad = 0;
            foreach (var r in relocs)
            {
                if (Enumerable.Range(r, 4).All(i => exe[i] == orig[i])) continue;
                uint v = BitConverter.ToUInt32(exe, r);
                if (v < 0x400000 || v >= 0x27F0000) relocBad++;
            }
            Ok(relocBad == 0, name + ": Relocations zeigen weiter ins Image");
            RagePatch.Write(exe, new RageSettings());
            Ok(exe.SequenceEqual(orig), name + ": zurück auf Original bytegleich");
        }

        Console.WriteLine("\n== HD-Stellen unberührt");
        RagePatch.Anniversary = false;
        Ok(RagePatch.Validate(orig) != null, "HD-Prüfung lehnt die AE-Exe ab (" + RagePatch.Validate(orig) + ")");
        RagePatch.Anniversary = true;

        Console.WriteLine(fails == 0 ? "\nALLES OK" : "\n" + fails + " FEHLER");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Dateioffsets aller HIGHLOW-Relocations (Typ 3).</summary>
    static List<int> Relocs(byte[] d)
    {
        var res = new List<int>();
        int pe = BitConverter.ToInt32(d, 0x3C), nsec = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
        uint baseVa = BitConverter.ToUInt32(d, pe + 24 + 28);
        var secs = new List<uint[]>();
        for (int i = 0; i < nsec; i++)
        {
            int h = pe + 24 + opt + i * 40;
            secs.Add(new[] { BitConverter.ToUInt32(d, h + 12), Math.Max(BitConverter.ToUInt32(d, h + 8), BitConverter.ToUInt32(d, h + 16)), BitConverter.ToUInt32(d, h + 20) });
        }
        Func<uint, int> off = rva => { foreach (var s in secs) if (rva >= s[0] && rva < s[0] + s[1]) return (int)(rva - s[0] + s[2]); return -1; };
        uint rrva = BitConverter.ToUInt32(d, pe + 24 + 96 + 40), size = BitConverter.ToUInt32(d, pe + 24 + 96 + 44);
        int o = off(rrva), end = o + (int)size;
        while (o < end)
        {
            uint page = BitConverter.ToUInt32(d, o), bs = BitConverter.ToUInt32(d, o + 4);
            if (bs == 0) break;
            for (int i = 8; i < bs; i += 2)
            {
                int e = BitConverter.ToUInt16(d, o + i);
                if ((e >> 12) == 3) { int f = off(page + (uint)(e & 0xFFF)); if (f >= 0) res.Add(f); }
            }
            o += (int)bs;
        }
        return res;
    }
}
