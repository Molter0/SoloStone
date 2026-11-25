using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using SabberStoneCore.Enums;

namespace SabberStoneCore.Model
{
    // Ordered card lists captured from the engine as it was before the offline pool system, pinned by
    // hash in the tests. Older random effects draw from them, so registering or admitting new cards can
    // never change what those effects draw (recorded games and golden traces depend on that). New random
    // effects use OfflineCardPools instead.
    // Compatibility inputs only. Do not extend V1 when registering or admitting cards.
    internal static class LegacyGenerationV1
    {
        private static readonly IReadOnlyDictionary<string, string[]> Inputs = Load();
        private static readonly ConcurrentDictionary<string, ReadOnlyCollection<Card>> Resolved =
            new ConcurrentDictionary<string, ReadOnlyCollection<Card>>();

        internal static IEnumerable<string> Names => Inputs.Keys;

        private static IReadOnlyDictionary<string, string[]> Load()
        {
            using (var stream = typeof(Cards).Assembly.GetManifestResourceStream("SabberStoneCore.Resources.LegacyGenerationV1.xml"))
            {
                var root = XDocument.Load(stream).Root;
                if (root.Name != "LegacyGeneration" || (string)root.Attribute("version") != "1")
                    throw new InvalidOperationException("Invalid legacy generation resource version");
                return new ReadOnlyDictionary<string, string[]>(root.Elements("Pool").ToDictionary(
                    p => (string)p.Attribute("name"),
                    p => p.Value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal));
            }
        }

        internal static string IdentityText(string name) => string.Join("\n", Inputs[name]);

        internal static ReadOnlyCollection<Card> Get(string name) => Resolved.GetOrAdd(name, key =>
            Array.AsReadOnly(Inputs[key].Select(identity =>
            {
                int separator = identity.LastIndexOf(':');
                Card card = Cards.FromId(identity.Substring(0, separator));
                if (card.AssetId.ToString(CultureInfo.InvariantCulture) != identity.Substring(separator + 1))
                    throw new InvalidOperationException("Legacy generation identity changed: " + identity);
                return card;
            }).ToArray()));

        internal static string[] Ids(string name) => Inputs[name]
            .Select(identity => identity.Substring(0, identity.LastIndexOf(':'))).ToArray();

        // Resolve entourage identities only when selected, as before. A card that reuses another card's behaviour (a Classic
        // VAN_ card) has no list of its own: it takes its original's list, each entry swapped for the same card's VAN_
        // version where one exists (Classic Animal Companion summons the Classic Misha, Leokk or Huffer).
        internal static string[] Entourage(Card source)
        {
            if (Inputs.ContainsKey("Entourage/" + source.Id)) return Ids("Entourage/" + source.Id);
            // Classic only: other reprints that borrow a behaviour keep what they drew before (recorded games depend on it).
            // The original comes from the Classic alias registry, not a new Card field: snapshot schemas hash every Card field.
            if (source.Set != CardSet.VANILLA
                || !CardSets.Client.Classic2014.ByDbfId.TryGetValue(source.AssetId, out int originDbf)) return Array.Empty<string>();
            string origin = Cards.FromAssetId(originDbf)?.Id;
            if (origin == null || !Inputs.ContainsKey("Entourage/" + origin)) return Array.Empty<string>();
            return Ids("Entourage/" + origin).Select(id =>
                Cards.TryFromId("VAN_" + id, out Card version) && version.Set == CardSet.VANILLA ? version.Id : id).ToArray();
        }

        internal static string Format(FormatType format) => Cards.LegacyPoolFormat(format) == FormatType.FT_STANDARD ? "Standard" : "Wild";
    }
}
