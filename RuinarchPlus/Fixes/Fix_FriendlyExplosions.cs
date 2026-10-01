using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Traits;

namespace RuinarchPlus
{
	// BUG: your own demons can destroy your Portal. The game spares your demonic buildings
	// from your own spells (TileObject.AdjustHP skips damage whose source is a player spell,
	// CombatManager.IsDamageSourceFromPlayerSpell), but not from the elemental explosions
	// your demons set off. When the Portal's defenders (DemonDefendPartyQuest) fight next to
	// it, a poison explosion takes up to all of the max HP of every object in range, and the
	// Portal's objects pass that on to the Portal: in a test run one chain took all 13,666 HP
	// in 17 seconds and the game ended in defeat. Fix: poison and frozen explosions and chain
	// lightning set off by your side (the player's spells or a character of the player's
	// faction) leave your demonic buildings' objects alone, the same objects the game's own
	// spell rule protects. Villagers and monsters in range are hurt as before.
	// A frozen explosion (something Frozen gets Zapped: 20 % of the max HP of every object
	// within 2 tiles) carries no character, only whether the player set it off, so a freeze
	// or a zap by one of your demons counted as nobody's: in a test run one destroyed the
	// Portal at full HP. Now it is your side's when the one who froze the target or the one
	// who zapped it is of the player's faction, as the game already counts any damage done by
	// a player-faction character as the player's (Character.AdjustHP).
	internal static class FriendlyExplosions
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.friendlyExplosionsSpareBuildings;

		internal static bool PlayerSide(Character c) => c?.faction?.isPlayerFaction == true;

		// True when the explosion should leave this object alone: your buildings' own objects
		// (the ones the game's spell rule protects) and their walls, which pass damage on to
		// the building too (LocationStructureObject adds block walls as damage contributors).
		internal static bool Spares(ITraitable traitable, Character responsible, bool isPlayerSource)
		{
			return Enabled && traitable is TileObject t
				&& (t.tileObjectType.IsDemonicStructureTileObject() || t.tileObjectType == TILE_OBJECT_TYPE.STRUCTURE_BLOCKER_TILE_OBJECT
					|| (t.tileObjectType == TILE_OBJECT_TYPE.BLOCK_WALL && t.gridTileLocation?.structure != null && t.gridTileLocation.structure.structureType.IsPlayerStructure()))
				&& (isPlayerSource || PlayerSide(responsible));
		}
	}

	[HarmonyPatch(typeof(CombatManager), "PoisonExplosionEffect")]
	internal static class Fix_FriendlyExplosions_Poison
	{
		private static bool Prefix(ITraitable traitable, Character characterResponsible, bool isPlayerSource) =>
			!FriendlyExplosions.Spares(traitable, characterResponsible, isPlayerSource);
	}

	[HarmonyPatch(typeof(CombatManager), "FrozenExplosionEffect")]
	internal static class Fix_FriendlyExplosions_Frozen
	{
		private static bool Prefix(ITraitable traitable, bool isPlayerSource) =>
			!FriendlyExplosions.Spares(traitable, null, isPlayerSource);
	}

	// Who froze and who zapped the target of the frozen explosion about to start: the game's
	// elemental check (TraitContainer.ProcessBeforeAddingElementalStatus) removes the Frozen
	// status and calls CombatManager.FrozenExplosion right away, on the same thread.
	[HarmonyPatch(typeof(TraitContainer), "ProcessBeforeAddingElementalStatus")]
	internal static class Fix_FriendlyExplosions_FrozenCause
	{
		[ThreadStatic] internal static List<Character> Froze;
		[ThreadStatic] internal static Character Zapped;

		private static void Prefix(TraitContainer __instance, Character characterResponsible)
		{
			Froze = __instance.GetTraitOrStatus<Frozen>("Frozen")?.responsibleCharacters?.ToList();
			Zapped = characterResponsible;
		}

		private static void Postfix()
		{
			Froze = null;
			Zapped = null;
		}
	}

	[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.FrozenExplosion))]
	internal static class Fix_FriendlyExplosions_FrozenSide
	{
		private static void Prefix(LocationGridTile targetTile, ref bool isPlayerSource)
		{
			try
			{
				List<Character> froze = Fix_FriendlyExplosions_FrozenCause.Froze;
				Character zapped = Fix_FriendlyExplosions_FrozenCause.Zapped;
				bool ours = FriendlyExplosions.PlayerSide(zapped) || (froze != null && froze.Any(FriendlyExplosions.PlayerSide));
				List<LocationGridTile> around = new List<LocationGridTile>();
				targetTile?.PopulateTilesInRadius(around, 2, 0, includeCenterTile: true, includeTilesInDifferentStructure: true);
				if (around.Any(t => t.structure != null && t.structure.structureType.IsPlayerStructure()))
				{
					RuinarchPlus.Log?.Info($"A frozen explosion at {targetTile.localPlace} next to your buildings: frozen by {(froze == null || froze.Count == 0 ? "nobody named" : string.Join(", ", froze.Select(c => $"{c?.name} ({c?.faction?.name})")))}, "
						+ $"zapped by {(zapped == null ? "nobody named" : $"{zapped.name} ({zapped.faction?.name})")}; player's={isPlayerSource || ours}{(FriendlyExplosions.Enabled ? "" : " (fix off)")}.");
				}
				if (FriendlyExplosions.Enabled && ours)
				{
					isPlayerSource = true;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Friendly frozen explosion check failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(ChainedElectric), "ChainElectricEffect")]
	internal static class Fix_FriendlyExplosions_Electric
	{
		private static bool Prefix(ChainedElectric __instance, ITraitable traitable, Character responsibleCharacter) =>
			!FriendlyExplosions.Spares(traitable, responsibleCharacter, __instance.isPlayerSource);
	}
}
