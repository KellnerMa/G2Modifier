using System;
using System.IO;

namespace G2Modifier.Core
{
    /// <summary>
    /// Spielversion. Wird auf der Startseite umgeschaltet (Automatisch / HD Remaster / Anniversary Edition).
    /// HD Remaster: content\data\afs, Exe von 2022 (SHA-256 adb2bc70...d3e3), Rage-Stellen in RagePatch.
    /// Anniversary Edition: data\afs, Exe von 2015 (PE-Zeitstempel 0x5640FE82), Rage-Stellen in RagePatchAE.
    /// </summary>
    public enum GameVersion { Auto, HdRemaster, Anniversary }

    public static class GameVersions
    {
        public const uint HdExeStamp = 0x6247AE91, AeExeStamp = 0x5640FE82;

        public static string Key(GameVersion v) { return v == GameVersion.HdRemaster ? "hd" : v == GameVersion.Anniversary ? "ae" : "auto"; }
        public static GameVersion Parse(string s) { return s == "hd" ? GameVersion.HdRemaster : s == "ae" ? GameVersion.Anniversary : GameVersion.Auto; }

        public static string Name(GameVersion v)
        {
            return v == GameVersion.HdRemaster ? L.T("HD Remaster") : v == GameVersion.Anniversary ? L.T("Anniversary Edition") : L.T("Automatisch");
        }

        public static string AfsFolder(GameVersion v) { return v == GameVersion.Anniversary ? "data\\afs" : "content\\data\\afs"; }

        /// <summary>PE-Zeitstempel der Exe, 0 wenn keine Windows-Exe.</summary>
        public static uint ExeStamp(byte[] exe)
        {
            if (exe == null || exe.Length < 0x200 || exe[0] != 'M' || exe[1] != 'Z') return 0;
            int pe = BitConverter.ToInt32(exe, 0x3C);
            if (pe < 0 || pe + 12 > exe.Length || exe[pe] != 'P' || exe[pe + 1] != 'E') return 0;
            return BitConverter.ToUInt32(exe, pe + 8);
        }

        /// <summary>
        /// Automatisch: zuerst nach der Exe (sie entscheidet, welchen Datenordner das Spiel liest), sonst nach den Ordnern.
        /// Wichtig, wenn nach einem Versionswechsel beide Datenordner im Spielordner liegen.
        /// </summary>
        public static GameVersion Detect(string gameRoot)
        {
            string exe = Workspace.FindFileIgnoreCase(gameRoot, "grandia2.exe");
            if (exe != null)
            {
                uint st;
                try { st = ExeStamp(File.ReadAllBytes(exe)); } catch { st = 0; }
                if (st == AeExeStamp) return GameVersion.Anniversary;
                if (st == HdExeStamp) return GameVersion.HdRemaster;
            }
            if (Directory.Exists(Path.Combine(gameRoot, "content", "data", "afs"))) return GameVersion.HdRemaster;
            if (Directory.Exists(Path.Combine(gameRoot, "data", "afs"))) return GameVersion.Anniversary;
            return GameVersion.Auto;
        }
    }
}
