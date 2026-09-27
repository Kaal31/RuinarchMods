# Records (home Books and the Library) Implementation Plan

> **For agentic workers:** execute task by task; steps use checkbox (`- [ ]`) syntax.

**Goal:** Knowledge of the player's buildings survives in Books (in dwellings and in a
Library that Towns and Cities build); villagers read it back into memory.

**Architecture:** one new feature file `RuinarchPlus/Phase4/Records.cs` (records, hourly
write/read, Library queueing, visits, save) and one building class
`RuinarchPlus/Phase4/Library.cs` (like `Phase5/TownHall.cs`). `Knowledge` gains a `Read`
entry point and exposes its villager/standing predicates. The Records hourly check is
called from `Knowledge_HourTick`, right after `Knowledge.HourlyCheck`.

**Tech stack:** C# against the stock game (RuinarchRE reference), Harmony,
Ruinarch.ModContent (`RegisterStructure`, `ModSave`). Build: `RuinarchModLoader/tools/build-mod.sh`.

**Spec:** `docs/specs/2026-09-27-records-library-design.md`

## Global constraints

- Never edit RuinarchRE; stock `Assembly-CSharp` only; all behaviour via Harmony.
- Config keys and defaults: `recordsEnabled` true, `readChance` 25, `libraryVisitChance` 10,
  `libraryBooks` 4. Records need `knowledgeEnabled`; Libraries need `settlementTiersEnabled`.
- Save file: `ModData/ruinarch.plus.records.json`, strings only (JsonUtility drops lists of
  mod classes), always written.
- Library id `ruinarch.plus.library`, display name "Library", borrows `STRUCTURE_TYPE.WORKSHOP`
  (the only candidate with a prefab for every culture: `Wood Workshop 1`, `Stone Workshop 1`,
  `Nature Workshop`, `Divine Workshop`, `Corrupted Workshop 1/2` in `sharedassets0.assets`;
  the Magic Academy has only elven and corrupted prefabs).
- No in-game run (harness, probe, regression) without the user's go.

## Files

| File | Change |
|---|---|
| `RuinarchPlus/Phase3/Knowledge.cs` | `CanRemember`, `Counts`, `Standing` become `internal`; add `Read`; `Knowledge_HourTick` calls `Records.HourlyCheck` after `Knowledge.HourlyCheck`. |
| `RuinarchPlus/Phase4/Library.cs` | new: `Inner_Maps.Location_Structures.Library : ManMadeStructure`. |
| `RuinarchPlus/Phase4/Records.cs` | new: records, hourly, Library queue, visits prefix, save. |
| `RuinarchPlus/Config.cs` | four keys. |
| `RuinarchPlus/RuinarchPlus.cs` | `Phase4.Records.Register()`. |
| `RuinarchPlus/Phase3/KnowledgePanel.cs` | records suffix on each faction line. |
| `RuinarchDebug/PlusBridge.cs` | record accessors for the harness. |
| `RuinarchDebug/AutoTest.cs` | `RecordsSuite`. |
| `RuinarchPlus/README.md`, `RuinarchPlus-DESIGN.md` | feature + config rows; Phase 4 item 4. |

---

### Task 1: Knowledge entry point

**Files:** `RuinarchPlus/Phase3/Knowledge.cs`

**Produces:** `internal static bool Knowledge.CanRemember(Character)`,
`internal static bool Knowledge.Counts(Character)`,
`internal static bool Knowledge.Standing(LocationStructure)`,
`internal static bool Knowledge.Read(Character reader, LocationStructure s)`: true if the
reader now remembers `s` and did not before.

- [x] Make the three predicates `internal`.
- [x] Add after `Hear`:

```csharp
/// <summary><paramref name="reader"/> read of the structure in a record at home or in their
/// village's Library (Phase4/Records.cs): they remember it, and being at home, it is not
/// news they carry. True if they did not remember it before.</summary>
internal static bool Read(Character reader, LocationStructure structure)
{
	return CanRemember(reader) && !reader.isAlliedWithPlayer && Standing(structure) && Remember(reader, structure);
}
```

- [x] In `Knowledge_HourTick.Postfix`, after `Knowledge.HourlyCheck();` (own try/catch):
  `Phase4.Records.HourlyCheck();`
- [x] Build: `tools/build-mod.sh ../RuinarchMods/RuinarchPlus /tmp/rp-build` (compiles once
  Task 3 exists; build Tasks 1-3 together).

### Task 2: Library building

**Files:** `RuinarchPlus/Phase4/Library.cs`

**Produces:** `Library.FindFor(BaseSettlement) : Library`, `Library.Active`.

- [x] Same shape as `TownHall`: two constructors (`SetMaxHPAndReset(8000)` /
  `SetMaxHP(8000)`, the Workshop's HP), `Active` list, `FindFor`, and
  `AfterStructureDestruction` calling `RuinarchPlus.Phase4.Records.OnLibraryLost(this, settlement)`
  after `base`.

### Task 3: Records

**Files:** `RuinarchPlus/Phase4/Records.cs`, `Config.cs`, `RuinarchPlus.cs`

**Consumes:** Task 1 and 2; `ModBuildings.Add/QueueBlueprint/HasPendingFor/InstantBuild`;
`SettlementTiers.Get`; `Curfew.Binds/Note/Announce`; `MissingPersons.Now`.

**Produces (used by the panel and the harness):**
- `Records.Enabled`
- `Records.LibraryBuilding : ModBuilding`
- `Records.RecordOf(LocationStructure holder) : HashSet<LocationStructure>` (null if none)
- `Records.BooksOf(LocationStructure holder) : List<TileObject>`
- `Records.Write(Character c, LocationStructure holder) : bool`
- `Records.ReadFrom(Character c, LocationStructure holder) : List<LocationStructure>` (all
  entries on a forced read, for the harness: `force` parameter)
- `Records.InstantBuildLibrary(NPCSettlement) : LocationStructure`
- `Records.Summary(Faction, out int homes, out List<NPCSettlement> libraries)`
- `Records.HourlyCheck()`, `Records.OnLibraryLost(Library, NPCSettlement)`

Behaviour (spec sections "Records", "Writing and reading"):

- Record = holder structure (dwelling or Library) + its standing Books + entries.
- Hourly: prune (Books no longer inside their holder; destroyed holders; destroyed
  entries; all Books gone: entries lost, a Library's loss announced); per village of a
  major faction: queue a Library for a Town/City without one (no pending blueprint, no
  `PLACE_BLUEPRINT` job), furnish a Library seen for the first time with `libraryBooks`
  Books; each resident who counts, standing in their own dwelling or their village's
  Library, writes and reads.
- Writing into a record with no standing Book places one first (on a free passable tile
  inside the holder); none free: nothing written.
- Reading: `readChance` % per entry the villager does not remember; learned by reading is
  noted in the event log.
- Visits: prefix on `BehaviourComponent.RunBehaviour`, at most one roll per villager per
  game hour; skipped for anyone the curfew binds.
- Save: `kind|holderId|bookId,bookId|structureId,structureId` (kind `H` or `L`).

- [x] Write the file, register in `RuinarchPlus.OnLoad` after `Phase4.LifeCycle.Register()`,
  add the config keys under the dementia block.
- [x] Build both mods; expect `0 failure(s)`.
- [x] Commit: `Ruinarch+: records - home Books and the Library`.

### Task 4: Panel

**Files:** `RuinarchPlus/Phase3/KnowledgePanel.cs`

- [x] In `Build`, append to each faction line, when `Records.Enabled` and the faction has
  records holding anything: ` (written in 3 homes and the Library of Andorlad)`; homes
  only: ` (written in 2 homes)`; one home: `1 home`.
- [x] Build; commit with Task 3 or separately.

### Task 5: Harness suite (written now, run only on the user's go)

**Files:** `RuinarchDebug/PlusBridge.cs`, `RuinarchDebug/AutoTest.cs`

- [x] Bridge: `RecordOf`, `BooksOf`, `WriteRecord`, `ReadRecord(force)`,
  `InstantBuildLibrary`, `LibraryFor`, `HasPendingLibrary`.
- [x] `RecordsSuite` (spec "Testing" 1-8), dispatched through `Safe` after
  `KnowledgeSuite`; each check skips with a reason when the world lacks what it needs.
- [x] Build RuinarchDebug; commit.

### Task 6: Docs

- [x] README feature row + config rows; design doc Phase 4 item 4: "Shipped (not yet
  verified in game)" until the suite passes; spec status line.
- [x] Commit and push both repos.

### Task 7 (on the user's go): in-game verification

- [x] `tools/run-autotest.sh 1500 RecordsSuite` twice, then a full regression.
