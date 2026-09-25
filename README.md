# AC EVO Simple Telemetry

A small, always-on-top WPF overlay for **Assetto Corsa EVO**. It shows:

- steering-wheel rotation and angle;
- current gear;
- vertical throttle, brake, and clutch gauges;
- a scrolling graph for the same three pedal inputs, with the throttle trace turning purple while TC is active and the brake trace turning yellow while ABS is active.

The settings panel lets you independently show or hide each pedal input, choose a graph history from 5–30 seconds, scale the complete overlay from 75–200%, and switch between dark and light themes. Preferences are saved automatically.

The complete telemetry layout is shown only during an `AC_LIVE` driving session. At other times the overlay collapses to a single status row for waiting, replay, or pause state, with settings and close controls available on hover.

## Run

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet run --project .\ACEvo-Simple-Telemetry.csproj
```

Start Assetto Corsa EVO and enter a driving session. There is no in-game telemetry option to enable. The game and overlay must run in the same Windows session and at compatible privilege levels (normally, run both without Administrator elevation).

Use **Borderless Fullscreen** or **Windowed** display mode in AC EVO. The overlay reasserts its native topmost position when the game takes focus, but Windows cannot composite a normal WPF window over a true exclusive-fullscreen DirectX surface.

Hover the overlay to reveal the settings and close buttons. Drag the telemetry surface to move it, or drag any edge/corner to resize it. The borderless surface keeps rounded corners at every size.

To print raw telemetry to a console, start the executable with `--console-log`:

```powershell
.\bin\Release\net10.0-windows\ACEvo-Simple-Telemetry.exe --console-log
```

The console receives consistent graphics-block snapshots at up to 10 Hz: packet ID, raw `ACEVO_STATUS`, TC-active and ABS-active states, throttle, brake, clutch, raw gear, and signed steering degrees. Unchanged packets are suppressed. Without this argument, no console is attached and no debug telemetry is written anywhere.

## Telemetry implementation

AC EVO 0.6 introduced its updated shared-memory output. This application uses the official HUD-rate graphics mapping `Local\acevo_pmf_graphics`, opened read-only with `MemoryMappedFile.OpenExisting`; it deliberately never creates a mapping when the game is absent.

Only the documented fields required by this overlay are read:

| Byte offset | Type | Field |
| ---: | --- | --- |
| 0 | `int32` | packet id |
| 4 | `int32` | `status` (`0=off`, `1=replay`, `2=live`, `3=pause`) |
| 45 | `bool` | `tc_active` |
| 46 | `bool` | `abs_active` |
| 68 | `int16` | `gear_int` (`0=R`, `1=N`, `2=1st`, …) |
| 76 | `float` | `gas_percent` (`0..1`) |
| 80 | `float` | `brake_percent` (`0..1`) |
| 88 | `float` | `clutch_percent` (`0..1`) |
| 156 | `int32` | signed `steer_degrees` |

The reader checks the packet id before and after each sample so a frame being updated by the game is skipped rather than rendered partially.

## Research sources

- [Kunos/505 Games shared-memory documentation on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3707421508) — canonical mapping names and data layout.
- [Assetto Corsa EVO 0.6 announcement](https://assettocorsa.gg/assetto-corsa-evo-early-access-06-now-available/) — confirms the updated shared-memory library and official telemetry support.
- [Community field-by-field transcription and validation](https://github.com/albertowd/live-telemetry-evo/blob/develop/docs/SHARED_MEMORY.md) — offsets, units, packing, and concurrency notes cross-checked against the official guide.

AC EVO is still evolving, so a future shared-memory version may require updating the documented offsets.
