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

The console receives packet-consistent hybrid snapshots at up to 10 Hz. It includes physics pedals and intervention signals alongside graphics status, gear, steering, and intervention signals. Unchanged packets are suppressed. Without this argument, no console is attached and no debug telemetry is written anywhere.

## Telemetry implementation

AC EVO 0.6 introduced its updated shared-memory output. This application opens both `Local\acevo_pmf_physics` and `Local\acevo_pmf_graphics` read-only with `MemoryMappedFile.OpenExisting`; it deliberately never creates mappings when the game is absent.

Only the documented fields required by this overlay are read:

| Block | Byte offset | Type | Field |
| --- | ---: | --- | --- |
| Physics | 0 | `int32` | packet id |
| Physics | 4 | `float` | `gas` (`0..1`) |
| Physics | 8 | `float` | `brake` (`0..1`) |
| Physics | 204 | `float` | `tc` intervention intensity |
| Physics | 252 | `float` | `abs` intervention intensity |
| Physics | 364 | `float` | `clutch` (`0..1`) |
| Physics | 672 | `int32` | `tcInAction` |
| Physics | 676 | `int32` | `absInAction` |
| Graphics | 0 | `int32` | packet id |
| Graphics | 4 | `int32` | `status` (`0=off`, `1=replay`, `2=live`, `3=pause`) |
| Graphics | 45 | `bool` | `tc_active` |
| Graphics | 46 | `bool` | `abs_active` |
| Graphics | 68 | `int16` | `gear_int` (`0=R`, `1=N`, `2=1st`, …) |
| Graphics | 156 | `int32` | signed `steer_degrees` |

Pedals come from physics while steering uses the graphics block's degree value. TC and ABS activity are merged from the physics `*InAction` flag, physics intervention intensity, and graphics active flag so any source can mark an intervention.

The reader checks each block's packet id before and after its own snapshot. If either block is being updated, that combined sample is skipped rather than treating independently produced physics and graphics pages as one synchronized frame.

## Research sources

- [Kunos/505 Games shared-memory documentation on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3707421508) — canonical mapping names and data layout.
- [Assetto Corsa EVO 0.6 announcement](https://assettocorsa.gg/assetto-corsa-evo-early-access-06-now-available/) — confirms the updated shared-memory library and official telemetry support.
- [Community field-by-field transcription and validation](https://github.com/albertowd/live-telemetry-evo/blob/develop/docs/SHARED_MEMORY.md) — offsets, units, packing, and concurrency notes cross-checked against the official guide.

AC EVO is still evolving, so a future shared-memory version may require updating the documented offsets.
