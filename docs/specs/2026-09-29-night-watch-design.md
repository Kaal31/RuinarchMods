# Night watch (Ruinarch+, Phase 6, first slice)

Status: designed 2026-09-29 without a review round (the user asked for work to continue
unattended), not built. Plan: `docs/plans/2026-09-29-night-watch.md`.

## Goal

The roadmap's Phase 6 opens with "training grounds produce standing armies" and "patrols:
locals patrol and kill threats". The first turned out to exist already; the second has
a hole: villages are watched by day and not at night. This adds a night watch to Towns
and Cities: a few of their fighters keep watch at night, walking the village aggressively,
so a monster or a demon that slips in at night meets someone awake.

## Base game (cited against RuinarchRE)

- Standing fighters already exist: `SettlementClassComponent.GetNumberOfNeededCombatants`
  (`SettlementClassComponent.cs:500`) wants N - ceil(3N/8) combatants among N villagers
  (about 62 percent), and posts "Combatant" change-class jobs when short (line 588). Barracks
  are a village facility (`HumanVillage.cs:30`, weight 20, cap 1; capitals 2) and
  combatants train there by chance (`CharacterClassBehaviour.cs:65-79`, Martial Arts
  talent). So a "training grounds" building would add nothing.
- Patrols by day: `SettlementPartyComponent.TryCreateMorningPatrolQuest` (line 167) posts
  one `MorningPatrolPartyQuest` per faction; a village party takes it at its daily quest
  check (5:00 to 7:00, `Party.InitialScheduleToCheckQuest`).
- Patrols by night exist but are never used: `NightPatrolPartyQuest` and
  `NightPatrolBehaviour` (aggressive combat mode while added, a `PATROL` job to a random
  tile of a random village structure) are only created by the debug console
  (`ConsoleBase.cs:1557`). As a party quest it would compete with the morning patrol for the
  same parties (a party takes the first quest of the highest priority,
  `PartyQuestBoard.GetFirstPriorityUnassignedPartyQuestFor`), so this feature does not
  use the quest.
- Schedules: `DailyScheduleComponent.UpdateDailySchedule` (private) picks the schedule:
  party members, Nocturnal (`NocturnalSchedule`: work 22:00 to 9:00, sleep 12:00 to 19:00),
  everyone else `NonPartyMemberSchedule`. `SetSchedule` is private.
- Combatants defend a village under siege (`BehaviourComponent.cs:1200-1209`), but only
  once it is under siege.

## The watch

A settlement keeps a watch while it is a Town or City (`SettlementTiers`, capitals
included) and has at least 4 combatants. Watch size: one guard per 8 residents, at least
1, at most 3.

Guards: residents of the settlement who are combatants (`characterClass.IsCombatant()`),
alive, adult, not in an active party, not the ruler or faction leader, not Restrained;
picked by highest Martial Arts talent, then at random. Checked every hour: a guard who no
longer qualifies (dead, left, joined a party, became the ruler, changed class) is replaced;
extra guards are released when the settlement shrinks or drops to a village.

On duty:
- Schedule: a postfix on `DailyScheduleComponent.UpdateDailySchedule` gives a guard not in a
  party the game's `NocturnalSchedule`; naming or releasing a guard calls it (by reflection)
  so the schedule changes at once.
- Night: hourly, a guard in their Work hours (22:00 to 9:00) who is at home in the
  settlement gets the game's `NightPatrolBehaviour`
  (`behaviourComponent.AddBehaviourComponent`); outside those hours, or released, it is
  removed (the behaviour restores their combat mode).
- The game's combat does the rest: an aggressive patroller attacks hostiles in sight.

Announcements (event log): "{settlement} has set a night watch: {guards}." when a watch is
first set; nothing for replacements (`mods.log` only).

## Config

`nightWatchEnabled` (true). Watch size and hours are fixed.

## Saving

Guards are saved (`ModData/ruinarch.plus.watch.json`, one `settlementId|guardIds` each);
on load the schedules are recomputed. A save without the file: no guards, named at the next
hourly check.

## Testing (`WatchSuite` in `RuinarchDebug/AutoTest.cs`)

Hooks: `NightWatch.GuardsOf(settlement)`, `NightWatch.Check()` (run the hourly check now).
1. A Town or City (made one with the tier config if needed) gets guards: all combatants,
   none the ruler, and the event log says so.
2. A guard has the Nocturnal schedule; a non-guard does not.
3. At night (waiting for 23:00) a guard in the village has a `PATROL` job or action, or the
   night patrol behaviour, and is in aggressive combat mode.
4. A hostile monster (a Wolf) placed in the village at night is fought by a guard within
   two hours.
5. Released (feature switched off): the schedule goes back and the behaviour is removed.
6. Guards are stored in the save file and come back when it loads.

## Out of scope

- Levies and war marches (later Phase 6 slices).
- Watch towers or any new building.
