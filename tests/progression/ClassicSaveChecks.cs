using System.Reflection;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

// Classic 2014 (docs/classic-2014.md): a save's meta is chosen when it is created (ProfileOptions.MetaId) and becomes the
// process's meta when that save opens. A Classic save starts with every Basic Classic card, a Basic deck per class and Classic
// welcome packs, has no Core grants, and its decks are Classic-format decks of VAN cards only. Violet Hold saves are untouched.
internal static class ClassicSaveChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("classic save: " + message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is InvalidOperationException || error is ArgumentException
            || error is IOException || error is TargetInvocationException) { return; }
        throw new Exception("classic save: expected rejection: " + message);
    }

    internal static void Run(string testRoot)
    {
        string root = Path.Combine(testRoot, "classic-save"); Directory.CreateDirectory(root);
        string legacy = Path.Combine(root, "profile.xml");
        Check(ActiveSaveMeta.Current == ActiveSaveMeta.VioletHold, "a process starts in Violet Hold");
        Check(new ProfileOptions().MetaId == ActiveSaveMeta.VioletHold, "a new save's default meta is Violet Hold");
        Reject(() => ProfileCatalog.Create(legacy, "Unknown meta", new ProfileOptions { MetaId = "wild-2027" }), "an unknown meta");
        Reject(() => ActiveSaveMeta.Set("wild-2027"), "an unknown process meta");

        var violet = ProfileCatalog.Create(legacy, "Violet", new ProfileOptions());
        var classic = ProfileCatalog.Create(legacy, "Classic", new ProfileOptions { MetaId = ActiveSaveMeta.Classic });
        Check(ActiveSaveMeta.Current == ActiveSaveMeta.VioletHold, "creating a Classic save leaves the process meta as it was");
        string violetPath = ProfileCatalog.ProfilePathForId(legacy, violet.Id), classicPath = ProfileCatalog.ProfilePathForId(legacy, classic.Id);
        Check(ProfileCatalog.OptionsForProfile(legacy, classicPath).MetaId == ActiveSaveMeta.Classic, "the Classic choice is stored");
        Check(ProfileCatalog.OptionsForProfile(legacy, violetPath).MetaId == ActiveSaveMeta.VioletHold, "the Violet Hold choice is stored");
        var listed = ProfileCatalog.List(legacy);
        Check(listed.Single(slot => slot.Id == classic.Id).MetaId == ActiveSaveMeta.Classic
            && listed.Single(slot => slot.Id == violet.Id).MetaId == ActiveSaveMeta.VioletHold, "the picker lists each save's meta");
        Check(ProfileCatalog.Describe(legacy, classic.Id).MetaId == ActiveSaveMeta.Classic, "the save summary names the meta");
        Check(ActiveSaveMeta.Label(ActiveSaveMeta.Classic) == "Classic — June 2014"
            && ActiveSaveMeta.Label(null) == ProfileCatalog.MetaLabel, "meta labels");

        // The process meta follows the save that opens (SelectedProfile.OpenStartup sets it before the profile loads).
        var basic = LocalProfile.ClassicBasicIds();
        Check(basic.Count >= 100 && basic.All(id => id.StartsWith("VAN_", StringComparison.Ordinal)), "Basic Classic cards: " + basic.Count);
        Check(basic.Select(Cards.FromId).All(card => card.Set == CardSet.VANILLA && card.Rarity == Rarity.FREE && card.Collectible),
            "every Basic Classic card is a collectible FREE VAN card");
        var first = Saved(classicPath, basic);
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        {
            using (var profile = new LocalProfile(classicPath))
            {
                var decks = profile.CustomDecks;
                Check(decks.Count == 9, "a Basic deck per original class: " + decks.Count);
                Check(decks.Select(deck => deck.Hero).Distinct().Count() == 9, "nine different heroes");
                foreach (var deck in decks)
                {
                    Check(deck.Format == LocalProfile.ClassicFormat, deck.Name + " is a Classic deck");
                    Check(deck.Cards.Count == 30 && deck.Cards.All(id => basic.Contains(id)), deck.Name + " holds 30 Basic Classic cards");
                    Check(profile.IsDeckComplete(deck.Id), deck.Name + " can be played: " + profile.NotPlayableReason(deck.Id));
                    Check(profile.WildCards(deck.Id).Count == 0, deck.Name + " has no card outside Classic");
                }
                Check(profile.Gold == LocalWelcomeGift.Gold && profile.DustBalance == LocalWelcomeGift.Dust, "the welcome gold and dust");
                Legality();
                Check(!profile.HasUnredeemedDeckKit, "no unspendable deck kit");
                Check(LocalLadderMilestones.EvaluateCreation(60, 1, MilestoneCatalog.V1).Single(reward => reward.MilestoneId == "floor:gold-10")
                    .Grants.All(grant => grant.Kind != "DeckKit"), "the Gold 10 reward grants no deck kit in a Classic save");
            }
            // Saved and reloaded: a second open reads the same save, unchanged.
            var second = Saved(classicPath, basic);
            Check(second.Collection.Select(card => card.Id + ":" + card.Count).SequenceEqual(first.Collection.Select(card => card.Id + ":" + card.Count))
                && second.CustomDecks.Count == first.CustomDecks.Count, "reloading keeps the collection and the decks");
        }
        Check(ActiveSaveMeta.Current == ActiveSaveMeta.VioletHold, "the meta scope ends");
        Settlement(legacy);
        LegendOne(legacy);

        // A Classic Sandbox save with every option: All cards completes the Classic set, whose collection achievement must
        // be a valid ledger entry (the VANILLA roster), and the save opens again.
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        {
            string sandbox = Path.Combine(root, "classic-sandbox", "profile.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(sandbox));
            var options = new ProfileOptions { MetaId = ActiveSaveMeta.Classic, AllCards = true, AllDecks = true, AllCosmetics = true,
                InfiniteGold = true, InfiniteDust = true };
            using (var profile = new LocalProfile(sandbox, options)) profile.PersistNewProfile(options);
            using (var profile = new LocalProfile(sandbox, options))
                Check(Cards.Classic.All(card => profile.OwnedCount(card.Id, 0) >= 1), "a Classic All-cards save owns every Classic card");
            var data = LocalProfile.ReadFile(sandbox);
            Check(data.Achievements.Unlocks.Any(row => row.Id == "the-complete-works") && data.Achievements.Career.CompletedExpansion == "VANILLA",
                "completing the Classic set earns The Complete Works");
        }

        // A Violet Hold save in the same process: Core cards and no VAN card, exactly as before.
        using (var profile = new LocalProfile(violetPath)) { }
        var vh = LocalProfile.ReadFile(violetPath);
        Check(vh.CoreGrants.Count > 0 && vh.Collection.All(card => !card.Id.StartsWith("VAN_", StringComparison.Ordinal))
            && vh.CustomDecks.Count == 0, "a Violet Hold save keeps its Core start");
        Check(vh.UnopenedPacks.Single().PackType == FairPackRules.Standard, "Violet Hold welcome packs stay Standard");
        Check(vh.DeckKits.Single().Id == LocalWelcomeGift.StandardKitId && vh.Welcome.KitGoldCap == LocalWelcomeGift.StandardKitGoldCap,
            "a Violet Hold save keeps its welcome deck kit");
        Check(LocalLadderMilestones.EvaluateCreation(60, 1, MilestoneCatalog.V1).Single(reward => reward.MilestoneId == "floor:gold-10")
            .Grants.Any(grant => grant.Kind == "DeckKit"), "the Violet Hold Gold 10 reward keeps its deck kit");
        Check(!LocalCardCatalog.InActiveMeta(Cards.FromId(basic[0])) && !LocalCardCatalog.IsStandardId(basic[0]),
            "outside a Classic save a VAN card is neither in the meta nor Standard");
        Console.WriteLine("classic save checks passed: " + basic.Count + " Basic cards, 9 decks");
    }

    // Start Legend #1 (review #5): the world installed at boot is Violet Hold's, so a Classic save takes its #1 rating from
    // the Classic world (LocalProfile.MetaLegendTopRating, which NpcWorldBoot wires); a Violet Hold save keeps the installed one.
    private static void LegendOne(string legacy)
    {
        var previousTop = LocalProfile.NpcLegendTopRating; var previousMeta = LocalProfile.MetaLegendTopRating;
        try
        {
            LocalProfile.NpcLegendTopRating = _ => 1850;
            LocalProfile.MetaLegendTopRating = meta => meta == ActiveSaveMeta.Classic ? 2222 : null;
            var classic = ProfileCatalog.Create(legacy, "Classic Legend", new ProfileOptions { MetaId = ActiveSaveMeta.Classic, StartLegendOne = true });
            var violet = ProfileCatalog.Create(legacy, "Violet Legend", new ProfileOptions { StartLegendOne = true });
            Check(ProfileCatalog.OptionsForProfile(legacy, ProfileCatalog.ProfilePathForId(legacy, classic.Id)).LegendRating == 2222,
                "a Classic Start Legend #1 save takes the Classic world's #1 rating");
            Check(ProfileCatalog.OptionsForProfile(legacy, ProfileCatalog.ProfilePathForId(legacy, violet.Id)).LegendRating == 1850,
                "a Violet Hold Start Legend #1 save keeps the installed world's #1 rating");
            using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
            using (var profile = new LocalProfile(ProfileCatalog.ProfilePathForId(legacy, classic.Id),
                ProfileCatalog.OptionsForProfile(legacy, ProfileCatalog.ProfilePathForId(legacy, classic.Id))))
                Check(profile.Ladder.LegendReached && profile.Ladder.Rating == 2222, "the Classic save opens at that rating");
        }
        finally { LocalProfile.NpcLegendTopRating = previousTop; LocalProfile.MetaLegendTopRating = previousMeta; }
    }

    // A won Classic match settles end to end: gold, ladder, class XP, achievements, quests and card mastery. Classic 2014
    // has no Signature or Diamond card, so mastery offers golden only and never looks up per-meta data that Classic lacks
    // (live: "Unknown mastery meta" refused the result of a won Classic match).
    private static void Settlement(string legacy)
    {
        var created = ProfileCatalog.Create(legacy, "Classic match", new ProfileOptions { MetaId = ActiveSaveMeta.Classic });
        string path = ProfileCatalog.ProfilePathForId(legacy, created.Id);
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        using (var p = new LocalProfile(path))
        {
            Check(!LocalCardPremium.HasSignature("VAN_EX1_116", ActiveSaveMeta.Classic) && !LocalCardPremium.HasDiamond("VAN_EX1_116", ActiveSaveMeta.Classic)
                && !LocalCardPremium.HasSignature("VAN_EX1_116"), "a Classic card has no Signature or Diamond version");
            var deck = p.CustomDecks.First(row => row.Hero == "HERO_08");
            var played = new LocalDeck(deck.Name, deck.Hero, deck.Cards.ToArray());
            Check(deck.Cards.All(id => LocalCardMastery.Tiers(id, 0).All(tier => tier.Premium == 1)), "Classic mastery offers golden only");
            int gold = p.Gold, wins = 0; var grants = new List<LocalMasteryGrant>();
            for (int i = 0; i < 40 && grants.Count == 0; i++)
            {
                var context = p.AchievementContextFor(played, true, "npc-classic", 2500, "Diamond");
                var pending = p.BeginLaunch("custom:" + deck.Id, "priest", i, "npc-classic", "priest", 2500, context, _ => { });
                var frozen = pending.AchievementContext;
                var evidence = new LocalAchievementMatchEvidence(pending.Id, "Won", frozen.StartingHero, frozen.QueuedNpcId, frozen.QueuedNpcBand,
                    false, false, false, LocalAchievementContextBinding.Create(frozen), new string('A', 64), true, 1, 2, 3, 4
#if ENGINE_SOURCE
                    , null
#endif
                    ).Invalidated("No engine facts in the Classic settlement fixture.");
                var match = p.Settle(pending.Id, "Won", 6, false, null, null, null, evidence);
                Check(match.Result == "Won" && p.Pending == null && p.History.Any(row => row.Id == match.Id), "a won Classic match is saved");
                p.AcknowledgeRewardPresentation(match.Id);
                Check(match.Packs.All(pack => pack.PackType == FairPackRules.Classic), "a Classic match reward pack is a Classic pack");
                grants.AddRange(match.MasteryGrants);
                wins++;
            }
            Check(grants.Count > 0 && grants.All(grant => grant.Premium == 1 && grant.CardId.StartsWith("VAN_", StringComparison.Ordinal)),
                "Classic mastery pays golden VAN cards after " + wins + " wins: " + grants.Count);
            Check(p.Gold > gold && p.ClassLevel("HERO_08") > 1, "the wins paid gold and Mage class XP");
        }
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        using (var reopened = new LocalProfile(path))
            Check(reopened.History.Count(row => row.Result == "Won") >= 1, "the settled Classic save opens again");
    }

    // The saved file of a new Classic save: Basic VAN cards, two copies each, no Core grant, Classic welcome packs.
    private static LocalProfileData Saved(string path, IReadOnlyList<string> basic)
    {
        var data = LocalProfile.ReadFile(path);
        Check(data.Collection.Select(card => card.Id).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(basic)
            && data.Collection.All(card => card.Count == 2 && card.Acquired == 2), "the collection is the Basic Classic cards, two each");
        Check(data.PremiumCollection.Count == 0 && data.CoreGrants.Count == 0, "no Core grants and no premium cards");
        Check(data.UnopenedPacks.Count == 1 && data.UnopenedPacks[0].PackType == FairPackRules.Classic
            && data.UnopenedPacks[0].Count == LocalWelcomeGift.Packs, "the welcome packs are Classic packs");
        Check(data.Welcome?.PackType == FairPackRules.Classic && data.Welcome.Cards.All(card => card.Count == 0),
            "the welcome receipt names Classic packs and grants no Violet Hold card");
        // A Classic shop sells no ready-made deck, so a new Classic save holds no deck kit it could never spend (review #1).
        Check(data.DeckKits.Count == 0 && data.Welcome.KitId == null && data.Welcome.KitGoldCap == 0, "a new Classic save has no deck kit");
        return data;
    }

    // Inside a Classic save: "in the meta" and "Standard" both mean a collectible VAN card.
    private static void Legality()
    {
        Check(ActiveSaveMeta.IsClassic && LocalProfile.ActiveDeckFormat == LocalProfile.ClassicFormat, "the deck format is Classic");
        var vanilla = Cards.FromId("VAN_EX1_116"); var modern = Cards.FromId("EX1_116"); var core = LocalCoreProgression.AvailableCards.First();
        Check(LocalCardCatalog.InActiveMeta(vanilla) && LocalCardCatalog.IsStandard(vanilla), "Leeroy (VAN) is in the meta");
        Check(!LocalCardCatalog.InActiveMeta(modern) && !LocalCardCatalog.IsStandard(modern), "today's Leeroy is not");
        Check(!LocalCardCatalog.InActiveMeta(core) && !LocalCardCatalog.IsStandard(core), "Core cards are not");
        Check(LocalCardCatalog.NonStandardIds(new[] { "VAN_EX1_116", "EX1_116", "VAN_CS2_182", "CS2_182" })
            .SequenceEqual(new[] { "EX1_116", "CS2_182" }), "a Classic deck may hold VAN cards only");
        Check(LocalCardCatalog.IsStandardSet((int)CardSet.VANILLA) && !LocalCardCatalog.IsStandardSet((int)CardSet.CORE),
            "the Classic set is the save's one set");
        Check(LocalCardCatalog.ActivePoolIds().All(id => id.StartsWith("VAN_", StringComparison.Ordinal)), "the active pool is Classic");
        Check(LocalShop.IsStandardCard(vanilla) && !LocalShop.IsStandardCard(modern), "shop legality follows the save");
        Check(LocalCrafting.TryGetPrice("VAN_EX1_116", out int craft, out _) && craft == LocalCrafting.LegendaryCraft,
            "Classic cards are crafted at their rarity");
        Check(!LocalCrafting.TryGetPrice("VAN_CS2_182", out _, out _), "Basic cards are not craftable");
        Check(!LocalCrafting.TryGetPrice("EX1_116", out _, out _), "Violet Hold cards are not craftable in a Classic save");
        Check(LocalProfile.KnownLedgerCard("VAN_EX1_116"), "VAN cards are valid collection rows");
    }
}
