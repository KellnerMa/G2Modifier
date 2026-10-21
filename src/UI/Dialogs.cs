using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>Auswahl eines Werts aus einer Liste (Bezeichnungen oder Verweise auf andere Tabellen).</summary>
    public class PickDialog : Form
    {
        readonly ListBox list = new ListBox();
        readonly TextBox filter = new TextBox();
        readonly List<KeyValuePair<long, string>> all;
        public long SelectedValue;

        public PickDialog(string title, List<KeyValuePair<long, string>> items, long current)
        {
            all = items;
            Text = title; Width = 460; Height = 560; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            filter.Dock = DockStyle.Top;
            list.Dock = DockStyle.Fill; list.IntegralHeight = false;
            var ok = Theme.Primary(L.T("Übernehmen")); ok.AutoSize = false; ok.Dock = DockStyle.Bottom; ok.Height = 36;
            Font = Theme.Body;
            Controls.Add(list); Controls.Add(filter); Controls.Add(ok);
            filter.TextChanged += (s, e) => Fill(null);
            ok.Click += (s, e) => Accept();
            list.DoubleClick += (s, e) => Accept();
            AcceptButton = ok;
            Fill(current);
        }

        void Fill(long? select)
        {
            string q = filter.Text.Trim().ToLowerInvariant();
            list.BeginUpdate(); list.Items.Clear();
            foreach (var kv in all)
            {
                string t = kv.Key + "  ·  " + kv.Value;
                if (q.Length > 0 && !t.ToLowerInvariant().Contains(q)) continue;
                int i = list.Items.Add(new Entry { Value = kv.Key, Text = t });
                if (select.HasValue && kv.Key == select.Value) list.SelectedIndex = i;
            }
            list.EndUpdate();
        }

        void Accept()
        {
            var e = list.SelectedItem as Entry;
            if (e == null) return;
            SelectedValue = e.Value; DialogResult = DialogResult.OK; Close();
        }

        class Entry { public long Value; public string Text; public override string ToString() { return Text; } }
    }

    /// <summary>Liste der Zwischenstaende im Backup-Ordner (neueste oben).</summary>
    public class SnapshotDialog : Form
    {
        readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None };
        readonly Label detail = new Label { Dock = DockStyle.Bottom, Height = 110, AutoSize = false, ForeColor = Theme.Muted, Padding = new Padding(8) };
        public BackupService.Snapshot Chosen;

        public SnapshotDialog(BackupService b)
        {
            Text = L.T("Zwischenstände"); Width = 820; Height = 560; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; Font = Theme.Body; BackColor = Theme.Background;
            foreach (var c in new[] { "Zeitpunkt", "Dateien", "Anlass" }) list.Columns.Add(L.T(c));
            list.Columns[0].Width = 200; list.Columns[1].Width = 80; list.Columns[2].Width = 500;
            var snaps = b.ListSnapshots();
            foreach (var s in snaps)
            {
                var it = new ListViewItem(new[] { s.Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), s.Entries.Count.ToString(), s.Reason ?? "" });
                it.Tag = s; list.Items.Add(it);
            }
            var top = new Label { Dock = DockStyle.Top, Height = 76, AutoSize = false, Padding = new Padding(8),
                Text = (snaps.Count == 0 ? L.T("Noch keine Zwischenstände vorhanden.") + " " : "") +
                       L.T("Jeder Zwischenstand enthält die Dateien so, wie sie VOR dem jeweiligen Speichern waren. Zurückspielen stellt genau diesen Stand wieder her.") + "\r\n" + L.C(L.T("Ordner")) + b.SnapshotRoot };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8, 6, 8, 6), FlowDirection = FlowDirection.RightToLeft };
            var ok = Theme.Primary(L.T("Zurückspielen"));
            var cancel = Theme.Secondary(L.T("Schließen"));
            var open = Theme.Secondary(L.T("Ordner öffnen"));
            ok.Enabled = false;
            ok.Click += (s, e) => { Chosen = list.SelectedItems.Count > 0 ? (BackupService.Snapshot)list.SelectedItems[0].Tag : null; if (Chosen != null) { DialogResult = DialogResult.OK; Close(); } };
            cancel.Click += (s, e) => Close();
            open.Click += (s, e) => { try { System.IO.Directory.CreateDirectory(b.SnapshotRoot); System.Diagnostics.Process.Start("explorer.exe", "\"" + b.SnapshotRoot + "\""); } catch { } };
            foreach (var x in new Control[] { ok, cancel, open }) { x.Margin = new Padding(8, 0, 0, 0); buttons.Controls.Add(x); }
            list.SelectedIndexChanged += (s, e) =>
            {
                ok.Enabled = list.SelectedItems.Count > 0;
                if (list.SelectedItems.Count == 0) { detail.Text = ""; return; }
                var sn = (BackupService.Snapshot)list.SelectedItems[0].Tag;
                detail.Text = sn.Name + "\r\n" + string.Join("\r\n", sn.Entries.Take(5).Select(en => en[0]).ToArray()) + (sn.Entries.Count > 5 ? "\r\n" + L.F("... und {0} weitere", sn.Entries.Count - 5) : "");
            };
            list.DoubleClick += (s, e) => ok.PerformClick();
            Controls.Add(list); Controls.Add(top); Controls.Add(detail); Controls.Add(buttons);
            CancelButton = cancel;
        }
    }

    /// <summary>Bitfelder (Zustaende, Traeger) per Haekchen.</summary>
    public class FlagsDialog : Form
    {
        readonly CheckedListBox box = new CheckedListBox();
        readonly long unknownBits;
        public long Value;

        public FlagsDialog(string title, string[] names, long current)
        {
            Text = title; Width = 320; Height = 360; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            box.Dock = DockStyle.Fill; box.CheckOnClick = true;
            for (int i = 0; i < names.Length; i++) box.Items.Add(names[i], (current & (1L << i)) != 0);
            unknownBits = current & ~((1L << names.Length) - 1);
            var ok = Theme.Primary(L.T("Übernehmen")); ok.AutoSize = false; ok.Dock = DockStyle.Bottom; ok.Height = 36;
            Font = Theme.Body;
            Controls.Add(box); Controls.Add(ok);
            if (unknownBits != 0)
                Controls.Add(new Label { Dock = DockStyle.Top, Height = 34, Text = L.F("Unbekannte Bits 0x{0} bleiben unverändert gesetzt.", unknownBits.ToString("X")) });
            ok.Click += (s, e) =>
            {
                long v = unknownBits;
                for (int i = 0; i < box.Items.Count; i++) if (box.GetItemChecked(i)) v |= 1L << i;
                Value = v; DialogResult = DialogResult.OK; Close();
            };
            AcceptButton = ok;
        }
    }

    /// <summary>Gegner und Bosse skalieren.</summary>
    public class ScaleDialog : Form
    {
        public readonly List<Variant> Chosen = new List<Variant>();
        public readonly List<Batch.ScaleRule> Rules = new List<Batch.ScaleRule>();
        readonly List<CheckBox> variantBoxes = new List<CheckBox>();
        readonly List<Tuple<NumericUpDown, RecordDef, string[]>> factors = new List<Tuple<NumericUpDown, RecordDef, string[]>>();

        public ScaleDialog(Workspace ws)
        {
            Text = L.T("Gegner und Bosse skalieren"); Width = 560; Height = 660; Font = Theme.Body; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(10) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            Controls.Add(panel);

            AddHeader(panel, L.T("Bereich"));
            foreach (var key in new[] { "ENEMY", "BOSS" })
            {
                var t = ws.Table(key);
                if (t == null) continue;
                foreach (var v in t.Variants)
                {
                    var cb = new CheckBox { Text = t.Title + " " + v.Description + "  (" + L.F("{0} Einträge", v.Records.Count) + ")", AutoSize = true, Tag = v, Enabled = v.Records.Count > 0 };
                    variantBoxes.Add(cb);
                    panel.Controls.Add(cb); panel.SetColumnSpan(cb, 2);
                }
            }

            AddHeader(panel, L.T("Faktoren (1,00 = unverändert, 0 bleibt 0)"));
            AddFactor(panel, "HP", Formats.EnemyStats, "Health");
            AddFactor(panel, "MP", Formats.EnemyStats, "MP");
            AddFactor(panel, "Vitalität (Abwehr)", Formats.EnemyStats, "Vitality");
            AddFactor(panel, "Mentalität (Magieabwehr)", Formats.EnemyStats, "Mentality");
            AddFactor(panel, "Agilität", Formats.EnemyStats, "Agility");
            AddFactor(panel, "Angriffe: Stärke", Formats.EnemyMove, "Stats.Strength");
            AddFactor(panel, "Angriffe: Kraft", Formats.EnemyMove, "Stats.Power");
            AddFactor(panel, "Erfahrung", Formats.EnemyStats, "Experience");
            AddFactor(panel, "Skill- und Magie-Münzen", Formats.EnemyStats, "SkillCoins", "MagicCoins");
            AddFactor(panel, "Gold", Formats.EnemyStats, "GoldCoins");
            AddFactor(panel, "Drop-Chancen (höchstens 100)", Formats.EnemyStats, "Item1Chance", "Item2Chance");

            var info = new Label { AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = Theme.Muted, Text = L.T("Wirkt nur im Arbeitsspeicher. Geschrieben wird erst mit Strg+S. Welche Werte im Kampf wie wirken, steht nicht vollständig fest. Erst klein anfangen (z. B. 1,5) und im Spiel testen.") };
            panel.Controls.Add(info); panel.SetColumnSpan(info, 2);

            var ok = Theme.Primary(L.T("Anwenden")); ok.AutoSize = false; ok.Dock = DockStyle.Bottom; ok.Height = 38;
            Controls.Add(ok);
            ok.Click += (s, e) =>
            {
                foreach (var cb in variantBoxes) if (cb.Checked) Chosen.Add((Variant)cb.Tag);
                foreach (var f in factors)
                    foreach (var src in f.Item3) Rules.Add(new Batch.ScaleRule(f.Item2, src, (double)f.Item1.Value) { Cap = src.EndsWith("Chance") ? 100 : long.MaxValue });
                if (Chosen.Count == 0) { MessageBox.Show(this, L.T("Bitte mindestens einen Bereich wählen.")); Rules.Clear(); return; }
                DialogResult = DialogResult.OK; Close();
            };
        }

        static void AddHeader(TableLayoutPanel p, string text)
        {
            var l = new Label { Text = text, Font = Theme.BodyBold, AutoSize = true, Margin = new Padding(0, 10, 0, 4) };
            p.Controls.Add(l); p.SetColumnSpan(l, 2);
        }

        void AddFactor(TableLayoutPanel p, string label, RecordDef def, params string[] src)
        {
            p.Controls.Add(new Label { Text = L.T(label), AutoSize = true, Anchor = AnchorStyles.Left });
            var n = new NumericUpDown { Minimum = 0.1m, Maximum = 20m, DecimalPlaces = 2, Increment = 0.1m, Value = 1m, Width = 90 };
            p.Controls.Add(n);
            factors.Add(Tuple.Create(n, def, src));
        }
    }

    /// <summary>
    /// Level-ups per Kurve: Figuren und Werte waehlen, Faktor am Anfang und Ende eines Stufenbereichs.
    /// Gleicher Faktor an beiden Enden = einfacher Multiplikator. Wirkt auf alle gueltigen Kopien.
    /// </summary>
    public class CurveDialog : Form
    {
        static readonly string[] Src = { "IncreaseHP", "IncreaseMP", "IncreaseSP", "IncreaseStrength", "IncreaseVitality", "IncreaseAction", "IncreaseMovement", "IncreaseMagic", "IncreaseMentality", "IncreaseSkillSlot", "Experience" };
        static readonly string[] Names = { "HP", "MP", "SP", "Stärke", "Vitalität", "Aktion", "Bewegung", "Magie", "Mentalität", "Skill-Slot", "Benötigte Erfahrung" };
        readonly TableInfo table;
        readonly CheckedListBox chars = new CheckedListBox { CheckOnClick = true, Height = 170, Width = 230, IntegralHeight = false };
        readonly CheckedListBox stats = new CheckedListBox { CheckOnClick = true, Height = 230, Width = 230, IntegralHeight = false };
        readonly NumericUpDown from = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1, Width = 70 };
        readonly NumericUpDown to = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 99, Width = 70 };
        readonly NumericUpDown fStart = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 100, Increment = 5, Width = 80 };
        readonly NumericUpDown fEnd = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 100, Increment = 5, Width = 80 };
        readonly Label preview = new Label { AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 10, 0, 0) };
        public Batch.ScaleResult Result;

        public CurveDialog(TableInfo t)
        {
            table = t;
            Text = L.T("Kurve und Multiplikator"); Width = 560; Height = 700; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; Font = Theme.Body; BackColor = Theme.Background;
            var v = t.Variants.FirstOrDefault(x => x.Valid);
            if (v != null) foreach (var r in v.Records) chars.Items.Add(r.Label.Trim(), true);
            for (int i = 0; i < Names.Length; i++) stats.Items.Add(L.T(Names[i]), i < Names.Length - 2);

            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14), AutoScroll = true };
            f.Controls.Add(Theme.Hint(L.T("Statt jede Stufe einzeln zu ändern: Zuwachs je Stufe mit einem Faktor multiplizieren. Gleicher Wert bei Anfang und Ende = fester Multiplikator. Unterschiedliche Werte = Kurve, z. B. 100 % bis 150 %: am Anfang normal, später immer schneller."), 500));
            var two = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var c1 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            c1.Controls.Add(Theme.Strong(L.T("Figuren"))); c1.Controls.Add(chars);
            var c2 = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(16, 0, 0, 0) };
            c2.Controls.Add(Theme.Strong(L.T("Werte"))); c2.Controls.Add(stats);
            two.Controls.Add(c1); two.Controls.Add(c2);
            f.Controls.Add(two);
            f.Controls.Add(Theme.Strong(L.T("Stufenbereich")));
            f.Controls.Add(Row(L.T("von Stufe"), from, L.T("bis Stufe"), to));
            f.Controls.Add(Theme.Strong(L.T("Faktor in Prozent")));
            f.Controls.Add(Row(L.T("am Anfang"), fStart, L.T("am Ende"), fEnd));
            f.Controls.Add(preview);
            f.Controls.Add(Theme.Hint(L.T("Wirkt auf alle Kopien der Tabelle (xls_data, sys, sys_hardmode). 0 bleibt 0. Noch nicht gespeichert, bis Strg+S."), 500));

            var ok = Theme.Primary(L.T("Anwenden")); ok.AutoSize = false; ok.Dock = DockStyle.Bottom; ok.Height = 40;
            ok.Click += (s, e) => Apply();
            Controls.Add(f); Controls.Add(ok);
            foreach (var n in new[] { from, to, fStart, fEnd }) n.ValueChanged += (s, e) => UpdatePreview();
            chars.ItemCheck += (s, e) => BeginInvoke(new Action(UpdatePreview));
            stats.ItemCheck += (s, e) => BeginInvoke(new Action(UpdatePreview));
            UpdatePreview();
        }

        static Control Row(string a, Control ca, string b, Control cb)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            p.Controls.Add(new Label { Text = a, AutoSize = true, Padding = new Padding(0, 5, 4, 0) }); p.Controls.Add(ca);
            p.Controls.Add(new Label { Text = b, AutoSize = true, Padding = new Padding(16, 5, 4, 0) }); p.Controls.Add(cb);
            return p;
        }

        List<string> ChosenStats() { return stats.CheckedIndices.Cast<int>().Select(i => Src[i]).ToList(); }
        List<int> ChosenChars() { return chars.CheckedIndices.Cast<int>().ToList(); }

        void UpdatePreview()
        {
            var v = table.Variants.FirstOrDefault(x => x.Valid);
            var cs = ChosenChars(); var ss = ChosenStats();
            if (v == null || cs.Count == 0 || ss.Count == 0) { preview.Text = L.T("Bitte mindestens eine Figur und einen Wert wählen."); return; }
            var r = v.Records[cs[0]];
            var fd = Formats.Levelup.Get(ss[0]);
            var parts = new List<string>();
            foreach (int lv in new[] { (int)from.Value, (int)((from.Value + to.Value) / 2), (int)to.Value }.Distinct())
            {
                var sec = r.Sections[lv - 1];
                long old = fd.ReadNumber(r.File.Data, sec.Base + fd.Offset);
                double t = to.Value > from.Value ? (double)(lv - from.Value) / (double)(to.Value - from.Value) : 0;
                double fac = (double)fStart.Value / 100 + ((double)fEnd.Value - (double)fStart.Value) / 100 * t;
                long nv = old == 0 ? 0 : Math.Max(old > 0 ? 1 : long.MinValue, (long)Math.Round(old * fac, MidpointRounding.AwayFromZero));
                parts.Add(L.F("Stufe {0}: {1} → {2}", lv, old, nv));
            }
            preview.Text = L.F("Vorschau {0}, {1}:", r.Label.Trim(), stats.Items[stats.CheckedIndices[0]]) + "  " + string.Join("   ", parts.ToArray());
        }

        void Apply()
        {
            var cs = ChosenChars(); var ss = ChosenStats();
            if (cs.Count == 0 || ss.Count == 0) { MessageBox.Show(this, L.T("Bitte mindestens eine Figur und einen Wert wählen.")); return; }
            if (to.Value < from.Value) { MessageBox.Show(this, L.T("Die Endstufe muss größer oder gleich der Anfangsstufe sein.")); return; }
            var recs = table.Variants.Where(x => x.Valid).SelectMany(x => x.Records.Where(r => cs.Contains(r.Index))).ToList();
            Result = Batch.LevelCurve(recs, ss, (int)from.Value, (int)to.Value, (double)fStart.Value / 100, (double)fEnd.Value / 100);
            DialogResult = DialogResult.OK; Close();
        }
    }
}
