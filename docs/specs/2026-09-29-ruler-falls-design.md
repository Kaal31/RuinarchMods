# Other ways a ruler falls (Ruinarch+, Phase 5)

Status: design approved in chat (2026-09-29), not built; jailing amended the same day
(approved in chat) after the crime system proved unable to charge a ruler. Plan:
`docs/plans/2026-09-29-ruler-falls.md`.

## Goal

Since 0.8.0 an uprising (`Phase5/Unrest.cs`) is always the same: the villager who thinks
least of the ruler leads everyone who dislikes them against the ruler and those who like
them, in the game's non-lethal brawl; the ruler knocked out, the leader takes the rule. The
roadmap (`RuinarchPlus-DESIGN.md`, Phase 5, "Next for unrest") asks for other outcomes,
depending on the village and the people involved. This adds three: an **assassination**
plot, the ruler **jailed** by the new ruler, and a lethal **civil war** whose losers are exiled.
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
- Crimes cannot charge the old ruler cleanly, so jailing does not use them:
  `CrimeManager.ReactToCrime` does nothing when the actor is their faction's ruler or leader
  ("actor_leader_do_nothing"); every `CrimeData` keeps the `ICrimeable` (an action or
  interrupt) behind it and dereferences it unchecked (`CrimeData.cs:481, 510, 523`), and a
  crime without one is not saved (`SaveDataCrimeComponent.cs:30`); `CRIME_TYPE.Treason`
  exists (`Crime_System/Treason.cs`) but no action produces it. The game's own framing
  (`FabricateCrimeData`) charges a random serious crime spread by gossip.
- Judging needs a crime: `SettlementJobTriggerComponent.TryCreateJudgePrisoner`
  (line 405) only judges a Restrained Criminal in the prison who is wanted.
- Carrying to prison: `INTERACTION_TYPE.DROP_RESTRAINED` (`DropRestrained.cs:152
  AfterDropSuccess`) carries a Restrained character and puts them down at the structure in
  its other data (the settlement's `APPREHEND` job passes `NPCSettlement.prison`); it needs
  no crime. Friends of a Restrained prisoner who is not their own faction's may free them
  (`ReactionComponent.cs:1380`, `RELEASE_CHARACTER`). Being held takes the rule from nobody.
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
1. the taker takes the rule (`TakeRule`);
2. the old ruler is Restrained (`traitContainer.AddTrait(ruler, "Restrained", taker)`) and
   a standing rebel (the taker first) gets a personal `JOB_TYPE.APPREHEND` job with
   `DROP_RESTRAINED` to the village's prison (other data: the prison), the game's own carry
   and drop.
Announced: "{taker} has taken the rule of {village} and holds {ruler} in the prison."
The mod keeps who it holds (the old ruler, the village, since when). Each hour:
- held (Restrained, inside the prison) for 48 hours: the village's ruler now (else the
  faction leader; else nobody, and they are released) decides by what they think of them:
  a grudge, or opinion -50 or lower: executed (`Death("executed")` with the ruler as
  responsible): "{judge} has had {ruler}, once ruler of {village}, executed."; below 0:
  exiled (Restrained removed, `KickOutCharacterAndRollForGrudge`): "{judge} has exiled
  {ruler}, once ruler of {village}."; otherwise released (Restrained removed, stays a
  villager): "{judge} has released {ruler}, once ruler of {village}.";
- no longer Restrained before that (freed by a friend, or broke loose): "{ruler}, once
  ruler of {village}, has escaped the prison." and the mod lets go;
- not in the prison 12 hours after the uprising (nobody could carry them): released where
  they are, no announcement beyond the overthrow;
- dead, or left the faction: the mod lets go.
In the base game any villager who is not hostile and sees a Restrained non-Criminal queues a
job to untie them (`Restrained.CreateJobsOnEnterVisionBasedOnTrait`, `Restrained.cs:74-104`),
so a held ruler would be untied at once. A prefix on it returns false for a held ruler
unless the villager seeing them is their friend (`relationshipContainer.IsFriendsWith`):
the village leaves them tied, a friend breaks them out.
The loyal camp winning or the 12 hours running out: as the brawl. The jailing weight needs
a standing prison (`NPCSettlement.prison`) at the roll.

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

Event log lines above (people and villages as links, through `Curfew.Announce`). Prisoners,
exiled vagrants and grudges show in the game's own panels.

## Config

`uprisingKindsEnabled` (true). Weights and thresholds are fixed in code. README config row
and the Unrest feature row updated; design doc Phase 5 "Next for unrest" becomes shipped.

## Saving

An uprising in progress is not saved (a loaded village rises again at the next check).
Who the mod holds is saved: a new `held` list in the unrest file
(`ModData/ruinarch.plus.unrest.json`), one `rulerId|villageId|sinceTick|delivered` each; a
0.9.0 file without it loads as empty. The Restrained trait, exiles and grudges are the
game's own saved state.

## Order of work

1. `Uprisings.cs`: move the brawl (UnrestSuite still passes), add the roll.
2. Assassination, jailing (with its hold, judgement and save), civil war.
3. Config, README, design doc.
4. Harness `UprisingKindsSuite`; bridge hooks.
5. In game: the suite twice, two full regressions; commit after each green step.
6. Release Ruinarch+ 0.10.0.

## Testing (`UprisingKindsSuite` in `RuinarchDebug/AutoTest.cs`)

Hooks: `Uprisings.ForceNext(village, kind)` (harness only: the next uprising in that village
is that kind), `Uprisings.Weights(village, leader, ruler)`, `Uprisings.HeldSince(ruler, tick)`
(harness only: shortens the 48 hours).
1. Weights: in a live village, a Psychopath leader raises assassination; no Prison gives
   jailing 0; fewer than 12 adults gives civil war 0; `uprisingKindsEnabled` false gives
   brawl only.
2. Assassination (forced): within a day the ruler is dead, and either the leader rules and
   is not wanted, or the leader is wanted for Murder and someone else rules. A failed plot
   is a skip with its reason.
3. Jailing (forced; the harness builds a Prison if the village has none): the ruler is
   knocked out, the leader rules, and the ruler ends up Restrained inside the Prison; the
   hold is stored in the save file and comes back from it; with the hold shortened, the
   judge's decision matches their opinion (executed, exiled or released).
4. Civil war (forced in the largest village with 4+ per camp; skipped if none): lethal
   fighting; the winner's side holds the rule; every surviving member of the losing side
   has left the faction.
5. UnrestSuite unchanged and still passing.
By hand: a jailed ruler's Restrained trait survives a real save and load (the game's own
state; the harness can only replay mod data).

## Out of scope

- The losers founding their own faction or moving to another village (exile only).
- Charging anyone with a crime (Treason or other) as part of an uprising.
- Player schemes that start or steer an uprising.
- Configurable weights.
