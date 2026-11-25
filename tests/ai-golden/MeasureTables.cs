using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// `AiGolden --write-measures <repo>`: regenerates the two AI measure tables when the card set changes (new cards), from
// the same probes TriggerValueTests.TestTableMatchesTheCardSet and DeathValueTests.TestTableMatchesTheCardSet check.
// Only the generated parts change: the trigger rows and arming hero powers, and the death rows (Holders stays).
internal static class MeasureTables
{
    internal static int Write(string repo)
    {
        string trigger = Path.Combine(repo, "plugin", "Npc", "TriggerValueData.cs");
        string death = Path.Combine(repo, "plugin", "Npc", "DeathValueData.cs");
        string triggerRows = string.Join("\n", TriggerProbe.Generate().Select(r => r.Serialize()));
        string arming = string.Join(" ", TriggerProbe.ArmingHeroPowers());
        string deathRows = string.Join("\n", DeathProbe.Generate().OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Serialize()));
        Replace(trigger, @"(internal const string HeroPowerAttack = "")[^""]*("";)", "${1}" + arming + "${2}");
        Replace(trigger, @"(internal const string Rows = @""\n)[\s\S]*?(\n"";)", "${1}" + triggerRows.Replace("$", "$$") + "${2}");
        Replace(death, @"(internal const string Rows = @""\n)[\s\S]*?(\n"";)", "${1}" + deathRows.Replace("$", "$$") + "${2}");
        Console.WriteLine("wrote " + trigger + " and " + death);
        return 0;
    }

    private static void Replace(string path, string pattern, string replacement)
    {
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!Regex.IsMatch(text, pattern)) throw new InvalidDataException("table shape not found in " + path);
        File.WriteAllText(path, Regex.Replace(text, pattern, replacement));
    }
}
