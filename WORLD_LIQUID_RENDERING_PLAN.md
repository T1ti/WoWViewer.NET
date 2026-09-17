# DX11 world-liquid rendering implementation plan

Updated: 2026-09-17

## Goal and scope

Add outdoor ADT liquid rendering to `WoWRenderLib.DX11` using the structured liquid data already
exposed by the repository's `Wowlib` 0.0.9 dependency. The first complete implementation must:

- render every visible `MH2O` layer, including partial rectangles, exists masks, height maps,
  depth maps, UV maps, and stacked layers;
- classify and texture water, ocean, magma, slime, and newer material families through WowLib's
  client-database support, with safe fallbacks when a record or texture is unavailable;
- share ADT streaming, ownership, culling, settings, and metrics rather than introduce a parallel
  tile-loading system; and
- keep all Direct3D immediate-context work on the render thread.

This plan intentionally targets outdoor ADT `MH2O` only. Liquid editing/saving, other liquid chunk
formats, underwater post-processing, reflections, and physically accurate refraction are deferred.
The CPU material and mesh types should nevertheless remain reusable by later liquid implementations.

## Current implementation handoff

The working tree now contains the MH2O baseline: managed parsing and mesh generation, a
session-scoped WowLib material/texture catalog with deterministic fallbacks, ADT-owned DX11 upload
and teardown, a separate translucent/emissive liquid pass, tile-bound union, `RenderLiquid` MVVM
settings, live metrics, client LightData colors, first-surface texture resolution, and synthetic smoke
coverage. LightParams liquid alpha is loaded for a future scene-color/refraction implementation, but
must not be used as framebuffer coverage by the current simple pass; likewise, texture alpha must not
mask that pass. Advanced refraction, animated multi-slot wave/foam composition, shoreline intersection,
underwater post-processing, reflections, and per-id diagnostics remain future work. Future agents should
extend those items in the ordered phases below while preserving the MH2O-only boundary.

## Current repository baseline

- `WoWRenderLib/Loaders/ADTLoader.cs` reads an ADT with WowLib on the background ADT worker,
  copies terrain data into `ParsedADT`, and disposes the WowLib ADT before returning.
- `WoWRenderLib.DX11/Cache/ADTCache.cs` uploads each `ParsedADT` on the render thread and owns the
  tile's lifetime. This is the correct place to keep liquid GPU upload and teardown.
- `WoWRenderLib.DX11/Loaders/ADTLoader.cs` creates one `Terrain` resource bundle per tile.
- `WoWRenderLib.DX11/Managers/SceneManager.cs` renders opaque WMO, M2, and terrain passes, performs
  tile/chunk culling, and records workload metrics. Liquids should be a pass after all opaque world
  geometry and before debug overlays.
- `WoWRenderLib.DX11/Managers/ShaderManager.cs` currently has explicit input layouts for ADT, WMO,
  M2, and bounding boxes. It needs an explicit liquid layout and hot-reload branch.
- `WoWRenderLib.DX11/Objects/TileSceneBounds.cs` currently seeds the tile aggregate with terrain-only
  bounds. Liquid above the terrain must be included or coarse tile culling can incorrectly hide it.
- `BLPCache` already provides asynchronous BLP decode, render-thread upload, placeholders, and
  parent-root ownership. Liquid textures should use it rather than a new texture cache.
- `RendererSettings`, the Avalonia rendering configuration, and `Editor3DViewModel` already carry the
  terrain/WMO/M2 visibility toggles end to end. A liquid toggle should follow the same MVVM path.

## Verified WowLib 0.0.9 data surface

The implementing agent should re-check these names against the installed package before coding if
the package version has changed.

### Modern liquid (`MH2O`)

The version-specific `ADTWotlk`, `ADTCataToLegion`, and `ADTBfaPlus` types expose `Water` as
`Mh2OData`. `Water.Cells` contains up to 256 `MapChunkLiquid` values in `y * 16 + x` order. Each
cell has `Fishable`, `Deep`, `HasAttributes`, and a vector of `LiquidInstance` layers.

Each `LiquidInstance` exposes:

- `LiquidType` and `LiquidObjectOrLvf`;
- resolved `VertexFormat` (`height_depth`, `height_uv`, `depth_only`, or `height_uv_depth`);
- `MinHeight`, `MaxHeight`, `XOffset`, `YOffset`, `Width`, and `Height`;
- `ExistsBitmap`, whose LSB-first `width * height` bits select rendered quads; an empty bitmap means
  every quad exists; and
- `Heightmap`, `Depthmap`, and `Uvmap`, each with `(width + 1) * (height + 1)` entries when required
  by the resolved vertex format.

`LiquidObjectOrLvf >= 42` is a `LiquidObject` database id. Smaller values are raw LVF overrides.
Do not repeat the reference viewer's raw-offset parsing: WowLib has already resolved the layout and
decoded the arrays.

### Liquid databases

WowLib's generic database API is suitable for a version-independent catalog:

```csharp
using var table = Db2TableLoader.TryLoad(
    fileSystem,
    "LiquidType",
    out var diagnostic)
    ?? throw new InvalidDataException(diagnostic);
```

`Db2TableLoader` tries the exact `.db2` and `.dbc` paths, resolves them through the WowLib
filesystem/listfile, and reads by FileDataID on CASC (or by canonical path on MPQ). It opens
the table schema against `ClientVersion.FormatLineage`, which is essential for modern Classic
clients: their version tuple is `1.x`/`4.x`, but their DB2 payloads use the current retail WDC
lineage. Do not hard-code FileDataIDs. The relevant
tables are:

- `LiquidObject`: maps object ids to `LiquidTypeId` and supplies flow direction/speed;
- `LiquidType`: supplies material id, flags, colors/floats/coefficients, path-based texture strings, and
  frame counts; and
- `LiquidTypeXTexture` on BfA+: supplies texture FileDataIDs, slots/types, and ordering.

Vanilla/TBC `LiquidType` is a deliberately smaller schema (no `material_id` or `texture[]`); keep
those fields disabled for those eras and use the MH2O type-id fallback. For each client/version,
bind only the lower-snake-case columns that the renderer actually uses. Pass those names directly
and fail if one of those required fields is absent; do not require unrelated fields from another
client's schema.

`LiquidMaterial` can be loaded for future fidelity, but the material-family id already present on
`LiquidType` is sufficient to select the first shader family. Database tables must be loaded once
per client session (or lazily once behind a lock), never once per ADT or per frame.

### DB2 name and texture-linking pitfall

WowLib's `Table.ColumnInfo(index).Name` exposes the exact lower snake_case schema names
(`liquid_type_id`, `file_data_id`, `order_index`, `type`, `texture`, and so on). The implementation
must pass the names for the active client/version directly to `Table.ColumnIndex`; do not normalize
aliases or silently skip a required column. Required-column access wraps a missing name in a
table/column-specific exception so a schema change is actionable, while fields not used by that
version are not looked up. `LiquidTypeXTexture` rows should be grouped by `liquid_type_id`, sorted by
`order_index` (then `type` and row index for deterministic ties), and retain every non-zero
`file_data_id` before requesting those ids from `BLPCache`. Keep the magenta placeholder while a BLP is
pending or genuinely missing so an unresolved link remains diagnosable. This is the same relationship
used by WebWowViewerCpp's `LiquidTypeXTexture` query, but the managed implementation should use
WowLib's typed schema rather than a separate SQLite database.

Classic/legacy clients can expose an empty `LiquidTypeXTexture` table while still providing the actual
assets through `LiquidType.texture[]`. Those path entries may contain a C-style `%d` frame marker
(`frame_count_texture[]` supplies the number of frames) or omit the `.blp` extension. Expand the marker
from frame 1 through the bounded frame count and try the extension-bearing candidate before retaining
the placeholder. Procedural names (`proceduralOceanDepthTex`, `proceduralRiverDepthTex`, and
`proceduralWmoWaterTex`) are depth selectors, not BLP paths; preserve them for water-body classification.

For runtime loading, do not pass a path-only `FileKey` to `Table.Read` on a CASC client: some client
roots have no name hashes even though the DB2 is installed. Load the community listfile before opening
the WowLib filesystem, resolve the exact `DBFilesClient/<table>.db2` or `.dbc` path, and read with an
FDID-only key when resolution supplies a `FileDataID`. MPQ clients retain the resolved path key. The
loader must report both attempted paths, the resolved key/FDID, client version, and the full exception
chain when neither extension can be read. A successful load should emit only
`Loaded client database table '<table>'.`; a failed optional liquid table may keep the magenta
placeholder, while LightData failure explicitly identifies the requested `light_param_id`/`time`
profile before using renderer defaults. The LightData compatibility reader should decode only the
version-specific prefix needed by the renderer. For the current 1.60.1.69876 layout this is the
first 20 inline cells through `ocean_close_color`, `ocean_far_color`, `river_close_color`, and
`river_far_color`; the intervening sky cells are structural decoder slots, not semantic
requirements, and all fields after the prefix remain ignored. The liquid shader should select
ocean/river close/far colors from those LightData values. `LiquidTypeXTexture.type` is the client
water-body selector (0=ocean, 1=river, 2=WMO), with legacy type-id fallbacks when its procedural
row is absent.

## Target CPU and GPU model

Keep WowLib wrapper objects inside `WoWRenderLib.Loaders.ADTLoader.ParseADT`. Their spans and native
views are invalid after the `using var adt` scope. Copy only renderer-owned managed values into the
following conceptual types (names can change during implementation):

```text
ParsedADT
  ParsedWorldLiquid Liquid
    WorldLiquidVertex[] Vertices
    uint[] Indices
    ParsedWorldLiquidBatch[] Batches
    BoundingBox Bounds

ParsedWorldLiquidBatch
  ChunkIndex, FirstIndex, IndexCount
  LiquidTypeId, LiquidObjectId, MaterialKey, Family
  Bounds, IsFishable, IsDeep

WorldLiquidVertex
  Position.xyz, Depth01
  TexCoord.xy, CellCoord.xy
```

Use one liquid vertex buffer and one 32-bit index buffer per ADT, with a batch for every source
liquid layer/material run. A single full layer has only 81 vertices per chunk, but stacked layers
across a tile can exceed 65,535 vertices; 32-bit indices avoid a fragile limit and match terrain's
existing index format. Empty-liquid tiles should hold no GPU buffers.

On the DX11 side, add a `WorldLiquid` resource bundle to `Terrain` (or rename the containing type if
that makes ownership clearer) with vertex/index buffers, batches, local bounds/bounding spheres, and
referenced texture FileDataIDs. `ADTLoader.UnloadTerrain` must dispose its buffers and release every
liquid texture through the same root ADT parent used at acquisition.

## Geometry rules

Put mesh generation in a pure, testable helper, not in `SceneManager`.

### `MH2O`

For each cell and each instance:

1. Validate offsets and dimensions (`0..7`, `1..8`, rectangle contained in the 8x8 chunk grid).
2. Build `(width + 1) * (height + 1)` vertices in row-major order.
3. Use the matching MCNK header position as the origin and the existing terrain unit size
   (`1600 / 3 / 16 / 8`). Match the terrain loader's axes exactly:
   `X = chunk.Position.X - (YOffset + row) * unitSize` and
   `Y = chunk.Position.Y - (XOffset + column) * unitSize`.
4. Use `Heightmap[i]` when the resolved format carries heights; otherwise use the instance's flat
   height (`MinHeight`). Reject/skip non-finite heights with a diagnostic rather than poisoning the
   entire tile bounds.
5. Normalize `Depthmap[i]` to `0..1`; use `1` for depth-only/full-depth ocean data and a documented
   neutral fallback when no depth data exists.
6. Use decoded UV entries multiplied by `3 / 256` for height-UV layouts, matching the reference
   viewer's `s/t * 3.0 / 256.0` conversion. For layouts without UV data, generate stable world-space
   UVs from X/Y using `0.06`. Also retain 0..1 cell coordinates for future shoreline effects.
7. Emit two consistently wound triangles only when the exists bitmap says the quad is present.
8. Compute bounds from emitted vertices, not the declared min/max heights. Drop a batch with no
   emitted indices.

Use the same model matrix as the owning `ADTContainer`; do not add a second coordinate conversion.
Verify winding with back-face culling disabled first, then enable culling only after a visual and
unit-tested orientation check.

## Material catalog and texture ownership

Add a CPU-only `LiquidMaterialCatalog` in `WoWRenderLib` so parsing and material resolution are not
tied to DX11. Build immutable descriptors keyed by the effective liquid type/object and expose a
fallback descriptor for malformed/missing database rows.

Resolution order:

1. When `LiquidObjectOrLvf >= 42`, look up `LiquidObject` and use its `LiquidTypeId`, flow direction,
   and flow speed; otherwise use the instance `LiquidType` directly.
2. Look up `LiquidType`, map its material id to a small renderer enum (`Water`, `Magma`, `Mercury`,
   `Fog`, `LeyLine`, `Fel`, `Swamp`, `Azerite`, `Unknown`), and retain the source parameters without
   exposing WowLib row wrappers.
3. On BfA+, collect ordered texture FileDataIDs from `LiquidTypeXTexture`. On older clients, expand
   the `LiquidType` texture strings/frame counts and resolve each path through the WowLib filesystem.
4. Request only textures referenced by loaded ADTs through `BLPCache.GetOrLoad(device, id,
   rootAdtFileDataId)`. Add those ids to the tile's release set exactly once.
5. If a table, row, or texture is missing, use deterministic colors and a shared placeholder; never
   fail terrain loading because optional liquid appearance data is absent.

For the first visible milestone, support a fallback water/ocean shader and an emissive opaque
magma/slime variant using a static first texture frame. Add animated frames and specialized modern
families only after geometry, ownership, and ordering are correct.

## DX11 render path

### Resources and shader

- Add `WoWRenderLib.DX11/Shaders/liquid.hlsl` and copy it through the DX11 project file.
- Extend `ShaderManager` with a liquid input layout matching `WorldLiquidVertex`, compilation cache,
  disposal, and debug hot reload.
- Add 16-byte-aligned liquid constant buffers for model/view/projection, time, camera position,
  lighting, UV animation, colors/opacity, family flags, and texture-frame selection.
- Add a liquid rasterizer state (initially no culling), a depth-stencil state with depth test enabled
  and depth writes disabled, and explicit alpha/opaque blend choices per material family.
- Use the existing sampler and `BLPCache` SRVs. Unbind liquid SRVs after the pass so later render
  target or resource reuse cannot create D3D11 hazards.

The first shader should be deliberately modest: animated/scaled UVs, texture sampling, depth-driven
shore alpha, ambient/diffuse tint for water, and emissive output for magma/slime. Do not port the
reference project's shader text. Its architecture is useful, but no license file was present at the
reviewed repository root; implement behavior independently.

### Submission and culling

Add a liquid pass in `SceneManager.RenderScene` after WMO, M2, and terrain opaque work, and before
debug overlays:

1. Return immediately when `RenderLiquid` is false.
2. Reuse coarse tile visibility and the terrain render distance.
3. Frustum/distance-test each liquid batch against its own bounds. Do not require `RenderADT`; users
   should be able to hide terrain while keeping liquids visible.
4. Collect visible batches into a reusable list and sort translucent batches back-to-front using
   view-space/bounds depth. Keep deterministic tie-breaking by tile, chunk, and source layer so
   stacked liquids do not flicker.
5. Bind each tile's buffers once, minimize material/SRV changes, and draw batch index ranges.
   Correct transparent ordering has priority over aggressive cross-tile batching.
6. Restore opaque blend/depth/rasterizer state before leaving the pass.

Union `liquid.Bounds` with terrain bounds when seeding `TileSceneBounds`, while retaining separate
terrain and liquid bounds for their own fine culling. Also include liquid height in navigation's
coarse aggregate only; `TerrainTileHeightAvailable` should remain terrain height unless callers are
explicitly changed to request surface height.

### Metrics and controls

- Add `RenderLiquid` (default `true`) through `RendererSettings`, Avalonia configuration/store,
  mapper, `Editor3DViewModel`, and the viewport toolbar using the existing MVVM pattern.
- Add liquid candidate/visible batch counts, draw calls, submitted indices, CPU culling/submission
  time, and an optional GPU timer phase. Include them in the live panel and performance capture.
- Track missing liquid type/object/material/texture counts in load diagnostics, rate-limited per id.

## Ordered implementation phases

### Phase 1: pure parsing and geometry

- Add the managed parsed types and `WorldLiquidMeshBuilder`.
- Extract every `MH2O` layer while the WowLib ADT is alive.
- Compute batches and conservative bounds.
- Add focused tests before any D3D code.

Exit criterion: synthetic inputs produce correct vertices, masks, indices, material keys, and bounds
for all four MH2O vertex formats and representative material families.

### Phase 2: fallback DX11 pass

- Upload/release liquid buffers with the owning `Terrain` in `ADTCache`.
- Add `liquid.hlsl`, render states, culling, sorting, and a fallback color/material path.
- Include liquid bounds in tile aggregates and add the visibility toggle and metrics.

Exit criterion: water/ocean and magma/slime geometry renders at correct positions with stable
transparency, unload/re-entry has no leaked COM or BLP references, and hiding terrain does not hide
liquid.

### Phase 3: WowLib database materials

- Implement the session-scoped `LiquidMaterialCatalog` with `LiquidObject`, `LiquidType`, and
  `LiquidTypeXTexture`.
- Resolve static texture frames through `BLPCache` and add database/fallback diagnostics.
- Validate water, ocean, magma, and slime on at least one old and one modern client.

Exit criterion: known liquid types select the expected family and texture, and missing data degrades
to the fallback without losing the tile.

### Phase 4: animation and modern families

- Advance texture frames and UV flow from renderer time, not wall-clock reads inside the shader
  binding loop.
- Add specialized material-family permutations only where captured scenes show a visible benefit.
- Keep shader variants bounded and cache them in `ShaderManager`.

Exit criterion: animated liquid is deterministic under a supplied time value and produces no new
per-frame allocations or texture-cache churn.

### Phase 5: advanced water, separately reviewed

Depth-based refraction, shoreline intersection, underwater classification, and reflections require
render-graph changes. The current depth texture is DSV-only (`D24UnormS8`, no SRV), and the color RTV
is caller-owned. Before adding those effects, design either a typeless depth texture with DSV/SRV
views plus a scene-color copy, or an engine-owned intermediate color/depth target. Measure the copy
and bandwidth cost. Do not smuggle these changes into the basic liquid pass.

Other liquid chunk formats should be a separately reviewed follow-up using the same material catalog
and shader, with a format-specific mesh adapter and owning-object transform. They must not delay
outdoor ADT liquid rendering.

## Expected file changes

| Area | Likely files |
| --- | --- |
| Parsed data and mesh builder | `WoWRenderLib/Structs/ADT.cs`, `WoWRenderLib/Loaders/ADTLoader.cs`, new liquid helper/catalog files |
| GPU resources | `WoWRenderLib.DX11/Structs/ADT.cs`, `WoWRenderLib.DX11/Loaders/ADTLoader.cs` |
| Lifetime/streaming | `WoWRenderLib.DX11/Cache/ADTCache.cs`, `WoWRenderLib.DX11/Objects/ADTContainer.cs` only if ownership API needs clarification |
| Rendering | `WoWRenderLib.DX11/Managers/SceneManager.cs`, `ShaderManager.cs`, `GpuFrameTimer.cs`, new `Shaders/liquid.hlsl` |
| Bounds and metrics | `TileSceneBounds.cs`, `WowViewerEngine.cs`, Avalonia profile DTO/panel files |
| Settings/UI | `RendererSettings.cs`, Avalonia configuration mapper/store/view model and `Editor3DView.axaml` |
| Tests | new `WTEditor.Avalonia.Tests/WorldLiquidSmokeTests.cs` plus settings/metrics coverage |

Avoid adding business logic to Avalonia code-behind. If `SceneManager` becomes materially larger,
extract a DX11 `WorldLiquidRenderer` that owns states/constants/submission while `SceneManager` owns
pass ordering and visibility inputs.

## Required automated coverage

Add asset-independent tests using synthetic managed inputs:

- MH2O empty exists bitmap emits every quad; sparse LSB-first masks emit only selected quads.
- Rectangle offsets and X/Y axis mapping align with terrain chunk corners.
- Height/depth, height/UV, depth-only, and height/UV/depth decode to expected attributes.
- Flat-height fallback, normalized depth, UV `* 3 / 256`, triangle winding, and bounds are exact.
- Multiple layers remain separate batches and exceed-16-bit aggregate meshes retain valid indices.
- Malformed dimensions/counts/non-finite values are rejected predictably without corrupt bounds.
- Tile aggregate bounds include liquid above terrain.
- Material resolution covers object-to-type indirection, direct LVF/type, missing rows, and texture
  ordering/fallback.
- Settings persist/map `RenderLiquid`; metrics remain zero when the pass is disabled.
- Upload/unload ownership releases each liquid texture and COM buffer once.

After every code, project, configuration, or shader change, run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

Then perform a D3D11 debug-layer run and inspect for resource-binding, use-after-dispose, state-leak,
and unreleased-object messages.

## Manual validation matrix

Use legal local client data and record map/build/tile coordinates in the eventual PR or test notes:

1. WotLK tile with partial `MH2O`, a non-zero rectangle offset, and a sparse exists mask.
2. WotLK magma/slime instance to verify opaque/emissive material selection and decoded UV data.
3. Modern retail tile with stacked layers and `LiquidObjectOrLvf >= 42`.
4. Ocean edge for flat/depth-only data and terrain hidden/visible toggling.
5. Rapid tile-boundary traversal followed by renderer restart to validate ownership and lifecycle.

Capture before/after frame metrics in a water-heavy scene. The pass should add no work when disabled,
no managed allocations in steady-state submission, and no texture requests after residency stabilizes.

## Definition of done

- Outdoor `MH2O` liquid coverage is correct; all source layers and masks are honored.
- Geometry uses the same coordinate system and lifetime as the owning ADT.
- Water-like surfaces blend after opaque geometry with depth reads and no depth writes; opaque
  emissive families use deliberate state.
- Coarse and fine culling cannot reject water solely because it lies above terrain.
- Database/texture failures visibly degrade to a fallback and never fail the terrain tile.
- Toggling liquids off yields zero liquid draws/submitted indices and avoids per-frame liquid work.
- Metrics expose liquid cost; smoke tests pass; manual debug-layer validation reports no new D3D11
  errors or leaks.
- Deferred non-`MH2O` liquid formats and advanced screen-space effects are documented rather than
  partially mixed into the first implementation.

## References reviewed

- WowLib maps guide and structured ADT liquid API:
  <https://skarndev.github.io/wowlib/guide/maps/> and
  <https://skarndev.github.io/wowlib/python/adt/entity/>
- WowLib database guide: <https://skarndev.github.io/wowlib/guide/db/>
- WebWowViewerCpp master at reviewed commit
  `1a8cccbeffc46231c6497e6b3f5bfbf3507d8071` (2026-09-14):
  - ADT layer discovery/culling:
    <https://github.com/Deamon87/WebWowViewerCpp/blob/1a8cccbeffc46231c6497e6b3f5bfbf3507d8071/wowViewerLib/src/engine/objects/adt/adtObject.cpp>
  - mesh generation and exists-mask handling:
    <https://github.com/Deamon87/WebWowViewerCpp/blob/1a8cccbeffc46231c6497e6b3f5bfbf3507d8071/wowViewerLib/src/engine/objects/liquid/LiquidInstance.cpp>
  - object/type/material resolution and caching:
    <https://github.com/Deamon87/WebWowViewerCpp/blob/1a8cccbeffc46231c6497e6b3f5bfbf3507d8071/wowViewerLib/src/engine/objects/liquid/liquidMaterials/LiquidMaterialManager.cpp>
  - material/shader organization:
    <https://github.com/Deamon87/WebWowViewerCpp/tree/1a8cccbeffc46231c6497e6b3f5bfbf3507d8071/wowViewerLib/shaders/slang/forwardRendering/liquids>

The reference viewer confirms the useful architecture—one mesh per decoded layer, mask-driven
topology, database-driven material indirection, material caching, and a separate liquid pass. This
plan deliberately adapts those concepts to this repository's WowLib-decoded data and DX11 lifetime
model rather than copying its raw format parser or renderer abstraction.
