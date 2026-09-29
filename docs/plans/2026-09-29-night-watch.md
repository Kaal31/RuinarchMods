# Night watch: Implementation Plan

**For agentic workers:** execute task by task; steps use checkbox (`- [ ]`) syntax.

**Goal:** Towns and Cities keep guards on the night schedule who patrol the village at night.

**Spec:** `docs/specs/2026-09-29-night-watch-design.md`.

## Global constraints

- Stock game, Harmony only. Config `nightWatchEnabled` (true).
- Watch: Town/City/capital with 4+ fighters; one guard per 8 residents, 1 to 3; never the
  ruler or faction leader, nobody in an active party; best Martial Arts first.
- Guards: `NocturnalSchedule`; `NightPatrolBehaviour` in their Work hours at home.
- Save `ModData/ruinarch.plus.watch.json` (`settlementId|guardIds`), always written.
- No em or en dashes in docs, logs or messages.

### Task 1: `Phase6/NightWatch.cs`

- [x] Hourly check (name, replace, release, duty), schedule postfix on
  `DailyScheduleComponent.UpdateDailySchedule`, save/load, `GuardsOf`/`Check` hooks.
- [x] Config field; `Register()` in `RuinarchPlus.cs`. Builds, patch check clean.

### Task 2: harness `WatchSuite`

- [x] Bridge `WatchAvailable`, `GuardsOf`, `WatchCheck`; suite checks 1-6 of the spec
  (guards, schedule, night patrol, a wolf fought, save round trip, switched off).

### Task 3: in game

- [ ] `run-autotest.sh 1500 WatchSuite` twice; two full regressions; commit after each
  green step.

### Task 4: docs and release

- [ ] README feature and config rows; design doc Phase 6; release with the uprisings as
  Ruinarch+ 0.10.0.
