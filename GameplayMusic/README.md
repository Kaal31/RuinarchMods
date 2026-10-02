# Gameplay Music 1.1 — dynamic playlists

For Xm0x/RuinarchModLoader, using the loader and Harmony already installed with this game.

## Add music

Place MP3, OGG, or WAV files in the appropriate folders under `Mods/GameplayMusic/Music`:

| Folder | Trigger |
| --- | --- |
| `Ambient` | Normal gameplay |
| `Threat` | The game's retaliation state (angels/nature spirits), including a saved active retaliation |
| `Disaster` | A character gains the Plagued trait, or a fire spreads to at least three objects in one burning source |
| `Summon` | A demon minion is summoned |

Tracks directly in `Music` remain part of the ambient playlist. No need to move or rename existing tracks. Only these named subfolders are scanned; deeper subfolders are ignored. Arbitrary filenames, including spaces, are supported. OGG or PCM WAV are good choices if Unity cannot decode a particular MP3 or WAV encoding.

Restart Ruinarch to load the updated DLL, then start/load a world. Menu and loading music remain native. Music continues while paused, at normal pitch regardless of simulation speed. The game's Master Volume and Music Volume sliders still work. Sound effects and saved settings are not changed.

## How it reacts

- Priority is **Threat > Disaster > Summon > Ambient**.
- Custom tracks crossfade over 3 seconds, including playlist advancement near a track's end. Short clips use a shorter fade.
- An available higher-priority mood can interrupt the minimum hold, after any current crossfade finishes. Lower-priority changes wait until the current mood has played for at least 12 seconds.
- Retaliation music persists through the attack and for 8 seconds after it ends.
- Disaster activity holds its playlist for 25 seconds after the latest qualifying infection or spreading-fire event. It does not measure all ongoing fires or existing plague cases when loading a save.
- Summoning holds its playlist for 12 seconds after the latest summon; it is a temporary playlist rather than an extra sound layered over another track.
- Repeated events extend the mood without restarting its track. Timers use real seconds, including while paused.
- Empty or entirely unreadable event folders fall back to ambient. If no usable ambient tracks exist, the native soundtrack resumes. Native/custom changes restore or mute the Wwise music volume; the two-source crossfade applies to custom audio, not the original soundtrack.
- Tracks loop in shuffled passes by default. With one track, that track repeats. Each folder keeps its own playlist position. Files added while playing are discovered within about 10 seconds; an existing track need not restart.
- Failed files are skipped for the current gameplay session and logged. Leave/re-enter gameplay or restart after repairing a rejected file.

These are world-wide triggers; the camera does not need to be near the event. Ordinary combat, non-demon creature summoning, and every possible village disaster are not triggers in this version.

## Configuration

Edit `config.json` with the game closed, then restart:

| Setting | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | Enable custom playback |
| `shuffle` | `true` | Shuffle each playlist pass; `false` uses sorted full paths (filename order within each folder) |
| `volume` | `1.0` | Extra volume multiplier, 0–1 |
| `dynamicMusic` | `true` | Set `false` to play only ambient/root tracks |
| `crossfadeSeconds` | `3` | Fade length, 0–15 seconds |
| `minimumMoodSeconds` | `12` | Minimum mood hold before a lower-priority switch, 0–120 seconds |
| `disasterSeconds` | `25` | Hold after the most recent disaster trigger, 1–300 seconds |
| `summonSeconds` | `12` | Hold after the most recent demon summon, 1–120 seconds |
| `threatReleaseSeconds` | `8` | Delay after retaliation ends, 0–120 seconds |

For ordered playback, set `shuffle` to `false` and prefix files with `01`, `02`, etc. Previous enabled/shuffle/volume choices were preserved when upgrading.

## Troubleshooting and removal

Look for `[local.gameplaymusic]` in `Mods/mods.log`. Playback entries identify the selected mood and filename. No audio is included or downloaded.

Disable the mod in the Mods window and restart to stop using it, or close the game and remove only `Mods/GameplayMusic`.

## Build and checks

Run `build.ps1 -GameRoot "C:\path\to\Ruinarch"` to compile the three C# files against your installed game, loader, and Harmony. Windows' .NET Framework compiler is used; no SDK installation is needed. When already installed under the game's `Mods` folder, the GameRoot argument is optional. Copy `config.example.json` to `config.json` to customize settings when building from source; missing settings use the defaults listed above.

Validation: compiled successfully; 24 automated checks passed for priority, cooldowns, world resets, sorted/shuffled playlists, newly added files, and missing/corrupt-track fallback. Reflection checks verified all three Harmony target methods, the saved retaliation state property, and the loader entry interface against the installed assemblies.

In-game audio decoding, audible fades, and live Harmony execution remain unverified. Add music and check: native menu music; ambient after loading; demon summon switches to Summon; a new plague infection/spreading fire switches to Disaster; retaliation takes priority; eventual return to ambient; Music/Master sliders; pause/resume; exit to main menu; empty event folders.

References: https://github.com/Xm0x/RuinarchModLoader and https://github.com/Xm0x/RuinarchRE.
