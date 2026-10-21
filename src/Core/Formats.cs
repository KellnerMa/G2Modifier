using System;
using System.Collections.Generic;
using System.IO;

namespace G2Modifier.Core
{
    /// <summary>
    /// Alle Datenstrukturen dieses Tools. Quelle der Byte-Layouts:
    /// G2DataGUI von krogenth (MIT-Lizenz), https://github.com/krogenth/G2DataGUI
    /// Ordner src/G2DataGUI.Common/Data. Feldnamen in SourceName entsprechen den Eigenschaften dort.
    /// Nichts hier ist geraten: Felder, die dort "Unknown" heissen, heissen hier "Unbekannt".
    /// </summary>
    public static class Formats
    {
        static readonly string[] Ailments = { "Gift", "Schlaf", "Lähmung", "Verwirrung", "Seuche", "Magieblock", "Bewegungsblock", "Tod" };
        static readonly string[] CharFlags = { "Ryudo", "Elena", "Millenia", "Roan", "Tio", "Mareg", "Prinz Roan" };
        static readonly string[] Elems = { "Feuer", "Wind", "Erde", "Blitz", "Blizzard" };
        static readonly string[] ElemsIncrease = { "Feuer", "Wind", "Erde", "Blitz", "Blizzard", "Wasser", "Explosion", "Wald" };

        public static readonly RecordDef Item, Equipment, Usable, Move, Skill, Manaegg, Skillbook, SpecialSet, PcInit, Levelup, EnemyStats, EnemyAI, EnemyMove;

        // Konstanten aus G2DataGUI
        public const int ItemCount = 0x31F;          // Items.NumberOfItems
        public const int ItemSize = 0x50;            // ItemData._itemObjectByteSize
        public const int MoveCount = 0x80;           // Moves.NumberOfMoves
        public const int SkillCount = 0x80;          // Skills.NumberOfSkills
        public const int ManaeggCount = 0x0B;        // Manaeggs.NumberOfManaeggs
        public const int SkillbookCount = 0x0A;      // Skillbooks.NumberOfSkillbooks
        public const int SpecialSetCount = 0x0E;     // SpecialSets.NumberOfSpecialSets
        public const int LevelsPerCharacter = 99;    // CharacterLevelups.LevelupsPerCharacter
        public const int EnemyStatsPtr1 = 0x34, EnemyStatsPtr2 = 0x44, EnemyMovesPtr1 = 0x3C, EnemyMovesPtr2 = 0x4C;
        public const int EnemyMovesPerSet = 5;

        static Formats()
        {
            // ---------- ITEM.BIN: Item.cs, ItemStats.cs, ItemOffsets.cs ----------
            Item = new RecordDef("Item", ItemSize);
            Item.Text("Name", "Name", 0, 18);
            Item.Text("Beschreibung", "Description", 18, 40);
            Item.Add("Eintragstyp", "Stats.EntryType", 58, FieldType.U8).Labels = G2DataGuiLabels.ItemEntries;
            Item.Unknown("Stats.Unknown1", 59, FieldType.U8);
            Item.Unknown("Stats.Unknown2", 60, FieldType.U8);
            Item.Unknown("Stats.Unknown3", 61, FieldType.U8);
            Item.Add("Symbol", "Stats.Icon", 62, FieldType.U8);
            Item.Unknown("Stats.Unknown4", 63, FieldType.U8);
            Item.Add("Preis", "Stats.Price", 64, FieldType.S32);
            Struct(Item.Add("Zeiger Ausrüstung", "EquipmentOffset", 68, FieldType.S32));
            Struct(Item.Add("Zeiger Verwendung", "UsableOffset", 72, FieldType.S32));
            Struct(Item.Add("ID", "ID", 76, FieldType.U32));

            // Equipment.cs (0x1C)
            Equipment = new RecordDef("Ausrüstung", 0x1C);
            Flags(Equipment.Add("Träger", "CharacterBitflag", 0, FieldType.Flags16), CharFlags);
            Equipment.Add("Stärke", "Strength", 2, FieldType.S16);
            Equipment.Add("Vitalität", "Vitality", 4, FieldType.S16);
            Equipment.Add("Aktion", "Action", 6, FieldType.S16);
            Equipment.Add("Bewegung", "Movement", 8, FieldType.S16);
            Equipment.Add("Wirksam gegen", "EffectiveOn", 10, FieldType.U8).Labels = G2DataGuiLabels.EffectiveOn;
            for (int i = 0; i < 5; i++)
                Equipment.Add("Affinität " + Elems[i], new[] { "FireAffinity", "WindAffinity", "EarthAffinity", "LightningAffinity", "BlizzardAffinity" }[i], 11 + i, FieldType.S8);
            Flags(Equipment.Add("Zustandsschutz", "AilmentsBitflag", 16, FieldType.Flags8), Ailments);
            Equipment.Add("Zustand Chance", "AilmentsChance", 17, FieldType.U8);
            for (int i = 0; i < 8; i++)
                Equipment.Add("Bonus " + ElemsIncrease[i] + " %", "Increase" + new[] { "Fire", "Wind", "Earth", "Lightning", "Blizzard", "Water", "Explosion", "Forest" }[i] + "Percent", 18 + i, FieldType.S8);
            Equipment.Add("Spezialeffekt", "Special", 26, FieldType.S16).Labels = G2DataGuiLabels.EquipmentSpecials;

            // Usable.cs (0x20)
            Usable = new RecordDef("Verwendung", 0x20);
            Usable.Add("Zielwirkung", "TargetEffect", 0, FieldType.U8).Labels = G2DataGuiLabels.TargetEffects;
            Usable.Add("Zieltyp", "TargetType", 1, FieldType.U8).Labels = G2DataGuiLabels.TargetTypes;
            Usable.Add("Kraft", "Power", 2, FieldType.S16);
            Usable.Add("Reichweite", "Range", 4, FieldType.S16);
            Usable.Add("Wirkzeit", "CastTime", 6, FieldType.S16);
            Usable.Add("Erholzeit", "RecoveryTime", 8, FieldType.S16);
            Usable.Add("Animation", "Animation", 10, FieldType.U16).Labels = G2DataGuiLabels.Animations;
            Usable.Add("Wirksam gegen", "EffectiveOn", 12, FieldType.U8).Labels = G2DataGuiLabels.EffectiveOn;
            Usable.Unknown("Unknown1", 13, FieldType.U8);
            Usable.Add("IP-Schaden", "IpDamage", 14, FieldType.S16);
            Usable.Add("IP-Cancel-Schaden", "IpCancelDamage", 16, FieldType.S16);
            Usable.Add("Rückstoß", "Knockback", 18, FieldType.S16);
            Usable.Add("Element", "Element", 20, FieldType.U8).Labels = Elems;
            Usable.Add("Elementstärke", "ElementStrength", 21, FieldType.U8);
            Flags(Usable.Add("Zustände", "AilmentsBitflag", 22, FieldType.Flags8), Ailments);
            Usable.Add("Zustand Chance", "AilmentsChance", 23, FieldType.U8);
            Usable.Add("Mod. Angriff", "AttackModifier", 24, FieldType.S8);
            Usable.Add("Mod. Abwehr", "DefenseModifier", 25, FieldType.S8);
            Usable.Add("Mod. Aktion", "ActionModifier", 26, FieldType.S8);
            Usable.Add("Mod. Bewegung", "MovementModifier", 27, FieldType.S8);
            Usable.Add("Bruchchance", "BreakChance", 28, FieldType.U8);
            Usable.Add("Spezial", "Special", 29, FieldType.U8);
            Usable.Unknown("Unknown2", 30, FieldType.U8);
            Usable.Unknown("Unknown3", 31, FieldType.U8);

            // ---------- MS_PARAM.BIN: Move.cs, MoveStats.cs (108 Bytes) ----------
            Move = new RecordDef("Move", 108);
            Struct(Move.Add("ID", "ID", 0, FieldType.U8));
            Move.Add("Symbol", "Icon", 1, FieldType.U8).Labels = G2DataGuiLabels.Icons;
            Move.Text("Name", "Name", 2, 18);
            Move.Add("Kosten", "Stats.Cost", 20, FieldType.S16);
            Move.Add("Zielwirkung", "Stats.TargetEffect", 22, FieldType.U8).Labels = G2DataGuiLabels.TargetEffects;
            Move.Add("Zieltyp", "Stats.TargetType", 23, FieldType.U8).Labels = G2DataGuiLabels.TargetTypes;
            Move.Add("Stärke", "Stats.Strength", 24, FieldType.S16);
            Move.Add("Kraft", "Stats.Power", 26, FieldType.S16);
            Move.Add("Reichweite", "Stats.Range", 28, FieldType.S16);
            Move.Add("Wirkzeit Stufe 1", "Stats.Cast1Time", 30, FieldType.S16);
            Move.Add("Wirkzeit Stufe 5", "Stats.Cast5Time", 32, FieldType.S16);
            Move.Add("Erholzeit", "Stats.RecoveryTime", 34, FieldType.S16);
            Move.Add("Animation", "Stats.Animation", 36, FieldType.S16).Labels = G2DataGuiLabels.Animations;
            Move.Unknown("Stats.Unknown1", 38, FieldType.U8);
            Move.Add("Niederwerfen (0/1)", "Stats.Knockdown", 39, FieldType.U8);
            Move.Add("IP-Schaden", "Stats.IpDamage", 40, FieldType.S16);
            Move.Add("IP-Cancel-Schaden", "Stats.IpCancelDamage", 42, FieldType.S16);
            Move.Add("Rückstoß", "Stats.Knockback", 44, FieldType.S16);
            Move.Add("Element", "Stats.Element", 46, FieldType.U8).Labels = Elems;
            Move.Add("Elementstärke", "Stats.ElementStrength", 47, FieldType.U8);
            Flags(Move.Add("Zustände", "Stats.AilmentsBitflag", 48, FieldType.Flags8), Ailments);
            Move.Add("Zustand Chance", "Stats.AilmentsChance", 49, FieldType.U8);
            Move.Add("Mod. Angriff", "Stats.AttackModifier", 50, FieldType.S8);
            Move.Add("Mod. Abwehr", "Stats.DefenseModifier", 51, FieldType.S8);
            Move.Add("Mod. Aktion", "Stats.ActionModifier", 52, FieldType.S8);
            Move.Add("Mod. Bewegung", "Stats.MovementModifier", 53, FieldType.S8);
            Move.Add("Spezial", "Stats.Special", 54, FieldType.S16);
            for (int i = 0; i < 5; i++) Move.Add("Münzkosten Stufe " + (i + 1), "Stats.CoinCost" + (i + 1), 56 + 2 * i, FieldType.S16);
            Move.Add("Multiplikator", "Stats.Multiplier", 66, FieldType.S16);
            Move.Text("Beschreibung", "Description", 68, 40);

            // ---------- SK_PARAM.BIN: Skill.cs, SkillStats.cs (104 Bytes) ----------
            Skill = new RecordDef("Skill", 104);
            Skill.Text("Name", "Name", 0, 18);
            Skill.Add("Kostentyp 1", "Stats.CostType1", 18, FieldType.U8).Labels = G2DataGuiLabels.CoinCostTypes;
            Skill.Add("Kostentyp 2", "Stats.CostType2", 19, FieldType.U8).Labels = G2DataGuiLabels.CoinCostTypes;
            string[] sk = { "HP", "MP", "SP", "Strength", "Vitality", "Action", "Movement", "Magic", "Mentality" };
            string[] skDe = { "HP", "MP", "SP", "Stärke", "Vitalität", "Aktion", "Bewegung", "Magie", "Mentalität" };
            for (int i = 0; i < sk.Length; i++) Skill.Add("Basis " + skDe[i], "Stats.Base" + sk[i], 20 + 2 * i, FieldType.S16);
            for (int i = 0; i < 5; i++) Skill.Unknown("Stats.Unknown" + (i + 1), 38 + i, FieldType.U8);
            for (int i = 0; i < 8; i++)
                Skill.Add("Bonus " + ElemsIncrease[i] + " %", "Stats.Increase" + new[] { "Fire", "Wind", "Earth", "Lightning", "Blizzard", "Water", "Explosion", "Forest" }[i] + "Percent", 43 + i, FieldType.S8);
            Skill.Add("Spezial", "Stats.Special", 51, FieldType.U8);
            for (int i = 0; i < 5; i++) Skill.Add("Münzkosten Stufe " + (i + 1), "Stats.CoinCost" + (i + 1), 52 + 2 * i, FieldType.S16);
            Skill.Add("Multiplikator", "Stats.Multiplier", 62, FieldType.S16);
            Skill.Text("Beschreibung", "Description", 64, 40);

            // ---------- TB_MAGIC.BIN: Manaegg.cs, ManaeggSpell.cs (18 x 4 Bytes) ----------
            Manaegg = new RecordDef("Manaei", 72);
            for (int i = 0; i < 18; i++)
            {
                int b = i * 4; string n = "Zauber " + (i + 1) + ": ";
                var r = Manaegg.Add(n + "Move-Index", "Spell" + (i + 1) + ".SpellOffset", b, FieldType.U8); r.Ref = RefKind.Move;
                Manaegg.Add(n + "Startstufe", "Spell" + (i + 1) + ".StartingLevel", b + 1, FieldType.U8);
                Manaegg.Add(n + "Benötigte Stufe", "Spell" + (i + 1) + ".RequiredLevel", b + 2, FieldType.U8);
                Manaegg.Unknown("Spell" + (i + 1) + ".Unknown1", b + 3, FieldType.U8).Label = n + "Unbekannt";
            }

            // ---------- TB_SKILL.BIN: Skillbook.cs, SkillbookSkill.cs (6 x 4 Bytes) ----------
            Skillbook = new RecordDef("Skillbuch", 24);
            for (int i = 0; i < 6; i++)
            {
                int b = i * 4; string n = "Skill " + (i + 1) + ": ";
                Skillbook.Add(n + "Skill-Index", "Skill" + (i + 1) + ".SkillOffset", b, FieldType.U8).Ref = RefKind.Skill;
                Skillbook.Add(n + "Startstufe", "Skill" + (i + 1) + ".StartingLevel", b + 1, FieldType.U8);
                Skillbook.Add(n + "Benötigte Stufe", "Skill" + (i + 1) + ".RequiredLevel", b + 2, FieldType.U8);
                Skillbook.Unknown("Skill" + (i + 1) + ".Unknown1", b + 3, FieldType.U8).Label = n + "Unbekannt";
            }

            // ---------- TB_SPCL.BIN: SpecialSet.cs, SpecialMove.cs (6 x 4 Bytes) ----------
            SpecialSet = new RecordDef("Spezialtechniken", 24);
            for (int i = 0; i < 6; i++)
            {
                int b = i * 4; string n = "Technik " + (i + 1) + ": ";
                SpecialSet.Add(n + "Move-Index", "Special" + (i + 1) + ".MoveOffset", b, FieldType.U8).Ref = RefKind.Move;
                SpecialSet.Add(n + "Startstufe", "Special" + (i + 1) + ".StartingLevel", b + 1, FieldType.U8);
                SpecialSet.Add(n + "Story-Flag", "Special" + (i + 1) + ".StoryFlag", b + 2, FieldType.U16);
            }

            // ---------- PC_INIT.BIN: CharacterStartingStats.cs (80 Bytes) ----------
            PcInit = new RecordDef("Startwerte", 80);
            PcInit.Add("Erfahrung", "Experience", 0, FieldType.U32);
            string[] eq = { "Weapon", "Armor", "Headgear", "Footwear", "Accessory", "Manaegg" };
            string[] eqDe = { "Waffe", "Rüstung", "Kopf", "Füße", "Accessoire", "Manaei" };
            for (int i = 0; i < 6; i++) PcInit.Add(eqDe[i] + " (Item-Index)", eq[i], 4 + 2 * i, FieldType.S16).Ref = RefKind.Item;
            PcInit.Add("Ausdauer", "Stamina", 16, FieldType.S16);
            for (int i = 0; i < 10; i++) PcInit.Unknown("Unknown" + (i + 1), 18 + 2 * i, FieldType.S16);
            PcInit.Add("IP-Betäubung", "IpStun", 38, FieldType.S16);
            PcInit.Add("IP-Cancel-Betäubung", "IpCancelStun", 40, FieldType.S16);
            PcInit.Add("SP-Regen Combo", "ComboSpRegen", 42, FieldType.U8);
            PcInit.Add("SP-Regen Kritisch", "CritSpRegen", 43, FieldType.U8);
            PcInit.Unknown("Unknown11", 44, FieldType.U8);
            PcInit.Add("SP-Regen Treffer", "HitSpRegen", 45, FieldType.U8);
            PcInit.Unknown("Unknown12", 46, FieldType.U8);
            PcInit.Add("Ausweichen stehend", "EvasionStillRate", 47, FieldType.U8);
            PcInit.Add("Ausweichen in Bewegung", "EvasionMovingRate", 48, FieldType.U8);
            PcInit.Add("Rückstoß-Resistenz", "KnockbackResist", 49, FieldType.U8);
            PcInit.Unknown("Unknown13", 50, FieldType.S16);
            PcInit.Add("Zustandserholung", "StatusRecoveryTime", 52, FieldType.S16);
            PcInit.Add("TDMG", "TDMG", 54, FieldType.S16);
            PcInit.Unknown("Unknown14", 56, FieldType.S16);
            PcInit.Add("THEAL", "THEAL", 58, FieldType.S16);
            PcInit.Add("Größe", "Size", 60, FieldType.S16);
            for (int i = 0; i < 9; i++) PcInit.Unknown("Unknown" + (15 + i), 62 + 2 * i, FieldType.S16);

            // ---------- TB_LVUP.BIN: LevelupStats.cs (24 Bytes je Stufe) ----------
            Levelup = new RecordDef("Stufe", 24);
            Levelup.Add("Erfahrung", "Experience", 0, FieldType.S32);
            string[] lv = { "HP", "MP", "SP", "Strength", "Vitality", "Action", "Movement", "Magic", "Mentality", "SkillSlot" };
            string[] lvDe = { "HP", "MP", "SP", "Stärke", "Vitalität", "Aktion", "Bewegung", "Magie", "Mentalität", "Skill-Slot" };
            for (int i = 0; i < lv.Length; i++) Levelup.Add("+ " + lvDe[i], "Increase" + lv[i], 4 + 2 * i, FieldType.S16);

            // ---------- enemy/boss *_0.dat: EnemyStats.cs (0x60) ----------
            EnemyStats = new RecordDef("Werte", 0x60);
            EnemyStats.Text("Name", "Name", 0, 18);
            EnemyStats.Unknown("Unknown1", 18, FieldType.U8);
            EnemyStats.Unknown("Unknown2", 19, FieldType.U8);
            EnemyStats.Add("Typ 1", "Type1", 20, FieldType.U8).Labels = G2DataGuiLabels.EnemyTypes;
            EnemyStats.Add("Typ 2", "Type2", 21, FieldType.U8).Labels = G2DataGuiLabels.EnemyTypes;
            EnemyStats.Add("Stufe", "Level", 22, FieldType.S16);
            EnemyStats.Add("HP", "Health", 24, FieldType.S32);
            EnemyStats.Add("MP", "MP", 28, FieldType.S16);
            EnemyStats.Add("SP", "SP", 30, FieldType.S16);
            EnemyStats.Add("Vitalität", "Vitality", 32, FieldType.S16);
            EnemyStats.Add("Agilität", "Agility", 34, FieldType.S16);
            EnemyStats.Add("Tempo", "Speed", 36, FieldType.S16);
            EnemyStats.Add("Mentalität", "Mentality", 38, FieldType.S16);
            EnemyStats.Add("Ausdauer", "Stamina", 40, FieldType.S16);
            EnemyStats.Add("IP-Betäubung Dauer", "IpStunDuration", 42, FieldType.S16);
            EnemyStats.Add("IP-Cancel-Betäubung Dauer", "IpCancelStunDuration", 44, FieldType.S16);
            EnemyStats.Add("Ausweichen stehend", "EvasionStillRate", 46, FieldType.U8);
            EnemyStats.Add("Ausweichen in Bewegung", "EvasionMovingRate", 47, FieldType.U8);
            for (int i = 0; i < 5; i++)
                EnemyStats.Add("Resistenz " + Elems[i], new[] { "FireResist", "WindResist", "EarthResist", "LightningResist", "BlizzardResist" }[i], 48 + i, FieldType.S8);
            Flags(EnemyStats.Add("Zustandsimmunität", "AilmentsBitflag", 53, FieldType.Flags8), Ailments);
            EnemyStats.Add("Rückstoß-Resistenz", "KnockbackResist", 54, FieldType.S16);
            EnemyStats.Add("Zustandserholung", "StatusRecoveryTime", 56, FieldType.S16);
            EnemyStats.Add("TDMG", "TDMG", 58, FieldType.S16);
            EnemyStats.Unknown("Unknown3", 60, FieldType.S16);
            EnemyStats.Add("THEAL", "THEAL", 62, FieldType.S16);
            EnemyStats.Add("Größe", "Size", 64, FieldType.S16);
            EnemyStats.Unknown("Unknown4", 66, FieldType.S16);
            EnemyStats.Unknown("Unknown5", 68, FieldType.U8);
            EnemyStats.Add("Flucht verboten (0/1)", "NoRunFlag", 69, FieldType.U8);
            EnemyStats.Unknown("Unknown6", 70, FieldType.S16);
            EnemyStats.Add("Erfahrung", "Experience", 72, FieldType.S32);
            EnemyStats.Add("Skill-Münzen", "SkillCoins", 76, FieldType.S32);
            EnemyStats.Add("Magie-Münzen", "MagicCoins", 80, FieldType.S32);
            EnemyStats.Add("Gold", "GoldCoins", 84, FieldType.S32);
            EnemyStats.Add("Drop 1 (Item-Index)", "Item1Offset", 88, FieldType.S16).Ref = RefKind.Item;
            EnemyStats.Add("Drop 2 (Item-Index)", "Item2Offset", 90, FieldType.S16).Ref = RefKind.Item;
            EnemyStats.Add("Drop 1 Chance", "Item1Chance", 92, FieldType.U8);
            EnemyStats.Add("Drop 2 Chance", "Item2Chance", 93, FieldType.U8);
            EnemyStats.Unknown("Unknown7", 94, FieldType.S16);

            // EnemyAISection.cs / EnemyAI.cs (5 x 6 Bytes), liegt direkt hinter den Werten
            EnemyAI = new RecordDef("KI", 30);
            for (int i = 0; i < 5; i++)
            {
                int b = i * 6; string n = "KI " + (i + 1) + ": ";
                EnemyAI.Add(n + "Bedingung", "AI" + (i + 1) + ".Condition", b, FieldType.U8);
                for (int m = 0; m < 5; m++) EnemyAI.Add(n + "Chance Angriff " + (m + 1), "AI" + (i + 1) + ".Move" + (m + 1) + "Chance", b + 1 + m, FieldType.U8);
            }

            // EnemyMove.cs + EnemyMoveStats.cs (18 + 42 = 60 Bytes)
            EnemyMove = new RecordDef("Angriff", 60);
            EnemyMove.Text("Name", "Name", 0, 18);
            EnemyMove.Add("MP", "Stats.MP", 18, FieldType.S16);
            EnemyMove.Add("SP", "Stats.SP", 20, FieldType.S16);
            EnemyMove.Unknown("Stats.Unknown1", 22, FieldType.U8);
            EnemyMove.Add("Zielwirkung", "Stats.TargetEffect", 23, FieldType.U8).Labels = G2DataGuiLabels.TargetEffects;
            EnemyMove.Add("Stärke", "Stats.Strength", 24, FieldType.S16);
            EnemyMove.Add("Kraft", "Stats.Power", 26, FieldType.S16);
            EnemyMove.Add("AD", "Stats.AD", 28, FieldType.S16);
            EnemyMove.Add("Zieltyp", "Stats.TargetType", 30, FieldType.U8).Labels = G2DataGuiLabels.TargetTypes;
            EnemyMove.Add("Normaler Angriff (0/1)", "Stats.NormalAttackFlag", 31, FieldType.U8);
            EnemyMove.Add("Distanz", "Stats.Distance", 32, FieldType.S16);
            EnemyMove.Add("Treffsicherheit", "Stats.Accuracy", 34, FieldType.S16);
            EnemyMove.Add("Reichweite", "Stats.Range", 36, FieldType.S16);
            EnemyMove.Add("Wirkzeit", "Stats.CastTime", 38, FieldType.S16);
            EnemyMove.Add("Erholzeit", "Stats.RecoveryTime", 40, FieldType.S16);
            EnemyMove.Add("Animation", "Stats.Animation", 42, FieldType.U8).Labels = G2DataGuiLabels.Animations;
            EnemyMove.Add("Niederwerfen (0/1)", "Stats.Knockdown", 43, FieldType.U8);
            EnemyMove.Add("IP-Betäubung", "Stats.IpStun", 44, FieldType.S16);
            EnemyMove.Add("IP-Cancel-Betäubung", "Stats.IpCancelStun", 46, FieldType.S16);
            EnemyMove.Add("Rückstoß", "Stats.Knockback", 48, FieldType.S16);
            EnemyMove.Add("Element", "Stats.Element", 50, FieldType.U8).Labels = Elems;
            EnemyMove.Add("Elementstärke", "Stats.ElementStrength", 51, FieldType.U8);
            Flags(EnemyMove.Add("Zustände", "Stats.AilmentsBitflag", 52, FieldType.Flags8), Ailments);
            EnemyMove.Add("Zustand Chance", "Stats.AilmentsChance", 53, FieldType.U8);
            EnemyMove.Add("Mod. Angriff", "Stats.AttackModifier", 54, FieldType.S8);
            EnemyMove.Add("Mod. Abwehr", "Stats.DefenseModifier", 55, FieldType.S8);
            EnemyMove.Add("Mod. Aktion", "Stats.ActionModifier", 56, FieldType.S8);
            EnemyMove.Add("Mod. Bewegung", "Stats.MovementModifier", 57, FieldType.S8);
            EnemyMove.Add("Spezial", "Stats.Special", 58, FieldType.S16);

            foreach (var d in new[] { Item, Equipment, Usable, Move, Skill, Manaegg, Skillbook, SpecialSet, PcInit, Levelup, EnemyStats, EnemyAI, EnemyMove })
                d.SelfCheck();
        }

        static void Struct(FieldDef f) { f.Structural = true; }
        static void Flags(FieldDef f, string[] names) { f.FlagNames = names; }

        // =====================================================================
        // Pruefung + Aufbau der Datensaetze. Jede Datei muss exakt zu den belegten
        // Groessen passen, sonst wird sie gesperrt (Errors) und nie geschrieben.
        // =====================================================================

        public static List<Record> BuildTable(GameFile f)
        {
            switch (f.FormatKey)
            {
                case "ITEM.BIN": return BuildItems(f);
                case "MS_PARAM.BIN": return BuildFixed(f, Move, MoveCount, "Move", 2, null);
                case "SK_PARAM.BIN": return BuildFixed(f, Skill, SkillCount, "Skill", 0, null);
                case "TB_MAGIC.BIN": return BuildFixed(f, Manaegg, ManaeggCount, "Manaei", -1, G2DataGuiLabels.Eggs);
                case "TB_SKILL.BIN": return BuildFixed(f, Skillbook, SkillbookCount, "Skillbuch", -1, G2DataGuiLabels.Books);
                case "TB_SPCL.BIN": return BuildFixed(f, SpecialSet, SpecialSetCount, "Set", -1, G2DataGuiLabels.SpecialChars);
                case "PC_INIT.BIN": return BuildPcInit(f);
                case "TB_LVUP.BIN": return BuildLevelups(f);
                case "ENEMY": return BuildEnemyFile(f);
            }
            f.Errors.Add(L.F("Unbekanntes Format {0}", f.FormatKey));
            return new List<Record>();
        }

        static string NameAt(GameFile f, int abs, int len)
        {
            string n = FixedText.Decode(f.Data, abs, len);
            return n.Length == 0 ? L.T("(ohne Namen)") : n;
        }

        static void CheckTextShare(GameFile f, List<Record> recs, int nameOffsetInFirstSection, int len)
        {
            if (recs.Count == 0) return;
            int bad = 0;
            foreach (var r in recs)
                if (!Bin.IsPrintableText(f.Data, r.Sections[0].Base + nameOffsetInFirstSection, len)) bad++;
            if (bad * 4 > recs.Count)
                f.Errors.Add(L.F("{0} von {1} Namen enthalten keine lesbaren Zeichen. Das Layout passt nicht zu dieser Datei.", bad, recs.Count));
            else if (bad > 0)
                f.Notes.Add(L.F("{0} Namen enthalten Sonderbytes (werden als {{XX}} angezeigt und unverändert erhalten).", bad));
        }

        static List<Record> BuildFixed(GameFile f, RecordDef def, int expectedCount, string prefix, int nameOff, string[] names)
        {
            var list = new List<Record>();
            int expected = def.Size * expectedCount;
            if (f.Data.Length != expected)
            {
                f.Errors.Add(L.F("Dateigröße {0} Bytes, erwartet {1} ({2} x {3}). Datei gesperrt.", f.Data.Length, expected, expectedCount, def.Size));
                return list;
            }
            for (int i = 0; i < expectedCount; i++)
            {
                var r = new Record { File = f, Index = i, Key = f.FormatKey + "#" + i };
                r.Sections.Add(new Section(def.Name, i * def.Size, def));
                string label;
                if (nameOff >= 0) label = NameAt(f, i * def.Size + nameOff, 18);
                else label = (names != null && i < names.Length) ? names[i] : L.T(prefix) + " " + i;
                r.Label = i.ToString("D3") + "  " + label;
                list.Add(r);
            }
            if (nameOff >= 0) CheckTextShare(f, list, nameOff, 18);
            return list;
        }

        /// <summary>
        /// Abschnitte eines Items neu aus den aktuellen Zeigern bestimmen (nach ItemUsable.Assign/Release, Verwerfen,
        /// Wiederherstellen). Gleiche Regeln wie BuildItems, ohne Fehlermeldungen: ungueltige Zeiger ergeben keinen Abschnitt.
        /// </summary>
        public static void RefreshItemSections(Record r)
        {
            var d = r.File.Data;
            int b = r.Index * ItemSize, tableEnd = ItemCount * ItemSize;
            if (d.Length < tableEnd) return;
            r.Sections.Clear();
            r.Sections.Add(new Section("Allgemein", b, Item));
            int eq = Bin.S32(d, b + 68), us = Bin.S32(d, b + 72);
            if (eq >= tableEnd && eq + Equipment.Size <= d.Length) r.Sections.Add(new Section("Ausrüstung", eq, Equipment));
            if (us >= tableEnd && us + Usable.Size <= d.Length) r.Sections.Add(new Section("Verwendung", us, Usable));
        }

        static List<Record> BuildItems(GameFile f)
        {
            var list = new List<Record>();
            int tableEnd = ItemCount * ItemSize;
            if (f.Data.Length < tableEnd)
            {
                f.Errors.Add(L.F("Datei kleiner als die Item-Tabelle ({0} < {1} Bytes). Datei gesperrt.", f.Data.Length, tableEnd));
                return list;
            }
            int idMismatch = 0;
            for (int i = 0; i < ItemCount; i++)
            {
                int b = i * ItemSize;
                var r = new Record { File = f, Index = i, Key = "ITEM.BIN#" + i };
                r.Sections.Add(new Section("Allgemein", b, Item));
                int eq = Bin.S32(f.Data, b + 68), us = Bin.S32(f.Data, b + 72);
                if (eq > 0)
                {
                    if (eq < tableEnd || eq + Equipment.Size > f.Data.Length)
                        f.Errors.Add(L.F("Item {0}: Zeiger Ausrüstung 0x{1} zeigt außerhalb des Datenbereichs.", i, eq.ToString("X")));
                    else r.Sections.Add(new Section("Ausrüstung", eq, Equipment));
                }
                else if (eq != -1 && eq != 0) f.Errors.Add(L.F("Item {0}: unerwarteter Zeigerwert {1}", i, eq));
                if (us > 0)
                {
                    if (us < tableEnd || us + Usable.Size > f.Data.Length)
                        f.Errors.Add(L.F("Item {0}: Zeiger Verwendung 0x{1} zeigt außerhalb des Datenbereichs.", i, us.ToString("X")));
                    else r.Sections.Add(new Section("Verwendung", us, Usable));
                }
                else if (us != -1 && us != 0) f.Errors.Add(L.F("Item {0}: unerwarteter Zeigerwert {1}", i, us));
                if (Bin.U32(f.Data, b + 76) != i) idMismatch++;
                int type = f.Data[b + 58];
                r.Group = type < G2DataGuiLabels.ItemEntries.Length ? G2DataGuiLabels.ItemEntries[type] : L.T("Typ") + " " + type;
                r.Label = i.ToString("D3") + "  " + NameAt(f, b, 18);
                list.Add(r);
            }
            if (idMismatch > 0) f.Notes.Add(L.F("{0} Items haben im ID-Feld einen anderen Wert als ihre Position (nur Info, ID bleibt unverändert).", idMismatch));
            if (f.Errors.Count > 20) { int n = f.Errors.Count; f.Errors.RemoveRange(20, n - 20); f.Errors.Add(L.F("... {0} weitere Fehler", n - 20)); }
            CheckTextShare(f, list, 0, 18);
            return list;
        }

        static List<Record> BuildPcInit(GameFile f)
        {
            var list = new List<Record>();
            if (f.Data.Length == 0 || f.Data.Length % PcInit.Size != 0)
            {
                f.Errors.Add(L.F("Dateigröße {0} ist kein Vielfaches von {1}. Datei gesperrt.", f.Data.Length, PcInit.Size));
                return list;
            }
            int n = f.Data.Length / PcInit.Size;
            for (int i = 0; i < n; i++)
            {
                var r = new Record { File = f, Index = i, Key = "PC_INIT.BIN#" + i };
                r.Sections.Add(new Section("Startwerte", i * PcInit.Size, PcInit));
                r.Label = i.ToString("D2") + "  " + (i < G2DataGuiLabels.Characters.Length ? G2DataGuiLabels.Characters[i] : L.T("Figur") + " " + i);
                list.Add(r);
            }
            return list;
        }

        static List<Record> BuildLevelups(GameFile f)
        {
            var list = new List<Record>();
            int per = Levelup.Size * LevelsPerCharacter;
            if (f.Data.Length == 0 || f.Data.Length % per != 0)
            {
                f.Errors.Add(L.F("Dateigröße {0} ist kein Vielfaches von {1} (99 Stufen x 24 Bytes). Datei gesperrt.", f.Data.Length, per));
                return list;
            }
            int n = f.Data.Length / per;
            for (int c = 0; c < n; c++)
            {
                var r = new Record { File = f, Index = c, Key = "TB_LVUP.BIN#" + c };
                for (int l = 0; l < LevelsPerCharacter; l++)
                    r.Sections.Add(new Section("Stufe " + (l + 1), c * per + l * Levelup.Size, Levelup));
                r.Label = c.ToString("D2") + "  " + (c < G2DataGuiLabels.Characters.Length ? G2DataGuiLabels.Characters[c] : L.T("Figur") + " " + c);
                list.Add(r);
            }
            return list;
        }

        /// <summary>Gegner/Boss-Datei *_0.dat: bis zu zwei Gegner pro Datei (Zeiger 0x34/0x3C und 0x44/0x4C).</summary>
        static List<Record> BuildEnemyFile(GameFile f)
        {
            var list = new List<Record>();
            if (f.Data.Length < 0x50)
            {
                f.Errors.Add(L.F("Datei zu klein für den Kopf ({0} Bytes).", f.Data.Length));
                return list;
            }
            for (int slot = 0; slot < 2; slot++)
            {
                int sp = Bin.S32(f.Data, slot == 0 ? EnemyStatsPtr1 : EnemyStatsPtr2);
                int mp = Bin.S32(f.Data, slot == 0 ? EnemyMovesPtr1 : EnemyMovesPtr2);
                if (slot == 1 && sp <= 0) break;
                if (sp <= 0 || sp + EnemyStats.Size + EnemyAI.Size > f.Data.Length)
                {
                    f.Errors.Add(L.F("Gegner {0}: Werte-Zeiger 0x{1} ungültig.", slot + 1, sp.ToString("X")));
                    break;
                }
                if (mp <= 0 || mp + EnemyMove.Size * EnemyMovesPerSet > f.Data.Length)
                {
                    f.Errors.Add(L.F("Gegner {0}: Angriffs-Zeiger 0x{1} ungültig.", slot + 1, mp.ToString("X")));
                    break;
                }
                var r = new Record { File = f, Index = slot, Key = f.RelPath + "#" + slot };
                r.Sections.Add(new Section("Werte", sp, EnemyStats));
                r.Sections.Add(new Section("KI", sp + EnemyStats.Size, EnemyAI));
                for (int m = 0; m < EnemyMovesPerSet; m++)
                    r.Sections.Add(new Section("Angriff " + (m + 1) + ": " + NameAt(f, mp + m * EnemyMove.Size, 18), mp + m * EnemyMove.Size, EnemyMove));
                r.Group = Path.GetFileName(f.FullPath);
                r.Label = NameAt(f, sp, 18) + "   [" + Path.GetFileName(f.FullPath) + (slot == 1 ? " #2" : "") + "]";
                if (!Bin.IsPrintableText(f.Data, sp, 18))
                    f.Errors.Add(L.F("Gegner {0}: Name an 0x{1} ist kein Text. Layout passt nicht.", slot + 1, sp.ToString("X")));
                list.Add(r);
            }
            if (!f.Valid) list.Clear();
            return list;
        }
    }
}
