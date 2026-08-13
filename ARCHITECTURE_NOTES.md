# WTEditor.Avalonia architecture and working notes

Last reviewed against the source on 2026-08-13. Keep this file current when a
change alters ownership, threading, renderer lifecycle, or the editor/engine API.

## Solution map

`WTEditor.Avalonia` is the active desktop editor host (`net10.0`, Avalonia 12).
It references the renderer-independent `WTEditor.Application` for authoritative editor
configuration/session state and `WoWRenderLib.DX11` for rendering. DX11 in turn uses the rendering-agnostic
`WoWRenderLib` and the WoW data projects (`WoWFormatLib`, `TACTSharp`, `CascLib`,
`DBCD`, and `BLPSharp`).  The solution still includes earlier WPF/OpenGL hosts;
they are useful references, not the current UI path.

```
Avalonia shell and MVVM
  MainWindow -> MainView -> Editor3DView
                         -> Dx11View
                              -> Dx11RendererSession
                                   -> WowViewerEngine (DX11)
                                   -> SceneManager / ShaderManager / caches
                                   -> CASC + FileProvider + WoW format readers

Editor application state
  EditorSession -> client/rendering/keyboard/camera/window snapshots
                -> IEditorSettingsStore
  EditorDocument -> stable objects / dirty state / changed-object set
  UndoService -> editor commands / grouped transactions
  SelectionService + ToolManager -> editor interaction state
  Avalonia/DX11 boundary -> Dx11ConfigurationMapper
```

## Application/UI flow

- `Program` builds a small DI container and registers `MainViewModel` and
  `MainWindowViewModel` as singletons. `App` resolves the window view model.
- `MainWindowViewModel.CurrentView` starts as `MainViewModel`; an explicit typed
  Avalonia data template maps it to `MainView`.
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
  and imports the new shared texture handle. Attach, restart, and detach are serialized;
  the renderer session is asynchronously disposed before D3D resources.
- This host is Windows/D3D11-compositor specific. Unsupported Avalonia graphics
  backends currently only log a message and leave the viewport blank.

## Renderer lifecycle and data flow

1. `Dx11RendererSession` owns a `WowViewerEngine` generation and translates its lifecycle
   into renderer-independent status snapshots for the editor overlay.
2. `WowViewerEngine.Initialize` constructs `ShaderManager` from the absolute
   `AppContext.BaseDirectory/Shaders` path, creates `SceneManager` and the shared target,
   then begins cancellable product loading.
3. CASC builds are created without publishing process-global state. Only the latest live
   engine generation activates its completed build, configures `FileProvider`, loads the
   default WDT, and preloads TEX data. Initialization failures become visible status.
4. Each render frame updates tiles around the camera, processes queued work, then calls
   `SceneManager.RenderScene` using the active camera.
5. The scene queues ADT tiles surrounding the camera (currently view distance 4).
   ADT/WMO/M2/BLP caches parse/decode on background workers and enqueue render-thread
   GPU uploads. `SceneManager.ProcessQueue` limits these uploads to roughly 10 ms per frame.
6. `SceneManager` renders ADT terrain plus instanced WMO and M2 data, performs camera
   frustum visibility checks, and can expose selection/bounding-volume debug state.

## Responsibilities by project

| Area | Main responsibility |
| --- | --- |
| `WTEditor.Application` | Renderer-independent settings, documents, selection, tools, commands, undo history, and renderer contracts. |
| `WTEditor.Avalonia` | Window layout, editor input, composition-surface presentation, telemetry overlay. |
| `WoWRenderLib.DX11` | Direct3D 11 resources, HLSL compilation, camera/input behavior, scene rendering, async asset caches. |
| `WoWRenderLib` | Shared map/model structures, WDT cache, CASC initialization facade, data providers, raycasting. |
| `WoWFormatLib` | Readers for ADT/WDT/M2/WMO and abstraction over local/CASC file access. |
| `TACTSharp` / `CascLib` | Blizzard storage/build/config retrieval and archive access. |
| `DBCD` | DB2/DBC schema and table loading. |

## Current editor state

- Client, renderer, keyboard, camera, and window settings are owned by `EditorSession` and
  persisted as `settings.json` beside the executable by `JsonEditorSettingsStore`. The defaults still target a Classic Era
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
- Client-setting changes restart the entire engine while retaining the same D3D11 device.
  Restart cancellation, cache reset, and recreation are serialized; camera state is copied
  through the view model first.
- The world-viewport profiler is an owned native tool window, so it remains tied to its
  viewport while being movable outside the editor window and onto another display. It retains
  180 frame samples and publishes UI updates at 10 Hz. Its paired stacked bars compare CPU
  command building with GPU execution;
  an adaptive 8.33/16.67/33.33 ms scale avoids compressing fast frames into the graph floor.
- CPU stages are world streaming, resource-upload submission, visibility culling, draw
  submission, and other frame work. GPU stages are measurable resource-transfer activity,
  world drawing, and remaining GPU frame work. The engine-frame card measures `Update` plus
  `Render` wall time; Avalonia scheduling delay is deliberately excluded.
- Culling telemetry exposes both cost and visible/tested counts for terrain chunks, WMO
  instances, and M2 instances. Streaming telemetry also reports resources uploaded per frame;
  a zero GPU transfer duration is hidden because D3D11 initial-data resource creation is not
  always independently observable as an asynchronous GPU pass.
- GPU frame and phase durations come from a four-slot ring of non-blocking D3D11
  timestamp/disjoint queries. Results are polled with `DoNotFlush`, arrive a few frames late,
  and never deliberately stall the immediate context. Unsupported query creation leaves GPU
  timing unavailable.
- Terrain, WMO, and M2 visibility are local to each world viewport. The icon toolbar emits
  an effective rendering configuration without persisting these transient visibility choices;
  engine-wide quality/distance settings remain in the application settings dialog.
- CPU/GPU bottleneck classification compares average CPU submission and GPU execution over
  the most recent 30 comparable samples with a 15% dominance threshold. The panel also
  reports process CPU, managed memory, working set, allocation rate, GC count, draw calls,
  submitted vertices/indices, and pending asset operations.

## Threading and ownership invariants

- `Dx11View` creates and owns the D3D11 device/context and disposes them after the engine.
- The update, GPU-upload, render, resize, and normal renderer-disposal paths execute from
  Avalonia's UI dispatcher at render priority. Keep D3D11 immediate-context access on this
  thread unless the engine is deliberately redesigned around deferred contexts.
- ADT/WMO/M2 parsing and BLP decoding run on background tasks. Their queues hand parsed
  CPU data back to `SceneManager.ProcessQueue`, where GPU resources are created. Teardown
  cancels and awaits all four workers, clears queues/state, then releases GPU caches.
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
| `WTEditor.Avalonia/Rendering/Dx11RendererSession.cs` | DX11 engine generation ownership and editor-facing status translation. |
| `WTEditor.Avalonia/Controls/FloatingMetricsPanel.axaml` | Movable/resizable world-viewport profiler UI and timing descriptions. |
| `WTEditor.Avalonia/Controls/FrameTimelineGraph.cs` | Paired CPU/GPU stacked history, adaptive frame-budget scaling, and profiler color mapping. |
| `WTEditor.Application/Models/PerformanceModels.cs` | Renderer-independent frame snapshots and CPU/GPU bottleneck policy. |
| `WoWRenderLib.DX11/Profiling/GpuFrameTimer.cs` | Non-blocking D3D11 timestamp-query ring for whole-frame, upload, and drawing GPU durations. |
| `WTEditor.Application/EditorDocument.cs` | Document identity, object snapshots, dirty/version state, and changed-object tracking. |
| `WTEditor.Application/Services/UndoService.cs` | Command history and grouped transaction semantics. |
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

### Completed baseline: lifecycle and D3D ownership

- CASC startup is cancellation-aware and generation-checked before publishing global state.
- `Dx11CacheLifecycle.ResetAsync` awaits ADT/M2/WMO/BLP workers before clearing queues,
  references, cached device pointers, and GPU resources.
- Shader paths are absolute, missing shaders fail immediately, replaced/final shaders are
  disposed, omitted scene resources are released, and keyed-mutex release uses `finally`.
- Remaining hardware validation: exercise rapid client switching and inspect D3D11 debug
  live-object output. Static caches still enforce one active DX11 renderer per process.

### P0: complete document-to-renderer editing path

- `EditorDocument`, stable object IDs, selection snapshots, `UndoService`, grouped
  transactions, `ToolManager`, and `TransformObjectCommand` now establish the application
  layer. Implement a DX11 `IEditorSceneSink`, map renderer selection to stable IDs, and
  route actual gizmo transforms through commands rather than direct `SceneManager` writes.

### P1: remaining initialization configuration

- `.build.info` rows are length-checked, but parsing still relies on fixed column positions
  rather than the header names.
- Explicit build/CDN configuration now takes precedence over `.build.info`; add validation
  and explain this precedence in the settings UI.
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

As of 2026-08-13 this builds `WTEditor.Avalonia` and passes 11 tests. Coverage includes
settings/session behavior, normal-window bounds preservation, camera-direction restoration,
document transform undo/redo and renderer notification, grouped undo transactions, and
selection identity, plus instantaneous and rolling CPU/GPU bottleneck classification. It
does not cover hardware D3D initialization, shader compilation,
cache teardown, streaming, input routing, or rendered output.

For renderer work, also verify manually on Windows with the D3D11 compositor:

1. startup with a valid and invalid client configuration;
2. resize/minimize/restore and clean window close;
3. camera movement, mouse look, selection, and focus loss;
4. tile load/unload while moving rapidly across boundaries;
5. applying renderer settings and changing client/product;
6. debug-layer output for live objects, mutex errors, and device-context warnings.
