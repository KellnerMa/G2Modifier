using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using G2Modifier.Core;

// Selbsttest mit kuenstlichen Dateien (keine Spieldaten). Prueft Logik, nicht das Spiel.
static class SelfTest
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what) { if (ok) passes++; else { fails++; Console.WriteLine("FEHLER: " + what); } }

    static byte[] Name(string s, int len, bool rightAligned)
    {
        var b = new byte[len];
        int start = rightAligned ? len - s.Length : 0;
        for (int i = 0; i < s.Length; i++) b[start + i] = (byte)s[i];
        return b;
    }

    static void Put(byte[] d, int o, byte[] src) { Buffer.BlockCopy(src, 0, d, o, src.Length); }

    static int Main(string[] args)
    {
        string root = args[0];
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string afs = Path.Combine(root, "game", "content", "data", "afs");
        foreach (var f in new[] { "xls_data", "sys", "sys_hardmode", "enemy", "enemy_hardmode", "boss" }) Directory.CreateDirectory(Path.Combine(afs, f));
        string game = Path.Combine(root, "game");

        // ITEM.BIN: 799 Items, Item 5 hat Ausruestung, Item 6 Verwendung
        int table = Formats.ItemCount * Formats.ItemSize;
        var item = new byte[table + 0x1C + 0x20];
        for (int i = 0; i < Formats.ItemCount; i++)
        {
            int b = i * 0x50;
            Put(item, b, Name("Item" + i, 18, false));
            Put(item, b + 18, Name("Beschreibung " + i, 40, false));
            Bin.W32(item, b + 64, (uint)(i * 10));
            Bin.W32(item, b + 68, 0xFFFFFFFF);
            Bin.W32(item, b + 72, 0xFFFFFFFF);
            Bin.W32(item, b + 76, (uint)i);
        }
        Bin.W32(item, 5 * 0x50 + 68, (uint)table);
        Bin.W32(item, 6 * 0x50 + 72, (uint)(table + 0x1C));
        Bin.W16(item, table + 2, 25); // Staerke Item 5
        File.WriteAllBytes(Path.Combine(afs, "xls_data", "ITEM.BIN"), item);
        File.WriteAllBytes(Path.Combine(afs, "sys", "ITEM.BIN"), item);
        var hard = (byte[])item.Clone(); Bin.W32(hard, 3 * 0x50 + 64, 999);
        File.WriteAllBytes(Path.Combine(afs, "sys_hardmode", "ITEM.BIN"), hard);

        // MS_PARAM.BIN: Namen rechtsbuendig (wie G2DataGUI-Setter PadLeft)
        var ms = new byte[Formats.MoveCount * 108];
        for (int i = 0; i < Formats.MoveCount; i++) { ms[i * 108] = (byte)i; Put(ms, i * 108 + 2, Name("Move" + i, 18, true)); }
        File.WriteAllBytes(Path.Combine(afs, "xls_data", "MS_PARAM.BIN"), ms);

        // falsche Groesse -> muss gesperrt werden
        File.WriteAllBytes(Path.Combine(afs, "xls_data", "SK_PARAM.BIN"), new byte[1000]);

        // Gegnerdatei mit zwei Gegnern
        var en = new byte[0x500];
        Bin.W32(en, 0x34, 0x100); Bin.W32(en, 0x3C, 0x200); Bin.W32(en, 0x44, 0x180); Bin.W32(en, 0x4C, 0x300);
        Put(en, 0x100, Name("Wolf", 18, false)); Bin.W32(en, 0x100 + 24, 500); Bin.W32(en, 0x100 + 72, 40);
        Put(en, 0x180, Name("Wolf B", 18, false)); Bin.W32(en, 0x180 + 24, 30000);
        for (int m = 0; m < 5; m++) { Put(en, 0x200 + m * 60, Name("Biss" + m, 18, false)); Bin.W16(en, 0x200 + m * 60 + 24, 100 + m); }
        Directory.CreateDirectory(Path.Combine(afs, "enemy", "sub"));
        File.WriteAllBytes(Path.Combine(afs, "enemy", "sub", "d01_0.dat"), en);
        File.WriteAllBytes(Path.Combine(afs, "enemy", "d01_1.dat"), new byte[10]); // darf nicht geladen werden
        var broken = new byte[0x60]; Bin.W32(broken, 0x34, 0x5000);
        File.WriteAllBytes(Path.Combine(afs, "boss", "z99_0.dat"), broken);

        // ---- Laden ----
        var ws = Workspace.Open(game);
        Check(ws.Edition.StartsWith("HD"), "HD erkannt");
        var it = ws.Table("ITEM.BIN");
        Check(it != null && it.Variants.Count == 3, "ITEM.BIN in 3 Kopien");
        Check(it.Variants.All(v => v.Valid), "ITEM.BIN gültig");
        Check(it.Variants[0].Records.Count == 799, "799 Items");
        Check(it.Variants[0].Records[5].Sections.Count == 2 && it.Variants[0].Records[5].Sections[1].Title == "Ausrüstung", "Item 5 Ausrüstung");
        Check(it.Variants[0].Records[6].Sections[1].Title == "Verwendung", "Item 6 Verwendung");
        // Reihenfolge seit 0.13.3: sys, sys_hardmode, xls_data (verglichen wird mit sys)
        Check(it.Variants[0].Name == "sys", "sys ist erste Kopie");
        Check(it.Variants.First(v => v.Name == "xls_data").Files[0].Notes.Any(n => n.Contains("identisch")), "xls_data identisch mit sys erkannt");
        Check(it.Variants.First(v => v.Name == "sys_hardmode").Files[0].Notes.Any(n => n.Contains("weicht")), "sys_hardmode abweichend erkannt");
        var sk = ws.Table("SK_PARAM.BIN");
        Check(sk != null && !sk.Variants[0].Valid && sk.Variants[0].Records.Count == 0, "SK_PARAM falsche Größe gesperrt");
        var enemy = ws.Table("ENEMY");
        Check(enemy.Variants.Count == 1 && enemy.Variants[0].Records.Count == 2, "2 Gegner aus d01_0.dat, d01_1 ignoriert");
        var wolf = enemy.Variants[0].Records[0];
        Check(wolf.Label.StartsWith("Wolf"), "Gegnername");
        Check(wolf.Sections.Count == 7, "Werte+KI+5 Angriffe");
        var boss = ws.Table("BOSS");
        Check(boss.Variants[0].Files[0].Valid == false && boss.Variants[0].Records.Count == 0, "kaputte Bossdatei gesperrt");

        // ---- Backup-Ort ----
        Check(BackupService.CheckLocation(game, Path.Combine(game, "bk")) != null, "Backup im Spielordner abgelehnt");
        Check(BackupService.CheckLocation(game, game) != null, "Backup = Spielordner abgelehnt");
        Check(BackupService.CheckLocation(game, root) != null, "Spielordner im Backup abgelehnt");
        string bkRoot = Path.Combine(root, "backup");
        Directory.CreateDirectory(bkRoot);
        Check(BackupService.CheckLocation(game, bkRoot) == null, "gültiger Backup-Ort");

        // ---- Speichern ohne Backup verboten ----
        var itemFile = it.Variants[0].Files[0];
        var priceField = Formats.Item.Get("Stats.Price");
        Check(priceField.TryWrite(itemFile.Data, 3 * 0x50 + 64, "12345") == null, "Preis schreiben");
        var bkTmp = new BackupService(game, bkRoot);
        var rep0 = bkTmp.SaveDirty(ws);
        Check(rep0.Any(r => r.Contains("kein geprüftes Backup")), "Ohne Backup kein Schreiben");
        Check(Bin.Equal(File.ReadAllBytes(itemFile.FullPath), item), "Datei unverändert ohne Backup");

        // ---- Backup anlegen, speichern ----
        var bk = new BackupService(game, bkRoot);
        int created = bk.EnsureBackups(ws.AllFiles, null);
        Check(created == ws.AllFiles.Count, "Backups angelegt: " + created);
        Check(bk.Problems.Count == 0, "keine Backup-Probleme");
        Check(itemFile.Original != null && Bin.Equal(itemFile.Original, item), "Original = Stand vor Änderung");
        var rep = bk.SaveDirty(ws);
        var onDisk = File.ReadAllBytes(itemFile.FullPath);
        var diffs = GameFile.DiffRanges(item, onDisk);
        Check(diffs.Count == 1 && diffs[0][0] == 3 * 0x50 + 64, "nur Preisbytes geändert");
        Check(Bin.S32(onDisk, 3 * 0x50 + 64) == 12345, "Preis auf Platte");
        Check(onDisk.Length == item.Length, "Dateigröße gleich");
        Check(!File.Exists(itemFile.FullPath + ".g2m-tmp"), "Temp-Datei entfernt");
        Check(File.ReadAllText(bk.LogPath).Contains("ITEM.BIN"), "Protokoll geschrieben");

        // zweites Oeffnen: Backup bleibt Original (wird nicht mit gemoddetem Stand ueberschrieben)
        var ws2 = Workspace.Open(game);
        var bk2 = new BackupService(game, bkRoot);
        int created2 = bk2.EnsureBackups(ws2.AllFiles, null);
        Check(created2 == 0, "kein erneutes Backup");
        var f2 = ws2.Table("ITEM.BIN").Variants[0].Files[0];
        Check(Bin.Equal(f2.Original, item) && f2.DiskState == "verändert", "Backup bleibt Original, Status verändert");

        // Backup manipuliert -> gesperrt
        string bp = Path.Combine(bk2.Dir, f2.RelPath.Replace('\\', Path.DirectorySeparatorChar));
        var save = File.ReadAllBytes(bp); var bad = (byte[])save.Clone(); bad[0] ^= 1; File.WriteAllBytes(bp, bad);
        var ws3 = Workspace.Open(game); var bk3 = new BackupService(game, bkRoot); bk3.EnsureBackups(ws3.AllFiles, null);
        Check(bk3.Problems.Count == 1, "beschädigtes Backup erkannt");
        var f3 = ws3.Table("ITEM.BIN").Variants[0].Files[0];
        priceField.TryWrite(f3.Data, 64, "7");
        var rep3 = bk3.SaveDirty(ws3);
        Check(rep3.Any(r => r.StartsWith("NICHT")), "Schreiben bei beschädigtem Backup verweigert");
        File.WriteAllBytes(bp, save);

        // Wiederherstellen
        var ws4 = Workspace.Open(game); var bk4 = new BackupService(game, bkRoot); bk4.EnsureBackups(ws4.AllFiles, null);
        var f4 = ws4.Table("ITEM.BIN").Variants[0].Files[0];
        Check(bk4.Restore(f4) == null && Bin.Equal(File.ReadAllBytes(f4.FullPath), item), "Wiederherstellung bytegleich");

        // ---- Texte: Ausrichtung und Fuellbyte bleiben ----
        var mv = ws4.Table("MS_PARAM.BIN").Variants[0];
        var nameF = Formats.Move.Get("Name");
        var mf = mv.Files[0];
        Check(nameF.Format(mf.Data, 2) == "Move0", "rechtsbündiger Name gelesen");
        Check(nameF.TryWrite(mf.Data, 2, "Feuerball") == null, "Name geschrieben");
        Check(mf.Data[2 + 17] == (byte)'l' && mf.Data[2] == 0, "rechtsbündig mit 0x00 erhalten");
        Check(nameF.TryWrite(mf.Data, 2, "Ä") != null, "Umlaut abgelehnt");
        Check(nameF.TryWrite(mf.Data, 2, new string('x', 19)) != null, "zu langer Name abgelehnt");
        var iname = Formats.Item.Get("Name");
        var itf = ws4.Table("ITEM.BIN").Variants[0].Files[0];
        iname.TryWrite(itf.Data, 0, "Schwert");
        Check(itf.Data[0] == (byte)'S' && itf.Data[7] == 0 && itf.Data[17] == 0, "linksbündig mit 0x00 erhalten");

        // ---- Zahlenbereiche ----
        var hp = Formats.EnemyStats.Get("Health");
        var str = Formats.Equipment.Get("Strength");
        Check(str.TryWrite(new byte[2], 0, "40000") != null, "s16 Überlauf abgelehnt");
        Check(str.TryWrite(new byte[2], 0, "-5") == null, "s16 negativ erlaubt");
        Check(Formats.Item.Get("Stats.EntryType").TryWrite(new byte[1], 0, "256") != null, "u8 Überlauf abgelehnt");

        // ---- Skalierung ----
        var ws5 = Workspace.Open(game);
        var recs = ws5.Table("ENEMY").Variants[0].Records;
        var res = Batch.Scale(recs, new List<Batch.ScaleRule> {
            new Batch.ScaleRule(Formats.EnemyStats, "Health", 2.0),
            new Batch.ScaleRule(Formats.EnemyMove, "Stats.Strength", 1.5) });
        var ef = recs[0].File;
        Check(Bin.S32(ef.Data, 0x100 + 24) == 1000, "HP x2");
        Check(Bin.S32(ef.Data, 0x180 + 24) == 60000, "HP zweiter Gegner x2");
        Check(Bin.S16(ef.Data, 0x200 + 24) == 150, "Angriff x1.5");
        Check(Bin.S32(ef.Data, 0x100 + 72) == 40, "EXP unverändert");

        Console.WriteLine("Selbsttest: " + passes + " ok, " + fails + " Fehler");
        return fails == 0 ? 0 : 1;
    }
}
