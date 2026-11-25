using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using System.Text.RegularExpressions;
using MiniJSON;
using EngineCard = SabberStoneCore.Model.Card;

// Versioned inputs and stable integer draws keep every offer reproducible across runtimes and save reloads.
public sealed class DraftRules
{
    // Version 1: Violet Hold Arena (arena-rules-v1.json). Version 2: Classic 2014 Arena (arena-rules-classic-v1.json,
    // docs/classic-2014.md), the only version a Classic save starts. A run keeps the version it started under.
    public const int VioletHoldVersion = 1, ClassicVersion = 2;
    public static DraftRules Arena(int version = VioletHoldVersion) => version == VioletHoldVersion ? arena.Value
        : version == ClassicVersion ? classicArena.Value
        : throw new InvalidOperationException("Unknown Arena rules version.");
    // The rules a new run in the open save starts under.
    public static int ActiveVersion => ActiveSaveMeta.IsClassic ? ClassicVersion : VioletHoldVersion;
    public static DraftRules Active => Arena(ActiveVersion);
    private static readonly Lazy<DraftRules> arena = new Lazy<DraftRules>(() => new DraftRules(VioletHoldVersion));
    private static readonly Lazy<DraftRules> classicArena = new Lazy<DraftRules>(() => new DraftRules(ClassicVersion));
    public int Version { get; }
    public int EntryGold { get; }
    public int DeckSize { get; }
    public int OfferSize { get; }
    public int MaxWins { get; }
    public int MaxLosses { get; }
    private readonly int classWeight;
    // Violet Hold: pick 1 offers legendaries only and no later pick rolls legendary. Classic 2014: no legendary first pick;
    // legendary is one more rarity in the table.
    private readonly bool firstPickLegendary;
    private readonly string[] heroes;
    private readonly KeyValuePair<Rarity, int>[] rarities;
    private readonly HashSet<string> denyIds;
    private readonly int[][] rewards;
    // Built on the first offer only: reading the rules (View with no run, validation) never touches the card pool.
    // Per save meta (LocalCardCatalog.ActivePoolIds follows it).
    private readonly MetaLazy<EngineCard[]> pool;

    // Tests only: the same rules over a fixed card list (a tiny pool exercises the offer fallback).
    internal static DraftRules WithPool(IEnumerable<EngineCard> cards, int version = VioletHoldVersion) => new DraftRules(version, cards.ToArray());
    // Called once with a message when an offer had to fall back (the plugin logs it); never throws.
    internal static Action<string> FallbackLog;
    internal static bool fallbackLogged;

    private DraftRules(int version, EngineCard[] poolOverride = null)
    {
        string resource = version == ClassicVersion ? "SoloStone.Arena.arena-rules-classic-v1.json" : "SoloStone.Arena.arena-rules-v1.json";
        using (var stream = typeof(DraftRules).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing Arena rules: " + resource))
        {
            var root = EmbeddedJson.Object(EmbeddedJson.Read(stream));
            Version = Required(root, "version"); EntryGold = Required(root, "entryGold");
            DeckSize = Required(root, "deckSize"); OfferSize = Required(root, "offerSize");
            MaxWins = Required(root, "maxWins"); MaxLosses = Required(root, "maxLosses");
            classWeight = Required(root, "classWeight");
            firstPickLegendary = !root.ContainsKey("firstPickLegendary") || EmbeddedJson.Boolean(root, "firstPickLegendary");
            heroes = EmbeddedJson.Array(root["heroes"]).Cast<string>().ToArray();
            denyIds = new HashSet<string>(EmbeddedJson.Array(root["denyIds"]).Cast<string>(), StringComparer.Ordinal);
            rarities = EmbeddedJson.Array(root["rarities"]).Select(value => {
                var row = EmbeddedJson.Object(value);
                return new KeyValuePair<Rarity, int>((Rarity)Enum.Parse(typeof(Rarity), EmbeddedJson.Text(row, "rarity")), Required(row, "weight"));
            }).ToArray();
            rewards = EmbeddedJson.Array(root["rewards"]).Select((value, index) => {
                var row = EmbeddedJson.Object(value);
                if (Required(row, "wins") != index) throw new InvalidOperationException("Invalid Arena reward order.");
                return new[] { Required(row, "packs"), Required(row, "gold"), Required(row, "dust") };
            }).ToArray();
        }
        if (Version != version || EntryGold <= 0 || DeckSize <= 0 || OfferSize < 2 || MaxWins <= 0 || MaxLosses <= 0
            || classWeight <= 0 || heroes.Distinct().Count() != heroes.Length || heroes.Length < OfferSize
            || rarities.Length == 0 || rarities.Any(row => row.Value <= 0 || firstPickLegendary && row.Key == Rarity.LEGENDARY)
            || rewards.Length != MaxWins + 1 || rewards.Any(row => row.Any(value => value < 0)))
            throw new InvalidOperationException("Invalid Arena rules.");
        if (poolOverride != null) { pool = new MetaLazy<EngineCard[]>(() => poolOverride); return; }
        // Classic 2014: the engine's Classic legality (Cards.Classic) whatever the open save; the rules version is the meta.
        pool = version == ClassicVersion
            ? new MetaLazy<EngineCard[]>(() => ClassicPool().Where(Eligible).OrderBy(card => card.Id, StringComparer.Ordinal).ToArray())
            : new MetaLazy<EngineCard[]>(() => LocalCardCatalog.ActivePoolIds().Select(Cards.FromId).Where(LocalCardCatalog.IsStandard).Where(Eligible)
                .OrderBy(card => card.Id, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<EngineCard> ClassicPool()
    {
#if ENGINE_SOURCE
        return Cards.Classic;
#else
        return Enumerable.Empty<EngineCard>();
#endif
    }

    private static int Required(JsonNode row, string key) =>
        EmbeddedJson.Integer(row, key) ?? throw new InvalidOperationException("Missing Arena rule: " + key);

    public bool Eligible(EngineCard card)
    {
        if (card == null || !card.Collectible || !card.Implemented || denyIds.Contains(card.Id)
            || LocalDecks.FabledBundle(card.Id).Count != 0 || LocalDecks.IsFabledCompanion(card.Id)
            || card[(GameTag)2931] > 0 || card[(GameTag)997] > 0 || card[(GameTag)1048] > 0) return false;
#if ENGINE_SOURCE
        if (SabberStoneCore.Actions.DeckbuildingRules.HasSideboard(card.Id)) return false;
#endif
        string text = card.Text ?? "";
        // Printed deck conditions have no single engine tag (highlander, parity, starting-deck replacements).
        string plain = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", ""), @"[\s_]+", " ").ToLowerInvariant();
        if (plain.Contains("your deck has") || plain.Contains("your deck contains") || plain.Contains("your deck had")
            || plain.Contains("your starting deck") || plain.Contains("start of game:") || plain.Contains("start of game,")
            || plain.Contains("you started the game with") || plain.Contains("whizbang")) return false;
        return card.Cost <= 10 || !string.IsNullOrWhiteSpace(text);
    }

    internal IReadOnlyList<EngineCard> Pool => pool.Value;
    public IReadOnlyList<string> Heroes => heroes;
    public IReadOnlyList<string> HeroOffers(long seed) => Draw(heroes.ToList(), seed, 0);

    private static EngineCard KnownCard(string id) { try { return Cards.FromId(id); } catch (KeyNotFoundException) { return null; } }

    public IReadOnlyList<string> Offers(long seed, int slot, string hero, IEnumerable<string> deck)
    {
        var ids = deck.ToArray();
        if (!heroes.Contains(hero) || slot < 1 || slot > DeckSize || ids.Length != slot - 1)
            throw new InvalidOperationException("Invalid draft slot or hero.");
        int decision = StableSeed.Derive(seed, Version, slot, Hash(hero), Hash(string.Join("|", ids)));
        var heroClass = LocalDecks.HeroClass(hero);
        // Runes matter for Death Knight only; a drafted card a later card-data update no longer knows adds none instead of throwing.
        var runes = hero == "HERO_11" ? LocalDecks.RuneCounts(ids.Select(KnownCard).Where(card => card != null)) : new int[3];
        var allowed = pool.Value.Where(card => LocalDecks.IsAllowedInDeck(card, heroClass)
            && (hero != "HERO_11" || Enumerable.Range(0, 3).Sum(i => Math.Max(runes[i], card[(GameTag)(2196 + i)])) <= 3)).ToArray();
        Rarity rarity = Rarity.LEGENDARY;
        if (slot != 1 || !firstPickLegendary)
        {
            int roll = StableSeed.Index(decision, rarities.Sum(row => row.Value));
            foreach (var row in rarities) { roll -= row.Value; if (roll < 0) { rarity = row.Key; break; } }
        }
        var candidates = allowed.Where(card => card.Rarity == rarity || rarity == Rarity.COMMON && card.Rarity == Rarity.FREE).ToList();
        if (candidates.Count < OfferSize) candidates = Fallback(allowed, rarity, slot);
        var result = new List<string>();
        for (int i = 0; i < OfferSize; i++)
        {
            int roll = StableSeed.Index(StableSeed.Derive(decision, i), candidates.Sum(card => ClassWeight(card)));
            var chosen = candidates[0];
            foreach (var card in candidates) { roll -= ClassWeight(card); if (roll < 0) { chosen = card; break; } }
            result.Add(chosen.Id); candidates.Remove(chosen);
        }
        return result.AsReadOnly();
    }

    // A future pool change can leave a rarity bucket (or the legendary first pick) under OfferSize. The offer never fails: it widens
    // deterministically to the other rarities in table order (legendary last, so a later pick gets one only when nothing else fills it),
    // then to every eligible card by id. Only a pool with fewer eligible cards than an offer is unrecoverable.
    private List<EngineCard> Fallback(EngineCard[] allowed, Rarity rarity, int slot)
    {
        var order = new List<Rarity> { rarity };
        order.AddRange(rarities.Select(row => row.Key).Where(key => key != rarity));
        if (!order.Contains(Rarity.LEGENDARY)) order.Add(Rarity.LEGENDARY);
        var result = new List<EngineCard>();
        foreach (var next in order) {
            result.AddRange(allowed.Where(card => (card.Rarity == next || next == Rarity.COMMON && card.Rarity == Rarity.FREE) && !result.Contains(card)));
            if (result.Count >= OfferSize) break;
        }
        if (result.Count < OfferSize) result = allowed.OrderBy(card => card.Id, StringComparer.Ordinal).ToList();
        if (result.Count < OfferSize) throw new InvalidOperationException("Draft pool cannot fill an offer.");
        if (!fallbackLogged) { fallbackLogged = true; FallbackLog?.Invoke("Arena offer for slot " + slot + " fell back: fewer than " + OfferSize + " " + rarity + " cards in the pool."); }
        return result;
    }

    private int ClassWeight(EngineCard card) => card.Class == CardClass.NEUTRAL && card.MultiClassGroup == 0
        && card[(GameTag)476] == 0 ? 1 : classWeight;
    private IReadOnlyList<string> Draw(List<string> choices, long seed, int slot)
    {
        var result = new List<string>();
        for (int i = 0; i < OfferSize; i++) {
            int index = StableSeed.Index(StableSeed.Derive(seed, Version, slot, i), choices.Count);
            result.Add(choices[index]); choices.RemoveAt(index);
        }
        return result.AsReadOnly();
    }
    private static long Hash(string value)
    {
        unchecked { ulong hash = 14695981039346656037UL; foreach (char c in value) { hash ^= c; hash *= 1099511628211UL; } return (long)hash; }
    }

    // The reward for every win count at one pace, for the native rewards list (season reward levels). Preview only: the paid
    // reward is computed at the end of the run with the run's own pack set.
    public IReadOnlyList<ArenaReward> RewardTable(string tierId, string packType)
    {
        string key = tierId + "|" + packType;
        lock (rewardTables)
        {
            if (!rewardTables.TryGetValue(key, out var table))
                rewardTables[key] = table = Enumerable.Range(0, MaxWins + 1).Select(wins => Reward(null, wins, tierId, packType)).ToList().AsReadOnly();
            return table;
        }
    }
    private readonly Dictionary<string, IReadOnlyList<ArenaReward>> rewardTables = new Dictionary<string, IReadOnlyList<ArenaReward>>();
    internal static string PreviewPackType => FairPackRules.PackTypes.Contains(FairPackRules.Standard) ? FairPackRules.Standard : FairPackRules.PackTypes.FirstOrDefault();

    public ArenaReward Reward(string runId, int wins, string tierId, string packType)
    {
        if (wins < 0 || wins > MaxWins || !FairPackRules.PackTypes.Contains(packType))
            throw new InvalidOperationException("Invalid Arena reward inputs.");
        var row = rewards[wins]; var tier = GrindTiers.Find(tierId);
        return new ArenaReward { RunId = runId, Wins = wins, Gold = tier.Gold(row[1]), Dust = tier.Dust(row[2]),
            PackTypes = Enumerable.Repeat(packType, row[0]).ToList() };
    }
}

public sealed class DraftBotResult
{
    public string Hero;
    public List<string> Deck = new List<string>();
    public double Quality;
}

public static class DraftBot
{
    // ratings: optional 0-100 Arena ratings by card id (data/arena/draft-ratings-classic-v1.json); a rated card is scored by its
    // rating, any other card by Power's text heuristic.
    public static DraftBotResult Draft(long seed, double skill, DraftRules rules = null, IReadOnlyDictionary<string, int> ratings = null)
    {
        if (double.IsNaN(skill) || skill < 0 || skill > 1) throw new ArgumentOutOfRangeException(nameof(skill));
        rules = rules ?? DraftRules.Arena();
        var result = new DraftBotResult { Hero = rules.HeroOffers(seed)[0] };
        for (int slot = 1; slot <= rules.DeckSize; slot++)
        {
            var choices = rules.Offers(seed, slot, result.Hero, result.Deck)
                .Select(id => new { Id = id, Score = Score(Cards.FromId(id), result.Deck, ratings) })
                .OrderByDescending(row => row.Score).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
            int index = StableSeed.Index(StableSeed.Derive(seed, slot, 0x424f54), 10000) < (1 - skill) * 8000 ? 1 : 0;
            result.Deck.Add(choices[index].Id); result.Quality += choices[index].Score;
        }
        result.Quality /= rules.DeckSize;
        return result;
    }

    // A card's draft value plus a curve term. Only the NPC deck generator (tests/progression/ArenaDeckGenerator.cs) calls it.
    public static double Score(EngineCard card, IEnumerable<string> deck, IReadOnlyDictionary<string, int> ratings = null)
    {
        var cards = deck.Select(Cards.FromId).ToArray();
        int bucket = Math.Min(7, card.Cost);
        double[] target = { 1, 3, 6, 6, 5, 4, 2, 3 };
        // A rating maps onto Power's scale: 50 (filler) is 2.25, 90 (premium) 3.25.
        double power = ratings != null && ratings.TryGetValue(card.Id, out int rating) ? 1.0 + rating / 40.0 : Power(card);
        return power - 0.4 * (cards.Count(c => Math.Min(7, c.Cost) == bucket) - target[bucket] * cards.Length / 30.0);
    }

    // How strong a card is in Arena, on a scale where a vanilla on-curve minion is about 2 and premium removal about 3. A rough
    // reading of stats and printed text: stats against the vanilla curve (2 x cost + 1), what the text does (removal, area
    // damage, card draw, summons), and its drawbacks (can't attack, self-damage, overload, discard, gifts to the opponent).
    // Before 2026-11-24 it was stats per mana plus a flat keyword bonus, which drafted Wisp and Ancient Watcher over Fireball.
    public static double Power(EngineCard card)
    {
        string text = Plain(card.Text);
        int cost = Math.Max(0, card.Cost);
        if (Regex.IsMatch(text, @"costs \(\d+\) less")) cost = Math.Min(cost, 9);   // the giants are cast below their printed cost
        int curveCost = Math.Min(7, cost);
        double value;
        if (card.Type == CardType.MINION)
        {
            double attack = card.ATK, health = card.Health;
            Match self = Regex.Match(text, @"deal (\d+) damage to himself");
            if (self.Success) health = Math.Max(1, health - Number(self.Groups[1].Value));
            // Can't attack: the attack counts only when the text deals damage on its own (Ragnaros), not for Ancient Watcher.
            if (text.Contains("can't attack") && !text.Contains("deal")) attack = 0;
            if (text.Contains("attack is always equal to its health")) attack = health;
            if (text.Contains("divine shield")) health += Math.Min(attack, 3);   // the shield soaks about one hit
            double expected = cost == 0 ? 2 : 2 * cost + 1;
            // Doomsayer and Deathwing clear the board: their body is not the point.
            value = text.Contains("destroy all") ? 1.8 + 0.15 * curveCost
                : 1.8 + 0.25 * (attack + health - expected) + 0.15 * curveCost - 0.08 * Math.Abs(attack - health);
            if (attack == 0) value -= 0.5;
            double keywordScale = 0.1 + 0.05 * curveCost;
            if (card.Taunt || text.Contains("taunt")) value += 1.2 * keywordScale;
            if (text.Contains("charge")) value += 0.2;
            if (text.Contains("windfury")) value += 0.15;
            if (text.Contains("stealth")) value += 0.2;
            if (text.Contains("poisonous")) value += 0.4;
            if (text.Contains("spell damage")) value += 0.15;
            value += 0.6 * Effects(text);
        }
        else if (card.Type == CardType.WEAPON)
        {
            double attack = card.ATK, durability = card[GameTag.DURABILITY], swings = text.Contains("windfury") ? 2 : 1;
            value = 1.6 + Math.Min(1.2, 0.2 * (attack * durability * swings - 2 * cost)) + 0.15 * (attack - 2) + 0.12 * curveCost
                + 0.6 * Effects(text);
        }
        else
        {
            double effects = Effects(text);
            value = 1.3 + (effects > 0 ? effects : 0.5);
            if (text.Contains("secret")) value += 0.2;
        }
        if (card.Class != CardClass.NEUTRAL) value += 0.15;
        value -= Drawbacks(text);
        return value;
    }

    private static string Plain(string text) => Regex.Replace(Regex.Replace(Regex.Replace(text ?? "", "<[^>]+>|\\[x\\]", ""),
        @"[$#]", ""), @"[\s_]+", " ").ToLowerInvariant();
    private static double Number(string text) => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal)
        { ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4 };

    // What the text does for its owner, in the same units as Power.
    private static double Effects(string text)
    {
        double value = 0;
        Match damage = Regex.Match(text, @"deal (\d+) damage( randomly split)?(.{0,40})");
        if (damage.Success && !damage.Groups[3].Value.Contains("your hero") && !damage.Groups[3].Value.Contains("himself"))
        {
            double amount = Number(damage.Groups[1].Value);
            string target = damage.Groups[3].Value;
            if (damage.Groups[2].Success) value += 0.5 + 0.06 * amount;
            else if (target.Contains("all enemy minions") || target.Contains("all enemies")) value += 0.6 + 0.25 * amount;
            else if (target.Contains("all minions") || target.Contains("all characters") || target.Contains("all other")) value += 0.4 + 0.15 * amount;
            else if (target.Contains("random")) value += 0.3 + 0.06 * Math.Min(6, amount);
            else if (target.Contains("enemy hero")) value += 0.2 + 0.05 * amount;
            else value += 0.6 + 0.12 * Math.Min(6, amount);
        }
        else if (text.Contains("deal damage") || text.Contains("deal its attack damage")) value += 0.8;
        if (Regex.IsMatch(text, @"destroy (a|an|one) (random )?(enemy )?minion|destroy a damaged|destroy (a|an) (random )?enemy")) value += 1.3;
        else if (text.Contains("destroy all minions") || text.Contains("destroy all other minions")) value += 1.3;
        if (text.Contains("transform")) value += 1.4;
        if (text.Contains("take control")) value += text.Contains("if your opponent has") ? 0.5 : 1.2;
        if (text.Contains("return") && text.Contains("hand")) value += 0.6;
        if (text.Contains("silence")) value += 0.3;
        if (text.Contains("freeze")) value += 0.2;
        Match draw = Regex.Match(text, @"draw (\d+|a|two|three|four) cards?");
        if (draw.Success) value += 0.3 * (Counts.TryGetValue(draw.Groups[1].Value, out int n) ? n : Number(draw.Groups[1].Value));
        else if (text.Contains("draw")) value += 0.3;
        if (text.Contains("discover")) value += 0.4;
        foreach (Match summon in Regex.Matches(text, @"summon (a|an|one|two|three|four|\d+) (\d+)/(\d+)( [a-z' ]{0,30}for your opponent)?"))
        {
            if (summon.Groups[4].Success) continue;   // The Beast's Pip Quickwit is a drawback, not a summon

            double count = Counts.TryGetValue(summon.Groups[1].Value, out int c) ? c : Number(summon.Groups[1].Value);
            value += 0.15 * count * (Number(summon.Groups[2].Value) + Number(summon.Groups[3].Value));
        }
        if (!Regex.IsMatch(text, @"summon (a|an|one|two|three|four|\d+) \d+/\d+") && Regex.IsMatch(text, @"summon (a|two|three|random)")) value += 0.5;
        Match buff = Regex.Match(text, @"\+(\d+)/\+(\d+)");
        if (buff.Success) value += Math.Min(1.0, 0.1 * (Number(buff.Groups[1].Value) + Number(buff.Groups[2].Value)));
        else if (Regex.IsMatch(text, @"\+(\d+) (attack|health)")) value += 0.25;
        Match heal = Regex.Match(text, @"restore (\d+) health");
        if (heal.Success) value += Math.Min(0.4, 0.03 * Number(heal.Groups[1].Value));
        Match armor = Regex.Match(text, @"gain (\d+) armor");
        if (armor.Success) value += Math.Min(0.5, 0.05 * Number(armor.Groups[1].Value));
        if (text.Contains("mana crystal") && !text.Contains("destroy")) value += 0.4;
        if (text.Contains("lifesteal")) value += 0.2;
        if (text.Contains("rush")) value += 0.2;
        return value;
    }

    // What the text costs its owner or gives the opponent.
    private static double Drawbacks(string text)
    {
        double value = 0;
        Match overload = Regex.Match(text, @"overload:? \((\d+)\)");
        if (overload.Success) value += 0.2 * Number(overload.Groups[1].Value);
        Match hero = Regex.Match(text, @"deal (\d+) damage to your hero");
        if (hero.Success) value += 0.05 * Number(hero.Groups[1].Value);
        if (text.Contains("discard your hand")) value += 1.5;
        else if (text.Contains("discard")) value += 0.5;
        if (text.Contains("destroy one of your mana crystals") || text.Contains("destroy a friendly")) value += 0.5;
        if (text.Contains("give your opponent") || text.Contains("for your opponent") || text.Contains("enemy spells cost")) value += 0.6;
        if (text.Contains("your opponent draws")) value += 0.8;
        if (Regex.IsMatch(text, @"cost \(\d+\) more")) value += 1.0;
        if (text.Contains("can't attack")) value += 0.2;
        return value;
    }
}
