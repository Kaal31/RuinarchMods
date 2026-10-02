# Ruinarch Co-op — world-transfer preview 0.1.0

This is the first **paused-world connection test**, not playable co-op yet.
A host makes a complete stock save (world plus SQLite database), transfers it over
TCP, and a guest loads it with the normal game loader. Camera position and zoom
are local on each PC. Both simulations stay paused throughout the test.

Updated for RuinarchModLoader API 1 and its strict package manifest checks.
The package includes `RuinarchCoop.dll`, built against the installed game and loader.
The installed loader accepts the package, and all nine Harmony target methods were
checked against the installed game assembly. Two-PC gameplay has not been verified.
Run `build.ps1 -GameRoot "C:\path\to\Ruinarch"` from PowerShell to rebuild after source changes.
When installed under the game's `Mods` folder, the GameRoot argument is optional.
The Co-op entry beside Eradication, live entity replication, guest abilities,
reconnection, internet lobbies and independent progression are not implemented.
The temporary **CO-OP TEST** overlay is available at the main menu and in-game.

## Install and test on two PCs

1. Install the same game version and RuinarchModLoader on both PCs. Extract the
   preview's `Mods/RuinarchCoop/` into each game's `Mods/` directory. The loader
   installation must already provide `Mods/0Harmony.dll`.
2. For the first test, enable only RuinarchCoop on both PCs. Other mods can mutate
   a paused world. The handshake compares game assembly, mod DLLs and JSON files;
   keep both Mods directories identical, including disabled mods/configurations.
3. On the host create or load a **small Eradication world**, place its portal,
   and finish any setup dialogs. Open **CO-OP TEST** and click **Host current
   world**. It pauses the world and writes a uniquely named `CoopSnapshot-*.zip`
   into the game's normal save directory. Wait for **Listening**.
4. On the guest stay at the main menu. Enter the host's LAN IPv4 address, port
   (default **29471**) and displayed session code, then click **Join**. Allow the
   host game's TCP port through its private-network firewall if prompted.
   `127.0.0.1` is only for two game instances on the same PC.
5. Wait until the host reports **Guest loaded the snapshot**. Close the overlay
   if it covers the map. Pan and zoom on each PC: the other camera must not move.
   Compare landmarks, portal position and villagers. Both clocks must stay paused.
6. Disconnect the guest: its world must remain paused. Use the overlay's
   **Return guest to main menu** button. End the host test to restore gameplay
   buttons; the host remains paused until you resume it manually.

Gameplay buttons and spell-input modules are disabled during this read-only test.
The guest uses a unique `SessionCache/received-*.zip` inside the mod directory;
existing guest saves are not overwritten. Failed connections can be retried from
main menu. Each test retains its snapshot for diagnosis; remove old test archives
manually when no longer needed. LAN connections are not encrypted: use this
preview only with a trusted partner on a trusted network.

## Diagnostics / acceptance criteria

Look in the mod loader log for:

- `COOP_HOST_SNAPSHOT sha256=...`
- `COOP_RECEIVED sha256=...` (must match the host)
- `COOP_GUEST_LOADED ...`
- `COOP_PEER_READY`

Archive checksums prove transferred bytes match, **not** that loaded simulation
states are equivalent. Verify the visual/camera/paused-clock checks above and
report both logs if loading fails. Test an incorrect session code, mismatched mod
configuration, and disconnect during transfer as well. No partial archive should
be loaded. After a rejected guest, end/restart the host test to listen again.

## Building and automated checks

Use the existing loader checkout's build tool:

```bash
/path/to/RuinarchModLoader/tools/build-mod.sh /path/to/RuinarchMods/RuinarchCoop
```

It requires the game's Managed assemblies and the built loader. No game binaries
are included in this repository or preview package. Transport-only tests use .NET 8:

```bash
dotnet run --project tests/CoopTransport/CoopTransport.csproj
```

Validation performed for this preview:

- Compiled against supplied real game Managed DLLs and loader 0.5.0.
- Loader's Cecil patch checker: 9 targets resolved, 0 failures.
- 12 transport checks passed: complete archive, checksum corruption, truncated
  transfer, three invalid length cases, oversized handshake, path traversal,
  duplicate entries, missing database, actual loopback TCP transfer and acknowledgement.
- **Not run in the Unity game / not verified on two PCs.** No live replication is
  implemented. This is an initial-state transfer foundation, not proof that the
  full world simulation can be synchronized live.

## Next milestone

After verifying the initial load in-game, introduce stable entity-ID mapping and
host-to-guest updates, beginning with character positions. Keep the guest's AI
suppressed, retain local camera/UI state, and add state comparisons. Expand to
entity creation/deletion, traits, combat, terrain and buildings before enabling
ability commands. Full-save reloads are unsuitable for continuous replication.
