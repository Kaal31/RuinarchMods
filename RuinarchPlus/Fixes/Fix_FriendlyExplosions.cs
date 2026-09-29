using HarmonyLib;
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
	internal static class FriendlyExplosions
	{
		internal static bool Enabled => RuinarchPlusConfig.Current.friendlyExplosionsSpareBuildings;

		// True when the explosion should leave this object alone.
		internal static bool Spares(ITraitable traitable, Character responsible, bool isPlayerSource)
		{
			return Enabled && traitable is TileObject t
				&& (t.tileObjectType.IsDemonicStructureTileObject() || t.tileObjectType == TILE_OBJECT_TYPE.STRUCTURE_BLOCKER_TILE_OBJECT)
				&& (isPlayerSource || responsible?.faction?.isPlayerFaction == true);
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

	[HarmonyPatch(typeof(ChainedElectric), "ChainElectricEffect")]
	internal static class Fix_FriendlyExplosions_Electric
	{
		private static bool Prefix(ChainedElectric __instance, ITraitable traitable, Character responsibleCharacter) =>
			!FriendlyExplosions.Spares(traitable, responsibleCharacter, __instance.isPlayerSource);
	}
}
