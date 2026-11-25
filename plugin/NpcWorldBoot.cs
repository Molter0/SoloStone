using System;
using System.IO;
using System.Reflection;

// Plugin-only (not linked into tests/harness). Loads data/npc/*.json from embedded resources with the strict
// Unity-Mono-safe EmbeddedJson parser and installs NpcWorldRuntime. Called once from ClientBridge.Initialize (seam S11).
internal static class NpcWorldBoot
{
    internal static string Read(string resourceName)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Missing embedded resource: " + resourceName))
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }

    internal static NpcWorldCatalogs LoadCatalogs() => LoadCatalogs(null, null);

    // casual: its decks join the ranked library (admission "casual": never generated, matched or inferred on the ladder).
    // arena (#6): its drafted decks join the library too (admission "arena": likewise never served by the ladder or Casual).
    // ranked: the ladder's library; null reads decks-v1.json (a Classic save passes ClassicNpcDecks.Ranked).
    internal static NpcWorldCatalogs LoadCatalogs(CasualDeckCatalog casual, ArenaDeckCatalog arena, NpcDeckLibrary ranked = null)
    {
        NpcWorldJson.Parser = EmbeddedJson.Parse;
        ranked = ranked ?? NpcDeckLibrary.Parse(Read(NpcDeckLibrary.ResourceName));
        var withCasual = casual == null ? ranked : casual.MergeInto(ranked);
        return new NpcWorldCatalogs(arena == null ? withCasual : arena.MergeInto(withCasual),
            SkillCatalog.Parse(Read(SkillCatalog.ResourceName)), PersonalityCatalog.Parse(Read(PersonalityCatalog.ResourceName)),
            NpcNotables.Parse(Read(NpcNotables.ResourceName)), NpcNameTables.Parse(Read(NpcNameTables.ResourceName)),
            NpcCosmeticPool.Parse(Read(NpcCosmeticPool.ResourceName)));
    }

    // Seam S11 startup entry (ClientBridge.Initialize, after SelectedProfile.Initialize). Points every world diagnostics sink
    // at the plugin log (rate limited), then generates and installs the world with its client seams. A failed boot logs
    // once and leaves every hook null, so ladder play keeps today's opponents (LocalDecks.NextOpponent).
    // The save meta the installed world was built for. The world installs before a save is chosen (Start Legend #1), so a
    // save of another meta reinstalls it when it opens (EnsureForSaveMeta); otherwise a Classic save met Violet Hold NPCs.
    private static string installedMeta;
    internal static string InstalledMeta => installedMeta;

    internal static bool EnsureForSaveMeta(Action<string> log)
    {
        if (installedMeta == ActiveSaveMeta.Current) return true;
        log?.Invoke("npc-world reinstall for save meta " + ActiveSaveMeta.Current + " (was " + (installedMeta ?? "none") + ")");
        return TryInstall(log);
    }

    internal static bool TryInstall(Action<string> log)
    {
        LocalProfile.MetaLegendTopRating = LegendTopRatingFor;
        bool installed = TryInstallForMeta(log);
        installedMeta = installed ? ActiveSaveMeta.Current : null;
        return installed;
    }

    private static bool TryInstallForMeta(Action<string> log)
    {
        var limited = NpcWorldRuntime.RateLimited(log);
        NpcCatalog.ProfileHookFailed = error => limited?.Invoke("NpcCatalog.ForDeck hook failed: " + error.GetType().Name + ": " + error.Message);
        NpcPopulationRules.Log = limited;
        DeckPlans.SeamLog = limited;
#if ENGINE_SOURCE
        FairBotState.HistoryLimitLog = limited;
#endif
        LocalDecks.ExternalDecksLog = limited;
        // Classic 2014 (docs/classic-2014.md): a Classic save's opponents play classic-decks-v1.json on the ladder and in
        // Casual, and arena-classic-decks-v1.json (drafted under the Classic Arena rules) in Arena.
        if (ActiveSaveMeta.IsClassic)
        {
            NpcDeckLibrary classicRanked; CasualDeckCatalog classicCasual;
            try
            {
                NpcWorldJson.Parser = EmbeddedJson.Parse;
                var classic = NpcDeckLibrary.Parse(Read(ClassicNpcDecks.ResourceName));
                classicRanked = ClassicNpcDecks.Ranked(classic); classicCasual = ClassicNpcDecks.Casual(classic);
            }
            catch (Exception failure)
            {
                // The world installed at boot is Violet Hold's; leaving it live would match a Classic save against its NPCs.
                limited?.Invoke("npc-world classic catalog unavailable: " + failure.Message);
                NpcWorldRuntime.Uninstall();
                return false;
            }
            ArenaDeckCatalog classicArena = null;
            try { classicArena = ArenaDeckCatalog.Parse(Read(ArenaDeckCatalog.ClassicResourceName)); }
            catch (Exception failure) { limited?.Invoke("npc-world classic arena catalog unavailable: " + failure.Message); }
            return NpcWorldRuntime.TryInstallClient(() => NpcWorld.Generate(LoadCatalogs(classicCasual, classicArena, classicRanked)), PendingNpcForDeck, log,
                classicCasual, classicArena);
        }
        CasualDeckCatalog casual = null;
        try { NpcWorldJson.Parser = EmbeddedJson.Parse; casual = CasualDeckCatalog.Parse(Read(CasualDeckCatalog.ResourceName)); }
        catch (Exception failure) { limited?.Invoke("npc-world casual catalog unavailable: " + failure.Message); }
        ArenaDeckCatalog arena = null;
        try { NpcWorldJson.Parser = EmbeddedJson.Parse; arena = ArenaDeckCatalog.Parse(Read(ArenaDeckCatalog.ResourceName)); }
        catch (Exception failure) { limited?.Invoke("npc-world arena catalog unavailable: " + failure.Message); }
        return NpcWorldRuntime.TryInstallClient(() => NpcWorld.Generate(LoadCatalogs(casual, arena)), PendingNpcForDeck, log, casual, arena);
    }

    // Start Legend #1 for a new save of another meta than the installed world's: that meta's world, generated as its save
    // will install it (same catalogs, default seed) but not installed, gives the #1 rating at match 0. Null when the
    // installed world is already that meta's.
    private static double? LegendTopRatingFor(string meta)
    {
        if (meta == installedMeta) return null;
        using (ActiveSaveMeta.Use(meta))
        {
            NpcWorldJson.Parser = EmbeddedJson.Parse;
            NpcWorld world;
            if (ActiveSaveMeta.IsClassic)
            {
                var classic = NpcDeckLibrary.Parse(Read(ClassicNpcDecks.ResourceName));
                ArenaDeckCatalog classicArena = null;
                try { classicArena = ArenaDeckCatalog.Parse(Read(ArenaDeckCatalog.ClassicResourceName)); } catch (Exception) { }
                world = NpcWorld.Generate(LoadCatalogs(ClassicNpcDecks.Casual(classic), classicArena, ClassicNpcDecks.Ranked(classic)));
            }
            else
            {
                CasualDeckCatalog casual = null; ArenaDeckCatalog arena = null;
                try { casual = CasualDeckCatalog.Parse(Read(CasualDeckCatalog.ResourceName)); } catch (Exception) { }
                try { arena = ArenaDeckCatalog.Parse(Read(ArenaDeckCatalog.ResourceName)); } catch (Exception) { }
                world = NpcWorld.Generate(LoadCatalogs(casual, arena));
            }
            return new LadderMatchmaker(world).Legend.FieldRating(1, 0);
        }
    }

    // The saved, unsettled match's NPC when it plays this deck (pins and names the deck while NpcWorldRuntime.Active is
    // unset). Normal builds settle an interrupted pending match as Abandoned at profile open, before this boot runs.
    private static string PendingNpcForDeck(string deckId)
    {
        var pending = LocalLadder.Profile?.Pending;
        return pending != null && pending.NpcId != null && (pending.NpcDeckId ?? pending.Opponent) == deckId ? pending.NpcId : null;
    }
}
