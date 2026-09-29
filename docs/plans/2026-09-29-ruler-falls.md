# Other ways a ruler falls: Implementation Plan

**For agentic workers:** execute task by task; steps use checkbox (`- [ ]`) syntax.

**Goal:** an uprising rolls its kind (brawl, assassination, jailing, civil war), each
played out with the game's own jobs, fights and exile.

**Architecture:** `Phase5/Unrest.cs` keeps grievances, the score, the restless state and the
trigger (`Rise` picks leader and camps). A new `Phase5/Uprisings.cs` owns the uprising
record, the roll, each kind's start and hourly advance, taking the rule, and the held
(jailed) ex-rulers with their save list and the "leave them tied" patch.

**Spec:** `docs/specs/2026-09-29-ruler-falls-design.md`.

## Global constraints

- Stock game, Harmony only; nothing in RuinarchRE changes.
- Config: `uprisingKindsEnabled` (true); false = every uprising is a brawl.
- Weights: brawl 50, assassination 15, jailing 20 (needs a standing prison), civil war 15
  (needs 12+ adult villagers and 4+ per camp); multipliers stack: leader Diplomatic or
  Coward -> brawl x2; leader Evil/Psychopath/Ruthless/Treacherous -> assassination x3;
  leader's grudge against the ruler -> assassination x2; ruler wanted by own faction ->
  jailing x3; 20+ villagers -> civil war x2.
- Timings: brawl and jailing 12 h, assassination and civil war 24 h, held 48 h before
  judgement, delivery to the prison within 12 h; a day's calm after every kind.
- Event log text exactly as in the spec; the roll goes to `mods.log` only.
- Save: unrest file gains `held` (`rulerId|villageId|sinceTick|delivered`); old files load.
- Writing: no em or en dashes in docs, logs or messages.

## Files

- Create `RuinarchPlus/Phase5/Uprisings.cs`: `UprisingKind`, `Uprising`, `Uprisings`
  (roll, kinds, `TakeRule`, held ex-rulers), `Uprisings_KeepHeldTied` patch.
- Modify `RuinarchPlus/Phase5/Unrest.cs`: `Rise` builds the record and calls
  `Uprisings.Start`; `Update` calls `Uprisings.Advance`; brawl code, `Down`, `TakeRule`,
  `Engage`/`Disengage` move out; `Calm(s, won)` for the kinds; save/load carry `held`.
- Modify `RuinarchPlus/Config.cs`, `README.md`, `RuinarchPlus-DESIGN.md`.
- Modify `RuinarchDebug/PlusBridge.cs`, `RuinarchDebug/AutoTest.cs` (`UprisingKindsSuite`).

## Interfaces (Uprisings.cs)

```csharp
internal enum UprisingKind { Brawl, Assassination, Jailing, CivilWar }
internal sealed class Uprising { Character Leader, Ruler; List<Character> Rebels, Loyal; int Hours; UprisingKind Kind; }
internal static class Uprisings
{
    internal static bool Enabled;                                  // config
    internal static Dictionary<UprisingKind, float> Weights(NPCSettlement s, Uprising u);
    internal static void Start(NPCSettlement s, Uprising u);       // rolls Kind (or forced), announces, engages
    internal static bool Advance(NPCSettlement s, Uprising u);     // hourly; true when over (calm set)
    internal static void HourlyHeld();                             // judgement, escapes, delivery
    internal static bool IsHeld(Character c);
    internal static List<string> SaveHeld(); internal static void LoadHeld(List<string> entries);
    // harness only
    internal static void ForceNext(NPCSettlement s, string kind);
    internal static string WeightsText(NPCSettlement s, Character leader, Character ruler);
    internal static void HeldSince(Character ruler, long tick);
    internal static string HeldState(Character ruler);            // null when not held, else "held"/"carrying"
}
// Unrest.cs
internal static void Calm(NPCSettlement s, bool won);             // CalmUntil = now+24h; won: points 0, not restless
internal static long Now;                                         // existing (MissingPersons.Now)
```

### Task 1: move the brawl, add the roll

- [ ] Create `Uprisings.cs` with `Uprising`, `UprisingKind`, `Down`, `CanFight`, `Engage`
  (isLethal parameter), `Disengage`, `TakeRule` moved verbatim from `Unrest.cs`.
- [ ] `Unrest.Rise` builds the `Uprising` (as now) and calls `Uprisings.Start`; the
  announcement moves into the kinds. `Unrest.Update` calls `Uprisings.Advance`, removing
  the record when it returns true. `Unrest.Calm(s, won)`.
- [ ] `Weights`/roll/`ForceNext`, the `mods.log` line. Brawl `Advance` = the current one.
- [ ] Build; `UnrestSuite` still passes in game (after Task 3's build, run together).

### Task 2: assassination, jailing, civil war

- [ ] Assassination: `leader.jobComponent.CreateAssassinateTargetJob(ruler)`; hourly
  outcomes per spec; wanted = `leader.crimeComponent.IsWantedBy(faction)`; failure cancels
  the job (`leader.jobQueue` ASSASSINATE jobs targeting the ruler) and adds the grudge.
- [ ] Jailing: brawl until the ruler is down; the rebels win -> `TakeRule`, Restrained
  (responsible: taker), personal job
  `JobManager.Instance.CreateNewGoapPlanJob(JOB_TYPE.APPREHEND, INTERACTION_TYPE.DROP_RESTRAINED, ruler, carrier)`
  with `AddOtherData(INTERACTION_TYPE.DROP_RESTRAINED, new object[] { prison })`, added to
  the carrier's queue; the hold recorded. `HourlyHeld`: delivery (in `prison`), 12 h
  undelivered release, escape, death/left faction, 48 h judgement (execute, exile,
  release) by the ruler now / faction leader. `Uprisings_KeepHeldTied`: prefix on
  `Restrained.CreateJobsOnEnterVisionBasedOnTrait` returns false (with `__result` false) for
  a held character unless the viewer `IsFriendsWith` them.
- [ ] Civil war: lethal `Engage` every hour; outcomes per spec; exile with
  `faction.KickOutCharacterAndRollForGrudge(c, out _)` for living, non-down-by-death members
  still in the faction (Restrained removed first if held).
- [ ] Save/load `held` in `UnrestSaveData`.
- [ ] Build; `check-patches` clean.

### Task 3: config and docs

- [ ] `Config.cs` `uprisingKindsEnabled` with comment; README feature row and config row;
  design doc Phase 5 "Next for unrest" -> shipped paragraph.

### Task 4: harness

- [ ] Bridge: `ForceUprising(village, kind)`, `UprisingWeights(village, leader, ruler)`,
  `HeldState(c)`, `SetHeldSince(c, tick)`, `UprisingKind(village)` (current kind or null).
- [ ] `UprisingKindsSuite` checks 1-4 of the spec's testing list; save round trip of the hold
  with `SaveAndRead`/`ReplayLoad`; dispatch by name and in the full run after UnrestSuite.
- [ ] Both mods build.

### Task 5 (in game)

- [ ] `run-autotest.sh 1800 UnrestSuite,UprisingKindsSuite` twice; then two full regressions.
- [ ] Commit and push after each green step.

### Task 6: release

- [ ] Ruinarch+ 0.10.0 (`mod.json`, zip, release notes; needs loader v0.5.0).
