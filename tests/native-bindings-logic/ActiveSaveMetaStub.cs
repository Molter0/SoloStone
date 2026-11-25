// The extracted Arena code reads the save meta (Classic saves have no Arena); this suite links no LocalCardCatalog, so it
// models a Violet Hold save. Classic saves' Arena refusal is pinned in PluginSourcePins.
internal static class ActiveSaveMeta
{
    internal const string VioletHold = "violet-hold", Classic = "classic-2014";
    internal static bool IsClassic => false;
}
