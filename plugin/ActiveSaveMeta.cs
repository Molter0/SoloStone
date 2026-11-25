using System;
using System.Collections.Generic;

// Classic 2014 (docs/classic-2014.md): the meta of the save this process opened. One process-wide value, set from the
// save's ProfileOptions.MetaId before its LocalProfile loads (SelectedProfile.OpenStartup), and scoped with Use while a
// different save is created or checked. Every meta question the plugin asks (LocalCardCatalog and its users) reads it.
public static class ActiveSaveMeta
{
    public const string VioletHold = "violet-hold", Classic = "classic-2014";
    public const string VioletHoldLabel = "Violet Hold — September 2026", ClassicLabel = "Classic — June 2014";
    public static readonly IReadOnlyList<string> Ids = Array.AsReadOnly(new[] { VioletHold, Classic });
    private static volatile string current = VioletHold;
    public static string Current => current;
    public static bool IsClassic => current == Classic;
    public static bool IsKnown(string id) => id == VioletHold || id == Classic;
    // A missing MetaId (options files from before Classic) is Violet Hold.
    public static string Normalize(string id) => id ?? VioletHold;
    public static string Label(string id) => Normalize(id) == Classic ? ClassicLabel : VioletHoldLabel;
    public static void Set(string id)
    {
        id = Normalize(id);
        if (!IsKnown(id)) throw new InvalidOperationException("Unknown save meta: " + id);
        current = id;
    }
    // Sets the meta until the result is disposed, then restores the previous one.
    public static IDisposable Use(string id)
    {
        string previous = current;
        Set(id);
        return new Restore(previous);
    }
    private sealed class Restore : IDisposable
    {
        private string previous;
        internal Restore(string previous) { this.previous = previous; }
        public void Dispose() { if (previous != null) { current = previous; previous = null; } }
    }
}
