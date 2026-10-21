using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace G2Modifier.Core
{
    public enum FieldType { U8, S8, U16, S16, U32, S32, Text, Flags8, Flags16 }

    /// <summary>Worauf ein Feldwert verweist (Index in eine andere Tabelle).</summary>
    public enum RefKind { None, Item, Move, Skill }

    /// <summary>Ein Feld innerhalb eines Datensatzes. Offset relativ zum Abschnittsanfang.</summary>
    public class FieldDef
    {
        public string Label;        // Anzeige (deutsch)
        public string SourceName;   // Feldname in G2DataGUI (Beleg)
        public int Offset;
        public FieldType Type;
        public int Length;          // nur Text
        public bool Known = true;   // false = in der Quelle als "Unknown" gefuehrt
        public bool Structural;     // Zeiger/IDs: niemals editierbar
        public string[] Labels;     // Bedeutung pro Wert (Index = Wert)
        public string[] FlagNames;  // Bitnamen (Bit 0 zuerst)
        public RefKind Ref = RefKind.None;
        public string Hint;

        public int Size
        {
            get
            {
                switch (Type)
                {
                    case FieldType.U8: case FieldType.S8: case FieldType.Flags8: return 1;
                    case FieldType.U16: case FieldType.S16: case FieldType.Flags16: return 2;
                    case FieldType.U32: case FieldType.S32: return 4;
                    default: return Length;
                }
            }
        }

        public bool IsText { get { return Type == FieldType.Text; } }
        public bool IsFlags { get { return Type == FieldType.Flags8 || Type == FieldType.Flags16; } }

        public long Min
        {
            get
            {
                switch (Type)
                {
                    case FieldType.S8: return sbyte.MinValue;
                    case FieldType.S16: return short.MinValue;
                    case FieldType.S32: return int.MinValue;
                    default: return 0;
                }
            }
        }

        public long Max
        {
            get
            {
                switch (Type)
                {
                    case FieldType.U8: case FieldType.Flags8: return byte.MaxValue;
                    case FieldType.S8: return sbyte.MaxValue;
                    case FieldType.U16: case FieldType.Flags16: return ushort.MaxValue;
                    case FieldType.S16: return short.MaxValue;
                    case FieldType.U32: return uint.MaxValue;
                    case FieldType.S32: return int.MaxValue;
                    default: return 0;
                }
            }
        }

        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case FieldType.U8: return "u8";
                    case FieldType.S8: return "s8";
                    case FieldType.U16: return "u16";
                    case FieldType.S16: return "s16";
                    case FieldType.U32: return "u32";
                    case FieldType.S32: return "s32";
                    case FieldType.Flags8: return "Bits8";
                    case FieldType.Flags16: return "Bits16";
                    default: return "Text[" + Length + "]";
                }
            }
        }

        public long ReadNumber(byte[] d, int abs)
        {
            switch (Type)
            {
                case FieldType.U8: case FieldType.Flags8: return d[abs];
                case FieldType.S8: return (sbyte)d[abs];
                case FieldType.U16: case FieldType.Flags16: return Bin.U16(d, abs);
                case FieldType.S16: return Bin.S16(d, abs);
                case FieldType.U32: return Bin.U32(d, abs);
                case FieldType.S32: return Bin.S32(d, abs);
            }
            throw new InvalidOperationException("Kein Zahlenfeld");
        }

        public void WriteNumber(byte[] d, int abs, long v)
        {
            if (v < Min || v > Max)
                throw new ArgumentOutOfRangeException(Label, L.F("Wert {0} außerhalb {1} bis {2}", v, Min, Max));
            switch (Type)
            {
                case FieldType.U8: case FieldType.S8: case FieldType.Flags8: d[abs] = (byte)(v & 0xFF); return;
                case FieldType.U16: case FieldType.S16: case FieldType.Flags16: Bin.W16(d, abs, (int)(v & 0xFFFF)); return;
                case FieldType.U32: case FieldType.S32: Bin.W32(d, abs, (uint)(v & 0xFFFFFFFFL)); return;
            }
            throw new InvalidOperationException("Kein Zahlenfeld");
        }

        /// <summary>Lesbarer Wert, wie er in der Tabelle steht.</summary>
        public string Format(byte[] d, int abs)
        {
            if (IsText) return FixedText.Decode(d, abs, Length);
            return ReadNumber(d, abs).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Parst die Eingabe und schreibt sie in den Puffer. Gibt Fehlertext zurueck oder null.</summary>
        public string TryWrite(byte[] d, int abs, string input)
        {
            if (IsText)
            {
                byte[] enc;
                string err = FixedText.TryEncode(input, d, abs, Length, out enc);
                if (err != null) return err;
                Buffer.BlockCopy(enc, 0, d, abs, Length);
                return null;
            }
            long v;
            string s = (input ?? "").Trim();
            bool ok;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                ok = long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            else
                ok = long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            if (!ok) return L.F("Keine gültige Zahl: \"{0}\"", input);
            if (v < Min || v > Max) return L.F("{0}: erlaubt {1} bis {2}, eingegeben {3}", L.T(Label), Min, Max, v);
            WriteNumber(d, abs, v);
            return null;
        }

        public string Meaning(byte[] d, int abs, Workspace ws)
        {
            if (IsText) return "";
            long v = ReadNumber(d, abs);
            if (IsFlags && FlagNames != null)
            {
                var parts = new List<string>();
                for (int b = 0; b < FlagNames.Length; b++)
                    if ((v & (1L << b)) != 0) parts.Add(L.T(FlagNames[b]));
                long unknownBits = v & ~((1L << FlagNames.Length) - 1);
                if (unknownBits != 0) parts.Add(L.F("unbekannte Bits 0x{0}", unknownBits.ToString("X")));
                return parts.Count == 0 ? L.T("keine") : string.Join(", ", parts.ToArray());
            }
            if (Labels != null)
            {
                if (v >= 0 && v < Labels.Length) return L.T(Labels[v]);
                return L.T("(kein Eintrag in der Bezeichnungsliste)");
            }
            if (Ref != RefKind.None && ws != null) return ws.ResolveRef(Ref, v);
            return "";
        }
    }

    public class RecordDef
    {
        public string Name;
        public int Size;
        public List<FieldDef> Fields = new List<FieldDef>();

        public RecordDef(string name, int size) { Name = name; Size = size; }

        public FieldDef Add(string label, string src, int off, FieldType t)
        {
            var f = new FieldDef { Label = label, SourceName = src, Offset = off, Type = t };
            Fields.Add(f);
            return f;
        }

        public FieldDef Text(string label, string src, int off, int len)
        {
            var f = Add(label, src, off, FieldType.Text);
            f.Length = len;
            return f;
        }

        public FieldDef Unknown(string src, int off, FieldType t)
        {
            var f = Add("Unbekannt @0x" + off.ToString("X2"), src, off, t);
            f.Known = false;
            return f;
        }

        public FieldDef Get(string src)
        {
            foreach (var f in Fields) if (f.SourceName == src) return f;
            throw new KeyNotFoundException(Name + "." + src);
        }

        /// <summary>Prueft, dass die Felder lueckenlos und ueberlappungsfrei genau Size Bytes abdecken.</summary>
        public void SelfCheck()
        {
            var cover = new int[Size];
            foreach (var f in Fields)
            {
                if (f.Offset < 0 || f.Offset + f.Size > Size)
                    throw new Exception(Name + "." + f.SourceName + " liegt außerhalb des Datensatzes");
                for (int i = 0; i < f.Size; i++) cover[f.Offset + i]++;
            }
            for (int i = 0; i < Size; i++)
                if (cover[i] != 1)
                    throw new Exception(Name + ": Byte 0x" + i.ToString("X") + " ist " + cover[i] + "x belegt");
        }
    }

    /// <summary>Abschnitt eines Datensatzes: ein RecordDef an einer festen Dateiposition.</summary>
    public class Section
    {
        public string Title;
        public int Base;
        public RecordDef Def;
        public Section(string title, int b, RecordDef def) { Title = title; Base = b; Def = def; }
    }

    /// <summary>Ein Eintrag in der Liste links (Item, Gegner, Move ...).</summary>
    public class Record
    {
        public GameFile File;
        public string Key;     // gleicher Datensatz in anderer Kopie (xls_data/sys/sys_hardmode) hat denselben Key
        public string Label;
        public string Group;   // z. B. Ordner oder Typ
        public int Index;
        public List<Section> Sections = new List<Section>();
        public override string ToString() { return Label; }
    }

    /// <summary>Feste Textfelder: Single-Byte ASCII, Polsterung mit 0x00 oder Leerzeichen.
    /// Beim Schreiben bleibt Ausrichtung und Fuellzeichen des Originals erhalten.</summary>
    public static class FixedText
    {
        public static string Decode(byte[] d, int abs, int len)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                byte b = d[abs + i];
                if (b == 0) sb.Append(' ');
                else if (b >= 0x20 && b <= 0x7E && b != (byte)'{') sb.Append((char)b);
                else sb.Append("{" + b.ToString("X2") + "}");
            }
            return sb.ToString().Trim(' ');
        }

        public static string TryEncode(string input, byte[] orig, int abs, int len, out byte[] result)
        {
            // Unveraenderter Text: Originalbytes exakt behalten
            if ((input ?? "") == Decode(orig, abs, len))
            {
                result = new byte[len];
                Buffer.BlockCopy(orig, abs, result, 0, len);
                return null;
            }
            return EncodeFresh(input, orig, abs, len, out result);
        }

        /// <summary>
        /// Neu kodieren. Fuellbytes werden positionsgenau aus dem Original uebernommen
        /// (das Spiel mischt 0x20 und 0x00, z. B. "Text   ....\0\0\0"). Wo vorher Text stand, kommt 0x20.
        /// Rechtsbuendige Felder (Namen) bleiben rechtsbuendig, sonst bleibt die Startposition.
        /// </summary>
        public static string EncodeFresh(string input, byte[] orig, int abs, int len, out byte[] result)
        {
            result = null;
            var raw = new List<byte>();
            string s = input ?? "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{' && i + 3 < s.Length && s[i + 3] == '}')
                {
                    byte hb;
                    if (byte.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hb))
                    {
                        raw.Add(hb); i += 3; continue;
                    }
                }
                if (c < 0x20 || c > 0x7E || c == '{')
                    return L.F("Zeichen '{0}' ist nicht erlaubt. Erlaubt: einfache ASCII-Zeichen (keine Umlaute), Sonderbytes als {{XX}}.", c);
                raw.Add((byte)c);
            }
            if (raw.Count > len) return L.F("Text zu lang: {0} Zeichen, maximal {1}.", raw.Count, len);

            // Stil des Originals ermitteln
            int first = -1, last = -1;
            for (int i = 0; i < len; i++)
            {
                byte b = orig[abs + i];
                if (b != 0 && b != 0x20) { if (first < 0) first = i; last = i; }
            }
            bool rightAligned = first > 0 && last == len - 1;

            result = new byte[len];
            for (int i = 0; i < len; i++)
            {
                byte b = orig[abs + i];
                result[i] = (b == 0 || b == 0x20) ? b : (byte)0x20;
            }
            int start;
            if (rightAligned) start = len - raw.Count;
            else if (first > 0 && first + raw.Count <= len) start = first;
            else start = 0;
            for (int i = 0; i < raw.Count; i++) result[start + i] = raw[i];
            return null;
        }
    }
}
