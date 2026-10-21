using System;
using System.Collections.Generic;
using System.Linq;

namespace G2Modifier.Core
{
    /// <summary>
    /// Millenias Rage in grandia2.exe der Anniversary Edition (Steam, 2015, PE-Zeitstempel 0x5640FE82, 3.535.872 Byte).
    /// Gleiche Einstellungen wie RagePatch (HD Remaster), aber andere Exe: anderer Compiler, andere Adressen, anderer Code.
    /// Alle Stellen per Disassemblat bestimmt und gegen die HD-Stellen abgeglichen (gleiche Funktionen, gleiche Logik).
    /// Dateioffset = VA - 0x400C00 (.text), .rdata: VA - 0x401600.
    /// Variablen: Rage-Sperre 0x68097C, Wut 0x680784, Ausloese-Chance 0x680788, Rage-Dauer 0x68078C, gesicherte Wut 0x68104C.
    /// Die Exe ist mit ASLR gebaut. Jede absolute Adresse bleibt genau an der Stelle, die die Relocation-Tabelle vorsieht.
    /// Vor jedem Lesen/Schreiben werden alle Stellen gegen das erwartete Muster geprueft. Passt eine nicht, wird nichts geschrieben.
    /// </summary>
    public static class RagePatchAE
    {
        const int RdataDelta = 0x401600;

        /// <summary>13 Vergleiche "cmp dword [reg+4], 3", dieselben 13 Stellen wie in der HD-Exe. Byte +3 = Figuren-ID.
        /// (0x4CDE98 "cmp [eax+4],3 / je" bleibt wie in HD unangetastet.)</summary>
        public static readonly int[][] IdSites =
        {
            new[] { 0xAF908, 0x4B0508 }, new[] { 0xB1AA1, 0x4B26A1 }, new[] { 0xB201E, 0x4B2C1E }, new[] { 0xB5FBC, 0x4B6BBC },
            new[] { 0xB66CF, 0x4B72CF }, new[] { 0xB85E9, 0x4B91E9 }, new[] { 0xB89DC, 0x4B95DC }, new[] { 0xCC0B8, 0x4CCCB8 },
            new[] { 0xCD39D, 0x4CDF9D }, new[] { 0xCDEAE, 0x4CEAAE }, new[] { 0xCE229, 0x4CEE29 }, new[] { 0xCE42E, 0x4CF02E },
            new[] { 0x184E22, 0x585A22 },
        };

        // Rage-Sperre beim Kampfstart: "xor eax,eax" ... "mov [0x68097C], eax" (VA 0x413D16 / 0x413D6E).
        // eax wird dazwischen nicht benutzt. Rage aus: "mov al,1" -> Sperre ungleich 0 (alle Lesestellen pruefen nur auf 0).
        public const int LockInit = 0x13116;            // VA 0x413D16
        static readonly byte[] LockInitOn = { 0x33, 0xC0 }, LockInitOff = { 0xB0, 0x01 };
        const int LockStore = 0x1316E;                   // VA 0x413D6E
        static readonly byte[] LockStorePat = { 0xA3, 0x7C, 0x09, 0x68, 0x00 };

        // Elena: "cmp bl,2 | mov eax,[Sperre] | mov ecx,1 | cmove eax,ecx | mov [Sperre],eax" (VA 0x413DAE).
        // Byte bei 0x413DB7 = Wert, der bei Elena gesetzt wird (Original 1).
        public const int ElenaSite = 0x131AE;            // VA 0x413DAE
        public const int ElenaImm = ElenaSite + 9;       // VA 0x413DB7
        static readonly byte[] ElenaPrefix = { 0x80, 0xFB, 0x02, 0xA1, 0x7C, 0x09, 0x68, 0x00, 0xB9 };
        static readonly byte[] ElenaSuffix = { 0x00, 0x00, 0x00, 0x0F, 0x44, 0xC1, 0xA3, 0x7C, 0x09, 0x68, 0x00 };

        // Die Sperre wird an genau fuenf Stellen gelesen ("cmp dword [0x68097C], 0"): Wut zu Kampfbeginn uebernehmen,
        // Wut am Kampfende sichern, Ring zeichnen, Wut aufbauen, Ausloesen. Wie in HD: bei "Rage auch mit Elena"
        // zeigen alle fuenf auf eine feste Null in .rdata.
        public static readonly int[] LockReadSites = { 0x12407, 0x136A1, 0xAEEDC, 0xAF83C, 0xB85DC };  // VA 0x413007, 0x4142A1, 0x4AFADC, 0x4B043C, 0x4B91DC
        const uint LockVar = 0x68097C, ZeroConst = 0x662D8C;

        // Dauer: mov dword [0x68078C], imm32  und  cmp dword [0x68078C], imm32
        public const int DurationSet = 0xB865A;          // VA 0x4B925A
        public const int DurationCmp = 0x18744A;         // VA 0x58804A
        static readonly byte[] DurSetPrefix = { 0xC7, 0x05, 0x8C, 0x07, 0x68, 0x00 };
        static readonly byte[] DurCmpPrefix = { 0x81, 0x3D, 0x8C, 0x07, 0x68, 0x00 };

        // Dateiindex der Party-Datei beim Rage-Start (id*16-9 = (id-1)*16+7) und -Ende (id*16-10 = (id-1)*16+6)
        public const int StartIndex = 0x149F7D;          // VA 0x54AB7D
        public const int EndIndex = 0x14EDE1;            // VA 0x54F9E1
        static readonly byte[] StartOrig = { 0x8B, 0x43, 0x04, 0xC1, 0xE0, 0x04, 0x83, 0xE8, 0x09 };
        static readonly byte[] StartFixed = { 0xB8, 0x27, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90 };   // eax = 39 = c03ang_e
        static readonly byte[] EndOrig = { 0x8B, 0x43, 0x04, 0xC1, 0xE0, 0x04, 0x83, 0xE8, 0x0A };
        static readonly byte[] EndFixed = { 0xB8, 0x26, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90 };     // eax = 38 = c03ang_s

        // Wut-Schwelle: 4 Vergleiche "comiss xmm0, [konstante]" mit der neuen Wut direkt vor dem Setzen der Rage-Chance.
        // Original 100.0 (0x6635C0, wird auch anderswo genutzt, daher nie geaendert). Umgestellt wird nur der Zeiger auf
        // eine andere, schon vorhandene Konstante in .rdata (jeweils die vom Code meistgenutzte mit diesem Wert).
        public static readonly int[] ThresholdSites = { 0x713F1, 0xCC0F1, 0xCD4FC, 0xCD54B };   // VA 0x471FF1, 0x4CCCF1, 0x4CE0FC, 0x4CE14B
        static readonly byte[] ThresholdPrefix = { 0x0F, 0x2F, 0x05 };
        static readonly uint[] ThresholdConst = { 0x6635C0, 0x66354C, 0x663504, 0x6634E8, 0x6634BC, 0x662D8C };   // 100, 50, 30, 20, 10, 0

        // Wut bei Kampfbeginn: "movss xmm0, [0x68104C]" (gesicherte Wut) -> [0x680784]. "leer" zeigt auf 0.0, "voll" auf 100.0.
        public const int StartWrathSite = 0x12488;       // VA 0x413088
        static readonly uint[] StartWrathConst = { 0x68104C, 0x662D8C, 0x6635C0 };
        static readonly byte[] MovssPrefix = { 0xF3, 0x0F, 0x10, 0x05 };

        // Ausloese-Chance: hier ein Direktwert "mov dword [0x680788], float" an den vier Ausloese-Ereignissen
        // (HD: Zeiger auf Konstante). Original 0.2 / 0.25 / 0.5 / 0.33.
        public static readonly int[] ChanceSites = { 0x71402, 0xCC108, 0xCD50D, 0xCD55C };   // VA 0x472002, 0x4CCD08, 0x4CE10D, 0x4CE15C
        static readonly byte[] ChancePrefix = { 0xC7, 0x05, 0x88, 0x07, 0x68, 0x00 };
        static readonly uint[] ChanceOrig = { 0x3E4CCCCD, 0x3E800000, 0x3F000000, 0x3EA8F5C3 };
        static readonly uint[] ChanceImm = { 0x3F000000, 0x3F400000, 0x3F800000 };            // 0.5, 0.75, 1.0

        // Mindestwut 1, Kampfbeginn (VA 0x413086 bis 0x4130A3). Relocations bei 0x41308C, 0x413094, 0x41309C bleiben Adressen.
        //   Original: jne +12 | movss xmm0,[gesichert] | movss [Wut],xmm0 | jmp +10 | mov dword [Wut],0
        //   neu:      jne +0  | movss xmm0,[gesichert] | maxss xmm0,[1.0] | movss [Wut],xmm0 | 4x nop
        //   [gesichert] (0x41308C) bleibt der Zeiger von "Wut bei Kampfbeginn".
        // Rage-Ende (VA 0x54F91F): "push 80h | push edi | mov [Wut],eax | mov [Chance],eax | mov [Dauer],eax" mit eax = 0.
        //   Fuer 1.0 ist dort kein Platz (drei Adressen mit Relocation direkt hintereinander). Deshalb: "push 80h" wird
        //   "jmp Hoehle". Die Hoehle (VA 0x4496A0, unbenutzte CC-Fuellbytes nach einem ret, ohne Relocations) setzt
        //   Wut = 1.0, Chance = 0, Dauer = 0 ueber eine positionsunabhaengige Basis (call/pop, also ASLR-fest), stellt ecx
        //   wieder her, legt die beiden Argumente auf den Stapel und springt zurueck auf den Aufruf bei 0x54F934.
        //   Die alten Speicherbefehle bleiben unberuehrt stehen (werden uebersprungen), ihre Relocations damit gueltig.
        public const int MinStartJne = 0x12486, MinStartBody = 0x12490, MinEnd = 0x14ED1F, Cave = 0x48AA0;
        const uint VaWrath = 0x680784, VaChance = 0x680788, VaDur = 0x68078C, OneConst = 0x663020;
        const uint VaMinEnd = 0x54F91F, VaBack = 0x54F934, VaCave = 0x4496A0;
        static readonly byte[] MinJneOrig = { 0x75, 0x12 }, MinJneNew = { 0x75, 0x00 };
        static readonly byte[] MinBodyOrig = { 0xF3, 0x0F, 0x11, 0x05, 0x84, 0x07, 0x68, 0x00, 0xEB, 0x0A, 0xC7, 0x05, 0x84, 0x07, 0x68, 0x00, 0x00, 0x00, 0x00, 0x00 };
        static readonly byte[] MinBodyNew = { 0xF3, 0x0F, 0x5F, 0x05, 0x20, 0x30, 0x66, 0x00, 0xF3, 0x0F, 0x11, 0x05, 0x84, 0x07, 0x68, 0x00, 0x90, 0x90, 0x90, 0x90 };
        static readonly byte[] MinEndOrig = { 0x68, 0x80, 0x00, 0x00, 0x00 };
        static readonly byte[] MinEndTail = { 0x57, 0xA3, 0x84, 0x07, 0x68, 0x00, 0xA3, 0x88, 0x07, 0x68, 0x00, 0xA3, 0x8C, 0x07, 0x68, 0x00, 0xE8 };  // bleibt immer so
        static readonly byte[] MinEndNew = JmpRel(VaMinEnd, VaCave);
        static readonly byte[] CaveCode = BuildCave();
        public const int CaveSize = 41, MinBodySize = 20;

        static byte[] JmpRel(uint from, uint to)
        {
            var b = new byte[5]; b[0] = 0xE9;
            Array.Copy(BitConverter.GetBytes((int)(to - (from + 5))), 0, b, 1, 4);
            return b;
        }

        static byte[] BuildCave()
        {
            uint l = VaCave + 6;   // Adresse des "pop ecx" = Basis
            var c = new List<byte>();
            c.Add(0x51);                                                           // push ecx
            c.AddRange(new byte[] { 0xE8, 0, 0, 0, 0 });                           // call $+5
            c.Add(0x59);                                                           // pop ecx  (ecx = l)
            c.AddRange(new byte[] { 0xC7, 0x81 }); c.AddRange(BitConverter.GetBytes((int)(VaWrath - l))); c.AddRange(BitConverter.GetBytes(0x3F800000));   // mov dword [ecx+Wut], 1.0f
            c.AddRange(new byte[] { 0x89, 0x81 }); c.AddRange(BitConverter.GetBytes((int)(VaChance - l)));   // mov [ecx+Chance], eax (0)
            c.AddRange(new byte[] { 0x89, 0x81 }); c.AddRange(BitConverter.GetBytes((int)(VaDur - l)));      // mov [ecx+Dauer], eax (0)
            c.Add(0x59);                                                           // pop ecx
            c.AddRange(new byte[] { 0x68, 0x80, 0, 0, 0 });                        // push 80h
            c.Add(0x57);                                                           // push edi
            c.AddRange(JmpRel(VaCave + (uint)c.Count, VaBack));                    // jmp 0x54F934 (call 0x4B6A20)
            return c.ToArray();
        }

        static bool Match(byte[] d, int o, byte[] pat)
        {
            if (o < 0 || o + pat.Length > d.Length) return false;
            for (int i = 0; i < pat.Length; i++) if (d[o + i] != pat[i]) return false;
            return true;
        }

        static bool AllCC(byte[] d, int o, int n)
        {
            if (o < 0 || o + n > d.Length) return false;
            for (int i = 0; i < n; i++) if (d[o + i] != 0xCC) return false;
            return true;
        }

        static bool FloatAt(byte[] exe, uint va, float v)
        {
            int o = (int)va - RdataDelta;
            return o >= 0 && o + 4 <= exe.Length && BitConverter.ToSingle(exe, o) == v;
        }

        /// <summary>Prueft alle Stellen. null = Exe passt.</summary>
        public static string Validate(byte[] exe)
        {
            var errs = new List<string>();
            if (exe.Length < 0x200 || exe[0] != 'M' || exe[1] != 'Z') return L.T("keine Windows-Exe");
            if (GameVersions.ExeStamp(exe) != GameVersions.AeExeStamp || exe.Length < 0x260000) return L.T("Keine Exe der Anniversary Edition (Build 2015).");
            foreach (var s in IdSites)
            {
                int o = s[0];
                bool ok = o + 6 <= exe.Length && exe[o] == 0x83 && exe[o + 1] >= 0x78 && exe[o + 1] <= 0x7F && exe[o + 1] != 0x7C
                          && exe[o + 2] == 0x04 && exe[o + 3] >= RagePatch.MinId && exe[o + 3] <= RagePatch.MaxId
                          && (exe[o + 4] == 0x75 || (exe[o + 4] == 0x0F && exe[o + 5] == 0x85));
                if (!ok) errs.Add(L.F("ID-Vergleich bei 0x{0}", s[1].ToString("X")));
            }
            if ((!Match(exe, LockInit, LockInitOn) && !Match(exe, LockInit, LockInitOff)) || !Match(exe, LockInit + 2, new byte[] { 0xC7, 0x05 }) || !Match(exe, LockStore, LockStorePat))
                errs.Add(L.F("Rage-Sperre bei Datei 0x{0}", LockInit.ToString("X")));
            if (!Match(exe, ElenaSite, ElenaPrefix) || exe[ElenaImm] > 1 || !Match(exe, ElenaImm + 1, ElenaSuffix))
                errs.Add(L.F("Rage-Sperre bei Datei 0x{0}", ElenaSite.ToString("X")));
            if (!Match(exe, DurationSet, DurSetPrefix)) errs.Add(L.T("Dauer (setzen)"));
            if (!Match(exe, DurationCmp, DurCmpPrefix)) errs.Add(L.T("Dauer (Vergleich)"));
            if (!Match(exe, StartIndex, StartOrig) && !Match(exe, StartIndex, StartFixed)) errs.Add(L.T("Dateiindex Rage-Start"));
            if (!Match(exe, EndIndex, EndOrig) && !Match(exe, EndIndex, EndFixed)) errs.Add(L.T("Dateiindex Rage-Ende"));
            if (!ConstantsOk(exe) || BitConverter.ToUInt32(exe, (int)ZeroConst - RdataDelta) != 0) errs.Add(L.T("Konstanten der Wut-Schwelle"));
            foreach (var o in LockReadSites) if (LockReadState(exe, o) < 0) errs.Add(L.F("Rage-Sperre (Lesen) bei Datei 0x{0}", o.ToString("X")));
            foreach (var o in ThresholdSites) if (ThresholdIndexAt(exe, o) < 0) errs.Add(L.F("Wut-Schwelle bei Datei 0x{0}", o.ToString("X")));
            if (StartWrathAt(exe) < 0) errs.Add(L.T("Wut bei Kampfbeginn"));
            for (int i = 0; i < ChanceSites.Length; i++) if (ChanceIndexAt(exe, i) == -1) errs.Add(L.F("Auslöse-Chance bei Datei 0x{0}", ChanceSites[i].ToString("X")));
            for (int i = 0; i < 3; i++) if (MinChunkState(exe, i) < 0) errs.Add(L.F("Mindestwut bei Datei 0x{0}", (i == 0 ? MinStartJne : i == 1 ? MinStartBody : MinEnd).ToString("X")));
            if (!FloatAt(exe, OneConst, 1f)) errs.Add(L.T("Konstanten der Auslöse-Chance"));
            if (errs.Count > 0) return L.F("Unbekannte Exe-Version, {0} Stelle(n) passen nicht: {1}", errs.Count, string.Join(", ", errs.Take(4).ToArray()) + (errs.Count > 4 ? " ..." : ""));
            return null;
        }

        /// <summary>Liest den Stand. warning != null, wenn die Exe in sich widerspruechlich ist (z. B. halb gepatcht).</summary>
        public static RageSettings Read(byte[] exe, out string warning)
        {
            warning = null;
            var s = new RageSettings();
            s.Enabled = Match(exe, LockInit, LockInitOn);
            var lr = LockReadSites.Select(o => LockReadState(exe, o)).Distinct().ToList();
            bool bypass = lr.Count == 1 && lr[0] == 1;
            s.ElenaLock = exe[ElenaImm] == 1 && !bypass;
            if (lr.Count > 1) warning = (warning ?? "") + " " + L.T("Die fünf Lesestellen der Rage-Sperre sind uneinheitlich.");

            var ids = IdSites.Select(x => (int)exe[x[0] + 3]).Distinct().ToList();
            s.CharId = ids[0];
            if (ids.Count > 1) warning = L.F("Die 13 Figuren-Vergleiche haben verschiedene Werte ({0}).", string.Join(", ", ids.Select(i => i.ToString()).ToArray()));
            s.Duration = (int)Bin.U32(exe, DurationSet + 6);
            if (Bin.U32(exe, DurationCmp + 6) != s.Duration) warning = (warning ?? "") + " " + L.T("Die beiden Dauer-Werte sind verschieden.");
            var th = ThresholdSites.Select(o => ThresholdIndexAt(exe, o)).Distinct().ToList();
            s.Threshold = th[0] >= 0 ? RagePatch.ThresholdValues[th[0]] : 100;
            if (th.Count > 1) warning = (warning ?? "") + " " + L.T("Die vier Stellen der Wut-Schwelle haben verschiedene Werte.");
            s.StartWrath = Math.Max(0, StartWrathAt(exe));
            // -2 = Stelle passt zu Original und 50 % (dritte Stelle, Original ist dort schon 0.5): zaehlt nicht mit
            var ch = Enumerable.Range(0, ChanceSites.Length).Select(i => ChanceIndexAt(exe, i)).Where(x => x != -2).Distinct().ToList();
            s.Chance = ch.Count == 1 ? Math.Max(0, ch[0]) : 0;
            if (ch.Count > 1) warning = (warning ?? "") + " " + L.T("Die vier Stellen der Auslöse-Chance haben verschiedene Werte.");
            var mn = Enumerable.Range(0, 3).Select(i => MinChunkState(exe, i)).Distinct().ToList();
            s.MinWrathOne = mn.Count == 1 && mn[0] == 1;
            if (mn.Count > 1) warning = (warning ?? "") + " " + L.T("Die Stellen der Mindestwut sind nur teilweise umgestellt.");
            bool fixedIdx = Match(exe, StartIndex, StartFixed), fixedEnd = Match(exe, EndIndex, EndFixed);
            if (fixedIdx != fixedEnd) warning = (warning ?? "") + " " + L.T("Nur einer der beiden Dateiindizes ist umgestellt.");
            if (s.CharId != 3 && !fixedIdx) warning = (warning ?? "") + " " + L.T("Rage-Figur ist nicht Millenia, aber der Dateiindex ist nicht fixiert.");
            return s;
        }

        /// <summary>Schreibt den Stand in die Exe-Daten (nur im Speicher, gespeichert wird per Strg+S).</summary>
        public static void Write(byte[] exe, RageSettings s)
        {
            string e = Validate(exe);
            if (e != null) throw new InvalidOperationException(e);
            if (s.CharId < RagePatch.MinId || s.CharId > RagePatch.MaxId) throw new ArgumentOutOfRangeException("CharId");
            if (s.Duration < 1 || s.Duration > 100000) throw new ArgumentOutOfRangeException("Duration");
            int ti = Array.IndexOf(RagePatch.ThresholdValues, s.Threshold);
            if (ti < 0) throw new ArgumentOutOfRangeException("Threshold");
            if (s.StartWrath < 0 || s.StartWrath >= StartWrathConst.Length) throw new ArgumentOutOfRangeException("StartWrath");
            if (s.Chance < 0 || s.Chance > ChanceImm.Length) throw new ArgumentOutOfRangeException("Chance");
            foreach (var x in IdSites) exe[x[0] + 3] = (byte)s.CharId;
            // Rage aus: Sperre beim Kampfstart ungleich 0. Elena-Wert ebenfalls 1, sonst wuerde Elena die Sperre auf 0 setzen.
            Buffer.BlockCopy(s.Enabled ? LockInitOn : LockInitOff, 0, exe, LockInit, 2);
            exe[ElenaImm] = (byte)(!s.Enabled || s.ElenaLock ? 1 : 0);
            Bin.W32(exe, DurationSet + 6, (uint)s.Duration);
            Bin.W32(exe, DurationCmp + 6, (uint)s.Duration);
            foreach (var o in ThresholdSites) Bin.W32(exe, o + 3, ThresholdConst[ti]);
            Bin.W32(exe, StartWrathSite + 4, StartWrathConst[s.StartWrath]);
            for (int i = 0; i < ChanceSites.Length; i++) Bin.W32(exe, ChanceSites[i] + 6, s.Chance == 0 ? ChanceOrig[i] : ChanceImm[s.Chance - 1]);
            Buffer.BlockCopy(s.MinWrathOne ? MinJneNew : MinJneOrig, 0, exe, MinStartJne, 2);
            Buffer.BlockCopy(s.MinWrathOne ? MinBodyNew : MinBodyOrig, 0, exe, MinStartBody, MinBodyOrig.Length);
            Buffer.BlockCopy(s.MinWrathOne ? MinEndNew : MinEndOrig, 0, exe, MinEnd, 5);
            if (s.MinWrathOne) Buffer.BlockCopy(CaveCode, 0, exe, Cave, CaveSize);
            else for (int i = 0; i < CaveSize; i++) exe[Cave + i] = 0xCC;
            bool bypassLock = s.Enabled && !s.ElenaLock;
            foreach (var o in LockReadSites) Bin.W32(exe, o + 2, bypassLock ? ZeroConst : LockVar);
            Buffer.BlockCopy(s.CharId == 3 ? StartOrig : StartFixed, 0, exe, StartIndex, StartOrig.Length);
            Buffer.BlockCopy(s.CharId == 3 ? EndOrig : EndFixed, 0, exe, EndIndex, EndOrig.Length);
        }

        static int LockReadState(byte[] exe, int site)   // 0 = Original, 1 = umgangen, -1 = unbekannt
        {
            if (site + 7 > exe.Length || exe[site] != 0x83 || exe[site + 1] != 0x3D || exe[site + 6] != 0) return -1;
            uint a = (uint)Bin.U32(exe, site + 2);
            return a == LockVar ? 0 : a == ZeroConst ? 1 : -1;
        }

        static int MinChunkState(byte[] exe, int i)      // 0 = Original, 1 = Mindestwut, -1 = unbekannt
        {
            if (i == 0) return Match(exe, MinStartJne, MinJneOrig) ? 0 : Match(exe, MinStartJne, MinJneNew) ? 1 : -1;
            if (i == 1) return Match(exe, MinStartBody, MinBodyOrig) ? 0 : Match(exe, MinStartBody, MinBodyNew) ? 1 : -1;
            if (!Match(exe, MinEnd + 5, MinEndTail)) return -1;
            if (Match(exe, MinEnd, MinEndOrig) && AllCC(exe, Cave, CaveSize)) return 0;
            if (Match(exe, MinEnd, MinEndNew) && Match(exe, Cave, CaveCode)) return 1;
            return -1;
        }

        static int StartWrathAt(byte[] exe)
        {
            if (!Match(exe, StartWrathSite, MovssPrefix)) return -1;
            return Array.IndexOf(StartWrathConst, (uint)Bin.U32(exe, StartWrathSite + 4));
        }

        static int ChanceIndexAt(byte[] exe, int i)   // 0 = Original, 1..3 = ChanceImm, -2 = Original oder gleicher Wert, -1 = unbekannt
        {
            int o = ChanceSites[i];
            if (!Match(exe, o, ChancePrefix)) return -1;
            uint v = (uint)Bin.U32(exe, o + 6);
            int k = Array.IndexOf(ChanceImm, v);
            if (v == ChanceOrig[i]) return k >= 0 ? -2 : 0;
            return k < 0 ? -1 : k + 1;
        }

        static int ThresholdIndexAt(byte[] exe, int site)
        {
            if (!Match(exe, site, ThresholdPrefix)) return -1;
            return Array.IndexOf(ThresholdConst, (uint)Bin.U32(exe, site + 3));
        }

        static bool ConstantsOk(byte[] exe)
        {
            for (int i = 0; i < ThresholdConst.Length; i++)
                if (!FloatAt(exe, ThresholdConst[i], RagePatch.ThresholdValues[i])) return false;
            return true;
        }
    }
}
