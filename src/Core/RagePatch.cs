using System;
using System.Collections.Generic;
using System.Linq;

namespace G2Modifier.Core
{
    public class RageSettings
    {
        public bool Enabled = true;      // Rage kann ausgeloest werden
        public bool ElenaLock = true;    // Original: Rage gesperrt, sobald Elena (Figur 2) in der Gruppe ist (ab New Valmar)
        public int CharId = 3;           // Original: Millenia
        public int Duration = 1200;      // Original: 0x4B0
        public int Threshold = 100;      // Wut-Schwelle, ab der ein Ausloese-Ereignis die Rage-Chance setzt. Original 100
        public int StartWrath = 0;       // Wut bei Kampfbeginn: 0 Original (aus dem Spielstand), 1 leer (0), 2 voll (100)
        public int Chance = 0;           // Ausloese-Chance je Ereignis: 0 Original (20 bis 50 %), 1 = 50 %, 2 = 75 %, 3 = 100 %
        public bool MinWrathOne = false; // Wut faellt nie unter 1 (Kampfbeginn: max(Wut, 1), Rage-Ende: 1 statt 0)

        public RageSettings Clone() { return (RageSettings)MemberwiseClone(); }
        public bool SameAs(RageSettings o)
        {
            return o != null && Enabled == o.Enabled && CharId == o.CharId && Duration == o.Duration && Threshold == o.Threshold && StartWrath == o.StartWrath && Chance == o.Chance && MinWrathOne == o.MinWrathOne && (!Enabled || ElenaLock == o.ElenaLock);
        }
    }

    /// <summary>
    /// Millenias Rage in grandia2.exe (Steam, SHA-256 adb2bc70...d3e3). Alle Stellen per Ghidra-Dekompilat bestimmt
    /// und gegen die Exe geprueft. Dateioffset = virtuelle Adresse - 0x400C00.
    /// Vor jedem Lesen/Schreiben werden die Bytes um jede Stelle herum gegen das erwartete Muster geprueft.
    /// Passt eine Stelle nicht (andere Exe-Version), wird nichts geschrieben.
    /// </summary>
    public static class RagePatch
    {
        public const int MinId = 1, MaxId = 7;

        /// <summary>
        /// Anniversary Edition: Validate, Read, Write und NeedsLockUpgrade leiten an RagePatchAE weiter (andere Exe, andere Stellen).
        /// Gesetzt von Workspace.Open. Die Stellen der HD-Remaster-Exe unten bleiben unveraendert.
        /// </summary>
        public static bool Anniversary;

        /// <summary>13 Vergleiche "cmp dword [reg+4], 3" (83 7x 04 03). Byte +3 = Figuren-ID.</summary>
        public static readonly int[][] IdSites =
        {
            new[] { 0xB2F0E, 0x4B3B0E }, new[] { 0xB36FB, 0x4B42FB }, new[] { 0xB7553, 0x4B8153 }, new[] { 0xB7A27, 0x4B8627 },
            new[] { 0xC06BC, 0x4C12BC }, new[] { 0xCF3F6, 0x4CFFF6 }, new[] { 0xCF982, 0x4D0582 }, new[] { 0xDEFBC, 0x4DFBBC },
            new[] { 0xE0728, 0x4E1328 }, new[] { 0xE1288, 0x4E1E88 }, new[] { 0xE1684, 0x4E2284 }, new[] { 0xE1C95, 0x4E2895 },
            new[] { 0x1BC0F8, 0x5BCCF8 },
        };

        // mov dword [0x6C3D24], imm32  (Rage-Sperre)
        public const int LockAtBattleStart = 0x17E20;   // VA 0x418A20, Original 0 = frei
        public const int LockIfElena = 0x17EAE;         // VA 0x418AAE, Original 1 = gesperrt wenn Elena in der Gruppe
        static readonly byte[] LockPrefix = { 0xC7, 0x05, 0x24, 0x3D, 0x6C, 0x00 };

        // Dauer: mov dword [0x6C3D48], imm32  und  cmp dword [0x6C3D48], imm32
        public const int DurationSet = 0xCF47A;         // VA 0x4D007A
        public const int DurationCmp = 0x1BF746;        // VA 0x5C0346
        static readonly byte[] DurSetPrefix = { 0xC7, 0x05, 0x48, 0x3D, 0x6C, 0x00 };
        static readonly byte[] DurCmpPrefix = { 0x81, 0x3D, 0x48, 0x3D, 0x6C, 0x00 };

        // Dateiindex der Party-Datei beim Rage-Start ((id-1)*16+7) und -Ende ((id-1)*16+6)
        public const int StartIndex = 0x12F78D;         // VA 0x53038D
        public const int EndIndex = 0x13101C;           // VA 0x531C1C
        static readonly byte[] StartOrig = { 0x8B, 0x51, 0x04, 0x83, 0xEA, 0x01, 0xC1, 0xE2, 0x04, 0x83, 0xC2, 0x07 };
        static readonly byte[] StartFixed = { 0xBA, 0x27, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 }; // edx = 39 = c03ang_e
        static readonly byte[] EndOrig = { 0x8B, 0x48, 0x04, 0x83, 0xE9, 0x01, 0xC1, 0xE1, 0x04, 0x83, 0xC1, 0x06 };
        static readonly byte[] EndFixed = { 0xB9, 0x26, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };   // ecx = 38 = c03ang_s

        // GBIX in den beiden Party-Dateien: Index des Textur-Slots, den das Rage-Gesicht ersetzt.
        // 0xAD = 173 = sys\btl_sys1_2 (Millenias Kampfportraet). Slot der Figur X = 0xAA + X.
        public const int GbixAngE = 0x28;
        public const int GbixAngS = 0x49C8;
        public static int GbixFor(int charId) { return 0xAA + charId; }

        static bool Match(byte[] d, int o, byte[] pat)
        {
            if (o < 0 || o + pat.Length > d.Length) return false;
            for (int i = 0; i < pat.Length; i++) if (d[o + i] != pat[i]) return false;
            return true;
        }

        /// <summary>Prueft alle Stellen. null = Exe passt.</summary>
        public static string Validate(byte[] exe)
        {
            if (Anniversary) return RagePatchAE.Validate(exe);
            var errs = new List<string>();
            if (exe.Length < 0x200 || exe[0] != 'M' || exe[1] != 'Z') return L.T("keine Windows-Exe");
            foreach (var s in IdSites)
            {
                int o = s[0];
                bool ok = o + 6 <= exe.Length && exe[o] == 0x83 && exe[o + 1] >= 0x78 && exe[o + 1] <= 0x7F && exe[o + 1] != 0x7C
                          && exe[o + 2] == 0x04 && exe[o + 3] >= MinId && exe[o + 3] <= MaxId
                          && (exe[o + 4] == 0x75 || (exe[o + 4] == 0x0F && exe[o + 5] == 0x85));
                if (!ok) errs.Add(L.F("ID-Vergleich bei 0x{0}", s[1].ToString("X")));
            }
            foreach (var o in new[] { LockAtBattleStart, LockIfElena })
                if (!Match(exe, o, LockPrefix) || exe[o + 7] != 0 || exe[o + 8] != 0 || exe[o + 9] != 0 || exe[o + 6] > 1)
                    errs.Add(L.F("Rage-Sperre bei Datei 0x{0}", o.ToString("X")));
            if (!Match(exe, DurationSet, DurSetPrefix)) errs.Add(L.T("Dauer (setzen)"));
            if (!Match(exe, DurationCmp, DurCmpPrefix)) errs.Add(L.T("Dauer (Vergleich)"));
            if (!Match(exe, StartIndex, StartOrig) && !Match(exe, StartIndex, StartFixed)) errs.Add(L.T("Dateiindex Rage-Start"));
            if (!Match(exe, EndIndex, EndOrig) && !Match(exe, EndIndex, EndFixed)) errs.Add(L.T("Dateiindex Rage-Ende"));
            if (!ConstantsOk(exe) || BitConverter.ToUInt32(exe, (int)ZeroConst - RdataDelta) != 0) errs.Add(L.T("Konstanten der Wut-Schwelle"));
            foreach (var o in LockReadSites) if (LockReadState(exe, o) < 0) errs.Add(L.F("Rage-Sperre (Lesen) bei Datei 0x{0}", o.ToString("X")));
            foreach (var o in ThresholdSites) if (ThresholdIndexAt(exe, o) < 0) errs.Add(L.F("Wut-Schwelle bei Datei 0x{0}", o.ToString("X")));
            if (StartWrathAt(exe) < 0) errs.Add(L.T("Wut bei Kampfbeginn"));
            for (int i = 0; i < ChanceSites.Length; i++) if (ChanceIndexAt(exe, i) < 0) errs.Add(L.F("Auslöse-Chance bei Datei 0x{0}", ChanceSites[i].ToString("X")));
            for (int i = 0; i < MinChunks.Length; i++) if (MinChunkState(exe, i) < 0) errs.Add(L.F("Mindestwut bei Datei 0x{0}", MinChunks[i].ToString("X")));
            if (!FloatAt(exe, 0x6A83A0, 100f) || !FloatAt(exe, 0x6983D0, 0.5f) || !FloatAt(exe, 0x6985F4, 0.75f) || !FloatAt(exe, 0x6A7E0C, 1f)) errs.Add(L.T("Konstanten der Auslöse-Chance"));
            if (errs.Count > 0) return L.F("Unbekannte Exe-Version, {0} Stelle(n) passen nicht: {1}", errs.Count, string.Join(", ", errs.Take(4).ToArray()) + (errs.Count > 4 ? " ..." : ""));
            return null;
        }

        /// <summary>Liest den Stand. warning != null, wenn die Exe in sich widerspruechlich ist (z. B. halb gepatcht).</summary>
        public static RageSettings Read(byte[] exe, out string warning)
        {
            if (Anniversary) return RagePatchAE.Read(exe, out warning);
            warning = null;
            var s = new RageSettings();
            s.Enabled = exe[LockAtBattleStart + 6] == 0;
            var lr = LockReadSites.Select(o => LockReadState(exe, o)).Distinct().ToList();
            bool bypass = lr.Count == 1 && lr[0] == 1;
            s.ElenaLock = exe[LockIfElena + 6] == 1 && !bypass;
            if (lr.Count > 1) warning = (warning ?? "") + " " + L.T("Die fünf Lesestellen der Rage-Sperre sind uneinheitlich.");

            var ids = IdSites.Select(x => (int)exe[x[0] + 3]).Distinct().ToList();
            s.CharId = ids[0];
            if (ids.Count > 1) warning = L.F("Die 13 Figuren-Vergleiche haben verschiedene Werte ({0}).", string.Join(", ", ids.Select(i => i.ToString()).ToArray()));
            s.Duration = (int)Bin.U32(exe, DurationSet + 6);
            if (Bin.U32(exe, DurationCmp + 6) != s.Duration) warning = (warning ?? "") + " " + L.T("Die beiden Dauer-Werte sind verschieden.");
            var th = ThresholdSites.Select(o => ThresholdIndexAt(exe, o)).Distinct().ToList();
            s.Threshold = th[0] >= 0 ? ThresholdValues[th[0]] : 100;
            if (th.Count > 1) warning = (warning ?? "") + " " + L.T("Die vier Stellen der Wut-Schwelle haben verschiedene Werte.");
            s.StartWrath = Math.Max(0, StartWrathAt(exe));
            var ch = Enumerable.Range(0, ChanceSites.Length).Select(i => ChanceIndexAt(exe, i)).Distinct().ToList();
            s.Chance = ch.Count == 1 ? Math.Max(0, ch[0]) : 0;
            if (ch.Count > 1) warning = (warning ?? "") + " " + L.T("Die vier Stellen der Auslöse-Chance haben verschiedene Werte.");
            var mn = Enumerable.Range(0, MinChunks.Length).Select(i => MinChunkState(exe, i)).Distinct().ToList();
            s.MinWrathOne = mn.Count == 1 && mn[0] == 1;
            if (mn.Count > 1) warning = (warning ?? "") + " " + L.T("Die Stellen der Mindestwut sind nur teilweise umgestellt.");
            bool fixedIdx = Match(exe, StartIndex, StartFixed), fixedEnd = Match(exe, EndIndex, EndFixed);
            if (fixedIdx != fixedEnd) warning = (warning ?? "") + " " + L.T("Nur einer der beiden Dateiindizes ist umgestellt.");
            if (s.CharId != 3 && !fixedIdx) warning = (warning ?? "") + " " + L.T("Rage-Figur ist nicht Millenia, aber der Dateiindex ist nicht fixiert.");
            return s;
        }

        /// <summary>Stand von 0.12.2 und frueher: Elena-Sperre aus, aber die fuenf Lesestellen noch original.</summary>
        public static bool NeedsLockUpgrade(byte[] exe)
        {
            if (Anniversary) return false;   // AE-Unterstuetzung gibt es erst seit 0.13.0, also keinen Altstand
            if (Validate(exe) != null) return false;
            return exe[LockAtBattleStart + 6] == 0 && exe[LockIfElena + 6] == 0 && LockReadSites.All(o => LockReadState(exe, o) == 0);
        }

        /// <summary>Schreibt den Stand in die Exe-Daten (nur im Speicher, gespeichert wird per Strg+S).</summary>
        public static void Write(byte[] exe, RageSettings s)
        {
            if (Anniversary) { RagePatchAE.Write(exe, s); return; }
            string e = Validate(exe);
            if (e != null) throw new InvalidOperationException(e);
            if (s.CharId < MinId || s.CharId > MaxId) throw new ArgumentOutOfRangeException("CharId");
            if (s.Duration < 1 || s.Duration > 100000) throw new ArgumentOutOfRangeException("Duration");
            int ti = Array.IndexOf(ThresholdValues, s.Threshold);
            if (ti < 0) throw new ArgumentOutOfRangeException("Threshold");
            if (s.StartWrath < 0 || s.StartWrath >= StartWrathConst.Length) throw new ArgumentOutOfRangeException("StartWrath");
            if (s.Chance < 0 || s.Chance > ChanceConst.Length) throw new ArgumentOutOfRangeException("Chance");
            foreach (var x in IdSites) exe[x[0] + 3] = (byte)s.CharId;
            // Rage aus: Sperre beim Kampfstart auf 1. Elena-Zeile ebenfalls 1, sonst wuerde sie die Sperre nie aufheben,
            // aber auch nichts freigeben (sie setzt nur 1). Bei "an" entscheidet der Haken.
            exe[LockAtBattleStart + 6] = (byte)(s.Enabled ? 0 : 1);
            exe[LockIfElena + 6] = (byte)(!s.Enabled || s.ElenaLock ? 1 : 0);
            Bin.W32(exe, DurationSet + 6, (uint)s.Duration);
            Bin.W32(exe, DurationCmp + 6, (uint)s.Duration);
            foreach (var o in ThresholdSites) Bin.W32(exe, o + 3, ThresholdConst[ti]);
            Bin.W32(exe, StartWrathSite + 4, StartWrathConst[s.StartWrath]);
            for (int i = 0; i < ChanceSites.Length; i++) Bin.W32(exe, ChanceSites[i] + 4, s.Chance == 0 ? ChanceOrig[i] : ChanceConst[s.Chance - 1]);
            for (int i = 0; i < MinChunks.Length; i++) Buffer.BlockCopy(s.MinWrathOne ? MinNew[i] : MinOrig[i], 0, exe, MinChunks[i], MinOrig[i].Length);
            bool bypassLock = s.Enabled && !s.ElenaLock;
            foreach (var o in LockReadSites) Bin.W32(exe, o + 2, bypassLock ? ZeroConst : LockVar);
            Buffer.BlockCopy(s.CharId == 3 ? StartOrig : StartFixed, 0, exe, StartIndex, 12);
            Buffer.BlockCopy(s.CharId == 3 ? EndOrig : EndFixed, 0, exe, EndIndex, 12);
        }

        // Wut-Schwelle: 4 Vergleiche "comiss xmm0, [konstante]" direkt vor dem Setzen der Rage-Chance [0x6C3D44].
        // Die Wut [0x6C3D40] steigt bei Treffern auf die Rage-Figur um 2 bis 5, bei bestimmten Ereignissen um mehr.
        // Original-Konstante 100.0 (0x6A83A0, wird auch anderswo genutzt, daher nie geaendert). Umgestellt wird nur der
        // Zeiger auf eine andere, schon vorhandene Konstante in .rdata mit dem gewuenschten Wert.
        public static readonly int[] ThresholdSites = { 0x4D4D5, 0xDF014, 0xE07B8, 0xE0834 };   // VA 0x44E0D5, 0x4DFC14, 0x4E13B8, 0x4E1434
        static readonly byte[] ThresholdPrefix = { 0x0F, 0x2F, 0x05 };
        public static readonly int[] ThresholdValues = { 100, 50, 30, 20, 10, 0 };
        static readonly uint[] ThresholdConst = { 0x6A83A0, 0x69F6E0, 0x6A82CC, 0x6A82A4, 0x6981B0, 0x6A7A58 };
        const int RdataDelta = 0x401600;   // .rdata: Dateioffset = VA - 0x401600

        // Die Sperre [0x6C3D24] wird an genau fuenf Stellen gelesen ("cmp dword [0x6C3D24], 0": Ring zeichnen, Wut
        // aufbauen, Ausloesen, Wut zu Kampfbeginn uebernehmen, Wut am Kampfende sichern). Bei "Rage auch mit Elena"
        // zeigen alle fuenf auf eine feste Null in .rdata (0x6A7A58). Dann ist egal, wer die Sperre irgendwo setzt.
        public static readonly int[] LockReadSites = { 0x16A85, 0x1837E, 0xBF952, 0xC0609, 0xCF3E6 };  // VA 0x417685, 0x418F7E, 0x4C0552, 0x4C1209, 0x4CFFE6
        const uint LockVar = 0x6C3D24, ZeroConst = 0x6A7A58;

        static int LockReadState(byte[] exe, int site)   // 0 = Original, 1 = umgangen, -1 = unbekannt
        {
            if (site + 7 > exe.Length || exe[site] != 0x83 || exe[site + 1] != 0x3D || exe[site + 6] != 0) return -1;
            uint a = (uint)Bin.U32(exe, site + 2);
            return a == LockVar ? 0 : a == ZeroConst ? 1 : -1;
        }

        // Wut bei Kampfbeginn: "movss xmm0, [0x6C3C1C]" (gesicherte Wut aus dem Spielstand) -> [0x6C3D40].
        // Die gesicherte Wut bleibt ueber Kaempfe und Speichern erhalten und wird nur nach einer Rage auf 0 gesetzt.
        // Deshalb wirkt die Wut-Schwelle im Original erst nach der ersten Rage. "leer" zeigt auf 0.0, "voll" auf 100.0.
        public const int StartWrathSite = 0x16A8E;      // VA 0x41768E
        public static readonly int[] StartWrathValues = { -1, 0, 100 };
        static readonly uint[] StartWrathConst = { 0x6C3C1C, 0x6A7A58, 0x6A83A0 };
        static readonly byte[] MovssPrefix = { 0xF3, 0x0F, 0x10, 0x05 };

        // Ausloese-Chance: "movss xmm0, [konstante]" direkt vor "movss [0x6C3D44], xmm0" an den vier Ausloese-Ereignissen.
        // Original 0.2 / 0.25 / 0.33 / 0.5. Gewuerfelt wird einmal beim naechsten Treffer-Zustand der Figur (rand/32768 <= Chance).
        public static readonly int[] ChanceSites = { 0x4D4DE, 0xDF01D, 0xE07C1, 0xE083D };   // VA 0x44E0DE, 0x4DFC1D, 0x4E13C1, 0x4E143D
        static readonly uint[] ChanceOrig = { 0x6A7C24, 0x6A7C34, 0x6A7C5C, 0x6A7CF8 };
        public static readonly int[] ChanceValues = { 0, 50, 75, 100 };                          // 0 = Original
        static readonly uint[] ChanceConst = { 0x6983D0, 0x6985F4, 0x6A7E0C };                  // 0.5, 0.75, 1.0 (0.5 bewusst nicht 0x6A7CF8, das ist schon das Original der vierten Stelle)

        // Mindestwut 1. Die Exe ist mit ASLR gebaut (Relocations). Deshalb steht jede absolute Adresse weiter genau an der
        // Stelle, an der die Relocation-Tabelle eine erwartet (0x417692, 0x41769A, 0x4176A7, 0x531CFD); neue Adressen gibt es keine.
        // Kampfbeginn, Original:  jne +12 | movss xmm0,[gesichert] | movss [Wut],xmm0 | jmp +11 | xorps xmm0,xmm0 | movss [Wut],xmm0
        //            neu:          jne +0  | movss xmm0,[gesichert] | maxss xmm0,[1.0] | 5x nop             | movss [Wut],xmm0
        //   -> Wut = max(gesichert, 1), auch wenn die Sperre gesetzt ist (ausloesen kann sie dann trotzdem nicht).
        //   "Wut bei Kampfbeginn" stellt weiter nur den Zeiger bei 0x417692 um (leer -> 1, voll -> 100).
        // Rage-Ende (0x531CF6), Original: xorps xmm0,xmm0 | movss [Wut],xmm0   neu: mov eax,1.0f | mov [Wut],eax
        //   eax wird danach erst wieder nach einem Aufruf gelesen, der es selbst setzt.
        public static readonly int[] MinChunks = { 0x16A8C, 0x16A96, 0x16A9E, 0x1310F6 };   // VA 0x41768C, 0x417696, 0x41769E, 0x531CF6
        static readonly byte[][] MinOrig =
        {
            new byte[] { 0x75, 0x12 },
            new byte[] { 0xF3, 0x0F, 0x11, 0x05, 0x40, 0x3D, 0x6C, 0x00 },
            new byte[] { 0xEB, 0x0B, 0x0F, 0x57, 0xC0 },
            new byte[] { 0x0F, 0x57, 0xC0, 0xF3, 0x0F, 0x11, 0x05, 0x40, 0x3D, 0x6C, 0x00 },
        };
        static readonly byte[][] MinNew =
        {
            new byte[] { 0x75, 0x00 },
            new byte[] { 0xF3, 0x0F, 0x5F, 0x05, 0x0C, 0x7E, 0x6A, 0x00 },   // maxss xmm0, [0x6A7E0C] = 1.0
            new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 },
            new byte[] { 0xB8, 0x00, 0x00, 0x80, 0x3F, 0x89, 0x05, 0x40, 0x3D, 0x6C, 0x00 },
        };

        static int MinChunkState(byte[] exe, int i)   // 0 = Original, 1 = Mindestwut, -1 = unbekannt
        {
            return Match(exe, MinChunks[i], MinOrig[i]) ? 0 : Match(exe, MinChunks[i], MinNew[i]) ? 1 : -1;
        }

        static int StartWrathAt(byte[] exe)
        {
            if (!Match(exe, StartWrathSite, MovssPrefix)) return -1;
            return Array.IndexOf(StartWrathConst, (uint)Bin.U32(exe, StartWrathSite + 4));
        }

        static int ChanceIndexAt(byte[] exe, int i)   // 0 = Original, 1..3 = ChanceConst, -1 = unbekannt
        {
            int o = ChanceSites[i];
            if (!Match(exe, o, MovssPrefix)) return -1;
            uint a = (uint)Bin.U32(exe, o + 4);
            if (a == ChanceOrig[i]) return 0;
            int k = Array.IndexOf(ChanceConst, a);
            return k < 0 ? -1 : k + 1;
        }

        static bool FloatAt(byte[] exe, uint va, float v)
        {
            int o = (int)va - RdataDelta;
            return o >= 0 && o + 4 <= exe.Length && BitConverter.ToSingle(exe, o) == v;
        }

        static int ThresholdIndexAt(byte[] exe, int site)
        {
            if (!Match(exe, site, ThresholdPrefix)) return -1;
            return Array.IndexOf(ThresholdConst, (uint)Bin.U32(exe, site + 3));
        }

        static bool ConstantsOk(byte[] exe)
        {
            for (int i = 0; i < ThresholdConst.Length; i++)
            {
                int o = (int)ThresholdConst[i] - RdataDelta;
                if (o < 0 || o + 4 > exe.Length || BitConverter.ToSingle(exe, o) != ThresholdValues[i]) return false;
            }
            return true;
        }

        /// <summary>GBIX-Kopf pruefen: "GBIX", Laenge 8, dann der Index.</summary>
        public static string CheckGbix(byte[] dat, int indexOffset)
        {
            int h = indexOffset - 8;
            if (dat == null || h < 0 || indexOffset + 4 > dat.Length) return L.T("Datei zu kurz");
            if (dat[h] != 'G' || dat[h + 1] != 'B' || dat[h + 2] != 'I' || dat[h + 3] != 'X' || Bin.U32(dat, h + 4) != 8) return L.T("kein GBIX-Kopf an der erwarteten Stelle");
            long v = Bin.U32(dat, indexOffset);
            if (v < GbixFor(MinId) || v > GbixFor(MaxId)) return L.F("unerwarteter GBIX-Wert 0x{0}", v.ToString("X"));
            return null;
        }

        public static int ReadGbixChar(byte[] dat, int indexOffset) { return (int)Bin.U32(dat, indexOffset) - 0xAA; }
        public static void WriteGbixChar(byte[] dat, int indexOffset, int charId) { Bin.W32(dat, indexOffset, (uint)GbixFor(charId)); }
    
        // ------------------------------------------------------------ Workspace-Ebene

        public static bool Available(Workspace ws) { return ws.Exe != null && ws.Exe.Valid; }
        static bool AngOk(GameFile f) { return f != null && f.Valid; }

        public static RageSettings Current(Workspace ws, out string warning)
        {
            var s = Read(ws.Exe.Data, out warning);
            foreach (var t in new[] { new { F = ws.AngE, O = GbixAngE }, new { F = ws.AngS, O = GbixAngS } })
                if (AngOk(t.F) && ReadGbixChar(t.F.Data, t.O) != s.CharId)
                    warning = (warning ?? "") + " " + L.F("{0} zeigt auf den Porträt-Platz von Figur {1}.", System.IO.Path.GetFileName(t.F.RelPath), ReadGbixChar(t.F.Data, t.O));
            return s;
        }

        /// <summary>Stand laut Backup. Ohne Backup: die Werte der unveraenderten Steam-Exe.</summary>
        public static RageSettings Original(Workspace ws)
        {
            string w;
            return ws.Exe.Original != null && Validate(ws.Exe.Original) == null ? Read(ws.Exe.Original, out w) : new RageSettings();
        }

        /// <summary>Schreibt Exe und GBIX der beiden Party-Dateien (nur im Speicher). Rueckgabe: Hinweise.</summary>
        public static List<string> Apply(Workspace ws, RageSettings s)
        {
            var notes = new List<string>();
            var exe = (byte[])ws.Exe.Data.Clone();
            Write(exe, s);
            ws.Exe.Data = exe;
            foreach (var t in new[] { new { F = ws.AngE, O = GbixAngE }, new { F = ws.AngS, O = GbixAngS } })
            {
                if (!AngOk(t.F)) { if (s.CharId != 3) notes.Add(L.T("Party-Datei fehlt oder ist gesperrt: Das Rage-Gesicht landet weiter auf Millenias Porträt-Platz.")); continue; }
                var d = (byte[])t.F.Data.Clone();
                WriteGbixChar(d, t.O, s.CharId);
                t.F.Data = d;
            }
            return notes;
        }
    }
}
