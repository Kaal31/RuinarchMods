# Records: home Books and the Library (Ruinarch+, Phase 4)

Status: approved (2026-09-27). Plan: `docs/plans/2026-09-27-records-library.md`.

## Goal

Since 0.7.0 a faction's knowledge of the player's buildings lives only in people: a village
knows a building while a living resident remembers it and has told it at home. Death,
dementia and new generations wear it away. After this change knowledge also lives in
**records**:

- A household writes what its people remember into a **Book** in their dwelling.
- A Town or City builds a **Library**, where any villager writes and reads.
- Villagers who spend time where a record is **read** it and remember again. Records refill
  memory after dementia and deaths and teach children and newcomers.
- Records are real objects. Burn the Books and the Library, and kill or outlive the people
  who remember, and the faction loses track of the player.

Records never count by themselves: knowledge still lives only in people, so everything
built on 0.7.0 (a village or faction knows, gossip, witnesses, traders, counterattacks,
rescues, the "Who Knows of You" panel) works unchanged. A record only turns back into
knowledge when a villager reads it.

## Base game (cited against RuinarchRE)

- `TILE_OBJECT_TYPE.BOOK` (`TILE_OBJECT_TYPE.cs:267`, class `Book`): 1x1, 200 HP
  (`TileObjectDB.cs:724-727`). Every tile object gets the `Flammable` trait
  (`TileObject.cs:252`), so a Book burns like other furniture. A Book offers `STUDY_MAGIC`
  only inside a `MAGIC_ACADEMY` (`Book.cs:21`); elsewhere it is plain furniture.
- The Magic Academy is placed only by Elven Kingdom villagers, one per region
  (`CharacterClassBehaviour.cs:22,50`, `PlaceBlueprint.cs:19`). No library exists.
- Structure prefabs are looked up per faction type, falling back to `FACTION_TYPE.None`
  (`StructureData.GetStructurePrefabs`, `StructureData.cs:57-75`); a missing choice throws.
  The game data (`sharedassets0.assets`) has Workshop prefabs for every culture (`Wood
  Workshop 1`, `Stone Workshop 1`, `Nature Workshop`, `Divine Workshop`, `Corrupted
  Workshop 1/2`) but Magic Academy prefabs only for elves and the corrupted set (`Magic
  Academy 1/2`, `Corrupted Wood/Stone Magic Academy 1`).
- A free-time trip to a building uses
  `jobComponent.CreateGoToJob(JOB_TYPE.VISIT_STRUCTURE, tile, out job)`
  (`SocializingBehaviour.cs:78`).

## Mod code this builds on

- `Phase3/Knowledge.cs`: `Memory` (per villager), `Carried`, `NewsOf(c)` (standing
  buildings a counted villager remembers), `Remembers`, `Witness`/`Hear` (learning away
  from home), `ForgetOne` (dementia, called from `Phase4/LifeCycle.cs`), `HourlyCheck`
  (witnesses tell their village), `Knowledge_HourTick` (postfix on the game's tick, every
  20 ticks). `Counts(c)`: alive and not allied with the player (cultists keep secrets).
- `ModBuildings.cs`: `Add(id, name, borrowed)`, `QueueBlueprint`, `HasPendingFor`,
  `InstantBuild`, saved pending blueprints. Used by the Mass Grave (Cemetery prefab) and the
  Town Hall (Tavern prefab).
- `Phase5/SettlementTiers.cs`: `Tier { Village, Town, City }` per village, hourly check.
- `Phase2/Curfew.cs`: `Curfew_RunBehaviour`, a prefix on `BehaviourComponent.RunBehaviour`
  that acts only in `DAILY_SCHEDULE.Free_Time` for a villager with no queued job;
  `Curfew.IsUnderCurfew`, `Curfew.Note` (event log line).
- `Phase5/Unrest.cs`: a destroyed village building is already a grievance.
- `ModSave.Register(id, save, load)` for per-save data in `ModData/`.

## Records

A **record** is a `BOOK` tile object. `Phase4/Records.cs` keeps, per record holder, the set
of player buildings it names:

- **Home Book.** At most one per dwelling. The mod places it (`CreateNewTileObject` +
  the dwelling's `AddPOI`) on a free tile inside the dwelling the first time a resident
  standing at home has something to write. It serves the household: the dwelling's
  residents.
- **Library.** A new village building, id `ruinarch.plus.library`, name "Library", added
  through `ModBuildings`, borrowing the **Workshop** prefab (the only candidate every culture
  can place). When it is first seen built, the mod places `libraryBooks` Books on free tiles
  inside. The Library's record is one set shared by its Books: it stands while at least one
  of its Books stands.

Entries leave a record when:

- the Book is destroyed (burned, broken, or removed with its building): a home Book's
  entries go with it; the Library's record goes when its last Book goes or the Library is
  destroyed. Burned Books are not replaced by themselves: the next villager who writes into
  a home or a Library with no Book left places one new Book, holding only what that
  villager remembers. A record is rewritten only from living memory.
- the building an entry names is destroyed (the `Standing` rule of `Knowledge`).

**Building a Library.** Hourly, a village whose tier is Town or City, with no Library
standing and none pending, queues a Library blueprint through `ModBuildings.QueueBlueprint`,
waiting its turn behind the game's own blueprints (one `PLACE_BLUEPRINT` job per village),
exactly like the Town Hall. Its villagers build it through the game's own pipeline. A
village that falls back to Village keeps its Library.

## Writing and reading

One hourly check (`Records.HourlyCheck`, own postfix on the same tick as `Knowledge`),
after `Knowledge.HourlyCheck`, so a witness who reaches home tells the village before
writing. For each villager `Knowledge` counts (alive, normal, sapient, not allied with the
player):

- **At home** (standing inside their own dwelling):
  - **Write:** every building in `Knowledge.NewsOf(c)` the home Book does not name yet goes
    into it, placing the Book first if the dwelling has none. Always, no roll.
  - **Read:** for each entry they do not remember, a `readChance` % roll; on success they
    remember it (`Knowledge.Read`, below).
- **In the Library** of their own village: the same, against the Library's record.

`Knowledge.Read(Character c, LocationStructure s)` is a new entry point beside `Witness`
and `Hear`: the reader is in their own village, so they remember the building without
carrying it, and the village knows it at once.

**Visits.** A prefix on `BehaviourComponent.RunBehaviour` (the curfew's hook, which the
game calls every tick while a villager is idle; `Character.StartTickGoapPlanGeneration`):
in `Free_Time`, a villager whose village's Library names something they do not remember
rolls `libraryVisitChance` % once per game hour and on success walks there with
`CreateGoToJob(JOB_TYPE.VISIT_STRUCTURE, <free tile inside the Library>)`. Anyone the
curfew binds (`Curfew.Binds`) does not visit, so the order of the two prefixes does not
matter.

**Order.** The records check runs inside the knowledge hourly postfix, right after
`Knowledge.HourlyCheck`, so a witness back home has told the village before anyone writes.
`Knowledge.TellVillage` already makes every resident remember what anyone standing at home
remembers; reading is what restores knowledge when nobody who remembers is left (children,
newcomers, a village whose rememberers died or forgot).

**Dementia.** A forgetful elder who forgets a building (`Knowledge.ForgetOne`) can read it
back from a record. Records slow forgetting; once every record naming it is gone,
forgetting sticks.

**Event log.**
- "Casey read of your Portal in Andorlad's Library." / "Casey read of your Portal at
  home.": when a villager learns a building by reading (event log only).
- "A household in Andorlad started keeping a record of your buildings.": the first entry
  in a new home Book.
- "Andorlad's Library was destroyed; its records of your Portal and Corrupt Kennel are
  lost." / "Andorlad's Library has lost its last Book; its records of ... are lost.": the
  Library's record lost (a notification). Home Books lost with their houses are not
  announced.

## What the player sees

- The "Who Knows of You" section (`Phase3/KnowledgePanel.cs`) adds each faction's records
  to its line: "Aurenad know of your Portal (written in 3 homes and the Library of
  Andorlad)". Nothing is added for a faction with no records.
- Books are ordinary objects: the game's own fire and destruction reach them. No new
  player powers.

## Config

In `config.json` (an existing file keeps its values; missing keys use these defaults):

| Key | Default | Meaning |
|---|---|---|
| `recordsEnabled` | `true` | Home Books and Libraries; needs `knowledgeEnabled`. |
| `readChance` | `25` | Percent per hour that a villager beside a record learns one entry they do not remember. |
| `libraryVisitChance` | `10` | Percent per free-time hour that a villager goes to read in the Library. |
| `libraryBooks` | `4` | Books in a Library. |

## Saving

`ModData/ruinarch.plus.records.json` through `ModSave`, one line per record holder:
`H|<book persistentID>|<dwelling persistentID>|<structure ids...>` for a home Book and
`L|<library persistentID>|<book persistentIDs...>|<structure ids...>` for a Library. On
load, a holder whose Book, dwelling or Library is gone is dropped. As with the other
handlers, the file is always written (even empty) and a null load clears state.

Without the mod a save keeps the Books as plain furniture and the Library as a building of
a type the game does not know, the same as the Town Hall and Mass Grave today.

## Order of work

1. Knowledge entry point (`Knowledge.Read`, predicates made internal).
2. Records, reading, writing, save (`Phase4/Records.cs`, `Knowledge.Read`).
3. Library building, Book placement, visits.
4. Panel line, config, README, design doc.
5. `RecordsSuite` in the harness, then a full regression (in game, waits for approval).

## Testing (`RecordsSuite` in `RuinarchDebug/AutoTest.cs`)

The faction is never made aware of the player (no counterattacks), and forgets everything
taught at the end (`Knowledge.Forget`, `Records.Forget`). A villager's neighbours are told
whatever anyone standing at home remembers, so a reading check first leaves exactly one
record and nobody who remembers.

1. A villager at home who remembers a building writes it into a new home Book (and the
   event log says the household started one); the panel's faction line says where records
   are kept.
2. With nobody left who remembers (dead, moved away, or forgotten like a forgetful elder), a
   member of the household reads it back from the Book.
3. A Book destroyed while nobody remembers takes its record with it; nobody learns from it.
4. A Town or City queues a Library blueprint (skipped with no room for a Workshop-sized
   building).
5. A Library (built at once; villagers take days) holds Books.
6. A villager writes in the Library; with only the Library's record left and nobody who
   remembers, a villager goes there and reads it within a day.
7. Records survive a save and load (`SaveAndRead`, `ReplayLoad`).
8. A destroyed Library is announced and its record is gone.

## Out of scope

- Visible read/write actions (would need new interaction types in the framework and would
  break saves without the mod).
- Records of anything but the player's buildings.
- Stealing or carrying Books between villages.
- Records counting as knowledge without a reader.
