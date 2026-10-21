using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace G2Modifier.Core
{
    /// <summary>
    /// Sprachen der Oberflaeche. Schluessel ist der deutsche Text. de.txt uebersetzt nur die englischen
    /// Bezeichnungslisten aus G2DataGUI (Zielwirkung, Zieltyp ...) ins Deutsche. Die Uebersetzungen liegen als
    /// Textdateien vor (eingebettet in die Exe, ueberschreibbar durch sprachen\en.txt bzw. sprachen\fr.txt
    /// neben der Exe). Format je Zeile: deutscher Text, TAB, Uebersetzung. {0}, {1} = Platzhalter.
    /// Ein Schluessel mit Platzhaltern dient auch als Muster fuer zusammengesetzte Texte
    /// ("Münzkosten Stufe {0}"); der eingesetzte Teil wird seinerseits uebersetzt.
    /// </summary>
    public static class L
    {
        public static readonly string[] Codes = { "de", "en", "fr" };
        public static readonly string[] Names = { "Deutsch", "English", "Français" };

        public static string Lang { get; private set; }
        static Dictionary<string, string> map = new Dictionary<string, string>();
        static List<KeyValuePair<Regex, string>> patterns = new List<KeyValuePair<Regex, string>>();
        static readonly Regex Placeholder = new Regex(@"\{(\d)\}");
        public static readonly HashSet<string> Missing = new HashSet<string>();
        public static bool TrackMissing;
        public static string LoadedFrom = "";

        static L() { Lang = "de"; }

        public static void Set(string code)
        {
            Lang = Array.IndexOf(Codes, code) >= 0 ? code : "de";
            map = new Dictionary<string, string>();
            patterns = new List<KeyValuePair<Regex, string>>();
            Missing.Clear();
            string text = null;
            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string file = Path.Combine(Path.Combine(exeDir, "sprachen"), Lang + ".txt");
                if (File.Exists(file)) { text = File.ReadAllText(file, Encoding.UTF8); LoadedFrom = file; }
            }
            catch { }
            if (text == null)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("G2Modifier.lang." + Lang + ".txt"))
                    if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
                LoadedFrom = "eingebettet";
            }
            if (text != null) Parse(text);
        }

        public static void Parse(string text)
        {
            var pats = new List<KeyValuePair<string, string>>();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string k = Unescape(line.Substring(0, tab)), v = Unescape(line.Substring(tab + 1));
                map[k] = v;
                if (Placeholder.IsMatch(k)) pats.Add(new KeyValuePair<string, string>(k, v));
            }
            // Laengere feste Textanteile zuerst, damit "Münzkosten Stufe {0}" vor "{0} {1}" greift
            pats.Sort((a, b) => Placeholder.Replace(b.Key, "").Length.CompareTo(Placeholder.Replace(a.Key, "").Length));
            foreach (var p in pats)
            {
                string rx = "^" + Placeholder.Replace(Regex.Escape(p.Key).Replace(@"\{", "{"), m => "(?<p" + m.Groups[1].Value + ">.+?)") + "$";
                patterns.Add(new KeyValuePair<Regex, string>(new Regex(rx, RegexOptions.Singleline), p.Value));
            }
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Uebersetzt einen deutschen Text. Unbekannt: deutscher Text bleibt (und wird bei TrackMissing gemerkt).</summary>
        public static string T(string de)
        {
            if (string.IsNullOrEmpty(de)) return de;
            if (map.Count == 0) return de;
            string v = Translate(de, 0);
            if (v != null) return v;
            return Lang == "de" ? de : Miss(de);
        }

        static string Miss(string de) { if (TrackMissing) Missing.Add(de); return de; }

        static string Translate(string de, int depth)
        {
            string v;
            if (map.TryGetValue(de, out v)) return v;
            if (depth > 3) return null;
            foreach (var p in patterns)
            {
                var m = p.Key.Match(de);
                if (!m.Success) continue;
                string result = p.Value;
                for (int i = 0; i < 10; i++)
                {
                    var g = m.Groups["p" + i];
                    if (!g.Success) continue;
                    string part = g.Value;
                    string tr = IsNeutral(part) ? part : (Translate(part, depth + 1) ?? part);
                    result = result.Replace("{" + i + "}", tr);
                }
                return result;
            }
            return null;
        }

        /// <summary>Zahlen, Hex, Dateinamen und Spielnamen (ASCII ohne Umlaut) werden nicht uebersetzt.</summary>
        static bool IsNeutral(string s)
        {
            if (s.Length == 0) return true;
            bool digits = true;
            foreach (char c in s) if (!char.IsDigit(c) && c != 'x' && c != 'X' && !(c >= 'A' && c <= 'F')) { digits = false; break; }
            return digits;
        }

        /// <summary>Uebersetzt und setzt Werte ein: L.F("{0} Dateien geladen", n).</summary>
        public static string F(string de, params object[] args) { return string.Format(T(de), args); }

        /// <summary>Bezeichnung mit Doppelpunkt, im Franzoesischen mit Leerzeichen davor ("Sauvegarde : ...").</summary>
        public static string C(string translatedLabel) { return translatedLabel + (Lang == "fr" ? " : " : ": "); }

        public static bool Has(string de) { return map.ContainsKey(de) || Translate(de, 0) != null; }
    }
}
