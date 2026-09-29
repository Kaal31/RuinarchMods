# Write and Read actions on Book Shelves: Implementation Plan

**For agentic workers:** execute task by task; steps use checkbox (`- [ ]`) syntax.

**Goal:** records are kept on a building's Book Shelves (else placed Books), and villagers
write and read them through two real actions in their free time, logged on the shelf.
Spec: `docs/specs/2026-09-27-records-library-design.md` (amended 2026-09-28).

**Repos:** RuinarchModLoader (framework, v0.5.0), RuinarchMods (Ruinarch+, RuinarchDebug).
Decompiled reference: RuinarchRE (read only). Build: `RuinarchModLoader/tools/build.sh`
(framework), `tools/build-mod.sh <mod dir> <out dir>` (mods). In-game runs:
`tools/run-autotest.sh <seconds> [suites]`, only with the user's go.

---

### Task 1: framework `RegisterAction`

**Files:** `src/Ruinarch.ModContent/ActionRegistration.cs` (new), `ActionPatches.cs` (new),
`ModContent.cs`, `ContentRegistry.cs`.

- [x] `ActionRegistration { Id, Name, Factory, States, Type }`,
  `ActionState(name, durationTicks, success, describe)`.
- [x] `ContentRegistry.ActionsByType`; `AllocateValue<T>` (structures and actions keep
  separate spaces).
- [x] `RegisterAction`: allocates the type. States go into `GoapActionStateDB.goapActionStates`
  in `Patch_ActionData` (its static constructor needs `GameManager`; touching it from
  `OnLoad` broke the class for the session, found by the first ShelfProbe run).
  Names: the game rebuilds `_interactionTypeStrings` in `StringEnumLookUp.Initialize` at the
  main menu (after mods load), so a postfix adds them there (`Patch_ActionNames`).
  `ActionTypeFor(id)`.
- [x] `Patch_ActionData`: postfix on `ConstructGoapActionData` adds each action to
  `goapActionData`, `goapActionList`, `actionsCategorizedByEffectCondition`.
- [x] Lookups keyed by `INTERACTION_TYPE`: all dictionaries (states, instances, names,
  action trackers, job other-data); invalidity logs check for a missing text key; the
  description log is patched (`Patch_ActionDescription`, from `Describe`, table
  `"ModContent"`), and `Log.ResetText` keeps fixed text (`Patch_FixedTextLog`).
- [x] Thought bubbles: `CreateThoughtBubbleLog` checks for a missing key, but its one
  reader (`GetCurrentLog`, used by `CharacterVisuals.GetThoughtBubble`: map nameplate,
  panel, tooltip) does not, and threw inside `Faction.LeaveFaction` in RecordsSuite run 2.
  `Patch_ActionThoughtBubble` gives every registered action both bubbles (`Going`/`Doing`
  on the registration, defaults from the name).
- [x] Framework builds; `check-patches.sh`: 19 patch classes, 0 failures.

### Task 2: framework docs

- [x] `docs/ASSETS_AND_CONTENT.md` "Adding a new action"; `CONTENT_FRAMEWORK.md` patch
  table and API; README lines. No em-dashes.

### Task 3: the two actions (Ruinarch+)

- [x] `Phase4/RecordActions.cs`: `WriteRecord`, `ReadRecord` (`NEAR_TARGET`, Read icon, one
  20-tick state, requirement: the target is a carrier), `AfterWriteSuccess` /
  `AfterReadSuccess` call `Records.Wrote` / `Records.ReadAt`; `Describe` gives the log
  text. Registered from `Records.Register` (in `OnLoad`).

### Task 4: records on shelves, free-time trigger

- [x] Carriers (`SHELF_BOOKS`, else placed Books), `Ensure`/`Bare`/`CarrierFor`, `Furnish`.
- [x] Record lost when every carrier is gone (`Bare`, from `Prune` and before a new write).
- [x] `Records.JobFor` + `Records_FreeTime` (priority High, ahead of the curfew's prefix);
  job via `PlanIdle(JOB_TYPE.IDLE, action, carrier)`.
- [x] Instant writing and reading removed from `HourlyCheck`.
- [x] Save format unchanged (`kind|holder|carriers|structures`).
- [x] Builds; 95 patch classes, 0 failures.

### Task 5: config, README, design doc

- [x] `Config.cs` comment; README records and config rows; design doc Phase 4 item.
- [ ] At release: the notes say Ruinarch+ 0.9.0 needs RuinarchModLoader v0.5.0 (`mod.json`
  has no requirement field).

### Task 6: harness

- [x] Bridge: `CarriersOf`, `CarrierFor`, `RecordAction`, `PlanRecordAction`.
- [x] `ShelfProbe` (run by name).
- [x] `RecordsSuite` rewritten (spec checks 1-7, 9, 10); check 8 in `CurfewSuite`
  (`CurfewRecordsTest`); `LogsOf` reads a carrier's Logs tab from the game's log database.
- [x] Both mods build.

### Task 7 (on the user's go): in game

- [x] `run-autotest.sh 1200 ShelfProbe` (after the `GoapActionStateDB` fix): both actions
  registered with their states; spec updated with the shelf findings.
- [x] `run-autotest.sh 1500 RecordsSuite` twice, then two full regressions. Runs 1-2 (before
  the thought bubble fix): writing, reading, free-time writing, carriers, Library reading
  and saves passed. Harness-given jobs were IDLE (priority 250) and waited behind work;
  the bridge now gives them as VISIT_STRUCTURE (1000), after clearing whatever job holds
  the villager. Final: ExplosionTest + RecordsSuite 21/0/0; full run 39 130/1/14 (a
  missing-persons victim's body left the map before any search: harness now skips that),
  full run 40 141/0/9; no game exceptions, the Portal at full HP.
- [x] Commit and push both repos after each green step.

### Task 8: releases

- [ ] RuinarchModLoader v0.5.0 (package-release includes `Ruinarch.ModContent.dll`).
- [ ] Ruinarch+ 0.9.0 with records, notes say it needs loader v0.5.0.
