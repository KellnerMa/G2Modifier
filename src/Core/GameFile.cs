using System;
using System.Collections.Generic;
using System.IO;

namespace G2Modifier.Core
{
    /// <summary>Eine Spieldatei im Speicher. Data = Arbeitsstand, Disk = Stand auf der Platte, Original = Backup.</summary>
    public class GameFile
    {
        public string RelPath;      // relativ zum Spielordner, mit '\'
        public string FullPath;
        public string FormatKey;
        public byte[] Data;
        public byte[] Disk;
        public byte[] Original;     // aus dem Backup, null solange kein Backup existiert
        public List<string> Errors = new List<string>();
        public List<string> Notes = new List<string>();

        public bool Valid { get { return Errors.Count == 0; } }
        public bool Dirty { get { return !Bin.Equal(Data, Disk); } }

        /// <summary>Stimmt die Datei auf der Platte mit dem Backup ueberein?</summary>
        public string DiskState
        {
            get
            {
                if (Original == null) return "kein Backup";
                return Bin.Equal(Disk, Original) ? "Original" : "verändert";
            }
        }

        public static GameFile Load(string root, string fullPath, string formatKey)
        {
            var f = new GameFile();
            f.FullPath = Path.GetFullPath(fullPath);
            f.RelPath = Workspace.MakeRelative(root, f.FullPath);
            f.FormatKey = formatKey;
            f.Disk = File.ReadAllBytes(f.FullPath);
            f.Data = (byte[])f.Disk.Clone();
            return f;
        }

        /// <summary>Liste geaenderter Bytebereiche zwischen zwei Staenden (fuer Protokoll und Anzeige).</summary>
        public static List<int[]> DiffRanges(byte[] a, byte[] b)
        {
            var res = new List<int[]>();
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n)
            {
                if (a[i] != b[i])
                {
                    int s = i;
                    while (i < n && a[i] != b[i]) i++;
                    res.Add(new int[] { s, i - s });
                }
                else i++;
            }
            return res;
        }
    }
}
