using SabberStoneCore.Enums;
using SabberStoneCore.Model;

// Classic 2014 (docs/classic-2014.md): a Classic save's shop sells one card pack, the Classic pack: gold only, five cards at the
// fair-pack rarities, drawn from the collectible non-Basic VAN cards. Violet Hold's shop is unchanged in the same process.
internal static class ClassicPackTests
{
    internal static void Run(Action<bool, string> check, Action<Action, string> reject, string root)
    {
        string legacy = Path.Combine(root, "classic-shop", "profile.xml"); Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var violetPacks = LocalShop.Default.Products.Where(p => p.Kind == "Pack").Select(p => p.Id).ToArray();
        check(violetPacks.Contains("pack-standard") && !violetPacks.Contains(LocalShop.ClassicPackProductId), "Violet Hold sells its own packs");
        var slot = ProfileCatalog.Create(legacy, "Classic shop", new ProfileOptions { MetaId = ActiveSaveMeta.Classic });
        string path = ProfileCatalog.ProfilePathForId(legacy, slot.Id);
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        {
            // The pool: every collectible, implemented VAN card that is not Basic, pinned by hash (a new pool needs a new rule).
            var pool = FairPackRules.Eligible(FairPackRules.Classic, FairPackRules.RuleVersion);
            check(FairPackRules.PackTypes.SequenceEqual(new[] { FairPackRules.Classic }), "a Classic save has one pack type");
            check(FairPackRules.MainPackType == FairPackRules.Classic && FairPackRules.NewestSet == FairPackRules.Classic, "rewards grant Classic packs");
            check(FairPackRules.NativeBoosterId(FairPackRules.Classic) == 1, "the Classic pack opens the client's Classic booster");
            check(pool.Count == 253 && pool.All(card => card.Set == CardSet.VANILLA && card.Collectible && card.Implemented && card.Rarity != Rarity.FREE),
                "the Classic pool is the 253 non-Basic VAN cards: " + pool.Count);
            check(pool.Select(card => card.Id).SequenceEqual(Cards.Classic.Where(card => card.Rarity != Rarity.FREE).Select(card => card.Id)
                .OrderBy(id => id, StringComparer.Ordinal)), "the pool holds every non-Basic Classic card");
            check(FairPackRules.ClassicPoolDigest(pool) == FairPackRules.ClassicPoolHash, "the pool hash is pinned");
            foreach (var rarity in new[] { Rarity.COMMON, Rarity.RARE, Rarity.EPIC, Rarity.LEGENDARY })
                check(pool.Any(card => card.Rarity == rarity), "the pool has " + rarity + " cards");

            // The shop: the Classic pack alone, gold only, no Violet Hold pack, recipe or ready-made deck; cosmetics stay.
            var shop = LocalShop.Default;
            var packs = shop.Products.Where(p => p.Kind == "Pack").ToArray();
            check(packs.Length == 1 && packs[0].Id == LocalShop.ClassicPackProductId && packs[0].PackType == FairPackRules.Classic
                && packs[0].Price == LocalShop.ClassicPackPrice && packs[0].MaxQuantity == 50, "the Classic pack is the only pack");
            check(!shop.Products.Any(p => p.Kind == "Recipe" || p.Kind == "Deck") && shop.Recipes.Count == 0, "no Violet Hold recipe or deck");
            check(shop.Products.Any(p => p.Kind == "CardBack"), "cosmetics are still sold");

            using (var profile = new LocalProfile(path))
            {
                int gold = profile.Gold;
                profile.Purchase(LocalShop.ClassicPackProductId, 2, 2 * LocalShop.ClassicPackPrice, Guid.NewGuid().ToString());
                check(profile.Gold == gold - 2 * LocalShop.ClassicPackPrice, "two Classic packs cost 200 gold");
                reject(() => profile.Purchase("pack-standard", 1, 100, Guid.NewGuid().ToString()), "a Violet Hold pack cannot be bought");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < 12; i++)
                {
                    var pack = profile.OpenPack(FairPackRules.Classic);
                    check(pack.PackType == FairPackRules.Classic && pack.Cards.Count == 5, "a Classic pack holds five cards");
                    var cards = pack.Cards.Select(card => Cards.FromId(card.Id)).ToArray();
                    check(cards.All(card => card.Set == CardSet.VANILLA && card.Rarity != Rarity.FREE), "only non-Basic Classic cards");
                    check(cards.Any(card => card.Rarity != Rarity.COMMON), "Rare or better in every pack");
                    foreach (var card in pack.Cards) seen.Add(card.Id);
                }
                check(seen.Count > 20, "packs spread over the pool: " + seen.Count);
                var data = LocalProfile.ReadFile(path);
                check(data.Collection.Concat(data.PremiumCollection).All(card => card.Id.StartsWith("VAN_", StringComparison.Ordinal)),
                    "the collection stays Classic");
                check(data.Purchases.Single().ProductId == LocalShop.ClassicPackProductId, "the purchase receipt names the Classic pack");
            }
            // Reopened, the packs, receipts and counters validate again.
            using (var profile = new LocalProfile(path))
                check(profile.Gold >= 0, "the Classic save reloads after packs");
        }
        check(LocalShop.Default.Products.Any(p => p.Id == "pack-standard") && FairPackRules.PackTypes.Contains(FairPackRules.Standard),
            "the Violet Hold shop is back when the Classic scope ends");
        Console.WriteLine("classic pack tests passed");
    }
}
