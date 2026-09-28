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

**Files:** `src/Ruinarch.ModContent/ActionRegistration.cs` (new), `ModContent.cs`,
`ContentRegistry.cs`, `ContentPatches.cs`.

- [ ] `ActionRegistration { Id, Name, Factory (Func<GoapAction>), States (List<ActionState>),
  Type (filled in) }`, `ActionState(name, durationTicks, success)`.
- [ ] `ContentRegistry`: `ActionsByType`, `Actions`; `AllocateValue` takes any
  `ICollection<int>`-like set of taken values (structures and actions keep separate spaces).
- [ ] `ModContent.RegisterAction(reg)`: validate, allocate, add the name to
  `StringEnumLookUp._interactionTypeStrings` and the states to
  `GoapActionStateDB.goapActionStates` (both static dictionaries, set by reflection) at
  registration, before any factory runs. `ActionTypeFor(id)`.
- [ ] Postfix on `InteractionManager.ConstructGoapActionData`: add `Factory()` for each
  registration to `goapActionData`.
- [ ] Grep RuinarchRE for every other lookup keyed by `INTERACTION_TYPE` that a virtual
  value can reach from a job on a tile object (object pools, icon lookups, save data,
  `ToStringEnum` callers with switch defaults); handle each the same way or document why it
  is unreachable.
- [ ] Build the framework; zero errors.

### Task 2: framework docs

- [ ] `docs/ASSETS_AND_CONTENT.md`: "Adding a new action" for strangers (what an action is
  in the game, the registration, a minimal action class, making a job for it, logs, saves).
- [ ] README feature line; no em-dashes.

### Task 3: the two actions (Ruinarch+)

**Files:** `RuinarchPlus/Phase4/RecordActions.cs` (new), `RuinarchPlus.cs` (registration
before `PatchAll`-dependent code runs).

- [ ] `WriteRecord : GoapAction` and `ReadRecord : GoapAction`: `NEAR_TARGET`,
  `GoapActionStateDB.Read_Icon`, one state ("Write Success" / "Read Success", 20 ticks),
  `AreRequirementsSatisfied` (target built, a carrier of a holder, actor counted by
  `Knowledge`), `Perform` sets the state, `AfterWriteSuccess` / `AfterReadSuccess` call
  `Records.Write` / `Records.ReadFrom` and log.
- [ ] Suppress the game's own description log if it produces empty or broken text for an
  unknown action name (check `ActualGoapNode.CreateDescriptionLog`).

### Task 4: records on shelves, free-time trigger

**Files:** `RuinarchPlus/Phase4/Records.cs`, `Phase3/Knowledge.cs` if needed.

- [ ] Carriers: a record's objects are the holder's `SHELF_BOOKS` when it has any, else
  placed Books (`PlaceBook`, keeps two tiles free). Home: taken at first Write. Library:
  taken or placed when first seen built (`Furnish`).
- [ ] Record lost when every carrier is gone (`Prune`), including shelves.
- [ ] `Records.JobFor(c)`: the free-time decision (home Write, home Read with `readChance`,
  Library Write/Read with `libraryVisitChance`, curfew blocks the Library), once per
  villager per game hour. Job built like `Character.TryCreateJobForSpecificAction`.
- [ ] Replace `Records_LibraryVisit` (the `RunBehaviour` prefix) with the new trigger.
- [ ] Remove instant writing and reading from `HourlyCheck`.
- [ ] Log helper: one log with actor and carrier as fillers, fixed text, added to the
  database (as `Curfew.Post` does, plus the carrier filler).
- [ ] Save format unchanged; carriers may be shelves.
- [ ] Build; zero errors.

### Task 5: config, README, design doc

- [ ] `Config.cs` comments for `readChance`, `libraryVisitChance`, `libraryBooks`.
- [ ] RuinarchPlus README records row and config rows; design doc Phase 4 item.
- [ ] `mod.json`: requires RuinarchModLoader v0.5.0 (release notes too).

### Task 6: harness

**Files:** `RuinarchDebug/PlusBridge.cs`, `RuinarchDebug/AutoTest.cs`.

- [ ] Bridge: carriers of a holder, queue Write/Read job, action types registered.
- [ ] `ShelfProbe` (run by name): per culture, which structure types in live villages have
  `SHELF_BOOKS`; both actions present in `goapActionData`, `goapActionStates`, name table.
- [ ] `RecordsSuite` rewritten to the spec's ten checks.
- [ ] Build both mods; zero errors.

### Task 7 (on the user's go): in game

- [ ] `run-autotest.sh 1200 ShelfProbe`; update the spec with the shelf findings.
- [ ] `run-autotest.sh 1500 RecordsSuite` twice, then two full regressions.
- [ ] Commit and push both repos after each green step.

### Task 8: releases

- [ ] RuinarchModLoader v0.5.0 (package-release includes `Ruinarch.ModContent.dll`).
- [ ] Ruinarch+ 0.9.0 with records, notes say it needs loader v0.5.0.
