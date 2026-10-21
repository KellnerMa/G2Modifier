using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace G2Modifier.Core
{
    /// <summary>Einstellungen in %APPDATA%\G2Modifier\settings.ini (Schluessel=Wert).</summary>
    public class Settings
    {
        public string GameRoot = "";
        public string BackupRoot = "";
        public bool ShowUnknown = false;
        public string Language = "";
        public bool KeepSnapshots = false;   // optionale Zwischenstaende vor jedem Speichern
        public GameVersion Version = GameVersion.Auto;   // Spielversion: automatisch, HD Remaster, Anniversary Edition

        static string FilePath
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G2Modifier"), "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "GameRoot") s.GameRoot = v;
                    else if (k == "BackupRoot") s.BackupRoot = v;
                    else if (k == "ShowUnknown") s.ShowUnknown = v == "1";
                    else if (k == "Language") s.Language = v;
                    else if (k == "Snapshots") s.KeepSnapshots = v == "1";
                    else if (k == "Version") s.Version = GameVersions.Parse(v);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "GameRoot=" + GameRoot + "\r\nBackupRoot=" + BackupRoot + "\r\nShowUnknown=" + (ShowUnknown ? "1" : "0") + "\r\nLanguage=" + Language + "\r\nSnapshots=" + (KeepSnapshots ? "1" : "0") + "\r\nVersion=" + GameVersions.Key(Version) + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
