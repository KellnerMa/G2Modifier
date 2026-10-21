using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>Startseite: Kacheln zu jedem Bereich, Werkzeuge, Zustand von Spielordner und Backup.</summary>
    public class StartPage : Panel
    {
        readonly Workspace ws;
        readonly BackupService backup;
        readonly RageFaces faces;
        readonly Action<string> show;
        readonly Action chooseGame, scale;
        readonly Dictionary<string, string> titles;
        readonly GameVersion chosen;
        readonly Action<GameVersion> switchVersion;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(24, 12, 24, 24), Location = new Point(0, 0) };
        readonly List<KeyValuePair<string, Tile>> tiles = new List<KeyValuePair<string, Tile>>();

        static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            { "ITEM.BIN", "Preise, Ausrüstungswerte und Wirkung von Verbrauchsgegenständen" },
            { "MS_PARAM.BIN", "Kosten, Stärke, Reichweite und Wirkzeit von Moves und Zaubern" },
            { "SK_PARAM.BIN", "Grundwerte und Münzkosten der Skills" },
            { "TB_MAGIC.BIN", "Welche Zauber ein Manaei enthält und ab welcher Stufe" },
            { "TB_SKILL.BIN", "Welche Skills ein Skillbuch enthält" },
            { "TB_SPCL.BIN", "Spezialtechniken je Figur und ab wann sie verfügbar sind" },
            { "PC_INIT.BIN", "Startausrüstung und Grundwerte der Figuren" },
            { "TB_LVUP.BIN", "Wertezuwachs je Stufe und Figur" },
            { "ENEMY", "HP, Werte, Belohnungen und Angriffe normaler Gegner" },
            { "BOSS", "HP, Werte, Belohnungen und Angriffe der Bosse" },
            { "avatars", "Porträts für Menü, Dialog, Kampf und Siegbildschirm" },
            { "rage", "Millenias Rage: an/aus, Wut-Schwelle, Auslöse-Chance, eigene Rage-Gesichter" },
        };

        public StartPage(Workspace ws, BackupService backup, RageFaces faces, Action<string> show, Action chooseGame, Action scale, Dictionary<string, string> titles,
                         GameVersion chosen, Action<GameVersion> switchVersion)
        {
            this.ws = ws; this.backup = backup; this.faces = faces; this.show = show; this.chooseGame = chooseGame; this.scale = scale; this.titles = titles;
            this.chosen = chosen; this.switchVersion = switchVersion;
            Dock = DockStyle.Fill; BackColor = Theme.Background; AutoScroll = true;
            Controls.Add(flow);
            Build();
        }

        void Build()
        {
            flow.Controls.Clear(); tiles.Clear();
            if (ws == null)
            {
                var c = Theme.Card(); c.Width = 640; c.Height = 300;
                var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
                f.Controls.Add(Theme.Strong(L.T("Willkommen")));
                f.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 14),
                    Text = L.T("Wähle den Ordner, in dem Grandia II installiert ist (mit grandia2.exe und content). Danach fragt das Tool nach einem Backup-Ordner außerhalb des Spielordners. Ohne Backup wird nie etwas gespeichert.") });
                var b = Theme.Primary(L.T("Spielordner wählen ..."));
                b.Click += (s, e) => chooseGame();
                f.Controls.Add(b);
                f.Controls.Add(VersionRow());
                c.Controls.Add(f);
                flow.Controls.Add(c);
                return;
            }

            flow.Controls.Add(Theme.Strong(L.T("Was willst du ändern?")));
            var grid = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(1010, 0), Margin = new Padding(0, 4, 0, 8) };
            foreach (var key in titles.Keys.Where(k => k != "start" && k != "files"))
            {
                string d;
                var t = new Tile { Caption = titles[key], Description = Descriptions.TryGetValue(key, out d) ? L.T(d) : "" };
                string k = key;
                t.Click += (s, e) => show(k);
                tiles.Add(new KeyValuePair<string, Tile>(key, t));
                grid.Controls.Add(t);
            }
            flow.Controls.Add(grid);

            flow.Controls.Add(Theme.Strong(L.T("Werkzeuge")));
            var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8) };
            var sc = new Tile { Caption = L.T("Gegner und Bosse skalieren"), Description = L.T("HP, Abwehr, Angriff und Belohnungen aller Gegner per Faktor ändern") };
            sc.Click += (s, e) => scale();
            tools.Controls.Add(sc);
            var files = new Tile { Caption = L.T("Dateien und Backup"), Description = L.T("Zustand jeder Datei, Wiederherstellen aus dem Backup") };
            files.Click += (s, e) => show("files");
            tools.Controls.Add(files);
            flow.Controls.Add(tools);

            flow.Controls.Add(Theme.Strong(L.T("Zustand")));
            var card = Theme.Card(); card.Width = 1000; card.Height = 196;
            var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Surface };
            info.Controls.Add(new Label { AutoSize = true, Text = L.C(L.T("Spielordner")) + ws.GameRoot + "   (" + ws.Edition + ")" });
            info.Controls.Add(VersionRow());
            info.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(3, 6, 3, 0),
                Text = L.C(L.T("Backup")) + (backup != null ? backup.Dir : L.T("nicht festgelegt, Speichern gesperrt")),
                ForeColor = backup != null ? Theme.Text : Theme.Danger
            });
            int locked = ws.AllFiles.Count(f => !f.Valid), changed = ws.AllFiles.Count(f => f.DiskState == "verändert");
            info.Controls.Add(new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 0), ForeColor = Theme.Muted,
                Text = L.F("{0} Dateien geladen, {1} gegenüber dem Backup verändert, {2} gesperrt.", ws.AllFiles.Count, changed, locked) });
            var link = Theme.Link(L.T("Anderen Spielordner wählen ..."));
            link.Margin = new Padding(3, 8, 3, 0);
            link.LinkClicked += (s, e) => chooseGame();
            info.Controls.Add(link);
            card.Controls.Add(info);
            flow.Controls.Add(card);
            RefreshAll();
        }

        /// <summary>Schalter Spielversion. Wechsel oeffnet den Spielordner neu (nach Rueckfrage bei ungespeicherten Aenderungen).</summary>
        Control VersionRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0), BackColor = Theme.Surface };
            row.Controls.Add(new Label { AutoSize = true, Text = L.C(L.T("Spielversion")), Padding = new Padding(0, 5, 0, 0) });
            var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            var values = new[] { GameVersion.Auto, GameVersion.HdRemaster, GameVersion.Anniversary };
            foreach (var v in values)
                cmb.Items.Add(v == GameVersion.Auto && ws != null && ws.Chosen == GameVersion.Auto
                    ? L.F("Automatisch (erkannt: {0})", GameVersions.Name(ws.Version)) : GameVersions.Name(v));
            cmb.SelectedIndex = Array.IndexOf(values, chosen);
            // Erst nach dem Ereignis umschalten: das Neuladen baut diese Seite (und damit die ComboBox) neu auf.
            cmb.SelectedIndexChanged += (s, e) => { var v = values[cmb.SelectedIndex]; BeginInvoke(new Action(() => switchVersion(v))); };
            row.Controls.Add(cmb);
            var box = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Surface };
            box.Controls.Add(row);
            box.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(3, 2, 3, 0),
                Text = L.T("HD Remaster liest content\\data\\afs, Anniversary Edition liest data\\afs. Eigener Backup-Ordner je Version.") });
            return box;
        }

        public void RefreshAll()
        {
            if (ws == null) return;
            foreach (var kv in tiles)
            {
                var t = ws.Table(kv.Key);
                if (t != null)
                {
                    var v = t.Variants.FirstOrDefault(x => x.Valid);
                    int dirty = t.Variants.Sum(x => x.Files.Count(f => f.Dirty));
                    kv.Value.Figure = (v != null ? L.F("{0} Einträge", v.Records.Count) : L.T("gesperrt")) + (dirty > 0 ? "  ·  ● " + L.T("ungespeichert") : "");
                }
                else if (kv.Key == "rage" && RagePatch.Available(ws))
                {
                    string w; var s = RagePatch.Current(ws, out w);
                    kv.Value.Figure = (s.Enabled ? L.T("an") : L.T("aus")) + "  ·  " + Avatars.CharName(s.CharId) + (s.Enabled && !s.ElenaLock ? "  ·  " + L.T("auch nach New Valmar") : "");
                }
                else if (kv.Key == "avatars")
                    kv.Value.Figure = L.F("{0} Bilddateien", ws.AllFiles.Count(f => f.FormatKey == "DDS"));
                kv.Value.Invalidate();
            }
        }
    }

    /// <summary>Dateien und Backup: Zustand jeder Datei, Wiederherstellen, Ordner wechseln.</summary>
    public class FilesPage : Panel
    {
        readonly Workspace ws;
        readonly Func<BackupService> backup;
        readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None };
        readonly Label info = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 92, ForeColor = Theme.Muted, Padding = new Padding(0, 4, 0, 8) };
        readonly CheckBox onlyChanged = new CheckBox { Text = L.T("Nur veränderte und gesperrte Dateien zeigen"), AutoSize = true, Margin = new Padding(16, 8, 0, 0) };
        readonly CheckBox keepSnaps = new CheckBox { Text = L.T("Zwischenstände sichern (vor jedem Speichern, die letzten 20)"), AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
        readonly Func<bool> snapsOn;

        public FilesPage(Workspace ws, Func<BackupService> backup, Action<List<GameFile>> restore, Action chooseGame, Action chooseBackup,
                         Func<bool> snapsOn, Action<bool> setSnaps, Action showSnaps)
        {
            this.ws = ws; this.backup = backup; this.snapsOn = snapsOn;
            Dock = DockStyle.Fill; BackColor = Theme.Background;
            foreach (var c in new[] { "Datei", "Struktur", "Backup", "Stand", "Hinweise" }) list.Columns.Add(L.T(c));
            list.Columns[0].Width = 470; list.Columns[1].Width = 100; list.Columns[2].Width = 100; list.Columns[3].Width = 120; list.Columns[4].Width = 560;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Background };
            var rSel = Theme.Primary(L.T("Markierte wiederherstellen"));
            rSel.Click += (s, e) => restore(list.SelectedItems.Cast<ListViewItem>().Select(i => (GameFile)i.Tag).ToList());
            var rAll = Theme.Secondary(L.T("Alle veränderten wiederherstellen"));
            rAll.Click += (s, e) => restore(ws.AllFiles.Where(f => f.DiskState == "verändert" || f.Dirty).ToList());
            var open = Theme.Secondary(L.T("Backup-Ordner öffnen"));
            open.Click += (s, e) => { var b = backup(); if (b != null) try { Process.Start("explorer.exe", "\"" + b.Dir + "\""); } catch { } };
            var chB = Theme.Secondary(L.T("Backup-Ordner wechseln ..."));
            chB.Click += (s, e) => chooseBackup();
            var chG = Theme.Secondary(L.T("Spielordner wechseln ..."));
            chG.Click += (s, e) => chooseGame();
            foreach (var b in new[] { rSel, rAll, open, chB, chG }) { b.Margin = new Padding(0, 0, 8, 0); buttons.Controls.Add(b); }
            buttons.Controls.Add(onlyChanged);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, BackColor = Theme.Background, Padding = new Padding(0), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            top.Controls.Add(new Label { Text = L.T("Geschrieben werden nur geänderte Bytes. Jede Datei nur mit geprüfter Sicherung (SHA-256). Jede Speicherung steht im Änderungsprotokoll im Backup-Ordner."), AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(0, 9, 0, 0) });
            var snapRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0), BackColor = Theme.Background };
            snapRow.Controls.Add(keepSnaps);
            var snapLink = Theme.Link(L.T("Zwischenstände ansehen und zurückspielen ..."));
            snapLink.Margin = new Padding(0, 8, 0, 0);
            snapLink.LinkClicked += (s, e) => showSnaps();
            snapRow.Controls.Add(snapLink);
            top.Controls.Add(snapRow);
            keepSnaps.Checked = snapsOn();
            keepSnaps.CheckedChanged += (s, e) => { if (keepSnaps.Checked != snapsOn()) setSnaps(keepSnaps.Checked); };
            onlyChanged.CheckedChanged += (s, e) => RefreshAll();

            var host = new CardPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(1) };
            host.Controls.Add(list);
            Controls.Add(host);
            Controls.Add(top);
            Controls.Add(info);
            Controls.Add(buttons);
            RefreshAll();
        }

        public void RefreshAll()
        {
            keepSnaps.Checked = snapsOn();
            var b = backup();
            info.Text = L.C(L.T("Spielordner")) + ws.GameRoot + "\r\n" +
                        L.C(L.T("Datenordner")) + ws.AfsRoot + "   (" + ws.Edition + ")\r\n" +
                        L.C(L.T("Backup")) + (b != null ? b.Dir : L.T("nicht festgelegt, Speichern gesperrt")) + "\r\n" +
                        string.Join("   ", ws.Messages.ToArray());
            list.BeginUpdate(); list.Items.Clear();
            foreach (var f in ws.AllFiles)
            {
                if (onlyChanged.Checked && f.Valid && !f.Dirty && f.DiskState == "Original") continue;
                string bk = b == null ? "-" : b.HasVerifiedBackup(f) ? L.T("geprüft") : (b.Problems.ContainsKey(f.RelPath) ? L.T("PROBLEM") : "-");
                var it = new ListViewItem(new[] { f.RelPath, f.Valid ? "ok" : L.T("GESPERRT"), bk, L.T(f.DiskState) + (f.Dirty ? " ●" : ""), string.Join("  ·  ", f.Errors.Concat(f.Notes).ToArray()) });
                it.Tag = f;
                if (!f.Valid) it.ForeColor = Theme.Danger;
                else if (f.Dirty) it.ForeColor = Theme.Warning;
                else if (f.DiskState == "verändert") it.ForeColor = Theme.Changed;
                list.Items.Add(it);
            }
            list.EndUpdate();
        }
    }
}
