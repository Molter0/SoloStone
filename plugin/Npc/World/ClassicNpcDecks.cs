using System;
using System.Collections.Generic;
using System.Linq;

// Classic 2014 (docs/classic-2014.md): data/npc/classic-decks-v1.json, the NPC decks of a Classic save. In the file every deck
// has admission "classic", so no Violet Hold queue ever serves one. A Classic save's world is built from them alone:
// - Ranked: every classic deck admitted, served by its band tags (the starters at Bronze and Silver);
// - Casual: the same decks as Casual decks under their own ids (CasualSuffix), one family per deck;
// - the Core step (NpcCoreDecks.Include): each class's classic starter stands in as its Core deck.
public static class ClassicNpcDecks
{
    public const string ResourceName = "SoloStone.Npc.classic-decks-v1.json";
    public const string Meta = "classic-2014", Admission = "classic", Origin = "classic-curated";
    public const string StarterPrefix = "npc-classic-starter-", CasualSuffix = "-casual";

    public static bool IsClassic(NpcDeckLibrary library) => library != null && library.ActiveMeta == Meta;

    private static NpcDeckEntry With(NpcDeckEntry deck, string id, string admission, string origin = Origin, NpcDeckTags tags = null) =>
        new NpcDeckEntry(id, deck.Version, deck.Name, deck.Class, deck.Hero, origin, admission, deck.PlanId, tags ?? deck.Tags,
            deck.Source, deck.Cards, deck.Sideboards);

    private static void Require(NpcDeckLibrary classic)
    {
        if (!IsClassic(classic)) throw new System.IO.InvalidDataException("Not the classic NPC deck library.");
        var bad = classic.Decks.FirstOrDefault(d => d.Admission != Admission || d.Origin != Origin);
        if (bad != null) throw new System.IO.InvalidDataException("Classic deck must have admission \"classic\": " + bad.Id);
    }

    // The ladder's library of a Classic save: every classic deck admitted.
    public static NpcDeckLibrary Ranked(NpcDeckLibrary classic)
    {
        Require(classic);
        return new NpcDeckLibrary(classic.Schema, classic.LibraryVersion, classic.ActiveMeta,
            classic.Decks.Select(d => With(d, d.Id, "admitted")));
    }

    // Casual's catalog of a Classic save: each classic deck once more as a Casual deck, in the family of its ranked id.
    public static CasualDeckCatalog Casual(NpcDeckLibrary classic)
    {
        Require(classic);
        var decks = classic.Decks.Select(d => With(d, d.Id + CasualSuffix, "casual")).ToList();
        var families = classic.Decks.ToDictionary(d => d.Id + CasualSuffix, d => "classic:" + d.Id, StringComparer.Ordinal);
        return new CasualDeckCatalog(new NpcDeckLibrary(classic.Schema, classic.LibraryVersion, classic.ActiveMeta, decks),
            new Dictionary<string, string>(StringComparer.Ordinal), families);
    }

    // The Core decks of a Classic world: each class's starter (npc-classic-starter-<class>) as npc-core-<class>.
    public static IReadOnlyList<NpcDeckEntry> CoreEntries(NpcDeckLibrary library) => library == null ? new NpcDeckEntry[0]
        : library.Decks.Where(d => d.Id.StartsWith(StarterPrefix, StringComparison.Ordinal) && d.Admission != "casual")
            .GroupBy(d => d.Class, StringComparer.Ordinal).Select(g => g.First())
            .Select(d => With(d, NpcCoreDecks.IdFor(d.Class), NpcCoreDecks.Admission, NpcCoreDecks.Origin,
                new NpcDeckTags(d.Tags.Archetype, 5, 2, 0, 3, LadderBand.Bronze, LadderBand.Legend)))
            .ToArray();
}
