using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

// docs/classic-2014.md: classic-decks-v1.json, the Classic save's NPC decks. Every card is a Classic-format card
// (VAN_*), every original class has a period deck and a starter, and every deck plays a whole Classic match.
internal static class ClassicSuite
{
    internal static void Run()
    {
        var library = NpcDeckLibrary.Parse(Program.Data("classic-decks-v1.json"));
        Program.Require(library.ActiveMeta == "classic-2014", "classic library meta");
        Program.Require(library.Validate().Count == 0, "classic library structure: " + string.Join("; ", library.Validate()));
        Program.Require(library.Decks.All(d => d.Admission == "classic" && !d.Admitted && d.Id.StartsWith("npc-classic-", StringComparison.Ordinal)),
            "every classic deck is admission classic (never served by the Violet Hold ladder)");
        var ranked = NpcDeckLibrary.Parse(Program.Data("decks-v1.json"));
        Program.Require(library.Decks.All(d => ranked.Find(d.Id) == null), "classic ids never collide with ranked decks");
        var classes = new[] { "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR" };
        Program.Require(library.Decks.Select(d => d.Class).Distinct().OrderBy(c => c).SequenceEqual(classes),
            "only the nine original classes, each with a deck");
        Program.Require(classes.All(c => library.Decks.Count(d => d.Class == c) >= 2), "every class has a period deck and a starter");
        foreach (var deck in library.Decks)
        {
            var hero = Cards.FromId(deck.Hero);
            Program.Require(hero.Class.ToString() == deck.Class, deck.Id + ": hero matches class");
            foreach (var id in deck.ExpandedCardIds())
            {
                var card = Cards.FromId(id);
                Program.Require(Cards.IsFormatLegal(card, FormatType.FT_CLASSIC), deck.Id + ": " + id + " is not a Classic card");
                Program.Require(card.Class == CardClass.NEUTRAL || card.Class == hero.Class, deck.Id + ": " + id + " belongs to another class");
            }
            Program.Require(deck.Cards.Where(c => Cards.FromId(c.Id).Rarity == Rarity.LEGENDARY).All(c => c.Count == 1), deck.Id + ": one copy of a Legendary");
        }
        Matches(library);
        World(library);
        Console.WriteLine("classic suite passed: " + library.Decks.Count + " decks, every deck played a Classic match to the end");
    }

    // A Classic save's world (ClassicNpcDecks, NpcWorldBoot): the ladder, Casual and the Core step serve classic decks only.
    private static void World(NpcDeckLibrary library)
    {
        var ranked = ClassicNpcDecks.Ranked(library);
        var casual = ClassicNpcDecks.Casual(library);
        Program.Require(ranked.Decks.Count == library.Decks.Count && ranked.Decks.All(d => d.Admitted), "every classic deck is admitted on a Classic ladder");
        Program.Require(ranked.Validate().Count == 0 && casual.Library.Validate().Count == 0,
            "Classic world libraries validate: " + string.Join("; ", ranked.Validate().Concat(casual.Library.Validate())));
        Program.Require(casual.Decks.Count == library.Decks.Count && casual.Decks.All(d => d.Admission == "casual" && d.Id.EndsWith(ClassicNpcDecks.CasualSuffix, StringComparison.Ordinal)),
            "every classic deck is also a Casual deck");
        var bad = 0;
        try { ClassicNpcDecks.Ranked(NpcDeckLibrary.Parse(Program.Data("decks-v1.json"))); } catch (System.IO.InvalidDataException) { bad++; }
        Program.Require(bad == 1, "the Violet Hold library is refused as the classic one");
        var merged = casual.MergeInto(ranked);
        var withCore = NpcCoreDecks.Include(merged);
        var core = withCore.Decks.Where(d => d.Admission == NpcCoreDecks.Admission).ToArray();
        Program.Require(core.Length == 9 && core.All(d => d.ExpandedCardIds().All(id => id.StartsWith("VAN_", StringComparison.Ordinal))),
            "the Core step of a Classic world serves the classic starters");
        Program.Require(!withCore.Decks.Any(d => d.Origin == NpcBeginnerDecks.Origin), "no Violet Hold beginner deck in a Classic world");
        var world = NpcWorld.Generate(new NpcWorldCatalogs(merged, SkillCatalog.Parse(Program.Data("skills-v1.json")),
            PersonalityCatalog.Parse(Program.Data("personalities-v1.json")), NpcNotables.Parse(Program.Data("notables-v1.json")),
            NpcNameTables.Parse(Program.Data("names-v1.json")), NpcCosmeticPool.Parse(Program.Data("cosmetics-v1.json"))));
        Program.Require(world.All.Count > 100 && world.All.All(n => n.DeckIds.All(id => ranked.Find(id) != null)),
            "every NPC of a Classic world owns classic decks only");
        bool Classic(NpcDeckEntry deck) => deck != null && deck.ExpandedCardIds().All(id => Cards.IsFormatLegal(Cards.FromId(id), FormatType.FT_CLASSIC));
        var ladder = new LadderMatchmaker(world);
        var served = new HashSet<string>(StringComparer.Ordinal);
        foreach (var star in new[] { 0, 10, 25, 40, LadderRules.LegendIndex })
            foreach (var difficulty in new[] { null, NpcDifficulties.Find(NpcDifficultyIds.Easy), NpcDifficulties.Find(NpcDifficultyIds.Medium) })
                for (int i = 0; i < 12; i++)
                {
                    var encounter = ladder.Next(new MatchRequest(2014, i, star, LadderRules.RatingForIndex(star), star >= LadderRules.LegendIndex ? 500 : (int?)null, null, difficulty));
                    Program.Require(Classic(encounter.Deck), "the Classic ladder served " + encounter.Deck.Id);
                    served.Add(encounter.Deck.Id);
                }
        Program.Require(served.Count >= 5, "the Classic ladder serves several classic decks: " + string.Join(",", served));
        var casualQueue = new CasualMatchmaker(world, casual);
        for (int i = 0; i < 40; i++)
        {
            var encounter = casualQueue.Next(new CasualRequest(2014, i, 10, 1200, new PastEncounter[0]));
            Program.Require(Classic(encounter.Deck), "Casual served " + encounter.Deck.Id);
        }
        Arena(ranked, casual, Classic);
    }

    // Classic Arena: arena-classic-decks-v1.json joins a Classic world as NpcWorldBoot does. The ladder and Casual pools stay the
    // classic decks, and the Arena matchmaker serves Classic drafts of the nine original classes.
    private static void Arena(NpcDeckLibrary ranked, CasualDeckCatalog casual, Func<NpcDeckEntry, bool> classic)
    {
        var arena = ArenaDeckCatalog.Parse(Program.Data("arena-classic-decks-v1.json"));
        Program.Require(arena.Library.ActiveMeta == "classic-2014" && arena.Decks.Count == 200
            && arena.Decks.All(d => d.Admission == "arena" && !d.Admitted && classic(d)), "200 Classic Arena drafts, never admitted");
        Program.Require(arena.Decks.Select(d => d.Class).Distinct().Count() == 9, "Classic Arena drafts cover the nine classes only");
        Program.Require(arena.Decks.All(d => ranked.Find(d.Id) == null && casual.Library.Find(d.Id) == null
            && NpcDeckLibrary.ToLocalDeck(d, d.Name).Build().Count == 30), "Classic Arena decks build and collide with nothing");
        var merged = arena.MergeInto(casual.MergeInto(ranked));
        var world = NpcWorld.Generate(new NpcWorldCatalogs(merged, SkillCatalog.Parse(Program.Data("skills-v1.json")),
            PersonalityCatalog.Parse(Program.Data("personalities-v1.json")), NpcNotables.Parse(Program.Data("notables-v1.json")),
            NpcNameTables.Parse(Program.Data("names-v1.json")), NpcCosmeticPool.Parse(Program.Data("cosmetics-v1.json"))));
        Program.Require(world.All.All(n => n.DeckIds.All(id => ranked.Find(id) != null)), "Arena drafts never become an NPC's ladder deck");
        var matchmaker = new ArenaMatchmaker(world, arena, null, SyntheticNpcWorld.StubProfile);
        var used = new List<string>();
        for (int i = 0; i < 14; i++)
        {
            var encounter = matchmaker.Next(new ArenaRequest(2014, i, i / 2, i % 3 == 2 ? 1 : 0, used, null));
            Program.Require(arena.Library.Find(encounter.Deck.Id) != null && classic(encounter.Deck), "Classic Arena served " + encounter.Deck.Id);
            used.Add(encounter.Deck.Id);
        }
    }

    // Each deck plays the next one with the plugin's quick bot on both sides, as a Classic match (FT_CLASSIC, classic pool).
    private static void Matches(NpcDeckLibrary library)
    {
        var decks = library.Decks.ToArray();
        for (int i = 0; i < decks.Length; i++)
        {
            var a = decks[i]; var b = decks[(i + 1) % decks.Length];
            var game = new Game(new GameConfig {
                Player1HeroClass = Cards.FromId(a.Hero).Class, Player2HeroClass = Cards.FromId(b.Hero).Class,
                Player1Deck = a.ExpandedCardIds().Select(Cards.FromId).ToList(), Player2Deck = b.ExpandedCardIds().Select(Cards.FromId).ToList(),
                StartPlayer = 1, Shuffle = true, SkipMulligan = true, History = true, Logging = false, RandomSeed = 1000 + i,
                FormatType = FormatType.FT_CLASSIC, PoolFormat = GenerationFormat.Classic });
            game.StartGame();
            for (int step = 0; step < 3000 && game.State != State.COMPLETE; step++)
            {
                var pending = game.GetPendingDecisions().FirstOrDefault();
                var owner = pending == null ? game.CurrentPlayer : game.ControllerById(pending.ControllerId);
                var task = LocalBot.Choose(owner.Options());
                if (task is ChooseTask choice)
                {
                    var decision = game.GetPendingDecisions().Single(x => x.ControllerId == owner.Id);
                    Program.Require(game.TryAnswer(new SabberStoneCore.Model.Decisions.DecisionAnswer(decision.Id, decision.ControllerId, choice.Choices), out var error),
                        a.Id + " vs " + b.Id + ": " + error);
                }
                else Program.Require(game.Process(task), a.Id + " vs " + b.Id + ": action rejected " + task);
            }
            Program.Require(game.State == State.COMPLETE, a.Id + " vs " + b.Id + ": the match ends (turn " + game.Turn + ")");
            var generated = game.Player1.BoardZone.Concat(game.Player2.BoardZone).Select(m => m.Card)
                .Concat(game.Player1.HandZone.Concat(game.Player2.HandZone).Select(c => c.Card));
            Program.Require(generated.Where(c => c.Collectible).All(c => c.Set == CardSet.VANILLA),
                a.Id + " vs " + b.Id + ": only Classic cards in play");
        }
    }
}
