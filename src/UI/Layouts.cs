using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using G2Modifier.Core;

namespace G2Modifier.UI
{
    /// <summary>
    /// Welche Felder je Tabelle vorne stehen. Alles, was hier nicht genannt wird, landet automatisch unter "Experte".
    /// Feldnamen (SourceName) wie in Formats.cs, also wie in G2DataGUI.
    /// </summary>
    public static class Layouts
    {
        static readonly string[] Elem5 = { "Fire", "Wind", "Earth", "Lightning", "Blizzard" };
        static readonly string[] Elem8 = { "Fire", "Wind", "Earth", "Lightning", "Blizzard", "Water", "Explosion", "Forest" };

        // ------------------------------------------------------------ Namen, Kurzinfo, Symbol fuer die Liste

        /// <summary>Name so, wie er gerade in den Daten steht (nach Umbenennen sofort aktuell).</summary>
        public static string RawName(Record r)
        {
            var d = r.File.Data; int b = r.Sections[0].Base;
            switch (r.File.FormatKey)
            {
                case "ITEM.BIN": case "SK_PARAM.BIN": case "ENEMY": return FixedText.Decode(d, b, 18);
                case "MS_PARAM.BIN": return FixedText.Decode(d, b + 2, 18);
            }
            string l = r.Label ?? "";
            int i = 0;
            while (i < l.Length && char.IsDigit(l[i])) i++;
            return L.T(l.Substring(i).Trim());
        }

        public static string Name(Record r)
        {
            string n = RawName(r);
            return n.Length == 0 ? L.T("(ohne Namen)") : n;
        }

        public static int CharOf(Record r)
        {
            switch (r.File.FormatKey)
            {
                case "PC_INIT.BIN": case "TB_LVUP.BIN": return r.Index < 7 ? r.Index + 1 : 1;
                case "TB_SPCL.BIN": return r.Index == 0 ? 0 : r.Index <= 7 ? r.Index : 1;
            }
            return 0;
        }

        public static Bitmap ListIcon(Record r)
        {
            int c = CharOf(r);
            if (c > 0) return GameArt.Portrait(c, 34);
            if (r.File.FormatKey == "MS_PARAM.BIN") return GameArt.MoveIcon(r.File.Data[r.Sections[0].Base + 1]);
            return null;
        }

        static int Used(Record r, int slots, int stride)
        {
            int n = 0, b = r.Sections[0].Base;
            for (int i = 0; i < slots; i++) if (r.File.Data[b + i * stride] != 0) n++;
            return n;
        }

        static string ItemName(Workspace ws, long index)
        {
            var pv = ws.PrimaryVariant("ITEM.BIN");
            if (pv == null || index < 0 || index >= pv.Records.Count) return "#" + index;
            if (index == 0) return L.T("(leer)");
            return Name(pv.Records[(int)index]);
        }

        public static string Summary(Workspace ws, Record r)
        {
            var d = r.File.Data; int b = r.Sections[0].Base;
            switch (r.File.FormatKey)
            {
                case "ITEM.BIN":
                {
                    int t = d[b + 58];
                    string type = t < G2DataGuiLabels.ItemEntries.Length ? L.T(G2DataGuiLabels.ItemEntries[t]) : L.T("Typ") + " " + t;
                    return type + "  ·  " + L.F("{0} G", Bin.S32(d, b + 64));
                }
                case "MS_PARAM.BIN": return L.F("Kosten {0}", Bin.S16(d, b + 20)) + "  ·  " + FixedText.Decode(d, b + 68, 40);
                case "SK_PARAM.BIN": return FixedText.Decode(d, b + 64, 40);
                case "TB_MAGIC.BIN": return L.F("{0} Zauber", Used(r, 18, 4));
                case "TB_SKILL.BIN": return L.F("{0} Skills", Used(r, 6, 4));
                case "TB_SPCL.BIN": return L.F("{0} Techniken", Used(r, 6, 4));
                case "PC_INIT.BIN": return L.C(L.T("Waffe")) + ItemName(ws, Bin.S16(d, b + 4));
                case "TB_LVUP.BIN":
                {
                    int hp = 0, mp = 0;
                    foreach (var s in r.Sections) { hp += Bin.S16(d, s.Base + 4); mp += Bin.S16(d, s.Base + 6); }
                    return L.F("Bis Stufe 99: +{0} HP, +{1} MP", hp, mp);
                }
                case "ENEMY":
                    return L.F("Stufe {0}  ·  HP {1}", Bin.S16(d, b + 22), Bin.S32(d, b + 24)) + "  ·  " + Path.GetFileName(r.File.FullPath) + (r.Index == 1 ? " #2" : "");
            }
            return "";
        }

        static int Si(Record r, RecordDef def)
        {
            for (int i = 0; i < r.Sections.Count; i++) if (r.Sections[i].Def == def) return i;
            return -1;
        }

        static Bitmap El(int i) { return GameArt.Element(i); }

        // ------------------------------------------------------------ Aufbau

        public static void Build(RecordView v, Record r)
        {
            switch (r.File.FormatKey)
            {
                case "ITEM.BIN": Item(v, r); break;
                case "MS_PARAM.BIN": Move(v, r); break;
                case "SK_PARAM.BIN": Skill(v, r); break;
                case "TB_MAGIC.BIN": Slots(v, r, 18, "Spell{0}.SpellOffset", "Zauber", RefKind.Move, "Spell{0}.StartingLevel", "Startstufe", "Spell{0}.RequiredLevel", "Benötigte Stufe", "Zauber"); break;
                case "TB_SKILL.BIN": Slots(v, r, 6, "Skill{0}.SkillOffset", "Skill", RefKind.Skill, "Skill{0}.StartingLevel", "Startstufe", "Skill{0}.RequiredLevel", "Benötigte Stufe", "Skills"); break;
                case "TB_SPCL.BIN": Slots(v, r, 6, "Special{0}.MoveOffset", "Technik", RefKind.Move, "Special{0}.StartingLevel", "Startstufe", "Special{0}.StoryFlag", "Story-Flag", "Spezialtechniken"); break;
                case "PC_INIT.BIN": PcInit(v, r); break;
                case "TB_LVUP.BIN": Levelup(v, r); break;
                case "ENEMY": Enemy(v, r); break;
                default:
                    v.Header(r.File.RelPath, null, () => Name(r));
                    v.ExpertCard(Enumerable.Range(0, r.Sections.Count));
                    break;
            }
        }

        static void Item(RecordView v, Record r)
        {
            Formats.RefreshItemSections(r);   // Zeiger koennen sich geaendert haben (Verwendung zugewiesen, verworfen, wiederhergestellt)
            int t = r.File.Data[r.Sections[0].Base + 58];
            string type = t < G2DataGuiLabels.ItemEntries.Length ? L.T(G2DataGuiLabels.ItemEntries[t]) : "";
            v.Header(type, null, () => Name(r));

            var c = v.NewCard(L.T("Allgemein"));
            c.Field(0, "Name", 240);
            c.Field(0, "Description", 440);
            c.Break();
            c.Field(0, "Stats.Price", 140);

            int eq = Si(r, Formats.Equipment);
            if (eq >= 0)
            {
                c = v.NewCard(L.T("Ausrüstung"));
                c.Field(eq, "CharacterBitflag");
                c.Break();
                foreach (var n in new[] { "Strength", "Vitality", "Action", "Movement" }) c.Field(eq, n, 130);
                c.Break();
                c.Field(eq, "EffectiveOn", 220);
                c.Field(eq, "Special", 300);

                c = v.NewCard(L.T("Elemente"));
                for (int i = 0; i < 5; i++) c.Field(eq, Elem5[i] + "Affinity", 160, El(i));
                c.Break();
                for (int i = 0; i < 8; i++) c.Field(eq, "Increase" + Elem8[i] + "Percent", 160, El(i));

                c = v.NewCard(L.T("Zustandsschutz"));
                c.Field(eq, "AilmentsBitflag", 0, null, L.T("Schützt vor"));
                c.Break();
                c.Field(eq, "AilmentsChance", 140);
            }

            int us = Si(r, Formats.Usable);
            if (us >= 0)
            {
                c = v.NewCard(L.T("Verwendung"));
                c.Field(us, "TargetType", 220);
                c.Field(us, "TargetEffect", 220);
                c.Break();
                foreach (var n in new[] { "Power", "Range", "CastTime", "RecoveryTime", "BreakChance" }) c.Field(us, n, 130);
                c.Break();
                c.Field(us, "Element", 180);
                c.Field(us, "ElementStrength", 130);
                c.Break();
                c.Field(us, "AilmentsBitflag");
                c.Break();
                c.Field(us, "AilmentsChance", 130);
                foreach (var n in new[] { "AttackModifier", "DefenseModifier", "ActionModifier", "MovementModifier" }) c.Field(us, n, 130);
            }
            UsableSlot(v, r, eq, us);
            v.ExpertCard(Enumerable.Range(0, r.Sections.Count));
        }

        /// <summary>Ausruestung ohne Verwendung: freien Debug-Block zuweisen. Mit zugewiesenem Debug-Block: zurueckgeben.</summary>
        static void UsableSlot(RecordView v, Record r, int eq, int us)
        {
            if (eq < 0) return;
            int donor = us >= 0 ? ItemUsable.DonorOf(r.File, r.Index) : -1;
            if (us >= 0 && donor < 0) return;   // eigene Verwendung aus dem Original
            var c = v.NewCard(L.T("Im Kampf benutzbar machen"));
            Action<Func<Workspace, int, string>> run = act => v.Defer(() => { v.FlushAll(); v.Status(act(v.Ws, r.Index)); v.NotifyChanged(); v.Rebuild(); });
            if (us < 0)
            {
                var free = ItemUsable.FreeDonors(v.Ws);
                c.Note(L.T("Nur Ausrüstung mit eigenem Verwendungs-Block kann im Kampf benutzt werden (eigene Attacke). item.bin hat keinen freien Platz. Frei sind nur die Blöcke der zwei Entwickler-Items 001 \"Date 8/16\" und 002 \"Debug Item\", die man im Spiel nicht bekommt.") +
                       " " + L.F("Noch frei: {0} von 2.", free.Count));
                var b = Theme.Primary(L.T("Verwendung hinzufügen"));
                b.Enabled = free.Count > 0 && ItemUsable.CanAssign(r.File, r.Index);
                b.Click += (s, e) => run(ItemUsable.Assign);
                c.Add(b);
            }
            else
            {
                c.Note(L.F("Diese Verwendung benutzt den Block von {0}. Startwert war die Verwendung der ersten Waffe mit eigener Verwendung, oben frei änderbar.", ItemUsable.DonorName(r.File, donor)));
                var b = Theme.Secondary(L.T("Verwendung wieder entfernen"));
                b.Enabled = r.File.Valid;
                b.Click += (s, e) => run(ItemUsable.Release);
                c.Add(b);
            }
        }

        static void Move(RecordView v, Record r)
        {
            int b = r.Sections[0].Base;
            v.Header(FixedText.Decode(r.File.Data, b + 68, 40), GameArt.MoveIcon(r.File.Data[b + 1]), () => Name(r));
            var c = v.NewCard(L.T("Grundwerte"));
            c.Field(0, "Name", 240);
            c.Field(0, "Description", 440);
            c.Break();
            foreach (var n in new[] { "Stats.Cost", "Stats.Power", "Stats.Strength", "Stats.Range" }) c.Field(0, n, 130);
            c.Break();
            foreach (var n in new[] { "Stats.Cast1Time", "Stats.Cast5Time", "Stats.RecoveryTime" }) c.Field(0, n, 150);
            c.Break();
            c.Field(0, "Stats.Element", 180);
            c.Field(0, "Stats.ElementStrength", 130);
            c.Field(0, "Stats.TargetType", 220);

            c = v.NewCard(L.T("Lernen (Münzkosten)"));
            for (int i = 1; i <= 5; i++) c.Field(0, "Stats.CoinCost" + i, 140);
            c.Field(0, "Stats.Multiplier", 140);

            c = v.NewCard(L.T("Zustände und Werte"));
            c.Field(0, "Stats.AilmentsBitflag");
            c.Break();
            c.Field(0, "Stats.AilmentsChance", 130);
            foreach (var n in new[] { "Stats.AttackModifier", "Stats.DefenseModifier", "Stats.ActionModifier", "Stats.MovementModifier" }) c.Field(0, n, 130);
            c.Field(0, "Stats.Knockdown", 170);
            v.ExpertCard(new[] { 0 });
        }

        static void Skill(RecordView v, Record r)
        {
            v.Header(FixedText.Decode(r.File.Data, r.Sections[0].Base + 64, 40), null, () => Name(r));
            var c = v.NewCard(L.T("Allgemein"));
            c.Field(0, "Name", 240);
            c.Field(0, "Description", 440);
            c.Break();
            c.Field(0, "Stats.CostType1", 220);
            c.Field(0, "Stats.CostType2", 220);

            c = v.NewCard(L.T("Boni auf Werte"));
            foreach (var n in new[] { "HP", "MP", "SP", "Strength", "Vitality", "Action", "Movement", "Magic", "Mentality" }) c.Field(0, "Stats.Base" + n, 130);

            c = v.NewCard(L.T("Element-Boni"));
            for (int i = 0; i < 8; i++) c.Field(0, "Stats.Increase" + Elem8[i] + "Percent", 160, El(i));

            c = v.NewCard(L.T("Lernen (Münzkosten)"));
            for (int i = 1; i <= 5; i++) c.Field(0, "Stats.CoinCost" + i, 140);
            c.Field(0, "Stats.Multiplier", 140);
            v.ExpertCard(new[] { 0 });
        }

        /// <summary>Manaeier, Skillbücher, Spezialtechniken: feste Plätze mit Auswahl per Name.</summary>
        static void Slots(RecordView v, Record r, int count, string refField, string refCaption, RefKind kind, string f2, string c2, string f3, string c3, string title)
        {
            int ch = CharOf(r);
            int used = Used(r, count, 4);
            v.Header(L.F("{0} von {1} Plätzen belegt", used, count), ch > 0 ? GameArt.Portrait(ch, 64) : null, () => Name(r));
            var c = v.NewCard(L.T(title));
            var choices = v.RefChoices(kind, null);
            c.Add(Head("", 30, 10));
            c.Add(Head(L.T(refCaption), 280, 14));
            c.Add(Head(L.T(c2), 120, 14));
            c.Add(Head(L.T(c3), 140, 0));
            c.Break();
            for (int i = 1; i <= count; i++)
            {
                c.Add(new System.Windows.Forms.Label { Text = i + ".", AutoSize = false, Size = new Size(30, 26), TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = Theme.BodyBold, Margin = new System.Windows.Forms.Padding(0, 0, 10, 0) });
                c.Field(0, string.Format(refField, i), 280, null, "", choices);
                c.Field(0, string.Format(f2, i), 120, null, "");
                c.Field(0, string.Format(f3, i), 140, null, "");
                c.Break();
            }
            v.ExpertCard(new[] { 0 });
        }

        static System.Windows.Forms.Label Head(string text, int width, int gap)
        {
            return new System.Windows.Forms.Label { Text = text, AutoSize = false, Size = new Size(width, 18), ForeColor = Theme.Muted, Font = Theme.Small, AutoEllipsis = true, Margin = new System.Windows.Forms.Padding(0, 0, gap, 2) };
        }

        static void PcInit(RecordView v, Record r)
        {
            v.Header(L.T("Werte zu Spielbeginn bzw. beim Beitritt"), GameArt.Portrait(CharOf(r), 64), () => Name(r));
            var c = v.NewCard(L.T("Ausrüstung zum Start"));
            string[] eq = { "Weapon", "Armor", "Headgear", "Footwear", "Accessory", "Manaegg" };
            string[] cap = { "Waffe", "Rüstung", "Kopf", "Füße", "Accessoire", "Manaei" };
            for (int i = 0; i < 6; i++)
            {
                int type = 4 + i;   // ItemEntries: 4 Weapon ... 9 Mana Egg
                c.Field(0, eq[i], 280, null, L.T(cap[i]), v.RefChoices(RefKind.Item, x => x.File.Data[x.Sections[0].Base + 58] == type));
            }
            // Grundwerte: Stufe 1 der Figur in tb_lvup.bin (gleicher Ordner, gleiche Nummer). Stufe 1 enthaelt die vollen
            // Grundwerte (z. B. Ryudo HP 300), ab Stufe 2 folgen kleine Zuwaechse.
            var lv = LevelRecord(v, r);
            if (lv != null)
            {
                var lvWriter = new FieldWriter(v.Ws, v.Ws.Table("TB_LVUP.BIN"), () => v.Writer.SyncOn);
                c = v.NewCard(L.T("Grundwerte auf Stufe 1"));
                c.Note(L.T("Das ist Stufe 1 aus Level-ups (tb_lvup.bin). Eine Änderung hier ändert auch die Seite Level-ups."));
                foreach (var n in LvFields)
                {
                    var f = Formats.Levelup.Get("Increase" + n);
                    if (n == "SkillSlot") continue;
                    c.Add(v.ForeignField(lv, lvWriter, 0, f.SourceName, 120, L.T(f.Label).TrimStart('+', ' ')));
                }

            }

            // Bonus der Startausrüstung (Summe der Ausrüstungswerte der fünf Teile, ohne Manaei)
            c = v.NewCard(L.T("Bonus der Startausrüstung"));
            foreach (var es in new[] { "Strength", "Vitality", "Action", "Movement" })
            {
                var ef = Formats.Equipment.Get(es);
                string stat = es;
                var tile = new System.Windows.Forms.Label { AutoSize = false, Size = new Size(130, 44), Font = Theme.Body, ForeColor = Theme.Text, Margin = new System.Windows.Forms.Padding(0, 0, 10, 6) };
                v.Live(tile, () => L.T(ef.Label) + "\n+" + EquipBonus(v.Ws, r, stat));
                c.Add(tile);
            }

            c = v.NewCard(L.T("Grundwerte"));
            c.Field(0, "Experience", 150);
            c.Field(0, "Stamina", 130);
            c = v.NewCard(L.T("Kampf"));
            foreach (var n in new[] { "IpStun", "IpCancelStun", "StatusRecoveryTime", "KnockbackResist" }) c.Field(0, n, 170);
            c.Break();
            foreach (var n in new[] { "ComboSpRegen", "CritSpRegen", "HitSpRegen" }) c.Field(0, n, 170);
            c.Break();
            foreach (var n in new[] { "EvasionStillRate", "EvasionMovingRate" }) c.Field(0, n, 190);
            v.ExpertCard(new[] { 0 });
        }

        /// <summary>Gleiche Figur in tb_lvup.bin, aus derselben Kopie (xls_data, sys ...).</summary>
        static Record LevelRecord(RecordView v, Record r)
        {
            var pc = v.Ws.Table("PC_INIT.BIN");
            var lt = v.Ws.Table("TB_LVUP.BIN");
            if (pc == null || lt == null) return null;
            var pv = pc.Variants.FirstOrDefault(x => x.Records.Contains(r));
            var lvv = pv == null ? null : lt.Variants.FirstOrDefault(x => x.Name == pv.Name && x.Valid);
            if (lvv == null || r.Index >= lvv.Records.Count) return null;
            return lvv.Records[r.Index];
        }

        /// <summary>Summe eines Ausrüstungswerts (Stärke, Vitalität, Aktion, Bewegung) über die fünf Startteile.</summary>
        static long EquipBonus(Workspace ws, Record pc, string stat)
        {
            var items = ws.PrimaryVariant("ITEM.BIN");
            if (items == null) return 0;
            long sum = 0;
            for (int i = 0; i < 5; i++)
            {
                int idx = Bin.S16(pc.File.Data, pc.Sections[0].Base + 4 + 2 * i);
                if (idx <= 0 || idx >= items.Records.Count) continue;
                var it = items.Records[idx];
                var eq = it.Sections.FirstOrDefault(x => x.Def == Formats.Equipment);
                if (eq == null) continue;
                var f = Formats.Equipment.Get(stat);
                sum += f.ReadNumber(it.File.Data, eq.Base + f.Offset);
            }
            return sum;
        }

        static readonly string[] LvFields = { "HP", "MP", "SP", "Strength", "Vitality", "Action", "Movement", "Magic", "Mentality", "SkillSlot" };

        static void Levelup(RecordView v, Record r)
        {
            v.Header(L.T("Zuwachs je Stufe. Mit „Kurve und Multiplikator“ änderst du viele Stufen auf einmal."), GameArt.Portrait(CharOf(r), 64), () => Name(r));
            var sum = v.NewCard(L.T("Summe über alle Stufen"));
            foreach (var n in LvFields)
            {
                var f = Formats.Levelup.Get("Increase" + n);
                var tile = new System.Windows.Forms.Label { AutoSize = false, Size = new Size(120, 44), Font = Theme.Body, ForeColor = Theme.Text, Margin = new System.Windows.Forms.Padding(0, 0, 14, 6) };
                v.Live(tile, () =>
                {
                    long s = 0;
                    foreach (var sec in r.Sections) s += f.ReadNumber(r.File.Data, sec.Base + f.Offset);
                    return L.T(f.Label).TrimStart('+', ' ') + "\n+" + s;
                });
                sum.Add(tile);
            }
            var names = Enumerable.Range(1, r.Sections.Count).Select(i => L.F("Stufe {0}", i)).ToList();
            v.Picker("level", L.T("Einzelne Stufe"), L.T("Stufe:"), names, (c, i) =>
            {
                c.Field(i, "Experience", 150);
                c.Break();
                foreach (var n in LvFields) c.Field(i, "Increase" + n, 120);
            });
        }

        static readonly string[] Basics = { "Level", "Health", "MP", "SP", "Vitality", "Agility", "Speed", "Mentality", "Stamina" };
        static readonly string[] Rewards = { "Experience", "GoldCoins", "SkillCoins", "MagicCoins" };
        static readonly string[] AttackFields = { "Stats.MP", "Stats.SP", "Stats.Strength", "Stats.Power", "Stats.Range", "Stats.Accuracy", "Stats.CastTime", "Stats.RecoveryTime", "Stats.Element", "Stats.ElementStrength", "Stats.TargetType", "Stats.TargetEffect" };

        static void Enemy(RecordView v, Record r)
        {
            var st = r.Sections[0];
            string types = L.C(L.T("Typ")) + st.Def.Get("Type1").Meaning(r.File.Data, st.Base + 20, v.Ws) + " / " + st.Def.Get("Type2").Meaning(r.File.Data, st.Base + 21, v.Ws);
            v.Header(types, null, () => Name(r));

            var c = v.NewCard(L.T("Grundwerte"));
            c.Field(0, "Name", 240);
            c.Break();
            foreach (var n in Basics) c.Field(0, n, 130);

            c = v.NewCard(L.T("Belohnungen"));
            foreach (var n in Rewards) c.Field(0, n, 130);
            c.Break();
            var items = v.RefChoices(RefKind.Item, null);
            c.Field(0, "Item1Offset", 260, null, null, items); c.Field(0, "Item1Chance", 120);
            c.Field(0, "Item2Offset", 260, null, null, items); c.Field(0, "Item2Chance", 120);

            c = v.NewCard(L.T("Resistenzen und Schutz"));
            for (int i = 0; i < 5; i++) c.Field(0, Elem5[i] + "Resist", 150, El(i));
            c.Break();
            c.Field(0, "AilmentsBitflag");
            c.Break();
            foreach (var n in new[] { "KnockbackResist", "StatusRecoveryTime", "EvasionStillRate", "EvasionMovingRate" }) c.Field(0, n, 170);
            c.Field(0, "NoRunFlag", 190);

            var attacks = Enumerable.Range(2, Math.Max(0, r.Sections.Count - 2)).ToList();
            Func<int, string> atkName = i => (i + 1) + "  ·  " + FixedText.Decode(r.File.Data, r.Sections[attacks[i]].Base, 18);
            v.Picker("attack", L.T("Angriffe"), L.T("Angriff wählen:"), attacks.Select((x, i) => atkName(i)).ToList(), (card, i) =>
            {
                int si = attacks[i];
                card.Field(si, "Name", 240);
                card.Break();
                foreach (var n in AttackFields) card.Field(si, n, Formats.EnemyMove.Get(n).Labels != null ? 210 : 130);
                card.Break();
                v.Expert(card, new[] { si });
            }, atkName);
            v.ExpertCard(new[] { 0, 1 });
        }
    }
}
