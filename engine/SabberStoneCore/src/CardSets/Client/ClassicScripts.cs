using System;
using System.Collections.Generic;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.Client
{
	/// <summary>
	/// Classic (June 2014) scripts for the VAN_* cards whose behaviour differs from their original card's script
	/// (docs/classic-2014.md). Each replaces what Classic2014 copied from the original; the row's own cost, stats,
	/// text and keyword tags already hold the 2014 values. A card listed here must be a VANILLA card.
	/// </summary>
	internal static class ClassicScripts
	{
		/// <summary>The VAN ids that get a classic script, for proofs.</summary>
		internal static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);

		internal static void Apply(Dictionary<string, Card> cards)
		{
			void Set(string id, Power power, Action<Card> configure = null)
			{
				Card card = cards[id];
				if (card.Set != CardSet.VANILLA || !Ids.Add(id)) throw new InvalidOperationException("Invalid classic script: " + id);
				card.Power = power;
				configure?.Invoke(card);
			}

			// Charge (CS2_103): "Give a friendly minion +2 Attack and Charge."
			Set("VAN_CS2_103", new Power { PowerTask = new AddEnchantmentTask("VAN_CS2_103e2", EntityType.TARGET) });
			Set("VAN_CS2_103e2", new Power { Enchant = new Enchant(Effects.Charge, Effects.Attack_N(2)) });

			// Blade Flurry (CS2_233): "Destroy your weapon and deal its damage to all enemies."
			Set("VAN_CS2_233", new Power
			{
				PowerTask = ComplexTask.Create(
					new GetGameTagTask(GameTag.ATK, EntityType.WEAPON),
					new DamageNumberTask(EntityType.ENEMIES, true),
					new DestroyTask(EntityType.WEAPON))
			});

			// Warsong Commander (EX1_084): "Whenever you summon a minion with 3 or less Attack, give it Charge."
			// The Charge stays after the Commander leaves, as in 2014. The condition reads the minion's Attack with auras applied
			// (a 3-Attack minion next to Raid Leader gets no Charge), as period players reported (docs/classic-2014.md).
			Set("VAN_EX1_084", new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsTagValue(GameTag.ATK, 3, RelaSign.LEQ),
					SingleTask = new SetGameTagTask(GameTag.CHARGE, 1, EntityType.TARGET)
				}
			});

			// Coldlight Seer (EX1_103): "Battlecry: Give ALL other Murlocs +2 Health."
			Set("VAN_EX1_103", new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.ALLMINIONS_NOSOURCE),
					new FilterStackTask(SelfCondition.IsRace(Race.MURLOC)),
					new AddEnchantmentTask("EX1_103e", EntityType.STACK))
			});

			// Preparation (EX1_145): "The next spell you cast this turn costs (3) less."
			Set("VAN_EX1_145", new Power { PowerTask = new AddEnchantmentTask("VAN_EX1_145o", EntityType.CONTROLLER) });
			Set("VAN_EX1_145o", new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(3))
				{
					Condition = SelfCondition.IsSpell,
					RemoveTrigger = (TriggerType.CAST_SPELL, null)
				}
			});

			// Druid of the Claw (EX1_165): "Choose One - Charge; or +2 Health and Taunt." No transform in 2014.
			Set("VAN_EX1_165", null);
			Set("VAN_EX1_165a", new Power { PowerTask = new SetGameTagTask(GameTag.CHARGE, 1, EntityType.SOURCE) });
			// No 2014 enchantment card exists for Bear Form; DRG_311e is the client's implemented "+2 Health and Taunt".
			Set("VAN_EX1_165b", new Power { PowerTask = new AddEnchantmentTask("DRG_311e", EntityType.SOURCE) });

			// Innervate (EX1_169): "Gain 2 Mana Crystals this turn only."
			Set("VAN_EX1_169", new Power { PowerTask = new TempManaTask(2) });

			// Bane of Doom (EX1_320): "Deal 3 damage to a character. If it dies, summon a random Demon."
			Set("VAN_EX1_320", new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(3, EntityType.TARGET, true),
					new ConditionTask(EntityType.TARGET, SelfCondition.IsDead),
					new FlagTask(true, ComplexTask.SummonRandomMinion(GameTag.CARDRACE, (int)Race.DEMON)))
			});

			// Murloc Warleader (EX1_507): "ALL other murlocs have +2/+1."
			Set("VAN_EX1_507", new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD_EXCEPT_SOURCE, "VAN_EX1_507e") { Condition = SelfCondition.IsRace(Race.MURLOC) },
					new Aura(AuraType.OP_BOARD, "VAN_EX1_507e") { Condition = SelfCondition.IsRace(Race.MURLOC) })
			});
			Set("VAN_EX1_507e", new Power { Enchant = new Enchant(Effects.Attack_N(2), Effects.Health_N(1)) });

			// Grimscale Oracle (EX1_508): "ALL other Murlocs have +1 Attack."
			Set("VAN_EX1_508", new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD_EXCEPT_SOURCE, "EX1_508o") { Condition = SelfCondition.IsRace(Race.MURLOC) },
					new Aura(AuraType.OP_BOARD, "EX1_508o") { Condition = SelfCondition.IsRace(Race.MURLOC) })
			});

			// Murloc Tidecaller (EX1_509): "Whenever a Murloc is summoned, gain +1 Attack."
			Set("VAN_EX1_509", new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.MURLOC),
					SingleTask = new AddEnchantmentTask("EX1_509e", EntityType.SOURCE)
				}
			});

			// Eaglehorn Bow (EX1_536): "Whenever a Secret is revealed, gain +1 Durability." Either player's Secret.
			Set("VAN_EX1_536", new Power
			{
				Trigger = new Trigger(TriggerType.SECRET_REVEALED)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new AddEnchantmentTask("EX1_536e", EntityType.WEAPON)
				}
			});

			// Bestial Wrath (EX1_549): "Give a Beast +2 Attack and Immune this turn." Any Beast, either side.
			Set("VAN_EX1_549", new Power { PowerTask = new AddEnchantmentTask("EX1_549o", EntityType.TARGET) },
				card => card.SetPlayRequirements(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } }));

			// Force of Nature (EX1_571): "Summon three 2/2 Treants with Charge that die at the end of the turn."
			Set("VAN_EX1_571", new Power { PowerTask = new SummonTask("VAN_EX1_tk9b", 3) },
				card => card.SetPlayRequirements(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }));
			Set("VAN_EX1_tk9b", new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new DestroyTask(EntityType.SOURCE)
				}
			}, card => card.Implemented = true);

			// The Beast (EX1_577): "Deathrattle: Summon a 3/3 Pip Quickwit for your opponent."
			Set("VAN_EX1_577", new Power { DeathrattleTask = new SummonOpTask("VAN_EX1_finkle") });

			// Ancient of Lore (NEW1_008): the heal option restores 5 Health (the original's script restores 7).
			Set("VAN_NEW1_008b", new Power { PowerTask = new HealTask(5, EntityType.TARGET) });

			// Master of Disguise (NEW1_014): "Battlecry: Give a friendly minion Stealth." It stays.
			Set("VAN_NEW1_014", new Power { PowerTask = new SetGameTagTask(GameTag.STEALTH, 1, EntityType.TARGET) });
		}
	}
}
