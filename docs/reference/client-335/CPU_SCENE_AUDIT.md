# 3.3.5.12340 CPU scene and submission audit

Updated 2026-09-30. This is the CPU dossier for
[CLIENT_335_RENDERING_ACCURACY_PLAN.md](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md).
The reference is the 32-bit 12340 IDB recorded in [snapshot.json](snapshot.json);
the target is the DX9 SM3 configuration. The original executable SHA-256
remains unpinned.

## Roadmap

Completed in the current batch: Stormwind directed-reference fixtures and
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

1. **Finish all portal consumers.** Port the depth-zero exterior blocker/view
   queues in `0x7AC060`, emission bit 8 in `0x7A9200`, forwarding `0x795D20`,
   complement generation `0x7968D0`, clip-volume early-out `0x7CCFA0`, terrain
   clip-buffer tests and portal-view volume submission/state. Close callback
   `0xD1BED8`, top-level emission gate and clip-enable writers. Acceptance:
   matching native queues and facade/sky/depth pixels for both directions and
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

Keep this list synchronized with the [plan roadmap](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap)
after every rendering batch; preserve outstanding CPU and shader work together.

## Saved evidence and coverage

[cpu-evidence.json](cpu-evidence.json) indexes **93 complete Hex-Rays exports**
(**11,201 reported pseudocode lines**). The full outputs are cached in
[artifacts/client-335-shaders/ida/index.md](../../../artifacts/client-335-shaders/ida/index.md).
Forty-four functions also have complete instruction exports, totaling 5,918
instructions: BSP/face queries, placement/list/transform handling, viewer
registries, portal seeding and the scene/flag/callback consumers. Reuse these
files; refresh the affected function
only when the reference database, analysis, or build changes.

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
recursive/reentrant writers are classified. The original ten unresolved sites
remain open.

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
Candidate callback 0x799310 and the DX9 slot interpretation still require
complete writer/ABI/body closure; guessed types do not prove them.

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
| `0x7A9200`, `0x7AC272`, `0x7AC416`, `0x7AC64F` | Outside-pass depth-zero back-facing links add a full blocker rectangle. Eligible front-facing links project an offset polygon once under cache bits 4/8; only local views disjoint from blockers are forwarded to the global list. This **unported** occlusion/view-volume stage is retained as the next roadmap item, not counted as a rendering fix. |

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
**52 core + 257 DX11 + 185 Avalonia = 494 tests**, zero skipped.
This includes 87 added CPU viewer/BSP/placement/seed/view/portal cases and four mode/persistence cases;
the existing memory-limit case now also exercises its recovered low-memory branch.
Log: C:\Users\Titi\AppData\Local\Temp\wowviewer-smoke-portal-boundaries-final-1790734046299.log.
This verifies the implementation and synthetic cases. Whole-client rendering
accuracy remains gated by the extended plan's complete closure and matched
CPU/GPU/frame captures.
