# WTEditor.Avalonia editor architecture

This document is the design contract for editor modes and editing tools. New
tools should follow these rules so selection, terrain, object transforms, and
future editors share the same interaction model.

## Ownership boundaries

- `MainViewModel` owns mode selection, panel visibility, and the active tool
  settings exposed to the user.
- A mode-specific view model owns tool settings and validation. It must not
  mutate scene data directly.
- `Editor3DViewModel` owns viewport input state and the application undo
  service reference. It is the bridge between Avalonia controls and the
  renderer.
- `WowViewerEngine` and `SceneManager` own frame-time input handling,
  projection, GPU uploads, and live scene mutation. They must expose mutation
  results as deltas/snapshots rather than owning an application undo stack.
- `UndoService` is the one editor-wide history. Tools must not create private
  undo/redo stacks.

## Shared coordinate and geometry utilities

Reusable coordinate math belongs in `WTEditor.Application/Geometry`, starting
with `MapCoordinates`. These helpers are pure, deterministic functions with
explicit source/destination names and units. They use primitive numbers and
application-owned value types (`TilePoint`, `TileBounds`); they must not depend
on Avalonia, a renderer, wowlib, a file system, or global client state. They do
not perform I/O, mutate scene state, or own presentation settings.

Services adapt format-specific values into these helpers once when loading
metadata. For example, `MapTerrainMetadataCacheService` reads WDT MODF extents
and caches their projected `TileBounds`. View models own fit/zoom/pan state;
controls convert tile coordinates to pixels and draw. Do not put coordinate
conventions into XAML, code-behind, or duplicate conversion formulas in views.
New coordinate conventions should get explicitly named functions and tests,
not flags that silently change the meaning of a generic vector.

## Mode and tool rules

1. Every editing mode must be represented by one typed `EditorModeId` in the
   input frame. Selection raycasts and selection visuals must be disabled when
   Selection mode is not active; do not add mutually-exclusive boolean gates.
2. A mode must provide a dedicated settings view model and panel. Panels use
   the shared movable/resizable panel host in `MainView`.
3. Tool settings are configuration, not scene mutations. Changing a slider or
   toggle must not create an undo entry.
4. Continuous pointer gestures are actions with a lifecycle:

   ```text
   begin -> update* -> end
                  \-> cancel
   ```

   Begin captures the required before-state. Updates apply live changes. End
   produces one reversible delta and commits one history transaction. Cancel
   restores the before-state and commits nothing.
5. Shift and Control are the shared positive/negative edit modifiers. Tools
   with directional actions must consume `EditAction`, rather than adding
   tool-specific Raise/Lower state.

6. Mode presentation metadata belongs to `EditorModeDefinition`: identity,
   icon, shortcut, description, and capabilities are registered together.
   The shell must not repeat mode IDs in icon switches, keyboard handlers, or
   renderer gates.

7. A family of tools with shared geometry and gesture behavior should use a
   common operation pipeline plus small mode-specific operations. Terrain uses
   `TerrainBrushTool`, `TerrainBrushTools`, and `BrushMath`: the shared path
   owns chunk traversal, falloff, uploads, dirty state, and undo boundaries;
   each operation only computes the next value for one sample.

## Undo/redo contract

Every operation that changes document or scene state must implement
`IEditorCommand` or be recorded as a `DelegateEditorCommand`.

- `Execute()` applies the forward state.
- `Undo()` restores the exact previous state.
- Redo calls `Execute()` again; commands must therefore be deterministic and
  safe to replay.
- A continuous gesture must use `UndoService.BeginTransaction(...)` and end
  with exactly one user-facing history entry.
- If a renderer has already applied a live edit, use
  `UndoService.RecordExecuted(...)` when the gesture ends. Do not execute the
  same delta a second time just to add it to history.
- A failed or cancelled action must dispose its transaction without calling
  `Commit()`.
- Commands must not capture transient UI controls. Capture stable IDs and
  before/after values, and resolve the current renderer/document sink when the
  command executes.
- Undo and redo must update both the authoritative document state and the
  renderer state. Updating only the viewport is not sufficient.

Terrain strokes follow this contract through `TerrainStrokeDelta`: the
renderer captures the before/after vertex arrays for every touched ADT and the
Avalonia layer records the completed delta as one `Terrain stroke` action.

## Spatial brush and range rules

`WorldChunkRange.ForEachChunkInRange` is the shared spatial traversal for
world-editing tools. It is generic over tile and chunk types and performs the
common work once:

- reject unloaded tiles and non-invertible transforms;
- cull tile aggregate bounds against the world-space brush radius;
- transform the center and conservative radius into tile-local space;
- cull individual chunk bounds;
- invoke the tool callback only for intersecting chunks;
- optionally notify the owner once when one or more chunks changed.

Terrain sculpting uses this traversal for mutation, and projected brush rings
use it to build their raycast candidates. Future liquid, foliage, navigation,
or paint tools should use the same traversal instead of scanning every loaded
tile or every chunk independently. The traversal is intentionally mutation-
agnostic: the callback owns the domain-specific operation, while the range
query owns spatial selection and change notification.

Tool modes should use domain enums or registered operations, not numeric magic
values. The renderer uses `EditorModeId` for viewport interaction modes and
`TerrainBrushMode` for terrain operations; adding another target should extend
the appropriate typed registry while reusing the same range and gesture
contracts.

The terrain operation registry is intentionally renderer-independent of UI
panels: adding paint, hole, or vertex-color terrain operations should add a
new `TerrainBrushTool` implementation and registration entry, not another
branch in `SceneManager.ApplyTerrainChunk`. If an operation needs different
data traversal rather than a different per-sample value calculation, it should
introduce a new shared domain pipeline instead of widening the terrain tool
base class with unrelated options.

## Dirty state and saving

Every persisted resource needs an explicit baseline and dirty state.

- `ADTContainer` retains the loaded vertex baseline and exposes `IsModified`.
- Terrain edits refresh that state after every mutation, including undo/redo.
- Modified ADTs remain resident so streaming cannot discard edits before a
  save operation consumes them.
- `SceneManager.GetModifiedTerrainTiles()` is the save handoff. A future ADT
  writer should consume this list, write the tile data, and then call
  `MarkTerrainChangesSaved()` only after the write succeeds.
- Undoing a change back to the loaded baseline clears that tile's modified
  state automatically.
- Save operations must not clear dirty state before the writer reports
  success.

The current repository has the dirty tracking and save handoff, but does not
yet contain an ADT binary writer. Adding one should preserve this contract.

## Adding a new editor

Before merging a new mode or tool, verify:

- it has an explicit mode gate in `InputFrame`;
- it has a mode-specific view model and panel;
- it uses shared `InputModifiers`/`EditAction` semantics where applicable;
- one gesture creates one undo entry;
- cancel, undo, and redo restore the same state byte-for-byte or by an
  equivalent domain comparison;
- persistent resources expose dirty state and are included in the save
  handoff;
- no tool-specific undo stack or direct UI-to-GPU mutation path was added;
- tests cover command replay, transaction grouping, cancellation, and dirty
  state transitions.

## WMO minimap loading boundary

The application core does not reference wowlib. Pure coordinate and footprint
math stays in `WTEditor.Application/Geometry`. File-format parsing belongs in
an infrastructure adapter, not in view models or controls. Calling wowlib from
a host service is a workable adapter boundary, but spreading native format
objects throughout the application is not the desired design.

`WoWRenderLib.Services.IWmoMinimapLoader` / `WmoMinimapLoader` provide the new
CPU-only WMO path in the existing shared library. This does not depend on DX11,
GPU caches, or a live render engine. The loader reads WDT MWMO/MODF, resolves the
root WMO by path or FileDataID/listfile, and parses MOGI group bounds with wowlib.
It returns managed placement metadata and decoded RGBA images; native format
objects are disposed inside the adapter. `MinimapService` owns the Avalonia
bitmaps and applies application coordinate helpers. Existing terrain/catalog
wowlib adapters in Avalonia services can move behind similar shared readers
incrementally; this feature does not refactor all content loading.

The WMO implementation enumerates `<root>_<sourceGroupIndex:000>_<x:00>_<y:00>.blp`
from each MOGI bounding box. Counts are `ceil(width/128)` by `ceil(height/128)`;
filenames are resolved only after enumeration. The listfile is a name resolver,
not the source of tile counts. Group indices are original MOGI indices, never
filtered-list indices. Try `world/minimaps/` names, then logical filenames, then
`world/minimaps/md5translate.trs` mappings to hashed files. Missing tiles are
counted independently without stopping the rest of a group.

A texture's world-unit side is 16, 32, 64, or 128 according to the generator's
whole-group bounding-box thresholds, independent of BLP pixel dimensions.
Offset `(x,y)` starts at `(groupMinX + x*side, groupMinY + y*side)`. Image top-left
is `(groupMinX + x*side, groupMinY + (y+1)*side, groupMaxZ)`; its right and down
vectors are projected with MODF scale, rotation, and translation. Edge tiles
keep their full square footprint rather than stretching into the remaining
bounds. This makes adjacent textures share exactly the same edge.

The view draws the affine quads and retains the green MODF bounds behind them.
Groups are drawn from lower to higher local bounds, with stable group/X/Y
ordering. Floor selection is not yet implemented. Installed Classic Gnomeregan
(WDT 782773) provides 94 textures across 73 groups: bounds-based enumeration
matches the reference list (FileDataIDs 213257–213350) without missing or extra
paths. An offscreen render of the actual control verifies the combined layout.

WMO roots may include the `World/` prefix; strip it when constructing logical
minimap names before adding `World/Minimaps/`. For `_Classic.wmo` replacement
roots, prefer their own texture names and fall back to the unsuffixed root's
names. Gnomeregan's Classic and original roots share the same group indices and
bounds, while the installed textures use the original name.

Native MODF extents use `(model Y, model Z, model X)` at zero rotation. Ground
axis reversal happens once in `PlacementToTile`; reversing the model axes too
would mirror the images away from the bounds used by Fit map.
