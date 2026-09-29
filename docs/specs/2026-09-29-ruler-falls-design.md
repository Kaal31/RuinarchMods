# Other ways a ruler falls (Ruinarch+, Phase 5)

Status: design approved in chat (2026-09-29), not built. Plan:
`docs/plans/2026-09-29-ruler-falls.md`.

## Goal

Since 0.8.0 an uprising (`Phase5/Unrest.cs`) is always the same: the villager who thinks
least of the ruler leads everyone who dislikes them against the ruler and those who like
them, in the game's non-lethal brawl; the ruler knocked out, the leader takes the rule. The
roadmap (`RuinarchPlus-DESIGN.md`, Phase 5, "Next for unrest") asks for other outcomes,
depending on the village and the people involved. This adds three: an **assassination**
plot, the ruler **jailed** for treason, and a lethal **civil war** whose losers are exiled.
Which one happens is a weighted roll the people shape.

## Base game (cited against RuinarchRE)

- No rebellion, coup or faction split exists anywhere in the game (no `rebel`, `coup`,
  `overthrow`, `usurp`; `FactionManager.CreateNewFaction` is never called by an event).
- Assassination: `CharacterJobTriggerComponent.CreateAssassinateTargetJob(Character)`
  (`CharacterJobTriggerComponent.cs:5082`) queues a `JOB_TYPE.ASSASSINATE` plan for the
  target's `DEATH`; the job is lethal (`Extensions.cs:1025`). It ends in `Murder`
  (`Murder.cs:203 AfterMurderSuccess` kills the target); `Murder.GetCrimeType`
  (`Murder.cs:166`) makes it a Murder, and witnesses react (`Murder.cs:34 ReactionToActor`:
  report the crime, the faction processes a murder of its leader or ruler).
- Succession: the faction leader's death runs the faction's succession
  (`Faction.OnCharacterDied` -> `FactionSuccessionComponent`); `NPCSettlement.SetRuler`
  (`NPCSettlement.cs:683`) and `Faction.SetLeader` (`Faction.cs:648`) set them directly.
  A faction leader always rules their home village
  (`Faction.ProcessFactionLeaderAsSettlementRuler`), which `Unrest.TakeRule` already
  handles.
- Crimes: `CrimeComponent.AddCrime(CRIME_TYPE, CRIME_SEVERITY, ICrimeable, criminal, target,
  targetFaction, REACTION_STATUS)` (`CrimeComponent.cs:258`) and
  `CrimeData.AddFactionThatConsidersWanted(Faction)` (`CrimeData.cs:291`) make a character
  wanted. `CRIME_TYPE.Treason` exists (`Crime_System/Treason.cs`) and nothing in the game
  uses it.
- Arrest: `SettlementJobTriggerComponent.CreateApprehendJob(JOB_TYPE, Character)`
  (`SettlementJobTriggerComponent.cs:428`) posts a settlement `APPREHEND` job ending in
  `DROP_RESTRAINED` at `NPCSettlement.prison`; the game's own trigger
  (`TryCreateApprehend`, line 420) asks for the Criminal trait and being wanted by the
  village's faction. Being jailed does not take the rule from anyone.
- Judging: the faction leader holds the `JUDGE_PRISONER` job (`Faction.cs:378-386`);
  `JudgeCharacter` (`JudgeCharacter.cs:204-278`) absolves, executes, exiles, whips or burns
  at the stake.
- Exile: `Faction.KickOutCharacterAndRollForGrudge(Character, out bool)`
  (`Faction.cs:514`), the game's own exile (as `Exile.AfterExileSuccess` does): leaves the
  faction, rolls grudges by mood; the character goes to the vagrant faction.
- Combat: `CombatComponent.Fight(target, reason, connectedAction, isLethal, ...)`
  (`CombatComponent.cs:635`); `isLethal: false` knocks out, `true` kills.
- Traits used below all exist and are checked by the game: Evil, Psychopath, Ruthless,
  Treacherous, Diplomatic, Coward.

## Mod code this builds on

`Phase5/Unrest.cs`: the score, grievances, the restless state, `Rise` (picks the leader,
rebels and loyal camp; `CanTakePart`), `Engage`/`Disengage` (brawl), `Advance` (hourly),
`TakeRule` (the leader takes the rule, and the faction's leadership if the ruler held it),
`Down`, `CalmUntil`. `Phase2.Curfew.Announce`/`Note` for the event log. `Famine.Villagers`.

## Choosing the kind

`Rise` stays as it is up to the camps. Then it rolls the kind (weights, 0 when a need fails):

| Kind | Base | Needs | Shifted by |
|---|---|---|---|
| Brawl | 50 | - | leader Diplomatic or Coward x2 |
| Assassination | 15 | - | leader Evil, Psychopath, Ruthless or Treacherous x3; leader holds a grudge against the ruler x2 |
| Jailing | 20 | the village has a standing Prison (`NPCSettlement.prison`) | ruler wanted by their own faction x3 |
| Civil war | 15 | 12+ adult villagers, 4+ in each camp | 20+ villagers x2 |

Multipliers stack. The roll and weights go to `mods.log`:
`Uprising in Andorlad: brawl 50, assassination 90, jail 0, civil war 0 -> assassination`.
With `uprisingKindsEnabled` false the kind is always Brawl (0.9.0 behaviour).

## The kinds

Code: `Phase5/Uprisings.cs`, one small class per kind with `Start`, hourly `Advance` (true
when finished) and the shared end handling; `Unrest.cs` keeps the grievances, the score and
the trigger, and calls into it. The current brawl moves there unchanged.

**Brawl.** As in 0.9.0.

**Assassination.** Only the leader acts: `leader.jobComponent.CreateAssassinateTargetJob(ruler)`.
Announced: "{leader} is plotting against {ruler} in {village}." Each hour:
- the ruler dead and the leader not wanted for it by the faction: the leader takes the
  rule (`TakeRule`): "{ruler} of {village} has been assassinated; {leader} takes the rule.";
- the ruler dead and the leader wanted (witnessed): the leader takes nothing; the game's
  succession and ruler choice stand: "{ruler} of {village} has been murdered by {leader},
  who is now wanted.";
- after 24 hours with the ruler alive, or the leader dead, Restrained, Unconscious or no
  longer holding the job: the plot fails (the job is cancelled), the ruler holds a grudge
  against the leader: "The plot against {ruler} in {village} has failed."

**Jailing.** Starts as the brawl (same camps, non-lethal). When the ruler is down and the
rebels win (the brawl's own rule for who takes the rule):
1. the ruler is charged: `AddCrime(CRIME_TYPE.Treason, CRIME_SEVERITY.Serious, null, ruler,
   ruler, faction, REACTION_STATUS.Witnessed)` and `AddFactionThatConsidersWanted(faction)`;
2. the taker takes the rule (`TakeRule`);
3. the village posts the arrest: `settlementJobTriggerComponent.CreateApprehendJob(JOB_TYPE.APPREHEND, ruler)`.
Announced: "{leader} has taken the rule of {village}; {ruler} is charged with treason and
taken to the prison." From there the game's own flow decides the ruler's fate (the faction
leader judges). The loyal camp winning or the 12 hours running out: as the brawl. (Exact
`CRIME_SEVERITY`/`REACTION_STATUS` values and whether `AddCrime` accepts a null
`ICrimeable` are checked in the plan's first task; if it does not, the charge uses the
game's crime flow with the leader as reporter.)

**Civil war.** The same camps, `Fight(..., isLethal: true)`, re-engaged every hour.
Announced: "Civil war breaks out in {village}! ({n} rebels against {m} loyal to {ruler})".
Ends when one side is all `Down` (dead, unconscious, restrained, unable), or after 24 hours:
- the ruler's side down: the rebels' taker takes the rule; the old ruler (alive) and every
  surviving loyalist are exiled (`KickOutCharacterAndRollForGrudge`): "{leader} has won the
  civil war in {village}; {n} who stood by {ruler} are exiled.";
- the rebels down: the surviving rebels, leader included, are exiled; the ruler keeps the
  rule: "{ruler} has won the civil war in {village}; {n} rebels are exiled.";
- 24 hours: the ruler holds, nobody is exiled, the ruler and the leader hold grudges
  against each other: "The civil war in {village} ends with no winner."
Deaths feed unrest through the existing `Unrest_Death` hook.

**Shared.** Every kind ends with `CalmUntil = now + 24h`; a won one also zeroes the score and
the restless state, as the brawl does. A taker who is down falls back to the rebel still
standing who thinks least of the ruler (the brawl's rule).

## What the player sees

Event log lines above (people and villages as links, through `Curfew.Announce`). Crimes,
prisoners, judging, exiled vagrants and grudges show in the game's own panels.

## Config

`uprisingKindsEnabled` (true). Weights and thresholds are fixed in code. README config row
and the Unrest feature row updated; design doc Phase 5 "Next for unrest" becomes shipped.

## Saving

Unchanged: an uprising in progress is not saved (a loaded village rises again at the next
check). A jailed ruler, the Treason crime, exiles and grudges are the game's own saved state.

## Order of work

1. First, a probe: check `AddCrime` with Treason on a live ruler (null `ICrimeable`,
   severity, Criminal trait, wanted) in the harness before building on it.
2. `Uprisings.cs`: move the brawl, add the roll, then assassination, jailing, civil war.
3. Config, README, design doc.
4. Harness `UprisingKindsSuite`; bridge hooks.
5. In game: the suite twice, two full regressions; commit after each green step.
6. Release Ruinarch+ 0.10.0.

## Testing (`UprisingKindsSuite` in `RuinarchDebug/AutoTest.cs`)

Hooks: `Uprisings.ForceNext(village, kind)` (harness only: the next uprising in that village
is that kind) and `Uprisings.Weights(village, leader, ruler)`.
1. Weights: in a live village, a Psychopath leader raises assassination; no Prison gives
   jailing 0; fewer than 12 adults gives civil war 0; `uprisingKindsEnabled` false gives
   brawl only.
2. Assassination (forced): within a day the ruler is dead, and either the leader rules and
   is not wanted, or the leader is wanted for Murder and someone else rules. A failed plot
   is a skip with its reason.
3. Jailing (forced; the harness builds a Prison if the village has none): the ruler is
   knocked out, wanted for Treason by their faction, the leader rules, and the ruler ends
   up Restrained inside the Prison.
4. Civil war (forced in the largest village with 4+ per camp; skipped if none): lethal
   fighting; the winner's side holds the rule; every surviving member of the losing side
   has left the faction.
5. UnrestSuite unchanged and still passing.
By hand: a jailed ruler survives a save and load (the harness cannot reload the game's own
state).

## Out of scope

- The losers founding their own faction or moving to another village (exile only).
- Treason charges against a failed uprising's leader.
- Player schemes that start or steer an uprising.
- Configurable weights.
