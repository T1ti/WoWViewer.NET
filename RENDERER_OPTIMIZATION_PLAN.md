# DX11 world-renderer profiling and optimization plan

Updated: 2026-08-14

## Performance model

CPU and GPU durations are parallel timelines and must not be added. `CPU frame` is the
render-thread work that prepares and submits a frame. `GPU timeline` is asynchronous elapsed
time between timestamp/disjoint-query markers and normally describes an earlier frame. It is
not GPU utilization: if CPU submission is slow enough to drain the command queue, the GPU can
sit idle between those markers and the idle starvation is included in the measured span.

Performance captures must run without the D3D11 debug layer. It is opt-in through
`WTEDITOR_D3D11_DEBUG=1`; validation is useful for correctness diagnostics but can dominate
immediate-context submission in this draw-heavy renderer.

Representative captures must also use a Release build. A controlled 2026-08-13 comparison
measured a heavier Release scene (11,993 draws) at 5.24 ms CPU / 4.83 ms GPU timeline, while
the Debug build needed 75.1 ms / 68.4 ms for 9,478 draws. CPU cost per draw fell from about
7.9 microseconds to 0.44 microseconds. The Debug GPU span was mainly a record of command-queue
starvation, not 68 ms of continuously busy shader/raster work.

The profiler must establish which world pass is expensive before render architecture or
shaders are changed. A pass with high CPU command time and low GPU time is submission-bound.
A pass with low CPU command time and high GPU time needs shader, bandwidth, geometry, or
overdraw investigation. High culling time is independent of draw submission.

## Profiling baseline

The world viewport records the following without flushing or waiting for GPU results:

- CPU culling and CPU command-submission time for WMO, M2, and ADT passes;
- GPU timeline spans for WMO, M2, ADT, and debug-overlay passes;
- visible/loaded-candidate objects for WMO, M2, and ADT;
- draw calls and submitted instance/chunk draws per pass;
- dynamic instance-buffer maps, constant-buffer updates, material texture-binding calls,
  and blend-state binding calls;
- upload submission, observable GPU upload activity, streaming depth, and frame totals.

`Submitted instance draws` counts an instance once for every material/submesh draw in which
it participates. It is deliberately different from the unique visible-instance count: a
large ratio exposes material/submesh amplification. API call counters do not represent bytes
transferred.

GPU pass timings include all elapsed time between their timestamps, including possible queue
starvation. They cannot by themselves separate shader ALU, texture latency, rasterization,
overdraw, cache misses, idle gaps, or driver-inserted pipeline waits. PIX or RenderDoc captures
remain necessary for that level of diagnosis.

## Phase 1: measure representative scenes

Capture stable 10-30 second samples for:

1. terrain only;
2. WMO only;
3. M2 only;
4. the same mixed scene and camera angle used for regressions;
5. a tile-streaming traversal, kept separate from steady-state measurements.

Use the profiler's `Capture 10 s` button. It discards a two-second warm-up so delayed GPU
queries and visibility changes settle, records ten seconds of raw frames, computes
mean/median/P95/P99/maximum summaries, and writes JSON to
`%LOCALAPPDATA%/WTEditor/PerformanceCaptures`. The file records viewport dimensions, camera,
render distances, visibility state, build configuration, D3D11 validation state, pass timings,
culling counts, draw workload, and D3D11 command-pressure counters. Keep the camera stationary
during the four steady-state captures.
Detailed GPU pass timing is optional and disabled by default. When enabled, pass timestamps
are sampled every eight submitted frames to reduce query and driver perturbation; whole-frame
GPU timing, CPU pass timing, and workload counters remain active in lightweight mode. Compare
a mixed-world capture with detailed timing off and on before trusting absolute pass timings on
a new driver/hardware configuration.

Record viewport resolution, visibility toggles, visible/loaded-candidate counts, pass timings, draw
calls, instance/chunk draws, and D3D11 command-pressure counters. Use medians and 95th
percentiles once capture export is available; do not optimize from a single frame.

Exit criterion: the dominant CPU pass and GPU pass are identified, and the result is
repeatable at a fixed camera and viewport size.

## Phase 2: low-risk CPU submission reductions

- Cache immutable model transforms and world-space bounds instead of rebuilding them during
  culling and instance upload.
- Upload visible instance data through a frame ring/arena rather than repeatedly mapping the
  same 1024-matrix dynamic buffer with `WriteDiscard`.
- Cache the currently bound pipeline and material state and suppress redundant D3D11 calls.
- Consolidate constant-buffer writes; use range-bound constant buffers where D3D11.1 support
  is available.
- Build render packets grouped by pass, pipeline, material, mesh, and texture set.

### 2026-08-13 state-cache result

At an identical 1920x977 Release capture with 12,472 draws, suppressing unchanged material
constant-buffer writes and blend-state binds, plus binding WMO geometry once per group,
reduced median CPU frame time from 5.63 ms to 4.73 ms and GPU timeline from 5.00 ms to
4.25 ms. Constant-buffer updates fell from 12,557 to 2,757 and blend-state binds from 6,393
to 406. WMO CPU submission improved by 37 percent and terrain submission by 18 percent.

A generic per-draw shader-resource-view comparison cache was measured and rejected. Although
it suppressed 35 percent of texture-binding calls, scanning and comparing up to 18 COM view
handles per draw raised CPU time from 4.73 ms to 5.89 ms. Future texture-bind reduction should
come from precomputed material keys and render-packet sorting/batching, not a linear slot scan
in the hot draw loop.

Exit criterion: WMO/M2 CPU command time and Map/Update/Bind counts fall without changing the
rendered image or increasing steady-state GPU time.

## Phase 3: shader and terrain cost

- Specialize ADT shaders by active layer count so unused diffuse and height textures are not
  sampled.
- Compute static ADT vertex positions in the shader from the base tile position and each
  vertex's fixed local-grid offset, avoiding per-vertex X/Y uploads. Validate the near/far LOD
  layouts and tile-boundary results before relying on the reduced vertex bandwidth.
- Specialize WMO/M2 material combiners so textures are sampled only when the combiner uses
  them.
- Move inverse-transpose normal-matrix construction out of the vertex shader; use a cached
  per-instance normal transform or a validated uniform-scale shortcut.
- Measure transparent overdraw and consider depth prepasses only where a capture demonstrates
  a benefit.
- Move compatible terrain textures toward arrays and batch chunks that share render state.

Exit criterion: the targeted GPU pass improves at fixed resolution and workload, with shader
output checked against reference screenshots.

### Whole-world baseline (2026-08-13)

At 1920x977 with a 32-tile loading radius, 200,000 terrain distance, and a high-altitude
camera, Release renders 148,902 draws per frame. Terrain accounts for 119,519 visible chunks,
38.5 ms CPU submission, and a 40.4 ms GPU timeline span. M2 evaluates 175,164 instances,
accepts 163,234, spends 8.9 ms culling, and occupies an 11.8 ms GPU span. The complete frame
is about 60.5 ms CPU / 59.0 ms GPU timeline.

Compressed BLP textures already upload their native mip chains and use trilinear mip sampling.
Decoded/uncompressed BLP textures currently upload only mip zero; generating or decoding their
remaining levels is a separate texture-quality and bandwidth task. Texture mipmaps do not
reduce geometry, draw count, CPU culling, or command submission.

Projected-size culling is applied to M2 and WMO sphere diameters before draw-list construction.
The world viewport controls the pixel threshold; zero disables it and selected objects bypass
it. Profiler counters report objects rejected by this test. Terrain requires its LOD mesh path
rather than applying object-style size rejection to individual chunks.

The first high-altitude size-culling result was invalidated by a bounds audit. M2 header bounds
can be collision-oriented and did not contain all render vertices, temporary asynchronous cache
entries could supply the wrong model's bounds, and the ADT placement transform was not applied to
the local sphere. The loader now derives bounds from every uploaded render vertex, waits for the
requested cache entry, and transforms the local sphere through the complete placement matrix,
including ADT scale. At the fixed whole-world camera a 1-pixel threshold now retains about 31,762
of 127,500 candidate doodads rather than only a few hundred. This is the truthful baseline; do not
use the earlier dramatic M2 numbers to justify a more aggressive default.

### Terrain representation hierarchy

The current renderer now has a conservative intermediate terrain tier for loaded, textured
ADTs. Distant chunks retain their 9x9 boundary grid but omit the 8x8 cell-centre vertices,
reducing each chunk from 768 to 384 submitted indices without changing chunk edges or opening
cracks. A projected-size threshold selects this tier; zero disables it and selected ADTs remain
full resolution. This tier preserves the existing per-chunk diffuse/height/alpha materials, so
it does not reduce terrain draw count or texture binding by itself.

A controlled whole-world A/B at the same camera selected this tier for 117,191 of 117,545
visible chunks. It reduced median terrain GPU time from 46.09 to 44.91 ms, median total GPU time
from 56.24 to 54.91 ms, and median CPU frame time from 58.24 to 56.98 ms. Draw count and the
376,424 texture-binding calls were unchanged, confirming that geometry reduction is useful but
that state/material consolidation is the dominant next terrain task.

The active Classic Era client was checked directly before scheduling native `_lod.adt` work.
For build 1.15.9.69109, active Azeroth WDT FileDataID 775971 has 687 resident terrain tiles but
zero nonzero MAID `lodADT` FileDataIDs. MLLL/MLVH/MLVI/MLND/MLSI rendering would therefore be
dead code for the current editor workload. The repository's existing `LODADTReader` remains a
future capability for a product/map that actually advertises native LOD data; it is not on the
active optimization path.

WDL horizon terrain is also deferred until the renderer has fog and a deliberate far-horizon
visual design. The next terrain phase instead targets retained per-ADT material data, texture
arrays/atlases where formats permit them, and larger compatible draw packets so the renderer
stops issuing one heavily rebound draw per visible MCNK.

## Phase 4: retained instance data and GPU culling

- Store static transforms and bounds in GPU structured buffers.
- Run frustum/distance culling in a compute shader and compact visible instance identifiers.
- Fetch transforms through visible-ID indirection and issue
  `DrawIndexedInstancedIndirect` per compatible mesh/material group.
- Keep a CPU path for validation and hardware fallback. Compare its visibility results with
  the GPU path during development.

DX11 does not provide the same flexible multi-draw-indirect model as modern explicit APIs;
heterogeneous mesh/material groups will still require multiple dispatches or indirect draws.
GPU culling is successful when it removes meaningful CPU culling/submission work without
making the already dominant GPU timeline worse.

### DX11 indirect-rendering decision

Indirect rendering is appropriate, but only after retained render packets exist. The planned
M2/WMO path is:

1. retain immutable transforms and bounds in structured GPU buffers per compatible
   mesh/material/LOD packet;
2. compute frustum, distance, and projected-size visibility into a compact visible-ID buffer;
3. write or copy the visible count into a `DrawIndexedInstancedIndirect` argument buffer;
4. issue one indirect draw per compatible packet, with transforms fetched by visible ID;
5. keep the current CPU path for validation and small workloads.

DX11 has `DrawIndexedInstancedIndirect` but no general multi-draw/ExecuteIndirect facility.
Indirect drawing therefore removes CPU visibility-list construction, instance-buffer maps,
and readback, but does not merge heterogeneous materials or textures. It is not the immediate
answer to 119,519 terrain draws: terrain first needs LOD geometry and material/texture-array
consolidation so that many chunks become compatible packets.

## Phase 5: regression tooling

- Add profiler capture export with hardware, resolution, map, camera, settings, and build ID.
- Report median, P95, P99, maximum, and sample count for each pass and counter.
- Add fixed-camera image comparisons and opt-in hardware performance thresholds.
- Keep loading/streaming captures separate from steady-state render regression captures.

The editor now supports an opt-in unattended steady-state benchmark. `build/run-render-benchmark.ps1`
launches the Release executable with detailed GPU pass timing enabled. The viewport waits until a
non-empty world workload has no pending/uploaded resources and its draw/culling counters remain
unchanged for a configured consecutive-frame window. It then performs the normal GPU timing warmup,
records the fixed capture interval, saves the JSON capture, and exits. A hard timeout prevents a
stalled load from leaving an editor process running indefinitely. This makes repeatable A/B captures
available without manual interaction while retaining the interactive capture workflow.

### Per-ADT alpha material consolidation result (2026-08-13)

Terrain alpha maps are now retained in one compact texture array per ADT, with one immutable
chunk-to-slice mapping buffer. The renderer binds both once per ADT instead of binding an individual
alpha texture for every visible MCNK. At the identical whole-world camera used for the terrain LOD
capture, texture-binding calls fell from 376,424 to 259,398 per frame. Median terrain CPU submission
fell from 42.52 ms to 23.97 ms, terrain GPU time from 44.91 ms to 26.34 ms, and total engine frame time
from 57.31 ms to 38.54 ms. A fixed 512-slice allocation was rejected after runtime inspection showed
excessive memory use; the retained array contains only alpha maps actually present in each ADT plus a
shared zero slice.

### Compatible adjacent MCNK batching result (2026-08-13)

After alpha resources became per-ADT state, consecutive visible chunks with the same index LOD and
identical diffuse, height, and layer constants could safely share one indexed draw. Their index ranges
are already contiguous, and the alpha shader derives each chunk's slice mapping from the indexed
vertex identifier. The renderer now coalesces only these exact compatible runs and retains per-chunk
culling and submitted-chunk accounting.

Two back-to-back unattended whole-world captures reduced terrain draws from 117,545 to 38,520 while
still submitting all 117,545 visible chunks. Texture-binding calls fell from 259,398 to 101,351.
Median terrain CPU submission improved from 35.35 ms to 12.31 ms, terrain GPU time from 39.78 ms to
14.79 ms, and total engine frame time from 53.99 ms to 25.96 ms. This confirms that DX11 draw/state
amplification was the dominant terrain cost in this view.

A follow-up runtime-branch experiment that retained the active terrain layer count and guarded the
eight unrolled texture-array samples was measured and reverted. It left the draw workload unchanged
but regressed median terrain CPU submission from 12.31 ms to 13.50 ms and terrain GPU time from
14.79 ms to 16.09 ms. It was subsequently replaced by the separately compiled variants documented
below so resource-array accesses remain statically specialized.

### Correct WMO grouping result (2026-08-14)

WMO packets were keyed by `bool[].GetHashCode()` for the enabled-group mask. Array hashing is
identity-based, so two placements with identical masks usually failed to instance together. A
stable content signature now keys the group and is rebuilt when group visibility changes. At the
fixed whole-world camera, visible WMOs remained 978 while WMO draws fell from 23,776 to 15,098,
instance-buffer maps from 1,625 to 931, and total draws from 63,759 to 55,081. Median WMO CPU
submission fell from 4.37 to 2.88 ms and its GPU timeline span from 9.95 to 6.14 ms.

### Classic terrain shader specialization (2026-08-14)

Classic terrain has at most four layers and does not use `_h` height texturing. The retained runtime
material metadata already expresses this as zero height scale and unit offset. ADT pixel shaders are
now compiled into fixed 1/2/4/8-layer variants and separate height/no-height variants. Classic
batches select the no-height path, which statically removes height-map samples and height-blend
reweighting; height-capable variants remain available for later client formats. Only the active
diffuse slots are bound, and compatible-run batching includes the shader capability in its key.

Against the immediately preceding fixed-layer run at the identical 117,545-chunk, 55,081-draw
workload, removing Classic height texturing reduced median terrain GPU time from 25.54 to 24.76 ms,
whole GPU time from 38.56 to 36.90 ms, and engine frame time from 40.74 to 38.93 ms. This is retained,
but the remaining 38,520 terrain draws are a much larger cost than the shader specialization.

Compressed Classic BLPs already upload all native DXT/BC mip levels and use trilinear sampling with
the full SRV mip range. Only the decoded/uncompressed fallback path uploads mip zero. Therefore
"enable mipmapping" is not an outstanding optimization for the compressed terrain textures in this
capture; future texture work should verify format distribution before changing that fallback path.

### Cached culling inputs (2026-08-14)

The render loop previously reconstructed each immutable MCNK bounding sphere and normalized the
same camera-forward vector inside every projected-size test. Each ADT now caches its 256 conservative
chunk spheres at load time, and the camera direction is normalized once per frame for WMO, M2, and
terrain size tests. Visibility, far-LOD selection, and draw counts remained unchanged.

Three back-to-back post-change captures produced median terrain-culling times of 6.13, 7.47, and
6.02 ms, versus 9.35 ms immediately before the change. Their median engine frame was 32.05 ms and
median GPU frame 30.19 ms versus 38.93/36.90 ms before it. One post-change run was system-noisy at
40.35 ms, so the reliable direct claim is the approximately 2-3 ms culling reduction; whole-frame
improvement should continue to be evaluated as a distribution rather than from the best capture.

### Hierarchical ADT culling and disabled-pass result (2026-08-14)

The terrain pass previously bound its full D3D11 pipeline and scanned the complete mixed
`SceneObjects` collection even when ADT visibility was disabled. In the whole-world scene that
meant type-testing roughly 128,000 WMO/M2 placements to find fewer than 500 ADTs, and the work was
misleadingly reported as about 1 ms of terrain commands. Disabled terrain now reports zero CPU
terrain-command time and skips terrain pipeline setup and collection traversal. Detailed GPU
profiling still emits a back-to-back timestamp pair on sampled frames so query slots remain valid.

Loaded ADTs now have a dedicated retained list and an aggregate box/sphere. One coarse tile test
rejects invisible ADTs, while tiles fully inside the render distance and frustum skip all 256
per-chunk distance/frustum tests. Per-chunk projected LOD selection remains unchanged. Two identical
whole-world captures retained 127,488 candidates, 117,545 visible chunks, and 117,191 far-LOD chunks,
while terrain culling fell from the recent 6.02-6.13 ms range to 4.36/4.40 ms.

### Retained MCNK layer constants (2026-08-14)

Terrain layer scale and height parameters are immutable per MCNK, but the renderer reconstructed
six vectors and called `UpdateSubresource` for nearly every terrain packet. Each ADT now owns one
immutable 24 KB constant buffer containing its 256 chunk records. The shader selects the record by
the existing chunk index, the buffer is bound once per visible ADT, and only lighting constants are
updated once for the terrain pass. Because per-chunk constants no longer belong to draw state,
compatible chunks may also batch across scale/height-constant differences when their actual bound
textures and shader mode match.

At the identical 55,081-draw workload, constant-buffer updates fell from 27,308 to 1,879. The first
controlled capture reduced median terrain CPU submission from 15.88 to 9.95 ms, terrain GPU timeline
from 19.08 to 13.30 ms, and engine frame from 32.28 to 25.20 ms. A repeat measured 7.25/9.52/19.90 ms
respectively. Terrain draw count remained 38,520, proving the improvement came from removing
per-frame CPU-to-GPU constant churn rather than rendering less terrain.

### M2 LOD status

M2 LOD is not currently active. `M2Reader` loads the normal SFID skin array, but render conversion
hardcodes `model.skins[0]` for texture units, submeshes, and indices. The LDV1 chunk and
`lod_skinFileDataIDs` are not parsed. Before adding runtime selection, collect the active Classic
models' skin counts, triangle counts, material compatibility, and ordering; then retain each valid
skin index buffer and split visible instances into projected-size LOD packets. Do not assume an LOD
ordering or introduce visual thresholds without that dataset.

### Combined tile-scene bounds (2026-08-14)

Each loaded ADT now owns a conservative aggregate containing its terrain and the world-space
bounds of its direct WMO/M2 placements and spawned WMO doodads. The aggregate is not allowed to
reject a tile while any asynchronously loaded child lacks a valid bound. Once complete, one
frustum-box test currently bypasses terrain fine-culling work for that ADT. The aggregate includes
WMO/M2/doodad bounds so this rejection remains conservative when objects extend beyond terrain.
WMO/M2 instances are globally resource-batched; measured per-instance tile membership checks
regressed the 130k-instance M2 hot loop while rejecting essentially no extra models in the
whole-world capture, so object consumption is deferred until packets carry contiguous tile ranges
or GPU visible-ID compaction can skip a range without branching on every instance. This deliberately
optimizes partial-world and ground-level terrain views; it adds a small test per
tile and is not expected to help the whole-world benchmark where nearly every tile is visible.

`SceneManager.MarkTileBoundsDirty` gives editor mutations a stable invalidation contract. Current
object movement calls it after changing a transform; future terrain sculpting and liquid/object
mutation paths must do the same and update the retained terrain bounds where applicable. Avoiding
a per-object observer/reference is deliberate because expanding 130k hot-loop objects measurably
regressed M2 culling. Metrics expose `Tile hierarchy culling (CPU)` and coarse-rejected/tested tile counts
so the hierarchy can be disabled or refined if its overhead exceeds the fine tests it saves.

Tile streaming selection was cleaned up in the same phase. Available WDT cells, desired cells, and
load/unload queue membership now use hash sets instead of repeated list/queue scans and nested
loaded-tile range searches. This particularly matters at the supported radius-32 setting.

The final unattended whole-world capture tested 498 loaded tiles and rejected 32. Median hierarchy
cost was 0.035 ms. World streaming fell from the preceding retained-buffer baseline's 1.310 ms to
0.048 ms, while terrain culling fell from 3.281 ms to 1.119 ms with the same 127,488 candidates,
117,545 visible chunks, and 38,520 terrain draws. Median engine/GPU time was 17.31/16.83 ms versus
19.90/18.23 ms in that baseline. Back-to-back runs showed substantial system/GPU variance, so the
stable claims are the streaming and terrain-culling reductions plus unchanged rendered workload;
whole-frame values remain distribution evidence rather than a guaranteed delta.

### WMO portal culling (implemented 2026-08-14)

The format reader now parses root `MOPV`, `MOPT`, and `MOPR` data plus each group file's `MODR`
doodad-reference indices. Render resources retain local portal polygons/planes, exterior/interior and
always-draw group flags, local group bounds, mapped adjacency links, and group-owned doodad indices.
Source group indices are mapped explicitly because non-rendered antiportal groups must not shift portal
targets.

For an exterior camera, traversal seeds enabled exterior groups intersecting the camera frustum. For
an interior camera it seeds all enabled interior AABBs containing the camera, conservatively unioning
overlaps. Directed portal-side tests and successively clipped portal frusta restrict traversal. The
result is placement-specific: placements with identical transient masks remain instanced together,
while different copies never share a visibility decision. Persistent editor group and doodad-set masks
remain separate. Globally resource-batched M2 doodads retain their original WMO doodad index and query
their parent placement's current `MODR` visibility before ordinary frustum/size culling.

Invalid chunk sizes, degenerate portal polygons, invalid indices/ranges, unclassified WMOs, singular
placement transforms, and traversal-budget exhaustion all fall back to the old all-enabled behavior.
Unreferenced/global doodads remain visible. These rules make failures conservative rather than allowing
format anomalies to remove geometry.

Two identical 1920x977 whole-world Release captures reduced WMO draws from 15,098 to 5,299 and total
draws from 55,103 to 45,285. Each frame portal-culled 1,464 WMO group instances and 2,396 group-owned
doodads while preserving 978 visible WMO placements. Submitted indexed triangles fell from 22,985,491
to 19,660,030. Median WMO culling rose from 0.09 to 0.32 ms, but WMO CPU submission fell from 2.99 to
0.85/0.92 ms, WMO GPU time from 5.11 to 3.27/3.38 ms, and engine time from 16.35 to 10.55/10.64 ms.
Visual validation must still cover exterior façades, doors, overlapping group bounds, and transitions
between outside and multiple interior rooms.

### Reviewed next priorities (2026-08-14)

1. Terrain submission remains the largest single CPU stage (7.19 ms median and 38,520 draws in the
   final capture). Continue retained material packets/texture consolidation and compatible-run
   expansion before attempting indirect terrain draws.
2. M2 culling remains the next CPU target (5.62 ms median across about 130k candidates). Avoid
   adding fields or membership branches to every object; reorganize immutable bounds/transforms
   into dense packet arrays, which is also the prerequisite for compute culling and indirect draws.
3. WMO portal parsing, traversal, and MODR-owned doodad filtering are implemented. Complete visual
   validation in building- and city-heavy exterior views, doorway transitions, and multi-room
   interiors. Revisit the traversal only if those views expose bad portal boundaries or source data.
4. Inventory M2 skin/LOD data from the active Classic corpus, then add projected-size LOD packet
   selection only for formats and models that actually contain usable alternative skins.

### Correct geometry workload counters and retained M2 packets (2026-08-14)

The former `VertexCount` was neither a vertex count nor a consistent measure of submitted work. It
added index counts once per API draw, omitted the instance multiplier for WMO/M2, and mixed in
non-indexed debug-line vertices. It has been replaced by 64-bit submitted-index and derived indexed-
triangle counters. Every instanced draw contributes `indexCount * instanceCount`; terrain contributes
the exact near/far index count for all chunks represented by a compatible run. The metrics panel now
reports totals and per-pass triangles, and calls visibility denominators "loaded candidates" rather
than "tested." Low draw count and high triangle count can both be correct when a draw represents many
instances or chunks.

The final pre-packet whole-world capture demonstrates the corrected accounting: 55,103 API draws
represented 68,956,473 submitted indices / 22,985,491 indexed triangles. Of those, terrain was
45,273,216 indices, WMO 13,617,627, and M2 10,065,630. These values expand instancing and batching;
they are not unique mesh-vertex counts.

M2 placements now also retain dense per-resource arrays of exact transformed bounding spheres and
world matrices. The culling loop scans the contiguous sphere array, while submission copies the same
retained matrices instead of returning through `M2Container` for each visible placement. Membership
changes rebuild packets; editor M2 movement invalidates its packet, and WMO movement invalidates M2
packets because parent transforms affect active doodads. Against the pre-packet capture, two runs
reduced median M2 culling from 7.41 ms to 4.20/3.80 ms and M2 submission from 1.33 ms to 0.99/0.91 ms.
The repeat used the identical 130,245-candidate, 31,865-visible workload. This dense layout is the CPU
optimization and data-model prerequisite for the later structured GPU bounds buffer, compute culling,
visible-ID compaction, and indirect draws.

### Retained terrain runs and correct Classic height-texture specialization (2026-08-14)

Each loaded ADT now retains the maximum material-compatible run length beginning at every chunk.
The frame loop only clips that immutable run against visibility gaps and the near/far LOD split,
instead of repeatedly comparing material arrays. Texture SRVs are also resolved once per file-data ID
per frame, preserving asynchronous replacement on the following frame while removing repeated
concurrent-cache lookups from terrain, WMO, and M2 submission.

The DX11 loader also incorrectly selected height-texture terrain shaders from initialized scale values.
Because parsed scale arrays are initialized to one, this classified every Classic chunk as height
textured, bound a second texture set, and executed height-blending samples even though Classic has no
height textures. Selection now requires a positive height-texture file-data ID in an active layer.
This remains valid for newer data, including the parser's deliberate diffuse-texture fallback when a
declared height texture is missing.

On the identical 1920x977 whole-world workload (55,103 draws, 117,545 visible terrain chunks,
22,985,491 submitted triangles), texture binding calls fell from 94,086 to 55,566. Median terrain CPU
submission fell from 8.87 to 6.00 ms, terrain GPU time from 9.87 to 6.97 ms, GPU frame time from 17.65
to 15.64 ms, and engine frame time from 18.24 to 16.35 ms. The intermediate retained-run/frame-texture
capture measured 7.72 ms terrain submission, separating its CPU benefit from the shader correction.

## Immediate interpretation guide

| Observation | Likely next investigation |
| --- | --- |
| High CPU cull, low pass GPU | cached bounds, spatial hierarchy, SIMD/jobs, then GPU culling |
| High CPU commands and high bind/update/map counts | batching, state cache, upload arena, render packets |
| High ADT GPU with terrain filling the viewport | layer sampling, texture bandwidth, overdraw |
| High WMO/M2 GPU with many instance draws | material/submesh amplification, shader variants, transparency |
| Low CPU and GPU but high engine frame | mutex/presentation, streaming, or unprofiled application work |
