using SabberStoneCore.Enums;
using SabberStoneCore.Model;

// Classic 2014 Arena (docs/classic-2014.md): a Classic save starts runs under rules version 2 (arena-rules-classic-v1.json):
// the 9 original classes, 30 picks of 3 Classic-format cards, no legendary first pick, 12 wins or 3 losses, the Arena reward
// table paid in Classic packs, and opponents drafted under the same rules (data/npc/arena-classic-decks-v1.json).
// Violet Hold Arena (version 1) is unchanged.
internal static class ClassicArenaChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("Classic Arena: " + message); }
    private static void Ok(ArenaView view) => Check(view.Error == null, view.Error ?? "");

    internal static void Run(string root)
    {
        var violet = DraftRules.Arena(DraftRules.VioletHoldVersion);
        Check(DraftRules.ActiveVersion == DraftRules.VioletHoldVersion && violet.Heroes.Count == 11 && violet.Version == 1,
            "a Violet Hold save keeps rules version 1 with its 11 heroes");
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        {
            var rules = DraftRules.Active;
            Check(DraftRules.ActiveVersion == DraftRules.ClassicVersion && rules.Version == 2, "a Classic save starts version 2");
            Check(rules.Heroes.SequenceEqual(Enumerable.Range(1, 9).Select(i => "HERO_0" + i)), "the 9 original classes");
            Check(rules.DeckSize == 30 && rules.OfferSize == 3 && rules.MaxWins == 12 && rules.MaxLosses == 3 && rules.EntryGold == 150,
                "30 picks of 3, 12 wins or 3 losses, 150 gold");
            Check(rules.Pool.Count > 200 && rules.Pool.All(card => card.Set == CardSet.VANILLA
                && Cards.IsFormatLegal(card, FormatType.FT_CLASSIC)), "the pool is the engine's Classic legality: " + rules.Pool.Count);
            Check(rules.Pool.Select(card => card.Id).ToHashSet().SetEquals(Cards.Classic.Where(rules.Eligible).Select(card => card.Id)),
                "the pool is every eligible Cards.Classic card");
            Offers(rules);
            Run(root, rules);
            Decks(rules);
        }
        Check(DraftRules.ActiveVersion == DraftRules.VioletHoldVersion, "the meta scope ends");
        Console.WriteLine("PASS Classic Arena: version 2 rules, 9 classes, Classic-only offers, no legendary first pick, Classic pack rewards, 200 Classic NPC drafts current");
    }

    private static void Offers(DraftRules rules)
    {
        int firstLegendary = 0, laterLegendary = 0, firstPicks = 0;
        for (long seed = 1; seed <= 300; seed++)
        {
            string hero = rules.HeroOffers(seed)[0];
            var heroClass = LocalDecks.HeroClass(hero);
            var deck = new List<string>();
            for (int slot = 1; slot <= rules.DeckSize; slot++)
            {
                var offer = rules.Offers(seed, slot, hero, deck);
                var cards = offer.Select(Cards.FromId).ToList();
                Check(offer.Distinct().Count() == 3 && cards.All(card => card.Set == CardSet.VANILLA && LocalDecks.IsAllowedInDeck(card, heroClass)),
                    "offer " + seed + "/" + slot + " is 3 Classic cards of the class or neutral");
                Check(cards.Select(card => card.Rarity == Rarity.FREE ? Rarity.COMMON : card.Rarity).Distinct().Count() == 1,
                    "offer " + seed + "/" + slot + " is one rarity");
                bool legendary = cards[0].Rarity == Rarity.LEGENDARY;
                if (slot == 1) { firstPicks++; if (legendary) firstLegendary++; } else if (legendary) laterLegendary++;
                deck.Add(offer[(int)(seed % 3)]);
                if (seed > 20) break;   // first picks only after the first 20 full drafts
            }
        }
        Check(firstLegendary < firstPicks / 4, "the first pick is not a legendary pick: " + firstLegendary + "/" + firstPicks);
        Check(laterLegendary > 0, "a legendary pick can come at any slot");
    }

    private static void Run(string root, DraftRules rules)
    {
        // A real Classic save (created like the Create save form does), not a bare profile file.
        string legacy = Path.Combine(root, "classic-arena", "profile.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var save = ProfileCatalog.Create(legacy, "Classic Arena", new ProfileOptions { MetaId = ActiveSaveMeta.Classic });
        string path = ProfileCatalog.ProfilePathForId(legacy, save.Id);
        using (var profile = new LocalProfile(path)) Play(profile, rules);
        using (new LocalProfile(path)) { }   // the receipt of a Classic run validates on reload
    }

    private static void Play(LocalProfile profile, DraftRules rules)
    {
        profile.CommitChange(next => { next.GoldBalance = 1000; return true; });
        var service = new ArenaService(profile, () => true);
        var none = service.View();
        Check(none.MaxWins == 12 && none.MaxLosses == 3 && none.MaxSlot == 30 && none.EntryCost == 150, "the empty view shows Classic rules");
        Check(none.RewardTable.Count == 13 && none.RewardTable.All(row => row.PackTypes.All(pack => pack == FairPackRules.Classic)),
            "the reward preview pays Classic packs");
        Ok(service.Begin("classic-entry"));
        var run = profile.ArenaRun;
        Check(run.RulesVersion == DraftRules.ClassicVersion && run.HeroOffers.All(rules.Heroes.Contains), "the run is a Classic run");
        Ok(service.Choose(0, 0));
        for (int slot = 1; slot <= 30; slot++)
        {
            Check(service.View().Offers.Select(Cards.FromId).All(card => card.Set == CardSet.VANILLA), "pick " + slot + " offers Classic cards");
            Ok(service.Choose(slot, slot % 3));
        }
        Check(service.View().Phase == ArenaPhase.Ready && service.View().Deck.Count == 30, "30 picks make a Ready run");
        profile.CommitChange(next => { LocalProfile.SettleArenaMatch(next, "Won"); LocalProfile.SettleArenaMatch(next, "Lost"); return true; });
        Ok(service.Retire());
        var reward = service.View().Reward;
        Check(reward.Wins == 1 && reward.PackTypes.Count == 1 && reward.PackTypes.All(pack => pack == FairPackRules.Classic),
            "the reward follows the Arena table in Classic packs");
        Ok(service.AcknowledgeReward(reward.RunId));
    }

    private static void Decks(DraftRules rules)
    {
        string json = ArenaDeckGenerator.Json(true);
        string repo = RepoRoot.Find("data/npc/arena-classic-decks-v1.json") ?? throw new Exception("Classic Arena: data/npc/arena-classic-decks-v1.json not found");
        Check(File.ReadAllBytes(Path.Combine(repo, "data/npc/arena-classic-decks-v1.json")).SequenceEqual(new System.Text.UTF8Encoding(false).GetBytes(json)),
            "data/npc/arena-classic-decks-v1.json is stale: regenerate with ProgressionTests --gen-arena-classic-decks data/npc/arena-classic-decks-v1.json");
        // The npc-world suite loads the file as an ArenaDeckCatalog; here its rows are checked against the Classic rules.
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var file = parsed.RootElement;
        var decks = file.GetProperty("decks").EnumerateArray().ToList();
        Check(file.GetProperty("activeMeta").GetString() == ActiveSaveMeta.Classic && decks.Count == ArenaDeckGenerator.DeckCount
            && decks.All(deck => deck.GetProperty("id").GetString()!.StartsWith("npc-arena-classic-", StringComparison.Ordinal)
                && deck.GetProperty("admission").GetString() == "arena" && deck.GetProperty("rulesVersion").GetInt32() == 2), "200 Classic Arena decks");
        var heroes = decks.Select(deck => deck.GetProperty("hero").GetString()!).Distinct().ToList();
        Check(heroes.All(rules.Heroes.Contains) && heroes.Count == 9, "every original class and no other");
        foreach (var deck in decks)
        {
            string hero = deck.GetProperty("hero").GetString()!;
            var cards = deck.GetProperty("cards").EnumerateArray()
                .SelectMany(card => Enumerable.Repeat(Cards.FromId(card.GetProperty("id").GetString()), card.GetProperty("count").GetInt32())).ToList();
            Check(cards.Count == 30 && cards.All(card => Cards.IsFormatLegal(card, FormatType.FT_CLASSIC)
                && LocalDecks.IsAllowedInDeck(card, LocalDecks.HeroClass(hero))), "deck " + deck.GetProperty("id").GetString() + " is 30 Classic cards of its class");
        }
    }
}
