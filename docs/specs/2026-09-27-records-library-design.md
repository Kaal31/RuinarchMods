# Records: home Books and the Library (Ruinarch+, Phase 4)

Status: first version implemented and verified in game (2026-09-27). Amended 2026-09-28
(approved in chat): records are kept on Book Shelves where a building has them, and writing
and reading are real actions that show in the shelf's Logs tab. The amendment is not built
yet. Plans: `docs/plans/2026-09-27-records-library.md` (first version),
`docs/plans/2026-09-28-write-read-actions.md` (amendment).

## Goal

Since 0.7.0 a faction's knowledge of the player's buildings lives only in people: a village
knows a building while a living resident remembers it and has told it at home. Death,
dementia and new generations wear it away. After this change knowledge also lives in
**records**:

- A household writes what its people remember on the **Book Shelf** of their dwelling, or
  into a **Book** when the dwelling has no shelf.
- A Town or City builds a **Library**, where any villager writes and reads.
- Writing and reading are things villagers visibly do in their free time: they walk up to
  the shelf, write or read for about an hour, and the shelf's Logs tab says so.
- Reading turns a record back into memory. Records refill memory after dementia and deaths
  and teach children and newcomers.
- Records are real objects. Burn the shelves, the Books and the Library, and kill or outlive
  the people who remember, and the faction loses track of the player.

Records never count by themselves: knowledge still lives only in people, so everything
built on 0.7.0 (a village or faction knows, gossip, witnesses, traders, counterattacks,
rescues, the "Who Knows of You" panel) works unchanged. A record only turns back into
knowledge when a villager reads it.

## Base game (cited against RuinarchRE)

- `TILE_OBJECT_TYPE.BOOK` (`TILE_OBJECT_TYPE.cs:267`, class `Book`): 1x1, 200 HP
  (`TileObjectDB.cs:724-727`). Every tile object gets the `Flammable` trait
  (`TileObject.cs:252`), so a Book burns like other furniture. A Book offers `STUDY_MAGIC`
  only inside a `MAGIC_ACADEMY` (`Book.cs:21`); elsewhere it is plain furniture.
- `TILE_OBJECT_TYPE.SHELF_BOOKS` (class `ShelfBooks`, shown as "Book Shelf"): furniture
  with no behaviour of its own, placed by the building prefabs that include one. Which
  prefabs do is asset data, not code. `ShelfProbe` (2026-09-28, a Large world of Human
  villages): 6 of 16 dwellings had a built Book Shelf; city centres, fisheries and mines
  had none. So most homes get a placed Book; other cultures are not surveyed yet.
- The Magic Academy is placed only by Elven Kingdom villagers, one per region
  (`CharacterClassBehaviour.cs:22,50`, `PlaceBlueprint.cs:19`). No library exists.
- Structure prefabs are looked up per faction type, falling back to `FACTION_TYPE.None`
  (`StructureData.GetStructurePrefabs`, `StructureData.cs:57-75`); a missing choice throws.
  The game data (`sharedassets0.assets`) has Workshop prefabs for every culture (`Wood
  Workshop 1`, `Stone Workshop 1`, `Nature Workshop`, `Divine Workshop`, `Corrupted
  Workshop 1/2`) but Magic Academy prefabs only for elves and the corrupted set (`Magic
  Academy 1/2`, `Corrupted Wood/Stone Magic Academy 1`).
- Actions. `InteractionManager.ConstructGoapActionData` (`InteractionManager.cs:67-91`)
  makes one `GoapAction` per `INTERACTION_TYPE` by reflection on the type's name and keeps
  them in the dictionary `goapActionData`. An action's states (name, duration in ticks,
  success or fail) come from the dictionary `GoapActionStateDB.goapActionStates`; its
  `PreX`/`PerTickX`/`AfterX` callbacks are found by name on the action's own class
  (`GoapAction.cs:106-130`). The action's name is `goapType.ToStringEnum()`, read from the
  dictionary `StringEnumLookUp._interactionTypeStrings` (`GoapAction.cs:78`,
  `StringEnumLookUp.cs:249-252`). `StudyMagic` is the model for a near-target action on a
  Book (`Read_Icon`, `NEAR_TARGET`).
- A job for one action on one target: `Character.TryCreateJobForSpecificAction`
  (`Character.cs:4140-4164`) builds the node, plan and `GoapPlanJob`.
- A tile object's Logs tab lists the logs that involve it
  (`TileObjectInfoUI.cs:483-487`, `RuinarchSQLDatabase.GetLogsThatMatchCriteria`); a log
  involves an object put into its fillers (`Log.AddToFillers`, `Log.IsInvolved`).

## Mod code this builds on

- `Phase3/Knowledge.cs`: `Memory` (per villager), `Carried`, `NewsOf(c)` (standing
  buildings a counted villager remembers), `Remembers`, `Witness`/`Hear` (learning away
  from home), `Read` (learning from a record), `ForgetOne` (dementia, called from
  `Phase4/LifeCycle.cs`), `HourlyCheck`, `Knowledge_HourTick` (postfix on the game's tick,
  every 20 ticks). `Counts(c)`: alive and not allied with the player (cultists keep
  secrets).
- `ModBuildings.cs`: `Add(id, name, borrowed)`, `QueueBlueprint`, `HasPendingFor`,
  `InstantBuild`, saved pending blueprints. Used by the Mass Grave (Cemetery prefab) and the
  Town Hall (Tavern prefab).
- `Phase5/SettlementTiers.cs`: `Tier { Village, Town, City }` per village, hourly check.
- `Phase2/Curfew.cs`: `Curfew_RunBehaviour`, a prefix on `BehaviourComponent.RunBehaviour`
  that acts only in `DAILY_SCHEDULE.Free_Time` for a villager with no queued job;
  `Curfew.IsUnderCurfew`, `Curfew.Binds`, `Curfew.Note` (event log line).
- `Phase5/Unrest.cs`: a destroyed village building is already a grievance.
- `ModSave.Register(id, save, load)` for per-save data in `ModData/`.
- `Ruinarch.ModContent` (RuinarchModLoader): virtual enum values and `RegisterStructure`.

## Framework: new actions (`Ruinarch.ModContent`, RuinarchModLoader v0.5.0)

A new API beside `RegisterStructure`:

```csharp
ModContent.RegisterAction(new ActionRegistration
{
    Id = "ruinarch.plus.write",        // stable id -> virtual INTERACTION_TYPE
    Name = "WRITE_RECORD",              // the type's enum-style name; goapName "Write Record"
    Factory = () => new WriteRecord(),  // the mod's own GoapAction subclass
    States = { new ActionState("Write Success", 20, success: true) },
});
INTERACTION_TYPE t = ModContent.ActionTypeFor("ruinarch.plus.write");
```

- The value comes from the same allocator as structures (FNV-1a of the id into
  [100000, 1000000)), so it is the same in every run and save.
- A postfix on `InteractionManager.ConstructGoapActionData` adds each registered action to
  `goapActionData`, its states to `GoapActionStateDB.goapActionStates` and its name to
  `StringEnumLookUp._interactionTypeStrings`. Registering the name must happen before the
  factory runs, since the `GoapAction` constructor reads it.
- Any other place that turns an action type into a name or data and throws for an unknown
  value is found during implementation and handled the same way; the in-game checks catch
  the rest.
- Saves: an action in progress is saved as its number; mods register at load, before any
  save loads, so it comes back. Without the mod such a save fails to load, as with any
  mod-added content.
- Documented for strangers in `docs/ASSETS_AND_CONTENT.md` ("Adding a new action").

## Records

A **record** belongs to a **holder** building and names a set of the player's buildings.
It is carried by objects inside the holder: all its **Book Shelves** (`SHELF_BOOKS`), or,
when it has none, Books (`BOOK`) the mod places. `Phase4/Records.cs` keeps, per holder, the
set of names and the carriers.

- **Home.** At most one record per dwelling; it serves the dwelling's residents. The first
  time a resident writes, the record takes the dwelling's Book Shelves as carriers, or
  places one Book (`CreateNewTileObject` + the dwelling's `AddPOI`) on a free tile inside
  when there is none.
- **Library.** A new village building, id `ruinarch.plus.library`, name "Library", added
  through `ModBuildings`, borrowing the **Workshop** prefab (the only candidate every
  culture can place). When it is first seen built, its Book Shelves become the carriers; if
  it has none, the mod places `libraryBooks` Books on free tiles inside.

Books never fill a building's last two free tiles (a Workshop-sized building has only a
few; villagers must still be able to walk in).

Entries leave a record when:

- every carrier is destroyed (burned, broken, or removed with its building): the record
  goes with them. Losing one shelf of two keeps it. Carriers are not replaced by
  themselves: the next villager who writes into a holder with no carrier left starts a new
  record (the holder's shelves if new ones exist, else one new Book), holding only what that
  villager remembers. A record is rewritten only from living memory.
- the building an entry names is destroyed (the `Standing` rule of `Knowledge`).

**Building a Library.** Hourly, a village whose tier is Town or City, with no Library
standing and none pending, queues a Library blueprint through `ModBuildings.QueueBlueprint`,
waiting its turn behind the game's own blueprints (one `PLACE_BLUEPRINT` job per village),
exactly like the Town Hall. Its villagers build it through the game's own pipeline. A
village that falls back to Village keeps its Library.

## Writing and reading

Two new actions, registered through the framework, both near-target on a carrier, with the
game's Read icon and one state of 20 ticks (one game hour):

- **Write** (`ruinarch.plus.write`): when the state ends, every building the writer
  remembers (`Knowledge.NewsOf`) that the record does not name goes into it.
- **Read** (`ruinarch.plus.read`): when the state ends, the reader remembers every entry
  of the record they did not (`Knowledge.Read`: they are in their own village, so they
  remember without carrying it, and the village knows it at once).

An interrupted action does nothing. Requirements: the carrier is built and still carries
the holder's record (or, for a first Write, belongs to a holder with no record), and the
actor is counted by `Knowledge` (alive, normal, sapient, not allied with the player).

**When.** Only in free time, through the prefix on `BehaviourComponent.RunBehaviour` (the
curfew's hook; the game calls it every tick while a villager is idle,
`Character.StartTickGoapPlanGeneration`): in `Free_Time`, with no queued job, at most once
per villager per game hour, in this order:

1. **At home** (standing inside their own dwelling): if they remember a building the home
   record does not name, they Write, no roll. Else, if the record names a building they do
   not remember, a `readChance` % roll; on success they Read.
2. **The Library** of their own village: if it lacks something they remember or holds
   something they do not, a `libraryVisitChance` % roll; on success they Write or Read
   (Write first) on one of its carriers. Walking there is part of the action.

The job is made with the game's own pieces (`TryCreateJobForSpecificAction` pattern, a
free-time job type). Under curfew (`Curfew.Binds`) home writing and reading still happen;
Library trips do not.

The old instant writing and reading in the hourly check are removed. The hourly check
(`Records.HourlyCheck`, inside the knowledge hourly postfix, after `Knowledge.HourlyCheck`)
keeps: queueing Library blueprints, taking a newly built Library's shelves or placing its
Books, and dropping records whose carriers are all gone.

`Knowledge.TellVillage` already makes every resident remember what anyone standing at home
remembers; reading is what restores knowledge when nobody who remembers is left (children,
newcomers, a village whose rememberers died or forgot).

**Dementia.** A forgetful elder who forgets a building (`Knowledge.ForgetOne`) can read it
back from a record in their free time. Records slow forgetting; once every record naming it
is gone, forgetting sticks.

**Logs.**
- Each finished action makes one log with the villager and the carrier as fillers, so it
  shows in the carrier's Logs tab, the villager's logs and the event log: "Jamie wrote of
  your Portal and Corrupt Kennel in a book on the Book Shelf at home." / "Kate read of your
  Portal in the Book at home." (Library: "... in a book on the Book Shelf of Andorlad's
  Library.")
- "A household in Andorlad started keeping a record of your buildings.": the first entry
  of a new home record.
- "Andorlad's Library was destroyed; its records of your Portal and Corrupt Kennel are
  lost." / "Andorlad's Library has lost the last of its books; its records of ... are lost.": the
  Library's record lost (a notification). Home records lost with their houses are not
  announced.

## What the player sees

- Villagers at home in their free time walk to the Book Shelf and write or read for an
  hour; the shelf's Logs tab lists who wrote or read what.
- The "Who Knows of You" section (`Phase3/KnowledgePanel.cs`) adds each faction's records
  to its line: "Aurenad know of your Portal (written in 3 homes and the Library of
  Andorlad)". Nothing is added for a faction with no records.
- Shelves and Books are ordinary objects: the game's own fire and destruction reach them.
  No new player powers.

## Config

In `config.json` (an existing file keeps its values; missing keys use these defaults):

| Key | Default | Meaning |
|---|---|---|
| `recordsEnabled` | `true` | Records and Libraries; needs `knowledgeEnabled`. |
| `readChance` | `25` | Percent per free-time hour that a villager at home reads a record naming something they do not remember. |
| `libraryVisitChance` | `10` | Percent per free-time hour that a villager goes to the Library to write or read. |
| `libraryBooks` | `4` | Books placed in a Library that has no Book Shelf. |

## Saving

`ModData/ruinarch.plus.records.json` through `ModSave`, one line per record:
`<kind>|<holder persistentID>|<carrier persistentIDs>|<structure ids>`, kind `H` (home) or
`L` (Library). Carriers are shelves or Books; the format is the same as the first version,
so its saves load unchanged. On load, a record whose holder or carriers are all gone is
dropped. As with the other handlers, the file is always written (even empty) and a null
load clears state. Actions in progress are the game's own jobs and are saved with them.

Without the mod a save keeps the shelves and Books as plain furniture and the Library as a
building of a type the game does not know, the same as the Town Hall and Mass Grave today;
a save with a Write or Read in progress does not load (see the framework section).

## Order of work

1. Framework: `RegisterAction`, `ActionTypeFor`, the three dictionary entries, docs.
2. Prefab probe in RuinarchDebug: per culture, which buildings have Book Shelves; that
   both actions are registered with states and names (in game, waits for approval).
3. Ruinarch+: carriers (shelves or Books), the Write and Read actions, the free-time
   trigger, logs; remove instant writing and reading.
4. `RecordsSuite` rewritten, then two full regressions (in game, waits for approval).
5. Release RuinarchModLoader v0.5.0, then Ruinarch+ 0.9.0 (requires it).

## Testing (`RecordsSuite` in `RuinarchDebug/AutoTest.cs`)

The faction is never made aware of the player (no counterattacks), and forgets everything
taught at the end (`Knowledge.Forget`, `Records.Forget`). A villager's neighbours are told
whatever anyone standing at home remembers, so a reading check first leaves exactly one
record and nobody who remembers. Most checks queue the Write or Read job directly so they
need not wait for free time; one waits for a natural free-time Write.

1. A Write job on a carrier: the villager walks to it and performs it to the end; the
   record names the building; the carrier's Logs tab has the line (and the event log says
   the household started a record). The panel's faction line says where records are kept.
2. The game saves while a villager is writing: the save completes and the writing finishes.
   Loading a save with a Write in progress needs a game restart, which the harness cannot
   do; that is checked by hand.
3. In their free time, a villager at home who remembers something unwritten writes it
   without being told to.
4. With nobody left who remembers (dead, moved away, or forgotten like a forgetful elder),
   a member of the household reads it back; their log and the carrier's have the line.
5. Destroying every carrier of a home record while nobody remembers takes the record;
   nobody learns from it. Destroying one of several carriers (the Library's) keeps it.
6. A Town or City queues a Library blueprint and its villagers place and build it within
   two days (capitals are Cities from the first hour, so this may happen before the suite
   starts; skipped with no room for a Workshop-sized building). The Library has carriers.
7. A villager writes in the Library; with only the Library's record left and nobody who
   remembers, a villager goes there and reads it.
8. Under curfew, villagers kept home still write there (in `CurfewSuite`, which puts a
   village under curfew; the Library is not checked there).
9. Records survive a save and load (`SaveAndRead`, `ReplayLoad`).
10. A destroyed Library is announced and its record is gone.

## Out of scope

- Records of anything but the player's buildings.
- Stealing or carrying Books between villages.
- Records counting as knowledge without a reader.
- Villagers building or crafting new shelves or Books by themselves.
