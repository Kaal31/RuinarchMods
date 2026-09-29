using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using Traits;

namespace RuinarchPlus.Phase5
{
	internal enum UprisingKind { Brawl, Assassination, Jailing, CivilWar }

	/// <summary>An uprising in progress (<see cref="Unrest"/> starts it; not saved).</summary>
	internal sealed class Uprising
	{
		internal Character Leader;
		internal Character Ruler;
		internal List<Character> Rebels;
		internal List<Character> Loyal;
		internal int Hours;
		// Assassination: the hour the plotter struck (the ruler asleep), or -1 while waiting.
		internal int StruckAt = -1;
		internal UprisingKind Kind;
	}

	/// <summary>
	/// How an uprising plays out (config: <c>uprisingKindsEnabled</c>, else always a brawl).
	/// The kind is a weighted roll the people shape: a brawl (50; the leader Diplomatic or a
	/// Coward x2), an assassination plot (15; the leader Evil, Psychopath, Ruthless or
	/// Treacherous x3, holding a grudge against the ruler x2), jailing (20 with a standing
	/// prison; the ruler wanted by their own faction x3) or a civil war (15 with 12+ adults and
	/// 4+ on each side; 20+ villagers x2).
	/// - Brawl: the game's knockout fights; the ruler down, the leader takes the rule.
	/// - Assassination: only the leader acts. They wait (up to 48 hours) until the ruler is
	///   asleep, stay up (woken if they sleep too) and strike with the game's own
	///   assassination job (an open, lethal fight: awake, rulers beat most plotters). The
	///   ruler killed by them: unseen, the leader takes the rule; seen (a Murder on record),
	///   the leader is a criminal and the game's own succession stands. The plotter killed,
	///   beaten or caught, or a day after striking: the plot fails.
	/// - Jailing: a brawl, then the rebels take the old ruler to the prison (Restrained, the
	///   game's carry and drop). The mod holds them: villagers leave them tied (friends may
	///   free them), and after 48 hours in the prison whoever rules decides by what they think
	///   of them: executed, exiled or released. The game's crime system is not used: it never
	///   charges a ruler, and a charge needs an action behind it.
	/// - Civil war: the same camps fight to the death; the losing side's survivors are exiled
	///   (the game's own exile). 24 hours without a winner: the ruler holds.
	/// </summary>
	internal static class Uprisings
	{
		private const long TicksPerHour = 20;
		private const int BrawlHours = 12;
		private const int LongHours = 24;
		private const int HeldHours = 48;
		private const int DeliverHours = 12;
		private const int PlotWaitHours = 48;

		private sealed class Held
		{
			internal NPCSettlement Village;
			internal long Since;
			internal bool Delivered;
		}

		private static readonly Dictionary<Character, Held> HeldRulers = new Dictionary<Character, Held>();
		private static readonly Dictionary<NPCSettlement, UprisingKind> Forced = new Dictionary<NPCSettlement, UprisingKind>();
		// A ruler under a plot, and who killed them (Character.Death does not keep it).
		private static readonly Dictionary<Character, Character> KilledBy = new Dictionary<Character, Character>();
		private static readonly HashSet<Character> PlotTargets = new HashSet<Character>();

		internal static bool Enabled => RuinarchPlusConfig.Current.uprisingKindsEnabled;

		private static long Now => Unrest.Now;

		// ---- choosing the kind -------------------------------------------------------------

		internal static Dictionary<UprisingKind, float> Weights(NPCSettlement s, Uprising u)
		{
			Dictionary<UprisingKind, float> w = new Dictionary<UprisingKind, float>
			{
				[UprisingKind.Brawl] = 50f,
				[UprisingKind.Assassination] = 0f,
				[UprisingKind.Jailing] = 0f,
				[UprisingKind.CivilWar] = 0f,
			};
			if (!Enabled)
			{
				return w;
			}
			Character leader = u.Leader;
			Character ruler = u.Ruler;
			if (leader.traitContainer.HasTrait("Diplomatic", "Coward"))
			{
				w[UprisingKind.Brawl] *= 2f;
			}
			w[UprisingKind.Assassination] = 15f;
			if (leader.traitContainer.HasTrait("Evil", "Psychopath", "Ruthless", "Treacherous"))
			{
				w[UprisingKind.Assassination] *= 3f;
			}
			if (leader.relationshipContainer.HasGrudgeAgainst(ruler))
			{
				w[UprisingKind.Assassination] *= 2f;
			}
			if (s.prison != null && !s.prison.hasBeenDestroyed)
			{
				w[UprisingKind.Jailing] = s.owner != null && ruler.crimeComponent.IsWantedBy(s.owner) ? 60f : 20f;
			}
			List<Character> villagers = Famine.Villagers(s);
			if (villagers.Count(c => !Phase4.LifeCycle.IsChild(c)) >= 12 && u.Rebels.Count >= 4 && u.Loyal.Count >= 4)
			{
				w[UprisingKind.CivilWar] = villagers.Count >= 20 ? 30f : 15f;
			}
			return w;
		}

		private static UprisingKind Roll(Dictionary<UprisingKind, float> w)
		{
			float total = w.Values.Sum();
			float pick = UnityEngine.Random.Range(0f, total);
			foreach (KeyValuePair<UprisingKind, float> kv in w)
			{
				if (kv.Value <= 0f)
				{
					continue;
				}
				if (pick < kv.Value)
				{
					return kv.Key;
				}
				pick -= kv.Value;
			}
			return UprisingKind.Brawl;
		}

		private static string Label(UprisingKind k) => k == UprisingKind.CivilWar ? "civil war" : k == UprisingKind.Jailing ? "jail" : k.ToString().ToLowerInvariant();

		// ---- start and advance -------------------------------------------------------------

		internal static void Start(NPCSettlement s, Uprising u)
		{
			Dictionary<UprisingKind, float> w = Weights(s, u);
			bool forced = Forced.TryGetValue(s, out UprisingKind kind);
			if (forced)
			{
				Forced.Remove(s);
			}
			else
			{
				kind = Roll(w);
			}
			u.Kind = kind;
			RuinarchPlus.Log?.Info($"Uprising in {s.name}: " + string.Join(", ", w.Select(kv => $"{Label(kv.Key)} {kv.Value:0}")) + $" -> {Label(kind)}{(forced ? " (forced)" : "")}");
			switch (kind)
			{
				case UprisingKind.Assassination:
					PlotTargets.Add(u.Ruler);
					Phase2.Curfew.Announce("{0} is plotting against {1} in {2}.", u.Leader, u.Ruler, s);
					break;
				case UprisingKind.CivilWar:
					Phase2.Curfew.Announce($"Civil war breaks out in {{0}}! ({u.Rebels.Count} rebels against {u.Loyal.Count} loyal to {{1}})", s, u.Ruler);
					Engage(u, lethal: true);
					break;
				default:
					Phase2.Curfew.Announce($"{{0}} leads an uprising against {{1}} in {{2}}! ({u.Rebels.Count} rise against the ruler, {u.Loyal.Count} stand by them.)", u.Leader, u.Ruler, s);
					Engage(u, lethal: false);
					break;
			}
		}

		/// <summary>One hour of the uprising; true when it is over (the calm is set).</summary>
		internal static bool Advance(NPCSettlement s, Uprising u)
		{
			u.Hours++;
			switch (u.Kind)
			{
				case UprisingKind.Assassination:
					return AdvancePlot(s, u);
				case UprisingKind.CivilWar:
					return AdvanceWar(s, u);
				default:
					return AdvanceBrawl(s, u);
			}
		}

		internal static bool Down(Character c)
		{
			return c == null || c.isDead || !c.hasMarker || !c.limiterComponent.canPerform || c.traitContainer.HasTrait("Unconscious", "Restrained");
		}

		// The one to take the rule: the leader, or, down too, the rebel still standing who
		// thinks least of the ruler.
		private static Character Taker(Uprising u)
		{
			return !Down(u.Leader) ? u.Leader
				: u.Rebels.Where(c => !Down(c)).OrderBy(c => c.relationshipContainer.GetTotalOpinion(u.Ruler)).FirstOrDefault();
		}

		private static void Engage(Uprising u, bool lethal)
		{
			List<Character> defenders = u.Loyal.Concat(new[] { u.Ruler }).Where(c => !Down(c)).ToList();
			List<Character> rebels = u.Rebels.Where(c => !Down(c)).ToList();
			foreach (Character r in rebels)
			{
				foreach (Character d in defenders)
				{
					r.combatComponent.Fight(d, lethal ? CombatManager.Hostility : CombatManager.Anger, null, isLethal: lethal);
				}
			}
			foreach (Character d in defenders)
			{
				foreach (Character r in rebels)
				{
					d.combatComponent.Fight(r, lethal ? CombatManager.Hostility : CombatManager.Anger, null, isLethal: lethal);
				}
			}
		}

		private static void Disengage(Uprising u)
		{
			List<Character> all = u.Rebels.Concat(u.Loyal).Concat(new[] { u.Ruler }).Where(c => c != null && !c.isDead).ToList();
			foreach (Character a in all)
			{
				foreach (Character b in all)
				{
					if (a != b)
					{
						a.combatComponent.RemoveHostileInRange(b);
					}
				}
			}
		}

		private static void Grudge(Character holder, Character against)
		{
			if (holder != null && against != null && !holder.isDead && !against.isDead && !holder.relationshipContainer.HasGrudgeAgainst(against))
			{
				holder.relationshipContainer.SetHasGrudgeAgainst(holder, against, p_state: true);
			}
		}

		// Brawl and jailing: the game's knockout fights.
		private static bool AdvanceBrawl(NPCSettlement s, Uprising u)
		{
			bool rulerDown = Down(u.Ruler) || s.ruler != u.Ruler;
			bool rebelsDown = u.Rebels.All(Down);
			Character taker = rulerDown ? Taker(u) : null;
			if (taker != null)
			{
				Disengage(u);
				bool jailed = u.Kind == UprisingKind.Jailing && CanJail(s, u.Ruler);
				TakeRule(s, taker, u.Ruler, announce: !jailed);
				if (jailed)
				{
					Jail(s, taker, u);
				}
				foreach (Character c in u.Loyal.Where(c => !c.isDead))
				{
					c.relationshipContainer.AdjustOpinion(c, taker, "Uprising", -30, "overthrew the ruler", createJobsOnReduce: false);
				}
				Unrest.Calm(s, won: true);
				return true;
			}
			if (rebelsDown || rulerDown || u.Hours >= BrawlHours)
			{
				Disengage(u);
				Grudge(u.Ruler, u.Leader);
				Phase2.Curfew.Announce(rebelsDown
					? "{0} has put down the uprising in {1}; {2} and the rebels are beaten."
					: "The uprising in {1} has failed: {0} keeps the rule, and {2} backs down.", u.Ruler, s, u.Leader);
				// (The ruler down with every rebel down too also lands here: nobody is left to take it.)
				Unrest.Calm(s, won: false);
				return true;
			}
			Engage(u, lethal: false);
			return false;
		}

		private static bool AdvancePlot(NPCSettlement s, Uprising u)
		{
			Character leader = u.Leader;
			Character ruler = u.Ruler;
			if (ruler.isDead)
			{
				PlotTargets.Remove(ruler);
				KilledBy.TryGetValue(ruler, out Character killer);
				KilledBy.Remove(ruler);
				if (killer != leader || leader.isDead)
				{
					leader.jobQueue.CancelAllJobs(JOB_TYPE.ASSASSINATE);
					Phase2.Curfew.Note("{0} died before the plot against them in {1} came to anything.", ruler, s);
					Unrest.Calm(s, won: false);
					return true;
				}
				// A Murder on the leader's record means someone saw it (the game adds the crime
				// when a witness reacts).
				if (leader.crimeComponent.GetExistingActiveCrimeData(ruler, CRIME_TYPE.Murder) != null)
				{
					Phase2.Curfew.Announce("{0} of {1} has been murdered by {2}, who is now wanted.", ruler, s, leader);
					Unrest.Calm(s, won: true);
					return true;
				}
				TakeRule(s, leader, ruler, announce: false);
				Phase2.Curfew.Announce("{0} of {1} has been assassinated; {2} takes the rule.", ruler, s, leader);
				Unrest.Calm(s, won: true);
				return true;
			}
			// A plotter asleep or busy still plots; one killed, caught (Restrained) or beaten
			// (Unconscious) does not. Until the ruler sleeps the plotter waits (up to two days);
			// then they stay up (woken if asleep) and strike, and have a day to finish it.
			string why = leader.isDead ? "the plotter is dead" : leader.traitContainer.HasTrait("Restrained") ? "the plotter was caught"
				: leader.traitContainer.HasTrait("Unconscious") ? "the plotter was beaten" : null;
			if (why == null && u.StruckAt < 0)
			{
				if (u.Hours >= PlotWaitHours)
				{
					why = "the ruler was never found asleep";
				}
				else if (ruler.traitContainer.HasTrait("Resting") && ruler.hasMarker)
				{
					if (leader.traitContainer.HasTrait("Resting"))
					{
						leader.interruptComponent.TriggerInterrupt(INTERRUPT.Noise_Wake_Up, leader);
					}
					leader.jobComponent.CreateAssassinateTargetJob(ruler);
					u.StruckAt = u.Hours;
					RuinarchPlus.Log?.Info($"{leader.name} strikes at {ruler.name} in {s.name} while they sleep (plotter awake={!leader.traitContainer.HasTrait("Resting")}).");
				}
			}
			else if (why == null)
			{
				bool fighting = leader.combatComponent.hostilesInRange.Contains(ruler);
				bool plotting = leader.jobQueue.HasJob(JOB_TYPE.ASSASSINATE, ruler) || leader.currentJob?.jobType == JOB_TYPE.ASSASSINATE || fighting;
				why = !plotting ? "the plotter gave it up" : u.Hours - u.StruckAt >= LongHours ? "a day passed" : null;
			}
			if (why != null)
			{
				RuinarchPlus.Log?.Info($"Plot against {ruler.name} in {s.name} over: {why} (plotter wanted={s.owner != null && leader.crimeComponent.IsWantedBy(s.owner)}).");
				PlotTargets.Remove(ruler);
				leader.jobQueue.CancelAllJobs(JOB_TYPE.ASSASSINATE);
				Grudge(ruler, leader);
				Phase2.Curfew.Announce("The plot against {0} in {1} has failed.", ruler, s);
				Unrest.Calm(s, won: false);
				return true;
			}
			return false;
		}

		private static bool AdvanceWar(NPCSettlement s, Uprising u)
		{
			Faction faction = s.owner;
			bool loyalDown = u.Loyal.Concat(new[] { u.Ruler }).All(Down) || s.ruler != u.Ruler;
			bool rebelsDown = u.Rebels.All(Down);
			if (loyalDown && !rebelsDown)
			{
				Character taker = Taker(u);
				Disengage(u);
				TakeRule(s, taker, u.Ruler, announce: false);
				int n = Exile(u.Loyal.Concat(new[] { u.Ruler }), faction);
				Phase2.Curfew.Announce(n > 0
					? $"{{0}} has won the civil war in {{1}}; {n} who stood by {{2}} {(n == 1 ? "is" : "are")} exiled."
					: "{0} has won the civil war in {1}; nobody who stood by {2} is left to exile.", taker, s, u.Ruler);
				Unrest.Calm(s, won: true);
				return true;
			}
			if (rebelsDown && !loyalDown)
			{
				Disengage(u);
				int n = Exile(u.Rebels, faction);
				Phase2.Curfew.Announce(n > 0
					? $"{{0}} has won the civil war in {{1}}; {n} {(n == 1 ? "rebel is" : "rebels are")} exiled."
					: "{0} has won the civil war in {1}; no rebel is left to exile.", u.Ruler, s);
				Unrest.Calm(s, won: false);
				return true;
			}
			if ((rebelsDown && loyalDown) || u.Hours >= LongHours)
			{
				Disengage(u);
				Grudge(u.Ruler, u.Leader);
				Grudge(u.Leader, u.Ruler);
				Phase2.Curfew.Announce("The civil war in {0} ends with no winner.", s);
				Unrest.Calm(s, won: false);
				return true;
			}
			Engage(u, lethal: true);
			return false;
		}

		// The living among them still in the faction leave it (the game's own exile).
		private static int Exile(IEnumerable<Character> people, Faction faction)
		{
			int n = 0;
			foreach (Character c in people.Where(c => c != null && !c.isDead && faction != null && c.faction == faction).Distinct().ToList())
			{
				HeldRulers.Remove(c);
				c.traitContainer.RemoveTrait(c, "Restrained");
				faction.KickOutCharacterAndRollForGrudge(c, out _);
				n++;
			}
			return n;
		}

		/// <summary>
		/// <paramref name="leader"/> takes the rule of the village. A faction leader always
		/// rules their home village (<c>Faction.ProcessFactionLeaderAsSettlementRuler</c> puts
		/// them back), so a ruler who is the faction leader is overthrown as leader too, the way
		/// the game's own Overthrow Leader scheme does it (<c>Become_Faction_Leader</c>, a grudge).
		/// </summary>
		private static void TakeRule(NPCSettlement s, Character leader, Character ruler, bool announce)
		{
			Faction faction = s.owner;
			bool factionLeader = faction != null && faction.leader == ruler;
			if (factionLeader)
			{
				leader.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Faction_Leader, leader, "succession");
				if (s.ruler == ruler)
				{
					s.SetRuler(null);
				}
			}
			if (s.ruler != leader && (!leader.interruptComponent.TriggerInterrupt(INTERRUPT.Become_Settlement_Ruler, leader) || s.ruler != leader))
			{
				s.SetRuler(leader);
			}
			if (ruler.isDead)
			{
				if (announce)
				{
					Phase2.Curfew.Announce("{0} has taken the rule of {1} in an uprising.", leader, s);
				}
				return;
			}
			ruler.relationshipContainer.AdjustOpinion(ruler, leader, "Deposed", -30, "took the rule of the village", createJobsOnReduce: false);
			Grudge(ruler, leader);
			if (!announce)
			{
				return;
			}
			if (factionLeader)
			{
				Phase2.Curfew.Announce("{0} has overthrown {1} as leader of " + faction.name + " and taken the rule of {2} in an uprising.", leader, ruler, s);
			}
			else
			{
				Phase2.Curfew.Announce("{0} has taken the rule of {1} from {2} in an uprising.", leader, s, ruler);
			}
		}

		// ---- jailing -----------------------------------------------------------------------

		private static bool CanJail(NPCSettlement s, Character ruler)
		{
			return s.prison != null && !s.prison.hasBeenDestroyed && !ruler.isDead && ruler.hasMarker && ruler.faction == s.owner;
		}

		private static void Jail(NPCSettlement s, Character taker, Uprising u)
		{
			Character ruler = u.Ruler;
			ruler.traitContainer.AddTrait(ruler, "Restrained", taker);
			HeldRulers[ruler] = new Held { Village = s, Since = Now, Delivered = ruler.currentStructure == s.prison };
			if (!HeldRulers[ruler].Delivered)
			{
				Character carrier = new[] { taker }.Concat(u.Rebels).FirstOrDefault(c => c != ruler && !Down(c));
				GiveCarryJob(carrier, ruler, s.prison);
			}
			Phase2.Curfew.Announce("{0} has taken the rule of {1} and holds {2} in the prison.", taker, s, ruler);
		}

		private static void GiveCarryJob(Character carrier, Character prisoner, LocationStructure prison)
		{
			if (carrier == null || prison == null)
			{
				return;
			}
			GoapPlanJob job = JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.APPREHEND, INTERACTION_TYPE.DROP_RESTRAINED, prisoner, carrier);
			job.AddOtherData(INTERACTION_TYPE.DROP_RESTRAINED, new object[] { prison });
			carrier.jobQueue.AddJobInQueue(job);
		}

		internal static bool IsHeld(Character c) => c != null && HeldRulers.ContainsKey(c);

		/// <summary>Hourly: the held ex-rulers are delivered, judged, or let go.</summary>
		internal static void HourlyHeld()
		{
			long now = Now;
			foreach (KeyValuePair<Character, Held> kv in HeldRulers.ToList())
			{
				Character c = kv.Key;
				Held h = kv.Value;
				NPCSettlement s = h.Village;
				if (c == null || c.isDead || s == null || c.faction != s.owner)
				{
					HeldRulers.Remove(c);
					continue;
				}
				if (!c.traitContainer.HasTrait("Restrained"))
				{
					HeldRulers.Remove(c);
					Phase2.Curfew.Announce("{0}, once ruler of {1}, has escaped the prison.", c, s);
					continue;
				}
				if (!h.Delivered)
				{
					if (s.prison != null && c.currentStructure == s.prison && c.carryComponent.isBeingCarriedBy == null)
					{
						h.Delivered = true;
						h.Since = now;
					}
					else if (now - h.Since >= DeliverHours * TicksPerHour && c.carryComponent.isBeingCarriedBy == null)
					{
						// Nobody could carry them there: let go where they are.
						HeldRulers.Remove(c);
						c.traitContainer.RemoveTrait(c, "Restrained");
					}
					else if (c.carryComponent.isBeingCarriedBy == null && !c.HasJobTargetingThis(JOB_TYPE.APPREHEND))
					{
						Character carrier = Famine.Villagers(s).Where(v => v != c && !Down(v) && !v.relationshipContainer.IsFriendsWith(c))
							.OrderBy(v => v.relationshipContainer.GetTotalOpinion(c)).FirstOrDefault();
						GiveCarryJob(carrier, c, s.prison);
					}
					continue;
				}
				if (now - h.Since >= HeldHours * TicksPerHour)
				{
					Judge(c, s);
				}
			}
		}

		private static void Judge(Character c, NPCSettlement s)
		{
			HeldRulers.Remove(c);
			Character judge = s.ruler != null && !s.ruler.isDead && s.ruler != c ? s.ruler : s.owner?.leader as Character;
			if (judge == null || judge == c || judge.isDead)
			{
				c.traitContainer.RemoveTrait(c, "Restrained");
				Phase2.Curfew.Announce("{0}, once ruler of {1}, has been released.", c, s);
				return;
			}
			int opinion = judge.relationshipContainer.GetTotalOpinion(c);
			if (judge.relationshipContainer.HasGrudgeAgainst(c) || opinion <= -50)
			{
				c.Death("executed", null, judge, null, null, null, null, isPlayerSource: false, judge);
				Phase2.Curfew.Announce("{0} has had {1}, once ruler of {2}, executed.", judge, c, s);
			}
			else if (opinion < 0)
			{
				c.traitContainer.RemoveTrait(c, "Restrained");
				s.owner.KickOutCharacterAndRollForGrudge(c, out _);
				Phase2.Curfew.Announce("{0} has exiled {1}, once ruler of {2}.", judge, c, s);
			}
			else
			{
				c.traitContainer.RemoveTrait(c, "Restrained");
				Phase2.Curfew.Announce("{0} has released {1}, once ruler of {2}.", judge, c, s);
			}
		}

		internal static void OnDeath(Character victim, Character responsible)
		{
			if (victim != null && PlotTargets.Contains(victim))
			{
				KilledBy[victim] = responsible;
			}
		}

		// ---- persistence (inside the unrest file) ------------------------------------------
		// One "rulerId|villageId|sinceTick|delivered" per held ex-ruler.

		internal static List<string> SaveHeld()
		{
			return HeldRulers.Where(kv => kv.Key != null && kv.Value.Village != null)
				.Select(kv => string.Join("|", kv.Key.persistentID, kv.Value.Village.persistentID, kv.Value.Since.ToString(), kv.Value.Delivered ? "1" : "0")).ToList();
		}

		internal static void LoadHeld(List<string> entries)
		{
			HeldRulers.Clear();
			Forced.Clear();
			KilledBy.Clear();
			PlotTargets.Clear();
			foreach (string entry in entries ?? new List<string>())
			{
				string[] p = entry.Split('|');
				if (p.Length < 4)
				{
					continue;
				}
				Character c = CharacterManager.Instance.GetCharacterByPersistentID(p[0]);
				if (c == null || !(LandmarkManager.Instance.GetSettlementByPersistentID(p[1]) is NPCSettlement s))
				{
					continue;
				}
				long.TryParse(p[2], out long since);
				HeldRulers[c] = new Held { Village = s, Since = since, Delivered = p[3] == "1" };
			}
		}

		// ---- test harness ------------------------------------------------------------------

		/// <summary>Harness only: the next uprising in <paramref name="s"/> is <paramref name="kind"/>.</summary>
		internal static void ForceNext(NPCSettlement s, string kind)
		{
			if (Enum.TryParse(kind, out UprisingKind k))
			{
				Forced[s] = k;
			}
		}

		/// <summary>Harness only: the weights for an uprising led by <paramref name="leader"/>.</summary>
		internal static string WeightsText(NPCSettlement s, Character leader, Character ruler)
		{
			List<Character> pool = Famine.Villagers(s).Where(c => c != ruler && c.faction == s.owner && !Phase4.LifeCycle.IsChild(c)).ToList();
			Uprising u = new Uprising
			{
				Leader = leader,
				Ruler = ruler,
				Rebels = pool.Where(c => c.relationshipContainer.GetTotalOpinion(ruler) < 0).ToList(),
				Loyal = pool.Where(c => c.relationshipContainer.GetTotalOpinion(ruler) > 0).ToList(),
			};
			return string.Join(", ", Weights(s, u).Select(kv => $"{kv.Key}={kv.Value:0}"));
		}

		/// <summary>Harness only: "carrying" (on the way), "held" (in the prison), or null.</summary>
		internal static string HeldState(Character c) => c != null && HeldRulers.TryGetValue(c, out Held h) ? (h.Delivered ? "held" : "carrying") : null;

		/// <summary>Harness only: move a held ex-ruler's clock (the 48 hours start from it).</summary>
		internal static void HeldSince(Character c, long tick)
		{
			if (c != null && HeldRulers.TryGetValue(c, out Held h))
			{
				h.Since = tick;
			}
		}
	}

	// A held ex-ruler stays tied unless the one who would untie them is their friend. In the
	// base game villagers untie a Restrained member of their faction who is not wanted, on
	// sight (Restrained.CreateJobsOnEnterVisionBasedOnTrait) and in their reactions
	// (ReactionComponent's remove-status reaction). The vision job is not created at all;
	// the untie action itself (RemoveRestrained) refuses anyone else, whatever queued it.
	[HarmonyPatch(typeof(Restrained), nameof(Restrained.CreateJobsOnEnterVisionBasedOnTrait))]
	internal static class Uprisings_KeepHeldTied
	{
		private static bool Prefix(IPointOfInterest traitOwner, Character characterThatWillDoJob, ref bool __result)
		{
			try
			{
				if (traitOwner is Character held && Uprisings.IsHeld(held) && characterThatWillDoJob != null
					&& !characterThatWillDoJob.relationshipContainer.IsFriendsWith(held))
				{
					__result = false;
					return false;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Uprisings keep-tied failed: " + e.Message);
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(RemoveRestrained), "AreRequirementsSatisfied")]
	internal static class Uprisings_KeepHeldTied_Action
	{
		private static void Postfix(Character actor, IPointOfInterest poiTarget, ref bool __result)
		{
			try
			{
				if (__result && poiTarget is Character held && Uprisings.IsHeld(held) && actor != null && !actor.relationshipContainer.IsFriendsWith(held))
				{
					__result = false;
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Uprisings keep-tied (action) failed: " + e.Message);
			}
		}
	}

	// Who killed a ruler under a plot (Character.Death does not keep it).
	[HarmonyPatch(typeof(Character), nameof(Character.Death))]
	internal static class Uprisings_PlotDeath
	{
		private static void Postfix(Character __instance, Character responsibleCharacter)
		{
			try
			{
				if (__instance.isDead)
				{
					Uprisings.OnDeath(__instance, responsibleCharacter);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Uprisings plot death failed: " + e.Message);
			}
		}
	}
}
