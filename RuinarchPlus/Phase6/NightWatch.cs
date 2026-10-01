using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Locations.Settlements;
using Ruinarch.ModContent;
using RuinarchPlus.Phase5;
using UnityEngine;

namespace RuinarchPlus.Phase6
{
	/// <summary>
	/// Night watch (config: <c>nightWatchEnabled</c>). The game watches villages by day (the
	/// morning patrol party quest) and never at night: its night patrol quest and behaviour
	/// are only used by the debug console. Now a Town or City (or a capital) with at least 4
	/// fighters keeps a watch: one guard per 8 residents (1 to 3), its best fighters (Martial
	/// Arts), never the ruler or the faction leader, nobody in an active party. Guards live on
	/// the game's Nocturnal schedule (work 22:00 to 9:00, sleep by day) and, in their work
	/// hours at home, walk the village with the game's own <c>NightPatrolBehaviour</c>
	/// (aggressive: they attack hostiles they see). Checked every hour; guards who no longer
	/// qualify are replaced. Saved in <c>ModData/ruinarch.plus.watch.json</c>.
	/// </summary>
	public static class NightWatch
	{
		private const string SaveId = "ruinarch.plus.watch";
		private const int MinFighters = 4;
		private const int ResidentsPerGuard = 8;
		private const int MaxGuards = 3;

		private static readonly Dictionary<NPCSettlement, List<Character>> Watches = new Dictionary<NPCSettlement, List<Character>>();
		private static readonly Dictionary<Character, NPCSettlement> GuardOf = new Dictionary<Character, NPCSettlement>();

		internal static bool Enabled => RuinarchPlusConfig.Current.nightWatchEnabled;

		public static void Register()
		{
			ModSave.Register(SaveId, Save, Load);
		}

		internal static bool IsGuard(Character c) => c != null && GuardOf.ContainsKey(c);

		/// <summary>Test harness: the guards of <paramref name="s"/> (empty when none).</summary>
		internal static List<Character> GuardsOf(NPCSettlement s) => s != null && Watches.TryGetValue(s, out List<Character> g) ? g.ToList() : new List<Character>();

		private static bool KeepsWatch(NPCSettlement s)
		{
			return s != null && s.locationType == LOCATION_TYPE.VILLAGE && s.owner != null && s.owner.isMajorNonPlayer && s.cityCenter != null
				&& (SettlementTiers.Get(s) != SettlementTiers.Tier.Village || SettlementTiers.IsCapital(s));
		}

		private static bool CanGuard(NPCSettlement s, Character c)
		{
			return c != null && !c.isDead && c.homeSettlement == s && c.faction == s.owner && c.isNormalCharacter && c.race.IsSapient()
				&& !Phase4.LifeCycle.IsChild(c) && c.characterClass != null && c.characterClass.IsCombatant()
				&& c != s.ruler && !c.isFactionLeader && !c.traitContainer.HasTrait("Restrained")
				// Any party, gathering or away: its members follow the party's schedule (asleep at night).
				&& !c.partyComponent.hasParty;
		}

		private static bool Fighter(NPCSettlement s, Character c)
		{
			return c != null && !c.isDead && c.isNormalCharacter && c.faction == s.owner && c.characterClass != null && c.characterClass.IsCombatant();
		}

		// The game's night patrol ranks below visiting and socializing (200 against 800), so
		// an idle guard would wander off. The game never uses the behaviour itself, and its one
		// instance is shared (CharacterManager.GetCharacterBehaviourComponent): ranked above
		// visits, below putting out fires (950) and berserk fits (1085). Its PATROL jobs get the
		// same rank (the patch below): a queued job only runs when it ranks at least as high as
		// the villager's highest behaviour (Character.HasSameOrHigherPriorityJobThanBehaviour),
		// so at the job's own 450 the jobs would pile up unrun.
		internal const int PatrolPriority = 850;
		private static bool _ranked;

		private static void RankPatrol()
		{
			if (_ranked || CharacterManager.Instance == null)
			{
				return;
			}
			CharacterBehaviour patrol = CharacterManager.Instance.GetCharacterBehaviourComponent(typeof(NightPatrolBehaviour));
			if (patrol != null)
			{
				AccessTools.Property(typeof(CharacterBehaviour), nameof(CharacterBehaviour.priority)).SetValue(patrol, PatrolPriority);
				_ranked = true;
			}
		}

		/// <summary>Hourly: name, replace and release guards; put those on duty at night on patrol.</summary>
		internal static void Check()
		{
			RankPatrol();
			List<BaseSettlement> settlements = GridMap.Instance?.mainRegion?.settlementsInRegion;
			if (settlements == null)
			{
				return;
			}
			foreach (NPCSettlement s in settlements.OfType<NPCSettlement>().Concat(Watches.Keys).Distinct().ToList())
			{
				try
				{
					Update(s);
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning($"Night watch check failed for {s?.name}: {e.Message}");
				}
			}
		}

		private static void Update(NPCSettlement s)
		{
			Watches.TryGetValue(s, out List<Character> guards);
			guards = guards ?? new List<Character>();
			int want = 0;
			if (Enabled && KeepsWatch(s) && s.residents.Count(c => Fighter(s, c)) >= MinFighters)
			{
				int residents = s.residents.Count(c => c != null && !c.isDead && c.isNormalCharacter && !Phase4.LifeCycle.IsChild(c));
				want = Mathf.Clamp(residents / ResidentsPerGuard, 1, MaxGuards);
			}
			foreach (Character g in guards.Where(g => !CanGuard(s, g)).ToList())
			{
				Release(s, guards, g, Why(s, g));
			}
			while (guards.Count > want)
			{
				Release(s, guards, guards[guards.Count - 1], want == 0 ? "no watch kept" : "fewer guards needed");
			}
			bool first = guards.Count == 0;
			List<Character> named = new List<Character>();
			if (guards.Count < want)
			{
				// Not someone away on the village's errands (a hunt, a trade trip): they would
				// start the watch away from home. Those errands in turn never take a guard.
				foreach (Character c in s.residents.Where(c => CanGuard(s, c) && !GuardOf.ContainsKey(c) && !Phase5.Hunters.IsHunting(c) && !Phase5.Traders.IsTrading(c))
					.OrderByDescending(c => c.TryGetTalentLevel(CHARACTER_TALENT.Martial_Arts)).ThenBy(_ => UnityEngine.Random.value).Take(want - guards.Count).ToList())
				{
					guards.Add(c);
					GuardOf[c] = s;
					RefreshSchedule(c);
					named.Add(c);
				}
			}
			if (guards.Count == 0)
			{
				Watches.Remove(s);
				return;
			}
			Watches[s] = guards;
			if (named.Count > 0)
			{
				string who = string.Join(", ", named.Select(c => c.name));
				if (first)
				{
					Phase2.Curfew.Announce("{0} has set a night watch: " + who + ".", s);
				}
				else
				{
					RuinarchPlus.Log?.Info($"{s.name}'s night watch takes on {who}.");
				}
			}
			int tick = GameManager.Instance.Today().tick;
			foreach (Character g in guards)
			{
				bool onDuty = g.dailyScheduleComponent.schedule.GetScheduleType(tick) == DAILY_SCHEDULE.Work
					&& g.currentSettlement == s && g.hasMarker && g.limiterComponent.canPerform;
				bool patrolling = g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour));
				if (onDuty && !patrolling)
				{
					g.behaviourComponent.AddBehaviourComponent(typeof(NightPatrolBehaviour));
				}
				else if (!onDuty && patrolling)
				{
					g.behaviourComponent.RemoveBehaviourComponent(typeof(NightPatrolBehaviour));
				}
			}
		}

		// Why <paramref name="g"/> can no longer guard <paramref name="s"/> (for mods.log).
		private static string Why(NPCSettlement s, Character g)
		{
			return g == null || g.isDead ? "dead" : g.homeSettlement != s ? "moved away" : g.faction != s.owner ? "left the faction"
				: g.characterClass == null || !g.characterClass.IsCombatant() ? "no longer a fighter" : g == s.ruler || g.isFactionLeader ? "now rules"
				: g.traitContainer.HasTrait("Restrained") ? "held" : g.partyComponent.hasParty ? "in a party" : "no longer fit";
		}

		private static void Release(NPCSettlement s, List<Character> guards, Character g, string why)
		{
			guards.Remove(g);
			GuardOf.Remove(g);
			if (g == null)
			{
				return;
			}
			if (g.behaviourComponent.HasBehaviour(typeof(NightPatrolBehaviour)))
			{
				g.behaviourComponent.RemoveBehaviourComponent(typeof(NightPatrolBehaviour));
			}
			if (!g.isDead)
			{
				RefreshSchedule(g);
				RuinarchPlus.Log?.Info($"{g.name} is released from {s.name}'s night watch ({why}).");
			}
		}

		private static readonly System.Reflection.MethodInfo UpdateSchedule = AccessTools.Method(typeof(DailyScheduleComponent), "UpdateDailySchedule");

		// Recompute the schedule now (the postfix below gives a guard the Nocturnal one).
		private static void RefreshSchedule(Character c)
		{
			UpdateSchedule?.Invoke(c.dailyScheduleComponent, new object[] { c });
		}

		private static readonly System.Reflection.MethodInfo SetSchedule = AccessTools.Method(typeof(DailyScheduleComponent), "SetSchedule");

		internal static void ScheduleFor(DailyScheduleComponent component, Character c)
		{
			if (IsGuard(c) && !c.partyComponent.hasParty && !(component.schedule is NocturnalSchedule))
			{
				SetSchedule?.Invoke(component, new object[] { CharacterManager.Instance.GetDailySchedule<NocturnalSchedule>() });
			}
		}

		// ---- persistence -------------------------------------------------------------------
		// One "settlementId|guardId,guardId" per watch.

		private static string Save()
		{
			WatchSaveData file = new WatchSaveData();
			foreach (KeyValuePair<NPCSettlement, List<Character>> kv in Watches)
			{
				if (kv.Key != null && kv.Value.Count > 0)
				{
					file.watches.Add(kv.Key.persistentID + "|" + string.Join(",", kv.Value.Where(c => c != null).Select(c => c.persistentID)));
				}
			}
			return JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			Watches.Clear();
			GuardOf.Clear();
			if (string.IsNullOrEmpty(json))
			{
				return;
			}
			foreach (string entry in JsonUtility.FromJson<WatchSaveData>(json)?.watches ?? new List<string>())
			{
				string[] p = entry.Split('|');
				if (p.Length < 2 || !(LandmarkManager.Instance.GetSettlementByPersistentID(p[0]) is NPCSettlement s))
				{
					continue;
				}
				List<Character> guards = p[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).Where(c => c != null && !c.isDead).ToList();
				if (guards.Count == 0)
				{
					continue;
				}
				Watches[s] = guards;
				foreach (Character g in guards)
				{
					GuardOf[g] = s;
					RefreshSchedule(g);
				}
			}
			RuinarchPlus.Log?.Info($"Night watch loaded: {Watches.Count} settlement(s), {GuardOf.Count} guard(s).");
		}
	}

	[Serializable]
	public class WatchSaveData
	{
		public List<string> watches = new List<string>();
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class NightWatch_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 == 0)
				{
					NightWatch.Check();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Night watch hourly failed: " + e.Message);
			}
		}
	}

	// The night patrol's PATROL job ranks with the behaviour (see NightWatch.PatrolPriority).
	[HarmonyPatch(typeof(NightPatrolBehaviour), nameof(NightPatrolBehaviour.TryDoBehaviour))]
	internal static class NightWatch_PatrolJobRank
	{
		private static void Postfix(JobQueueItem producedJob)
		{
			producedJob?.SetPriority(NightWatch.PatrolPriority);
		}
	}

	// A guard not in a party keeps the Nocturnal schedule, whatever else the game recomputes.
	[HarmonyPatch(typeof(DailyScheduleComponent), "UpdateDailySchedule")]
	internal static class NightWatch_Schedule
	{
		private static void Postfix(DailyScheduleComponent __instance, Character p_character)
		{
			try
			{
				NightWatch.ScheduleFor(__instance, p_character);
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Night watch schedule failed: " + e.Message);
			}
		}
	}
}
