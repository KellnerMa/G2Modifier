using System; using System.IO; using System.Linq; using System.Reflection; using System.Collections.Generic; using G2Modifier.Core;
// Prueft, dass jeder Text der Oberflaeche in EN und FR uebersetzt wird (inkl. zusammengesetzter Feldnamen).
static class LangCheck { static int Main(string[] a){
 int bad=0;
 foreach (var code in new[]{"en","fr"}) {
  L.Set(code); L.TrackMissing = true;
  var keys = File.ReadAllLines(a[0]).Select(k => k.Replace("\\n","\n")).ToList();
  foreach (var k in keys) L.T(k);
  foreach (var fi in typeof(Formats).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(RecordDef))) {
   var d=(RecordDef)fi.GetValue(null);
   foreach (var f in d.Fields){ L.T(f.Label); if(f.FlagNames!=null) foreach(var n in f.FlagNames) L.T(n);
     if(f.Labels!=null && code=="fr") foreach(var n in f.Labels.Where(x => !G2DataGuiLabels.Animations.Contains(x))) L.T(n); }
  }
  foreach (var x in G2DataGuiLabels.EquipmentSpecials) if (code=="fr") L.T(x);
  foreach (var x in G2DataGuiLabels.EnemyTypes) if (code=="fr") L.T(x);
  var ws = Workspace.Open(a[1]);
  foreach (var t in ws.Tables) { L.T(t.Title); foreach (var v in t.Variants) foreach (var r in v.Records) foreach (var s in r.Sections) L.T(System.Text.RegularExpressions.Regex.Replace(s.Title, @"^(Angriff \d+): .*$", "$1: Fire Bolt")); }
  foreach (var s in new[]{"Original","verändert","kein Backup"}) L.T(s);
  for (int c=1;c<=7;c++) Avatars.CharName(c);
  var miss = L.Missing.Where(m => !(code=="en" && IsEnglish(m))).OrderBy(m=>m).ToList();
  Console.WriteLine(code + ": " + miss.Count + " fehlend");
  foreach (var m in miss.Take(60)) Console.WriteLine("   " + m.Replace("\n","\\n"));
  bad += miss.Count;
  // Stichproben
  Console.WriteLine("   Beispiel: " + L.T("Zauber 3: Benötigte Stufe") + " | " + L.T("Affinität Feuer") + " | " + L.T("Münzkosten Stufe 2") + " | " + L.T("Unbekannt @0x3B") + " | " + L.T("Waffe (Item-Index)") + " | " + L.T("+ Mentalität") + " | " + L.T("Angriff 2: Fire Bolt") + " | " + L.T("30% damage as HP") + " | " + L.T("Unknown #12"));
  Console.WriteLine("   Format: " + L.F("{0} Dateien geladen, {1} gegenüber dem Backup verändert, {2} gesperrt.", 40, 2, 0) + " | " + L.F("Zeichen '{0}' ist nicht erlaubt. Erlaubt: einfache ASCII-Zeichen (keine Umlaute), Sonderbytes als {{XX}}.", 'ä'));
 }
 L.Set("de"); Console.WriteLine("de: " + L.T("Restore HP(MEN)") + " | " + L.T("+3 SP regen") + " | " + L.T("Zauber 3: Benötigte Stufe"));
 return bad == 0 ? 0 : 1;
}
static bool IsEnglish(string s) { return G2DataGuiLabels.TargetEffects.Contains(s) || G2DataGuiLabels.TargetTypes.Contains(s) || G2DataGuiLabels.ItemEntries.Contains(s) || G2DataGuiLabels.EffectiveOn.Contains(s) || G2DataGuiLabels.Icons.Contains(s) || G2DataGuiLabels.CoinCostTypes.Contains(s) || G2DataGuiLabels.EquipmentSpecials.Contains(s) || G2DataGuiLabels.EnemyTypes.Contains(s); }
}
