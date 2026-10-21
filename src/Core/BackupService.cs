using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace G2Modifier.Core
{
    /// <summary>
    /// Backup-Pflicht. Regeln:
    /// 1. Backup-Ordner muss ausserhalb des Spielordners liegen (und umgekehrt).
    /// 2. Jede Datei wird einmal gesichert (erster gesehener Stand) und nie ueberschrieben.
    /// 3. Jede Sicherung steht mit SHA-256 im Manifest. Vor jedem Schreiben wird die Sicherung geprueft.
    /// 4. Ohne gepruefte Sicherung wird eine Datei nicht geschrieben.
    /// </summary>
    public class BackupService
    {
        public const string FolderName = "G2Modifier-Backup";
        // Eigenes Backup je Version: beide Exe heissen grandia2.exe, das HD-Original darf nie in die Anniversary Edition zurueck.
        public const string FolderNameAE = "G2Modifier-Backup-Anniversary";
        public static string FolderFor(GameVersion v) { return v == GameVersion.Anniversary ? FolderNameAE : FolderName; }
        public readonly string Root;          // vom Nutzer gewaehlter Ordner
        public readonly string Dir;           // Root\G2Modifier-Backup
        readonly string manifestPath, logPath;
        readonly Dictionary<string, string[]> manifest = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string CheckLocation(string gameRoot, string backupRoot)
        {
            if (string.IsNullOrEmpty(backupRoot)) return "Kein Backup-Ordner gewählt.";
            string g = Norm(gameRoot), b = Norm(backupRoot);
            if (string.Equals(g, b, StringComparison.OrdinalIgnoreCase)) return "Der Backup-Ordner darf nicht der Spielordner sein.";
            if (b.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "Der Backup-Ordner darf nicht im Spielordner liegen. Steam kann ihn bei Updates oder Dateiprüfungen verändern.";
            if (g.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "Der Spielordner darf nicht im Backup-Ordner liegen.";
            return null;
        }

        static string Norm(string p) { return Path.GetFullPath(p).TrimEnd('\\', '/'); }

        public BackupService(string gameRoot, string backupRoot) : this(gameRoot, backupRoot, FolderName) { }

        public BackupService(string gameRoot, string backupRoot, string folderName)
        {
            string err = CheckLocation(gameRoot, backupRoot);
            if (err != null) throw new IOException(L.T(err));
            Root = Norm(backupRoot);
            Dir = Path.Combine(Root, folderName);
            Directory.CreateDirectory(Dir);
            manifestPath = Path.Combine(Dir, "manifest.tsv");
            logPath = Path.Combine(Dir, "aenderungsprotokoll.txt");
            if (File.Exists(manifestPath))
            {
                foreach (var line in File.ReadAllLines(manifestPath, Encoding.UTF8))
                {
                    if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                    var p = line.Split('\t');
                    if (p.Length >= 4) manifest[p[0]] = p;
                }
            }
        }

        string BackupPath(string rel) { return Path.Combine(Dir, rel.Replace('\\', Path.DirectorySeparatorChar)); }

        public bool HasVerifiedBackup(GameFile f) { return verified.Contains(f.RelPath) && f.Original != null; }

        /// <summary>Sichert fehlende Dateien und prueft vorhandene. Gibt Anzahl neu gesicherter Dateien zurueck.</summary>
        public int EnsureBackups(IEnumerable<GameFile> files, Action<int, int> progress)
        {
            var list = files.ToList();
            int created = 0, i = 0;
            var newLines = new List<string>();
            foreach (var f in list)
            {
                i++;
                if (progress != null && (i % 25 == 0 || i == list.Count)) progress(i, list.Count);
                string bp = BackupPath(f.RelPath);
                string[] entry;
                if (manifest.TryGetValue(f.RelPath, out entry))
                {
                    if (!File.Exists(bp)) { Problems[f.RelPath] = L.F("Backup-Datei fehlt: {0}", bp); f.Original = null; continue; }
                    var bytes = File.ReadAllBytes(bp);
                    if (Bin.Sha256(bytes) != entry[2] || bytes.Length.ToString(CultureInfo.InvariantCulture) != entry[1])
                    { Problems[f.RelPath] = L.F("Backup-Datei beschädigt (Prüfsumme passt nicht zum Manifest): {0}", bp); f.Original = null; continue; }
                    f.Original = bytes;
                    verified.Add(f.RelPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(bp));
                    if (File.Exists(bp))
                    {
                        // Datei ohne Manifest-Eintrag: nicht ueberschreiben, Nutzer muss entscheiden
                        Problems[f.RelPath] = L.F("Im Backup liegt bereits eine Datei ohne Manifest-Eintrag: {0}. Bitte prüfen oder anderen Backup-Ordner wählen.", bp);
                        continue;
                    }
                    File.WriteAllBytes(bp, f.Disk);
                    var check = File.ReadAllBytes(bp);
                    string hash = Bin.Sha256(f.Disk);
                    if (Bin.Sha256(check) != hash) { Problems[f.RelPath] = L.F("Backup konnte nicht fehlerfrei geschrieben werden: {0}", bp); continue; }
                    var e = new[] { f.RelPath, f.Disk.Length.ToString(CultureInfo.InvariantCulture), hash, DateTime.UtcNow.ToString("o") };
                    manifest[f.RelPath] = e;
                    newLines.Add(string.Join("\t", e));
                    f.Original = check;
                    verified.Add(f.RelPath);
                    created++;
                }
            }
            if (newLines.Count > 0)
            {
                bool fresh = !File.Exists(manifestPath);
                using (var w = new StreamWriter(manifestPath, true, new UTF8Encoding(false)))
                {
                    if (fresh) w.WriteLine("# relativer Pfad\tGröße\tSHA-256\tgesichert (UTC)");
                    foreach (var l in newLines) w.WriteLine(l);
                }
            }
            return created;
        }

        /// <summary>Schreibt alle geaenderten Dateien. Jede Datei nur mit geprueftem Backup.</summary>
        public int LastFailures;

        public List<string> SaveDirty(Workspace ws)
        {
            var report = new List<string>();
            LastFailures = 0;
            // 1. Pruefen, welche Dateien geschrieben werden duerfen
            var toWrite = new List<GameFile>();
            foreach (var f in ws.DirtyFiles.ToList())
            {
                if (!HasVerifiedBackup(f)) { report.Add(L.C(L.T("NICHT geschrieben (kein geprüftes Backup)")) + f.RelPath); LastFailures++; continue; }
                if (!f.Valid) { report.Add(L.C(L.T("NICHT geschrieben (Datei gesperrt)")) + f.RelPath); LastFailures++; continue; }
                // Vor dem Schreiben: Platte noch im erwarteten Zustand?
                var current = File.ReadAllBytes(f.FullPath);
                if (!Bin.Equal(current, f.Disk)) { report.Add(L.C(L.T("NICHT geschrieben (Datei wurde außerhalb des Tools geändert, bitte neu laden)")) + f.RelPath); LastFailures++; continue; }
                // Groessenaenderung nur bei Portraets (DDS mit geprueftem Kopf), z. B. Rage-Gesicht 64 -> 128 Pixel
                bool resized = f.Data.Length != f.Disk.Length;
                if (resized && !(f.FormatKey == "DDS" && Dds.Check(f.Data) == null)) { report.Add(L.C(L.T("NICHT geschrieben (Dateigröße würde sich ändern)")) + f.RelPath); LastFailures++; continue; }
                toWrite.Add(f);
            }
            // 2. Zwischenstand (Stand vor dem Speichern). Schlaegt er fehl, wird nichts geschrieben.
            if (KeepSnapshots && toWrite.Count > 0)
            {
                string snapErr;
                var snap = CreateSnapshot(toWrite, L.T("vor dem Speichern"), out snapErr);
                if (snap == null)
                {
                    foreach (var f in toWrite) { report.Add(L.C(L.T("NICHT geschrieben (Zwischenstand konnte nicht gesichert werden)")) + f.RelPath); LastFailures++; }
                    report.Add(snapErr);
                    return report;
                }
                report.Add(L.F("Zwischenstand gesichert: {0}", snap));
            }
            // 3. Schreiben
            var log = new StringBuilder();
            foreach (var f in toWrite)
            {
                bool resized = f.Data.Length != f.Disk.Length;
                var ranges = GameFile.DiffRanges(f.Disk, f.Data);
                WriteVerified(f.FullPath, f.Data);
                log.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  GESPEICHERT  " + f.RelPath + "  (" + ranges.Count + " Bereich(e))" +
                    (resized ? "  Größe " + f.Disk.Length + " -> " + f.Data.Length + " Bytes" : ""));
                foreach (var r in ranges.Take(200))
                    log.AppendLine("    0x" + r[0].ToString("X6") + "  alt: " + Bin.Hex(f.Disk, r[0], Math.Min(r[1], 16)) + "  neu: " + Bin.Hex(f.Data, r[0], Math.Min(r[1], 16)) + (r[1] > 16 ? " ..." : ""));
                f.Disk = (byte[])f.Data.Clone();
                report.Add(L.F("Gespeichert: {0} ({1} geänderte Bereiche)", f.RelPath, ranges.Count));
            }
            if (log.Length > 0) File.AppendAllText(logPath, log.ToString(), new UTF8Encoding(false));
            return report;
        }

        // ------------------------------------------------------------ Zwischenstaende (optional)
        // Unterordner zwischenstaende\JJJJ-MM-TT_HH-MM-SS mit dem Stand der Dateien VOR dem Ueberschreiben.
        // zwischenstand.tsv (Pfad, Groesse, SHA-256) wird zuletzt geschrieben und markiert einen vollstaendigen Zwischenstand.
        // Das Original-Backup wird dabei nie angefasst. Geloescht werden nur Ordner mit zwischenstand.tsv.

        public bool KeepSnapshots;
        public int SnapshotLimit = 20;
        public const string SnapshotFolder = "zwischenstaende", SnapshotIndex = "zwischenstand.tsv";
        public string SnapshotRoot { get { return Path.Combine(Dir, SnapshotFolder); } }

        public class Snapshot
        {
            public string Name, Path, Reason;
            public DateTime Time;
            public List<string[]> Entries = new List<string[]>();   // relativer Pfad, Groesse, SHA-256
        }

        /// <summary>Sichert den Plattenstand (f.Disk) der Dateien. Rueckgabe: Ordnername oder null (dann err gesetzt).</summary>
        public string CreateSnapshot(IEnumerable<GameFile> files, string reason, out string err)
        {
            err = null;
            string dir = null;
            try
            {
                var list = files.Where(f => f.Disk != null).ToList();
                if (list.Count == 0) return null;
                Directory.CreateDirectory(SnapshotRoot);
                string name = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                string unique = name;
                for (int n = 2; Directory.Exists(Path.Combine(SnapshotRoot, unique)); n++) unique = name + "_" + n;
                dir = Path.Combine(SnapshotRoot, unique);
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("# " + reason + "\t" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                foreach (var f in list)
                {
                    string target = Path.Combine(dir, f.RelPath.Replace('\\', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, f.Disk);
                    string hash = Bin.Sha256(f.Disk);
                    if (Bin.Sha256(File.ReadAllBytes(target)) != hash) throw new IOException(L.F("Zwischenstand fehlerhaft geschrieben: {0}", target));
                    sb.AppendLine(f.RelPath + "\t" + f.Disk.Length.ToString(CultureInfo.InvariantCulture) + "\t" + hash);
                }
                File.WriteAllText(Path.Combine(dir, SnapshotIndex), sb.ToString(), new UTF8Encoding(false));
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ZWISCHENSTAND  " + SnapshotFolder + "\\" + unique + "  (" + list.Count + " Datei(en), " + reason + ")" + Environment.NewLine, new UTF8Encoding(false));
                PruneSnapshots();
                return unique;
            }
            catch (Exception ex)
            {
                err = L.T("Zwischenstand konnte nicht gesichert werden:") + " " + ex.Message;
                // halben Ordner ohne Index entfernen, damit er nie als Zwischenstand gilt
                try { if (dir != null && Directory.Exists(dir) && !File.Exists(Path.Combine(dir, SnapshotIndex))) Directory.Delete(dir, true); } catch { }
                return null;
            }
        }

        public List<Snapshot> ListSnapshots()
        {
            var res = new List<Snapshot>();
            if (!Directory.Exists(SnapshotRoot)) return res;
            foreach (var d in Directory.GetDirectories(SnapshotRoot))
            {
                string idx = Path.Combine(d, SnapshotIndex);
                if (!File.Exists(idx)) continue;
                var s = new Snapshot { Name = Path.GetFileName(d), Path = d, Time = Directory.GetCreationTime(d) };
                foreach (var line in File.ReadAllLines(idx, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (line.StartsWith("#")) { s.Reason = p[0].TrimStart('#', ' '); DateTime t; if (p.Length > 1 && DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) s.Time = t; continue; }
                    if (p.Length >= 3) s.Entries.Add(p);
                }
                res.Add(s);
            }
            return res.OrderByDescending(x => x.Name, StringComparer.Ordinal).ToList();
        }

        void PruneSnapshots()
        {
            if (SnapshotLimit <= 0) return;
            foreach (var s in ListSnapshots().Skip(SnapshotLimit))
                try { Directory.Delete(s.Path, true); } catch { }
        }

        /// <summary>Spielt einen Zwischenstand zurueck. Vorher wird (wenn eingeschaltet) der aktuelle Stand selbst als Zwischenstand gesichert.</summary>
        public List<string> RestoreSnapshot(Workspace ws, Snapshot s, out int failures)
        {
            var report = new List<string>();
            failures = 0;
            var plan = new List<KeyValuePair<GameFile, byte[]>>();
            foreach (var e in s.Entries)
            {
                var f = ws.AllFiles.FirstOrDefault(x => string.Equals(x.RelPath, e[0], StringComparison.OrdinalIgnoreCase));
                if (f == null) { report.Add(L.C(L.T("Übersprungen (Datei nicht im Spielordner geladen)")) + e[0]); failures++; continue; }
                string src = Path.Combine(s.Path, e[0].Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(src)) { report.Add(L.C(L.T("Übersprungen (Datei fehlt im Zwischenstand)")) + e[0]); failures++; continue; }
                var bytes = File.ReadAllBytes(src);
                if (Bin.Sha256(bytes) != e[2] || bytes.Length.ToString(CultureInfo.InvariantCulture) != e[1]) { report.Add(L.C(L.T("Übersprungen (Prüfsumme passt nicht)")) + e[0]); failures++; continue; }
                if (!Bin.Equal(File.ReadAllBytes(f.FullPath), f.Disk)) { report.Add(L.C(L.T("Übersprungen (Datei wurde außerhalb des Tools geändert, bitte neu laden)")) + e[0]); failures++; continue; }
                if (bytes.Length != f.Disk.Length && f.FormatKey != "DDS") { report.Add(L.C(L.T("Übersprungen (Dateigröße passt nicht)")) + e[0]); failures++; continue; }
                plan.Add(new KeyValuePair<GameFile, byte[]>(f, bytes));
            }
            var changing = plan.Where(kv => !Bin.Equal(kv.Key.Disk, kv.Value)).ToList();
            if (KeepSnapshots && changing.Count > 0)
            {
                string err;
                var snap = CreateSnapshot(changing.Select(kv => kv.Key), L.F("vor dem Zurückspielen von {0}", s.Name), out err);
                if (snap == null) { report.Add(err); failures += plan.Count; return report; }
                report.Add(L.F("Aktueller Stand vorher gesichert: {0}", snap));
            }
            foreach (var kv in plan)
            {
                var f = kv.Key;
                if (!Bin.Equal(f.Disk, kv.Value)) WriteVerified(f.FullPath, kv.Value);
                f.Disk = (byte[])kv.Value.Clone();
                f.Data = (byte[])kv.Value.Clone();
                report.Add(L.F("Zurückgespielt: {0}", f.RelPath));
            }
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ZURÜCKGESPIELT  " + SnapshotFolder + "\\" + s.Name + "  (" + plan.Count + " Datei(en))" + Environment.NewLine, new UTF8Encoding(false));
            return report;
        }

        /// <summary>Stellt eine Datei aus dem Backup wieder her (mit Pruefsumme).</summary>
        public string Restore(GameFile f)
        {
            if (!HasVerifiedBackup(f)) return L.F("Kein geprüftes Backup für {0}", f.RelPath);
            WriteVerified(f.FullPath, f.Original);
            f.Disk = (byte[])f.Original.Clone();
            f.Data = (byte[])f.Original.Clone();
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  WIEDERHERGESTELLT  " + f.RelPath + Environment.NewLine, new UTF8Encoding(false));
            return null;
        }

        /// <summary>Temp-Datei schreiben, pruefen, dann ersetzen und erneut pruefen.</summary>
        static void WriteVerified(string target, byte[] data)
        {
            string tmp = target + ".g2m-tmp";
            File.WriteAllBytes(tmp, data);
            if (!Bin.Equal(File.ReadAllBytes(tmp), data)) { File.Delete(tmp); throw new IOException("Temp-Datei fehlerhaft: " + tmp); }
            var attr = File.GetAttributes(target);
            if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, attr & ~FileAttributes.ReadOnly);
            try { File.Replace(tmp, target, null); }
            catch (PlatformNotSupportedException) { File.Copy(tmp, target, true); File.Delete(tmp); }
            if (!Bin.Equal(File.ReadAllBytes(target), data)) throw new IOException("Nach dem Schreiben stimmt der Inhalt nicht: " + target);
        }

        public string ManifestPath { get { return manifestPath; } }
        public string LogPath { get { return logPath; } }
    }
}
