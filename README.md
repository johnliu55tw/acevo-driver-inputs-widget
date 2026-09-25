# Assetto Corsa EVO Simple Driver Inputs Widget

A small, always-on-top WPF overlay for **Assetto Corsa EVO** showing driver inputs. It shows:

- Steering-wheel rotation and angle
- Current gear
- Vertical throttle, brake, and clutch gauges;
- A scrolling graph for the same three pedal inputs, with the throttle trace turning **purple** while TC is active and the brake trace turning **yellow** while ABS is active.

The settings panel lets you configure:

- Which pedal inputs are shown
- Graph history from 5–30 seconds
- Always show telemetry graph: keep the telemetry layout visible outside a driving session (unchecked by default)
- Complete overlay scale from 50–250%
- Dark or light theme

The control bar also has a lock button that prevents dragging and edge resizing. Preferences, the lock state, window position, and the separate telemetry and compact window sizes are saved automatically.

During a Live session, the telemetry layout is shown without a status message. The entire control bar appears only while the pointer is over the overlay. Outside a Live session, the control bar stays visible with a waiting, replay, or pause message. The telemetry layout remains visible if **Always show telemetry graph** is checked; otherwise, the overlay collapses to the compact status and control bar.

## Installation

1. Open the [latest GitHub Release](https://github.com/johnliu55tw/acevo-driver-inputs-widget/releases/latest) and download one Windows x64 executable:
   - **`*-self-contained.exe`** is the compressed standalone build. It needs no separate .NET installation.
   - **`*-framework-dependent.exe`** is the smaller build. Install the [Windows x64 .NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) before running it. On the download page, choose **.NET Desktop Runtime → Windows x64 installer**; the plain .NET Runtime is not sufficient for this WPF app.
2. Save the executable wherever you want to keep the app and run it. There is no installer. To update, download the new version from Releases and replace the old executable.
3. Start Assetto Corsa EVO and enter a driving session. Run the game and overlay in the same Windows session and at compatible privilege levels (normally, neither as Administrator).

Use **Borderless Fullscreen** or **Windowed** display mode in AC EVO. Windows cannot show a normal WPF overlay above a true exclusive-fullscreen DirectX surface.

Version history and release notes are on the [Releases page](https://github.com/johnliu55tw/acevo-driver-inputs-widget/releases).

## Run from source

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet run --project .\ACEvo-Simple-Telemetry.csproj
```

Start Assetto Corsa EVO and enter a driving session. There is no in-game telemetry option to enable. The game and overlay must run in the same Windows session and at compatible privilege levels (normally, run both without Administrator elevation).

Use **Borderless Fullscreen** or **Windowed** display mode in AC EVO. The overlay reasserts its native topmost position when the game takes focus, but Windows cannot composite a normal WPF window over a true exclusive-fullscreen DirectX surface.

During a Live session, hover over the overlay to reveal the control bar. Drag the overlay to move it, or drag any edge/corner of the telemetry layout to resize it when the position lock is off. The borderless surface keeps rounded corners at every size.

To print raw telemetry to a console, start the executable with `--console-log`:

```powershell
.\bin\Release\net10.0-windows\ACEvo-Simple-Telemetry.exe --console-log
```

The console receives packet-consistent hybrid snapshots at up to 10 Hz. It includes physics pedals and intervention signals alongside graphics status, gear, steering, and intervention signals. Unchanged packets are suppressed. Without this argument, no console is attached and no debug telemetry is written anywhere.

## Releasing

Finish and push changes on `main`, then create and push a version tag:

```powershell
git switch main
git pull --ff-only
git tag -a v0.1.0 -m "v0.1.0"
git push origin v0.1.0
```

Replace `v0.1.0` with the next `vX.Y.Z` version. Pushing the tag runs the [release workflow](.github/workflows/release.yml), which validates the project, builds both Windows x64 executables, and publishes them in the matching GitHub Release. The tag supplies the version in the executable metadata and download names. GitHub Release notes serve as the changelog; edit the generated notes to add a short user-facing summary when needed.

If a release workflow fails, fix and push the workflow on `main`, then open **Actions → Release → Run workflow**, select `main`, and enter the existing tag to retry it. The retry builds the original tagged source with the corrected workflow; do not move or recreate the tag.

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

Pedals come from physics while steering uses the graphics block's degree value. The raw physics clutch value is inverted at the reader boundary so the displayed clutch follows the overlay's pedal convention. TC and ABS activity are merged from the physics `*InAction` flag, physics intervention intensity, and graphics active flag so any source can mark an intervention.

The reader checks each block's packet id before and after its own snapshot. If either block is being updated, that combined sample is skipped rather than treating independently produced physics and graphics pages as one synchronized frame.

## Research sources

- [Kunos/505 Games shared-memory documentation on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3707421508) — canonical mapping names and data layout.
- [Assetto Corsa EVO 0.6 announcement](https://assettocorsa.gg/assetto-corsa-evo-early-access-06-now-available/) — confirms the updated shared-memory library and official telemetry support.
- [Community field-by-field transcription and validation](https://github.com/albertowd/live-telemetry-evo/blob/develop/docs/SHARED_MEMORY.md) by [albertowd](https://github.com/albertowd) — the source for the detailed telemetry offsets, units, packing, and concurrency notes used here, cross-checked against the official guide.

AC EVO is still evolving, so a future shared-memory version may require updating the documented offsets.
