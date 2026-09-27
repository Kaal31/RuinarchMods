# Records: home Books and the Library (Ruinarch+, Phase 4)

Status: design approved in chat (2026-09-27), awaiting spec review.

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
  Whether non-elven factions can place the Magic Academy prefab is asset data, not code.
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
  through `ModBuildings`. It borrows the Magic Academy prefab if the prefab probe (below)
  shows every major village faction can place it, else the Workshop prefab. It holds
  `libraryBooks` Books; the mod places the missing ones on free tiles inside when it is
  built or loaded. The Library's record is one set shared by its Books: it stands while at
  least one of its Books stands.

Entries leave a record when:

- the Book is destroyed (burned, broken, or removed with its building): a home Book's
  entries go with it; the Library's record goes when its last Book goes or the Library is
  destroyed;
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

**Visits.** A prefix on `BehaviourComponent.RunBehaviour` (the curfew's hook, same
conditions: `Free_Time`, no queued job): a villager whose village's Library names something
they do not remember has a `libraryVisitChance` % per hour of walking there with
`CreateGoToJob(JOB_TYPE.VISIT_STRUCTURE, <free tile inside the Library>)`. No visits under
curfew (`Curfew.IsUnderCurfew`); the curfew's own prefix runs first.

**Dementia.** A forgetful elder who forgets a building (`Knowledge.ForgetOne`) can read it
back from a record. Records slow forgetting; once every record naming it is gone,
forgetting sticks.

**Event log.**
- "Casey read of your Portal in Andorlad's Library." / "Casey read of your Portal at
  home.": when a villager learns a building by reading (at most once per villager per
  building per day).
- "A household in Andorlad started keeping a record of your buildings.": the first entry
  in a new home Book.
- "Andorlad's Library has burned; its records of your Portal and Corrupt Kennel are lost."
  (or "was destroyed"): the Library's record lost. Home Books lost with their houses are
  not announced.

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

1. **Prefab probe (in game, waits for approval):** a RuinarchDebug probe logs, for every
   major village faction type, whether `GetStructurePrefabs` returns Magic Academy and
   Workshop choices, their footprints, and whether the Magic Academy prefab comes with
   `BOOK` objects. Decides the borrowed prefab.
2. Records, reading, writing, save (`Phase4/Records.cs`, `Knowledge.Read`).
3. Library building, Book placement, visits.
4. Panel line, config, README, design doc.
5. `RecordsSuite` in the harness, then a full regression (in game, waits for approval).

## Testing (`RecordsSuite` in `RuinarchDebug/AutoTest.cs`)

1. A villager at home who remembers a building writes it into a new home Book.
2. A member of that household who does not remember it reads it within a day.
3. Burning the home Book removes its record; nobody learns from it afterwards.
4. A Town builds a Library (instant-build fallback when no builder or no room, as the
   Town Hall test does).
5. A villager writes in the Library; another villager of the village learns it after a
   free-time visit.
6. A destroyed Library is announced and its record is gone.
7. Records survive a save and load (`ReplayLoad`).
8. A forgetful elder who forgot a building reads it back from a record.

## Out of scope

- Visible read/write actions (would need new interaction types in the framework and would
  break saves without the mod).
- Records of anything but the player's buildings.
- Stealing or carrying Books between villages.
- Records counting as knowledge without a reader.
