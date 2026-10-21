using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace G2Modifier.Core
{
    /// <summary>Massenbearbeitung, z. B. alle Gegner um Faktor X staerker.</summary>
    public static class Batch
    {
        public class ScaleRule
        {
            public RecordDef Def;
            public string SourceName;
            public double Factor;
            public long Cap = long.MaxValue;   // Obergrenze unterhalb des Feldtyps (z. B. 100 bei Prozentwerten)
            public ScaleRule(RecordDef def, string src, double factor) { Def = def; SourceName = src; Factor = factor; }
        }

        public class ScaleResult { public int Changed, Clamped, Records; }

        public static ScaleResult Scale(IEnumerable<Record> records, IList<ScaleRule> rules)
        {
            var res = new ScaleResult();
            foreach (var r in records)
            {
                res.Records++;
                foreach (var s in r.Sections)
                    foreach (var rule in rules)
                    {
                        if (rule.Def != s.Def || Math.Abs(rule.Factor - 1.0) < 1e-9) continue;
                        var f = s.Def.Get(rule.SourceName);
                        int abs = s.Base + f.Offset;
                        long old = f.ReadNumber(r.File.Data, abs);
                        if (old <= 0) continue; // 0 bleibt 0, negative Werte (Sonderwerte) bleiben unangetastet
                        long nv = (long)Math.Round(old * rule.Factor, MidpointRounding.AwayFromZero);
                        long max = Math.Min(f.Max, rule.Cap);
                        if (nv > max) { nv = Math.Max(old, max); res.Clamped++; }
                        if (nv < 1) nv = 1;
                        if (nv != old) { f.WriteNumber(r.File.Data, abs, nv); res.Changed++; }
                    }
            }
            return res;
        }

        /// <summary>
        /// Level-up-Kurve: Zuwachs je Stufe mit einem Faktor multiplizieren, der von Stufe "from" (fStart)
        /// bis Stufe "to" (fEnd) linear verlaeuft. 100 % = unveraendert. 0 bleibt 0, Grenzen des Feldtyps werden eingehalten.
        /// records: Figuren (Datensaetze aus TB_LVUP.BIN), Abschnitte = Stufen 1..99.
        /// </summary>
        public static ScaleResult LevelCurve(IEnumerable<Record> records, IList<string> sourceNames, int from, int to, double fStart, double fEnd)
        {
            var res = new ScaleResult();
            foreach (var r in records)
            {
                res.Records++;
                for (int l = from; l <= to && l <= r.Sections.Count; l++)
                {
                    var s = r.Sections[l - 1];
                    double t = to > from ? (double)(l - from) / (to - from) : 0;
                    double f = fStart + (fEnd - fStart) * t;
                    foreach (var src in sourceNames)
                    {
                        var fd = s.Def.Get(src);
                        int abs = s.Base + fd.Offset;
                        long old = fd.ReadNumber(r.File.Data, abs);
                        if (old == 0) continue;
                        long nv = (long)Math.Round(old * f, MidpointRounding.AwayFromZero);
                        if (old > 0 && nv < 1 && f > 0) nv = 1;
                        if (nv > fd.Max) { nv = fd.Max; res.Clamped++; }
                        if (nv < fd.Min) { nv = fd.Min; res.Clamped++; }
                        if (nv != old) { fd.WriteNumber(r.File.Data, abs, nv); res.Changed++; }
                    }
                }
            }
            return res;
        }

        /// <summary>CSV-Export einer Tabelle (alle Felder, Semikolon getrennt, fuer Excel).</summary>
        public static void ExportCsv(Variant v, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", new[] { L.T("Datensatz"), L.T("Datei"), L.T("Abschnitt"), L.T("Feld"), L.T("Wert"), L.T("Original"), "Offset", L.T("Typ"), L.T("Quelle-Feldname") }));
            foreach (var r in v.Records)
                foreach (var s in r.Sections)
                    foreach (var f in s.Def.Fields)
                    {
                        int abs = s.Base + f.Offset;
                        string orig = r.File.Original != null ? f.Format(r.File.Original, abs) : "";
                        sb.AppendLine(string.Join(";", new[] { Csv(r.Label), Csv(r.File.RelPath), Csv(L.T(s.Title)), Csv(L.T(f.Label)), Csv(f.Format(r.File.Data, abs)), Csv(orig), "0x" + abs.ToString("X"), f.TypeName, f.SourceName }));
                    }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
