using System;
using System.Collections.Generic;
using System.Linq;

namespace G2Modifier.Core
{
    /// <summary>
    /// "Verwendung" (im Kampf benutzbar, eigene Attacke) fuer Ausruestung ohne eigenen Block.
    /// item.bin hat genau 128 Verwendungs-Bloecke, alle belegt, kein freies Byte. Die Datei zu vergroessern ist nicht sicher
    /// (das Spiel laedt seine Daten in feste Speicherbereiche). Frei verfuegbar sind nur die Bloecke der zwei Entwickler-Items
    /// 001 "Date 8/16" und 002 "Debug Item" (im Spiel nicht erhaeltlich).
    /// Zuweisen: Die Waffe zeigt auf den Block, das Debug-Item bekommt -1 (= keine Verwendung, wie 671 andere Items).
    /// Der Block bekommt als Startwert die Verwendung der ersten Waffe, die im Original eine hat (danach frei aenderbar).
    /// Entfernen: Zeiger und Blockinhalt wie im Original. Geschrieben wird in alle gueltigen Kopien (xls_data, sys, sys_hardmode),
    /// nur im Speicher; gespeichert wird wie immer mit Strg+S. Dateigroesse bleibt gleich.
    /// </summary>
    public static class ItemUsable
    {
        public static readonly int[] Donors = { 1, 2 };
        const int PtrOffset = 72, EqOffset = 68;

        static byte[] Orig(GameFile f) { return f.Original ?? f.Disk; }
        static int Ptr(byte[] d, int item) { return Bin.S32(d, item * Formats.ItemSize + PtrOffset); }
        static int EqPtr(byte[] d, int item) { return Bin.S32(d, item * Formats.ItemSize + EqOffset); }

        static List<GameFile> Files(Workspace ws)
        {
            var t = ws.Table("ITEM.BIN");
            return t == null ? new List<GameFile>() : t.Variants.Where(v => v.Valid).Select(v => v.Files[0]).ToList();
        }

        /// <summary>Block des Debug-Items (aus dem Original), wenn ihn gerade kein anderes Item benutzt.</summary>
        static bool Free(GameFile f, int donor)
        {
            int block = Ptr(Orig(f), donor);
            if (block <= 0) return false;
            for (int i = 0; i < Formats.ItemCount; i++) if (i != donor && Ptr(f.Data, i) == block) return false;
            return true;
        }

        /// <summary>Freie Debug-Bloecke (in allen Kopien frei).</summary>
        public static List<int> FreeDonors(Workspace ws)
        {
            var files = Files(ws);
            return Donors.Where(d => files.Count > 0 && files.All(f => Free(f, d))).ToList();
        }

        /// <summary>Debug-Item, dessen Block dieses Item gerade benutzt, sonst -1.</summary>
        public static int DonorOf(GameFile f, int item)
        {
            int p = Ptr(f.Data, item);
            foreach (var d in Donors) if (d != item && p > 0 && p == Ptr(Orig(f), d)) return d;
            return -1;
        }

        public static bool CanAssign(GameFile f, int item)
        {
            return f.Valid && Array.IndexOf(Donors, item) < 0 && EqPtr(f.Data, item) > 0 && Ptr(f.Data, item) <= 0;
        }

        /// <summary>Weist dem Item einen freien Debug-Block zu. Rueckgabe: Meldung fuer die Statuszeile.</summary>
        public static string Assign(Workspace ws, int item)
        {
            var files = Files(ws);
            if (files.Count == 0) return L.T("item.bin ist gesperrt.");
            if (!files.All(f => CanAssign(f, item))) return L.T("Dieses Item kann keine Verwendung bekommen (keine Ausrüstung oder schon vorhanden).");
            var free = FreeDonors(ws);
            if (free.Count == 0) return L.T("Kein freier Block mehr. Beide Debug-Blöcke sind vergeben.");
            int donor = free[0];
            if (files.Any(f => !Enumerable.Range(0, Formats.ItemCount).Any(i => EqPtr(Orig(f), i) > 0 && Ptr(Orig(f), i) > 0 && Array.IndexOf(Donors, i) < 0)))
                return L.T("Keine Vorlage für die Verwendung gefunden.");
            foreach (var f in files)
            {
                var o = Orig(f);
                int block = Ptr(o, donor);
                int template = Enumerable.Range(0, Formats.ItemCount).FirstOrDefault(i => EqPtr(o, i) > 0 && Ptr(o, i) > 0 && Array.IndexOf(Donors, i) < 0);
                var d = (byte[])f.Data.Clone();
                Buffer.BlockCopy(o, Ptr(o, template), d, block, Formats.Usable.Size);
                Bin.W32(d, donor * Formats.ItemSize + PtrOffset, 0xFFFFFFFF);
                Bin.W32(d, item * Formats.ItemSize + PtrOffset, (uint)block);
                f.Data = d;
            }
            Resync(ws);
            return L.F("Verwendung hinzugefügt (Block von {0}). Noch {1} frei. Noch nicht gespeichert (Strg+S).", DonorName(files[0], donor), FreeDonors(ws).Count);
        }

        /// <summary>Gibt den Block an das Debug-Item zurueck (Zeiger und Inhalt wie im Original).</summary>
        public static string Release(Workspace ws, int item)
        {
            var files = Files(ws);
            if (files.Count == 0) return L.T("item.bin ist gesperrt.");
            int donor = DonorOf(files[0], item);
            if (donor < 0 || !files.All(f => DonorOf(f, item) == donor)) return L.T("Dieses Item benutzt keinen Debug-Block.");
            foreach (var f in files)
            {
                var o = Orig(f);
                int block = Ptr(o, donor);
                var d = (byte[])f.Data.Clone();
                Buffer.BlockCopy(o, block, d, block, Formats.Usable.Size);
                Buffer.BlockCopy(o, donor * Formats.ItemSize + PtrOffset, d, donor * Formats.ItemSize + PtrOffset, 4);
                Buffer.BlockCopy(o, item * Formats.ItemSize + PtrOffset, d, item * Formats.ItemSize + PtrOffset, 4);
                f.Data = d;
            }
            Resync(ws);
            return L.F("Verwendung entfernt, Block an {0} zurückgegeben. Noch nicht gespeichert (Strg+S).", DonorName(files[0], donor));
        }

        public static string DonorName(GameFile f, int donor)
        {
            return donor.ToString("D3") + " " + FixedText.Decode(f.Data, donor * Formats.ItemSize, 18).Trim();
        }

        /// <summary>Abschnitte aller Items aller Kopien neu aus den aktuellen Zeigern bestimmen.</summary>
        public static void Resync(Workspace ws)
        {
            var t = ws.Table("ITEM.BIN");
            if (t == null) return;
            foreach (var v in t.Variants) foreach (var r in v.Records) Formats.RefreshItemSections(r);
        }
    }
}
