using SabberStoneCore.CardSets.Client;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.RandomPools;
using SabberStoneCore.Tasks.PlayerTasks;
using static Core2026Proof;

// docs/classic-2014.md: the Classic format's cards (VAN_*, facts/classic-2014.json). The structure (every collectible
// card loaded, implemented, legal in Classic only, with its original's behaviour), the classic random pool, and one
// played scenario per classic script (ClassicScripts), each checking what differs from the original card.
internal static class ClassicFormatProof
{
    private static void Check(bool condition, string message) => Core2026Proof.Check(condition, "classic 2014: " + message);

    internal static void Run()
    {
        Structure();
        Pool();
        Scripts();
        RandomLists();
        Console.WriteLine("PASS classic 2014: 382 collectible VAN cards, Classic-only legality, classic random pool, " +
            ClassicScripts.Ids.Count + " classic scripts played");
    }

    private static Game NewGame(int seed = 7)
    {
        var deck = Enumerable.Repeat(Cards.FromId("VAN_CS2_182"), 30).ToList();
        var game = new Game(new GameConfig {
            RandomSeed = seed, StartPlayer = 1, SkipMulligan = true, Shuffle = false, History = true, Logging = false,
            FormatType = FormatType.FT_CLASSIC, PoolFormat = GenerationFormat.Classic,
            Player1HeroClass = CardClass.WARRIOR, Player2HeroClass = CardClass.HUNTER,
            Player1Deck = deck, Player2Deck = deck.ToList() });
        game.StartGame();
        game.Player1.BaseMana = 10; game.Player2.BaseMana = 10;
        return game;
    }

    private static void Structure()
    {
        var van = Cards.All.Where(c => c.Set == CardSet.VANILLA).ToArray();
        var collectible = van.Where(c => c.Collectible).ToArray();
        Check(collectible.Length == 382, "382 collectible VAN cards, found " + collectible.Length);
        Check(collectible.All(c => c.Implemented && c.Id.StartsWith("VAN_", StringComparison.Ordinal)), "every collectible VAN card is implemented");
        Check(Cards.Classic.Count == 382 && Cards.Classic.All(c => c.Set == CardSet.VANILLA), "Classic legality is exactly the VAN cards");
        Check(!Cards.All.Any(c => c.Set != CardSet.VANILLA && Cards.IsFormatLegal(c, FormatType.FT_CLASSIC)), "no other card is Classic-legal");
        Check(!collectible.Any(c => Cards.IsFormatLegal(c, FormatType.FT_WILD) || Cards.IsFormatLegal(c, FormatType.FT_STANDARD)),
            "no VAN card is Wild- or Standard-legal");
        Check(collectible.All(c => Classic2014.ByDbfId.ContainsKey(c.AssetId)), "every collectible VAN card has an original");
        Check(collectible.Select(c => c.Class).Distinct().All(c => c == CardClass.NEUTRAL || (int)c <= 10 && c != CardClass.DEATHKNIGHT),
            "only the nine original classes");
        // June 2014 values, each different from today's original.
        void Stats(string id, int cost, int atk = -1, int health = -1)
        {
            var card = Cards.FromId(id);
            Check(card.Cost == cost && (atk < 0 || card.ATK == atk) && (health < 0 || card.Health == health),
                id + " 2014 values " + card.Cost + " " + card.ATK + "/" + card.Health);
        }
        Stats("VAN_EX1_116", 4, 6, 2);     // Leeroy Jenkins
        Stats("VAN_NEW1_019", 2, 3, 2);    // Knife Juggler
        Stats("VAN_EX1_005", 3, 4, 2);     // Big Game Hunter
        Stats("VAN_CS2_203", 2, 2, 1);     // Ironbeak Owl
        Stats("VAN_CS2_084", 0);           // Hunter's Mark
        Stats("VAN_EX1_095", 5, 4, 4);     // Gadgetzan Auctioneer
        Stats("VAN_EX1_089", 3, 4, 2);     // Arcane Golem
        Stats("VAN_CS2_106", 2, 3);        // Fiery War Axe
        Check(Cards.FromId("VAN_CS2_106")[GameTag.DURABILITY] == 2, "Fiery War Axe durability 2");
        Check(Cards.FromId("VAN_EX1_089")[GameTag.CHARGE] == 1, "Arcane Golem has Charge");
        Check(Cards.FromId("VAN_EX1_248")[GameTag.OVERLOAD] == 2, "Feral Spirit Overload (2)");
        Check(Cards.FromId("VAN_EX1_563")[GameTag.SPELLPOWER] == 5, "Malygos Spell Damage +5");
        Check(Cards.FromId("VAN_NEW1_023")[GameTag.CANT_BE_TARGETED_BY_SPELLS] == 1
            && Cards.FromId("VAN_NEW1_023")[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] == 1, "Faerie Dragon Elusive");
        Check(Cards.FromId("VAN_EX1_170")[GameTag.POISONOUS] == 1, "Emperor Cobra destroys what it damages");
        Check(ReferenceEquals(Cards.FromId("VAN_EX1_572").Power, Cards.FromId("EX1_572").Power), "Ysera, the older VAN row, plays as Ysera");
    }

    private static void Pool()
    {
        var game = NewGame();
        foreach (var destination in new[] { PoolDestination.Hand, PoolDestination.Summon, PoolDestination.Other })
        {
            var pool = Cards.RandomPool(game, destination);
            Check(pool.Format == GenerationFormat.Classic && !pool.Legacy, "classic pool keyed by PoolFormat");
            Check(pool.All.Count > 300 && pool.All.All(c => c.Set == CardSet.VANILLA), destination + ": classic pool holds VAN cards only");
        }
        Check(Cards.RandomPool(game).ClassAndNeutral(CardClass.WARLOCK).Count(c => c.IsRace(Race.DEMON)) >= 5, "classic Demons for Bane of Doom");
        var standard = new Game(new GameConfig { FormatType = FormatType.FT_WILD, PoolFormat = GenerationFormat.Standard,
            Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE, Shuffle = false, SkipMulligan = true, Logging = false,
            Player1Deck = Enumerable.Repeat(Cards.FromId("CS2_179"), 30).ToList(), Player2Deck = Enumerable.Repeat(Cards.FromId("CS2_179"), 30).ToList() });
        standard.StartGame();
        Check(!Cards.RandomPool(standard).All.Any(c => c.Set == CardSet.VANILLA), "a Standard pool never holds a VAN card");
        // The legacy (pinned Standard/Wild) pools have no Classic version: a legacy replay of a Classic game fails loudly.
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        foreach (var flags in new[] { (true, false), (false, true) })
        {
            var legacy = new Game(new GameConfig { FormatType = FormatType.FT_CLASSIC, PoolFormat = GenerationFormat.Classic,
                LegacyPoolReplay = flags.Item1, UnversionedRandomPoolReplay = flags.Item2,
                Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE, Shuffle = false, SkipMulligan = true, Logging = false,
                Player1Deck = Enumerable.Repeat(Cards.FromId("VAN_CS2_182"), 30).ToList(), Player2Deck = Enumerable.Repeat(Cards.FromId("VAN_CS2_182"), 30).ToList() });
            Check(Throws(() => Cards.RandomPool(legacy)), "a legacy random pool refuses a Classic game");
        }
        Check(Throws(() => Cards.FormatTypeClassCards(FormatType.FT_CLASSIC)) && Throws(() => Cards.CostMinionCards(FormatType.FT_CLASSIC))
            && Throws(() => LegacyGenerationV1.Format(FormatType.FT_CLASSIC)), "the legacy Standard/Wild lists refuse FT_CLASSIC");
    }

    private static void Scripts()
    {
        var played = new HashSet<string>(StringComparer.Ordinal);
        void Mark(params string[] ids) { foreach (var id in ids) played.Add(id); }

        { // Charge: +2 Attack and Charge, and it may attack the hero.
            var game = NewGame(); var p = game.Player1;
            var m = Summon(p, "VAN_CS2_168"); // Murloc Raider 2/1
            Play(game, p, "VAN_CS2_103", m);
            Check(m.AttackDamage == 4 && m.HasCharge && m.ValidAttackTargets.Contains(p.Opponent.Hero), "Charge: +2 Attack, Charge, can attack heroes");
            Mark("VAN_CS2_103", "VAN_CS2_103e2");
        }
        { // Blade Flurry hits the enemy hero too.
            var game = NewGame(); var p = game.Player1;
            Play(game, p, "VAN_CS2_106"); // Fiery War Axe 3/2
            var foe = Summon(p.Opponent, "VAN_CS2_120"); // River Crocolisk 2/3
            int hp = p.Opponent.Hero.Health;
            Play(game, p, "VAN_CS2_233");
            Check(p.Opponent.Hero.Health == hp - 3 && foe.Zone != p.Opponent.BoardZone && p.Hero.Weapon == null, "Blade Flurry: all enemies, weapon destroyed: hero " + (hp - p.Opponent.Hero.Health) + " minion " + foe.Damage + " weapon " + (p.Hero.Weapon?.Card.Id ?? "none"));
            Mark("VAN_CS2_233");
        }
        { // Warsong Commander: a summoned minion with 3 or less Attack gets Charge; it keeps it.
            var game = NewGame(); var p = game.Player1;
            var warsong = Summon(p, "VAN_EX1_084");
            var small = Summon(p, "VAN_CS2_120");    // 2/3
            var big = Summon(p, "VAN_CS2_186");      // War Golem 7/7
            Check(small.HasCharge && !big.HasCharge, "Warsong: Charge for 3 or less Attack only");
            Kill(game, warsong);
            Check(small.HasCharge, "Warsong: the Charge stays");
            Mark("VAN_EX1_084");
        }
        { // Coldlight Seer, Murloc Warleader, Grimscale Oracle and Murloc Tidecaller count ALL murlocs.
            var game = NewGame(); var p = game.Player1;
            var tide = Summon(p, "VAN_EX1_509");
            var enemyMurloc = Summon(p.Opponent, "VAN_CS2_168"); // 2/1
            Check(tide.AttackDamage == 2, "Tidecaller: an enemy Murloc counts");
            Play(game, p, "VAN_EX1_103");
            Check(enemyMurloc.Health == 3, "Coldlight Seer: enemy Murlocs get +2 Health");
            var warleader = Summon(p, "VAN_EX1_507");
            Check(enemyMurloc.AttackDamage == 4 && enemyMurloc.Health == 4, "Warleader: enemy Murlocs get +2/+1");
            var oracle = Summon(p, "VAN_EX1_508");
            Check(enemyMurloc.AttackDamage == 5, "Grimscale Oracle: enemy Murlocs get +1 Attack");
            Kill(game, warleader); Kill(game, oracle);
            Check(enemyMurloc.AttackDamage == 2 && enemyMurloc.Health == 3, "the auras leave with their sources");
            Mark("VAN_EX1_509", "VAN_EX1_103", "VAN_EX1_507", "VAN_EX1_507e", "VAN_EX1_508");
        }
        { // Silence removes both Murloc auras (Warleader's and Grimscale Oracle's), on both sides.
            var game = NewGame(); var p = game.Player1;
            var friendMurloc = Summon(p, "VAN_CS2_168");          // 2/1
            var enemyMurloc = Summon(p.Opponent, "VAN_CS2_168");  // 2/1
            var warleader = Summon(p, "VAN_EX1_507");
            var oracle = Summon(p, "VAN_EX1_508");
            Check(friendMurloc.AttackDamage == 5 && friendMurloc.Health == 2 && enemyMurloc.AttackDamage == 5 && enemyMurloc.Health == 2,
                "Murloc auras before Silence: " + friendMurloc.AttackDamage + "/" + friendMurloc.Health + " and " + enemyMurloc.AttackDamage + "/" + enemyMurloc.Health);
            Play(game, p, "VAN_EX1_332", warleader);
            Check(friendMurloc.AttackDamage == 3 && friendMurloc.Health == 1 && enemyMurloc.AttackDamage == 3 && enemyMurloc.Health == 1,
                "Silence removes Warleader's aura: " + friendMurloc.AttackDamage + "/" + friendMurloc.Health + " and " + enemyMurloc.AttackDamage + "/" + enemyMurloc.Health);
            Play(game, p, "VAN_EX1_332", oracle);
            Check(friendMurloc.AttackDamage == 2 && enemyMurloc.AttackDamage == 2 && warleader.Zone == p.BoardZone && oracle.Zone == p.BoardZone,
                "Silence removes Grimscale Oracle's aura: " + friendMurloc.AttackDamage + " and " + enemyMurloc.AttackDamage);
        }
        { // Warsong Commander next to an Attack aura: the summon trigger reads the minion's Attack with the aura applied
          // (Game updates auras before SUMMON triggers), so a 3-Attack minion next to Raid Leader is a 4-Attack minion.
          // This is the 2014 behaviour (owner decision 2026-11-24, researched in docs/classic-2014.md "Warsong Commander"):
          // period players reported that auras and Battlecry buffs changed Warsong's condition, and no 2014 patch changed it.
            var game = NewGame(); var p = game.Player1;
            Summon(p, "VAN_EX1_084");                        // Warsong Commander
            Summon(p, "VAN_CS2_122");                        // Raid Leader: other minions +1 Attack
            var raptor = (Minion)Play(game, p, "VAN_CS2_172"); // Bloodfen Raptor 3/2, played from hand
            Check(raptor.AttackDamage == 4 && !raptor.HasCharge, "Warsong next to Raid Leader: a 3-Attack minion counts as 4 Attack, no Charge");
            var wisp = (Minion)Play(game, p, "VAN_CS2_231");  // Wisp 1/1, 2 with the aura
            Check(wisp.HasCharge, "Warsong next to Raid Leader: a minion at 3 or less with the aura still gets Charge");
        }
        { // Sylvanas Windrunner's Deathrattle names the one minion it steals (META_DATA TARGET in its TRIGGER block). Without
          // it the client played the steal FX on every enemy minion (owner live test 2026-11-24).
            var game = NewGame(); var p = game.Player1; var o = game.Player2;
            var enemies = new[] { "VAN_CS2_182", "VAN_CS2_172", "VAN_CS2_231" }.Select(id => Summon(o, id)).ToList();
            var sylvanas = Summon(p, "VAN_EX1_016");
            game.PowerHistory.Last.Clear();
            Play(game, p, "VAN_CS2_029", sylvanas);           // Fireball on Sylvanas
            var stolen = enemies.Single(m => m.Controller == p);
            var log = game.PowerHistory.Last;
            int trigger = log.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.TRIGGER && b.Source == sylvanas.Id);
            var targets = log.Skip(trigger + 1).TakeWhile(e => !(e is SabberStoneCore.Kettle.PowerHistoryBlockEnd))
                .OfType<SabberStoneCore.Kettle.PowerHistoryMetaData>().Where(m => m.Type == MetaDataType.TARGET).ToList();
            Check(trigger >= 0 && targets.Count == 1 && targets[0].Info.SequenceEqual(new[] { stolen.Id }),
                "Sylvanas: the Deathrattle names only the stolen minion: " + string.Join(";", targets.Select(t => string.Join(",", t.Info))));
        }
        { // Every other kind of change to a randomly selected recipient names it too (PowerHistory.SelectedHitTarget): a steal in
          // a Battlecry, a destroy, a transform, an end-of-turn buff. Without a TARGET the client plays the FX on every candidate.
            // The ids the TARGETs directly inside the first `type` block of `source` name.
            int[] Named(Game game, int source, BlockType type)
            {
                var log = game.PowerHistory.Last;
                int start = log.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == type && b.Source == source);
                var named = new List<int>();
                for (int i = start + 1, depth = 1; start >= 0 && i < log.Count && depth > 0; i++)
                {
                    if (log[i] is SabberStoneCore.Kettle.PowerHistoryBlockStart) depth++;
                    else if (log[i] is SabberStoneCore.Kettle.PowerHistoryBlockEnd) depth--;
                    else if (depth == 1 && log[i] is SabberStoneCore.Kettle.PowerHistoryMetaData m && m.Type == MetaDataType.TARGET) named.AddRange(m.Info);
                }
                return named.ToArray();
            }
            string[] Enemies = { "VAN_CS2_182", "VAN_CS2_172", "VAN_CS2_231", "VAN_CS2_168" };
            { // Mind Control Tech: a Battlecry steal (POWER block)
                var game = NewGame(); var p = game.Player1; var o = game.Player2;
                var enemies = Enemies.Select(id => Summon(o, id)).ToList();
                game.PowerHistory.Last.Clear();
                var tech = Play(game, p, "VAN_EX1_085");
                var stolen = enemies.Single(m => m.Controller == p);
                Check(Named(game, tech.Id, BlockType.POWER).SequenceEqual(new[] { stolen.Id }), "Mind Control Tech names the stolen minion");
            }
            { // Deadly Shot: a random destroy
                var game = NewGame(); var p = game.Player1; var o = game.Player2;
                var enemies = Enemies.Select(id => Summon(o, id)).ToList();
                game.PowerHistory.Last.Clear();
                var shot = Play(game, p, "VAN_EX1_617");
                var dead = enemies.Single(m => m.Zone?.Type != Zone.PLAY);
                Check(Named(game, shot.Id, BlockType.POWER).SequenceEqual(new[] { dead.Id }), "Deadly Shot names the destroyed minion");
            }
            { // Tinkmaster Overspark: a random transform
                var game = NewGame(); var p = game.Player1; var o = game.Player2;
                var enemies = Enemies.Select(id => Summon(o, id)).ToList();
                game.PowerHistory.Last.Clear();
                var tink = Play(game, p, "VAN_EX1_083");
                var changed = enemies.Single(m => m.Card.Id != Enemies[enemies.IndexOf(m)]);
                Check(Named(game, tink.Id, BlockType.POWER).SequenceEqual(new[] { changed.Id }), "Tinkmaster Overspark names the transformed minion");
            }
            { // Master Swordsmith: an end-of-turn buff on a random other friendly minion (TRIGGER block)
                var game = NewGame(); var p = game.Player1;
                var friends = new[] { "VAN_CS2_182", "VAN_CS2_172" }.Select(id => Summon(p, id)).ToList();
                var smith = Summon(p, "VAN_NEW1_037");
                game.PowerHistory.Last.Clear();
                game.Process(EndTurnTask.Any(p));
                var buffed = friends.Single(m => m.AttackDamage != m.Card.ATK);
                Check(Named(game, smith.Id, BlockType.TRIGGER).SequenceEqual(new[] { buffed.Id }), "Master Swordsmith names the buffed minion");
            }
        }
        { // Innervate gives 2; Preparation takes 3 off the next spell.
            var game = NewGame(); var p = game.Player1;
            p.BaseMana = 3; p.UsedMana = 0; p.TemporaryMana = 0;
            var innervate = Add(p, "VAN_EX1_169");
            Check(game.Process(PlayCardTask.Any(p, innervate)) && p.RemainingMana == 5, "Innervate: 2 Mana Crystals");
            var prep = Add(p, "VAN_EX1_145");
            var fireball = Add(p, "VAN_CS2_029");
            Check(game.Process(PlayCardTask.Any(p, prep)) && fireball.Cost == 1, "Preparation: 3 less");
            Mark("VAN_EX1_169", "VAN_EX1_145", "VAN_EX1_145o");
        }
        { // Preparation's discount expires unused at the end of the turn.
            var game = NewGame(); var p = game.Player1;
            var fireball = Add(p, "VAN_CS2_029");
            Play(game, p, "VAN_EX1_145");
            Check(fireball.Cost == 1, "Preparation: the discount applies this turn");
            End(game);
            Check(fireball.Cost == 4, "Preparation: the discount expires at the end of the turn: " + fireball.Cost);
            End(game);
            Check(fireball.Cost == 4 && !(p.AppliedEnchantments?.Any(e => e.Card.Id == "VAN_EX1_145o") ?? false), "Preparation: nothing is left on the next turn");
        }
        { // Druid of the Claw: Choose One gives Charge or +2 Health and Taunt, never a transform.
            foreach (int choice in new[] { 1, 2 })
            {
                var game = NewGame(); var p = game.Player1;
                p.BaseMana = 10; p.UsedMana = 0;
                var druid = Add(p, "VAN_EX1_165");
                Check(game.Process(PlayCardTask.Minion(p, druid, choice)), "Druid of the Claw played, choice " + choice);
                var minion = (Minion)druid;
                Check(minion.Card.Id == "VAN_EX1_165" && minion.Zone == p.BoardZone, "Druid of the Claw keeps its card");
                Check(choice == 1 ? minion.HasCharge && minion.Health == 4 && !minion.HasTaunt
                                  : minion.HasTaunt && minion.Health == 6 && !minion.HasCharge, "Druid of the Claw choice " + choice);
            }
            Mark("VAN_EX1_165", "VAN_EX1_165a", "VAN_EX1_165b");
        }
        { // Bane of Doom deals 3 and summons a classic Demon.
            var game = NewGame(); var p = game.Player1;
            var foe = Summon(p.Opponent, "VAN_CS2_120"); // 2/3
            Play(game, p, "VAN_EX1_320", foe);
            var demon = p.BoardZone.FirstOrDefault();
            Check(foe.IsDead || foe.Zone != p.Opponent.BoardZone, "Bane of Doom: 3 damage kills a 3-Health minion");
            Check(demon != null && demon.Card.IsRace(Race.DEMON) && demon.Card.Set == CardSet.VANILLA, "Bane of Doom: a classic Demon");
            Mark("VAN_EX1_320");
        }
        { // Eaglehorn Bow: an enemy Secret counts.
            var game = NewGame(); var p = game.Player1;
            Play(game, p, "VAN_EX1_536");
            int durability = p.Hero.Weapon.Durability;
            End(game);
            var enemy = game.Player2;
            Play(game, enemy, "VAN_EX1_287"); // Counterspell
            End(game);
            Play(game, p, "VAN_CS2_025");     // Arcane Explosion, countered
            Check(p.Hero.Weapon.Durability == durability + 1, "Eaglehorn Bow: an enemy Secret revealed");
            Mark("VAN_EX1_536");
        }
        { // Eaglehorn Bow: your own Secret counts too ("Whenever a Secret is revealed").
            var game = NewGame(); var p = game.Player1;
            Play(game, p, "VAN_EX1_536");
            Play(game, p, "VAN_EX1_609");     // Snipe
            int durability = p.Hero.Weapon.Durability;
            End(game);
            Play(game, game.Player2, "VAN_CS2_120");  // River Crocolisk: Snipe is revealed
            Check(p.SecretZone.Count == 0 && p.Hero.Weapon.Durability == durability + 1, "Eaglehorn Bow: a friendly Secret revealed");
        }
        { // Bestial Wrath can target an enemy Beast.
            var game = NewGame(); var p = game.Player1;
            var beast = Summon(p.Opponent, "VAN_CS2_120");
            Play(game, p, "VAN_EX1_549", beast);
            Check(beast.AttackDamage == 4 && beast.IsImmune, "Bestial Wrath on an enemy Beast");
            Mark("VAN_EX1_549");
        }
        { // Force of Nature: three 2/2 Charge Treants that die at the end of the turn.
            var game = NewGame(); var p = game.Player1;
            Play(game, p, "VAN_EX1_571");
            var treants = p.BoardZone.Where(m => m.Card.Id == "VAN_EX1_tk9b").ToArray();
            Check(treants.Length == 3 && treants.All(t => t.HasCharge && t.AttackDamage == 2 && t.Health == 2), "Force of Nature: three Charge Treants");
            End(game);
            Check(!p.BoardZone.Any(m => m.Card.Id == "VAN_EX1_tk9b"), "Force of Nature: the Treants die at the end of the turn");
            Mark("VAN_EX1_571", "VAN_EX1_tk9b");
        }
        { // The Beast gives the opponent Pip Quickwit.
            var game = NewGame(); var p = game.Player1;
            var beast = Summon(p, "VAN_EX1_577");
            Kill(game, beast);
            Check(p.Opponent.BoardZone.Any(m => m.Card.Id == "VAN_EX1_finkle"), "The Beast: Pip Quickwit for the opponent");
            Mark("VAN_EX1_577");
        }
        { // Ancient of Lore restores 5.
            var game = NewGame(); var p = game.Player1;
            p.Hero.Damage = 10;
            p.BaseMana = 10; p.UsedMana = 0;
            var lore = Add(p, "VAN_NEW1_008");
            Check(game.Process(PlayCardTask.MinionTarget(p, lore, p.Hero, 2)) && p.Hero.Damage == 5, "Ancient of Lore: restore 5");
            Mark("VAN_NEW1_008b");
        }
        { // Master of Disguise: the Stealth stays past the next turn.
            var game = NewGame(); var p = game.Player1;
            var friend = Summon(p, "VAN_CS2_120");
            Play(game, p, "VAN_NEW1_014", friend);
            End(game); End(game);
            Check(friend.HasStealth, "Master of Disguise: Stealth stays");
            Mark("VAN_NEW1_014");
        }
        Check(played.SetEquals(ClassicScripts.Ids), "every classic script is played here: missing "
            + string.Join(",", ClassicScripts.Ids.Except(played)) + "; unknown " + string.Join(",", played.Except(ClassicScripts.Ids)));
    }

    // Cards whose script picks from a fixed random list (an entourage). A VAN card has no list of its own: it uses its
    // original's, with each entry's VAN version where one exists. Before 2026-11-24 the list came back empty and Animal
    // Companion, Ysera, Elite Tauren Chieftain and Gelbin did nothing.
    private static void RandomLists()
    {
        foreach (string id in new[] { "VAN_NEW1_031", "VAN_EX1_572", "VAN_PRO_001", "VAN_EX1_112" })
            Check(LegacyGenerationV1.Entourage(Cards.FromId(id)).Length > 0, id + ": has a random list");
        Check(LegacyGenerationV1.Entourage(Cards.FromId("VAN_NEW1_031")).All(e => e.StartsWith("VAN_NEW1_03", StringComparison.Ordinal)),
            "Animal Companion picks the Classic Misha, Leokk and Huffer: " + string.Join(",", LegacyGenerationV1.Entourage(Cards.FromId("VAN_NEW1_031"))));
        Check(LegacyGenerationV1.Entourage(Cards.FromId("NEW1_031")).SequenceEqual(new[] { "NEW1_032", "NEW1_033", "NEW1_034" }),
            "the original Animal Companion keeps its own list");

        for (int seed = 1; seed <= 6; seed++)
        { // Animal Companion summons one of the three Classic beasts.
            var game = NewGame(seed); var p = game.Player1;
            int before = p.BoardZone.Count;
            Play(game, p, "VAN_NEW1_031");
            Check(p.BoardZone.Count == before + 1 && p.BoardZone.Last().Card.Id.StartsWith("VAN_NEW1_03", StringComparison.Ordinal),
                "Animal Companion summons a Classic beast (seed " + seed + "): " + string.Join(",", p.BoardZone.Select(m => m.Card.Id)));
        }
        { // The Shaman hero power summons a basic Totem.
            var deck = Enumerable.Repeat(Cards.FromId("VAN_CS2_182"), 30).ToList();
            var game = new Game(new GameConfig {
                RandomSeed = 7, StartPlayer = 1, SkipMulligan = true, Shuffle = false, History = true, Logging = false,
                FormatType = FormatType.FT_CLASSIC, PoolFormat = GenerationFormat.Classic,
                Player1HeroClass = CardClass.SHAMAN, Player2HeroClass = CardClass.HUNTER,
                Player1Deck = deck, Player2Deck = deck.ToList() });
            game.StartGame();
            var p = game.Player1; p.BaseMana = 10; p.UsedMana = 0;
            Check(game.Process(HeroPowerTask.Any(p)) && p.BoardZone.Count == 1,
                "Totemic Call (" + p.Hero.HeroPower.Card.Id + ") summons a Totem: " + string.Join(",", p.BoardZone.Select(m => m.Card.Id)));
        }
        { // Ysera adds a Dream card at the end of her owner's turn.
            var game = NewGame(); var p = game.Player1;
            Summon(p, "VAN_EX1_572");
            int hand = p.HandZone.Count;
            End(game);
            Check(p.HandZone.Count == hand + 1, "Ysera adds a Dream card at the end of the turn");
        }
    }
}
