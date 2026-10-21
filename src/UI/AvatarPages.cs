using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>Karte fuer einen Bildplatz: Titel, Vorschau, Zustand, Hinweis, eine Hauptaktion, eine Nebenaktion.</summary>
    public class ImageCard : CardPanel
    {
        readonly PortraitBox pic = new PortraitBox { Size = new Size(176, 176) };
        readonly Label title = new Label { AutoSize = false, Height = 22, Font = Theme.BodyBold };
        readonly Label state = new Label { AutoSize = false, Height = 34 };
        readonly Label hint = new Label { AutoSize = false, Height = 64, ForeColor = Theme.Muted, Font = Theme.Small };
        readonly Label files = new Label { AutoSize = false, Height = 16, ForeColor = Theme.Muted, Font = Theme.Small };
        public readonly Button Choose = Theme.Primary(L.T("Wählen ..."));
        public readonly Button Reset = Theme.Secondary(L.T("Original"));

        public ImageCard(string titleText, string hintText, ToolTip tip)
        {
            Width = 236; Height = 404; Margin = new Padding(0, 0, 12, 12); BackColor = Theme.Surface;
            int w = Width - 24;
            title.SetBounds(12, 10, w, 22); title.Text = titleText;
            pic.Location = new Point((Width - pic.Width) / 2, 36); pic.Cursor = Cursors.Hand;
            state.SetBounds(12, 220, w, 34);
            hint.SetBounds(12, 256, w, 64); hint.Text = hintText;
            files.SetBounds(12, 322, w, 16);
            var row = new FlowLayoutPanel { Location = new Point(10, 350), Size = new Size(w + 4, 44), WrapContents = false };
            Choose.AutoSize = Reset.AutoSize = false;
            Choose.Height = Reset.Height = 32;
            Choose.Width = 104; Reset.Width = w - 104 - 6;
            Choose.AutoEllipsis = Reset.AutoEllipsis = true;
            Choose.Margin = new Padding(0); Reset.Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(Choose); row.Controls.Add(Reset);
            Controls.AddRange(new Control[] { title, pic, state, hint, files, row });
            tip.SetToolTip(hint, hintText);
            tip.SetToolTip(pic, L.T("Klicken, um ein Bild zu wählen"));
            pic.Click += (s, e) => { if (Choose.Enabled) Choose.PerformClick(); };
        }

        public void SetImage(RgbaImage img, string empty) { pic.EmptyText = empty; pic.SetImage(img); }
        public void SetState(string text, Color color) { state.Text = text; state.ForeColor = color; }
        public void SetFiles(string text) { files.Text = text; }
    }

    /// <summary>Leiste mit den sieben Figuren als Bildknoepfe.</summary>
    public class FigureStrip : FlowLayoutPanel
    {
        readonly List<RadioButton> buttons = new List<RadioButton>();
        public event Action<int> Selected;
        public int Current { get; private set; }
        readonly int size;

        public FigureStrip(int imageSize)
        {
            size = imageSize; AutoSize = true; WrapContents = false; Margin = new Padding(0, 4, 0, 12);
            for (int c = 1; c <= 7; c++)
            {
                int id = c;
                var rb = new RadioButton
                {
                    Appearance = Appearance.Button, Width = size + 24, Height = size + 34, TextImageRelation = TextImageRelation.ImageAboveText,
                    ImageAlign = ContentAlignment.TopCenter, TextAlign = ContentAlignment.BottomCenter, Padding = new Padding(0, 4, 0, 2),
                    Text = Avatars.CharName(c), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 0), BackColor = Theme.Surface, Font = Theme.Small, Cursor = Cursors.Hand
                };
                rb.FlatAppearance.CheckedBackColor = Theme.AccentSoft;
                rb.FlatAppearance.BorderColor = Theme.Border;
                rb.CheckedChanged += (s, e) => { if (rb.Checked && Current != id) { Current = id; if (Selected != null) Selected(id); } };
                buttons.Add(rb);
                Controls.Add(rb);
            }
        }

        public void Select(int id) { if (id >= 1 && id <= 7) buttons[id - 1].Checked = true; }

        public void SetMarker(int id, string suffix) { buttons[id - 1].Text = Avatars.CharName(id) + suffix; }

        public void SetImages(Func<int, RgbaImage> get)
        {
            for (int c = 1; c <= 7; c++)
            {
                var rb = buttons[c - 1];
                if (rb.Image != null) { var old = rb.Image; rb.Image = null; old.Dispose(); }
                RgbaImage img = null;
                try { img = get(c); } catch { }
                if (img != null) using (var b = img.ToBitmap()) rb.Image = new Bitmap(b, size, size);
            }
        }
    }

    // =====================================================================

    /// <summary>Seite "Avatare": Figur waehlen, vier Bildplaetze.</summary>
    public class AvatarPage : Panel
    {
        readonly Workspace ws;
        readonly Func<List<GalleryItem>> gallery;
        readonly Action<AvatarSlot> afterChange;
        readonly Action<string> status;
        readonly List<AvatarSlot> slots;
        readonly FigureStrip strip = new FigureStrip(56);
        readonly FlowLayoutPanel cards = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(1000, 0), Margin = new Padding(0) };
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000 };
        readonly List<KeyValuePair<AvatarSlot, ImageCard>> shown = new List<KeyValuePair<AvatarSlot, ImageCard>>();

        public AvatarPage(Workspace ws, Func<List<GalleryItem>> gallery, Action<AvatarSlot> afterChange, Action<string> status)
        {
            this.ws = ws; this.gallery = gallery; this.afterChange = afterChange; this.status = status;
            Dock = DockStyle.Fill; BackColor = Theme.Background; AutoScroll = true;
            slots = Avatars.BuildSlots(ws);
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(24, 18, 24, 24), Location = new Point(0, 0) };
            flow.Controls.Add(Theme.Hint(L.T("Figur wählen, dann auf ein Bild klicken. Rahmen, transparenter Rand und Füllflächen bleiben wie im Original, nur das Gesicht wird ersetzt."), 860));            flow.Controls.Add(strip);
            flow.Controls.Add(cards);
            Controls.Add(flow);
            strip.Selected += id => ShowFigure(id);
            strip.Select(3);
            RefreshAll();
        }

        void ShowFigure(int id)
        {
            cards.SuspendLayout();
            foreach (var kv in shown) { cards.Controls.Remove(kv.Value); kv.Value.Dispose(); }
            shown.Clear();
            foreach (var s in slots.Where(x => x.CharId == id))
            {
                var slot = s;
                var card = new ImageCard(slot.Title, slot.Hint, tip);
                card.Choose.Click += (o, e) => Choose(slot);
                card.Reset.Click += (o, e) => { slot.Reset(); Done(slot, L.F("{0}: auf Original zurückgesetzt.", slot.Title)); };
                shown.Add(new KeyValuePair<AvatarSlot, ImageCard>(slot, card));
                cards.Controls.Add(card);
            }
            cards.ResumeLayout();
            RefreshCards();
        }

        void Choose(AvatarSlot slot)
        {
            Cursor = Cursors.WaitCursor;
            List<GalleryItem> g;
            try { g = gallery(); } finally { Cursor = Cursors.Default; }
            string info = L.F("Zielplatz: {0}.", slot.SizeText) + "\r\n" + (slot.Framed
                ? L.T("Rahmen und transparenter Rand bleiben, das Bild füllt die Fläche innerhalb des Rahmens.")
                : L.T("Füllfläche und Rand bleiben, das Bild füllt den Inhaltsbereich."));
            using (var d = new ImagePicker(g, Avatars.CharName(slot.CharId) + " · " + slot.Title, info, src => slot.Preview(src), slot.CharId))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                try { slot.Apply(d.Chosen); Done(slot, L.F("{0} · {1}: {2} gesetzt ({3} Datei(en)). Noch nicht gespeichert (Strg+S).", Avatars.CharName(slot.CharId), slot.Title, d.ChosenLabel, slot.Files.Count)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Bild"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void Done(AvatarSlot slot, string msg)
        {
            status(msg);
            afterChange(slot);
            RefreshAll();
        }

        public void RefreshAll()
        {
            strip.SetImages(id =>
            {
                var s = slots.FirstOrDefault(x => x.CharId == id && x.Kind == SlotKind.BattleSmall);
                return s != null && s.Usable ? s.Current() : null;
            });
            for (int c = 1; c <= 7; c++)
                strip.SetMarker(c, slots.Any(x => x.CharId == c && x.Dirty) ? " ●" : "");
            RefreshCards();
        }

        void RefreshCards()
        {
            foreach (var kv in shown)
            {
                var s = kv.Key; var card = kv.Value;
                if (s.Files.Count == 0)
                {
                    card.SetImage(null, L.T("Datei nicht gefunden")); card.SetState(L.T("nicht vorhanden"), Theme.Muted); card.SetFiles("");
                    card.Choose.Enabled = card.Reset.Enabled = false; continue;
                }
                if (!s.Usable)
                {
                    card.SetImage(null, L.T("gesperrt")); card.SetState(L.T("GESPERRT"), Theme.Danger); card.SetFiles(s.Files.First(f => !f.Valid).Errors[0]);
                    card.Choose.Enabled = card.Reset.Enabled = false; continue;
                }
                card.SetImage(s.Current(), "");
                card.SetFiles(s.SizeText);
                if (s.Dirty) card.SetState("● " + L.T("geändert, ungespeichert"), Theme.Warning);
                else if (s.Modified) card.SetState("◆ " + L.T("eigenes Bild, gespeichert"), Theme.Changed);
                else card.SetState(L.T("Original"), Theme.Success);
                card.Choose.Enabled = true;
                card.Reset.Enabled = s.Modified || s.Dirty;
            }
        }
    }

    // =====================================================================

    /// <summary>Seite "Rage": Einstellungen in der Exe und Rage-Gesichter je Figur.</summary>
    public class RagePage : Panel
    {
        readonly Workspace ws;
        readonly RageFaces faces;
        readonly Func<List<GalleryItem>> gallery;
        readonly Action changed;
        readonly Action<string> status;
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000 };
        bool loading;

        readonly CheckBox chkEnabled = new CheckBox { Text = L.T("Rage-Modus aktiv"), AutoSize = true, Font = Theme.BodyBold, Margin = new Padding(0, 4, 0, 4) };
        readonly CheckBox chkElena = new CheckBox { Text = L.T("Rage endet, sobald Elena in der Gruppe ist") + "\r\n" + L.T("(Original: ab New Valmar keine Rage mehr)"), AutoSize = true, Margin = new Padding(0, 4, 0, 10) };
        readonly ComboBox cmbFigure = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly NumericUpDown numDuration = new NumericUpDown { Minimum = 60, Maximum = 36000, Increment = 60, Width = 100 };
        readonly ComboBox cmbThreshold = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly ComboBox cmbStart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly ComboBox cmbChance = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckBox chkMinOne = new CheckBox { Text = L.T("Wut fällt nie unter 1"), AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        readonly Label warn = new Label { AutoSize = true, MaximumSize = new Size(332, 0), ForeColor = Theme.Warning, BackColor = Theme.WarningSoft, Padding = new Padding(8), Margin = new Padding(0, 10, 0, 0), Visible = false };
        readonly Label exeState = new Label { AutoSize = true, MaximumSize = new Size(332, 0), Margin = new Padding(0, 12, 0, 0) };
        readonly LinkLabel resetRage = Theme.Link(L.T("Rage-Einstellungen auf Original zurücksetzen"));
        readonly Panel settingsCard = Theme.Card();

        
        readonly Label activeInfo = new Label { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 10) };
        ImageCard during, after;
        int faceFigure = 3;

        public RagePage(Workspace ws, RageFaces faces, Func<List<GalleryItem>> gallery, Action changed, Action<string> status)
        {
            this.ws = ws; this.faces = faces; this.gallery = gallery; this.changed = changed; this.status = status;
            Dock = DockStyle.Fill; BackColor = Theme.Background; AutoScroll = true;

            var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(24, 18, 24, 24), Location = new Point(0, 0) };
            outer.Controls.Add(Theme.Hint(L.T("Ändert grandia2.exe direkt. Wirkt nach Strg+S und Neustart des Spiels. Die Exe liegt im Backup, \"Original\" stellt sie bytegleich wieder her."), 880));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(BuildSettings());
            row.Controls.Add(BuildFaces());
            outer.Controls.Add(row);
            outer.Controls.Add(Theme.Hint(L.T("So arbeitet die Rage laut Exe: Die Figur sammelt Wut bei Treffern, eigenen Fehlschlägen und wenn Verbündete fallen. Ab der Wut-Schwelle (Original 100) entscheidet bei jedem Treffer ein Zufallswurf. Beim Auslösen: SP auf Maximum, IP-Leiste doppelt so schnell, Autopilot, das Kampfporträt wechselt auf das Rage-Gesicht. Am Ende kommt das Bild \"Nach der Rage\"."), 880));
            Controls.Add(outer);
            RefreshAll();
        }

        Control BuildSettings()
        {
            var c = settingsCard;
            c.Width = 372; c.Height = 920; c.Margin = new Padding(0, 0, 16, 16);
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Theme.Surface };
            f.Controls.Add(Theme.Strong(L.T("Einstellungen")));
            f.Controls.Add(chkEnabled);
            f.Controls.Add(chkElena);
            // Rage nur fuer Millenia (Wunsch 0.12.4). Die Auswahl bleibt intern, wird aber nicht angezeigt.
            for (int i = 1; i <= 7; i++) cmbFigure.Items.Add(Avatars.CharName(i));
            f.Controls.Add(Theme.Strong(L.T("Dauer")));
            var dur = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            dur.Controls.Add(numDuration);
            dur.Controls.Add(new Label { Text = L.T("Original 1200"), AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(8, 4, 0, 0) });
            f.Controls.Add(dur);
            f.Controls.Add(new Label { Text = L.T("Einheit: interne Spieltakte. Die Umrechnung in Sekunden ist nicht geprüft."), AutoSize = true, MaximumSize = new Size(332, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 2, 0, 0) });
            f.Controls.Add(Theme.Strong(L.T("Wut-Schwelle")));
            foreach (var v in RagePatch.ThresholdValues) cmbThreshold.Items.Add(v == 100 ? L.T("100 (Original)") : v == 0 ? L.T("0 (beim nächsten Auslöser)") : v.ToString());
            f.Controls.Add(cmbThreshold);
            f.Controls.Add(Small(L.T("Wut steigt bei Treffern auf Millenia um 2 bis 7, bei einigen Kampfereignissen um 5 bis 50. Erst ab der Schwelle setzt ein Treffer die Auslöse-Chance. Die Wut bleibt über Kämpfe und Spielstände erhalten und fällt erst nach einer Rage auf 0. Liegt sie schon über der Schwelle, ändert die Schwelle bis zur nächsten Rage nichts.")));
            f.Controls.Add(Theme.Strong(L.T("Wut bei Kampfbeginn")));
            cmbStart.Items.AddRange(new object[] { L.T("Aus dem Spielstand (Original)"), L.T("Leer (0): Schwelle gilt in jedem Kampf"), L.T("Voll (100): Rage ab dem ersten Treffer möglich") });
            f.Controls.Add(cmbStart);
            f.Controls.Add(chkMinOne);
            f.Controls.Add(Small(L.T("Zusatz zu jeder Auswahl oben: Die Wut aus dem letzten Kampf bleibt erhalten, ist zu Kampfbeginn aber mindestens 1 und fällt nach einer Rage auf 1 statt auf 0.")));
            f.Controls.Add(Theme.Strong(L.T("Auslöse-Chance")));
            cmbChance.Items.AddRange(new object[] { L.T("20 bis 50 % (Original)"), "50 %", "75 %", L.T("100 % (sicher)") });
            f.Controls.Add(cmbChance);
            f.Controls.Add(Small(L.T("Gewürfelt wird einmal je Auslöse-Ereignis (Treffer auf Millenia), sobald die Wut die Schwelle erreicht hat. Voll + 100 % = Rage beim ersten Treffer jedes Kampfes, auch nach New Valmar.")));
            f.Controls.Add(warn);
            f.Controls.Add(exeState);
            resetRage.Margin = new Padding(0, 8, 0, 0);
            f.Controls.Add(resetRage);
            c.Controls.Add(f);

            chkEnabled.CheckedChanged += (s, e) => OnRageChanged();
            chkElena.CheckedChanged += (s, e) => OnRageChanged();
            numDuration.ValueChanged += (s, e) => OnRageChanged();
            cmbThreshold.SelectedIndexChanged += (s, e) => OnRageChanged();
            cmbStart.SelectedIndexChanged += (s, e) => OnRageChanged();
            cmbChance.SelectedIndexChanged += (s, e) => OnRageChanged();
            chkMinOne.CheckedChanged += (s, e) => OnRageChanged();
            resetRage.LinkClicked += (s, e) => ApplyRage(RagePatch.Original(ws), L.T("Rage auf Original gesetzt."));
            return c;
        }

        static Label Small(string text)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(332, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 2, 0, 6) };
        }

        Control BuildFaces()
        {
            var c = Theme.Card();
            c.Width = 548; c.Height = 920; c.Margin = new Padding(0, 0, 0, 16);
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Theme.Surface };
            f.Controls.Add(Theme.Strong(L.T("Rage-Gesichter")));
            f.Controls.Add(new Label { Text = L.T("Millenias Rage-Gesichter. Eigene Bilder werden in Originalgröße (64 × 64 Pixel) ins Spiel geschrieben."), AutoSize = true, MaximumSize = new Size(510, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 6) });            f.Controls.Add(activeInfo);
            var cards = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            during = new ImageCard(L.T("Während der Rage"), L.T("Ersetzt beim Start der Rage das Kampfporträt. Im Original Millenias böses Lächeln (64 × 64)."), tip);
            after = new ImageCard(L.T("Nach der Rage"), L.T("Bleibt nach dem Ende der Rage bis Kampfende stehen. Standard: das normale Kampfporträt."), tip);
            foreach (var card in new[] { during, after })
            {
                card.Reset.Text = L.T("Standard");
                bool isDuring = card == during;
                card.Choose.Click += (o, e) => ChooseFace(isDuring);
                card.Reset.Click += (o, e) => { faces.Clear(faceFigure, isDuring); FaceChanged(L.F("{0}: Rage-Gesicht auf Standard gesetzt.", Avatars.CharName(faceFigure))); };
                cards.Controls.Add(card);
            }
            f.Controls.Add(cards);
            c.Controls.Add(f);
            return c;
        }

        int RageId { get { return 3; } }   // nur Millenia

        public void RefreshAll()
        {
            RageSettings upgrade = null;
            loading = true;
            try
            {
                if (!RagePatch.Available(ws))
                {
                    settingsCard.Enabled = false;
                    exeState.Text = ws.Exe == null ? L.T("grandia2.exe nicht gefunden.") : L.C(L.T("grandia2.exe GESPERRT")) + ws.Exe.Errors[0];
                    exeState.ForeColor = Theme.Danger;
                }
                else
                {
                    settingsCard.Enabled = true;
                    string w;
                    var s = RagePatch.Current(ws, out w);
                    if (RagePatch.NeedsLockUpgrade(ws.Exe.Data) || s.CharId != 3) { upgrade = s.Clone(); upgrade.CharId = 3; }
                    chkEnabled.Checked = s.Enabled;
                    chkElena.Checked = s.ElenaLock;
                    chkElena.Enabled = s.Enabled;
                    cmbFigure.SelectedIndex = s.CharId - 1;
                    numDuration.Value = Math.Max(numDuration.Minimum, Math.Min(numDuration.Maximum, s.Duration));
                    cmbThreshold.SelectedIndex = Math.Max(0, Array.IndexOf(RagePatch.ThresholdValues, s.Threshold));
                    cmbStart.SelectedIndex = s.StartWrath;
                    cmbChance.SelectedIndex = s.Chance;
                    chkMinOne.Checked = s.MinWrathOne;
                    UpdateWarnings(s, w);
                    var files = new[] { ws.Exe, ws.AngE, ws.AngS }.Where(x => x != null).ToList();
                    if (files.Any(x => x.Dirty)) { exeState.Text = "● " + L.T("Ungespeichert. Strg+S schreibt grandia2.exe und die Party-Dateien."); exeState.ForeColor = Theme.Warning; }
                    else if (files.Any(x => x.DiskState == "verändert")) { exeState.Text = "◆ " + L.T("grandia2.exe ist gegenüber dem Backup verändert."); exeState.ForeColor = Theme.Changed; }
                    else if (files.Any(x => x.Original == null)) { exeState.Text = L.T("Kein Backup der Exe: Speichern gesperrt."); exeState.ForeColor = Theme.Danger; }
                    else { exeState.Text = L.T("grandia2.exe entspricht dem Backup (Original)."); exeState.ForeColor = Theme.Success; }
                }
                faceFigure = 3;
                RefreshFaces();
            }
            finally { loading = false; }
            // Alter Stand (Elena-Sperre nur an einer Stelle aus): auf die neue, vollstaendige Umgehung bringen.
            if (upgrade != null) ApplyRage(upgrade, L.T("Rage auf Millenia und den neuen Stand gebracht. Speichern mit Strg+S."));
        }

        void RefreshFaces()
        {
            bool active = faceFigure == RageId;
            activeInfo.Text = active
                ? "★ " + L.F("{0} hat die Rage. Dieses Paar steht im Spiel.", Avatars.CharName(faceFigure))
                : L.F("Wird ins Spiel geschrieben, sobald {0} die Rage-Figur ist.", Avatars.CharName(faceFigure));
            activeInfo.ForeColor = active ? Theme.Success : Theme.Muted;
            // Anzeigen geht immer, sobald die beiden Rage-Dateien da sind. Eigene Bilder brauchen zusaetzlich einen Rahmen (FrameFor).
            var frame = RageFaces.FrameFor(ws, faceFigure);
            if (frame != null && frame.Fallback)
                activeInfo.Text += "\r\n" + L.F("Hinweis: sys\\btl_sys1_{0}.dds fehlt in deiner Installation. Rahmen für eigene Bilder kommt aus {1}.", faceFigure - 1, frame.Source);
            foreach (var isDuring in new[] { true, false })
            {
                var card = isDuring ? during : after;
                var f = isDuring ? ws.AngEFace : ws.AngSFace;
                bool ok = f != null && f.Valid;
                card.Choose.Enabled = ok && frame != null;
                if (!ok) { card.SetImage(null, L.T("Datei nicht gefunden")); card.SetState(L.T("nicht vorhanden"), Theme.Muted); card.Reset.Enabled = false; continue; }
                card.SetImage(faces.Preview(ws, faceFigure, isDuring), "");
                bool custom = faces.Custom(faceFigure, isDuring) != null;
                if (faces.IsPending(faceFigure, isDuring)) card.SetState("● " + L.T("geändert, ungespeichert"), Theme.Warning);
                else if (custom) card.SetState("◆ " + L.T("eigenes Bild"), Theme.Changed);
                else card.SetState(faceFigure == 3 ? L.T("Standard: Originalbild des Spiels") : L.T("Standard: Kampfporträt"), Theme.Success);
                card.Reset.Enabled = custom || faces.IsPending(faceFigure, isDuring);
                card.SetFiles(active && f != null ? Dds.SizeOf(f.Data).Width + "×" + Dds.SizeOf(f.Data).Height + " · " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f.RelPath.Replace('\\', '/'))) : "");
            }
        }

        void UpdateWarnings(RageSettings s, string readWarning)
        {
            var lines = new List<string>();
            if (readWarning != null) lines.Add(L.T("Exe uneinheitlich:") + " " + readWarning.Trim() + " " + L.T("Einmal auf Original zurücksetzen behebt das."));
            if (!s.Enabled) lines.Add(L.T("Rage ist aus. Keine Figur bekommt den Rage-Modus."));
            if (s.Enabled)
                lines.Add(L.F("Auslösen laut Exe nur, wenn {0} selbst im Kampf ist, genug Wut gesammelt hat (Treffer einstecken) und ein Zufallswurf gelingt. Ist {0} nicht in der Gruppe, passiert nichts.", Avatars.CharName(s.CharId)));
            if (s.Enabled && s.CharId == 2 && s.ElenaLock) lines.Add(L.T("Elena hat die Rage, aber die Elena-Sperre ist an. Dann löst die Rage nie aus. Haken oben entfernen."));
            warn.Text = string.Join("\r\n\r\n", lines.ToArray());
            warn.Visible = lines.Count > 0;
        }

        void OnRageChanged()
        {
            if (loading || !RagePatch.Available(ws)) return;
            ApplyRage(new RageSettings { Enabled = chkEnabled.Checked, ElenaLock = chkElena.Checked, CharId = 3, Duration = (int)numDuration.Value, Threshold = RagePatch.ThresholdValues[Math.Max(0, cmbThreshold.SelectedIndex)],
                StartWrath = Math.Max(0, cmbStart.SelectedIndex), Chance = Math.Max(0, cmbChance.SelectedIndex), MinWrathOne = chkMinOne.Checked }, null);
        }

        void ApplyRage(RageSettings s, string msg)
        {
            try
            {
                var notes = RagePatch.Apply(ws, s);
                faces.WriteToGame(ws, s.CharId);
                faceFigure = 3;
                status(msg ?? L.F("Rage: {0}, {1}, Dauer {2}{3}. Noch nicht gespeichert (Strg+S).", L.T(s.Enabled ? "an" : "aus"), Avatars.CharName(s.CharId), s.Duration,
                    s.Enabled ? ", " + L.T(s.ElenaLock ? "endet mit Elena" : "auch nach New Valmar") : ""));
                if (notes.Count > 0) MessageBox.Show(this, string.Join("\n", notes.Distinct().ToArray()), "Rage", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rage", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            changed();
            RefreshAll();
        }

        void ChooseFace(bool isDuring)
        {
            int id = faceFigure;
            Cursor = Cursors.WaitCursor;
            List<GalleryItem> g;
            try { g = gallery(); } finally { Cursor = Cursors.Default; }
            string title = Avatars.CharName(id) + " · " + L.T(isDuring ? "Während der Rage" : "Nach der Rage");
            string info = L.T("Zielplatz: 64 × 64 im Spiel (wie das Original), im Rahmen des Kampfporträts dieser Figur.") + "\r\n" +
                L.T("Tipp: Die schärfsten Quellen sind Menü (192 Pixel) und Dialog (96 Pixel). Millenias böses Lächeln als Dialogbild ist f0314.");
            using (var d = new ImagePicker(g, title, info, src => RageFaces.Compose(ws, id, src), id))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                try
                {
                    faces.SetCustom(id, isDuring, RageFaces.Compose(ws, id, d.Chosen));
                    FaceChanged(L.F("{0}: {1} gesetzt. Noch nicht gespeichert (Strg+S).", title, d.ChosenLabel));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Bild"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void FaceChanged(string msg)
        {
            if (RagePatch.Available(ws)) faces.WriteToGame(ws, RageId);
            status(msg);
            changed();
            RefreshAll();
        }

        /// <summary>Kampfportraet der Rage-Figur geaendert: Standard-Gesichter folgen.</summary>
        public void BattlePortraitChanged(int id)
        {
            if (RagePatch.Available(ws) && id == RageId) faces.WriteToGame(ws, RageId);
            RefreshAll();
        }
    }
}
