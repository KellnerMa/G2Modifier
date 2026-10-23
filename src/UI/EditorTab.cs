using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>Ein Reiter: Fassung waehlen, links Liste mit Filter, rechts Werte. Aenderungen erst "Uebernehmen", dann Strg+S.</summary>
    public class EditorTab : UserControl
    {
        class RowInfo { public int SectionIndex; public Section S; public FieldDef F; public int Abs; public bool Editable; }
        class RecordItem
        {
            public Record R; public EditorTab Owner;
            public override string ToString() { return Owner.Prefix(R) + R.Label; }
        }

        static List<KeyValuePair<string, string>> clipboard; // SectionIndex|SourceName -> Wert
        static string clipboardTable, clipboardFrom;

        readonly Workspace ws;
        readonly TableInfo table;
        readonly Settings settings;
        readonly Action changed;
        readonly Action<string> status;

        readonly ComboBox variantBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        readonly CheckBox syncBox = new CheckBox { Text = L.T("Änderung in alle Kopien schreiben"), AutoSize = true, Checked = true, Margin = new Padding(12, 6, 0, 0) };
        readonly CheckBox unknownBox = new CheckBox { Text = L.T("Unbekannte Felder bearbeitbar"), AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
        readonly TextBox filterBox = new TextBox { Width = 150 };
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = Theme.Body, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 50, BackColor = Theme.Surface };
        readonly DataGridView grid = new DataGridView();
        readonly Label header = new Label { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 6, 4, 4), Font = Theme.BodyBold, BackColor = Theme.Surface };
        readonly Label pendingLabel = new Label { AutoSize = true, ForeColor = Theme.Warning, Padding = new Padding(8, 8, 0, 0) };
        readonly TextBox errorBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false, BackColor = Color.MistyRose };
        readonly Label fileInfo = new Label { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(8, 6, 0, 0) };
        public Action ScaleRequested, CurveRequested;
        RecordView cards;
        FieldWriter writer;
        FlowLayoutPanel buttons;
        readonly CheckBox tableBox = new CheckBox { Text = L.T("Tabellenansicht"), AutoSize = true };
        readonly CheckBox emptyBox = new CheckBox { Text = L.T("Leere Plätze zeigen"), AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
        bool HidesEmpty { get { return (table.Key == "ITEM.BIN" || table.Key == "MS_PARAM.BIN" || table.Key == "SK_PARAM.BIN") && !emptyBox.Checked; } }
        readonly Dictionary<int, string> pending = new Dictionary<int, string>();
        Record current;

        public Variant CurrentVariant { get { return variantBox.SelectedItem as Variant; } }
        /// <summary>Uebernimmt zuerst getippte Werte der Kartenansicht (sofort geschrieben), dann zaehlt nur noch die Tabellenansicht.</summary>
        public bool HasPending { get { if (cards != null && !cards.IsDisposed) cards.FlushAll(); return pending.Count > 0; } }
        public string Title { get { return table.Title; } }

        public EditorTab(Workspace ws, TableInfo table, Settings settings, Action changed, Action<string> status)
        {
            this.ws = ws; this.table = table; this.settings = settings; this.changed = changed; this.status = status;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            Font = Theme.Body;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(4, 8, 4, 4), WrapContents = false, BackColor = Theme.Background };
            top.Controls.Add(new Label { Text = L.T(table.CopiesCanSync ? "Kopie:" : "Modus:"), AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            top.Controls.Add(variantBox);
            if (table.CopiesCanSync && table.Variants.Count > 1) top.Controls.Add(syncBox);
            top.Controls.Add(new Label { Text = L.T("Liste filtern:"), AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
            top.Controls.Add(filterBox);
            unknownBox.Checked = settings.ShowUnknown;
            unknownBox.CheckedChanged += (s, e) => { settings.ShowUnknown = unknownBox.Checked; settings.Save(); RefreshAll(); };
            var csv = Theme.Secondary(L.T("CSV-Export"));
            csv.Margin = new Padding(12, 0, 0, 0);
            csv.Click += (s, e) => ExportCsv();
            top.Controls.Add(csv);
            writer = new FieldWriter(ws, table, () => syncBox.Checked);
            cards = new RecordView(ws, table, settings, writer, () => { if (current != null) RefreshListItem(current); changed(); }, status) { Visible = false };
            cards.ResetRequested = ResetDirect;
            cards.CopyRequested = CopyValues;
            cards.PasteRequested = PasteDirect;
            tableBox.Margin = new Padding(12, 6, 0, 0);
            tableBox.CheckedChanged += (s, e) =>
            {
                if (tableBox.Checked) cards.FlushAll();
                else if (!ConfirmLeave()) { tableBox.Checked = true; return; }
                var r = current; current = null; if (r != null) ShowRecord(r);
            };
            top.Controls.Add(tableBox);
            top.Controls.SetChildIndex(tableBox, top.Controls.IndexOf(filterBox) + 1);
            if (table.Key == "ITEM.BIN" || table.Key == "MS_PARAM.BIN" || table.Key == "SK_PARAM.BIN")
            {
                emptyBox.CheckedChanged += (s, e) => { if (ConfirmLeave()) FillList(); };
                top.Controls.Add(emptyBox);
                top.Controls.SetChildIndex(emptyBox, top.Controls.IndexOf(tableBox) + 1);
            }
            if (table.Key == "ENEMY" || table.Key == "BOSS")
            {
                var sc = Theme.Secondary(L.T("Alle skalieren ..."));
                sc.Margin = new Padding(6, 0, 0, 0);
                sc.Click += (s, e) => { if (ScaleRequested != null) ScaleRequested(); };
                top.Controls.Add(sc);
            }
            if (table.Key == "TB_LVUP.BIN")
            {
                var cv = Theme.Primary(L.T("Kurve und Multiplikator ..."));
                cv.Margin = new Padding(6, 0, 0, 0);
                cv.Click += (s, e) => { if (CurveRequested != null && ConfirmLeave()) CurveRequested(); };
                top.Controls.Add(cv);
            }

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            bool splitSet = false;
            split.Panel1MinSize = 260;
            split.Resize += (s, e) => { if (splitSet || split.Width < 700) return; try { split.SplitterDistance = 280; splitSet = true; } catch { } };
            split.Panel1.Controls.Add(list);
            split.BackColor = Theme.Border;

            buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(4, 6, 4, 4), BackColor = Theme.Background, WrapContents = false, AutoScroll = false };
            var apply = Theme.Primary(L.T("Übernehmen"));
            apply.Click += (s, e) => Apply();
            buttons.Controls.Add(apply);
            buttons.Controls.Add(Btn(L.T("Verwerfen"), Discard));
            buttons.Controls.Add(Btn(L.T("Werte kopieren"), CopyValues));
            buttons.Controls.Add(Btn(L.T("Werte einfügen"), PasteValues));
            buttons.Controls.Add(Btn(L.T("Eintrag auf Original"), ResetToOriginal));
            buttons.Controls.Add(unknownBox);
            buttons.Controls.Add(pendingLabel);

            SetupGrid();
            split.Panel2.Controls.Add(cards);
            split.Panel2.Controls.Add(grid);
            split.Panel2.Controls.Add(errorBox);
            split.Panel2.Controls.Add(header);
            split.Panel2.Controls.Add(buttons);
            Controls.Add(split);
            Controls.Add(top);

            foreach (var v in table.Variants) variantBox.Items.Add(v);
            variantBox.SelectedIndexChanged += (s, e) => { if (!ConfirmLeave()) return; FillList(); };
            filterBox.TextChanged += (s, e) => { if (ConfirmLeave()) FillList(); };
            list.DrawItem += DrawRecord;
            // Mono scrollt die Liste um einen Eintrag, wenn vor dem Anzeigen schon ausgewaehlt wurde.
            // Daher: vor dem Anzeigen nur den Eintrag zeigen, die Markierung in der Liste folgt beim Anlegen des Fensters.
            list.HandleCreated += (s, e) => list.BeginInvoke(new Action(() =>
            {
                if (list.IsDisposed || list.SelectedIndex >= 0 || current == null) return;
                SelectRecord(current);
            }));
            list.SelectedIndexChanged += (s, e) =>
            {
                var it = list.SelectedItem as RecordItem;
                if (it == null || it.R == current) return;
                if (!ConfirmLeave()) { SelectRecord(current); return; }
                ShowRecord(it.R);
            };
            if (variantBox.Items.Count > 0) variantBox.SelectedIndex = 0;
        }

        static Button Btn(string text, Action a)
        {
            var b = Theme.Secondary(text);
            b.Margin = new Padding(6, 0, 0, 0);
            b.Click += (s, e) => a();
            return b;
        }

        void SetupGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Theme.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Theme.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.BodyBold;
            grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.RowTemplate.Height = 26;
            foreach (var c in new[] { "Abschnitt", "Feld", "Wert", "Original", "Bedeutung", "Offset", "Typ" })
                grid.Columns.Add(c, L.T(c));
            grid.Columns[0].FillWeight = 70; grid.Columns[1].FillWeight = 110; grid.Columns[2].FillWeight = 90;
            grid.Columns[3].FillWeight = 70; grid.Columns[4].FillWeight = 130; grid.Columns[5].FillWeight = 45; grid.Columns[6].FillWeight = 40;
            foreach (DataGridViewColumn c in grid.Columns) { c.SortMode = DataGridViewColumnSortMode.NotSortable; c.ReadOnly = c.Index != 2; }
            grid.CellBeginEdit += (s, e) =>
            {
                var ri = grid.Rows[e.RowIndex].Tag as RowInfo;
                if (e.ColumnIndex != 2 || ri == null || !ri.Editable || ri.F.IsFlags) e.Cancel = true;
            };
            grid.CellValidating += (s, e) =>
            {
                if (e.ColumnIndex != 2 || !grid.IsCurrentCellInEditMode) return;
                var ri = grid.Rows[e.RowIndex].Tag as RowInfo;
                string text = Convert.ToString(e.FormattedValue);
                var scratch = (byte[])current.File.Data.Clone();
                string err = ri.F.TryWrite(scratch, ri.Abs, text);
                grid.Rows[e.RowIndex].ErrorText = err ?? "";
                if (err != null) { e.Cancel = true; status(err); }
            };
            grid.CellEndEdit += (s, e) =>
            {
                if (e.ColumnIndex != 2) return;
                var ri = grid.Rows[e.RowIndex].Tag as RowInfo;
                SetPending(e.RowIndex, Convert.ToString(grid.Rows[e.RowIndex].Cells[2].Value));
                grid.Rows[e.RowIndex].ErrorText = "";
            };
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
                var ri = grid.Rows[e.RowIndex].Tag as RowInfo;
                if (ri == null || !ri.Editable) return;
                if (ri.F.IsFlags || ri.F.Labels != null || ri.F.Ref != RefKind.None) PickValue(e.RowIndex, ri);
                else grid.BeginEdit(true);
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && grid.CurrentCell != null && !grid.IsCurrentCellInEditMode)
                {
                    var ri = grid.CurrentRow.Tag as RowInfo;
                    if (ri != null && ri.Editable && (ri.F.IsFlags || ri.F.Labels != null || ri.F.Ref != RefKind.None)) { PickValue(grid.CurrentCell.RowIndex, ri); e.Handled = true; }
                }
            };
        }

        void PickValue(int row, RowInfo ri)
        {
            long cur;
            string curText = pending.ContainsKey(row) ? pending[row] : current.ViewFormat(ri.SectionIndex, ri.F);
            if (!long.TryParse(curText, out cur)) cur = 0;
            if (ri.F.IsFlags)
            {
                using (var d = new FlagsDialog(L.T(ri.F.Label), ri.F.FlagNames.Select(x => L.T(x)).ToArray(), cur))
                    if (d.ShowDialog(this) == DialogResult.OK) SetPending(row, d.Value.ToString());
                return;
            }
            var items = new List<KeyValuePair<long, string>>();
            if (ri.F.Labels != null)
                for (int i = 0; i < ri.F.Labels.Length && i <= ri.F.Max; i++) items.Add(new KeyValuePair<long, string>(i, L.T(ri.F.Labels[i])));
            else
            {
                string key = ri.F.Ref == RefKind.Item ? "ITEM.BIN" : ri.F.Ref == RefKind.Move ? "MS_PARAM.BIN" : "SK_PARAM.BIN";
                var t = ws.Table(key);
                var v = t == null ? null : t.Variants.FirstOrDefault(x => x.Valid);
                if (v != null) foreach (var r in v.Records) if (r.Index <= ri.F.Max) items.Add(new KeyValuePair<long, string>(r.Index, r.Label));
            }
            using (var d = new PickDialog(L.T(ri.F.Label), items, cur))
                if (d.ShowDialog(this) == DialogResult.OK) SetPending(row, d.SelectedValue.ToString());
        }

        void SetPending(int row, string text)
        {
            var ri = grid.Rows[row].Tag as RowInfo;
            string now = current.ViewFormat(ri.SectionIndex, ri.F);
            if (text == now) pending.Remove(row); else pending[row] = text;
            grid.Rows[row].Cells[2].Value = text;
            grid.Rows[row].Cells[2].Style.BackColor = pending.ContainsKey(row) ? Theme.WarningSoft : Color.Empty;
            UpdatePendingLabel();
        }

        void UpdatePendingLabel()
        {
            pendingLabel.Text = pending.Count > 0 ? "● " + L.F("{0} Änderung(en) nicht übernommen", pending.Count) : "";
        }

        public void FillList()
        {
            pending.Clear(); UpdatePendingLabel();
            var v = CurrentVariant;
            list.BeginUpdate(); list.Items.Clear();
            if (v == null) { list.EndUpdate(); return; }
            var errs = v.Files.Where(f => !f.Valid).ToList();
            string q = filterBox.Text.Trim().ToLowerInvariant();
            foreach (var r in v.Records)
            {
                if (HidesEmpty && Layouts.RawName(r).Trim().Length == 0) continue;
                if (q.Length > 0 && !(r.Label.ToLowerInvariant().Contains(q) || (r.Group ?? "").ToLowerInvariant().Contains(q) || Layouts.Name(r).ToLowerInvariant().Contains(q))) continue;
                list.Items.Add(new RecordItem { R = r, Owner = this });
            }
            list.EndUpdate();
            var f0 = v.Files.Count == 1 ? v.Files[0] : null;
            fileInfo.Text = f0 != null ? f0.RelPath + "  ·  " + L.C(L.T("Stand")) + L.T(f0.DiskState) + (f0.Notes.Count > 0 ? "  ·  " + string.Join("  ·  ", f0.Notes.ToArray()) : "")
                                       : L.F("{0} Dateien  ·  {1} Einträge", v.Files.Count, v.Records.Count);
            if (errs.Count > 0)
            {
                errorBox.Visible = true; grid.Visible = false; cards.Visible = false; header.Visible = true; buttons.Visible = true;
                errorBox.Text = L.T("GESPERRT: Diese Datei(en) passen nicht zur belegten Struktur. Das Tool zeigt und schreibt sie nicht.") + "\r\n\r\n" +
                    string.Join("\r\n", errs.Select(f => f.RelPath + ":\r\n   " + string.Join("\r\n   ", f.Errors.ToArray())).ToArray());
                header.Text = v.Description;
            }
            else { errorBox.Visible = false; grid.Visible = true; }
            current = null;
            if (list.Items.Count > 0)
            {
                if (list.IsHandleCreated) list.SelectedIndex = 0;
                else ShowRecord(((RecordItem)list.Items[0]).R);
            }
            else { grid.Rows.Clear(); if (errs.Count == 0) header.Text = L.T("Keine Einträge"); }
        }

        /// <summary>Fuer die Suche: Fassung wechseln, Filter leeren, Eintrag waehlen.</summary>
        public void Show(Record r)
        {
            var v = table.Variants.FirstOrDefault(x => x.Records.Contains(r));
            if (v == null || !ConfirmLeave()) return;
            if (variantBox.SelectedItem != v) variantBox.SelectedItem = v;
            if (filterBox.Text.Length > 0) filterBox.Text = "";
            if (HidesEmpty && Layouts.RawName(r).Trim().Length == 0) emptyBox.Checked = true;
            SelectRecord(r);
        }

        public TableInfo Table { get { return table; } }

        void ExportCsv()
        {
            if (CurrentVariant == null) return;
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = Title + " - " + CurrentVariant.Name + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Batch.ExportCsv(CurrentVariant, d.FileName);
                status(L.F("Exportiert: {0}", d.FileName));
            }
        }

        void SelectRecord(Record r)
        {
            for (int i = 0; i < list.Items.Count; i++) if (((RecordItem)list.Items[i]).R == r) { list.SelectedIndex = i; return; }
        }

        void ShowRecord(Record r)
        {
            current = r; pending.Clear(); UpdatePendingLabel();
            header.Text = r.Label + "\r\n" + r.File.RelPath + "   ·   " + L.C(L.T("Stand der Datei")) + L.T(r.File.DiskState) + (r.File.Dirty ? "   ·   ● " + L.T("ungespeichert") : "");
            grid.SuspendLayout();
            grid.Rows.Clear();
            bool fileOk = r.File.Valid;
            for (int si = 0; si < r.Sections.Count; si++)
            {
                var s = r.Sections[si];
                foreach (var f in s.Def.Fields)
                {
                    int abs = s.Base + f.Offset;
                    var ri = new RowInfo { SectionIndex = si, S = s, F = f, Abs = abs, Editable = fileOk && !f.Structural && (f.Known || settings.ShowUnknown) };
                    string val = r.ViewFormat(si, f);   // Hard-Mode-Platzhalter: Wert der Normal-Kopie
                    int oa; var od = r.ViewOriginal(si, f, out oa);
                    string orig = od != null ? f.Format(od, oa) : "";
                    int idx = grid.Rows.Add(L.T(s.Title), L.T(f.Label), val, orig, f.Meaning(r.ViewData(si, f), r.ViewAbs(si, f), ws), "0x" + abs.ToString("X"), f.TypeName);
                    var row = grid.Rows[idx];
                    row.Tag = ri;
                    row.Cells[1].ToolTipText = L.F("Quelle: G2DataGUI, Feld {0}", f.SourceName) + (f.Known ? "" : " " + L.T("(dort als Unknown geführt)"));
                    if (!ri.Editable) row.DefaultCellStyle.ForeColor = Theme.Muted;
                    if (r.File.Original != null && orig != val) { row.Cells[3].Style.ForeColor = Theme.Warning; row.Cells[2].Style.Font = Theme.BodyBold; }
                    if (ri.Editable && (f.IsFlags || f.Labels != null || f.Ref != RefKind.None)) row.Cells[2].ToolTipText = L.T("Doppelklick oder Enter: Auswahl");
                }
            }
            grid.ResumeLayout();
            bool useCards = !tableBox.Checked && fileOk;
            cards.Visible = useCards;
            grid.Visible = header.Visible = buttons.Visible = !useCards;
            if (useCards) cards.Show(r);
        }

        bool ConfirmLeave()
        {
            if (pending.Count == 0) return true;
            var res = MessageBox.Show(this, L.F("{0} Änderung(en) sind noch nicht übernommen.\n\nJa = jetzt übernehmen\nNein = verwerfen\nAbbrechen = hierbleiben", pending.Count), L.T("Nicht übernommen"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (res == DialogResult.Cancel) return false;
            if (res == DialogResult.Yes) Apply(); else Discard();
            return true;
        }

        public bool ResolvePendingBeforeSave() { return ConfirmLeave(); }

        void Apply()
        {
            if (current == null || pending.Count == 0) return;
            bool sync = table.CopiesCanSync && table.Variants.Count > 1 && syncBox.Checked;
            var others = sync ? table.Variants.Where(v => v != CurrentVariant && v.Valid).Select(v => v.Records.FirstOrDefault(x => x.Key == current.Key)).Where(x => x != null).ToList() : new List<Record>();
            var conflicts = new List<Tuple<Record, RowInfo, string>>();
            var plain = new List<Tuple<Record, RowInfo, string>>();
            int applied = 0;
            foreach (var kv in pending.OrderBy(k => k.Key))
            {
                var ri = grid.Rows[kv.Key].Tag as RowInfo;
                int si = ri.SectionIndex;
                string before = current.ViewFormat(si, ri.F);
                int oa; var od = current.ViewOriginal(si, ri.F, out oa);
                string err;
                if (od != null && od != current.File.Original && kv.Value == ri.F.Format(od, oa))
                {
                    // Zurueck auf Original, und das Original war ein Hard-Mode-Platzhalter: Bytes exakt wiederherstellen
                    int from = ri.F.Offset & ~1, len = ((ri.F.Offset + ri.F.Size + 1) & ~1) - from;
                    Buffer.BlockCopy(current.File.Original, ri.S.Base + from, current.File.Data, ri.S.Base + from, len);
                    err = null;
                }
                else err = current.WriteValue(si, ri.F, kv.Value);
                if (err != null) { MessageBox.Show(this, L.C(L.T(ri.F.Label)) + err); continue; }
                applied++;
                foreach (var o in others)
                {
                    if (si >= o.Sections.Count || o.Sections[si].Def != ri.S.Def) continue;
                    if (o.UsesNormal(si, ri.F)) continue;   // Hard-Mode-Platzhalter zeigt den neuen Normal-Wert automatisch
                    int oAbs = o.Sections[si].Base + ri.F.Offset;
                    var t = Tuple.Create(o, new RowInfo { SectionIndex = si, S = o.Sections[si], F = ri.F, Abs = oAbs }, kv.Value);
                    // Texte: in jede Kopie (xls_data hat oft eine aeltere Fassung als sys)
                    if (ri.F.IsText || ri.F.Format(o.File.Data, oAbs) == before) plain.Add(t); else conflicts.Add(t);
                }
            }
            foreach (var t in plain) t.Item1.WriteValue(t.Item2.SectionIndex, t.Item2.F, t.Item3);
            if (conflicts.Count > 0)
            {
                string msg = L.F("{0} Wert(e) in anderen Kopien weichen schon vorher ab:", conflicts.Count) + "\n\n" +
                    string.Join("\n", conflicts.Take(12).Select(c => c.Item1.File.RelPath + "  " + L.T(c.Item2.F.Label) + " = " + c.Item2.F.Format(c.Item1.File.Data, c.Item2.Abs)).ToArray()) +
                    "\n\n" + L.T("Trotzdem überschreiben?");
                if (MessageBox.Show(this, msg, L.T("Kopien weichen ab"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    foreach (var t in conflicts) t.Item1.WriteValue(t.Item2.SectionIndex, t.Item2.F, t.Item3);
            }
            status(L.F("{0} Wert(e) übernommen", applied) + (sync ? ", " + L.F("Kopien: {0} Datei(en) mitgezogen", plain.Select(p => p.Item1.File.RelPath).Distinct().Count()) : "") + ". " + L.T("Speichern mit Strg+S."));
            var r = current; current = null; ShowRecord(r);
            RefreshListItem(r);
            changed();
        }

        void Discard() { if (current == null) return; var r = current; current = null; ShowRecord(r); }

        void CopyValues()
        {
            if (current == null) return;
            clipboard = new List<KeyValuePair<string, string>>();
            for (int si = 0; si < current.Sections.Count; si++)
                foreach (var f in current.Sections[si].Def.Fields.Where(x => x.Known && !x.Structural && !x.IsText))
                    clipboard.Add(new KeyValuePair<string, string>(si + "|" + f.SourceName, current.ViewFormat(si, f)));
            clipboardTable = table.Key; clipboardFrom = current.Label;
            status(L.F("Werte von \"{0}\" kopiert ({1} Felder, ohne Namen, Texte und Zeiger).", current.Label, clipboard.Count));
        }

        void PasteValues()
        {
            if (current == null || clipboard == null) return;
            if (clipboardTable != table.Key) { MessageBox.Show(this, L.T("Die kopierten Werte stammen aus einer anderen Tabelle.")); return; }
            var map = clipboard.ToDictionary(k => k.Key, k => k.Value);
            int n = 0;
            for (int row = 0; row < grid.Rows.Count; row++)
            {
                var ri = grid.Rows[row].Tag as RowInfo;
                string v;
                if (ri.Editable && map.TryGetValue(ri.SectionIndex + "|" + ri.F.SourceName, out v) && v != current.ViewFormat(ri.SectionIndex, ri.F)) { SetPending(row, v); n++; }
            }
            status(L.F("{0} Werte von \"{1}\" vorgemerkt. Mit \"Übernehmen\" bestätigen.", n, clipboardFrom));
        }

        void ResetToOriginal()
        {
            if (current == null || current.File.Original == null) return;
            int n = 0;
            for (int row = 0; row < grid.Rows.Count; row++)
            {
                var ri = grid.Rows[row].Tag as RowInfo;
                if (ri.F.Structural) continue;
                int oa; var od = current.ViewOriginal(ri.SectionIndex, ri.F, out oa);
                string o = ri.F.Format(od, oa);
                bool bytesDiffer = false;
                for (int i = 0; i < ri.F.Size; i++) if (current.File.Data[ri.Abs + i] != current.File.Original[ri.Abs + i]) { bytesDiffer = true; break; }
                if (o != current.ViewFormat(ri.SectionIndex, ri.F) || bytesDiffer) { pending[row] = o; grid.Rows[row].Cells[2].Value = o; grid.Rows[row].Cells[2].Style.BackColor = Theme.WarningSoft; n++; }
            }
            UpdatePendingLabel();
            status(L.F("{0} Werte auf Original vorgemerkt. Mit \"Übernehmen\" bestätigen.", n));
        }

        void RefreshListItem(Record r)
        {
            for (int i = 0; i < list.Items.Count; i++)
                if (((RecordItem)list.Items[i]).R == r) { list.Invalidate(list.GetItemRectangle(i)); break; }
        }

        /// <summary>Liste links als Karten: Name gross, darunter ein Kennwert, links Porträt oder Symbol, rechts Markierung.</summary>
        void DrawRecord(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var it = list.Items[e.Index] as RecordItem;
            if (it == null) return;
            var g = e.Graphics;
            var rc = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Surface)) g.FillRectangle(b, rc);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, rc.X, rc.Y + 8, 3, rc.Height - 16);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, rc.X + 10, rc.Bottom - 1, rc.Right - 10, rc.Bottom - 1);
            int x = rc.X + 12;
            Bitmap icon = null;
            try { icon = Layouts.ListIcon(it.R); } catch { }
            if (icon != null) { g.DrawImage(icon, new Rectangle(x, rc.Y + (rc.Height - 32) / 2, 32, 32)); x += 40; }
            string mark = Prefix(it.R).Trim();
            var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Layouts.Name(it.R), Theme.BodyBold, new Rectangle(x, rc.Y + 7, rc.Right - x - 28, 20), Theme.Text, flags);
            TextRenderer.DrawText(g, Layouts.Summary(ws, it.R), Theme.Small, new Rectangle(x, rc.Y + 28, rc.Right - x - 10, 16), Theme.Muted, flags);
            if (mark.Length > 0)
                TextRenderer.DrawText(g, mark, Theme.Body, new Rectangle(rc.Right - 26, rc.Y + 7, 20, 20), mark == "●" ? Theme.Accent : Theme.Warning, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        /// <summary>Kartenansicht: ganzen Eintrag sofort auf das Backup-Original zuruecksetzen.</summary>
        void ResetDirect()
        {
            if (current == null || current.File.Original == null) { status(L.T("Kein Backup vorhanden, kein Original zum Zurücksetzen.")); return; }
            var targets = new List<Record> { current };
            if (writer.CanSync && syncBox.Checked)
                targets.AddRange(table.Variants.Where(v => v.Valid && !v.Records.Contains(current)).Select(v => v.Records.FirstOrDefault(x => x.Key == current.Key)).Where(x => x != null && x.File.Original != null));
            int n = 0;
            foreach (var r in targets)
                foreach (var sec in r.Sections)
                    foreach (var f in sec.Def.Fields)
                    {
                        if (f.Structural) continue;
                        int abs = sec.Base + f.Offset;
                        bool diff = false;
                        for (int i = 0; i < f.Size; i++) if (r.File.Data[abs + i] != r.File.Original[abs + i]) { diff = true; break; }
                        if (!diff) continue;
                        Buffer.BlockCopy(r.File.Original, abs, r.File.Data, abs, f.Size);
                        if (r == current) n++;
                    }
            status(L.F("{0} Werte auf Original zurückgesetzt.", n) + (targets.Count > 1 ? " " + L.F("Kopien: {0} Datei(en) mitgezogen", targets.Count - 1) + "." : "") + " " + L.T("Speichern mit Strg+S."));
            AfterDirect();
        }

        void PasteDirect()
        {
            if (current == null || clipboard == null) { status(L.T("Erst bei einem Eintrag \"Werte kopieren\" klicken.")); return; }
            if (clipboardTable != table.Key) { MessageBox.Show(this, L.T("Die kopierten Werte stammen aus einer anderen Tabelle.")); return; }
            var map = clipboard.ToDictionary(k => k.Key, k => k.Value);
            int n = WriteAll(current, (si, f) => { string v; return map.TryGetValue(si + "|" + f.SourceName, out v) ? v : null; }, true);
            status(L.F("{0} Werte von \"{1}\" eingefügt.", n, clipboardFrom) + " " + L.T("Speichern mit Strg+S."));
            AfterDirect();
        }

        int WriteAll(Record r, Func<int, FieldDef, string> value, bool onlyEditable)
        {
            int n = 0;
            for (int si = 0; si < r.Sections.Count; si++)
                foreach (var f in r.Sections[si].Def.Fields)
                {
                    if (f.Structural) continue;
                    if (onlyEditable && (!f.Known && !settings.ShowUnknown || f.IsText)) continue;
                    string v = value(si, f);
                    if (v == null) continue;
                    string info;
                    if (writer.Write(r, si, f, v, out info) == null && info != null) n++;
                }
            return n;
        }

        void AfterDirect()
        {
            if (current != null) { RefreshListItem(current); cards.Rebuild(); }
            changed();
        }

        public void RefreshAll()
        {
            if (table.Key == "ITEM.BIN") ItemUsable.Resync(ws);   // nach Verwerfen/Wiederherstellen: Verwendungs-Zeiger neu lesen
            list.BeginUpdate();
            for (int i = 0; i < list.Items.Count; i++) list.Items[i] = list.Items[i];
            list.EndUpdate();
            if (current != null) { var r = current; current = null; ShowRecord(r); }
            var v = CurrentVariant;
            if (v != null && v.Files.Count == 1) fileInfo.Text = v.Files[0].RelPath + "  ·  " + L.C(L.T("Stand")) + L.T(v.Files[0].DiskState);
            if (unknownBox.Checked != settings.ShowUnknown) unknownBox.Checked = settings.ShowUnknown;
        }

        string Prefix(Record r)
        {
            bool unsaved = false, modded = false;
            foreach (var s in r.Sections)
            {
                for (int i = 0; i < s.Def.Size; i++)
                {
                    int a = s.Base + i;
                    if (r.File.Data[a] != r.File.Disk[a]) unsaved = true;
                    if (r.File.Original != null && r.File.Data[a] != r.File.Original[a]) modded = true;
                }
            }
            return unsaved ? "● " : modded ? "◆ " : "   ";
        }
    }
}
