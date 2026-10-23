using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace G2Modifier.Core
{
    /// <summary>Eine Fassung einer Tabelle, z. B. ITEM.BIN aus xls_data oder Gegner aus enemy_hardmode.</summary>
    public class Variant
    {
        public string Name;          // Ordnername (xls_data, sys, enemy_hardmode ...)
        public string Description;   // Anzeige
        public List<GameFile> Files = new List<GameFile>();
        public List<Record> Records = new List<Record>();
        public bool Valid { get { return Files.Count > 0 && Files.All(f => f.Valid); } }
        public override string ToString() { return Description; }
    }

    public class TableInfo
    {
        public string Key;           // ITEM.BIN, MS_PARAM.BIN ... oder ENEMY / BOSS
        public string Title;         // Reitername
        public List<Variant> Variants = new List<Variant>();
        public bool CopiesCanSync;   // nur BIN-Tabellen: gleiche Datei liegt mehrfach vor
    }

    public class Workspace
    {
        public string GameRoot;
        public string AfsRoot;
        public string Edition;
        public GameVersion Version;          // bearbeitete Version (HD Remaster oder Anniversary Edition)
        public GameVersion Chosen;           // Einstellung: Automatisch oder fest gewaehlt
        public bool Anniversary { get { return Version == GameVersion.Anniversary; } }
        public List<TableInfo> Tables = new List<TableInfo>();
        public List<GameFile> AllFiles = new List<GameFile>();
        public List<string> Messages = new List<string>();
        public GameFile Exe, AngE, AngS;   // Rage: grandia2.exe und die beiden Party-Dateien mit dem GBIX
        public GameFile AngEFace, AngSFace; // Rage-Gesichter (DDS) waehrend und nach der Rage

        // sys zuerst: das Spiel zeigt die Texte aus sys/sys_hardmode, xls_data hat bei vielen Beschreibungen eine aeltere Fassung (z. B. mit "|")
        public static readonly string[] BinFolders = { "sys", "sys_hardmode", "xls_data" };
        static readonly string[][] BinTables =
        {
            new[] { "ITEM.BIN", "Gegenstände" },
            new[] { "MS_PARAM.BIN", "Moves und Zauber" },
            new[] { "SK_PARAM.BIN", "Skills" },
            new[] { "TB_MAGIC.BIN", "Manaeier" },
            new[] { "TB_SKILL.BIN", "Skillbücher" },
            new[] { "TB_SPCL.BIN", "Spezialtechniken" },
            new[] { "PC_INIT.BIN", "Startwerte" },
            new[] { "TB_LVUP.BIN", "Level-ups" },
        };

        public static string FindAfsRoot(string root, out string edition)
        {
            edition = null;
            string hd = Path.Combine(root, "content", "data", "afs");
            string ae = Path.Combine(root, "data", "afs");
            if (Directory.Exists(hd)) { edition = "HD Remaster (content\\data\\afs)"; return hd; }
            if (Directory.Exists(ae)) { edition = "Anniversary Edition (data\\afs)"; return ae; }
            if (Directory.Exists(Path.Combine(root, "xls_data")) || Directory.Exists(Path.Combine(root, "enemy")))
            { edition = L.T("afs-Ordner direkt gewählt"); return root; }
            return null;
        }

        public static Workspace Open(string gameRoot) { return Open(gameRoot, GameVersion.Auto); }

        /// <summary>Oeffnet den Spielordner fuer die gewaehlte Version. Automatisch: Exe entscheidet (GameVersions.Detect).</summary>
        public static Workspace Open(string gameRoot, GameVersion choice)
        {
            var ws = new Workspace();
            ws.GameRoot = Path.GetFullPath(gameRoot).TrimEnd('\\', '/');
            ws.Chosen = choice;
            ws.Version = choice == GameVersion.Auto ? GameVersions.Detect(ws.GameRoot) : choice;
            if (ws.Version == GameVersion.Auto)
            {
                // Weder Exe noch Datenordner erkannt: wie bis 0.12.9 (afs-Ordner direkt gewaehlt), Rage wie HD Remaster
                string ed;
                ws.AfsRoot = FindAfsRoot(ws.GameRoot, out ed);
                ws.Edition = ed;
                ws.Version = GameVersion.HdRemaster;
            }
            else
            {
                string dir = Path.Combine(ws.GameRoot, GameVersions.AfsFolder(ws.Version));
                if (Directory.Exists(dir)) ws.AfsRoot = dir;
                ws.Edition = GameVersions.Name(ws.Version) + " (" + GameVersions.AfsFolder(ws.Version) + (choice == GameVersion.Auto ? ", " + L.T("automatisch erkannt") : "") + ")";
            }
            RagePatch.Anniversary = ws.Anniversary;
            if (ws.AfsRoot == null)
                throw new IOException(L.T("Kein Grandia-II-Datenordner gefunden.\nErwartet: <Spielordner>\\content\\data\\afs (HD Remaster) oder <Spielordner>\\data\\afs (Anniversary Edition).") +
                    (choice != GameVersion.Auto ? "\n\n" + L.F("Eingestellte Spielversion: {0}. Auf der Startseite lässt sie sich umstellen.", GameVersions.Name(choice)) : ""));
            if (Directory.Exists(Path.Combine(ws.GameRoot, "content", "data", "afs")) && Directory.Exists(Path.Combine(ws.GameRoot, "data", "afs")))
                ws.Messages.Add(L.F("Hinweis: Es gibt sowohl content\\data\\afs als auch data\\afs. Bearbeitet wird {0}.", ws.Edition));

            foreach (var t in BinTables)
            {
                var ti = new TableInfo { Key = t[0], Title = t[1], CopiesCanSync = true };
                foreach (var folder in BinFolders)
                {
                    string p = FindFileIgnoreCase(Path.Combine(ws.AfsRoot, folder), t[0]);
                    if (p == null) continue;
                    var gf = GameFile.Load(ws.GameRoot, p, t[0]);
                    var v = new Variant { Name = folder, Description = folder + "\\" + t[0] };
                    v.Files.Add(gf);
                    v.Records = Formats.BuildTable(gf);
                    ti.Variants.Add(v);
                    ws.AllFiles.Add(gf);
                }
                BorrowFillerLabels(ti);
                if (ti.Variants.Count > 0) ws.Tables.Add(ti);
                else ws.Messages.Add(L.F("{0} wurde in keinem der Ordner {1} gefunden.", t[0], string.Join(", ", BinFolders)));
            }

            ws.Tables.Add(ws.LoadEnemyTable("ENEMY", "Gegner", new[] { "enemy", "enemy_hardmode" }, new[] { "Normal", "Hard" }));
            ws.Tables.Add(ws.LoadEnemyTable("BOSS", "Bosse", new[] { "boss", "boss_hardmode" }, new[] { "Normal", "Hard" }));
            ws.Tables.RemoveAll(t => t.Variants.Count == 0);
            ws.MarkCopies();
            ws.LoadRageAndPortraits();
            return ws;
        }

        TableInfo LoadEnemyTable(string key, string title, string[] folders, string[] modes)
        {
            var ti = new TableInfo { Key = key, Title = title, CopiesCanSync = false };
            for (int i = 0; i < folders.Length; i++)
            {
                string dir = Path.Combine(AfsRoot, folders[i]);
                if (!Directory.Exists(dir)) { Messages.Add(L.F("Ordner {0} fehlt.", folders[i])); continue; }
                var v = new Variant { Name = folders[i], Description = L.T(modes[i]) + " (" + folders[i] + ")" };
                var files = Directory.GetFiles(dir, "*_0.dat", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var p in files)
                {
                    var gf = GameFile.Load(GameRoot, p, "ENEMY");
                    v.Files.Add(gf);
                    v.Records.AddRange(Formats.BuildTable(gf));
                    AllFiles.Add(gf);
                }
                if (v.Files.Count > 0) ti.Variants.Add(v);
            }
            return ti;
        }

        /// <summary>Platzhalter-Namen (sys_hardmode\ms_param.bin) in der Liste durch den Namen aus einer Kopie mit echtem Text ersetzen. Die Datei selbst bleibt unberuehrt.</summary>
        static void BorrowFillerLabels(TableInfo ti)
        {
            string tag = L.T("(Platzhalter)");
            foreach (var v in ti.Variants)
                foreach (var r in v.Records.Where(x => x.Label.EndsWith(tag)))
                {
                    r.Placeholders = true;
                    var src = ti.Variants.Where(o => o != v).SelectMany(o => o.Records).FirstOrDefault(o => o.Key == r.Key && !o.Label.EndsWith(tag));
                    if (src == null) continue;
                    r.TextFrom = src;
                    r.Label = src.Label;
                }
        }

        /// <summary>Vergleicht Kopien derselben BIN-Datei und notiert, ob sie identisch sind.</summary>
        void MarkCopies()
        {
            foreach (var t in Tables.Where(x => x.CopiesCanSync && x.Variants.Count > 1))
            {
                var first = t.Variants[0].Files[0];
                foreach (var v in t.Variants.Skip(1))
                {
                    var f = v.Files[0];
                    string msg = Bin.Equal(first.Disk, f.Disk)
                        ? L.F("Kopie identisch mit {0}", first.RelPath)
                        : L.F("Kopie weicht von {0} ab ({1} Bereiche)", first.RelPath, GameFile.DiffRanges(first.Disk, f.Disk).Count) + (first.Disk.Length != f.Disk.Length ? ", " + L.T("andere Größe") : "");
                    f.Notes.Add(msg);
                }
            }
        }

        /// <summary>Spiel nutzt Kleinbuchstaben (item.bin), die Community schreibt ITEM.BIN. Beides finden.</summary>
        public static string FindFileIgnoreCase(string dir, string name)
        {
            if (dir == null || !Directory.Exists(dir)) return null;
            foreach (var f in Directory.GetFiles(dir))
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        public static string FindDirIgnoreCase(string dir, string name)
        {
            if (dir == null || !Directory.Exists(dir)) return null;
            foreach (var d in Directory.GetDirectories(dir))
                if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        GameFile AddFile(string path, string formatKey)
        {
            if (path == null) return null;
            var gf = GameFile.Load(GameRoot, path, formatKey);
            AllFiles.Add(gf);
            return gf;
        }

        void AddPortrait(string path)
        {
            var gf = AddFile(path, "DDS");
            if (gf == null) return;
            string e = Dds.Check(gf.Disk);
            if (e != null) gf.Errors.Add("DDS: " + e);
        }

        /// <summary>
        /// Rage (grandia2.exe, party\c03ang_e.dat, party\c03ang_s.dat) und alle Portraets, die der Reiter
        /// "Rage und Avatare" schreiben kann. Fehlt etwas, bleibt der Rest nutzbar.
        /// </summary>
        void LoadRageAndPortraits()
        {
            Exe = AddFile(FindFileIgnoreCase(GameRoot, "grandia2.exe"), "EXE");
            if (Exe == null) Messages.Add(L.T("grandia2.exe nicht im Spielordner gefunden: Rage-Einstellungen nicht verfügbar."));
            else
            {
                string e = RagePatch.Validate(Exe.Disk);
                if (e != null) Exe.Errors.Add(e);
                uint st = GameVersions.ExeStamp(Exe.Disk);
                if ((Anniversary && st == GameVersions.HdExeStamp) || (!Anniversary && st == GameVersions.AeExeStamp))
                    Exe.Errors.Add(L.F("Die Exe passt nicht zur eingestellten Spielversion ({0}). Auf der Startseite umstellen.", GameVersions.Name(Version)));
            }

            string party = FindDirIgnoreCase(AfsRoot, "party");
            AngE = AddFile(FindFileIgnoreCase(party, "c03ang_e.dat"), "PARTY");
            AngS = AddFile(FindFileIgnoreCase(party, "c03ang_s.dat"), "PARTY");
            foreach (var t in new[] { new { F = AngE, O = RagePatch.GbixAngE }, new { F = AngS, O = RagePatch.GbixAngS } })
            {
                if (t.F == null) continue;
                string e = RagePatch.CheckGbix(t.F.Disk, t.O);
                if (e != null) t.F.Errors.Add(L.F("GBIX bei 0x{0}: {1}", t.O.ToString("X"), e));
            }
            // Anniversary Edition: Porträts meist DXT5, einige BGRA mit Mipmaps (HD: unkomprimiert RGBA). Dds liest und schreibt beides.
            int before = AllFiles.Count;
            AddPortrait(FindFileIgnoreCase(FindDirIgnoreCase(party, "c03ang_e"), Avatars.AngFace));
            if (AllFiles.Count > before) AngEFace = AllFiles[AllFiles.Count - 1];
            before = AllFiles.Count;
            AddPortrait(FindFileIgnoreCase(FindDirIgnoreCase(party, "c03ang_s"), Avatars.AngFace));
            if (AllFiles.Count > before) AngSFace = AllFiles[AllFiles.Count - 1];

            foreach (var folder in new[] { "sys", "sys_hardmode" })
            {
                string d = FindDirIgnoreCase(AfsRoot, folder);
                for (int i = 0; i <= 13; i++) AddPortrait(FindFileIgnoreCase(d, "btl_sys1_" + i + ".dds"));
                for (int i = 0; i <= 6; i++) AddPortrait(FindFileIgnoreCase(d, "result_" + i + ".dds"));
            }
            string title = FindDirIgnoreCase(AfsRoot, "title_data");
            string face99 = FindDirIgnoreCase(title, "face_99");
            for (int c = 1; c <= 7; c++)
            {
                AddPortrait(FindFileIgnoreCase(title, "f0" + c + "01.dds"));
                AddPortrait(FindFileIgnoreCase(face99, "f0" + c + "01.dds"));
            }
            string faces = FindDirIgnoreCase(AfsRoot, "f_face_g");
            if (faces != null)
            {
                var dirs = Directory.GetDirectories(faces);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (var d in dirs)
                    for (int c = 1; c <= 7; c++) AddPortrait(FindFileIgnoreCase(d, "f0" + c + "01.dds"));
            }
        }

        public TableInfo Table(string key) { return Tables.FirstOrDefault(t => t.Key == key); }

        public Variant PrimaryVariant(string key)
        {
            var t = Table(key);
            if (t == null) return null;
            return t.Variants.FirstOrDefault(v => v.Valid);
        }

        public string ResolveRef(RefKind kind, long v)
        {
            string key = kind == RefKind.Item ? "ITEM.BIN" : kind == RefKind.Move ? "MS_PARAM.BIN" : "SK_PARAM.BIN";
            var pv = PrimaryVariant(key);
            if (pv == null) return L.F("({0} nicht geladen)", key);
            if (v < 0 || v >= pv.Records.Count) return L.F("(Index außerhalb 0..{0})", pv.Records.Count - 1);
            return pv.Records[(int)v].Label;
        }

        public IEnumerable<GameFile> DirtyFiles { get { return AllFiles.Where(f => f.Dirty); } }

        public static string MakeRelative(string root, string full)
        {
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(full);
            if (f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return f.Substring(r.Length).Replace('/', '\\');
            throw new IOException(L.F("{0} liegt nicht im Spielordner {1}", full, root));
        }
    }
}
