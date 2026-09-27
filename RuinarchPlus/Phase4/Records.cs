using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Ruinarch.ModContent;
using RuinarchPlus.Phase2;
using RuinarchPlus.Phase3;
using RuinarchPlus.Phase5;
using UnityEngine;

namespace RuinarchPlus.Phase4
{
	/// <summary>
	/// Records (config: <c>recordsEnabled</c>, with <c>knowledgeEnabled</c>).
	///
	/// Knowledge of the player's buildings lives in people (Phase3/Knowledge.cs) and now also in
	/// records: Books (the game's own BOOK object) in dwellings and in a village's Library.
	/// - A villager standing in their own dwelling writes into the household's Book every
	///   building they remember and have told at home; a household without a Book starts one.
	/// - A Town or City (Phase5/SettlementTiers.cs) builds a Library, borrowing the Workshop's
	///   prefab, and the mod puts <c>libraryBooks</c> Books in it. Any villager of the village
	///   writes and reads there; in their free time a villager who does not remember something
	///   the Library holds may go and read (<c>libraryVisitChance</c> % an hour).
	/// - Beside a record, a villager who does not remember one of its buildings reads it
	///   (<c>readChance</c> % an hour per entry) and remembers it again. Records never count by
	///   themselves: only a reader turns them back into knowledge.
	/// - A burned or broken Book takes its entries with it; a Library losing its last Book, or
	///   destroyed, is announced. A record is only ever rewritten from living memory.
	/// Records are stored inside the player's save (<c>ModData/ruinarch.plus.records.json</c>).
	/// </summary>
	internal static class Records
	{
		public const string LibraryId = "ruinarch.plus.library";
		private const string SaveId = "ruinarch.plus.records";

		private sealed class Record
		{
			internal LocationStructure Holder;
			internal readonly List<TileObject> Books = new List<TileObject>();
			internal readonly HashSet<LocationStructure> Entries = new HashSet<LocationStructure>();
			internal bool IsLibrary => Holder is Library;
		}

		internal static ModBuilding LibraryBuilding;

		private static readonly Dictionary<LocationStructure, Record> ByHolder = new Dictionary<LocationStructure, Record>();

		// The game hour each villager last rolled for a Library visit.
		private static readonly Dictionary<Character, long> VisitRolled = new Dictionary<Character, long>();

		internal static bool Enabled => Knowledge.Enabled && RuinarchPlusConfig.Current.recordsEnabled;

		private static long Hour => MissingPersons.Now / GameManager.ticksPerHour;

		internal static void Register()
		{
			try
			{
				ModContent.RegisterStructure(new StructureRegistration
				{
					Id = LibraryId,
					DisplayName = "Library",
					Factory = (type, region) => new Library(type, region),
					LoadFactory = (type, region, save) => new Library(region, (SaveDataManMadeStructure)save),
					// Borrow the Workshop's prefab: the one building every culture has a prefab for.
					PrefabSource = STRUCTURE_TYPE.WORKSHOP,
					Skill = null,
					UnlockWith = PLAYER_SKILL_TYPE.NONE,
					IsPlayerStructure = false,
					IsVillageStructure = true
				});
				LibraryBuilding = ModBuildings.Add(LibraryId, "Library", STRUCTURE_TYPE.WORKSHOP);
				ModSave.Register(SaveId, Save, Load);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Library registration failed: " + e);
			}
		}

		// ---- queries -----------------------------------------------------------------------

		/// <summary>The buildings the record in <paramref name="holder"/> (a dwelling or a
		/// Library) names, or null if it keeps none.</summary>
		internal static HashSet<LocationStructure> RecordOf(LocationStructure holder)
		{
			return holder != null && ByHolder.TryGetValue(holder, out Record r) ? new HashSet<LocationStructure>(r.Entries) : null;
		}

		/// <summary>The standing Books of the record in <paramref name="holder"/>.</summary>
		internal static List<TileObject> BooksOf(LocationStructure holder)
		{
			return holder != null && ByHolder.TryGetValue(holder, out Record r) ? r.Books.Where(b => Stands(b, holder)).ToList() : new List<TileObject>();
		}

		/// <summary>Homes of <paramref name="faction"/> whose records name something, and the
		/// villages whose Library does (the bookmarks panel).</summary>
		internal static void Summary(Faction faction, out int homes, out List<NPCSettlement> libraries)
		{
			homes = 0;
			libraries = new List<NPCSettlement>();
			if (!Enabled || faction == null)
			{
				return;
			}
			foreach (Record r in ByHolder.Values)
			{
				if (r.Entries.Count == 0 || r.Holder.hasBeenDestroyed || !(r.Holder.settlementLocation is NPCSettlement v) || v.owner != faction)
				{
					continue;
				}
				if (r.IsLibrary)
				{
					libraries.Add(v);
				}
				else
				{
					homes++;
				}
			}
		}

		private static bool Stands(TileObject book, LocationStructure holder)
		{
			return book != null && book.gridTileLocation != null && book.gridTileLocation.structure == holder;
		}

		// ---- writing and reading -----------------------------------------------------------

		/// <summary><paramref name="c"/> writes into the record in <paramref name="holder"/> every
		/// standing building they remember and have told at home. A record with no Book left
		/// gets a new one first (none if there is no free spot inside). True if anything new
		/// was written.</summary>
		internal static bool Write(Character c, LocationStructure holder)
		{
			List<LocationStructure> news = Knowledge.NewsOf(c).Where(s => !Knowledge.Carries(c, s)).ToList();
			ByHolder.TryGetValue(holder, out Record r);
			if (news.Count == 0 || (r != null && news.All(r.Entries.Contains)))
			{
				return false;
			}
			if (r == null)
			{
				r = new Record { Holder = holder };
			}
			r.Books.RemoveAll(b => !Stands(b, holder));
			if (r.Books.Count == 0)
			{
				TileObject book = PlaceBook(holder);
				if (book == null)
				{
					return false;
				}
				r.Books.Add(book);
			}
			bool first = !r.IsLibrary && r.Entries.Count == 0;
			ByHolder[holder] = r;
			r.Entries.UnionWith(news);
			if (first && holder.settlementLocation is NPCSettlement village)
			{
				Curfew.Note("A household in {0} started keeping a record of your buildings.", village);
			}
			return true;
		}

		/// <summary><paramref name="c"/>, beside the record in <paramref name="holder"/>, reads what
		/// they do not remember: each entry at <c>readChance</c> %, every one when
		/// <paramref name="force"/> (test harness). Returns what they learned.</summary>
		internal static List<LocationStructure> ReadFrom(Character c, LocationStructure holder, bool force = false)
		{
			List<LocationStructure> learned = new List<LocationStructure>();
			if (!ByHolder.TryGetValue(holder, out Record r) || r.Books.All(b => !Stands(b, holder)))
			{
				return learned;
			}
			int chance = RuinarchPlusConfig.Current.readChance;
			foreach (LocationStructure s in r.Entries.ToList())
			{
				if (!Knowledge.Remembers(c, s) && (force || UnityEngine.Random.Range(0, 100) < chance) && Knowledge.Read(c, s))
				{
					learned.Add(s);
					if (r.IsLibrary && holder.settlementLocation is NPCSettlement village)
					{
						Curfew.Note("{0} read of your {1} in {2}'s Library.", c, s, village);
					}
					else
					{
						Curfew.Note("{0} read of your {1} at home.", c, s);
					}
				}
			}
			return learned;
		}

		// ---- books -------------------------------------------------------------------------

		// A Book goes on a free walkable tile inside, never one of the last two: villagers
		// must still be able to walk in (a Workshop-sized Library has only a few).
		private const int KeepFree = 2;

		private static TileObject PlaceBook(LocationStructure holder)
		{
			List<LocationGridTile> free = holder.passableTiles?.Where(t => t != null && t.structure == holder && !t.isOccupied).ToList();
			if (free == null || free.Count <= KeepFree)
			{
				return null;
			}
			TileObject book = InnerMapManager.Instance.CreateNewTileObject<TileObject>(TILE_OBJECT_TYPE.BOOK);
			return holder.AddPOI(book, free[UnityEngine.Random.Range(0, free.Count)]) ? book : null;
		}

		// A Library seen built for the first time gets its Books.
		private static void Furnish(Library library)
		{
			if (ByHolder.ContainsKey(library))
			{
				return;
			}
			Record r = new Record { Holder = library };
			for (int i = 0; i < RuinarchPlusConfig.Current.libraryBooks; i++)
			{
				TileObject book = PlaceBook(library);
				if (book == null)
				{
					break;
				}
				r.Books.Add(book);
			}
			ByHolder[library] = r;
			RuinarchPlus.Log?.Info($"The Library of {library.settlementLocation?.name ?? "a village"} holds {r.Books.Count} Book(s).");
		}

		// ---- the Library -------------------------------------------------------------------

		private static bool NeedsLibrary(NPCSettlement s)
		{
			return s.owner != null && s.owner.isMajorFaction && s.cityCenter != null
				&& SettlementTiers.Get(s) != SettlementTiers.Tier.Village
				&& Library.FindFor(s) == null
				&& !ModBuildings.HasPendingFor(s, LibraryBuilding)
				// The game allows one blueprint job per settlement at a time: wait our turn.
				&& !s.HasJob(JOB_TYPE.PLACE_BLUEPRINT);
		}

		/// <summary>Build a Library instantly (debug menu, test harness), with its Books. Null if
		/// the village has no room.</summary>
		internal static LocationStructure InstantBuildLibrary(NPCSettlement settlement)
		{
			Library existing = Library.FindFor(settlement);
			if (existing != null)
			{
				return existing;
			}
			Library built = ModBuildings.InstantBuild(settlement, LibraryBuilding, STRUCTURE_TYPE.WORKSHOP) as Library;
			if (built != null)
			{
				Furnish(built);
			}
			return built;
		}

		/// <summary>Empty every record kept by <paramref name="faction"/>'s villages (test harness);
		/// the Books stay, blank, so a Library is not furnished again.</summary>
		internal static void Forget(Faction faction)
		{
			foreach (Record r in ByHolder.Values.Where(r => r.Holder.settlementLocation is NPCSettlement v && v.owner == faction))
			{
				r.Entries.Clear();
			}
		}

		internal static void OnLibraryLost(Library library, NPCSettlement settlement)
		{
			if (!ByHolder.TryGetValue(library, out Record r))
			{
				return;
			}
			ByHolder.Remove(library);
			List<LocationStructure> lost = r.Entries.Where(Knowledge.Standing).ToList();
			if (Enabled && settlement != null && lost.Count > 0)
			{
				Curfew.Announce("{0}'s Library was destroyed; its records of " + Your(lost) + " are lost.", settlement);
			}
		}

		// "your Portal", "your Portal and Corrupt Kennel", "your Portal, Kennel and Lair".
		private static string Your(List<LocationStructure> structures)
		{
			List<string> names = Knowledge.Names(structures).ToList();
			return "your " + (names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1]);
		}

		// ---- hourly ------------------------------------------------------------------------

		internal static void HourlyCheck()
		{
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (!Enabled || settlements == null)
			{
				return;
			}
			Prune();
			foreach (NPCSettlement village in settlements.OfType<NPCSettlement>().ToList())
			{
				if (village.locationType != LOCATION_TYPE.VILLAGE || village.owner == null || !village.owner.isMajorNonPlayer)
				{
					continue;
				}
				try
				{
					Visit(village);
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Records check failed for {village.name}: {e.Message}");
				}
			}
		}

		private static void Visit(NPCSettlement village)
		{
			if (RuinarchPlusConfig.Current.settlementTiersEnabled && NeedsLibrary(village))
			{
				string prefab = ModBuildings.QueueBlueprint(village, LibraryBuilding, STRUCTURE_TYPE.WORKSHOP);
				if (prefab != null)
				{
					RuinarchPlus.Log?.Info($"{village.name} is a {SettlementTiers.Get(village)}: queued a Library blueprint ({prefab}).");
				}
			}
			Library library = Library.FindFor(village);
			if (library != null)
			{
				Furnish(library);
			}
			foreach (Character c in village.residents.ToList())
			{
				if (!Knowledge.CanRemember(c) || !Knowledge.Counts(c))
				{
					continue;
				}
				LocationStructure at = c.currentStructure;
				bool home = at != null && at == c.homeStructure && at.structureType == STRUCTURE_TYPE.DWELLING && !at.hasBeenDestroyed;
				if (home || (library != null && at == library))
				{
					Write(c, at);
					ReadFrom(c, at);
				}
			}
		}

		// Books that left their holder, holders destroyed, entries destroyed. A record with no
		// Book left loses its entries; a Library's loss is announced, a household's is not.
		private static void Prune()
		{
			foreach (Character c in VisitRolled.Keys.Where(c => c == null || c.isDead).ToList())
			{
				VisitRolled.Remove(c);
			}
			foreach (Record r in ByHolder.Values.ToList())
			{
				if (r.Holder.hasBeenDestroyed)
				{
					ByHolder.Remove(r.Holder);
					continue;
				}
				r.Entries.RemoveWhere(s => !Knowledge.Standing(s));
				r.Books.RemoveAll(b => !Stands(b, r.Holder));
				if (r.Books.Count > 0)
				{
					continue;
				}
				if (r.IsLibrary)
				{
					if (r.Entries.Count > 0 && r.Holder.settlementLocation is NPCSettlement village)
					{
						Curfew.Announce("{0}'s Library has lost its last Book; its records of " + Your(r.Entries.ToList()) + " are lost.", village);
					}
					// The Library stays known (it is not furnished again); its record is empty
					// until someone who remembers writes a new Book.
					r.Entries.Clear();
				}
				else
				{
					ByHolder.Remove(r.Holder);
				}
			}
		}

		// ---- visits ------------------------------------------------------------------------

		/// <summary>In free time, a villager whose village's Library names something they do not
		/// remember rolls once an hour to go and read. The tile to walk to, or null.</summary>
		internal static LocationGridTile VisitSpot(Character c)
		{
			// Called every tick for every idle villager: most villages have no Library, so that
			// is asked first.
			if (!Enabled || !(c?.homeSettlement is NPCSettlement home))
			{
				return null;
			}
			Library library = Library.FindFor(home);
			if (library == null || c.currentStructure == library || !ByHolder.TryGetValue(library, out Record r) || r.Entries.Count == 0
				|| !Knowledge.CanRemember(c) || !Knowledge.Counts(c) || Curfew.Binds(c)
				|| c.dailyScheduleComponent.schedule.GetScheduleType(GameManager.Instance.currentTick) != DAILY_SCHEDULE.Free_Time
				|| !r.Entries.Any(s => !Knowledge.Remembers(c, s)))
			{
				return null;
			}
			long hour = Hour;
			if (VisitRolled.TryGetValue(c, out long rolled) && rolled == hour)
			{
				return null;
			}
			VisitRolled[c] = hour;
			if (UnityEngine.Random.Range(0, 100) >= RuinarchPlusConfig.Current.libraryVisitChance)
			{
				return null;
			}
			List<LocationGridTile> free = library.passableTiles?.Where(t => t != null && t.structure == library && !t.isOccupied).ToList();
			return free == null || free.Count == 0 ? null : free[UnityEngine.Random.Range(0, free.Count)];
		}

		// ---- persistence -------------------------------------------------------------------
		// One "kind|holderId|bookId,bookId|structureId,structureId" string per record, kind H
		// (a home) or L (a Library). Strings only (JsonUtility drops lists of mod classes).
		// Always written: a missing file means a save from before records.

		private static string Save()
		{
			RecordsSaveData file = new RecordsSaveData();
			foreach (Record r in ByHolder.Values)
			{
				if (r.Holder == null || r.Holder.hasBeenDestroyed)
				{
					continue;
				}
				string books = string.Join(",", r.Books.Where(b => Stands(b, r.Holder)).Select(b => b.persistentID));
				string entries = string.Join(",", r.Entries.Where(Knowledge.Standing).Select(s => s.persistentID));
				file.records.Add($"{(r.IsLibrary ? "L" : "H")}|{r.Holder.persistentID}|{books}|{entries}");
			}
			return JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			ByHolder.Clear();
			VisitRolled.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			RecordsSaveData file = JsonUtility.FromJson<RecordsSaveData>(json);
			int entries = 0;
			foreach (string line in file?.records ?? new List<string>())
			{
				string[] parts = line.Split('|');
				LocationStructure holder = parts.Length == 4 ? DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(parts[1]) : null;
				if (holder == null || holder.hasBeenDestroyed)
				{
					continue;
				}
				Record r = new Record { Holder = holder };
				foreach (string id in parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					TileObject book = DatabaseManager.Instance.tileObjectDatabase.GetTileObjectByPersistentIDSafe(id);
					if (Stands(book, holder))
					{
						r.Books.Add(book);
					}
				}
				foreach (string id in parts[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					LocationStructure s = DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(id);
					if (Knowledge.Standing(s))
					{
						r.Entries.Add(s);
					}
				}
				if (r.Books.Count == 0 && !r.IsLibrary)
				{
					continue;
				}
				if (r.Books.Count == 0)
				{
					r.Entries.Clear();
				}
				ByHolder[holder] = r;
				entries += r.Entries.Count;
			}
			RuinarchPlus.Log?.Info($"Records loaded: {ByHolder.Values.Count(r => !r.IsLibrary)} home(s) and {ByHolder.Values.Count(r => r.IsLibrary)} Library(ies) keep {entries} entr(ies) in all.");
		}
	}

	[Serializable]
	public class RecordsSaveData
	{
		public List<string> records = new List<string>();
	}

	// Free time: a villager may go to the Library to read what they do not remember. Only
	// runs while the villager is idle (the game plans behaviour only then), so needs, work and
	// combat come first.
	[HarmonyPatch(typeof(BehaviourComponent), nameof(BehaviourComponent.RunBehaviour))]
	internal static class Records_LibraryVisit
	{
		private static bool Prefix(BehaviourComponent __instance, ref string __result)
		{
			try
			{
				Character c = __instance.owner;
				LocationGridTile spot = Records.VisitSpot(c);
				if (spot == null || !c.jobComponent.CreateGoToJob(JOB_TYPE.VISIT_STRUCTURE, spot, out JobQueueItem job) || job == null)
				{
					return true;
				}
				c.jobQueue.AddJobInQueue(job);
				__result = "Going to read in the Library.";
				return false;
			}
			catch
			{
				return true;
			}
		}
	}
}
