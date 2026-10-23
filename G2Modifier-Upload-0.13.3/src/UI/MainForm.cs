using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>
    /// Hauptfenster. Aufbau: Seitenleiste (Navigation), Kopfzeile (Seitentitel, Suche, Sprache),
    /// Inhalt (eine Seite), Speicherleiste (nur sichtbar, wenn etwas ungespeichert ist), Statuszeile.
    /// </summary>
    public class MainForm : Form
    {
        public const string AppName = "Grandia II Modifier";
        public const string Version = "0.13.3";
        const string DefaultSteamPath = @"C:\Program Files (x86)\Steam\steamapps\common\Grandia II Anniversary Edition";

        readonly Settings settings = Settings.Load();
        Workspace ws;
        BackupService backup;
        RageFaces faces = new RageFaces(null);
        List<GalleryItem> gallery;

        // Rahmen
        FlowLayoutPanel nav;
        Label pageTitle;
        TextBox search;
        Label searchCue;
        ComboBox langBox;
        Panel content, saveBar;
        Label saveText;
        ListBox searchResults;
        ToolStripStatusLabel statusText, backupText;

        // Seiten
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        readonly Dictionary<string, NavItem> navItems = new Dictionary<string, NavItem>();
        readonly Dictionary<string, string> pageTitles = new Dictionary<string, string>();
        readonly List<EditorTab> editors = new List<EditorTab>();
        AvatarPage avatarPage;
        RagePage ragePage;
        FilesPage filesPage;
        string currentKey = "start";

        public MainForm()
        {
            L.TrackMissing = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("G2M_MISSING"));
            L.Set(string.IsNullOrEmpty(settings.Language) ? DetectLanguage() : settings.Language);
            Text = AppName; StartPosition = FormStartPosition.CenterScreen;
            // Fenster passt sich dem Bildschirm an (Laptops mit Skalierung 125/150 % haben wenig Hoehe).
            var area = Screen.PrimaryScreen.WorkingArea;
            Width = Math.Min(1360, area.Width); Height = Math.Min(880, area.Height);
            MinimumSize = new Size(Math.Min(860, area.Width), Math.Min(540, area.Height));
            if (area.Width < 1360 || area.Height < 880) WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            Font = Theme.Body;
            BackColor = Theme.Background;
            BuildShell();
            ShowPage("start");
            Shown += (s, e) =>
            {
                if (!string.IsNullOrEmpty(settings.GameRoot) && Directory.Exists(settings.GameRoot)) OpenWorkspace(settings.GameRoot);
                else BuildPages();
            };
            FormClosing += (s, e) => { if (!ConfirmDiscardAll()) e.Cancel = true; };
            // Nur fuer Tests: G2M_CHECK=Datei prueft laufend, ob verworfene Steuerelemente noch im Fenster haengen
            string check = Environment.GetEnvironmentVariable("G2M_CHECK");
            if (!string.IsNullOrEmpty(check))
            {
                var t = new Timer { Interval = 300 };
                t.Tick += (s, e) => { var bad = new List<string>(); FindDisposed(this, bad); if (bad.Count > 0) try { File.AppendAllLines(check, bad); } catch { } };
                t.Start();
            }
        }

        static void FindDisposed(Control c, List<string> bad)
        {
            foreach (Control k in c.Controls)
            {
                if (k.IsDisposed) bad.Add("VERWORFEN IM FENSTER: " + k.GetType().Name + " in " + c.GetType().Name);
                else FindDisposed(k, bad);
            }
        }

        static string DetectLanguage()
        {
            string c = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Array.IndexOf(L.Codes, c) >= 0 ? c : "en";
        }

        // ------------------------------------------------------------ Rahmen

        void BuildShell()
        {
            SuspendLayout();
            Controls.Clear();

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };

            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Background };
            pageTitle = new Label { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Location = new Point(24, 18) };
            langBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            foreach (var n in L.Names) langBox.Items.Add(n);
            langBox.SelectedIndex = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            langBox.SelectedIndexChanged += (s, e) => SwitchLanguage(L.Codes[langBox.SelectedIndex]);
            search = new TextBox { Width = 340, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = Theme.Body };
            searchCue = new Label { Text = L.T("Suchen: Gegenstand, Move, Gegner, Seite ...") + "   Ctrl+F", AutoSize = false, ForeColor = Theme.Muted, BackColor = SystemColors.Window, Cursor = Cursors.IBeam, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            searchCue.Click += (s, e) => search.Focus();
            top.Controls.Add(pageTitle); top.Controls.Add(searchCue); top.Controls.Add(search); top.Controls.Add(langBox);
            top.Resize += (s, e) => LayoutTop(top);
            LayoutTop(top);
            search.TextChanged += (s, e) => { searchCue.Visible = search.Text.Length == 0 && !search.Focused; RunSearch(); };
            search.GotFocus += (s, e) => { searchCue.Visible = false; RunSearch(); };
            search.LostFocus += (s, e) => { searchCue.Visible = search.Text.Length == 0; BeginInvoke(new Action(() => { if (!searchResults.Focused) searchResults.Visible = false; })); };
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && searchResults.Visible && searchResults.Items.Count > 0) { searchResults.Focus(); searchResults.SelectedIndex = 0; e.Handled = true; }
                if (e.KeyCode == Keys.Enter && searchResults.Visible && searchResults.Items.Count > 0) { Go((Hit)searchResults.Items[0]); e.Handled = e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { search.Text = ""; searchResults.Visible = false; content.Focus(); }
            };

            searchResults = new ListBox { Visible = false, Width = 460, Height = 320, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
            searchResults.Click += (s, e) => { var h = searchResults.SelectedItem as Hit; if (h != null) Go(h); };
            searchResults.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { var h = searchResults.SelectedItem as Hit; if (h != null) Go(h); e.Handled = true; }
                if (e.KeyCode == Keys.Escape) { searchResults.Visible = false; search.Focus(); }
            };
            searchResults.LostFocus += (s, e) => { if (!search.Focused) searchResults.Visible = false; };

            saveBar = new CardPanel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Surface, Visible = false };
            saveText = new Label { AutoSize = true, Font = Theme.BodyBold, ForeColor = Theme.Warning, Location = new Point(24, 19) };
            var saveBtn = Theme.Primary(L.T("Speichern") + "   Ctrl+S");
            var discardBtn = Theme.Secondary(L.T("Verwerfen"));
            saveBtn.Anchor = discardBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveBtn.Click += (s, e) => Save();
            discardBtn.Click += (s, e) => DiscardAll();
            saveBar.Controls.Add(saveText); saveBar.Controls.Add(saveBtn); saveBar.Controls.Add(discardBtn);
            saveBar.Resize += (s, e) =>
            {
                saveBtn.Location = new Point(saveBar.Width - saveBtn.Width - 24, 12);
                discardBtn.Location = new Point(saveBtn.Left - discardBtn.Width - 8, 12);
            };

            var bar = new StatusStrip { BackColor = Theme.Background, SizingGrip = true };
            statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };
            backupText = new ToolStripStatusLabel { ForeColor = Theme.Muted };
            bar.Items.Add(statusText); bar.Items.Add(backupText);

            var side = new SidebarPanel { Dock = DockStyle.Left, Width = 232, BackColor = Theme.Sidebar };
            var brand = new Label { Text = "Grandia II\r\nModifier", Font = new Font(Theme.Title.FontFamily, 13f, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent, AutoSize = false, Height = 70, Dock = DockStyle.Top, Padding = new Padding(20, 16, 0, 0) };
            if (GameArt.Logo != null)
            {
                // Logo aus dem Titelbild des Spiels, darunter "Modifier"
                brand.Text = "Modifier"; brand.Image = GameArt.Logo; brand.ImageAlign = ContentAlignment.TopCenter; brand.TextAlign = ContentAlignment.BottomCenter;
                brand.Font = new Font(Theme.Title.FontFamily, 11f, FontStyle.Bold); brand.Padding = new Padding(0, 10, 0, 4);
                brand.Height = GameArt.Logo.Height + 40;
            }
            if (GameArt.AppIcon != null) Icon = GameArt.AppIcon;
            var ver = new Label { Text = L.T("Version") + " " + Version, ForeColor = Color.FromArgb(170, 185, 215), BackColor = Color.Transparent, Font = Theme.Small, Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(20, 6, 0, 0) };
            nav = new SidebarFlow { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Sidebar, Padding = new Padding(0) };
            side.Controls.Add(nav); side.Controls.Add(ver); side.Controls.Add(brand);

            Controls.Add(content);
            Controls.Add(top);
            Controls.Add(saveBar);
            Controls.Add(bar);
            Controls.Add(side);
            Controls.Add(searchResults);
            searchResults.BringToFront();
            ResumeLayout();
        }

        void LayoutTop(Panel top)
        {
            langBox.Location = new Point(top.Width - langBox.Width - 24, 20);
            search.Location = new Point(langBox.Left - search.Width - 12, 20);
            searchCue.SetBounds(search.Left + 4, search.Top + 3, search.Width - 8, search.Height - 6);
            searchCue.BringToFront();
        }

        void AddNavHeader(string text) { nav.Controls.Add(new NavHeader(text) { Width = 232, Margin = new Padding(0) }); }

        void AddNav(string key, string title)
        {
            var n = new NavItem(title) { Width = 232, Margin = new Padding(0) };
            n.Click += (s, e) => ShowPage(key);
            nav.Controls.Add(n);
            navItems[key] = n;
            pageTitles[key] = title;
        }

        // ------------------------------------------------------------ Seiten

        void BuildPages()
        {
            foreach (var p in pages.Values) p.Dispose();
            pages.Clear(); navItems.Clear(); pageTitles.Clear(); editors.Clear();
            avatarPage = null; ragePage = null; filesPage = null;
            nav.SuspendLayout();
            nav.Controls.Clear();
            AddNav("start", L.T("Start"));
            if (ws != null)
            {
                var bins = ws.Tables.Where(t => t.CopiesCanSync).ToList();
                if (bins.Count > 0) { AddNavHeader(L.T("Spieldaten")); foreach (var t in bins) AddNav(t.Key, L.T(t.Title)); }
                var foes = ws.Tables.Where(t => !t.CopiesCanSync).ToList();
                if (foes.Count > 0) { AddNavHeader(L.T("Gegner")); foreach (var t in foes) AddNav(t.Key, L.T(t.Title)); }
                AddNavHeader(L.T("Figuren"));
                AddNav("avatars", L.T("Avatare"));
                AddNav("rage", L.T("Rage"));
                AddNavHeader(L.T("System"));
                AddNav("files", L.T("Dateien und Backup"));
            }
            var about = new NavItem(L.T("Über und Quellen")) { Width = 232, Margin = new Padding(0) };
            about.Click += (s, e) => About();
            nav.Controls.Add(about);
            nav.ResumeLayout();
            if (!pageTitles.ContainsKey(currentKey)) currentKey = "start";
            ShowPage(currentKey);
            UpdateState();
        }

        Control CreatePage(string key)
        {
            if (key == "start") return new StartPage(ws, backup, faces, ShowPage, ChooseGameFolder, ScaleEnemies, pageTitles, settings.Version, SwitchVersion);
            if (ws == null) return null;
            if (key == "files")
                return filesPage = new FilesPage(ws, () => backup, RestoreFiles, ChooseGameFolder, () => ChooseBackupFolder(true),
                    () => settings.KeepSnapshots, SetKeepSnapshots, ShowSnapshots);
            if (key == "avatars")
                return avatarPage = new AvatarPage(ws, Gallery, slot =>
                {
                    if (slot.Kind == SlotKind.BattleSmall) { gallery = null; if (ragePage != null) ragePage.BattlePortraitChanged(slot.CharId); else faces.WriteToGame(ws, RageIdNow()); }
                    UpdateState();
                }, Status);
            if (key == "rage") return ragePage = new RagePage(ws, faces, Gallery, UpdateState, Status);
            var t = ws.Table(key);
            if (t == null) return null;
            var ed = new EditorTab(ws, t, settings, UpdateState, Status);
            ed.ScaleRequested = ScaleEnemies;
            ed.CurveRequested = () => LevelCurve(t);
            editors.Add(ed);
            return ed;
        }

        int RageIdNow()
        {
            if (!RagePatch.Available(ws)) return 3;
            string w;
            return RagePatch.Current(ws, out w).CharId;
        }

        void ShowPage(string key)
        {
            Control page;
            if (!pages.TryGetValue(key, out page))
            {
                page = CreatePage(key);
                if (page == null) return;
                page.Dock = DockStyle.Fill;
                pages[key] = page;
            }
            else RefreshPage(page);
            content.SuspendLayout();
            content.Controls.Clear();
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = key == "start" || key == "avatars" || key == "rage" ? new Padding(0) : new Padding(20, 0, 20, 12), BackColor = Theme.Background };
            wrap.Controls.Add(page);
            content.Controls.Add(wrap);
            content.ResumeLayout();
            currentKey = key;
            foreach (var kv in navItems) { kv.Value.Active = kv.Key == key; kv.Value.Invalidate(); }
            string title;
            pageTitle.Text = pageTitles.TryGetValue(key, out title) ? (key == "start" ? AppName : title) : AppName;
            searchResults.Visible = false;
            DumpMissing();
        }

        /// <summary>Nur fuer Tests: G2M_MISSING=Datei schreibt nicht uebersetzte Texte mit.</summary>
        static void DumpMissing()
        {
            string path = Environment.GetEnvironmentVariable("G2M_MISSING");
            if (string.IsNullOrEmpty(path)) return;
            L.TrackMissing = true;
            try { File.WriteAllLines(path, L.Missing.OrderBy(x => x).ToArray()); } catch { }
        }

        void RefreshPage(Control p)
        {
            var ed = p as EditorTab; if (ed != null) ed.RefreshAll();
            var ap = p as AvatarPage; if (ap != null) ap.RefreshAll();
            var rp = p as RagePage; if (rp != null) rp.RefreshAll();
            var fp = p as FilesPage; if (fp != null) fp.RefreshAll();
            var sp = p as StartPage; if (sp != null) sp.RefreshAll();
        }

        void RefreshAllPages() { foreach (var p in pages.Values) RefreshPage(p); }

        List<GalleryItem> Gallery() { return gallery ?? (gallery = Avatars.BuildGallery(ws)); }

        // ------------------------------------------------------------ Zustand

        void Status(string s) { statusText.Text = s; }

        void UpdateState()
        {
            int dirty = ws == null ? 0 : ws.DirtyFiles.Count();
            bool facesDirty = faces.Dirty;
            Text = AppName + (ws != null ? "  -  " + ws.GameRoot : "") + (dirty > 0 || facesDirty ? "  ●" : "");
            saveBar.Visible = dirty > 0 || facesDirty;
            saveText.Text = "● " + (dirty == 1 ? L.T("1 Datei mit ungespeicherten Änderungen") : L.F("{0} Dateien mit ungespeicherten Änderungen", dirty));
            if (dirty == 0 && facesDirty) saveText.Text = "● " + L.T("Rage-Gesichter ungespeichert");
            backupText.Text = backup != null ? L.C(L.T("Backup")) + backup.Dir : L.T("KEIN BACKUP-ORDNER: Speichern gesperrt");
            backupText.ForeColor = backup != null ? Theme.Muted : Theme.Danger;
            if (ws != null)
            {
                foreach (var t in ws.Tables) Mark(t.Key, t.Variants.Any(v => v.Files.Any(f => f.Dirty)));
                var rageFiles = new[] { ws.Exe, ws.AngE, ws.AngS, ws.AngEFace, ws.AngSFace };
                Mark("rage", rageFiles.Any(f => f != null && f.Dirty) || facesDirty);
                Mark("avatars", ws.AllFiles.Any(f => f.FormatKey == "DDS" && f.Dirty && !rageFiles.Contains(f)));
            }
            if (filesPage != null && currentKey == "files") filesPage.RefreshAll();
        }

        void Mark(string key, bool on)
        {
            NavItem n;
            if (navItems.TryGetValue(key, out n) && n.Marked != on) { n.Marked = on; n.Invalidate(); }
        }

        // ------------------------------------------------------------ Suche

        class Hit
        {
            public string Text; public string Key; public Record Rec;
            public override string ToString() { return Text; }
        }

        void RunSearch()
        {
            string q = search.Text.Trim();
            if (q.Length < 2 || ws == null) { searchResults.Visible = false; return; }
            string ql = q.ToLowerInvariant();
            var hits = new List<Hit>();
            foreach (var kv in pageTitles)
                if (kv.Value.ToLowerInvariant().Contains(ql)) hits.Add(new Hit { Text = "→  " + kv.Value, Key = kv.Key });
            for (int c = 1; c <= 7; c++)
                if (Avatars.CharName(c).ToLowerInvariant().Contains(ql)) hits.Add(new Hit { Text = L.T("Avatare") + "  ·  " + Avatars.CharName(c), Key = "avatars" });
            foreach (var t in ws.Tables)
            {
                var v = t.Variants.FirstOrDefault(x => x.Valid);
                if (v == null) continue;
                foreach (var r in v.Records)
                {
                    if (hits.Count >= 80) break;
                    if (r.Label.ToLowerInvariant().Contains(ql)) hits.Add(new Hit { Text = L.T(t.Title) + "  ·  " + r.Label.Trim(), Key = t.Key, Rec = r });
                }
            }
            searchResults.BeginUpdate();
            searchResults.Items.Clear();
            foreach (var h in hits) searchResults.Items.Add(h);
            if (hits.Count == 0) searchResults.Items.Add(new Hit { Text = L.T("Keine Treffer") });
            searchResults.EndUpdate();
            var pt = PointToClient(search.Parent.PointToScreen(new Point(search.Left, search.Bottom + 2)));
            searchResults.Location = new Point(Math.Max(0, pt.X + search.Width - searchResults.Width), pt.Y);
            searchResults.Height = Math.Min(360, Math.Max(1, searchResults.Items.Count) * searchResults.ItemHeight + 6);
            searchResults.Visible = true;
            searchResults.BringToFront();
        }

        void Go(Hit h)
        {
            if (h.Key == null) return;
            searchResults.Visible = false;
            search.Text = "";
            ShowPage(h.Key);
            if (h.Rec != null)
            {
                Control p;
                if (pages.TryGetValue(h.Key, out p)) { var ed = p as EditorTab; if (ed != null) ed.Show(h.Rec); }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.F5 && ws != null) { if (ConfirmDiscardAll()) OpenWorkspace(settings.GameRoot); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------ Sprache

        void SwitchLanguage(string code)
        {
            if (code == L.Lang) return;
            foreach (var ed in editors) if (ed.HasPending && !ed.ResolvePendingBeforeSave()) { langBox.SelectedIndex = Array.IndexOf(L.Codes, L.Lang); return; }
            Cursor = Cursors.WaitCursor;
            try
            {
                settings.Language = code; settings.Save();
                L.Set(code);
                if (ws != null)
                {
                    // Neu laden, damit auch Meldungen der Dateipruefung in der neuen Sprache vorliegen.
                    // Ungespeicherte Aenderungen und geprueftes Backup werden uebernommen.
                    var old = ws;
                    var fresh = Workspace.Open(old.GameRoot, old.Chosen);
                    foreach (var f in fresh.AllFiles)
                    {
                        var o = old.AllFiles.FirstOrDefault(x => x.RelPath == f.RelPath);
                        if (o == null || !Bin.Equal(o.Disk, f.Disk)) continue;
                        f.Data = (byte[])o.Data.Clone();
                        f.Original = o.Original;
                    }
                    ws = fresh;
                    gallery = null;
                }
                string key = currentKey;
                BuildShell();
                currentKey = key;
                BuildPages();
                ActiveControl = content;
                searchCue.Visible = true;
                Status(L.T("Sprache umgestellt."));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { Cursor = Cursors.Default; }
        }

        // ------------------------------------------------------------ Spielordner und Backup

        void ChooseGameFolder()
        {
            if (!ConfirmDiscardAll()) return;
            using (var d = new FolderBrowserDialog())
            {
                d.Description = L.T("Grandia-II-Spielordner wählen (der Ordner mit grandia2.exe und content).");
                d.ShowNewFolderButton = false;
                if (Directory.Exists(settings.GameRoot)) d.SelectedPath = settings.GameRoot;
                else if (Directory.Exists(DefaultSteamPath)) d.SelectedPath = DefaultSteamPath;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                OpenWorkspace(d.SelectedPath);
            }
        }

        bool ChooseBackupFolder(bool force)
        {
            if (ws == null) { MessageBox.Show(this, L.T("Bitte zuerst den Spielordner öffnen.")); return false; }
            if (!force && BackupService.CheckLocation(ws.GameRoot, settings.BackupRoot) == null && Directory.Exists(settings.BackupRoot)) return true;
            while (true)
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.Description = L.T("Backup-Ordner wählen. Er muss AUSSERHALB des Spielordners liegen, z. B. Dokumente\\Grandia2-Backup. Ohne Backup-Ordner speichert das Tool nichts.");
                    d.ShowNewFolderButton = true;
                    if (Directory.Exists(settings.BackupRoot)) d.SelectedPath = settings.BackupRoot;
                    else d.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    string err = BackupService.CheckLocation(ws.GameRoot, d.SelectedPath);
                    if (err != null) { MessageBox.Show(this, L.T(err), L.T("Backup-Ordner ungültig"), MessageBoxButtons.OK, MessageBoxIcon.Warning); continue; }
                    settings.BackupRoot = d.SelectedPath; settings.Save();
                    if (force) { InitBackup(); BuildPages(); }
                    return true;
                }
            }
        }

        void OpenWorkspace(string root)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                ws = Workspace.Open(root, settings.Version);
                settings.GameRoot = ws.GameRoot; settings.Save();
                GameArt.Load(ws);
                BuildShell();
                ActiveControl = content;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, ex.Message, L.T("Spielordner"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                BuildPages();
                return;
            }
            finally { Cursor = Cursors.Default; }

            backup = null; gallery = null;
            faces = new RageFaces(null);
            if (ChooseBackupFolder(false)) InitBackup();
            RepairRageFaceSize();
            BuildPages();
            int locked = ws.AllFiles.Count(f => !f.Valid);
            Status(ws.Edition + ": " + L.F("{0} Dateien geladen", ws.AllFiles.Count) + (locked > 0 ? ", " + L.F("{0} gesperrt (siehe Dateien und Backup)", locked) : "") + ".");
        }

        /// <summary>Schalter auf der Startseite: Spielversion umstellen und den Spielordner neu oeffnen.</summary>
        void SwitchVersion(GameVersion v)
        {
            if (v == settings.Version) return;
            if (!ConfirmDiscardAll()) { BuildPages(); return; }
            settings.Version = v; settings.Save();
            if (!string.IsNullOrEmpty(settings.GameRoot) && Directory.Exists(settings.GameRoot)) OpenWorkspace(settings.GameRoot);
            else BuildPages();
        }

        /// <summary>Altlast bis 0.12.8: Rage-Gesichter in 128x128 bringen das Spiel zum Absturz. Hier auf 64x64 umstellen.</summary>
        void RepairRageFaceSize()
        {
            if (ws == null || !RageFaces.DiskHasWrongSize(ws)) return;
            try
            {
                faces.WriteToGame(ws, RageIdNow());
                UpdateState();
                MessageBox.Show(this,
                    L.T("Die Rage-Gesichter (party\\c03ang_e und c03ang_s) haben im Spielordner 128 x 128 Pixel. So hat sie das Tool bis Version 0.12.8 geschrieben. Diese Größe kann das Spiel zum Absturz bringen.") + "\n\n" +
                    L.T("Das Tool hat sie auf die Originalgröße 64 x 64 umgestellt. Deine eigenen Bilder bleiben erhalten.") + "\n\n" +
                    L.T("Jetzt Strg+S drücken, dann startet das Spiel wieder."),
                    L.T("Rage-Gesichter"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Rage-Gesichter"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        void InitBackup()
        {
            try
            {
                var b = new BackupService(ws.GameRoot, settings.BackupRoot, BackupService.FolderFor(ws.Version));
                bool first = !File.Exists(b.ManifestPath);
                if (first)
                    MessageBox.Show(this,
                        L.F("Jetzt werden alle {0} bearbeitbaren Dateien gesichert nach:", ws.AllFiles.Count) + "\n" + b.Dir + "\n\n" +
                        L.T("Diese Sicherung gilt ab jetzt als Original und wird nie überschrieben.") + "\n\n" +
                        L.T("Wichtig: Falls die Spieldateien schon gemoddet sind, vorher abbrechen und in Steam \"Dateien auf Fehler überprüfen\" ausführen."),
                        L.T("Erstes Backup"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                int n = b.EnsureBackups(ws.AllFiles, (i, total) => { Status(L.F("Backup {0}/{1} ...", i, total)); Application.DoEvents(); });
                b.KeepSnapshots = settings.KeepSnapshots;
                backup = b;
                faces = new RageFaces(b.Dir);
                if (!first && n > 0)
                    MessageBox.Show(this, L.F("{0} weitere Dateien wurden neu gesichert (z. B. grandia2.exe und Porträts).", n) + "\n\n" +
                        L.T("Diese Sicherung gilt ab jetzt als Original. Falls du diese Dateien schon mit einem anderen Werkzeug verändert hattest, stimmt dieses \"Original\" nicht. Dann in Steam \"Dateien auf Fehler überprüfen\" ausführen und danach einen neuen, leeren Backup-Ordner wählen."),
                        L.T("Backup erweitert"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (b.Problems.Count > 0)
                    MessageBox.Show(this, L.T("Probleme mit dem Backup. Diese Dateien werden NICHT gespeichert:") + "\n\n" + string.Join("\n", b.Problems.Select(p => L.C(p.Key) + p.Value).Take(15).ToArray()),
                        L.T("Backup"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (faces.Problems.Count > 0) MessageBox.Show(this, string.Join("\n", faces.Problems.ToArray()), L.T("Rage-Gesichter"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Status(n > 0 ? L.F("{0} Dateien gesichert, {1} bereits vorhanden und geprüft.", n, ws.AllFiles.Count - n) : L.F("Backup geprüft: alle {0} Sicherungen stimmen mit dem Manifest überein.", ws.AllFiles.Count));
            }
            catch (Exception ex)
            {
                backup = null;
                MessageBox.Show(this, ex.Message, L.T("Backup"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateState();
        }

        // ------------------------------------------------------------ Speichern, Verwerfen, Wiederherstellen

        bool ConfirmDiscardAll()
        {
            if (ws == null) return true;
            foreach (var ed in editors) if (ed.HasPending && !ed.ResolvePendingBeforeSave()) return false;
            if (!ws.DirtyFiles.Any() && !faces.Dirty) return true;
            var r = MessageBox.Show(this, L.F("Es gibt ungespeicherte Änderungen in {0} Datei(en). Jetzt speichern?", ws.DirtyFiles.Count()), AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save();
            return true;
        }

        bool GameRunning()
        {
            try { return Process.GetProcessesByName("grandia2").Length > 0; } catch { return false; }
        }

        bool Save()
        {
            if (ws == null) return false;
            foreach (var ed in editors) if (ed.HasPending && !ed.ResolvePendingBeforeSave()) return false;
            if (!ws.DirtyFiles.Any() && !faces.Dirty) { Status(L.T("Keine Änderungen zu speichern.")); return true; }
            if (backup == null)
            {
                MessageBox.Show(this, L.T("Ohne gültigen Backup-Ordner wird nichts gespeichert. Bitte jetzt einen Ordner außerhalb des Spielordners wählen."), L.T("Backup fehlt"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (!ChooseBackupFolder(true) || backup == null) return false;
            }
            if (GameRunning() && MessageBox.Show(this, L.T("Grandia II läuft gerade. Änderungen wirken erst nach einem Neustart des Spiels. Trotzdem speichern?"), AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            List<string> report;
            try { report = backup.SaveDirty(ws); }
            catch (Exception ex) { MessageBox.Show(this, L.T("Fehler beim Speichern:") + "\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            bool allOk = backup.LastFailures == 0;
            if (allOk)
            {
                try { if (faces.Dirty) { faces.Flush(); report.Add(L.T("Rage-Gesichter im Backup-Ordner gesichert.")); } }
                catch (Exception ex) { report.Add(L.T("Rage-Gesichter konnten nicht gesichert werden:") + " " + ex.Message); allOk = false; }
            }
            MessageBox.Show(this, string.Join("\n", report.ToArray()) + "\n\n" + L.C(L.T("Protokoll")) + backup.LogPath, L.T(allOk ? "Gespeichert" : "Teilweise gespeichert"), MessageBoxButtons.OK, allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            RefreshAllPages();
            UpdateState();
            Status(L.T(allOk ? "Gespeichert." : "Nicht alles gespeichert, siehe Meldung."));
            return allOk;
        }

        void DiscardAll()
        {
            if (ws == null) return;
            int n = ws.DirtyFiles.Count();
            if (MessageBox.Show(this, L.F("Alle ungespeicherten Änderungen in {0} Datei(en) verwerfen?", n), AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var f in ws.DirtyFiles.ToList()) f.Data = (byte[])f.Disk.Clone();
            faces.DiscardPending();
            gallery = null;
            RefreshAllPages();
            UpdateState();
            Status(L.T("Änderungen verworfen."));
        }

        void RestoreFiles(List<GameFile> files)
        {
            if (backup == null || files.Count == 0) return;
            if (MessageBox.Show(this, L.F("{0} Datei(en) aus dem Backup wiederherstellen? Ungespeicherte Änderungen daran gehen verloren.", files.Count), AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var errs = new List<string>();
            if (backup.KeepSnapshots)
            {
                string err;
                var changing = files.Where(f => f.Original != null && f.Disk != null && !Bin.Equal(f.Disk, f.Original)).ToList();
                if (changing.Count > 0 && backup.CreateSnapshot(changing, L.T("vor dem Wiederherstellen des Originals"), out err) == null)
                {
                    MessageBox.Show(this, err + "\n\n" + L.T("Es wurde nichts wiederhergestellt."), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            foreach (var f in files) { var e = backup.Restore(f); if (e != null) errs.Add(e); }
            gallery = null;
            foreach (var ed in editors) ed.FillList();
            RefreshAllPages();
            UpdateState();
            MessageBox.Show(this, L.F("{0} Datei(en) wiederhergestellt.", files.Count - errs.Count) + (errs.Count > 0 ? "\n\n" + string.Join("\n", errs.ToArray()) : ""));
        }

        void SetKeepSnapshots(bool on)
        {
            settings.KeepSnapshots = on; settings.Save();
            if (backup != null) backup.KeepSnapshots = on;
            Status(on ? L.T("Zwischenstände an: Vor jedem Speichern wird der bisherige Stand gesichert.") : L.T("Zwischenstände aus."));
        }

        void ShowSnapshots()
        {
            if (backup == null) { MessageBox.Show(this, L.T("Kein Backup-Ordner gewählt."), AppName); return; }
            using (var d = new SnapshotDialog(backup))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen == null) return;
                var names = d.Chosen.Entries.Select(e => e[0]).ToList();
                var dirty = ws.AllFiles.Where(f => f.Dirty && names.Contains(f.RelPath, StringComparer.OrdinalIgnoreCase)).ToList();
                if (MessageBox.Show(this, L.F("Zwischenstand {0} zurückspielen ({1} Datei(en))?", d.Chosen.Name, names.Count) +
                    (dirty.Count > 0 ? "\n\n" + L.F("Ungespeicherte Änderungen an {0} dieser Datei(en) gehen verloren.", dirty.Count) : ""),
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int fails;
                List<string> report;
                try { report = backup.RestoreSnapshot(ws, d.Chosen, out fails); }
                catch (Exception ex) { MessageBox.Show(this, L.T("Fehler beim Zurückspielen:") + "\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                gallery = null;
                foreach (var ed in editors) ed.FillList();
                RefreshAllPages();
                UpdateState();
                MessageBox.Show(this, string.Join("\n", report.ToArray()) + "\n\n" + L.C(L.T("Protokoll")) + backup.LogPath, L.T(fails == 0 ? "Zurückgespielt" : "Teilweise zurückgespielt"),
                    MessageBoxButtons.OK, fails == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        void ScaleEnemies()
        {
            if (ws == null) return;
            using (var d = new ScaleDialog(ws))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var res = Batch.Scale(d.Chosen.SelectMany(v => v.Records), d.Rules);
                RefreshAllPages();
                UpdateState();
                MessageBox.Show(this, L.F("{0} Werte in {1} Einträgen geändert", res.Changed, res.Records) + (res.Clamped > 0 ? ", " + L.F("{0} auf das Maximum des Feldtyps begrenzt", res.Clamped) : "") + ".\n\n" + L.T("Noch nicht gespeichert: Strg+S."), L.T("Skalieren"));
            }
        }

        void LevelCurve(TableInfo t)
        {
            using (var d = new CurveDialog(t))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var res = d.Result;
                RefreshAllPages();
                UpdateState();
                MessageBox.Show(this, L.F("{0} Werte in {1} Einträgen geändert", res.Changed, res.Records) + (res.Clamped > 0 ? ", " + L.F("{0} auf das Maximum des Feldtyps begrenzt", res.Clamped) : "") + ".\n\n" + L.T("Noch nicht gespeichert: Strg+S."), L.T("Kurve und Multiplikator"));
            }
        }

        void About()
        {
            MessageBox.Show(this,
                AppName + " " + Version + "\n\n" +
                L.T("Editor für Grandia II HD Remaster / Anniversary Edition (PC), aufgebaut nach dem Schema des Spira Modifier.") + "\n\n" +
                L.T("Byte-Layouts: G2DataGUI von krogenth (MIT-Lizenz), github.com/krogenth/G2DataGUI.") + "\n" +
                L.T("Bezeichnungslisten (Animationen, Spezialeffekte, Gegnertypen) ebenfalls von dort, englisch belassen.") + "\n" +
                L.T("Felder, die dort als \"Unknown\" geführt sind, heißen hier \"Unbekannt\" und sind standardmäßig gesperrt.") + "\n\n" +
                L.T("Rage und Avatare: eigene Analyse der grandia2.exe (Steam) per Ghidra. Jede Patchstelle wird vor dem Schreiben gegen das erwartete Bytemuster geprüft.") + "\n\n" +
                L.T("Spielbegriffe in Englisch und Französisch: aus den offiziellen Texten des Spiels (content\\data\\text).") +
                (L.Lang != "de" ? "\n" + L.C(L.T("Sprachdatei")) + L.LoadedFrom : "") + "\n\n" +
                L.T("Ohne Gewähr. Backups liegen im gewählten Backup-Ordner."), L.T("Über"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
