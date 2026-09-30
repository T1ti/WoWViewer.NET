# 3.3.5.12340 CPU scene and submission audit

Updated 2026-10-01. This is the CPU dossier for
[CLIENT_335_RENDERING_ACCURACY_PLAN.md](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md).
The reference is the 32-bit 12340 IDB recorded in [snapshot.json](snapshot.json);
the target is the DX9 SM3 configuration. The original executable SHA-256
remains unpinned.

## Working loop

For a fresh thread, begin with the plan's
[handoff](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#start-here-in-a-new-thread)
and [working loop](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#working-loop).
Use this dossier by section/address, not as a required full read. The first
implementation slice for the current WMO/doodad lighting focus is callback
frustum retention, doodad sphere acceptance and the fog writer at `0x799C91`.
See the [static doodad batch](#static-wmo-doodad-ownership-and-baked-lighting-2026-10-01).
The remaining exterior occlusion slice is protected world-horizon source recovery: writers of
the sort-table line list at bucket offset 0x3C (`0xCD9084` for bucket zero),
endpoints at `0x7938BC` and `0x7CC880`/`0x78F900`, then native terrain bounds/
availability, callback/doodad consumers and volume submission. Terrain edge
producers and box reader `0x78FDC0` now feed the WMO gates.
Static CPU volumes, sphere rejection and `0x7CCFA0` early-out are ported;
`0xD2DCEC` is the volume count, not a clip-enable flag. Source activation/
streaming lifetimes remain open; scene order, AOI bounds, rebucketing, overlap
and cache switching are ported.
The handoff links the existing code, tests and relevant contracts.

Use focused live IDA for missing contracts and small saved slices for known
ones. CPU exports are optional. After a code batch, keep one compact entry here:
`rule; build/address/sites; port; verification; uncertainty; next/acceptance`.
Record newly discovered dependencies and evidence changes in that entry; update
the exhaustive CSV/JSON tables only at milestones or sign-off. Report implemented
behaviors and concrete blockers, not export counts as rendering progress.

**Evidence snapshot:** the coverage tables below describe the saved 2026-09-30
export audit. The portal, exterior group-order and scene-wide queue entries add
bounded live instruction/xref findings; callback, gate, forwarding, list identity,
subtraction, scene-consumer, source queue/bounds, bucket arithmetic and shared
placement-cache, camera-AOI, live visible-list and static-occluder producer/
predicate/sphere/cache-bypass and terrain-buffer producer/reader/phase/column/
distance-window claim deltas await milestone reconciliation.
The 2026-10-01 static doodad ownership, MODD split, lighting callback/material
gate and per-instance shader claims also await that reconciliation.
No export counts changed. Cached counts must not be used to
imply complete or current semantic coverage.

## Roadmap

Completed in the portal implementation batch: Stormwind directed-reference fixtures and
camera-on-portal projection fixes, native clipping classification/plane order,
previous-group/depth rules, null/unused graph handling, and selected-placement
exterior-window culling. Full smoke passes **494 tests**; final-frame parity
is still pending.

Viewport controls batch: mode selection is confined to viewport Advanced
Rendering; Settings edits client values in either mode. Editor fog defaults off,
while Client mode excludes all viewport rendering overrides and retains game fog
and game glow. Client projection/culling stays version-scoped; other clients'
baseline fallback is not native-rule evidence. Verified: conflicting banks,
mode restoration, default fog, separate glow and manual-lighting isolation pass;
full smoke exits 0 with **524 tests** (66 Render, 267 DX11, 191 Avalonia).
Native streaming, animation/particle eligibility and fades still
need the recovered policies and captures in the priorities below.
Native CPU closure and matched pixel evidence remain open in the priorities below.

Exterior queue batch: the depth-zero blocker/view lists, emission bit 8 and
ordered forwarding are implemented. Full smoke exits 0 with **544 tests**
(66 Render, 287 DX11, 191 Avalonia), including 20 new cases. The callback ABI
and installations and the always-enabled emission gate are instruction-supported
for this slice; full callback consumers and native occlusion remain open.
See [the compact entry](#exterior-portal-render-view-queues-2026-09-30).

Interior window/complement batch: primary scene preparation retains ordered
interior windows and builds the native rectangle complement, separately from
exterior polygons and sky/exterior unions. Full smoke exits 0 with **565 tests**
(66 Render, 308 DX11, 191 Avalonia), including 21 new cases. Native fragment
limits, zero-area windows, scene gates and reset behavior are covered; GPU volume
submission and matched pixels remain open. See
[the compact entry](#interior-portal-windows-and-complements-2026-09-30).

Exterior group-order batch: depth-sorted exterior seeds use buckets 0–63 within
each placement, with source append order inside each bucket; updated-transform
placements retain source order. Transformed MOGI bounds, horizontal depth,
conversion boundaries and cache/blocker consequences have independent coverage.
Full smoke exits 0 with **585 tests** (66 Render, 328 DX11, 191 Avalonia),
including 20 new cases. See [the compact entry](#exterior-group-depth-order-2026-09-30).
Scene-wide ordering was completed in the following batch; native occlusion remains open.

Scene-wide exterior batch: CPU preparation now uses one placement/group source
queue, the base-frustum AABB, native no-viewer rebucketing/cutoff and live
unbucketed overlap gates. Interior/exterior visits share placement-cache stamps;
always-draw callbacks bypass stamp switching. Secondary visible callbacks survive
the primary window reset, and exterior polygons collect globally before GPU
asset batching. Full smoke exits 0 with **613 tests** (66 Render, 356 DX11,
191 Avalonia), including 28 new cases. See
[the compact entry](#scene-wide-exterior-queues-and-placement-cache-2026-09-30).
Streaming activation, native clip buffers and matched pixels remain open.

Static CPU occluder batch: the 62 map-specific source polygons produce normalized
side/cap volumes after interior preparation. Exterior bucket spheres and offset
world portal polygons consume them, preserving updated-placement and cached
interior bypasses. Full smoke exits 0 with **639 tests** (66 Render, 382 DX11,
191 Avalonia), including 26 new cases. See
[the compact entry and scoped function list](#static-cpu-occluder-volumes-and-portal-early-out-2026-09-30).
At that batch seven contracts in the bounded list remained incomplete; streaming,
shadow-path integration and capture validation also remain open.

Terrain clip-buffer batch: loaded terrain feeds the 384-column horizon after
each depth band's WMO tests. Intact chunks defer to their far-corner band and
honor distance windows; hole chunks erase unprotected columns. Exterior WMO
boxes retain the stored clip-Z gate, column boundaries and unbucketed bypass.
Full smoke exits 0 with **674 tests** (66 Render, 417 DX11, 191 Avalonia),
including 35 new cases. See [the compact entry](#terrain-clip-buffer-and-depth-band-feed-2026-09-30).
Five contracts remain incomplete in the original scoped list; world-horizon
sources and terrain/streaming adaptation remain outside that list.

Static WMO doodad lighting batch: MODR and MODS ownership gate 12340 spawning;
MOGI group references select the baked MODD indoor bank or sunlight. Shared
exterior references persist across group linking. Per-instance M2 inputs and
native lit gates are connected, including neutral sky/other-client streams.
Full smoke exits 0 with **698 tests** (66 Render, 441 DX11, 191 Avalonia),
including 24 new cases and a live-shader WARP pixel fixture. See
[the compact entry](#static-wmo-doodad-ownership-and-baked-lighting-2026-10-01).
Portal frusta, per-doodad fog and matched captures remain open.

1. **Finish all portal consumers.** For the current doodad focus, retain callback
   frusta, recover sphere acceptance and first-admission/shared-reference order,
   then carry staged/current fog to the instance stream. Accept independent
   off-window, two-window union, sphere-edge, shared-owner and frame-reset cases.
   Recover protected world-horizon line sources
   at `0x7938BC` -> `0x7CC880`/`0x78F900` and native terrain combined bounds/
   availability, then callback/doodad consumers and GPU volume submission/state.
   Finish post-cull work at `0x79A242`, native activation/streaming
   lifetimes and the callback's group/liquid/frustum
   consumers. Acceptance:
   independent horizon endpoint/enlistment and protected-line/hole ordering fixtures, preserving
   verified queue order, no-viewer cutoff, interleaved caches and depth-sorted
   exceptions; matching queues and facade/sky/depth pixels for both directions and
   camera crossings, including single-owner, on-edge and cycle cases.
2. **Complete global scene preparation.** Apply exterior rectangles/distances
   and the +33.333332 handoff to terrain, M2, other WMOs, doodads and liquids;
   retain depth-sorted/unbucketed exceptions. Recover native group availability,
   WDT bounds, transform arithmetic and terrain fraction handoff. Acceptance:
   compare scene-node/batch lists with client captures without conservative
   full-frustum fallbacks in Client mode.
3. **Close liquid, lighting and frame contracts.** Recover viewer-liquid/plane
   selection, blend-sky, entity MOCV, animation/eligibility/bounds; extract M2
   submission and wire the common element queues/conditional frame graph.
   Acceptance: independent boundary/order fixtures, state restoration and
   liquid/interior/exterior captures.
4. **Finish modes and all rendering workstreams.** Close CVars, streaming,
   LOD/fades and explicit whole-map Editor demand, then every R00–R13 CPU path
   and its cached DX9 SM3 shader/selector/constant/state contract. Acceptance:
   settings boundaries, mode switching, other-client regressions and full
   rendering reachability/indirect-call closure.
5. **Sign-off evidence.** Pin the original executable hash and capture matched
   CPU/GPU state and whole frames. Passing tests or exporting every function
   alone does not satisfy this gate.

The [plan roadmap](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap) owns the
implementation order; this list retains CPU acceptance details. After each batch,
record the next action in the compact entry and update roadmap items whose status
or priority changed. Preserve outstanding CPU and shader work together.

## Saved evidence and coverage

[cpu-evidence.json](cpu-evidence.json) indexes **93 complete Hex-Rays exports**
(**11,201 reported pseudocode lines**). The full outputs are cached in
[artifacts/client-335-shaders/ida/index.md](../../../artifacts/client-335-shaders/ida/index.md).
Forty-four functions also have complete instruction exports, totaling 5,918
instructions: BSP/face queries, placement/list/transform handling, viewer
registries, portal seeding and the scene/flag/callback consumers. Reuse relevant
slices when helpful; refresh stale evidence only when the active claim needs it.
Complete exports and inventory revalidation are not startup requirements.

The bounded scene audit contains:

| Evidence | Coverage | Limit |
| --- | --- | --- |
| [scene-direct-calls.csv](scene-direct-calls.csv) | 71 roots, 418 direct relationships, 308 distinct callees. | Relationship order is not execution order; recursively review targets and conditional reachability. |
| [scene-call-sites.csv](scene-call-sites.csv) | 603 call instructions in those roots; all instruction-query pages completed without truncation. | Thirteen sites still need complete indirect/global-pointer target closure. |
| [ida-functions.csv](ida-functions.csv) | All 27,280 functions, with cached functions and direct scene dependencies marked. | The remaining rendering graph, jobs, callbacks, vtables, and data-driven selectors remain open. |

The evidence manifest retains thirteen unresolved sites. In particular, the
terrain render-layer function pointer at 0x798A89 and lighting/device calls in
WMO, detail, terrain, and frame submission require target recovery.
A bounded multi-root callgraph response omitted requested roots without
reporting truncation during discovery. It is not accepted as closure evidence;
the saved tables use per-function callee requests and independent instruction
queries. Direct-target completeness also needs xref/jump-table and thunk review.

"Pseudocode exported", "instructions inspected", "partial port", and
"capture verified" are separate states. IDB comments, existing names, casts,
and inferred structures need checking against instructions. Complete cached
pseudocode does not mean the function is semantically complete.

**All pre-existing IDA function names were guessed.** The manifests now mark
them `unverified-IDB-hypothesis` independently of analysis/port status. Labels
throughout this dossier and the cached exports are navigation hints. The
[semantic claims ledger](cpu-semantic-claims.csv) records address-based findings,
supporting instruction sites, port consumers, and remaining uncertainty.
Do not infer a parameter, structure, or algorithm from a symbol name; do not
use name matching to exclude a path from the full rendering closure.

## Reference frame preparation and submission

The first-class CPU scope includes scene construction and loading, camera
transforms, visibility, animation and geometry, per-instance lighting, draw
queues, resource lifetime, state submission, and presentation.

### Scene preparation

CWorldScene__Update (0x795400) records the camera and target, builds offset
clip planes and 64 depth buckets, captures the projection/view/viewport,
constructs world-space frustum corners and planes, and updates attached scene
objects using the async clock. Matrix conventions, camera-relative transforms,
bucket boundaries, clipping, and update callbacks must be translated together.

CWorldScene__Render (0x79A870) performs these stages before the outer frame
finishes drawing:

1. Rebucket pending WMO render nodes and update the current viewer liquid.
2. Rebuild the active frustum, object-fade state, and terrain/WMO/detail pools.
   Clear the clip buffer (384 entries) and shadow clip volumes.
3. Bucket WMO segments, prepare primary/secondary interior portal views, and
   constrain the exterior view. Recycle unavailable exterior nodes.
4. Cull exterior buckets and interior groups; prepare model animation state.
   CullSortTable (0x79A790) visits terrain, WMO groups, liquids, entities,
   doodads, their updated lists, and the horizon in each of 64 bands.
   Interior doodads/entities use group flags and the propagated interior mask.
5. Choose the clear color from the portal/environment/liquid state. Draw
   registered scene children and construct the camera plane.
6. Advance M2 time with int(float(deltaSeconds * 1000)), then animate models,
   resolve instance lighting, compose batch alpha, build elements and sort
   their pass lists. The reference rounds/truncates at explicit stores.
7. Update missile targeting and shadow preparation, then projection/shadow/fog
   state. Submit terrain, WMO groups, and special projected WMO groups.
8. Draw portal view volumes and the sky when the exterior portal view exists
   and the current viewer liquid ID is zero.
9. Update model bounds/cull flags, refresh procedural liquid textures, build
   cached liquid instances, and flush liquid list 0. Finish weather bounds and
   optional buffered transparent geometry.

The scene follows dependencies between these stages. A replacement pass order
must retain the state and data writers used by later stages.

### Outer frame order and liquid partitions

CGWorldFrame__OnWorldRender (0x4F8EA0) starts FFX, sets the scene viewport,
submits the scene and decals/nameplate work, then performs:

| Condition | Ordered calls after scene submission |
| --- | --- |
| Common prefix | CM2Scene__Draw(0); cursor projection; Gxu list-0 sort/flush; detail doodads. |
| Current viewer liquid ID == 0 | CM2Scene__Draw(2); fog/liquid-list-1/ripples; weather; world barriers; CM2Scene__Draw(1). |
| Current viewer liquid ID != 0 | World barriers; CM2Scene__Draw(1); weather; fog/liquid-list-1/ripples; CM2Scene__Draw(2). |
| Common suffix | Cursor projection; spell visuals; missile trajectory; pending callbacks; liquid particulate; Gxu list-1 sort/flush; textured viewports/glare; viewport/state restoration; FFX. |

Liquid__GetUnderwaterDarkenInfo (0x780620) returns the **liquid type ID**.
The branch does not test whether the reported surface depth is positive.
WorldRender__ApplyFogLightsAndWaterRipples (0x790A80) applies fog, conditionally
flushes liquid draw list 1, and renders ripples.

CM2Scene__Animate (0x821A20) creates three arrays of element indices:

- Array 0 receives opaque geometry, subject to composed alpha and eligibility.
  Compatible doodads are grouped separately; singleton groups return to array 0.
- Arrays 1 and 2 receive transparent elements according to instance lighting
  selection flags 0x20/0x40 and transformed bounds against the liquid plane.
  A crossing model can enter both lists with clipping. Projected elements have
  an additional routing branch. Particle/ribbon/callback routing is also audited.
- Both transparent arrays use CM2Scene__SortTransparent (0x81EF30).
  CM2SceneRender__Draw (0x823130) walks each sorted list directly and dispatches
  mesh, projected mesh, doodad run, ribbon, particle run, and callback entries.

The editor currently groups meshes by asset/animation, draws opaque meshes,
water, translucent meshes, ribbons, then particles. It has no equivalent
viewer-liquid branch or complete interleaved transparent element queue.
Changing a single water call cannot close this gap.

### Animation, alpha, bounds, and sorting

UpdateModelAnimationStates (0x793450) uses node flags, distance squared
1111.1111, SetAnimating, and callbacks. UpdateModelCullAndBounds (0x793980)
handles loaded/drawable state, split-body frustum flags, texture-load queueing,
sequence bounds, projected texture bounds, and unit callbacks.

CM2Scene__Animate composes master alpha, animated color alpha, and the selected
texture-weight alpha before enqueueing. The invisible threshold has bits
0x38D1B717; opaque/alpha-key geometry becomes transparent below threshold
0x3F7FFF58 (approximately 0.99999). Exact shader ID 0x8000 and a null resolved
effect independently suppress a geometry batch. These gates affect CPU queue
membership as well as the shader output.

SortTransparent compares element field +16 descending, flags(+8)&1 descending,
signed priority plane at +36 ascending, and field +20 descending. Optional
shader-key grouping under cache flag 0x4000, model identity, element type,
material-layer ordering, and the opaque-comparator fallback follow.
The distance fields' producers, pointer-based ties, heapsort behavior, and
additive-particle grouping still need independent fixtures and instruction checks.
An asset ID or asset-kind grouping is not evidence of equivalent queue order.

SetupMaterial (0x81FE90), SetupLighting (0x81FB10), and
ComputeElementShaders (0x81F1D0) are cached alongside these CPU writers.
They connect pass selection, composed alpha, material flags, local-light count,
fog banks, clip planes, blend state, alpha reference, and SM3 selector keys.
The port must map them to the cached BLS ordinals and constant registers.

## Ported CPU rule: WMO viewer BSP query

The prior editor camera query flattened the union of MOBR faces and intersected
them without walking MOBN. A face in an unvisited leaf could therefore choose
the viewer group and its interior lighting/fog.

The 12340 MPQ loader now copies wowlib's decoded MOBN/MOBR/MOPY data into
[WmoBspTree.cs](../../../WoWRenderLib/Structs/WmoBspTree.cs), retaining MOVI face
ordinals, duplicate references, and unsigned child indices (0xFFFF absent).
No new raw BSP chunk parser is introduced.

[Wrath335WmoBspRaycaster.cs](../../../WoWRenderLib.DX11/Raycasting/Wrath335WmoBspRaycaster.cs)
consumes that data through
[WmoPortalVisibility.cs](../../../WoWRenderLib.DX11/Renderer/WmoPortalVisibility.cs).
The new path applies only to MPQ 3.3.5.12340. Other supported clients retain
their existing triangle-query route.

Recovered and implemented behavior:

| Rule | Reference |
| --- | --- |
| MOBN leaf bit 4; split axis bits 0..1; unsigned children; root MOGP clip bounds. | BspWalkRay 0x7CA180; GetTris 0x7CB0C0. |
| Asymmetric clip-bound rejection and 0.01 split-plane tolerance. An endpoint on the plane visits positive then negative; a crossing visits the near child first. | BspWalkRay, complete instruction export. |
| Camera mask 0x80; collision/detail bits alone do not exclude a face. Eligible unique faces are marked before intersection. | InitRayQuery 0x7C78E0; TestRayFace_FlagGated 0x7C6C30. |
| Per-query cap of 8192 eligible unique faces; duplicate suppression; last equal-distance hit replaces the previous hit. | TestRayFace_FlagGated; LeafBboxRayCull 0x7C6D50. |
| Default bspcache CVar is 1; leaves above 300 faces or 450 distinct vertex indices fall back to raw face tests. | WorldParam Initialize 0x78E400; BuildRayCullBspLeafTriCache 0x79AE80; RayCullBspLeaves 0x79B1F0. |
| Cached-leaf rejection uses the original full segment's AABB expanded by 0.01. The AND of all three vertex outcodes rejects a triangle only after its visited stamp/count is recorded. | LeafBboxRayCull. |
| Two-sided triangle test, strict determinant interval (-1e-6,1e-6), barycentric edge tolerance 0.002, nonnegative distance, and inclusive current maximum. | NTempest__Intersect 0x983490; raw/cached face tests. |

Leaf eligibility is computed during preparation. Query stamps and traversal
storage are reused across frames and groups without mutating shared MOPY data.
The implementation preserves face order and rejection behavior; it does not
replicate the client's cache hash/LRU allocation policy.

Fifteen new behavioral cases cover reached leaves, near/on-plane order,
asymmetric clipping, equal hits, duplicate suppression and scratch reuse,
face flags, the 8192 cap, transformed distance and exact maximum, malformed
references/cycles, decoded span ownership and unsigned children, digest
vertex/face limits, outcode rejection, and modern routing.

This is a **partial CPU port**, pending numerical and matched-camera captures.
The client uses x87 arithmetic with explicit float stores; System.Numerics
float operations do not establish identical boundary rounding. Also audit:

- Original native load/unload/insertion timing and available group-list order
  versus scene insertion and prepared arrays; WDT-global bounds and native
  broad-phase, transform, scale and terrain handoff arithmetic.
- The runtime skip-bit writer, global portal-view/sky consumers and propagated
  group lighting modes. Runtime +0x0C flags are distinct from MODF file flags.
- BSP MOCV lighting queries for entities/doodads; the camera query does not
  implement them. Other query masks and AABB face-collection paths remain open.
- Cache-disabled captures, original visitation cleanup and overflow behavior,
  numerical limits, and resource-failure fallback.

## Per-placement viewer caller port (2026-09-30)

[Wrath335WmoViewerQuery.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335WmoViewerQuery.cs)
now owns the build-scoped per-placement query. The shared loader supplies an
explicit MPQ 3.3.5.12340 profile flag instead of inferring this behavior from
legacy lighting. Shader/material interpretation is shared between modes;
Client mode enables portal culling and Editor mode retains the custom toggle.

The query narrows geometry by its exact inclusive current extent. It does not
add a portal tolerance to triangle tests. Every equal geometry hit replaces
the previous group in prepared traversal order. MOGP exterior bit 8 discards
the result; exterior-lighting bit 0x40 alone keeps the viewer indoors. One
winning geometry group seeds traversal, rather than every near-equal group.

The cached `CMapObj::VectorIntersectPortal` (0x7AF280, 184 lines) and its
plane/polygon helpers establish the portal contract:

- Loaded source groups use the original full segment for MOGI broad phases.
  Portal extent starts at **1.05** independently of the geometry/terrain cap.
- Plane tests use a normalized local direction, parallel tolerance 0.0001,
  and signed plane proximity strictly below 0.1; a nearby plane returns zero.
- Inclusive portal distance replaces the prior portal on a tie. Near/far
  attribution uses signed plane distance and MOPR side, including zero.
- The caller accepts a portal only when `portalFraction - bestFraction <
  0.0001f`. This is a strict comparison in normalized 1760-unit segment units,
  allowing about 0.176 world units beyond geometry. Placement scale does not
  change that world-space tolerance.
- Major-axis ties choose Z, then Y. The table at 0xB2D6F4 projects X to (Y,Z),
  Y to (Z,X), and Z to (X,Y). Point-in-polygon uses multiplication and `<=`
  parity tests with asymmetric boundary inclusion, instead of the editor's
  generic division test.
- A portal near side marked MOGI exterior bit 8 discards the placement result;
  its far group cannot then seed an interior traversal. An exterior far side
  otherwise suppresses only the secondary group.

The five new complete exports include the plane/polygon/major-axis helpers
and `World::ValidateFarClip` (0x780770). The saved axis bytes and call tables
are linked from the ignored CPU index. Six new cases cover equal/exterior
geometry, exact caps, portal fraction boundaries, placement scale, polygon
boundaries, winning-group seeding and the older query regression.

The scene-level pool port below consumes these results for camera-WMO/fog
selection. Original source group availability/order, broad-phase arithmetic,
terrain fraction handoff and x87 rounding still need closure.
The portal sphere mode (`a5 != 0`) belongs to other callers and remains open.
Entity/doodad BSP lighting and propagated rendering light banks remain separate
CPU work; this query does not implement them.

## Scene placement pools and name verification (2026-09-30)

The new instruction check at **0x7D59B0** confirms reads of runtime flags at
placement +0x0C, skip mask 0x20, and independent running extents selected by
bit 0x400. It does not read MODF flags at file-record +0x38.
The file constructor **0x7BF460** clears runtime +0x0C at 0x7BF4FC and uses
stored MODF extents, remapping minimum to `(-max.Z, -max.X, min.Y)` and maximum
to `(-min.Z, -min.X, max.Y)`, then adding the placement offset. It reads the
doodad/name sets, but neither copies MODF flags nor reads its scale word.
The renderer's native static transform/scale arithmetic therefore remains open.

**0x7B67B0** copies translation and forward/inverse matrices, then calls
**0x7B64F0**, which sets runtime bit 0x400 at 0x7B64FC and rebuilds bounds.
That proves a transform-update path into the second query pool. It does not
justify assigning a broad meaning to every producer of that bit. The apparent
0x20 writer **0x7B6F60** modifies child doodad records, rather than the root WMO
placement; the root skip-bit writer remains unresolved.

**0x6DED60**, labelled `TSList__LinkNodeToHeadByOffset`, unlinks an existing
node then appends it after the list's last link, updating that last-link field.
**0x7BEAE0** invokes it for both the hash bucket and the all-placement list;
**0x7D59B0** walks forward from the first entry. The misleading label is retained
as unverified evidence rather than silently made authoritative.

[WmoSceneViewerQuery.cs](../../../WoWRenderLib.DX11/Renderer/WmoSceneViewerQuery.cs)
extracts scene viewer selection from SceneManager. Its 12340 path traverses
`SceneObjects` in insertion order, independently of GPU asset/group buckets.
[Wrath335ViewerPlacementSelection.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335ViewerPlacementSelection.cs)
holds both running caps and placement/group results:

- An accepted equal hit replaces the prior placement. A query without a hit
  leaves the previous result intact.
- An exterior hit clears its pool's placement while retaining the new cap;
  a later farther interior cannot replace it. A later equal interior can.
- Normal and updated-transform pools narrow independently. A normal winner
  takes priority regardless of the updated winner's distance. The updated
  winner becomes primary only when the normal pool is empty; otherwise it is
  returned as a separate secondary placement with its two group results.
- A portal can slightly increase the current pool cap, including beyond 1.0
  segment fraction. Following queries retain that cap while using the original
  segment for broad phases and digest rejection.

[WmoViewerPlacementState.cs](../../../WoWRenderLib.DX11/Renderer/WmoViewerPlacementState.cs)
separates runtime state from `PlacementFlags`. File placement initialization
clears runtime flags after object-initializer transform writes. ADT loading
retains remapped MODF bounds only for MPQ 3.3.5.12340; a later live transform
sets 0x400 and discards stored bounds in favor of rebuilt root bounds.
WDT-global placements currently use rebuilt root bounds pending recovery of
their separate constructor path. The other legacy client query remains unchanged.

Seventeen new behavioral cases cover both pools, equality/exterior/miss order,
secondary promotion, portal-raised caps, transformed placements, independent
file flags, stored-bound invalidation, lazy terrain queries and the other-client
regression. CPU scene fixtures seed decoded models without a GPU or asset I/O.
Eleven new complete pseudocode exports and 64 additional call instructions
extend the cache without repeating shader extraction.

The following batch connects those results to WMO traversal. This remains a
**partial CPU port**: streaming list timing, prepared group availability/order,
WDT bounds, exact transforms/scale, terrain handoff, x87 stores, executable hash
and matched captures remain open.

## Shared viewer groups in portal culling (2026-09-30)

The complete instruction export of **0x795D40** verifies registration of both
group pairs. **0x792FC0** deduplicates each registry. **0x7AD1F0** consumes it,
loads each selected group, checks MOGP +0x30 mask **0x48** for strict-interior
state, then traverses the registered group regardless of that mask. The query
can select an exterior-lit WMO while the strict-interior marker remains false.
The flag reader **0x7AE7B0** uses the separate root +0x130 table with 32-byte
stride, confirming that MOGI and loaded MOGP flags cannot be substituted.

**0x7B3B20**, labelled `CWorldScene_RenderMapObjLiquids`, installs a callback
and forwards the placement's matrices, camera data and group registry to the
portal seed pass. Its guessed name does not describe the complete role.
**0x79A870** seeds secondary first, resets accumulated portal/sky view state,
then seeds primary. With no primary it establishes full-screen exterior/sky
views. The following batch ports the view reset/union policy and sky consumer;
other global view consumers remain open.

The editor now selects scene viewers once before the sky/fog setup whenever
portal culling needs them, including scenes without environment fog data.
Fog consumes the same primary placement. WMO portal culling consumes the
primary and secondary placements' group pairs through an internal overload
that preserves the public API. Other placements receive an explicit empty
interior seed instead of independently selecting a local floor. Duplicate
groups are visited once; loaded MOGP flags distinguish strict-interior state.
The override is scoped to MPQ 3.3.5.12340; other clients retain their query.

Eight new cases verify explicit/empty group pairs, deduplication, invalid
indices, avoiding another BSP walk, MOGI/MOGP differences, exterior-lit state,
secondary-placement consumption and the other-client regression.
Five additional complete function exports and 768 instruction lines extend
the saved evidence, with all call-query pages completed.

A new indirect callback at **0x7AD32F** reads storage **0xD1BED8**. Setter
**0x7A6B40** writes the supplied callback and context to 0xD1BED8/0xD1BEDC;
the scene seed adapter installs candidate **0x799310**. The manifest retains
this site as a closure frontier until the callback body, other callers and
recursive/reentrant writers are classified. The later
[exterior queue entry](#exterior-portal-render-view-queues-2026-09-30) establishes
the bounded ABI and both installations; full queue/helper closure and the
original ten unresolved sites remain open.

### Scene portal views and sky scissor (2026-09-30)

Fifteen newly cached complete functions add 1,100 reported pseudocode lines.
Thirteen additional complete instruction exports add 2,042 instructions,
including the already cached update function at **0x795400**. No BLS files
were extracted or disassembled again. Bounded claims V20-V28 record the
instruction sites and remaining uncertainty independently of guessed names.

The recovered data flow is:

- **0x795400** resets sky/exterior minima to `FLT_MAX`, maxima to `-FLT_MAX`
  and distances to **-1**. **0x79A870** discards secondary-produced views and
  any earlier sky seed before the primary pass. No primary gives full views.
- **0x795D40** reads both primary registry groups' MOGI flags. Mask **0x40140**
  seeds full sky at distance zero, without opening the exterior view.
- **0x7AC060** emits an interior-pass portal when destination MOGI intersects
  **0x50148**. Mask **0x10008** also opens exterior and stops that recursion.
  Sky-only destinations continue inward traversal.
- **0x7A8F20** deduplicates emission using cache bit 4 before projection. The
  local normal offset is **+0.01** for side <=0, **-0.01** for side >0.
  The emitted view uses the offset polygon, independently of the recursion
  parent rectangle. **0x7A70D0** measures every original portal vertex along
  the local view axis and starts its maximum at zero.
- **0x7905B0**, reached by wrappers **0x790AB0/0x790AD0**, unions rectangle
  bounds and the maximum distance separately for sky/exterior.
- **0x7F09B0** intersects the sky rectangle with the viewport and enables
  scissor. **0x6A38D0** proves the DX9 window Y flip and integer edge rules:
  low edges add 0.5, high edges add 1, then truncate and clamp. Its distinct
  texture-target orientation branch remains open.

[WmoScenePortalPreparation.cs](../../../WoWRenderLib.DX11/Renderer/WmoScenePortalPreparation.cs)
prepares the primary's group/batch/doodad masks and view unions before sky
submission. The later WMO loop reuses those masks and traversal counts.
[Wrath335PortalSceneViews.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalSceneViews.cs)
owns reusable frame state. Closed interiors clear to the current fog color;
[SkyRenderer.cs](../../../WoWRenderLib.DX11/Renderer/SkyRenderer.cs) clips all
sky subpasses with pre-created scissor rasterizers and restores unscissored
state. Invalid portal graphs retain full sky and the existing visibility path.
The exact 12340 profile and portal toggle gate this behavior; disabling portal
culling in Editor mode retains the ordinary sky. Other clients keep their path.

Seventeen new behavioral cases cover independent unions, root flag seeding,
secondary reset, portal emission/deduplication, MOGI selection, offset and
parent-independent projection, local distance, reusable masks/scratch, invalid
graphs, viewport boundaries and DX9 edge rounding.

Two newly retained indirect sites are **0x7AC0EA** (the same callback storage
0xD1BED8 consumed during recursion) and **0x6A39C1** (device vtable call).
The later exterior queue entry establishes callback 0x799310's bounded
writer/ABI contract. Full callback consumer closure and the DX9 slot
interpretation remain open; guessed types do not prove them.

This remains a partial port. Unselected placements retain conservative exterior
frustum culling. The primary can seed its exterior groups after an exterior
portal opens, but global view rectangles/distances do not yet constrain the
scene's terrain, standalone M2, other WMO placements, doodads or liquids.
Native depth-sorted versus unbucketed exceptions, clip-buffer occlusion and
portal complement lists remain. The following boundary batch recovers the native
cycle/back-edge policy and world clipping epsilon. Exact corner/plane-D/x87
rounding, WMO blend-sky models, viewer-liquid
suppression, sub-viewports, sky depth and exact frame order need recovery/captures.

## Stormwind directed portals and camera boundaries (2026-09-30)

The reusable local audit retained the extracted asset
`World/wmo/Azeroth/Buildings/Stormwind/Stormwind.wmo`, root SHA-256
`C94A0183F1046FC33B0E579C0863A1320C85229B24DD317DA5672C2AD5D7C6D6`.
There are **286 groups and 627 MOGP-owned references**, with no null destinations
in those records. Eleven portals have a single owner: portal 38 goes from
`cathstairs01` (257) to `Trade District` (93); portals 52–61 go from groups
282/283/284 to `city01` (92). Both destination groups have zero portal references.
The seven portals touching `bazaarfacade03` (186) and `cathy_facade01` (185)
are **paired** in this asset: 181, 182, 212, 229, 231, 235 and 239.
Do not manufacture reverse links or classify groups from their names.

The compact [Stormwind fixture](../../../WoWRenderLib.DX11.Tests/Fixtures/Stormwind335Portals.json)
retains the seven original plane/polygon records, their reference order and
owner/target/side, eleven single-owner references and the relevant MOGI/MOGP
headers. It is embedded in tests; smoke needs neither the client files nor IDA.
It tests isolated query/projection/topology contracts, not the entire city mesh
or the original renderer's final pixels. Raw asset audit output is reused from
`artifacts/client-335-shaders/ida/stormwind-portals.json` when unchanged.

Fourteen new complete exports and instruction files support these findings:

| Address evidence | Recovered contract and current integration |
| --- | --- |
| `0x7A7210`, `0x9830D0` | Strict -0.01 < plane distance < +0.01 plus the native major-axis polygon test sets eye-containment bit 2 and a full rectangle. Projection now shares the viewer's asymmetric included/excluded edge rule; the side test remains strict and directional. |
| `0x7A72A0`, `0x7912C0` | World planes have normalized normals. Classify distances above +0.0001 as inside, below -0.0001 as outside and the inclusive band as on-plane. Emit current vertices and intersections in current-to-next order; do not duplicate intersections at on-plane vertices. The client stores float distances while classification/interpolation use x87 intermediates. |
| `0x6BF370`, `0x6BF6D0`, `0x984240`, `0x983E70`, `0x6A9B40` | The internal projection uses -1..1 depth, converted for DX9. Corner construction and facet arguments put **top, bottom, left, right, far** first; near is sixth. **Correction to the earlier audit:** five-plane clipping omits near, not far. The DX11 port maps these to normalized world matrix planes; exact native corner-construction rounding remains a capture gate. |
| `0x7AC060`, `0x9CE7E0`, default bytes at `0xADFE40` | Walk each MOGP-owned MOPR range in order; null destination 0xFFFF skips before reading the portal index. There is no reciprocal-reference requirement or path-wide visited guard. Skip only the previous group, enter at depth ≤10, process depth-10 references and return before callbacks at depth 11. The default storage contains 10 and its complete data-xref set identifies the sync writer. |
| `0x7AEA80`, `0x7AEB10`, `0x7AC09D` | Loaded lookup checks root availability and per-group +0x198 bit 1. Loaded MOGP 0x10000 returns before callback; MOGI still supplies destination metadata before recursive loaded lookup. Emissions therefore do not depend on the target's loaded mask. The native viewer's group-info helper has no index check. The editor keeps null references out of viewer links; their native viewer contract is not established and they must not be treated as a proven exterior group. |
| `0x7A6B90`, `0x7AC2F6`–`0x7AC366`, `0x482870` | Bounds writes prove native CRect order is minY/minX/maxY/maxX. The repeated max-X clamp leaves max-Y unclamped, confirming the earlier quirk. Degeneracy rejects abs(a-b) **<0.001**, accepting equality. Both rules now have direct regressions. |
| `0x7AC3C0`–`0x7AC3EF`, `0x7B3A10` | Interior-pass destination mask 0x10008 emits and stops; exterior-pass destination mask 0x10008 stops without a direct callback. The selected placement's exterior seeds now use the emitted window. Global depth lists, distances and clip-buffer exceptions remain open. |
| `0x7A9200`, `0x7AC272`, `0x7AC416`, `0x7AC64F` | Outside-pass depth-zero back-facing links add a full blocker rectangle. Eligible front-facing links project an offset polygon once under cache bits 4/8; only local views disjoint from blockers are forwarded. **Queue port completed in the following batch**; scene-wide aggregation and GPU consumers remain open. The separate interior complement is ported below. |

[Wrath335PortalProjection.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalProjection.cs)
owns reusable polygon banks and world planes. No frame file access, shader
extraction or GPU-resource creation was added. Graph validation examines only
owned non-null references for the exact profile and retains directed ranges;
other clients keep their validation/projection behavior. Source normal/D are
preserved instead of independently renormalized. All native loader/plane writers,
camera-relative projection arithmetic and exact x87 rounding still need closure.

Twenty-four new behavioral cases cover the seven real facade polygons and
viewer pairs, eleven one-way records, directional/front-back traversal, the
included portal edge, five-plane clipping, world tolerance, 12-input cap,
depth 10/11, cycles/immediate back edges, null/unowned records, original planes,
loaded always-draw rejection, target metadata availability, exterior-window
seeding, rectangle quirks and exact degeneracy boundaries. Owned-range iteration
uses an integer counter so a ushort start of 65535 does not wrap at the first
increment; its boundary fixture verifies the complete final reference.

Local caching helps repeated evidence reuse, not every IDA operation. BLS warm
reuse avoids extraction/disassembly outright; CPU cached slices avoid repeated
decompilation and provide stable audit references. Reading giant local exports
or manifests still spends tokens. Live MCP remains the preferred source for
new addresses, xrefs, bytes and changed analysis; there is no measured proof of
a universal speed advantage for local files. No BLS was extracted this batch.

## Exterior portal render-view queues (2026-09-30)

**Rule and evidence (3.3.5.12340):** `0x7AC162`/`0x7AC272` reset local
candidate/blocker lists per exterior depth-zero seed and add `[0,0,1,1]` for
back-facing links before any projected-rectangle rejection. After parent
intersection/degeneracy checks, destination MOGI `0x10008` stops exterior
recursion; depth zero with destination `0x140` clear can emit (`0x7AC3EF`–
`0x7AC416`). `0x7A920D` rejects either cache bit 4 or 8; `0x7A936A` sets bit 8
even when the offset polygon clips to nothing. Bits persist across seeds and
reset with the placement/frame preparation. Offset failures OR clip bit 1;
eye-containment bit 2 still overrides that rejection. This pass is distinct
from the interior pass even after propagation clears at loaded MOGP `0x48`.

`0x7A92F3`–`0x7A933F` maps rectangle bounds to unit-viewport coordinates but
copies projected polygon vertices without that mapping. `0x7A87C7`–`0x7A87DC`
divides X/Y by clamped W while retaining undivided internal clip Z. The port
undoes DX11's depth mapping with `2*clip.Z-clip.W`, using the previously
established internal -1..1 projection contract. `0x7AC630` treats touching
rectangle edges as overlap, rejects the whole candidate on any blocker and
preserves accepted candidate order. `0x795D20` copies one 28-byte view into
the separate render-view array `0xCDD0F8`; it does **not** update sky/exterior
rectangle/distance unions.

**New focused live findings:** complete data xrefs for `0xD1BED8`/`0xD1BEDC`
identify only setter `0x7A6B40` as a writer, with two call sites. Instructions
at `0x7B3A1B`–`0x7B3A21` and `0x7B3B26`–`0x7B3B2C` install `0x799310`
and the placement pointer. The callback reads stack arguments group index and
placement (`0x799313`–`0x79934C`) and returns with plain `ret` (`0x799428`);
its first-visit branches pass the group node and globals `0xCDB080`/`0xCDB08C`
to `0x6DED60` (`0x799351`–`0x79938A`), and repeated visits call `0x790020`
with current frustum storage, then `0x6DED60` with group+0x6C
(`0x7993F9`–`0x79941E`). This establishes the bounded
callback ABI/installation contract, not complete queue/indirect-call closure.
The emission gate `0xCFBEBC` has only setup writer `0x7A70B3`, which stores 1,
and traversal reader `0x7AC3FB` in its complete data-xref set. Existing IDB
names, types and comments remain navigation hypotheses. New claim/xref deltas
await milestone reconciliation; the saved export inventories are unchanged.

**Port and verification:**
[Wrath335PortalRenderViews.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalRenderViews.cs)
owns reusable candidate/blocker/forwarded polygon storage;
[WmoPortalVisibility.cs](../../../WoWRenderLib.DX11/Renderer/WmoPortalVisibility.cs)
distinguishes traversal passes and applies the cache/mask rules.
[Wrath335PortalSceneViews.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalSceneViews.cs)
retains the primary placement's render list separately from its unions.
Twenty new behavioral cases cover directed empty/unavailable destinations,
root/loaded flag differences, bits 4/8, failed-offset/eye-containment behavior,
per-seed blocker reset, per-frame reset and retained polygon copies, recursion
depth, inclusive overlap/order, offsets/depth representation, both portal
edges, other-client isolation and front/back/front camera crossings through
real Stormwind portals 181/182. Full smoke exits **0**, **544 passed**
(66 Render, 287 DX11, 191 Avalonia); full output was captured in the temporary
`client-335-exterior-portals-smoke.log`. No BLS extraction or IDB edits occurred.

**Remaining uncertainty and next acceptance (updated after subsequent batches):**
forwarded polygons now collect scene-wide with native order/cache generations;
see [the scene-wide entry](#scene-wide-exterior-queues-and-placement-cache-2026-09-30).
The separate interior complement is now ported;
see [the next entry](#interior-portal-windows-and-complements-2026-09-30) for list
identity and the concrete collection trace. Require independent ordered group/
polygon fixtures for bucketed/unbucketed and interleaved placements.
The later [static-occluder batch](#static-cpu-occluder-volumes-and-portal-early-out-2026-09-30)
identifies `0xD2DCEC` as a volume count and ports its producers/early-out.
Terrain clip buffers, volume draw/state,
global exterior distance/bounds and callback group/liquid/frustum consumers
remain. Exact native projection/x87 arithmetic and matched facade/sky/depth
pixels remain capture gates. This batch ports list preparation, not complete
occlusion or final-frame parity; follow the [Roadmap](#roadmap).

## Interior portal windows and complements (2026-09-30)

**Rule and evidence (3.3.5.12340):** the previous handoff coupled two distinct
lists. Interior emission `0x7A8F20` rejects projected counts below three at
`0x7A8FD7`, maps bounds with `(NDC + 1) * 0.5` at `0x7A9019`–`0x7A9049`,
measures maximum local distance at `0x7A904C`, then calls `0x795D00` at
`0x7A9058`. Focused live instructions establish that this wrapper appends to
`0xCDD0E8`, with no retained polygon. A three-vertex projection may have
zero-area bounds and is still appended. Exterior wrapper `0x795D20` appends
polygons to `0xCDD0F8`; neither that list nor the sky union feeds subtraction.

Complete live instructions for `0x7968D0` confirm two 64-rectangle banks,
starting with the unit viewport and subtracting interior windows in emission
order. Touching edges are disjoint (`0x7969B6`–`0x7969F4`). Emit min-Y,
max-Y, min-X, max-X strips, trimming the working Y band before X
(`0x796A16`–`0x796B28`); there is no epsilon or merging. The guard is strictly
**greater than 60**: `0x79696B` stops before another window; `0x796B6F` checks
after all strips for one source rectangle and drops unprocessed source pieces.
Thus a partial result can survive with up to 64 records. Output distance is
zeroed at `0x796BF4`; the native function overwrites the window array.

Scene sites `0x79A99E`/`0x79AA11`/`0x79AA1C` traverse the secondary placement
then discard both lists before primary traversal at `0x79AA2C`. A full sky seed
is not an input window. `0x79AC4C`–`0x79AC56` invokes subtraction only when
sky distance is nonnegative and the interior list is nonempty; the helper alone
returns the full viewport for empty input. Native volume calls use rectangle
mode 1 at `0x79AC62`, then exterior polygon mode 0 at `0x79AC78`, after terrain/
WMO submission and before sky. Existing IDB labels, prototypes and comments
remain unverified hypotheses; these roles come from instructions and callers.

**Port and verification:**
[Wrath335PortalComplement.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalComplement.cs)
owns reusable banks and ordered zero-distance output.
[Wrath335PortalSceneViews.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335PortalSceneViews.cs)
retains source windows separately from the complement, exterior polygons and
NDC unions. Interior emission retains zero-area bounds after the projected-count
gate; [WmoScenePortalPreparation.cs](../../../WoWRenderLib.DX11/Renderer/WmoScenePortalPreparation.cs)
builds the primary complement and clears stale state on disabled/invalid/other-
client paths. Twenty-one new behavioral cases cover exact strip order, area and
window-order differences, overlap/disjoint/repeated/offscreen/touching windows,
tiny bands and zero-area emission, the 60/61 boundary and dropped pieces, list
identity, both viewer-group emissions and primary/secondary/reset integration.
Full smoke exits **0**, **565 passed** (66 Render, 308 DX11, 191 Avalonia),
zero failures/skips; full output is in temporary
`client-335-portal-complement-smoke.log`. No BLS extraction or IDB edits occurred.
List/subtraction/consumer claim deltas await milestone reconciliation against
the saved 2026-09-30 snapshot; archival inventories were not regenerated.

**Remaining uncertainty and next acceptance (updated after the following batch):**
the source writer and transformed MOGI bounds are now recovered and per-placement
bucket order is ported; see [the next entry](#exterior-group-depth-order-2026-09-30)
for the recovered source gates, and
[the scene-wide entry](#scene-wide-exterior-queues-and-placement-cache-2026-09-30)
for their implementation and remaining native clipping/streaming gaps.
`0x79A790` calls bucket consumer
`0x79A160` at `0x79A7FA` and advances its 108-byte bucket stride at `0x79A841`;
that consumer follows group links and seeds `0x7B3A10` at `0x79A235` with the
depth-sorted exception argument 0. The later unbucketed pass `0x799F80` seeds
at `0x79A0E8` with argument 1. These reader sites do not prove source queue
construction. Placement-cache switching is recovered in the next entry: interleaved
A/B/A seeds can invalidate emission caches differently from per-placement
batching. Implement scene-wide polygon collection only with independently
expected native group/seed/polygon order and cache behavior. Subsequent batches
port scene-wide collection and [static volumes/early-out](#static-cpu-occluder-volumes-and-portal-early-out-2026-09-30).
Terrain clip buffers, GPU volume state/frame order,
global exterior consumers and exact projection/x87/pixel captures remain open.
Follow the [Roadmap](#roadmap); CPU complement generation does not close occlusion.

## Exterior group depth order (2026-09-30)

**Rule and evidence (3.3.5.12340):** `0x7BDE9A`–`0x7BDFD1` creates placement
groups in ascending index order, appending their links. `0x7B6110` walks those
links and calls queue writer `0x792AD0` at `0x7B6412`. The caller requires
placement runtime bit 0x80, loaded group +0x198 bit 1, placement bit 0x20 clear,
and overlap with the camera-AOI box at `0xCD8F44` (`0x7B63E7`–`0x7B6404`).
The writer selects **MOGI 0x10008** (`0x792AED`); runtime bit **0x400** appends
to the unbucketed list (`0x792AFF`/`0x792B07`). Otherwise it appends to one of
64 depth buckets. `0x6DEDAC`–`0x6DEDBB` updates the tail and previous link,
preserving arrival order despite its guessed "Head" label. `0x79A790` consumes
buckets 0 through 63; this is bucket order, not a distance sort within each band.

`0x7BDF80`/`0x7BDF94` and moved writer `0x7B669A`/`0x7B66AB` transform root
MOGI AABBs into group-node +0x24 world bounds. `0x790650` selects each minimum
coordinate when target >= eye, maximum otherwise. `0x7954E1`–`0x79563E`
normalizes the full view direction, then XY only if its squared length exceeds
0.0001; the depth plane has zero Z. Nonpositive depth selects bucket 0.
`0x792B8E`–`0x792BA9` multiplies by float 0.03, stores a float, subtracts float
0.5 and uses FISTP; indices >=64 are omitted. Raw bytes at `0xA3F7EC` and
`0xADF454` establish those constants. Startup `0x406D86` passes 0x9001F with
full mask to `0x40C377`; rounding-bit conversion at `0x40C1AC`–`0x40C1D4`
supports nearest-even. The port retains staged-scale and integer-quotient ties;
later device/thread control-word changes and exact x87 arithmetic remain gates.

**Cache/source dependencies recovered:** frame setup `0x7AD020` resets current/
previous placement storage `0xD1C420`/`0xD1C41C`. Both interior wrapper
`0x7AD219`–`0x7AD238` and exterior wrapper `0x7AD372`–`0x7AD38B` compare them,
advance the global portal stamp on a placement change and remember that placement.
Same-placement interior/exterior seeds share bits 4/8; A/B/A revisits need a new
projection/emission generation. The no-primary path rebuckets unbucketed groups
at `0x792BD0` before culling; it **breaks** at the first index >=64 (`0x792D0A`),
leaving later nodes unprocessed, unlike the ordinary writer's per-node skip.
These dependencies were implemented in the following scene-wide batch; the
per-placement sort alone did not implement them. Existing IDB names/types/comments remain hypotheses.

**Port and verification:**
[Wrath335ExteriorGroupOrder.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335ExteriorGroupOrder.cs)
owns reusable seed storage and deterministic bucket/corner/plane calculations.
[WmoPortalVisibility.cs](../../../WoWRenderLib.DX11/Renderer/WmoPortalVisibility.cs)
uses its order for 12340 depth-sorted exterior traversal; scene preparation and
the render caller supply the world forward direction and preserve source order
for runtime 0x400 placements. Other clients retain their traversal. Twenty new
cases cover stable buckets, nearest versus center/distance, negative direction,
world transforms, root/loaded flags and availability, staged scale/ties/cutoff,
near-vertical pitch, unbucketed order and storage reuse. Integrated fixtures verify
shared-portal blocker/cache behavior, forwarded polygon order and client scoping.
Full smoke exits **0**, **585 passed** (66 Render, 328 DX11, 191 Avalonia),
zero failures/skips; log: `client-335-exterior-group-order-smoke.log` in the
temporary directory. No BLS extraction or IDB edits occurred. New source/bounds/
arithmetic/cache claim deltas await milestone reconciliation with the saved
2026-09-30 snapshot; archival export counts were not regenerated.

**Remaining uncertainty and next acceptance (updated after the following batch):**
AOI bounds, scene-wide arrival order, no-viewer rebucketing, live overlap and
shared cache generations are now ported; see
[the next entry](#scene-wide-exterior-queues-and-placement-cache-2026-09-30).
Native streaming/activation lifetimes, clip-buffer exceptions, global exterior
distance/view consumers, callback closure, GPU volume state and matched pixels
remain open; follow the [Roadmap](#roadmap).

## Scene-wide exterior queues and placement cache (2026-09-30)

**Rule and evidence (3.3.5.12340):** the source/order/cache arithmetic sites are
established in [the previous entry](#exterior-group-depth-order-2026-09-30).
Focused data xrefs for camera-AOI `0xCD8F44` identify writer `0x795963` and
reader `0x795A13` in `0x795400`, plus overlap argument `0x7B63FF` in `0x7B6110`.
`0x7958D8`/`0x7958DD` passes eight base world-frustum corners; `0x79595C`
constructs their AABB and `0x795963`–`0x79599F` copies its six floats. This is
a frustum-enclosing box, not a distance sphere. Root preparation sets runtime
0x80 at `0x7B5D13` and creates group links at `0x7B5DDD`. The port adapts
decoded root/groups and editor-enabled masks to availability; it does not claim
to reproduce native streaming or copy MODF flags into runtime 0x80/0x20.
A focused direct-mask writer search did not close runtime 0x20 lifetimes.

Unbucketed consumer `0x799F80` reads live visible-list head `0xCDB088`.
`0x79A050`–`0x79A099` tests candidate world bounds against each callback node,
accepting touching endpoints; it follows group+0xB4 at `0x79A09E`. Without an
overlap it requires exterior distance >=0 (`0x79A0BA`); accepted candidates still
pass full camera culling (`0x79A0D6`) before adapter `0x7B3A10` (`0x79A0E8`,
exception argument 1). Earlier accepted seeds extend that live list. Callback
`0x799310` resolves the node at `0x799320`–`0x799341` and appends it once to
`0xCDB080` at `0x799366`. Repeated visits update the frustum chain; its full
doodad/liquid consumers remain open. Secondary callbacks survive the window/
polygon reset before the primary interior pass. Selected always-draw tail
`0x7AD2EE`–`0x7AD32F` uses transformed MOGI bounds and direct callbacks.
Adapter `0x7B3A10` likewise bypasses the exterior wrapper for MOGI 0x10000,
so these callbacks do not switch the shared placement stamp. Existing IDB
names/types/comments remain hypotheses; instruction/dataflow evidence owns roles.

**Port and verification:**
[Wrath335SceneExteriorGroups.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335SceneExteriorGroups.cs)
owns reusable arrival/bucket/unbucketed storage, inverse-DX11-frustum AOI bounds,
no-primary rebucketing and inclusive live overlap policy. Ordinary >=64 depths
skip one node; rebucketing breaks at the first cutoff across remaining updated
placements and appends after ordinary arrivals inside each bucket.
[WmoScenePortalPreparation.cs](../../../WoWRenderLib.DX11/Renderer/WmoScenePortalPreparation.cs)
prepares all 12340 placements once, visits secondary then primary interiors,
collects exterior polygons globally and returns masks/reference counts to the
existing render loop. Pooled frame records and placement-owned buffers avoid
per-frame per-object allocation. [WmoPortalVisibility.cs](../../../WoWRenderLib.DX11/Renderer/WmoPortalVisibility.cs)
records first-visible world MOGI bounds and separates seed visits from initialization.
Cache switches clear projection/emission state while retaining visible masks and
global lists; consecutive placement visits deduplicate, A/B/A revisits re-emit.
Other clients and invalid graphs retain their ordinary fallback.

Twenty-eight new behavioral cases cover independent interleaved seed/polygon
orders, repeated assets, always-draw stamp bypass, interior/exterior bit 4 reuse,
camera AOI/transform/touching boundaries, prepared/runtime/loaded-root gates,
outside rebucketing/cutoff, live overlap chains and rejected arrival order,
full-camera updated exceptions, secondary list reset, doodads and frame/profile
reset. Full smoke exits **0**, **613 passed** (66 Render, 356 DX11, 191 Avalonia),
zero failures/skips; full log: `client-335-scene-exterior-smoke.log` in the temporary
directory. No BLS extraction, IDB edits or inventory refresh occurred. New AOI/
visible-list/activation claim deltas await reconciliation with the saved
2026-09-30 snapshot; archival counts remain unchanged.

**Remaining uncertainty and next acceptance:** static volume producers, sphere
rejection and polygon early-out are completed in
[the following batch](#static-cpu-occluder-volumes-and-portal-early-out-2026-09-30).
Terrain clip-buffer contracts of `0x79A160`/`0x7B3A10` remain a conservative gap.
Require independent clip-buffer producer/reset and accepted/rejected fixtures, preserving
depth-sorted/unbucketed exceptions and the verified global queue/cache behavior.
Native streaming/runtime activation, exact frustum/transform/x87 arithmetic,
callback frustum-chain/liquid consumers, global distance +33.333332 handoffs,
GPU volume state/frame order, original binary hash and matched facade/sky/depth
pixels remain open. Follow the [Roadmap](#roadmap); 613 passing tests do not close parity.

## Static CPU occluder volumes and portal early-out (2026-09-30)

**Rule and evidence (3.3.5.12340):** data xrefs and descriptor callers correct
the earlier clip-enable hypothesis. `0xD2DCEC` is the count of eight-byte volume
ranges in descriptor `0xD2DCE8`; `0xD2DCD8` holds 16-byte planes. `0x7CCDF0`
returns the count. Builder append `0x7CD832`, prepare resets `0x7CD860`/
`0x7CD86C` and frame clear `0x7CD917`/`0x7CD923` establish its lifecycle.
Scene clear `0x79A96F` precedes secondary/primary interior visits; construction
at `0x79A7A5` runs only with the exterior bucket pass, so closed interiors
retain empty volumes. Outside distance is zero (`0x79AAF3`); interior exterior
distance comes from `0xADF59C`, not the separate +33.333332 handoff.

The static table at `0xAF0040` contains **62 forty-byte records / 280 vertices**,
on maps 0, 571, 609, 600, 575, 603 and 631. Map 0 has one Stormwind wall;
the other 61 belong to Northrend/instance maps. Bounds initialization
`0x7CCD40`–`0x7CCDB9` expands the original zero bounds, retaining the origin.
Selection `0x7CD8B6` checks map equality; optional shadow-subset sites
`0x7CD8C1`/`0x7CD8C4` require source bit 2. Full six-plane camera bounds
test `0x7CD8D0` uses `0x9839E0`'s 0.019444443 tolerance. Source bit 1
disables exterior-distance clipping (`0x7CD8DE`–`0x7CD8EE`). Raw table,
polygon bytes, values and addresses are saved once in
[static-clip-occluders.json](static-clip-occluders.json); no original binary hash
is inferred from this archive. IDB names/types/prototypes remain hypotheses.

`0x7CD4E0` clips to the positive side beyond eye + full normalized camera
direction × exterior distance when distance is strictly >0.000001 and source
bit 1 is clear (`0x7CD55F`–`0x7CD621`). The ±0.0001 clip classification
retains coplanar vertices; fewer than three survivors stop construction.
Only this branch skips side cross-product squared lengths <=0.0001
(`0x7CD6CB`); ordinary facets normalize unconditionally (`0x7CD765`). The cap
uses the original first three points (`0x7CD79B`), and negative eye distance
flips the entire new plane range (`0x7CD7CC`–`0x7CD822`). Float-store/x87
sites `0x7CD67F`–`0x7CD707` and `0x7912F9`–`0x79136F` support staged
cross/normalization arithmetic; exact numerical equivalence remains a capture gate.

Sphere predicate `0x7CCE00` requires center dot plane + radius <=0 for every
plane of one volume. Polygon predicate `0x7CCFA0` requires every point <=0
for every plane of one volume; partial coverage by multiple volumes does not
combine, and neither predicate adds an epsilon. Bucket gate `0x79A221` reads
the MOGI sphere. Midpoint/radius writer `0x7AE6D2`–`0x7AE713` and constructor
`0x7BDF5D`/`0x7BDF6E` transform its center in place (`0x4C2353`–`0x4C235A`)
while retaining the local radius. Unbucketed `0x799F80` bypasses this sphere
gate; outdoors rebucketing turns updated nodes back into ordinary bucket visits.

World projector `0x7A85E0` tests offset/transformed world points at `0x7A86B2`,
before five-plane frustum clipping (`0x7A86F2`), and returns an empty polygon
when enclosed. Cache bypass bit 0x10 is initialized on first visit only at
`0x7AC210`–`0x7AC21A` when destination MOGI bit 8 and loaded owner MOGP bit 8
are both clear; MOGP 0x40 does not disable this bypass. Base projection and
both signed offset emissions retain that cached decision (`0x7A8FD1`,
`0x7A92A6`). The eye-on-polygon full-view shortcut remains before projection.

**Port and verification:**
[Wrath335StaticOccluders.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335StaticOccluders.cs)
retains the original map/flag/float sources; reusable
[Wrath335ClipVolumes.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335ClipVolumes.cs)
owns selection, construction, reset and predicates. Scene preparation receives
the current map ID, clears before interiors, builds before exterior visits and
gates bucket spheres. Portal projection consumes the shared mutable volume set;
placement cache generations preserve the first bypass decision. Other client
profiles and disabled portal culling drop the binding and volume state.
Twenty-six new cases cover winding/eye-side orientation, one-volume coverage,
inclusive contacts, depth/tiny-edge/coplanar boundaries, full camera pitch,
map/shadow subset selection, origin-expanded bounds, world offsets, source
radius, real Stormwind wall rejection, owner/root flag disagreement, updated
placement bypass and scene/profile resets. Full smoke exits **0**, **639 passed**
(66 Render, 382 DX11, 191 Avalonia), zero failures/skips. Full temporary log:
`client-335-static-clip-volumes-smoke.log`. No BLS extraction, IDB edits or bulk
inventory refresh occurred; bounded producer/predicate/cache claim deltas await
milestone reconciliation with the saved 2026-09-30 snapshot.

**Scoped function count:** this continuation began with thirteen unfinished
function contracts in the immediate WMO portal/occlusion producer-consumer slice.
The static batch ported six world-scene CPU contracts and left seven incomplete.
After [the terrain-buffer batch](#terrain-clip-buffer-and-depth-band-feed-2026-09-30),
eight have their CPU rules ported and **five remain incomplete**, including
partially ported functions and numerical/rejection-cache validation. This list excludes
generic math/container helpers, already-ported traversal/queue routines and
unresolved streaming/global-scene dependencies; it is not full call-graph closure.

| Build-12340 address | Instruction-supported role | Status after terrain-buffer batch |
| --- | --- | --- |
| `0x7CCD20` | Initialize static source bounds | World-scene rule ported |
| `0x7CD850` | Reset/select static map/frustum/subset sources | World-scene rule ported; shadow caller integration remains outside this slice |
| `0x7CD4E0` | Construct occluder side/cap planes and ranges | CPU rule ported; exact x87 captures pending |
| `0x7CD910` | Clear plane/volume ranges | World-scene rule ported |
| `0x7CCE00` | Reject a sphere enclosed by one volume | CPU rule ported |
| `0x7CCFA0` | Reject a polygon enclosed by one volume | CPU rule ported |
| `0x78FDC0` | Terrain clip-buffer rejection | CPU box rule ported; world-horizon/terrain source adaptation remains a separate gap |
| `0x79A160` | Consume depth buckets | Partial: sphere and terrain gates ported; post-cull doodad consumers remain |
| `0x7B3A10` | Adapt exterior group visibility | CPU visibility gates ported, including terrain/full-camera exception; exact native frustum captures pending |
| `0x799310` | Visible group callback and frustum chain | Partial: visible-list append ported; frustum/liquid consumers remain |
| `0x7998A0` | Enlist group doodad definitions after culling | Open: native acceptance/order and integration |
| `0x796C10` | Submit GPU portal view volumes/state | Open |
| `0x7A85E0` | World portal projection and CPU volume early-out | Partial: early-out ported; exact camera/transform/x87 and rejection-cache validation remain |

**Remaining uncertainty and next acceptance:** start with `0x78FDC0` and its
clip-buffer field writers; implement producer/reset and accepted/rejected lookup
fixtures, then connect both bucket/adapter call sites with their unbucketed
exception. This slice is now implemented in
[the terrain-buffer batch](#terrain-clip-buffer-and-depth-band-feed-2026-09-30).
Follow with remaining horizon sources, callback/doodad consumers and GPU volume state/frame order
in the [Roadmap](#roadmap). Native runtime 0x80/0x20/loaded-group lifetimes,
updated sphere field writers, shadow-path integration, exact six-plane camera
construction/transform/x87 arithmetic, global exterior-distance handoffs,
original executable hash and matched facade/sky/depth pixels remain open.
Passing this batch does not establish original HLSL or full rendering parity.

## Terrain clip buffer and depth-band feed (2026-09-30)

**Rule and evidence (3.3.5.12340):** reader `0x78FDC0` gates on world-enable
bit 0x20 and normalized direction Z within inclusive ±0.9 (`0x78FDF9`). It
transforms all eight AABB corners through `0xADF460`; argument bit 8 alone
bypasses stored clip-Z >=50 (`0x78FE9D`). Both WMO callers pass **1**, which
does not bypass this gate (`0x79A221`, `0x7B3A76`). X/Y divide by stored clip Z,
not homogeneous W. Reader bounds retain min/max X and maximum Y; column
conversion stages float(X×64), subtracts float 0.5 at `0xADF454`, then nearest-even
FISTP (`0x78FF06`–`0x78FF32`). Left offset is 192, right offset 193; partly
offscreen intervals clamp to 0–383, wholly offscreen intervals stay visible.
All covered columns must satisfy maximum Y <= horizon, including contact
(`0x78FF65`–`0x78FF78`). Unbucketed adapter argument a4=1 skips the terrain
gate; repeated bucket/adapter reads use the same bounds and immutable buffer.

Camera update `0x7955B9`–`0x79563E` constructs a horizontal direction;
`0x795B82`/`0x795BB2` builds its SG-compatible view and translation, then
multiplies by captured projection at `0x795BC7`. Basis writes in `0x6BFFBD`–
`0x6C0019` establish right/up/forward conventions. Frame clear zeros 384
protection bytes at `0x79A951` and fills 384 heights with **-1000000** at
`0x79A95B`–`0x79A96D`. Column data xrefs identify terrain producer `0x78F6A0`
and protected-line producer `0x78F900` as the only non-reset height writers.

Terrain edge selector `0x7CFB10` supplies nine consecutive vertices beginning
at 0 or 136 according to strict eye-X > target-X, then nine vertices at
0/8 +17×index according to strict eye-Y > target-Y (`0x7CFB2D`, `0x7CFB8C`).
The height/offset reconstruction is confirmed by `0x79053B`–`0x79055B`.
`0x78F6A0` uses the lower projected Y endpoint of each segment, without slope
interpolation. Intact segments require both clip depths >=float 0.027777778
(`0x78F824`–`0x78F840`) and raise every covered column by max. Any nonzero
MCNK low-resolution hole word switches to erasure: lower unprotected columns
to **-1000001**, without the segment-depth gate (`0x78F775`–`0x78F7F3`).
Protected lines `0x7CC880` -> `0x78F900` use two points, mark column flags 1
and raise the horizon (`0x78FA6A`–`0x78FA98`); later hole erasure preserves them.
Their source/enlistment is not yet integrated.

Ordering is part of the rule. `0x79A7F2` culls terrain, `0x79A7FA` tests WMOs,
and `0x79A836` updates horizon lists after other consumers in each of 64 bands.
Chunk source `0x7C3EDA` picks near vertices from [0,8,136,144]; insertion at
`0x792DE6` prepends to its band. Terrain cull `0x799DF5`/`0x799E18`/`0x799E70`
requires frustum, static sphere and existing horizon acceptance before enlisting
an edge update. Band 63 does not enlist producers (`0x799D65`). Intact chunks
defer to the opposite horizontal corner band (`0x799F13`–`0x799F54`), while
hole chunks update in their source band. Update `0x793798`–`0x793881` processes
intact chunks first, then holes; protected lines follow at `0x7938BC`. Intact
updates require strict lower < node+0x88 distance < upper, where lower is
exterior distance +33.333332 (`0x79AA50`) or -10000 outdoors (`0x79AB0B`),
and upper is farclip -33.333332 (`0x79A8EC`). Distance writer `0x7C3ECE`
uses the full-view-facing corner of node+0x4C; horizontal band selection is
separate. Existing IDB labels, types and precise helper names remain hypotheses.

**Port and verification:**
[Wrath335TerrainClipBuffer.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335TerrainClipBuffer.cs)
owns reusable height/protection arrays, camera construction, edge/line rules,
reset and box rejection. [Wrath335SceneTerrainOcclusion.cs](../../../WoWRenderLib.DX11/Renderer/Wrath335SceneTerrainOcclusion.cs)
queues loaded CPU terrain sources and defers updates by band; scene preparation
tests WMO buckets before ending each band, then visits unbucketed updated groups.
SceneManager only supplies the captured projection and farclip. The shared ADT
loader retains low-resolution hole masks and decoded chunk count through the
DX11 upload. Missing metadata/unloaded terrain contributes no occluder. The
feed uses existing scene-frustum checks, edited CPU geometry and transformed
chunk bounds; it adapts editor availability rather than claiming native streaming.
Other client/profile paths do not construct or consume the buffer.

Thirty-five new cases cover column rounding/padding/clamping, all-corner depth
and maximum-height rejection, contacts, inclusive pitch gates, lower endpoint
and producer depth, clip-Z division, flattened pitch, both row/column signs,
protected-line/hole erasure/reset, same-band and far-corner timing, culled
producers, fully holed chunks, far-distance windows, always-draw seeds,
updated outside rebucketing versus interior bypass, metadata/unload/profile
reset and offscreen sources. Full smoke exits **0**, **674 passed** (66 Render,
417 DX11, 191 Avalonia), zero failures/skips. Full temporary log:
`client-335-terrain-clip-buffer-smoke.log`. No BLS extraction, IDB edits or
bulk inventory refresh occurred; bounded terrain claim deltas await milestone
reconciliation with the saved 2026-09-30 snapshot. The original thirteen-contract
list now has **five incomplete contracts**, as recorded above; new source
dependencies are not silently included in that count.

**Remaining uncertainty and next acceptance:** trace the writers/enlistment of
the sort-table line list at offset 0x3C (`0xCD9084` in band zero) and endpoints
passed at `0x7938BC`, then connect protected world-horizon lines after hole
updates. Require independent endpoint/order fixtures where lines protect a
hole-erased column and change WMO rejection. Recover native node+0x4C and
node+0x8C bounds/sphere writers and streaming eligibility; the current editor
feed uses geometry chunk bounds for these roles, not proven combined native
bounds. Full terrain draw/doodad culling, horizon/world-enable mask binding,
callback frustum/liquid/doodad consumers, GPU volumes, exact frustum/transform/
x87 captures and the original binary hash remain open in the [Roadmap](#roadmap).
Smoke success does not close the matched facade/sky/depth pixel gate.

## Static WMO doodad ownership and baked lighting (2026-10-01)

**Rule and evidence (3.3.5.12340).** Live instruction/dataflow checks establish
these static mesh contracts; existing IDB names, prototypes and comments remain
unverified navigation hints, separate from this port's analysis status.

- `0x7BF76D` reads each group's MODR index; `0x7BF775`–`0x7BF7E9` resolves
  MODS membership and creates global/selected/extra-set instances. `0x7AEC70`–
  `0x7AECA3` skips empty ranges and returns `0xFFFFFFFF` when none owns the index.
  Unreferenced MODD definitions are not created by this path. Existing editor
  set masks already include set zero plus the placement's selected sets.
- `0x7BDFAE`–`0x7BDFD1` derives runtime group interior bit 2 from MOGI
  `flags & 0x48`. `0x7BF859`–`0x7BF87F` sets doodad bit 2 for an indoor owner
  only while runtime exterior bit 4 is clear; otherwise it clears 2, sets 4
  and writes sunlight scale 1 at `def+0x8C`. An exterior reference therefore
  persists if later indoor references link the same definition. The creation
  writes at `0x7BF005`–`0x7BF012` initialize runtime flags to 1/0x1001 using
  only raw MODD bit 1. Raw MODD bits 2/4 do not independently select this bank.
- `0x7BF092`–`0x7BF0AA` passes MODD BGRA to `0x7C1AD0`, with diffuse floor
  112 and ambient ceiling 96. Diffuse below that floor raises HSV value while
  keeping hue/saturation; black stays black. Ambient above the ceiling uses
  `roundEven(float(96*255/maxRGB)-0.5)` and `(channel*multiplier+255)>>8`.
  The HSV helpers are `0x984F60`/`0x985030`; `0x9851B5`–`0x9851F3` stages
  float RGB*255 then packs with FISTP. RGB, rather than MODD alpha, feeds light
  selection. Float/x87 numerical captures remain required for exact rounding.
- Static model creation installs `0x780CD0` at `0x7BF0FD`–`0x7BF107`.
  `0x831B4F`–`0x831B7E` initializes lighting, dispatches that callback and
  collapses sunlight. The callback dispatches vtable slots 1/2 at `0x780D5E`/
  `0x780D68`, not floor-query slot 3. Slot 1 is `0x7C1150`: interior RGB
  reads `def+0x84`/`+0x88` and direction bytes at `0xAEEDF0`
  (`BE9DCF03 BE9DCF03 BF666666`); exterior reads sunlight and `def+0x8C`.
  Slot 2 (`0x7C23F0`) supplies underwater state, still open.
- `0x835335`–`0x8353B7` collapses the single diffuse accumulator and adjusts
  ambient. `0x873D6E`–`0x873E69` binds capped diffuse to c10, ambient to c11
  and normalized incoming direction to c12. Cached SM3 `Diffuse_T1` ordinal 1
  [assembly](../../../artifacts/client-335-shaders/objects/d3dcompiler_47-dx9-assembly-v1/1ce68e31d5716ced5587e5d4a7891bf4749ef4ec02a494e9765e9ffbc48635b2.asm)
  (`1ce68e31…`) uses `dp3_sat(-c12, normal)` then saturated ambient+diffuse.
  `0x81FB22`–`0x81FB48` gates lighting by material bit 1 and the seven DWORDs
  at `0xA45374`: blends 0–4 lit, 5–6 unlit. This is a bounded baseline port,
  not full M2 shader/material or local-light closure.

**Port.** `Wrath335WmoDoodadLighting` caches the owner classification and color
split during WMO upload. Both spawning paths require MODR ownership for 12340;
the shared decoder retains absent MODS membership as invalid for that client.
`M2InstanceData` adds ambient/diffuse/direction to the existing instance stream.
The live M2 shader selects the baked bank per instance and respects the material
lit gate, while exterior instances retain sunlight and neutral streams retain
the existing standalone/other-client/sky path. The interior direction stays in
world space when the WMO/doodad rotates. Geometry, animation pose grouping and
draw telemetry retain their existing submission path.

**Verification.** Full smoke exit **0**, **698 tests**: 66 Render, 441 DX11,
191 Avalonia; 24 new cases. CPU fixtures cover MOGI/MOGP disagreement, raw MODD
flags, shared-owner arrival order, invalid/unreferenced MODR, dark/black/bright
color splits, integer scaling, alpha independence, material lit gates, spawning
and version fallback. A WARP fixture compiles the live M2 VS/PS and input layout,
renders baked and sunlit instances together, reads float target pixels, verifies
the unlit gate and neutral stream restoration. Full log:
`%TEMP%/wteditor-smoke-wmo-doodad-20261001.log`.

**Remaining / next acceptance.** Recover the callback frustum chain at
`0x79940A`–`0x799425` and sphere/fog consumers around `0x799C91`, including
first accepted owner, shared-reference deduplication and reset order. Test
off-window rejection, two-window union, sphere boundaries and per-frame fog
changes before connecting per-instance staged/current fog. Exterior bucket
enlistment, native availability/lifetimes, entity BSP/MOCV, underwater/dynamic
light contributions, shadow receiving and other M2 permutations remain open.
No matched client pixels or original executable hash were obtained; smoke/WARP
pixels do not establish client rendering parity.

## Client/Editor policy foundation (2026-09-30)

`UseClientRenderingRules` is persisted and exposed in settings and the viewport.
[WorldRenderingRules.cs](../../../WoWRenderLib.DX11/Renderer/WorldRenderingRules.cs)
resolves effective rules without overwriting editor distances or client CVars.
For MPQ 3.3.5.12340, Client mode applies near clip 0.2 and the recovered
map/override/physical-memory farclip validation, enables portal culling and
removes editor model-pixel and terrain-pixel LOD heuristics. Editor mode retains
its custom distances and visibility controls. The explicit profile gate keeps
Wrath rules away from other clients.

The [CVar frontier](render-cvars.csv) records the commented UI options and
ultra values without treating them as recovered runtime contracts. The main
plan defines separate mode acceptance tests and all remaining CPU/shader
setting consumers. Full Client-mode parity and explicit all-map Editor loading
are still open. Animation/effect heuristics, native LOD/fades, streaming,
editor overrides and frame/element ordering require further mode integration.

## CPU work still required in every rendering area

| Area | Required CPU recovery and integration |
| --- | --- |
| Device/frame | Caps and CVars; DX9-to-DX11 transforms and viewport rules; frame graph; state restoration; clipping/scissor; targets, MSAA, resolve and presentation. |
| Scene/visibility | Camera and frustum construction; 64-band buckets; fade/LOD and transition lists; exterior/interior/portal propagation; occlusion; horizon and doodad/entity selection. |
| Terrain | MCNK preparation, topology, holes, LOD stitching and camera-facing rows; layer/alpha maps; tangent/normal/color data; upload timing; shader/state selection and draw batching. |
| WMO | Groups and batch categories; transitions/colors; per-group and per-batch lighting; BSP entity queries; portal masks; doodad sets; material and texture-stage fallbacks. |
| M2 geometry | Skin/profile/section selection; material-layer resolution; special shader substitutions; batch eligibility; per-instance opacity and lights; opaque grouping and one transparent element queue. |
| Animation | Sequence/global-loop clocks; interpolation and packed values; bones, parent flags, billboard transforms; skinning and palettes; attachments and animated bounds. |
| Ribbons/particles | Emitter eligibility and lifecycle; random seeds/clocks; spawn rate and simulation; geometry/trails/UVs; bounds; shared queue insertion, grouping and callbacks. |
| Detail doodads | Density and placement seeds; camera-facing geometry; lighting/fog constants; fades; terrain ownership; reuse and submission. |
| Liquids | WMO/terrain surface queries; viewer ID/depth and update timing; list-0/list-1 preparation; material/setting banks; cached geometry, UV/color split, procedural refresh, ripple geometry and underwater particulate. |
| Shadows | Sun-direction remap; caster/receiver visibility; clip volumes; cascade transforms/snapping; cache dirty regions and reuse; atlas resource/state lifetime and incremental draw submissions. |
| Sky/weather | Portal and viewer-liquid suppression; dome/cloud/celestial geometry; procedural textures; skybox queues; glare queries; weather volumes, model callbacks and water crossings. |
| FFX/presentation | Target and capability selection; pass sequence and parameter clocks; downsampling/kernel geometry; resolve, viewport, gamma and final alpha. |
| Assets/lifecycle | Texture/geometry decoding; BLP metadata and mip/address policy; cache and streaming transitions; missing programs/assets; animation/resource invalidation and device-reset behavior. |

Priority after the remaining global viewer consumers: extend the build-scoped mode
policy to all native settings and add independent frame/element fixtures;
recover viewer-liquid and plane selection;
extract the existing M2 submission work into a dedicated renderer and implement
the common element queue; then close terrain/WMO/detail/shadow preparation and
their CPU-to-SM3 upload/selector contracts. Keep SceneManager an orchestrator
under [WoWRenderLib.DX11/AGENTS.md](../../../WoWRenderLib.DX11/AGENTS.md).

## Verification

The latest CPU code batch passed the full repository smoke runner with exit 0:
**66 Render + 417 DX11 + 191 Avalonia = 674 tests**, zero failures/skips,
including 35 new terrain-buffer/preparation cases. Earlier batches retain their
verification totals in their entries above.
Log: C:\Users\Titi\AppData\Local\Temp\client-335-terrain-clip-buffer-smoke.log.
This verifies the implementation and synthetic cases. Whole-client rendering
accuracy remains gated by the extended plan's complete closure and matched
CPU/GPU/frame captures.
