# WTEditor.Avalonia architecture and working notes

Last reviewed against the source on 2026-08-13. Keep this file current when a
change alters ownership, threading, renderer lifecycle, or the editor/engine API.

## Solution map

`WTEditor.Avalonia` is the active desktop editor host (`net10.0`, Avalonia 12).
It references `WoWRenderLib.DX11`, which in turn uses the rendering-agnostic
`WoWRenderLib` and the WoW data projects (`WoWFormatLib`, `TACTSharp`, `CascLib`,
`DBCD`, and `BLPSharp`).  The solution still includes earlier WPF/OpenGL hosts;
they are useful references, not the current UI path.

```
Avalonia shell and MVVM
  MainWindow -> MainView -> Editor3DView
                         -> Dx11View
                              -> WowViewerEngine (DX11)
                                   -> SceneManager / ShaderManager / caches
                                   -> CASC + FileProvider + WoW format readers
```

## Application/UI flow

- `Program` builds a small DI container and registers `MainViewModel` and
  `MainWindowViewModel` as singletons. `App` resolves the window view model.
- `MainWindowViewModel.CurrentView` starts as `MainViewModel`; Avalonia's
  reflection-based `ViewLocator` maps view-model names to views.
- `MainViewModel` owns an `Editor3DViewModel`. `Editor3DView` captures pointer
  and keyboard input, with automatic AZERTY/QWERTY key selection, and writes
  input state into that view model.
- `Dx11View` uses the same data context. Every Avalonia render-priority frame it
  translates that state to `WoWRenderLib.DX11.InputFrame`, calls
  `engine.Update(delta, input)` then `engine.Render(delta)`, and copies renderer
  statistics back to the view model for the overlay.

## DX11/Avalonia integration

- `Dx11View` owns the Silk D3D11 device and device context. It requires the
  Avalonia compositor to support `D3D11TextureGlobalSharedHandle` import.
- `WowViewerEngine` renders into a BGRA8 shared texture. With `UseKeyedMutex`
  enabled, the engine releases mutex key `1` after rendering and Avalonia imports
  it using `UpdateWithKeyedMutexAsync(acquire: 1, release: 0)`.
- On a size change, the control resizes the engine, discards the imported image,
  and imports the new shared texture handle. `Dx11View.Cleanup` disposes engine
  and D3D resources when removed from the visual tree.
- This host is Windows/D3D11-compositor specific. Unsupported Avalonia graphics
  backends currently only log a message and leave the viewport blank.

## Renderer lifecycle and data flow

1. `WowViewerEngine.Initialize` constructs `ShaderManager` with
   `AppContext.BaseDirectory/Shaders`, creates `SceneManager`, creates the shared target,
   then selects a WoW product. Shader discovery uses that absolute directory, but shader
   compilation currently reads the relative path `Shaders/<name>.hlsl`; see the risks below.
2. Product initialization reads `.build.info` and starts `Services.CASC.Initialize` on a
   background task. It configures the global `WoWFormatLib.FileProvider`, loads the
   default WDT, and preloads its TEX data.
3. Each render frame updates tiles around the camera, processes queued work, then calls
   `SceneManager.RenderScene` using the active camera.
4. The scene queues ADT tiles surrounding the camera (currently view distance 4).
   ADT/WMO/M2/BLP caches parse/decode on background workers and enqueue render-thread
   GPU uploads. `SceneManager.ProcessQueue` limits these uploads to roughly 10 ms per frame.
5. `SceneManager` renders ADT terrain plus instanced WMO and M2 data, performs camera
   frustum visibility checks, and can expose selection/bounding-volume debug state.

## Responsibilities by project

| Area | Main responsibility |
| --- | --- |
| `WTEditor.Avalonia` | Window layout, editor input, composition-surface presentation, telemetry overlay. |
| `WoWRenderLib.DX11` | Direct3D 11 resources, HLSL compilation, camera/input behavior, scene rendering, async asset caches. |
| `WoWRenderLib` | Shared map/model structures, WDT cache, CASC initialization facade, data providers, raycasting. |
| `WoWFormatLib` | Readers for ADT/WDT/M2/WMO and abstraction over local/CASC file access. |
| `TACTSharp` / `CascLib` | Blizzard storage/build/config retrieval and archive access. |
| `DBCD` | DB2/DBC schema and table loading. |

## Current editor state

- Client, renderer, keyboard, camera, and window settings are persisted as `settings.json`
  beside the executable by `EditorSettingsStore`. The defaults still target a Classic Era
  installation. Writing beside the executable may fail for a packaged install in a
  protected directory; failures currently go only to the console.
- Renderer settings include terrain/model distance, tile radius, movement speed, mouse
  sensitivity, lighting colors, ADT/WMO/M2 visibility, and bounding-volume debugging.
  `Dx11View` applies them to the active engine, and movement speed/sensitivity are also
  refreshed each frame from `Editor3DViewModel`.
- Keyboard choices are `Auto`, `QWERTY`, and `AZERTY`. Auto uses Windows keyboard-layout
  detection; the viewport itself is Windows-specific because of this P/Invoke and D3D11.
- Mouse-wheel state exists in `Editor3DViewModel` and `InputFrame`, but the view never
  populates it and the engine never consumes it. `SetHasFocus` similarly stores an unused
  flag.
- Client-setting changes currently restart the entire engine while retaining the same
  D3D11 device. Camera position/direction are copied through the view model first.

## Threading and ownership invariants

- `Dx11View` creates and owns the D3D11 device/context and disposes them after the engine.
- The update, GPU-upload, render, resize, and normal renderer-disposal paths execute from
  Avalonia's UI dispatcher at render priority. Keep D3D11 immediate-context access on this
  thread unless the engine is deliberately redesigned around deferred contexts.
- ADT/WMO/M2 parsing and BLP decoding run on background tasks. Their queues hand parsed
  CPU data back to `SceneManager.ProcessQueue`, where GPU resources are created.
- The shared-texture keyed-mutex protocol is: renderer acquires key 0 and releases key 1;
  Avalonia acquires key 1 and releases key 0. Any change must preserve this pairing.
- `Services.CASC`, `FileProvider`, `WDTCache`, and the DX11 asset caches are process-global.
  The implementation therefore assumes one active client build, engine, and D3D device.
- Camera/world movement is Z-up. Model transforms convert WoW placement data in
  `Container3D.GetModelMatrix`; avoid duplicating the axis/rotation conversion in UI code.

## Important source index

| File | Why it matters |
| --- | --- |
| `WTEditor.Avalonia/Controls/Dx11View.cs` | Avalonia/D3D interop, frame loop, engine restart, input translation, renderer telemetry. |
| `WTEditor.Avalonia/Views/Editor3DView.axaml.cs` | Pointer capture, focus, QWERTY/AZERTY mapping, input-state reset. |
| `WTEditor.Avalonia/Services/EditorSettingsStore.cs` | Persisted client/editor/window/camera state. |
| `WoWRenderLib.DX11/WowViewerEngine.cs` | Public renderer surface, shared target, camera/input, product and CASC startup. |
| `WoWRenderLib.DX11/Managers/SceneManager.cs` | Tile streaming, scene ownership, selection, culling, batching, render passes. |
| `WoWRenderLib.DX11/Managers/ShaderManager.cs` | HLSL discovery, compilation, debug hot reload, shader resource lifetime. |
| `WoWRenderLib.DX11/Cache/*Cache.cs` | Background workers, reference tracking, GPU-upload queues, static device state. |
| `WoWRenderLib.DX11/Objects/Container3D.cs` | WoW-to-renderer coordinate and placement transform. |
| `WoWRenderLib/Services/CASC.cs` | Global build initialization, CDN fallback, TACT key loading. |
| `WTEditor.Avalonia.Tests/EditorSettingsSmokeTests.cs` | Current automated coverage; not a graphics or streaming test. |

## Risk register and preferred order of work

### P0: engine restart and cache lifetime

- `StartCASCInitialization` is fire-and-forget and has no cancellation, generation token,
  or observed error path. An old task can access a disposed `SceneManager` after a client
  change or viewport teardown.
- DX11 caches are static. M2/WMO retain a cached device pointer, and engine disposal neither
  awaits all workers nor releases all caches. `SceneManager.Dispose` stops WMO/BLP workers
  but leaves ADT/M2 cleanup commented out.
- `StopWorker` cancels without awaiting task completion and leaves shared queues/state that
  a newly started worker can consume.
- Before relying on product switching or multiple viewports, introduce a cancellable
  renderer session with deterministic teardown. Prefer instance-owned caches; if that is
  staged later, first add one explicit, device-aware reset operation.

### P0: shader and D3D resource lifetime

- `ShaderManager.CompileShader` ignores its configured absolute directory and reads a
  relative path. A missing file retries forever with `Thread.Sleep(100)`, which can freeze
  the UI/render thread.
- Hot reload replaces shader structs without disposing the old vertex shader, pixel shader,
  or input layout. `ShaderManager.Dispose` currently only disposes the compiler.
- `SceneManager.Dispose` omits several owned rasterizer/debug buffers and cached resources.
- Keyed-mutex acquire/release in `WowViewerEngine.Render` is not protected by `try/finally`;
  a render exception can leave the shared surface permanently locked.

### P1: initialization and status reporting

- CASC/build failures only reach console output or an unobserved task. Add explicit renderer
  states such as `Created`, `Initializing`, `Ready`, `Failed`, and `Disposed`, and expose a
  status/error snapshot to the editor overlay.
- `.build.info` parsing assumes fixed indices without validating the header or row length.
  The invalid-product diagnostic condition is also too narrow.
- `LoadCurrentProduct` replaces supplied build/CDN config values with `.build.info` values.
  Decide and document precedence before extending the settings UI.
- CASC locale/region are currently hardcoded to `enUS`/`us`, and TACT keys are read/written
  via a relative `WoW.txt` path.

### P1: streaming correctness and scale

- Desired tiles are recomputed every frame with list/queue `Contains` calls. Radius 4 is
  manageable; the allowed radius 32 can make the nested membership checks very expensive.
  Build a desired-tile `HashSet` and diff it against queued/in-flight/loaded sets.
- Unloading an ADT that is still parsing removes the scene callback but does not reliably
  cancel/release the cache request. This can leave stale callbacks, users, and GPU data.
- Worker failure paths can leave placeholders cached without requeueing. Reference tracking
  uses mutable `List<uint>` values and is not consistently synchronized.
- Preserve the rule that parsing is background work and GPU creation is render-thread work,
  but make requests cancellable and scoped to a renderer-session generation.

### P1: editor-facing engine API

- The public engine surface is mostly lifecycle, camera, settings, and statistics. Map
  loading, selection, object edits, scene queries, and visibility live inside
  `SceneManager`.
- Add deliberate engine commands and immutable snapshots instead of exposing
  `SceneManager.SceneObjects` to Avalonia. This provides a clean home for undo/redo,
  dirty-tile tracking, selection inspection, and save operations.

### P2: input, telemetry, and polish

- Wire wheel input and focus, and clamp unusually large frame deltas after attach/restart.
- M2 culling increments `visibleWMOs` in one path, while `visibleM2s` is incremented per
  model group rather than per visible instance.
- `RendererStats.VertexCount` is effectively a submitted-index counter and does not
  multiply instanced geometry by instance count. Rename or redefine it before using it for
  performance analysis.
- Unsupported compositor backends should surface an editor-visible error instead of only
  leaving a blank viewport.

## Verification workflow

After every code, project, configuration, or shader change, run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

As of 2026-08-13 this builds `WTEditor.Avalonia` and passes 4 tests. The tests cover
settings serialization/cloning, normal-window bounds preservation, and camera-direction
restoration. They do not cover D3D initialization, shader compilation, cache teardown,
streaming, input routing, selection, or rendered output. Add focused unit tests around
new non-GPU policies and keep hardware-dependent smoke checks separate and explicit.

For renderer work, also verify manually on Windows with the D3D11 compositor:

1. startup with a valid and invalid client configuration;
2. resize/minimize/restore and clean window close;
3. camera movement, mouse look, selection, and focus loss;
4. tile load/unload while moving rapidly across boundaries;
5. applying renderer settings and changing client/product;
6. debug-layer output for live objects, mutex errors, and device-context warnings.
