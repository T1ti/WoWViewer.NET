# 3.3.5.12340 CPU scene and submission audit

Updated 2026-09-30. This is the CPU dossier for
[CLIENT_335_RENDERING_ACCURACY_PLAN.md](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md).
The reference is the 32-bit 12340 IDB recorded in [snapshot.json](snapshot.json);
the target is the DX9 SM3 configuration. The original executable SHA-256
remains unpinned.

## Saved evidence and coverage

[cpu-evidence.json](cpu-evidence.json) indexes **48 complete Hex-Rays exports**
(**8,214 reported pseudocode lines**). The full outputs are cached in
[artifacts/client-335-shaders/ida/index.md](../../../artifacts/client-335-shaders/ida/index.md).
The BSP walker and raw face test also have complete instruction exports
(275 and 103 instructions). Reuse these files; refresh the affected function
only when the reference database, analysis, or build changes.

The bounded scene audit contains:

| Evidence | Coverage | Limit |
| --- | --- | --- |
| [scene-direct-calls.csv](scene-direct-calls.csv) | 26 roots, 271 direct relationships, 221 distinct callees. | Relationship order is not execution order; recursively review targets and conditional reachability. |
| [scene-call-sites.csv](scene-call-sites.csv) | 378 call instructions in those roots; all instruction-query pages completed without truncation. | Ten sites still need concrete indirect/global-pointer targets. |
| [ida-functions.csv](ida-functions.csv) | All 27,280 functions, with cached functions and direct scene dependencies marked. | The remaining rendering graph, jobs, callbacks, vtables, and data-driven selectors remain open. |

The evidence manifest retains the ten unresolved sites. In particular, the
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

- Full viewer selection across placements, root/MOGI broad-phase bounds and
  original group/list order; equal geometry hits can replace the current group.
- Native placement-list order and group-list order versus prepared array order;
  mixed placement pools and equal-distance exterior results across placements.
- Placement flags 0x20/0x400, primary/secondary fallback, exterior-result
  rejection, portal polygon behavior, and propagated group lighting modes.
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

This remains a partial caller port. The scene currently groups placements by
asset, lacks native slot-0/slot-1 selection for flags 0x20/0x400, and does not
discard an earlier placement winner when a later closer exterior placement
wins. Native list order, original source group availability, root/placement
broad-phase arithmetic, terrain fraction handoff and x87 rounding need closure.
The portal sphere mode (`a5 != 0`) belongs to other callers and remains open.
Entity/doodad BSP lighting and propagated rendering light banks remain separate
CPU work; this query does not implement them.

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

Priority after the remaining placement query: extend the build-scoped mode
policy to all native settings and add independent frame/element fixtures;
recover viewer-liquid and plane selection;
extract the existing M2 submission work into a dedicated renderer and implement
the common element queue; then close terrain/WMO/detail/shadow preparation and
their CPU-to-SM3 upload/selector contracts. Keep SceneManager an orchestrator
under [WoWRenderLib.DX11/AGENTS.md](../../../WoWRenderLib.DX11/AGENTS.md).

## Verification

The latest CPU code batch passed the full repository smoke runner with exit 0:
**50 core + 193 DX11 + 185 Avalonia = 428 tests**, zero skipped.
This includes 21 added CPU viewer/BSP cases and four mode/persistence cases;
the existing memory-limit case now also exercises its recovered low-memory branch.
Log: C:\Users\Titi\AppData\Local\Temp\wowviewer-smoke-cpu-viewer-309126ae0d6b4e40a6afef5b7aaea671.log.
This verifies the implementation and synthetic cases. Whole-client rendering
accuracy remains gated by the extended plan's complete closure and matched
CPU/GPU/frame captures.
