using System;
using System.IO;
using System.Linq;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

// Classic 2014 (docs/classic-2014.md): the GameConfig of a match in a Classic save. NativeMatch takes FormatType and PoolFormat
// from ActiveMatchFormat (pinned in native-bindings-logic); here a Classic save's world decks play a whole match with them.
internal static class ClassicMatchTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("classic match: " + message); }

    internal static void Run()
    {
        Check(ActiveMatchFormat.Format == FormatType.FT_WILD && ActiveMatchFormat.Pool == GenerationFormat.Standard,
            "a Violet Hold match stays FT_WILD with the Standard pool");
        var classic = NpcDeckLibrary.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "npc", "classic-decks-v1.json")));
        var ranked = ClassicNpcDecks.Ranked(classic);
        using (ActiveSaveMeta.Use(ActiveSaveMeta.Classic))
        {
            Check(ActiveMatchFormat.Format == FormatType.FT_CLASSIC && ActiveMatchFormat.Pool == GenerationFormat.Classic,
                "a Classic match is FT_CLASSIC with the classic pool");
            var player = NpcDeckLibrary.ToLocalDeck(ranked.Find("npc-classic-starter-mage"), "Player");
            var npc = NpcDeckLibrary.ToLocalDeck(ranked.Find("npc-classic-zoo"), "Classic NPC");
            var game = new Game(new GameConfig {
                Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARLOCK,
                Player1Deck = player.Build(), Player2Deck = npc.Build(),
                FormatType = ActiveMatchFormat.Format, PoolFormat = ActiveMatchFormat.Pool,
                StartPlayer = 1, Shuffle = true, SkipMulligan = true, History = true, Logging = false, RandomSeed = 2014 });
            Check(game.FormatType == FormatType.FT_CLASSIC, "the engine game is Classic");
            game.StartGame();
            for (int step = 0; step < 3000 && game.State != State.COMPLETE; step++)
            {
                var pending = game.GetPendingDecisions().FirstOrDefault();
                var owner = pending == null ? game.CurrentPlayer : game.ControllerById(pending.ControllerId);
                var task = LocalBot.Choose(owner.Options());
                if (task is ChooseTask choice)
                {
                    var decision = game.GetPendingDecisions().Single(x => x.ControllerId == owner.Id);
                    Check(game.TryAnswer(new SabberStoneCore.Model.Decisions.DecisionAnswer(decision.Id, decision.ControllerId, choice.Choices), out var error), "answer: " + error);
                }
                else Check(game.Process(task), "action rejected " + task);
            }
            Check(game.State == State.COMPLETE, "the Classic match ends");
            var seen = game.Player1.BoardZone.Concat(game.Player2.BoardZone).Select(m => m.Card)
                .Concat(game.Player1.HandZone.Concat(game.Player2.HandZone).Select(c => c.Card));
            Check(seen.Where(c => c.Collectible).All(c => c.Set == CardSet.VANILLA), "only Classic cards in play");
        }
        Check(ActiveMatchFormat.Format == FormatType.FT_WILD, "the Violet Hold format is back after the scope");
        // VAN cards keep the client's own localized text in a staged match; their modern originals still get the engine's.
        Check(!LocalNativeText.TryGetOverride(true, "VAN_EX1_116", value => value, "deDE", out _)
            && !LocalNativeText.TryGetOverride(true, "VAN_CS2_029", value => value, null, out _), "a VAN card gets no text override");
        Check(LocalNativeText.TryGetOverride(true, "CS2_089", value => value, null, out string modern) && !string.IsNullOrEmpty(modern),
            "a modern card still gets its engine text");
        Console.WriteLine("PASS classic match: FT_CLASSIC GameConfig and classic pool, a Classic save's NPC deck plays to the end");
    }
}
