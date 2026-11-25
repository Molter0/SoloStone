using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using SabberStoneCore.Loader;
using SabberStoneCore.Model;

namespace SabberStoneCore.CardSets.Client
{
	/// <summary>
	/// The Classic format's cards (VAN_*, facts/classic-2014.json; docs/classic-2014.md). Each row plays with its
	/// original card's behaviour (Card.UseBehaviourFrom), and its own cost, stats and text win. Cards whose June 2014
	/// behaviour differs from the original's get a classic script after that (ClassicScripts). Kept apart from
	/// Reprints, whose registry means the Core reprints.
	/// </summary>
	public static class Classic2014
	{
		private static readonly Dictionary<int, int> Aliases = new Dictionary<int, int>();
		private static readonly IReadOnlyDictionary<int, int> Registry = new ReadOnlyDictionary<int, int>(Aliases);

		/// <summary>Classic dbfId to the original card's dbfId.</summary>
		public static IReadOnlyDictionary<int, int> ByDbfId
		{
			get { _ = Cards.Count; return Registry; }
		}

		// VAN_EX1_572 Ysera was already a Violet Hold overlay row before the classic file existed; it keeps that row.
		private static readonly (string Id, string Original)[] OutsideFile = { ("VAN_EX1_572", "EX1_572") };

		internal static void Apply(Dictionary<string, Card> cards)
		{
			void Alias(string id, string originalId, int originalDbf)
			{
				if (!cards.TryGetValue(originalId, out Card original) || original.AssetId != originalDbf || id == originalId ||
				    !cards.TryGetValue(id, out Card card) || card.Set != Enums.CardSet.VANILLA)
					throw new InvalidDataException("Invalid classic card: " + id);
				card.UseBehaviourFrom(original);
				Aliases.Add(card.AssetId, original.AssetId);
			}
			foreach (var definition in ClientOverlay.Classic.Cards)
				if (definition.OriginalId != null) Alias(definition.CardId, definition.OriginalId, definition.OriginalDbfId);
			foreach (var (id, original) in OutsideFile)
				Alias(id, original, cards[original].AssetId);
			ClassicScripts.Apply(cards);
		}
	}
}
