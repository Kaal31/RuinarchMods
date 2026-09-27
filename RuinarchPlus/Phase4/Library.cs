using System.Collections.Generic;
using Locations.Settlements;

// Declared in the game's structure namespace, like MassGrave and TownHall: this type lives in
// the MOD assembly, and the Ruinarch.ModContent framework instantiates it through the
// registered factory.
namespace Inner_Maps.Location_Structures
{
	/// <summary>
	/// A Town's or City's Library (see <c>RuinarchPlus.Phase4.Records</c>): the village keeps
	/// its written record of the player's buildings in Books inside, and any villager of the
	/// village writes and reads there. Built through the game's own construction pipeline,
	/// borrowing the Workshop's prefab (look, footprint, build cost). A plain village building
	/// otherwise: it hires no worker and is damaged and destroyed like any other, with the
	/// Workshop's hit points.
	/// </summary>
	public class Library : ManMadeStructure
	{
		internal static readonly List<Library> Active = new List<Library>();

		public Library(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			SetMaxHPAndReset(8000);
			Active.Add(this);
		}

		public Library(Region location, SaveDataManMadeStructure data)
			: base(location, data)
		{
			SetMaxHP(8000);
			Active.Add(this);
		}

		/// <summary>The standing Library of <paramref name="settlement"/>, or null.</summary>
		internal static Library FindFor(BaseSettlement settlement)
		{
			for (int i = 0; i < Active.Count; i++)
			{
				Library library = Active[i];
				if (!library.hasBeenDestroyed && library.settlementLocation == settlement)
				{
					return library;
				}
			}
			return null;
		}

		protected override void AfterStructureDestruction(Character p_responsibleCharacter = null)
		{
			NPCSettlement settlement = settlementLocation as NPCSettlement;
			Active.Remove(this);
			base.AfterStructureDestruction(p_responsibleCharacter);
			global::RuinarchPlus.Phase4.Records.OnLibraryLost(this, settlement);
		}
	}
}
