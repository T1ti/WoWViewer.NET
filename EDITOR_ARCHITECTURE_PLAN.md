# WTEditor architecture plan

This plan tracks the incremental path from the current viewer/editor prototype to a
scalable editor architecture. Avoid framework-heavy abstractions without a real use case;
each phase should leave the editor runnable and covered by the repository smoke suite.

## Phase 1: application boundary and authoritative session (complete)

- Added renderer-independent `WTEditor.Application`.
- Added immutable client, rendering, camera, keyboard, and window models.
- Added `EditorSession` as the single owner of persisted editor configuration.
- Added `IEditorSettingsStore` and an injected JSON implementation with a testable path.
- Moved settings presentation behind `ISettingsDialogService` and `OpenSettingsCommand`.
- Replaced reflection view lookup with an explicit Avalonia data template.
- Kept D3D-specific conversion in `Dx11ConfigurationMapper` at the UI/infrastructure edge.
- Renderer-only settings no longer emit client-change events or restart the engine.
- Viewport speed/sensitivity update the authoritative session and persist on shutdown.

## Phase 2: renderer session and deterministic lifecycle (implementation complete; hardware validation pending)

1. Define editor-facing renderer state in `WTEditor.Application`:
   `RendererStatus`, `RendererError`, `ViewportTelemetry`, selection snapshots, and
   lifecycle commands. These types must not reference Avalonia, Silk.NET, or D3D11.
2. Introduce a DX11 renderer-session adapter that owns `WowViewerEngine` lifecycle.
   `Dx11View` should retain ownership of Avalonia composition and the D3D device/context,
   while initialization, restart policy, status, and cancellation move to the adapter.
3. Replace fire-and-forget CASC startup with cancellable initialization and a session
   generation. Results from an obsolete generation must never reach a new scene/device.
4. Make cache teardown deterministic. Prefer instance-owned caches; as an intermediate
   step, implement a single device-aware cache reset that stops and awaits every worker,
   clears queues/callbacks/users, and releases GPU resources before engine recreation.
5. Fix shader path resolution, shader hot-reload disposal, scene resource disposal, and
   keyed-mutex release with `try/finally`.
6. Surface initialization/loading/failure state in the viewport rather than only logging.

Exit criteria: client switching and viewport teardown are cancellable, errors are visible,
and the D3D debug layer reports no live renderer-owned objects after a lifecycle test.

Implemented: editor-facing status/telemetry, `Dx11RendererSession`, serialized Avalonia
attach/restart/detach, generation-checked CASC activation, awaited cache-worker reset,
shader/resource fixes, keyed-mutex exception safety, and viewport failure/loading status.
Pending: run the explicit D3D debug-layer lifecycle/live-object verification on hardware.

## Phase 3: editor document and commands (foundation in progress)

1. Add `EditorDocument`/`MapDocument` with identity, load state, dirty state, and changed
   tile/object tracking. Do not store editable document state only in `SceneManager`.
2. Add stable editor object identifiers and immutable selection/inspection snapshots.
3. Add `IEditorCommand` and `UndoService`, including grouped transactions for drag/paint
   operations. Renderer updates should be consequences of document commands.
4. Add `ToolManager` and an `IEditorTool` lifecycle for select, transform, placement,
   terrain, texture, vertex color, and water tools.
5. Expose deliberate renderer commands/queries for map load, selection visualization,
   object updates, and tile refresh. Never bind Avalonia directly to `SceneObjects`.

Exit criteria: a simple object transform is undoable/redoable, marks its document dirty,
and updates the renderer through an application-facing command.

Implemented foundation: `EditorDocument`, stable `EditorObjectId`, immutable object and
selection snapshots, dirty/changed-object tracking, `UndoService` with rollback-capable
grouped transactions, `ToolManager`, `IEditorSceneSink`, and an undoable transform command.
Next: adapt DX11 scene objects to stable IDs, implement the scene sink, and route viewport
selection/gizmo transforms through these application services.

## Phase 4: scalable shell and workflows

- Replace placeholder menus with commands for open, save, close, exit, undo, and redo.
- Add document tabs and active-document navigation only after the document lifecycle exists.
- Add inspector, hierarchy, asset browser, tool options, console/status, and render-settings
  panels as focused ViewModels consuming application snapshots/services.
- Persist layout separately from document data. Keep native window operations in Avalonia
  services/code-behind and application decisions in ViewModels/services.
- Add background operation tracking and cancellation for map open, asset search, and save.

## Phase 5: verification and performance

- Unit-test session transitions, command history, dirty tracking, tool transactions, tile
  set diffing, configuration precedence, and failure/cancellation policies without D3D.
- Add controlled integration tests for renderer lifecycle with fake adapters.
- Keep hardware/D3D tests separate and opt-in; check resize, minimize/restore, engine
  restart, rapid tile traversal, shader failure, device removal, and clean shutdown.
- Replace per-frame tile list/queue scans with desired/queued/in-flight/loaded sets and
  measure CPU time, GPU upload time, queue depth, memory, and rendered instance counts.

Implemented profiling baseline: a viewport-owned native tool window with paired
CPU/GPU stage stacks, asynchronous D3D11 timestamps for resource uploads and world drawing,
smoothed CPU/GPU bottleneck diagnosis, adaptive frame-budget scaling, queue/render counters,
engine-frame wall time, culling cost and visible/tested counts, per-frame resource-upload
activity, and process/GC memory metrics. Sampling can be paused or cleared. Terrain/WMO/M2
visibility also moved to a viewport-local icon toolbar so future viewport types can own different render
controls. Future refinement should add per-pass GPU markers, percentile summaries, capture
export, and hardware regression thresholds.

## Dependency rules

```text
WTEditor.Avalonia ------> WTEditor.Application
        |                         ^
        +--> WoWRenderLib.DX11 ---+
```

- `WTEditor.Application` must remain independent of Avalonia, Silk.NET, and renderer
  implementations.
- Avalonia code-behind may own native windowing, pointer capture, and compositor interop.
- ViewModels should use commands and application services, not construct windows, engines,
  stores, or renderer managers.
- Background work may parse/decode CPU data; D3D11 immediate-context work remains on the
  renderer/UI thread until an explicit deferred-context design replaces that invariant.
- Every code, project, configuration, or shader change must pass
  `build/run-smoke-tests.ps1` before completion.
