using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;
using G2Modifier.UI;

// Baut die Kartenansicht fuer JEDEN Eintrag JEDER Tabelle (mit und ohne Experte) und prueft:
// keine Ausnahme, keine unbekannten Feldnamen, jedes Feld erreichbar, Schreiben + Kopien-Abgleich byte-genau.
static class CardCheck
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what) { if (ok) passes++; else { fails++; Console.WriteLine("FEHLER: " + what); } }

    static IEnumerable<Control> All(Control c) { yield return c; foreach (Control k in c.Controls) foreach (var d in All(k)) yield return d; }

    [STAThread]
    static int Main(string[] a)
    {
        string game = a[0];
        // Kopien anlegen, damit der Abgleich xls_data -> sys -> sys_hardmode geprueft wird
        string afs = Path.Combine(game, "content", "data", "afs");
        foreach (var dir in new[] { "sys", "sys_hardmode" })
        {
            Directory.CreateDirectory(Path.Combine(afs, dir));
            foreach (var f in Directory.GetFiles(Path.Combine(afs, "xls_data"), "*.bin")) File.Copy(f, Path.Combine(afs, dir, Path.GetFileName(f)), true);
        }
        L.Set(a.Length > 1 ? a[1] : "de");
        L.TrackMissing = true;
        var ws = Workspace.Open(game);
        foreach (var f in ws.AllFiles) f.Original = (byte[])f.Disk.Clone();
        GameArt.Load(ws);
        var settings = new Settings();
        bool sync = true;
        var form = new Form { Size = new Size(1360, 880) };
        var host = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(host);
        form.Show();
        string lastStatus = "";
        int built = 0;

        foreach (var t in ws.Tables)
        {
            var writer = new FieldWriter(ws, t, () => sync);
            var view = new RecordView(ws, t, settings, writer, () => { }, s => lastStatus = s);
            host.Controls.Add(view);
            foreach (var v in t.Variants)
            {
                // alle Gegner/Bosse, sonst Stichprobe (Mono baut Fenster langsam): die ersten 25 und jeder 15.
                var sample = v.Records.Where((x, i) => t.Key == "ENEMY" || t.Key == "BOSS" || i < 25 || i % 15 == 0).ToList();
                if (v != t.Variants[0]) sample = sample.Take(3).ToList();
                foreach (var r in sample)
                {
                    foreach (bool expert in new[] { false, true })
                    {
                        view.ExpertOpen = expert;
                        try { view.Show(r); built++; }
                        catch (Exception ex) { Check(false, t.Key + " " + r.Label + ": " + ex); continue; }
                        Application.DoEvents();
                        if (!expert) continue;
                        var keys = new HashSet<string>(All(view).OfType<FieldControl>().Select(x => x.Key));
                        var shown = Enumerable.Range(0, r.Sections.Count).ToList();
                        if (t.Key == "TB_LVUP.BIN") shown = new List<int> { 0 };
                        if (t.Key == "ENEMY" || t.Key == "BOSS") shown = new List<int> { 0, 1, 2 };
                        foreach (int si in shown)
                            foreach (var f in r.Sections[si].Def.Fields)
                                Check(keys.Contains(si + "|" + f.SourceName), t.Key + " " + r.Label + ": Feld fehlt in der Ansicht: " + si + "|" + f.SourceName);
                    }
                }
            }
            // Auswahlkarte wechseln (Stufe 50, Angriff 3)
            if ((t.Key == "TB_LVUP.BIN" || t.Key == "ENEMY") && t.Variants[0].Records.Count > 0)
            {
                view.ExpertOpen = true;
                view.Show(t.Variants[0].Records[0]);
                var box = All(view).OfType<SafeCombo>().FirstOrDefault(c => c.Items.Count == (t.Key == "ENEMY" ? 5 : 99) && !(c.Parent is FieldControl));
                Check(box != null, t.Key + ": Auswahlliste gefunden");
                if (box != null)
                {
                    int idx = t.Key == "ENEMY" ? 2 : 49;          // Angriff 3 bzw. Stufe 50
                    box.SelectedIndex = idx;
                    Application.DoEvents();
                    int si = t.Key == "ENEMY" ? idx + 2 : idx;    // Abschnitte: Werte, KI, Angriff 1..5 bzw. Stufe 1..99
                    var keys = new HashSet<string>(All(view).OfType<FieldControl>().Select(x => x.Key));
                    Check(keys.Contains(si + "|" + (t.Key == "ENEMY" ? "Stats.Power" : "IncreaseHP")), t.Key + ": Auswahl zeigt Abschnitt " + si);
                    Check(!keys.Contains((t.Key == "ENEMY" ? 2 : 0) + "|" + (t.Key == "ENEMY" ? "Stats.Power" : "IncreaseHP")), t.Key + ": alter Abschnitt verworfen");
                    // gleicher Eintrag neu aufgebaut: Auswahl bleibt
                    view.Show(t.Variants[0].Records[0]);
                    keys = new HashSet<string>(All(view).OfType<FieldControl>().Select(x => x.Key));
                    Check(keys.Contains(si + "|" + (t.Key == "ENEMY" ? "Stats.Power" : "IncreaseHP")), t.Key + ": Auswahl bleibt nach Neuaufbau");
                }
            }
            host.Controls.Remove(view);
            view.Dispose();
        }
        Check(RecordView.MissingFields.Count == 0, "Unbekannte Feldnamen in Layouts: " + string.Join(", ", RecordView.MissingFields.Distinct().ToArray()));
        Console.WriteLine("Ansichten gebaut: " + built);

        // ---------- Schreiben und Kopien
        var items = ws.Table("ITEM.BIN");
        Check(items.Variants.Count == 3, "ITEM.BIN in drei Kopien");
        var w = new FieldWriter(ws, items, () => sync);
        var r0 = items.Variants[0].Records[5];
        var price = Formats.Item.Get("Stats.Price");
        Func<int, long> P = vi => price.ReadNumber(items.Variants[vi].Records[5].File.Data, items.Variants[vi].Records[5].Sections[0].Base + price.Offset);
        string info;
        Check(w.Write(r0, 0, price, "1234", out info) == null && info != null, "Preis schreiben");
        Check(P(0) == 1234 && P(1) == 1234 && P(2) == 1234, "Preis in allen Kopien: " + P(0) + "/" + P(1) + "/" + P(2));
        var r2 = items.Variants[2].Records[5];
        price.WriteNumber(r2.File.Data, r2.Sections[0].Base + price.Offset, 9);
        Check(w.Write(r0, 0, price, "55", out info) == null, "Preis 55");
        Check(P(0) == 55 && P(1) == 55 && P(2) == 9, "abweichende Kopie bleibt: " + P(0) + "/" + P(1) + "/" + P(2));
        Check(info.Contains("1"), "Meldung nennt Abweichung: " + info);
        sync = false;
        Check(w.Write(r0, 0, price, "77", out info) == null, "Preis 77 ohne Abgleich");
        Check(P(0) == 77 && P(1) == 55, "ohne Abgleich nur eine Datei");
        var before = (byte[])r0.File.Data.Clone();
        Check(w.Write(r0, 0, price, "abc", out info) != null, "Unsinn wird abgelehnt");
        Check(w.Write(r0, 0, price, "99999999999", out info) != null, "Überlauf wird abgelehnt");
        Check(Bin.Equal(before, r0.File.Data), "abgelehnte Eingabe ändert nichts");
        Check(w.Write(r0, 0, price, "77", out info) == null && info == null, "gleicher Wert: keine Meldung");
        // Nur das Zielfeld aendert sich
        var diff = GameFile.DiffRanges(r0.File.Disk, r0.File.Data);
        Check(diff.Count == 1 && diff[0][0] == r0.Sections[0].Base + 64 && diff[0][1] <= 4, "nur 4 Bytes Preis geändert");
        // Name (Text) mit Polsterung
        var name = Formats.Item.Get("Name");
        Check(w.Write(r0, 0, name, "Testname", out info) == null, "Name schreiben");
        Check(FixedText.Decode(r0.File.Data, r0.Sections[0].Base, 18) == "Testname", "Name gelesen");
        Check(w.Write(r0, 0, name, "Ümlaut", out info) != null, "Umlaut abgelehnt");
        Check(w.Write(r0, 0, name, new string('x', 19), out info) != null, "zu lang abgelehnt");
        Check(Layouts.Name(r0) == "Testname", "Liste zeigt neuen Namen sofort");

        // verworfene Bedienelemente im Formular?
        int dead = All(form).Count(c => c.IsDisposed);
        Check(dead == 0, "verworfene Elemente im Fenster: " + dead);
        form.Close();
        if (L.Lang != "de") foreach (var m in L.Missing.OrderBy(x => x)) Console.WriteLine("FEHLT[" + L.Lang + "]: " + m.Replace("\n", "\\n"));
        Console.WriteLine("CardCheck: " + passes + " ok, " + fails + " Fehler");
        return fails == 0 ? 0 : 1;
    }
}
