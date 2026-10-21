using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>
    /// Schreibt einen Feldwert sofort in den Arbeitsstand (Speichern weiterhin mit Strg+S).
    /// Kopien derselben BIN-Datei (xls_data/sys/sys_hardmode) werden mitgezogen, wenn sie vorher denselben Wert hatten.
    /// </summary>
    public class FieldWriter
    {
        readonly TableInfo table;
        readonly Func<bool> sync;
        readonly Workspace ws;

        public FieldWriter(Workspace ws, TableInfo table, Func<bool> sync) { this.ws = ws; this.table = table; this.sync = sync; }

        /// <summary>Wert fuer die Statuszeile: Bedeutung (Name, Häkchen) statt nackter Zahl, wo bekannt.</summary>
        string Show(FieldDef f, byte[] d, int abs)
        {
            if (f.IsText) return "\"" + f.Format(d, abs) + "\"";
            string m = (f.Labels != null || f.IsFlags || f.Ref != RefKind.None) ? f.Meaning(d, abs, ws) : "";
            return m.Length > 0 ? m : f.Format(d, abs);
        }

        public bool CanSync { get { return table.CopiesCanSync && table.Variants.Count > 1; } }
        public bool SyncOn { get { return sync(); } }
        public TableInfo Table { get { return table; } }

        /// <summary>Fehlertext oder null. info = Statuszeile (null, wenn sich nichts geaendert hat).</summary>
        public string Write(Record r, int si, FieldDef f, string input, out string info)
        {
            info = null;
            var s = r.Sections[si];
            int abs = s.Base + f.Offset;
            string before = f.Format(r.File.Data, abs);
            if (input == before) return null;
            string shownBefore = Show(f, r.File.Data, abs);
            string err = f.TryWrite(r.File.Data, abs, input);
            if (err != null) return err;
            string after = f.Format(r.File.Data, abs);
            if (after == before) return null;
            int synced = 0, skipped = 0;
            if (CanSync && sync())
            {
                foreach (var v in table.Variants)
                {
                    if (!v.Valid || v.Records.Contains(r)) continue;
                    var o = v.Records.FirstOrDefault(x => x.Key == r.Key);
                    if (o == null || si >= o.Sections.Count || o.Sections[si].Def != s.Def) continue;
                    int oAbs = o.Sections[si].Base + f.Offset;
                    string ov = f.Format(o.File.Data, oAbs);
                    if (ov == before) { if (f.TryWrite(o.File.Data, oAbs, input) == null) synced++; }
                    else if (ov != after) skipped++;
                }
            }
            info = L.F("{0}: {1} → {2}.", L.T(f.Label), shownBefore, Show(f, r.File.Data, abs));
            if (synced > 0) info += " " + L.F("{0} Kopie(n) mitgezogen.", synced);
            if (skipped > 0) info += " " + L.F("{0} Kopie(n) wichen schon vorher ab und bleiben unverändert.", skipped);
            info += " " + L.T("Speichern mit Strg+S.");
            return null;
        }
    }

    /// <summary>Auswahlliste, die das Mausrad nur bei geoeffneter Liste nutzt. Sonst scrollt die Seite (kein versehentliches Umstellen).</summary>
    public class SafeCombo : ComboBox
    {
        public SafeCombo() { DropDownStyle = ComboBoxStyle.DropDownList; Font = Theme.Body; MaxDropDownItems = 16; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && !DroppedDown)
            {
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                var v = Parent;
                while (v != null && !(v is RecordView)) v = v.Parent;
                if (v != null) ((RecordView)v).WheelScroll(delta);
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>Gemeinsame Basis aller Eingabefelder der Kartenansicht.</summary>
    public abstract class FieldControl : Panel
    {
        protected readonly RecordView view;
        protected readonly Record rec;
        protected readonly int si;
        protected readonly FieldDef f;
        protected readonly int abs;
        protected readonly bool editable;
        protected bool loading;
        public bool Dead;
        /// <summary>Feld aus einer anderen Tabelle (z. B. Stufe 1 aus tb_lvup.bin auf der Seite Startwerte).</summary>
        public FieldWriter WriterOverride;

        protected FieldControl(RecordView view, Record rec, int si, FieldDef f, bool editable)
        {
            this.view = view; this.rec = rec; this.si = si; this.f = f; this.editable = editable;
            abs = rec.Sections[si].Base + f.Offset;
            BackColor = Theme.Surface;
            Margin = new Padding(0, 0, 14, 6);
        }

        public string Key { get { return si + "|" + f.SourceName; } }

        bool Alive { get { return !Dead && !IsDisposed && !Disposing && !view.IsDisposed; } }

        protected bool Commit(string input)
        {
            if (loading || !Alive || !editable) return false;
            string info;
            string err = (WriterOverride ?? view.Writer).Write(rec, si, f, input, out info);
            if (err != null) { ShowError(true); view.Status(L.T(f.Label) + ": " + err); return false; }
            ShowError(false);
            if (info == null) return false;
            view.Status(info);
            RefreshValue();
            view.NotifyChanged();
            return true;
        }

        protected virtual void ShowError(bool on) { }
        public abstract void RefreshValue();
        /// <summary>Getippte, noch nicht bestaetigte Eingabe uebernehmen (vor Speichern, Wechsel, Neuaufbau).</summary>
        public virtual void Flush() { }

        protected string OriginalText(Func<byte[], string> show)
        {
            if (rec.File.Original == null) return "";
            string o = f.Format(rec.File.Original, abs), v = f.Format(rec.File.Data, abs);
            return o == v ? "" : L.C(L.T("Original")) + show(rec.File.Original);
        }
    }

    /// <summary>Zahl, Text oder Auswahl per Name. Uebernahme sofort (Enter, Feld verlassen, Auswahl).</summary>
    public class FieldBox : FieldControl
    {
        readonly Label caption = new Label { AutoSize = false, Height = 18, ForeColor = Theme.Muted, Font = Theme.Small, AutoEllipsis = true };
        readonly Label orig = new Label { AutoSize = false, Height = 16, ForeColor = Theme.Warning, Font = Theme.Small, AutoEllipsis = true };
        readonly TextBox text;
        readonly SafeCombo combo;
        readonly List<long> values = new List<long>();
        readonly Func<long, string> fallback;

        public FieldBox(RecordView view, Record rec, int si, FieldDef f, bool editable, string captionText, int width, Bitmap icon, List<KeyValuePair<long, string>> choices, Func<long, string> fallback)
            : base(view, rec, si, f, editable)
        {
            this.fallback = fallback;
            bool compact = captionText == "";   // Zeilen in Platz-Listen: Ueberschrift steht einmal darueber
            int top = compact ? 0 : 20;
            Width = width; Height = top + 42;
            int x = 0;
            if (icon != null) { Controls.Add(new PictureBox { Image = icon, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(0, top, 26, 26) }); x = 30; }
            caption.SetBounds(x, 0, width - x, 18);
            caption.Visible = !compact;
            caption.Text = captionText;
            Control input;
            if (choices != null)
            {
                combo = new SafeCombo();
                foreach (var c in choices) { values.Add(c.Key); combo.Items.Add(c.Value); }
                combo.DropDownWidth = Math.Max(width - x, Math.Min(420, 60 + 7 * choices.Select(c => c.Value.Length).DefaultIfEmpty(0).Max()));
                // Beide Ereignisse: Mono meldet Tastaturauswahl nur ueber SelectedIndexChanged. Doppelte Meldung schreibt nichts doppelt.
                EventHandler pick = (s, e) => { if (!loading && !Dead && combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Count) Commit(values[combo.SelectedIndex].ToString()); };
                combo.SelectionChangeCommitted += pick;
                combo.SelectedIndexChanged += pick;
                input = combo;
            }
            else
            {
                text = new TextBox { Font = Theme.Body };
                text.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { Commit(text.Text); e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.Escape) { RefreshValue(); e.SuppressKeyPress = true; }
                };
                text.Leave += (s, e) => Commit(text.Text);
                text.TextChanged += (s, e) => { if (!loading && !Dead) Validate(); };
                input = text;
            }
            input.SetBounds(x, top, width - x, 26);
            input.Enabled = editable;
            orig.SetBounds(x, top + 26, width - x, 16);
            Controls.Add(caption); Controls.Add(input); Controls.Add(orig);
            var tip = (compact ? "" : captionText + "\n") + L.F("Quelle: G2DataGUI, Feld {0}", f.SourceName) + (f.Known ? "" : " " + L.T("(dort als Unknown geführt)"));
            view.Tip(compact ? input : caption, tip);
            RefreshValue();
        }

        void Validate()
        {
            var scratch = (byte[])rec.File.Data.Clone();
            ShowError(f.TryWrite(scratch, abs, text.Text) != null);
        }

        protected override void ShowError(bool on)
        {
            if (text != null && !text.IsDisposed) text.BackColor = on ? Theme.WarningSoft : SystemColors.Window;
        }

        public override void Flush()
        {
            if (text != null && !Dead && !text.IsDisposed && text.Text != f.Format(rec.File.Data, abs)) Commit(text.Text);
        }

        string ChoiceName(long n)
        {
            int i = values.IndexOf(n);
            if (i >= 0) return (string)combo.Items[i];
            return fallback != null ? fallback(n) : n + "  ·  " + L.T("(kein Eintrag in der Bezeichnungsliste)");
        }

        public override void RefreshValue()
        {
            if (Dead || IsDisposed) return;
            loading = true;
            try
            {
                string v = f.Format(rec.File.Data, abs);
                if (combo == null) { text.Text = v; ShowError(false); }
                else
                {
                    long n; long.TryParse(v, out n);
                    int i = values.IndexOf(n);
                    if (i < 0) { string nm = ChoiceName(n); values.Add(n); combo.Items.Add(nm); i = values.Count - 1; }
                    combo.SelectedIndex = i;
                }
                orig.Text = OriginalText(d =>
                {
                    string o = f.Format(d, abs);
                    if (combo == null) return o;
                    long n; long.TryParse(o, out n);
                    return ChoiceName(n);
                });
            }
            finally { loading = false; }
        }
    }

    /// <summary>Bitfeld (z. B. Träger, Zustände) als Häkchen.</summary>
    public class FlagsBox : FieldControl
    {
        readonly List<CheckBox> boxes = new List<CheckBox>();
        readonly Label orig = new Label { AutoSize = false, Height = 16, ForeColor = Theme.Warning, Font = Theme.Small, AutoEllipsis = true };

        public FlagsBox(RecordView view, Record rec, int si, FieldDef f, bool editable, string captionText, int width)
            : base(view, rec, si, f, editable)
        {
            Width = width;
            var caption = new Label { AutoSize = false, Height = 18, ForeColor = Theme.Muted, Font = Theme.Small, Text = captionText, Bounds = new Rectangle(0, 0, width, 18) };
            var flow = new FlowLayoutPanel { Location = new Point(0, 20), WrapContents = true, BackColor = Theme.Surface, Margin = Padding.Empty };
            for (int b = 0; b < f.FlagNames.Length; b++)
            {
                int bit = b;
                var cb = new CheckBox { Text = L.T(f.FlagNames[b]), AutoSize = true, Enabled = editable, Margin = new Padding(0, 0, 14, 2), Font = Theme.Body };
                var icon = f.FlagNames.Length == 8 && f.FlagNames[0] == "Gift" ? GameArt.Status(b) : null;   // Zustandsfeld
                if (icon != null) { cb.Image = icon; cb.ImageAlign = ContentAlignment.MiddleLeft; cb.TextImageRelation = TextImageRelation.ImageBeforeText; }
                cb.CheckedChanged += (s, e) =>
                {
                    if (loading || Dead) return;
                    long v = f.ReadNumber(rec.File.Data, abs);
                    v = cb.Checked ? v | (1L << bit) : v & ~(1L << bit);
                    Commit(v.ToString());
                };
                boxes.Add(cb);
                flow.Controls.Add(cb);
            }
            var pref = flow.GetPreferredSize(new Size(width, 0));
            flow.Size = new Size(width, pref.Height);
            orig.SetBounds(0, 22 + pref.Height, width, 16);
            Height = 40 + pref.Height;
            Controls.Add(caption); Controls.Add(flow); Controls.Add(orig);
            view.Tip(caption, L.F("Quelle: G2DataGUI, Feld {0}", f.SourceName));
            RefreshValue();
        }

        public override void RefreshValue()
        {
            if (Dead || IsDisposed) return;
            loading = true;
            try
            {
                long v = f.ReadNumber(rec.File.Data, abs);
                for (int b = 0; b < boxes.Count; b++) boxes[b].Checked = (v & (1L << b)) != 0;
                orig.Text = OriginalText(d => f.Meaning(d, abs, view.Ws));
            }
            finally { loading = false; }
        }
    }

    /// <summary>Ja/Nein-Feld (Werte 0/1) als Häkchen.</summary>
    public class BoolBox : FieldControl
    {
        readonly CheckBox cb;
        readonly Label orig = new Label { AutoSize = false, Height = 16, ForeColor = Theme.Warning, Font = Theme.Small };

        public BoolBox(RecordView view, Record rec, int si, FieldDef f, bool editable, string captionText, int width)
            : base(view, rec, si, f, editable)
        {
            Width = width; Height = 62;
            cb = new CheckBox { Text = captionText, AutoSize = false, Bounds = new Rectangle(0, 20, width, 26), Enabled = editable, Font = Theme.Body };
            cb.CheckedChanged += (s, e) => { if (!loading && !Dead) Commit(cb.Checked ? "1" : "0"); };
            orig.SetBounds(0, 46, width, 16);
            Controls.Add(cb); Controls.Add(orig);
            view.Tip(cb, L.F("Quelle: G2DataGUI, Feld {0}", f.SourceName));
            RefreshValue();
        }

        public override void RefreshValue()
        {
            if (Dead || IsDisposed) return;
            loading = true;
            try
            {
                cb.Checked = f.ReadNumber(rec.File.Data, abs) != 0;
                orig.Text = OriginalText(d => f.ReadNumber(d, abs) != 0 ? L.T("ja") : L.T("nein"));
            }
            finally { loading = false; }
        }
    }

    /// <summary>Eine weisse Karte mit Ueberschrift und umbrechenden Feldern.</summary>
    public class Card
    {
        public readonly CardPanel Panel;
        public readonly FlowLayoutPanel Inner;
        readonly RecordView view;
        public int Keep;   // Anzahl fester Bedienelemente (Auswahlkarten: Titel + Auswahlzeile)

        public Card(RecordView view, string title)
        {
            this.view = view;
            Panel = new CardPanel { BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 12) };
            Inner = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Surface, Location = new Point(16, 12) };
            if (!string.IsNullOrEmpty(title))
            {
                var t = new Label { Text = title, AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 10) };
                Inner.Controls.Add(t);
                Inner.SetFlowBreak(t, true);
            }
            Panel.Controls.Add(Inner);
            Keep = Inner.Controls.Count;
        }

        public RecordView View { get { return view; } }

        public Control Field(int si, string src, int width = 130, Bitmap icon = null, string caption = null, List<KeyValuePair<long, string>> choices = null)
        {
            var c = view.MakeField(si, src, width, icon, caption, choices);
            if (c != null) Inner.Controls.Add(c);
            return c;
        }

        public void Add(Control c) { Inner.Controls.Add(c); }

        public void Break()
        {
            if (Inner.Controls.Count > 0) Inner.SetFlowBreak(Inner.Controls[Inner.Controls.Count - 1], true);
        }

        public void Note(string text)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Math.Max(300, view.CardWidth - 40), 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 8) };
            Inner.Controls.Add(l);
            Inner.SetFlowBreak(l, true);
        }

        public void SubHeading(string text)
        {
            Break();
            var l = new Label { Text = text, AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 6) };
            Inner.Controls.Add(l);
            Inner.SetFlowBreak(l, true);
        }

        public void Fit()
        {
            int w = view.CardWidth;
            var pref = Inner.GetPreferredSize(new Size(w - 32, 0));
            Inner.Size = new Size(w - 32, pref.Height);
            Panel.Size = new Size(w, pref.Height + 22);
        }

        /// <summary>Alles hinter den festen Elementen verwerfen (Auswahlkarten).</summary>
        public void ClearBody()
        {
            var old = Inner.Controls.Cast<Control>().Skip(Keep).ToList();
            view.Retire(old);
            foreach (var c in old) { Inner.Controls.Remove(c); c.Dispose(); }
        }
    }

    /// <summary>
    /// Kartenansicht eines Eintrags. Aufbau je Tabelle in Layouts. Jedes Feld schreibt sofort.
    /// Regel gegen ObjectDisposedException: Bedienelemente werden nie ueber einen Neuaufbau hinweg wiederverwendet,
    /// und Neuaufbau aus einem Klick auf ein Element, das dabei verworfen wird, laeuft verzoegert (Defer).
    /// </summary>
    public class RecordView : Panel
    {
        public readonly Workspace Ws;
        public readonly FieldWriter Writer;
        public readonly Settings Settings;
        public readonly TableInfo Table;
        readonly Action changed;
        readonly Action<string> status;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(16, 12, 16, 16), Location = new Point(0, 0), BackColor = Theme.Background };
        readonly ToolTip tips = new ToolTip();
        readonly List<FieldControl> fields = new List<FieldControl>();
        readonly List<Card> cards = new List<Card>();
        readonly List<Action> live = new List<Action>();
        readonly HashSet<string> used = new HashSet<string>();
        readonly Dictionary<string, int> picks = new Dictionary<string, int>();
        readonly Dictionary<RefKind, List<KeyValuePair<long, string>>> refCache = new Dictionary<RefKind, List<KeyValuePair<long, string>>>();
        Record rec;
        int builtWidth;
        bool building;
        public bool ExpertOpen;
        public Action ResetRequested, CopyRequested, PasteRequested, CurveRequested;

        public RecordView(Workspace ws, TableInfo table, Settings settings, FieldWriter writer, Action changed, Action<string> status)
        {
            Ws = ws; Table = table; Settings = settings; Writer = writer; this.changed = changed; this.status = status;
            Dock = DockStyle.Fill; AutoScroll = true; BackColor = Theme.Background;
            Controls.Add(flow);
        }

        public Record Current { get { return rec; } }
        public void Status(string s) { status(s); }
        public void Tip(Control c, string text) { tips.SetToolTip(c, text); }

        public int CardWidth
        {
            get
            {
                int w = Parent != null ? Parent.ClientSize.Width : ClientSize.Width;
                return Math.Max(460, Math.Min(1100, w - 60));
            }
        }

        public void WheelScroll(int delta)
        {
            int y = -AutoScrollPosition.Y - delta;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
        }

        public void NotifyChanged()
        {
            foreach (var a in live.ToList()) a();
            changed();
        }

        /// <summary>Text, der sich nach jeder Aenderung neu berechnet (Kopfzeile, Summen).</summary>
        public void Live(Label l, Func<string> text)
        {
            Action a = () => { if (!l.IsDisposed) l.Text = text(); };
            a();
            live.Add(a);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (rec != null && Visible && !building && Math.Abs(CardWidth - builtWidth) > 30) Defer(Rebuild);
        }

        /// <summary>Nach dem laufenden Ereignis ausfuehren (Element, das das Ereignis ausgeloest hat, darf noch leben).</summary>
        public void Defer(Action a)
        {
            if (IsDisposed || !IsHandleCreated) { if (!IsDisposed && !IsHandleCreated) a(); return; }
            BeginInvoke(new Action(() => { if (!IsDisposed) a(); }));
        }

        public void Rebuild() { if (rec != null) Show(rec); }

        public void FlushAll() { foreach (var f in fields.ToList()) f.Flush(); }

        public void RefreshValues() { foreach (var f in fields.ToList()) f.RefreshValue(); foreach (var a in live.ToList()) a(); }

        /// <summary>Felder werden gleich verworfen: Eingaben uebernehmen und abmelden.</summary>
        public void Retire(IEnumerable<Control> controls)
        {
            foreach (var c in controls)
            {
                foreach (var fc in Descendants(c).OfType<FieldControl>().ToList())
                {
                    fc.Flush();
                    fc.Dead = true;
                    fields.Remove(fc);
                }
            }
        }

        static IEnumerable<Control> Descendants(Control c)
        {
            yield return c;
            foreach (Control k in c.Controls) foreach (var d in Descendants(k)) yield return d;
        }

        public void Show(Record r)
        {
            bool same = r == rec;
            int scroll = same ? -AutoScrollPosition.Y : 0;
            if (!same) picks.Clear();
            building = true;
            try
            {
                SuspendLayout();
                flow.SuspendLayout();
                var old = flow.Controls.Cast<Control>().ToList();
                Retire(old);
                flow.Controls.Clear();
                foreach (var c in old) c.Dispose();
                fields.Clear(); cards.Clear(); live.Clear(); used.Clear(); refCache.Clear();
                rec = r;
                builtWidth = CardWidth;
                Layouts.Build(this, r);
                foreach (var c in cards) c.Fit();
                flow.ResumeLayout();
                ResumeLayout();
                AutoScrollPosition = new Point(0, scroll);
            }
            finally { building = false; }
        }

        // ------------------------------------------------------------ Bausteine fuer Layouts

        public void Header(string subtitle, Bitmap image, Func<string> name)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background, Margin = new Padding(0, 0, 0, 10) };
            if (image != null) row.Controls.Add(new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Margin = new Padding(0, 0, 14, 0) });
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Background, Margin = Padding.Empty };
            var title = new Label { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 2) };
            Live(title, name);
            col.Controls.Add(title);
            if (!string.IsNullOrEmpty(subtitle))
                col.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(Math.Max(300, CardWidth - 120), 0), ForeColor = Theme.Muted, Text = subtitle, Margin = new Padding(0, 0, 0, 4) });
            var state = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 0, 0, 6) };
            Live(state, () => StateText(rec));
            col.Controls.Add(state);
            var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background, Margin = Padding.Empty };
            if (CurveRequested != null) links.Controls.Add(ActionButton(Theme.Primary(L.T("Kurve und Multiplikator ...")), CurveRequested, false));
            links.Controls.Add(ActionButton(Theme.Secondary(L.T("Eintrag auf Original")), ResetRequested, true));
            links.Controls.Add(ActionButton(Theme.Secondary(L.T("Werte kopieren")), CopyRequested, false));
            links.Controls.Add(ActionButton(Theme.Secondary(L.T("Werte einfügen")), PasteRequested, true));
            col.Controls.Add(links);
            row.Controls.Add(col);
            flow.Controls.Add(row);
        }

        Button ActionButton(Button b, Action a, bool rebuilds)
        {
            b.Margin = new Padding(0, 0, 8, 0);
            b.Height = 30;
            b.Enabled = a != null && rec.File.Valid;
            b.Click += (s, e) =>
            {
                if (a == null) return;
                FlushAll();
                if (rebuilds) Defer(a); else a();
            };
            return b;
        }

        static string StateText(Record r)
        {
            bool unsaved = false, modded = false;
            foreach (var s in r.Sections)
                for (int i = 0; i < s.Def.Size; i++)
                {
                    int a = s.Base + i;
                    if (r.File.Data[a] != r.File.Disk[a]) unsaved = true;
                    if (r.File.Original != null && r.File.Data[a] != r.File.Original[a]) modded = true;
                }
            string st = r.File.RelPath;
            if (unsaved) st += "   ·   ● " + L.T("ungespeichert");
            else if (modded) st += "   ·   ◆ " + L.T("gegenüber Original verändert");
            return st;
        }

        public Card NewCard(string title)
        {
            var c = new Card(this, title);
            cards.Add(c);
            flow.Controls.Add(c.Panel);
            return c;
        }

        /// <summary>Nur fuer Tests: Feldnamen aus Layouts, die es im Schema nicht gibt (Tippfehler).</summary>
        public static readonly List<string> MissingFields = new List<string>();

        bool Editable(FieldDef f) { return rec.File.Valid && !f.Structural && (f.Known || Settings.ShowUnknown); }

        public Control MakeField(int si, string src, int width, Bitmap icon, string caption, List<KeyValuePair<long, string>> choices)
        {
            if (si < 0 || si >= rec.Sections.Count) return null;
            var def = rec.Sections[si].Def;
            FieldDef f = def.Fields.FirstOrDefault(x => x.SourceName == src);
            if (f == null) { MissingFields.Add(def.Name + "." + src); return null; }
            return MakeField(si, f, width, icon, caption, choices);
        }

        public Control MakeField(int si, FieldDef f, int width, Bitmap icon, string caption, List<KeyValuePair<long, string>> choices)
        {
            used.Add(si + "|" + f.SourceName);
            string cap = caption ?? L.T(f.Label);
            bool ed = Editable(f);
            FieldControl c;
            int abs = rec.Sections[si].Base + f.Offset;
            if (f.IsFlags && f.FlagNames != null)
                c = new FlagsBox(this, rec, si, f, ed, cap, Math.Max(width, CardWidth - 50));
            else if (!f.IsText && f.Label.EndsWith("(0/1)") && IsBool(f, rec.File.Data, abs) && (rec.File.Original == null || IsBool(f, rec.File.Original, abs)))
                c = new BoolBox(this, rec, si, f, ed, cap.Replace(" (0/1)", "").Replace("(0/1)", "").Trim(), Math.Max(width, 170));
            else
            {
                Func<long, string> fallback = null;
                if (choices == null && f.Labels != null) choices = LabelChoices(f);
                if (choices == null && f.Ref != RefKind.None) choices = RefChoices(f.Ref, null);
                if (f.Ref != RefKind.None) { var k = f.Ref; fallback = n => "#" + n + "  " + Ws.ResolveRef(k, n); }
                c = new FieldBox(this, rec, si, f, ed, cap, width, icon, choices, fallback);
            }
            fields.Add(c);
            return c;
        }

        /// <summary>Zahlenfeld eines Eintrags aus einer anderen Tabelle. Zaehlt nicht fuer "Experte".</summary>
        public Control ForeignField(Record other, FieldWriter w, int si, string src, int width, string caption)
        {
            if (other == null || si < 0 || si >= other.Sections.Count) return null;
            var f = other.Sections[si].Def.Fields.FirstOrDefault(x => x.SourceName == src);
            if (f == null) { MissingFields.Add(other.Sections[si].Def.Name + "." + src); return null; }
            var c = new FieldBox(this, other, si, f, other.File.Valid && !f.Structural, caption, width, null, null, null) { WriterOverride = w };
            fields.Add(c);
            return c;
        }

        static bool IsBool(FieldDef f, byte[] d, int abs) { long v = f.ReadNumber(d, abs); return v == 0 || v == 1; }

        public static List<KeyValuePair<long, string>> LabelChoices(FieldDef f)
        {
            var l = new List<KeyValuePair<long, string>>();
            for (int i = 0; i < f.Labels.Length && i <= f.Max; i++) l.Add(new KeyValuePair<long, string>(i, L.T(f.Labels[i])));
            return l;
        }

        /// <summary>Namen aus Item-/Move-/Skill-Tabelle. Leere Plaetze bleiben aus der Liste (0 = leer).</summary>
        public List<KeyValuePair<long, string>> RefChoices(RefKind kind, Func<Record, bool> filter)
        {
            List<KeyValuePair<long, string>> all;
            if (filter == null && refCache.TryGetValue(kind, out all)) return all;
            string key = kind == RefKind.Item ? "ITEM.BIN" : kind == RefKind.Move ? "MS_PARAM.BIN" : "SK_PARAM.BIN";
            var pv = Ws.PrimaryVariant(key);
            var l = new List<KeyValuePair<long, string>>();
            if (pv == null) return null;
            var names = pv.Records.Select(r => Layouts.RawName(r)).ToList();
            var count = new Dictionary<string, int>();
            foreach (var n in names) { int c; count.TryGetValue(n, out c); count[n] = c + 1; }
            foreach (var r in pv.Records)
            {
                string n = names[r.Index];
                if (r.Index == 0) { l.Add(new KeyValuePair<long, string>(0, L.T("(leer)"))); continue; }
                if (n.Length == 0) continue;
                if (filter != null && !filter(r)) continue;
                l.Add(new KeyValuePair<long, string>(r.Index, count[n] > 1 ? n + "  #" + r.Index : n));
            }
            if (filter == null) refCache[kind] = l;
            return l;
        }

        // ------------------------------------------------------------ Auswahlkarte (Angriffe, Stufen)

        bool pickerUpdating;

        public void Picker(string key, string title, string caption, IList<string> names, Action<Card, int> fill, Func<int, string> liveName = null)
        {
            var card = NewCard(title);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 8) };
            row.Controls.Add(new Label { Text = caption, AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
            var box = new SafeCombo { Width = 300 };
            foreach (var n in names) box.Items.Add(n);
            var prev = Theme.Secondary("◀"); prev.Margin = new Padding(6, 0, 0, 0); prev.Height = 30;
            var next = Theme.Secondary("▶"); next.Margin = new Padding(4, 0, 0, 0); next.Height = 30;
            row.Controls.Add(box); row.Controls.Add(prev); row.Controls.Add(next);
            card.Add(row);
            card.Break();
            card.Keep = card.Inner.Controls.Count;
            int sel;
            if (!picks.TryGetValue(key, out sel) || sel < 0 || sel >= names.Count) sel = 0;
            Action show = () =>
            {
                if (pickerUpdating || box.IsDisposed || box.SelectedIndex < 0) return;
                picks[key] = box.SelectedIndex;
                bool b = building; building = true;
                try
                {
                    card.Inner.SuspendLayout();
                    card.ClearBody();
                    fill(card, box.SelectedIndex);
                    card.Inner.ResumeLayout();
                    card.Fit();
                }
                finally { building = b; }
            };
            box.SelectedIndexChanged += (s, e) => show();
            prev.Click += (s, e) => { if (!box.IsDisposed && box.SelectedIndex > 0) box.SelectedIndex--; };
            next.Click += (s, e) => { if (!box.IsDisposed && box.SelectedIndex < box.Items.Count - 1) box.SelectedIndex++; };
            if (names.Count > 0) box.SelectedIndex = sel;
            if (liveName != null)
                live.Add(() =>
                {
                    if (box.IsDisposed) return;
                    int keepSel = box.SelectedIndex;
                    pickerUpdating = true;
                    try
                    {
                        for (int i = 0; i < box.Items.Count; i++) { string n = liveName(i); if ((string)box.Items[i] != n) box.Items[i] = n; }
                        if (box.SelectedIndex != keepSel) box.SelectedIndex = keepSel;
                    }
                    finally { pickerUpdating = false; }
                });
        }

        // ------------------------------------------------------------ Experte

        /// <summary>Alle noch nicht gezeigten Felder der Abschnitte, eingeklappt. Muss als letztes in einer Karte kommen.</summary>
        List<KeyValuePair<int, FieldDef>> Rest(IEnumerable<int> sections)
        {
            var rest = new List<KeyValuePair<int, FieldDef>>();
            foreach (int si in sections)
                if (si >= 0 && si < rec.Sections.Count)
                    foreach (var f in rec.Sections[si].Def.Fields)
                        if (!used.Contains(si + "|" + f.SourceName)) rest.Add(new KeyValuePair<int, FieldDef>(si, f));
            return rest;
        }

        /// <summary>Eigene Karte "Experte" am Ende, nur wenn es noch ungezeigte Felder gibt.</summary>
        public void ExpertCard(IEnumerable<int> sections)
        {
            var list = sections.ToList();
            if (Rest(list).Count > 0) Expert(NewCard(L.T("Experte")), list);
        }

        public void Expert(Card card, IEnumerable<int> sections)
        {
            var rest = Rest(sections);
            if (rest.Count == 0) return;
            int unknown = rest.Count(x => !x.Value.Known);
            var link = Theme.Link((ExpertOpen ? "▾ " : "▸ ") + L.F("Experte: {0} weitere Felder", rest.Count) + (unknown > 0 ? " (" + L.F("davon {0} unbekannt", unknown) + ")" : ""));
            link.Margin = new Padding(0, 4, 0, 6);
            link.LinkClicked += (s, e) => { ExpertOpen = !ExpertOpen; FlushAll(); Defer(Rebuild); };
            card.Add(link);
            card.Break();
            if (!ExpertOpen) return;
            card.Note(L.T("Technische Felder und Felder, deren Bedeutung niemand belegt hat. Nur ändern, wenn du weißt, was sie bewirken. Zeiger und IDs sind gesperrt."));
            if (unknown > 0)
            {
                var cb = new CheckBox { Text = L.T("Unbekannte Felder bearbeitbar"), AutoSize = true, Checked = Settings.ShowUnknown, Margin = new Padding(0, 0, 0, 8) };
                cb.CheckedChanged += (s, e) => { Settings.ShowUnknown = cb.Checked; Settings.Save(); FlushAll(); Defer(Rebuild); };
                card.Add(cb);
                card.Break();
            }
            int lastSi = -1;
            bool multi = rest.Select(x => x.Key).Distinct().Count() > 1;
            foreach (var kv in rest)
            {
                if (multi && kv.Key != lastSi) { card.SubHeading(L.T(rec.Sections[kv.Key].Title)); lastSi = kv.Key; }
                var f = kv.Value;
                int w = f.IsText ? 320 : (f.Labels != null || f.Ref != RefKind.None) ? 240 : 150;
                card.Add(MakeField(kv.Key, f, w, null, null, null));
            }
        }
    }
}
