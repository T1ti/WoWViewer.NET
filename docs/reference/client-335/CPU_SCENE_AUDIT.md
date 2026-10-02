# 3.3.5.12340 CPU scene and submission audit

Updated 2026-10-02. This is the CPU dossier for
[CLIENT_335_RENDERING_ACCURACY_PLAN.md](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md).
The reference is the 32-bit 12340 IDB recorded in [snapshot.json](snapshot.json);
the target is the DX9 SM3 configuration. The original executable SHA-256
remains unpinned.

## Working loop

For a fresh thread, begin with the plan's
[handoff](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#start-here-in-a-new-thread)
and [working loop](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#working-loop).
Use this dossier by section/address, not as a required full read. The active
priority is the liquid replacement: basic water/magma inputs and equations are
connected, alongside material-flag lists, retained placement identities and a
decoded-grid viewer type/depth producer. Retained terrain/MODR entity queries,
scene-side history and mesh water interleaving/clipping are connected; full smoke
1,052, exit 0. Next: neighboring WMO links/native availability and shared effect
queues, then procedural water/ripples/underwater. See the
[entity/mesh batch](#entity-liquid-cache-scene-history-and-mesh-water-clipping-2026-10-02).

Earlier acceptance work is the opaque-presentation fix for the Client-only Stormwind
gray layer. Live settings and GPU readback identify transition alpha surviving
into the GUI image; Editor glow masked it. Verify the rebuilt viewport with
both modes/glow settings, then compare matched native lighting/fog pixels.
See the [presentation audit](#wmo-transition-alpha-and-opaque-gui-presentation-2026-10-01).
Exterior doodads now use native sphere depth buckets and spatial admission,
including terrain-sphere occlusion and retained portal-written fog banks.
Loaded WMO size categories, detail-scaled thresholds, CPU submission gates and
initial staged fog are ported. Default non-shadow GPU opacity, fade blend and
scaled cutout references and layered base-material queue selection are connected.
ZFill's controls, eligibility and duplicate state are recovered; native sorted
element queues and eligibility reseeding still block its live port. The complete
base comparator/heap and model/mesh distance rules are now CPU policies. Retained
CPU queues, particle/ribbon keys, additive regrouping and water routing are ported;
shared effect producers and interleaved GPU consumption remain next,
followed by eligibility lifetimes and shadow alpha variants. Authored WotLK MPQ
sort inputs and a caller-supplied transform adapter now have decoding and
behavioral coverage; see the [decoded-input batch](#m2-decoded-sort-metadata-and-transform-inputs-2026-10-01). See the
[retained-pose batch](#m2-full-pose-billboard-and-retained-sort-identities-2026-10-01)
for full CPU bones, live billboard/parent transforms and lifetime-owned identities.
Reference allocator order and CM2 attachment/runtime overrides remain open. The
[native mesh preparation batch](#m2-native-mesh-preparation-and-shader-selectors-2026-10-01)
adds root-only poses, mesh shader selectors and
resolved-table adaptation now compose into queue preparation; authored water
bounds are retained independently of expanded culling bounds. The former
section +0x0E "bone count" claim is corrected to bone-combo start.
See the
[layer/ZFill batch](#doodad-layered-material-partition-and-zfill-dependency-2026-10-01).
Mesh submission is now isolated in `M2MeshRenderer`; preparation/frame order
remains in SceneManager. See the [renderer batch](#m2-mesh-submission-renderer-2026-10-01).
See the [ordering policy batch](#m2-element-keys-comparator-and-heap-policy-2026-10-01)
for the recovered comparator and exact next adapter work.
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
The same day's callback-frustum, six-plane sphere, append-order, consumer
snapshot and per-instance fog claims are additional pending deltas. Exterior
bucket spatial consumers are now partially ported. The additional 2026-10-01
sphere-reader/bucket/pending/history, transformed-box category, threshold/CVar,
CPU alpha and initial fog writer claims await reconciliation; archival counts
are unchanged. The same day's opacity propagation, composed alpha partition,
blend/alpha-reference table, SM3 alpha formula and retained fade-group claims
also await reconciliation. Layer-offset/base-pass, depth-independent queue,
ZFill control/eligibility/clone/color-mask and comparator findings are further
pending deltas. ZFill/shadow ports and native availability remain open.
Transition color gates/endpoints, polygon projection/crossings, unified c28,
effect bindings, pass counts and opaque exterior fog findings from the same
date are additional pending deltas; existing IDB names remain navigation aids.
Strict-interior fog eligibility and both retained viewer groups are included
in that bounded delta; native loaded-target availability remains open.
Model-origin/parent sort keys, mesh radius/sign/primary-key branches, complete
base comparator/fallbacks, wrapped texture pointer differences and heap equality
behavior are additional 2026-10-01 pending deltas. Their CPU policies are tested;
live queue producers and GPU consumption remain open.
The decoded sort-metadata/transform-input port is an additional pending port
delta. The full-bone/view-writer, billboard normalization/parent-mode and retained
pose/identity adaptation findings from 2026-10-01 are additional pending deltas;
they do not close attachment/runtime override lifetimes or live sorted queues.
Particle emitter key callers, additive group/material/gate rules and per-type
water routing/plane transformation are further 2026-10-01 claim/port deltas;
see the [queue batch](#m2-particle-keys-additive-regrouping-and-water-queues-2026-10-01).
The native mesh preparation/selector, simple initializer, authored water-bound,
post-query entity lighting and particle force-below initializer findings are
additional pending deltas. The old section +0x0E bone-count claim is superseded
by the verified bone-combo-start field in the [preparation batch](#m2-native-mesh-preparation-and-shader-selectors-2026-10-01).
No export counts changed. Cached counts must not be used to
imply complete or current semantic coverage.

## Roadmap

**Active liquid batch:** native material 1/2 shaders, material-flag lists,
placement/generation identities, viewer and terrain/MODR entity queries,
scene-side history and mesh water interleaving/clipping are connected;
full smoke exits 0 with 1,052 tests (66 Render, 793 DX11, 193 Avalonia). The latest
batch adds 14 cases, including two WARP clipping/interleaving fixtures.
Neighboring WMO links/native availability, shared effect queues, material 3,
underwater and matched captures remain open. Prioritize the tasks
and acceptance checks in the [entity/mesh entry](#entity-liquid-cache-scene-history-and-mesh-water-clipping-2026-10-02)
and [rendering roadmap](../../CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

Focused IDB annotation batch: `0x7ECD00` now has verified distance argument
names, descriptive x87 temporaries, typed float globals and a named unsigned
initialization guard. Fresh decompilation and all three callers passed; the
database was saved. See [the compact review](#daynight-fog-rate-idb-annotations-2026-10-01)
for return-type uncertainty and ordered follow-up checks. This changes no
renderer behavior or parity status; inventory reconciliation is still pending.

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
At that batch, portal frusta and per-doodad fog remained open; the following
batch advances those consumers. Matched captures remain open.

Doodad portal/fog batch: callback frusta now feed sphere admission; unbucketed
and final group consumers retain their call-time state and append order. The
first accepting reference selects per-instance staged/current fog. Full smoke
exits 0 with **718 tests** (66 Render, 459 DX11, 193 Avalonia), including 18 new
cases and extended WARP fog pixels. See
[the compact entry](#doodad-portal-sphere-admission-and-instance-fog-2026-10-01).
Exterior bucket enlistment/terrain-sphere tests, persistent fog-bit history,
fade/size gates and matched captures remain open.

WMO transition audit: 12340 attenuation and portal fog distance share the
native major-axis crossing and near-plane projection rules. The last transition
endpoint, packed c28=127/255, non-unified/no-MOCV single-pass dispatch and
unified opaque exterior staged fog are corrected. Interior fog now requires
a strict MOGP interior among the retained viewer groups, and portal weighting
considers both groups. Full smoke exits 0 with
**744 tests** (66 Render, 485 DX11, 193 Avalonia), including 22 new CPU/WARP
transition/fog cases. See [the compact entry](#wmo-transition-colors-lighting-and-fog-2026-10-01).
The following presentation batch identifies the Client-only Stormwind gray
layer: native blending leaves low alpha in the imported GUI image, and Editor
glow masked it. The final external-image pass now writes alpha 1 without
changing scene RGB or internal material alpha. Full smoke exits 0 with
**746 tests** (66 Render, 487 DX11, 193 Avalonia). The captured full scene has
identical RGB before/after and opaque alpha throughout. See the
[compact entry](#wmo-transition-alpha-and-opaque-gui-presentation-2026-10-01).

Exterior doodad spatial batch: one scene queue now enlists loaded spheres using
native depth buckets and source-group floors, then performs spatial admission
before each band's horizon update. Terrain-sphere rejection consumes pending
eligibility while frustum/volume failures leave portal retry available. Prior
portal-written fog banks survive exterior-only frames. Full smoke exits 0 with
**778 tests** (66 Render, 519 DX11, 193 Avalonia), including 32 new cases.
See the [compact entry](#exterior-doodad-depth-buckets-and-terrain-sphere-admission-2026-10-01).
At that batch, size/fade categories and initial fog writers remained open; the
following batch advances them. Availability/animation/barrier branches and
matched captures remain open.

Doodad size/fade batch: transformed model boxes select categories; detail-scaled
bucket and first-callback group thresholds precede spatial/pending writes.
Sphere-center distance and alpha gate CPU submission while preserving fog history.
Fresh definitions use staged fog. Full smoke exits 0 with **812 tests**
(66 Render, 553 DX11, 193 Avalonia), including 34 new cases. See the
[compact entry](#doodad-size-categories-cpu-fade-gates-and-initial-fog-2026-10-01).
At that batch continuous GPU fade remained open; the following batch connects
the default non-shadow path. Native lifetimes and matched captures remain open.

Doodad GPU fade batch: opacity reaches the live instance shader and composes with
animated alpha; opaque/cutout fades select translucent blend and native scaled
byte references test composed alpha. Retained pose groups split distance/material
fades while keeping full-opacity instances grouped. Authored depth state and
restoration are covered. Full smoke exits 0 with **827 tests** (66 Render,
568 DX11, 193 Avalonia), including 15 new cases and extended WARP pixel coverage.
See the [compact entry](#doodad-gpu-fade-alpha-and-material-state-2026-10-01).
At that batch ZFill/shadow/layered state remained open.

Layered doodad material batch: the 12340 loader retains base-material blend
metadata using `materialIndex - materialLayer`. Native queue classification
uses that base blend and composed alpha, independently of depth flags; actual
blend/cutout/depth state remains per layer. Translucent base layers leave native
instancing even at full opacity. Full smoke exits 0 with **841 tests** (66 Render,
582 DX11, 193 Avalonia), including 14 new cases and extended loaded-scene/WARP
fixtures. ZFill control/eligibility/clone state is recovered but its port needs
native sorted queues and eligibility reseeding. See the
[compact entry](#doodad-layered-material-partition-and-zfill-dependency-2026-10-01).

M2 mesh renderer batch: owned mesh constants/palette/instance uploads and depth
states moved out of SceneManager, preserving prepared groups, both phase slots
and facade telemetry. Full smoke exits 0 with **843 tests** (66 Render, 584 DX11,
193 Avalonia). WARP readback verifies chunk-boundary uploads, material phases,
pose versions and state/upload cleanup after failures. No new native queue
contract is claimed; sorted keys/queues and eligibility lifetimes remain next.
See the [compact entry](#m2-mesh-submission-renderer-2026-10-01).

M2 element ordering policy batch: model/mesh distance rules, the complete base
transparent comparator with mesh/ribbon/particle opaque fallbacks, and native
heap mechanics are ported as CPU-only policies. Full smoke exits 0 with
**874 tests** (66 Render, 615 DX11, 193 Avalonia), including 31 new cases.
Live identity/decoded-input adapters, water queues, additive regrouping and ZFill
eligibility lifetimes remain open; current GPU draw order is unchanged.
See the [compact entry](#m2-element-keys-comparator-and-heap-policy-2026-10-01).

M2 decoded-input batch: authored WotLK MPQ batch/section fields survive loading
as owned values; explicit bone/model/view transforms feed the base distance
policy, with no missing-bone identity substitution. Full smoke exits 0 with
**899 tests** (66 Render, 640 DX11, 193 Avalonia), including 25 new cases.
Native pose/billboard/full-bone mapping and retained identities were the next
dependency; see the following batch. See the [compact entry](#m2-decoded-sort-metadata-and-transform-inputs-2026-10-01).

M2 retained-pose batch: all CPU bones survive native decoding and pose caching,
separate from the GPU palette. Live placement/view inputs, native parent modes
and billboard normalization are corrected. Model/shared/batch tokens and resolved
SRV lifetimes feed a packet/pose adapter; global draw order is unchanged.
Full smoke exits 0 with **920 tests** (66 Render, 661 DX11, 193 Avalonia),
including 21 new cases. See the [compact entry](#m2-full-pose-billboard-and-retained-sort-identities-2026-10-01).

M2 CPU queue batch: emitter/ribbon keys, the gated additive regrouping heap,
water sphere/plane selection and mesh/projected/ribbon/particle/callback routing
feed retained shared index queues. A production packet/pose fixture exercises
mesh/effect keys across frames. Full smoke exits 0 with **957 tests** (66 Render,
698 DX11, 193 Avalonia), including 37 new cases. Live producers, GPU interleaving
and water clipping remain open. See the [compact entry](#m2-particle-keys-additive-regrouping-and-water-queues-2026-10-01).

M2 native mesh preparation batch: decoded simple-animation eligibility, authored
water bounds, bone influences and shader inputs feed composed-alpha mesh
preparation with resolved effect tables. Entity-to-lighting and particle
force-below initializer rules are ported. Section +0x0E is now correctly decoded
as bone-combo start, not count. Full smoke exits 0 with **992 tests** (66 Render,
733 DX11, 193 Avalonia), including 35 new cases. Live effect-table owners, liquid
queries/cache lifetimes and GPU consumption remain next. See the
[compact entry](#m2-native-mesh-preparation-and-shader-selectors-2026-10-01).

1. **Accept the transition presentation correction.** Rebuild/reopen the
   captured Stormwind viewport and verify Client/Editor modes with glow on/off.
   GPU tests preserve native transition alpha internally and force only final
   external alpha to 1; all 451,200 captured scene RGB pixels are unchanged.
   Compare matched reference light/fog pixels, then close remaining unified
   local-light/shadow/material selectors. Strict-interior eligibility and
   primary/secondary portal weighting are ported; native loaded-target
   availability in `0x7D77C0` remains open.
2. **Finish all portal consumers.** Exterior doodad sphere buckets, terrain
   rejection, pending-versus-consumed state and retained portal-written fog
   banks are ported; see the [batch entry](#exterior-doodad-depth-buckets-and-terrain-sphere-admission-2026-10-01).
   Loaded WMO categories (`def+0x24`), initial staged fog, `0x78FB60` thresholds
   and `0x791CB0` CPU gates are ported. Default non-shadow GPU opacity/blend/
   cutout state and layered base-material partition are connected. M2 mesh
   submission now owns its GPU resources in a dedicated renderer. Authored
   batch/section metadata and explicit transform inputs are retained/tested.
   Native view-bone writers/full-bone indexing are recovered and the live pose
   cache/retained identity adapter is ported. Particle/ribbon keys, additive
   regrouping and per-type water routing now feed retained CPU queues. Next resolve
   scene-wide effect table owners, neighboring WMO spatial links and native
   availability/invalidation histories. Terrain/MODR entity caches, scene-side
   history and mesh water partitions/clipping are connected. Native mesh selectors/table adaptation, guarded simple/no-pose inputs,
   authored bounds and initial particle water flags are ported. Connect live queue
   producers and GPU consumption,
   enabling ordered ZFill clones. Trace model +0x10 bit 0x40 reseeding and
   loaded-definition lifetimes, then shadow alpha testing.
   Acceptance: retain the 35 preparation, 37 queue and 21 native bone/billboard/parent/pose/identity cases;
   complete CM2 attachment/runtime override histories, live interleaved effect/water
   order, crossing clip pixels and state restoration; preserve the 25 decoded-input/transform
   and 31 CPU ordering cases. Reference allocator tie order remains unvalidated.
   Then ZFill depth/color ordering, small/large category controls,
   toggle/reload histories and shadow/layered cutout fixtures;
   preserve mixed material fade pixels/state restoration, indoor/exterior
   and unload/reload histories; retain category/distance/alpha boundaries,
   CVar gates and fresh fog, plus matched client doodads.
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
3. **Complete global scene preparation.** Apply exterior rectangles/distances
   and the +33.333332 handoff to terrain, M2, other WMOs, doodads and liquids;
   retain depth-sorted/unbucketed exceptions. Recover native group availability,
   WDT bounds, transform arithmetic and terrain fraction handoff. Acceptance:
   compare scene-node/batch lists with client captures without conservative
   full-frustum fallbacks in Client mode.
4. **Close liquid, lighting and frame contracts.** Recover viewer-liquid/plane
   selection, blend-sky, entity MOCV, animation/eligibility/bounds; wire common
   element queues/conditional frame graph using the extracted M2 renderer.
   Acceptance: independent boundary/order fixtures, state restoration and
   liquid/interior/exterior captures.
5. **Finish modes and all rendering workstreams.** Close CVars, streaming,
   LOD/fades and explicit whole-map Editor demand, then every R00–R13 CPU path
   and its cached DX9 SM3 shader/selector/constant/state contract. Acceptance:
   settings boundaries, mode switching, other-client regressions and full
   rendering reachability/indirect-call closure.
6. **Sign-off evidence.** Pin the original executable hash and capture matched
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
Model/mesh key writers, pointer-based ties, the full base comparator/fallbacks
and heap mechanics now have instruction-grounded CPU policies and independent
fixtures; see the [ordering batch](#m2-element-keys-comparator-and-heap-policy-2026-10-01).
Authored MPQ sort inputs and explicit transforms have a tested adapter;
see the [decoded-input batch](#m2-decoded-sort-metadata-and-transform-inputs-2026-10-01).
Live pose/identity adaptation, particle secondary-key callers and
additive-particle regrouping remain open.
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
| `0x7998A0` | Enlist group doodad definitions after culling | Partial: sphere buckets, source-group floors, append/link deduplication, size/detail prefilters, CPU distance/alpha eligibility and default non-shadow GPU fade ported; ZFill/shadow state and native runtime availability/lifetimes remain open. See the [GPU fade batch](#doodad-gpu-fade-alpha-and-material-state-2026-10-01). |
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

Continuation: the [portal sphere/fog batch](#doodad-portal-sphere-admission-and-instance-fog-2026-10-01)
implements that next slice and records its remaining exterior admission gates.

## Doodad portal sphere admission and instance fog (2026-10-01)

**Rule and evidence (3.3.5.12340).** Focused live instruction/dataflow checks
identify these roles by address; existing IDB names, types and comments remain
unverified hypotheses. This batch does not reconcile the archival inventories.

- `0x799366` appends a first visible group; `0x799415` copies the current
  frustum and `0x799425` appends it to that group's chain on every callback.
  The pointer writes at `0x6DEDAC`–`0x6DEDBB` establish append order despite
  the guessed "Head" label. `0x7993E2` clears group bit 0x8000 on first visit;
  `0x7993F2` ORs surviving interior propagation across later visits.
- `0x7AC45D`–`0x7AC492` crops the base frustum to `(rect+1)/2` before recursion
  using `0x790E20`. `0x983E70` constructs all six planes; `0x791341`–`0x79136F`
  normalizes their normals and sets their distances. Unlike portal polygon
  clipping, doodad sphere tests include the near plane. `0x983D31`–`0x983D5A`
  accepts each signed plane distance >= negative radius, including equality.
- `0x799B70` maintains a generation stamp and pending byte at `def+0x25`.
  `0x799C36`–`0x799C6D` accepts a sphere against any copied group frustum;
  rejection leaves it pending for another owner. `0x799C8D` consumes the
  first acceptance, and `0x799C93`/`0x799C9C` sets/clears doodad bit 0x8000
  before submission at `0x799CA8`. The existing `0x7C1150` lighting callback
  reads that bit to choose current versus staged fog, independently of baked
  versus sunlight classification. Missing models/barriers and size-category
  eligibility are separate branches and remain unported.
- `0x79A11C` consumes an unbucketed group's current frustum chain/propagation
  immediately after its exterior callback. `0x79A2EB` consumes the final visible
  group list in append order, allowing MOGI mask 0x10008 only for the primary
  placement. Later callbacks must not retroactively enlarge earlier consumers.
- The bucket path differs: `0x79A242` calls `0x7998A0` to enlist definitions
  using sphere depth and an originating-bucket floor. `0x7987A0` later tests
  the cropped scene frustum, static volumes and `0x78FC40` terrain sphere
  occlusion with flags 16. It stamps pending at `0x798859`/`0x798862`, then
  clears pending before terrain rejection; it does not write the portal fog
  bit. These contracts are traced, not ported in this batch.

**Port.** `Wrath335WmoDoodadVisibility` retains normalized world-plane frusta,
MODR reverse links, ordered consumer snapshots and per-definition results in
placement scratch. Scene preparation records unbucketed consumers and the final
group pass; the M2 culler supplies its refreshed world sphere. Shared references
select the first accepting consumer regardless of GPU asset order. Fog selection
is frame guarded and cleared on preparation/reset; `M2InstanceData`, the input
layout and live M2 shader carry an explicit fog bank in the 144-byte stream.
Material black/white/gray/unfogged policies still apply. Neutral streams retain
shared scene fog. The exterior bucket adapter retains scene fog and a scene-view
sphere gate; it is explicitly not native bucket admission or fog-bit history.

**Verification.** Required full smoke exit **0**, **718 tests**: 66 Render,
459 DX11, 193 Avalonia. Eighteen new behavioral cases cover off-window rejection,
two disjoint windows and their gap, all six inclusive sphere boundaries,
perspective radius/camera translation, shared-owner append order, failed-owner
retry, call-time snapshots, primary-placement eligibility, resource/frame reset,
invalid MODR and the live preparation/container frame seam. The existing WARP
fixture now renders mixed current/staged fog in one draw, verifies material
color overrides and explicit disabled fog, then restores the shared bank.
Log: `%TEMP%/wteditor-smoke-doodad-portals-fog-20261001.log`.

**Remaining / next acceptance.** Replace the exterior adapter with native
`0x7998A0`/`0x7987A0` enlistment/admission and recover `0x78FC40` sphere terrain
occlusion. Trace size/fade-category inputs and runtime 0x8000 writers across
frames before changing exterior fog history. Test sphere-derived bucket clamps,
nearest-even cutoffs, frustum/volume retry versus terrain consumption and
shared-owner indoor/exterior fog history. Underwater/dynamic lighting, entity
MOCV, native availability/lifetimes, animated bounds, liquids, numerical x87/
plane reconstruction and matched client pixels remain open. Original executable
hash and full rendering closure remain sign-off gates; smoke/WARP is not parity.
Follow the [Roadmap](#roadmap).

## Exterior doodad depth buckets and terrain-sphere admission (2026-10-01)

**Rule / evidence (3.3.5.12340).** Focused live instructions/dataflow establish
these roles by address; all IDB labels, prototypes and comments remain unverified
navigation aids. No shader re-extraction or archival inventory reconciliation.

- `0x7998E4`–`0x7998F7` requires runtime availability bit 0x80, a model and
  an unused intrusive queue link at `def+0xAC`. `0x7998F9`–`0x799920` computes
  horizontal-plane sphere-center depth minus radius. `0x799934`–`0x79994F`
  stages float(depth*0.03), subtracts 0.5, uses nearest-even FISTP, rejects
  unsigned bucket >=64, then floors to the originating group bucket. Nonpositive
  depth retains that group bucket. `0x79995F` uses the previously instruction-
  verified append helper at `0x6DED60`; a linked definition is not moved by a
  second owner. `0x79A242` enlists after source group gates, independently of
  callback visibility; `0x79880B`–`0x798811` unlinks before admission.
- `0x798859`/`0x798862` restamps pending. Frustum `0x983D20` and static-volume
  `0x7CCE00` failures leave it pending for later portal consumers. `0x79889E`
  clears pending before flags=16 terrain reader `0x78FC40`, so terrain rejection
  blocks later portal admission. Re-enlistment can restamp it; a model already
  submitted by an earlier bucket retains that submission.
- `0x78FC46`–`0x78FC8F` gates terrain enable, |radius| >=2^-22 and inclusive
  pitch [-0.9,0.9]. `0x78FCA2` transforms the center by flattened world matrix
  `0xADF460`; `0x78FCC2` transforms (radius,radius,0) as a point by separate
  camera projection `0xADF628`, including its translation. The matrix copy and
  composition are at `0x795671` and `0x795BB7`–`0x795BCE`; the point transform
  at `0x4C21B0` confirms the translation terms. Stored center clip-Z >=50 is
  required unless bit 8 is set; bit 16 does not bypass it. `0x78FD00`–`0x78FD1C`
  uses projected radius X for the top, radius Y for the width, and float stores
  before the right edge. `0x78FD27`–`0x78FD5C` retains nearest-even column
  conversion and the extra right column; every covered height must be >= top.
- `0x79A7F2`/`0x79A7FA` process terrain sources and WMO groups; `0x79A830`
  consumes doodads before the band's horizon updates at `0x79A836`. Bucket
  admission never writes runtime fog bit 0x8000. Portal writes at
  `0x799C93`/`0x799C9C` therefore survive later exterior-only frames.

**Port.** `Wrath335ExteriorDoodads` replaces the exterior adapter with one
reusable scene queue shared by placements. `WmoScenePortalPreparation` collects
loaded static M2 spheres, enlists source MODR links and consumes each band before
its terrain horizon updates. `Wrath335TerrainClipBuffer.ContainsSphere` ports
the separate sphere projection reader. `Wrath335WmoDoodadVisibility` retains
consumption and portal-written fog history separately from frame results; loaded
portal spheres prepare their fog before later GPU tile/distance rejection.
Resource replacement resets history. Fresh exterior definitions with no verified
portal write keep the existing scene-fog fallback; their initial runtime bit is
not guessed. Existing shader/material fog banks and other-client paths are reused.

**Verification.** Required full smoke exit **0**, **778 tests**: 66 Render,
519 DX11, 193 Avalonia. Full log:
`%TEMP%/wteditor-smoke-exterior-doodads-20261001-verified.log`.
The batch adds 32 behavioral cases covering sphere bucket floors/cutoffs,
translation/radius, shared queue links, missing models, cropped-frustum/volume
retry versus terrain consumption, earlier-band horizon timing, fog history and
resource/frame guards. Sphere-reader cases independently check epsilon, pitch,
clip-depth, separate/swapped projected extents, top equality, extra right column,
offscreen clamping and invalid inputs. The scene fixture checks actual loaded
M2 enlistment, delayed-band/cutoff/frustum decisions and profile/frame guards.

**Remaining / next acceptance.** `0x7998A0`/`0x7987A0` are partial contracts:
decoded M2 availability adapts native runtime 0x80 and model lifetimes; size/fade
eligibility, missing-model barriers and rejection animation gates are unported.
`0x78FB60` compares the band's squared distance against category thresholds;
`0x791DAF` and `0x791E28` gate hard distance and fade alpha during submission.
At this batch, writers of `def+0x24`, initial fog 0x8000 and threshold/CVar
producers were the next slice; the following size/fade entry advances those
claims while native lifecycle and GPU fade state remain open.
Protected horizon sources, native combined terrain bounds/availability, GPU
volumes, x87 numerical captures, original executable hash and matched pixels
remain open. This batch does not complete the original bounded function list
or rendering parity; follow the [Roadmap](#roadmap).

## Doodad size categories, CPU fade gates and initial fog (2026-10-01)

**Rule / evidence (32-bit 12340).** `0x7BDB10`, called from loaded preparation
at `0x7B5764`, transforms the model box at `0x7BDC91`. The largest world-box
extent is calculated in extended arithmetic at `0x7BDCFD`/`0x7BDD17`/`0x7BDD2E`;
`0x7BDD31..0x7BDD4C` stores category 0..4 at `def+0x24`, with inclusive
boundaries 1, 4, 15 and 100. Sphere radius is a separate field and does not
select the category. `0x78F570` produces maxima {30,100,200,750,1250} and fixed
fade ranges {5,10,15,20,50}; only categories 1..3 scale with `environmentDetail`.
`0x78DC60` clamps detail to 0.5..1.5. Products/minima remain extended before
float table stores; squared thresholds are not computed from reloaded rounded
distances. `0x78FB60` returns 0 for negative distance and uses strict squared
maximum comparisons for categories 0..3; equality advances the required size.

The bucket loop stores `float(bucket * 33.333332f)` at `0x79A826` and calls its
consumer at `0x79A830`. `0x798821` rejects undersized definitions before stamp/
pending writes. The first group callback alone writes full-direction nearest-box
depth at `0x7993A2..0x7993CF`; plane W is previously stored at `0x795586`.
Unbucketed/final portal consumers classify that retained depth (`0x79A0FC`,
`0x79A2CB`) and skip undersized references before pending/frustum processing.
These prefilters apply independently of `objectFade`.

Submission at `0x791CB0` uses the world sphere center and eye, without radius
subtraction: `x²+(y²+z²)` at `0x791D70..0x791DA1`. World enable 0x4000 gates
this stage (`0x791D50`); definition flag 0x800 bypasses it (`0x791D65`). Strict
distance² > stored maximum² rejects at `0x791DAF`. Values <= minimum² retain
alpha 1. Otherwise `0x791DCB` uses stored-float SQRTSS when CPU flag `0xCF08F8`
is enabled; `0x791DE7` uses extended FSQRT. Alpha is
`1-(distance-storedMinimum)/storedRange`, snaps to 1 above float 0.99 and rejects
at/below float 0.01 (`0x791E28`), before writing `CM2+0x178` at `0x791E60`.
Fog/pending changes precede this submission gate. `objectFade` registers default
"1" at `0x78E7BC..0x78E7D7`; callback `0x78DB90` toggles enable 0x4000.
Fresh MODD creation explicitly assigns flags 1 or 0x1001 at `0x7BF005`/
`0x7BF012`, clearing fog bit 0x8000. MODD bit 0x1000 is distinct from the MDDF
distance bypass 0x800 (`0x7BED99..0x7BEDA6`). Preparation at `0x7B5850`
preserves higher bits. Existing IDB names/types/comments remain hypotheses;
addresses, instructions and data flow support this bounded rule.

**Port.** `Wrath335DoodadFade` holds categories, thresholds, camera contracts
and CPU alpha eligibility. `WowViewerEngine` resolves the profile only for
12340 Client rules; `RendererSettings.ObjectFade` defaults true and clones.
`WmoScenePortalPreparation` supplies loaded spheres/world boxes, and
`Wrath335WmoDoodadVisibility` retains first-callback group categories, applies
bucket/portal prefilters and submission rejection, and records CPU opacity.
New resources initialize staged fog; rejected submissions preserve portal-written
history. Continuous GPU opacity/blend-state integration is deliberately open:
the retained value is not yet sent to M2 draws. No shader/material state changed.

**Verification.** Full smoke exit **0**, **812 tests**: 66 Render, 553 DX11,
193 Avalonia. Log: `%TEMP%/wteditor-smoke-doodad-size-fade-20261001.log`.
34 new cases cover axis/equality extents, rotated/scaled boxes, detail clamps
and fixed endpoint categories/ranges, strict threshold equality, extended-product
table stores, snap/drop and bypass gates, SSE versus x87 roots, center versus
radius/depth contracts, mode/other-client isolation, first-callback ordering,
undersized-owner retry, objectFade-independent prefilters, rejected-submission
fog history, retained opacity and loaded scene/frame integration. Fresh-bank
and resource-replacement fixtures now assert staged fog.

**Remaining / next acceptance.** Trace `CM2+0x178` consumers and native
opaque/alpha-test/blended fade and `objectFadeZFill` state before wiring opacity
to GPU draws. Add mixed-material fade pixels and state-restoration fixtures,
then compare matched client captures. Native runtime 0x80/streaming lifetimes,
animated bounds, missing-model barriers/rejection animation, standalone MDDF
consumers and full CVar/UI persistence remain open. The engine exposes
`ObjectFade` through renderer settings only; the existing detail UI stays disabled
pending the rest of its consumers. Double arithmetic approximates x87 extended
operations; exact numerical captures, original binary hash and full CPU/GPU
closure remain sign-off requirements. No inventory reconciliation or parity
claim is made; follow the [Roadmap](#roadmap).

## Doodad GPU fade alpha and material state (2026-10-01)

**Rule / evidence (32-bit 12340, default non-shadow SM3).** The submitted alpha
at `CM2+0x178` (`0x791E60`, `0x791E7C`) reaches animation at
`0x82E1E8..0x82E218` and `0x82E2B0..0x82E2DB`: model alpha multiplies the
material/global alpha inputs and stores the mesh master alpha at +0x19C.
Queue construction `0x821EB0..0x821EE9` multiplies that value by animated color
alpha and texture weight. Below float 0.0001 (`0x9E8CD0`), the batch is skipped
at `0x821EFD`. `0x821F4C..0x821F68` selects translucent for base-layer blend >1
or composed alpha below `0xA45528` (bits `0x3F7FFF58`, about 0.99999).
`0x8220A3` stores composed alpha in element+0x0C. `0x822057` excludes translucent
doodads from the type-2 instanced path. Full native global sorting/water separation
is not implemented by this batch.

Material setup at `0x81FEF0..0x81FF08` indexes the 3x7 table at `0xA453B0`:
opaque row {0,1,2,10,3,4,5}; translucent rows {2,2,2,10,3,4,5}. These are Gx
blend values for M2 modes 0..6; opaque/key/alpha fades use source-alpha blending,
and additive/modulate families keep their blend family. `0x6A4D58..0x6A4DE1`
confirms blend enable and source/destination table use. `0x81FF90..0x81FFA4`
sets cutout reference to composed alpha times stored float 224/255
(`0xA3FDCC`); opaque uses zero, other modes use 1/255. `0x873BBD..0x873BC3`
stores float(reference*255) and truncates to the byte reference. Zero disables
the fixed alpha test (`0x6A4E25..0x6A4EA3`). `0x8730B1..0x8730BE` selects
fixed testing when the pixel key's bit 8 is clear. Shader-key construction
`0x81F2D5..0x81F31B` ties that alternate shader test to caps/shadow conditions.
The default non-shadow DX9 path uses the byte reference; shadow variants remain
separate. Depth test/write remain authored flags 0x8/0x10 (`0x820013`,
`0x82004C`), not an automatic read-only override for fading.

**Cached SM3 formula evidence.** `Diffuse_T1.bls` ordinal 0, assembly
`4af184180cf9ca9c6b404eb52023ac46472189b0b324adb6f41c87e343df07c7`:
c28/c29 feed saturated diffuse output alpha. `Combiners_Opaque.bls` ordinal 0,
`70ec5f9b1330c5d5a7b3412727dca1533b18d202eaa505c633a03439c21335d8`:
output alpha is diffuse alpha, independent of texture alpha. `Combiners_Mod.bls`
ordinal 0, `2d60461d51d9f633ebe2394491b1d98d996b65a9e1988ccda03ab46f97c01e37`:
output alpha is texture alpha times diffuse alpha; fog changes RGB separately.
Its shader-test ordinal 8,
`670a7c67284f43d85bb159df327bd76ec9b3d0451a2211dac57597098ea17c36`,
kills on composed texture/diffuse alpha minus c2.w. These are bounded formula
witnesses, not original HLSL or a complete permutation selector audit. No cache
regeneration or hash/inventory sweep was needed. IDB names/types/comments remain
unverified; instructions and table bytes support the active claims.

**Port.** `Wrath335M2FadeMaterial` resolves composed-alpha visibility, pass,
blend and the default byte alpha reference. `M2DoodadFadeDrawGroups` retains
pose ownership, keeps full-opacity instances grouped and submits distance/
material-alpha fades individually using reusable storage. The packet owns that
scratch; `SceneManager` delegates classification and binds existing states.
Frame-guarded WMO opacity reaches `M2InstanceData.RenderParameters` and the live
`m2.hlsl` shader. A neutral extension preserves Editor/other-client/standalone/
sky behavior. Native cutout uses composed output alpha; opaque combiners ignore
texture alpha. Shared blend descriptions moved to `SceneBlendPolicy` so GPU
fixtures use renderer state. Depth flags and existing post-pass restoration
remain active; zero-material batches are omitted only for the native profile.

**Verification.** Full smoke exit **0**, **827 tests**: 66 Render, 568 DX11,
193 Avalonia. Log: `%TEMP%/wteditor-smoke-doodad-gpu-fade-20261001-verified.log`.
15 new cases cover blend families, threshold/invisible equality, composed
animation alpha, byte references, depth flags, baseline isolation, retained
pose/group reuse and frame-guarded loaded-scene-to-instance propagation.
The extended live-shader WARP fixture checks differing instance fades over a
colored background, animated alpha composition, scaled cutout rejection/pass,
full-opacity cutout/opaque switching, opaque zero texture alpha, read-only fading
depth and restored depth writes rejecting the following farther draw. The GPU
check caught and corrected an input-layout offset error before verification.

**Remaining / next acceptance.** `objectFadeZFill` defaults zero at
`0x78E7EC`; callback `0x78DBE0` toggles World enable 0x8000. Recover its scene
handoff, duplicate-element/color-mask gates and ordering before exposing it.
Next verify ZFill off/on depth/color pixels, then shadow shader alpha-reference
selection and layered base-material pass classification. General combiner RGB/
alpha saturation, dynamic lights/shadows, all caps/fallback paths, native sorted
mesh/effect/water queues, definition availability/animation/barriers, standalone
MDDF fade inputs and saved CVar UI remain open. Byte alpha conversion and x87
numerical captures, original binary hash and matched client pixels still gate
sign-off. The default WMO fade port does not close these paths or full parity;
follow the [Roadmap](#roadmap).

## Doodad layered material partition and ZFill dependency (2026-10-01)

**Rule / evidence (32-bit 12340).** `0x821F32..0x821F47` reads the batch's
ushort +0x0C and subtracts it from the material index at +0x0A. It indexes the
4-byte material table using that difference; it does not select a preceding
batch. `0x821F4C..0x821F68` classifies the queue using that base material's
blend >1 or this batch's composed alpha below bits 0x3F7FFF58. Actual material
setup still uses the current layer's material at `0x81FF08` (blend),
`0x81FF74..0x81FFA4` (cutout) and `0x820013`/`0x82004C` (depth). Depth flags
do not select the native queue. `0x822057` excludes translucent elements from
the instanced doodad path. Existing IDB labels/types/comments are navigation
hypotheses; the instructions and table addressing support these rules.

**ZFill dependency recovered.** `M2UseZFill` registers default 1 at `0x40279B`;
`0x4028B0` contributes cache bit 1, passed through `0x4048EB` to initialization.
`0x81C1B8..0x81C1C6` enables that bit only with color-write support.
`objectFadeZFill` is separate: default 0 at `0x78E7EC`, callback
`0x78DBFF`/`0x78DC1A` changes World enable 0x8000. Bounds/category preparation
`0x7BDD51..0x7BDD65` clears model +0x10 bit 0x40 when that enable is off and
category <3. The constructor seeds runtime bits 0x340 at `0x82BE80`.
Preparation only clears eligibility; turning the CVar on does not set the bit
here. Re-seeding/recreation and remaining writers must be traced before exposing
a live toggle or claiming its lifetime behavior.

`0x821DF6..0x821E15` requires cache bit 1, creation flags +4 bit 1 clear and
runtime +0x10 bit 0x40 set. `0x8224F7..0x82251A` additionally requires a
translucent, non-projected batch whose own material permits depth writes
(flag 0x10 clear). `0x822523..0x8225CD` gives the original secondary sort key
FLT_MAX (0x9EA8FC), clones the 68-byte element, sets clone +8 bit 1 and appends
it to the same above/below-water queues. Setup at `0x81FEDC..0x81FF44` gives
that clone zero color-write mask and Gx blend 1 for key materials, otherwise 0;
alpha reference and authored depth flags remain active. This is a sorted-queue
duplicate, not an independent scene-wide depth prepass. Comparator
`0x81EF36..0x81EFBA` sorts primary key descending, then clone bit descending,
signed priority plane ascending and secondary key descending; later shader/
model/layer tie-breakers remain part of the complete comparator. Native global
mesh/effect/water queues are absent from the destination. Implementing an
unordered duplicate would not establish this contract, so ZFill remains unported.

**Port.** Shared `M2Loader` retains WowLib's decoded material layer and resolves
`Submesh.baseBlendType` for WotLK roots. Valid layer offsets use the native
material-table rule; malformed underflow/out-of-range inputs retain the existing
safe material fallback, without claiming native malformed-file behavior.
`Wrath335M2FadeMaterial` uses base blend for native WMO doodad partition and
current-layer blend/reference for state; its native queue no longer depends on
depth flags. `M2DoodadFadeDrawGroups` classifies once per source pose, isolates
translucent-base instances and preserves distinct non-unit opacity constants.
`SceneManager` delegates both pass presence and live material selection.
Editor, standalone and other-client paths retain their existing partition rule.

**Verification.** Full smoke exit **0**, **841 tests**: 66 Render, 582 DX11,
193 Avalonia. Log: `%TEMP%/wteditor-smoke-layered-material-20261001-verified.log`.
14 new cases cover opaque-base layers across all seven blend families,
translucent-base overrides, per-layer cutout, composed/invisible alpha,
material-table layer offsets/malformed boundaries and baseline isolation.
Existing depth-flag cases now verify queue independence. The loaded-scene
fixture checks shared opaque-base versus individual translucent-base groups,
retained poses/storage and frame guards. Live M2 WARP pixels verify key layers
of opaque/translucent bases retain their cutout while switching blend rows.

**Remaining / next acceptance at this batch.** Move M2 submission into a dedicated
renderer (completed in the following batch) and establish native sorted element
keys/queues before connecting ZFill clones.
Trace runtime bit 0x40 reseeding through unload/reload and bounds refresh;
verify `M2UseZFill` versus `objectFadeZFill`, categories 0–2 versus 3–4,
projected/depth-write exclusions and color/depth restoration pixels. Then
recover shadow alpha-reference selectors and remaining layered shader/caps
variants. Standalone MDDF input adaptation, global ordering, native availability,
original executable hash, numerical captures and matched client pixels remain
open. Follow the [Roadmap](#roadmap); this batch does not establish full parity.

## M2 mesh submission renderer (2026-10-01)

**Rule / evidence.** Preserve the established placement/pose/fade/material
contracts while separating GPU mesh submission from scene preparation and frame
ordering. Build 12340's base-material queue selection remains supported at
`0x821F32..0x821F68`, per-layer blend/cutout at `0x81FF08`/
`0x81FF74..0x81FFA4`, and depth flags at `0x820013`/`0x82004C`.
The bounded comparator at `0x81EF36..0x81EFBA` and ZFill clones at
`0x822523..0x8225CD` still require full keys/global queues and eligibility
lifetimes before implementation. This extraction adds no native semantic
claim; unchanged evidence was not re-exported. Existing IDB names/types/comments
remain navigation hypotheses; the original executable hash remains unpinned.

**Port.** `Renderer/M2MeshRenderer.cs` owns mesh constants, a retained bone
palette, the 1,024-instance dynamic upload buffer, depth states and binding
scratch. Meshes/textures/shaders/samplers/rasterizers/device/context remain
borrowed, with explicit inputs and returned draw/work/upload/binding/timing
statistics. Palette uploads track pose identity/version; material constants
reset for each phase. SceneManager retains CPU culling/animation/fade preparation,
its shared WMO upload buffer, effect submission and the existing opaque-before-
water/translucent-after-water frame slots. Its public material helper facade
delegates to the transferred policy. Each mesh phase establishes its pipeline,
restores default depth and the one-sided rasterizer, and documents other bound
state. Mapped instance uploads unmap on failure; effects retain their own depth
controller. No per-object GPU allocation or native sorted queue was added.

**Verification.** Full smoke exit **0**, **843 tests**: 66 Render, 584 DX11,
193 Avalonia. Log: `%TEMP%/wteditor-smoke-m2-renderer-20261001-verified.log`.
`M2MeshRendererTests` renders through the extracted renderer at 1 and 1,025
instances; only the final instance covers the sampled pixel. It verifies opaque
and blended material pixels/work counters, chunk uploads, palette reuse and
version changes, updated material color, pipeline rebinding after intervening
water/debug state, default depth/rasterizer restoration on a texture-cache
failure, and successful upload/draw after an instance-fill failure. Existing
native fade/cutout, loaded-scene and other-client behavioral suites remain green.

**Remaining / next acceptance.** Recover all native sorted element key producers
and the full comparator, then shared mesh/effect/water queue insertion and
liquid-dependent consumption. Acceptance: independently grounded ordering and
tie-break fixtures across model/material/layer/effect types, above/below-water
frame histories, and pipeline restoration. Trace model +0x10 bit 0x40 reseeding
and loaded-definition lifetimes before ZFill; accept independent
`M2UseZFill`/`objectFadeZFill` category/toggle/reload behavior and ordered clone
depth/color pixels before shadow selectors. Native availability, standalone
MDDF adaptation, numerical/matched client captures and full rendering closure
remain open. Follow the [Roadmap](#roadmap). Archival semantic tables retain
their existing snapshot/pending deltas; this batch does not close parity.

## M2 element keys, comparator and heap policy (2026-10-01)

**Rule / evidence (32-bit build 12340, DX9 SM3).** Existing IDB names,
prototypes/types and comments remain hypotheses. These rules follow register
data flow, offsets and branch instructions; no reference cache/export refresh
was required. The original executable SHA-256 remains unpinned.

| Contract | Supporting instruction sites |
| --- | --- |
| Whole-model key is squared view-origin distance, not view Z. Model +0xF4 is assigned the local +0xB4 matrix times the supplied view matrix; translation +0x124/+0x128/+0x12C is squared into model +0x88. A non-null +0x48 source whose creation +4 bit 0x1 is clear supplies its +0x88 instead. | `0x82E345..0x82E3A2`, independently repeated at `0x82F2F5..0x82F354`; `0x821C69` establishes the model pointer consumed by mesh key writers. |
| Mesh secondary uses section +0x20 transformed by the section +0x12 bone matrix from model +0x98. Normally it is squared center distance, negated if the adjusted view Z is negative. Batch byte +0 bits 0x1/0x2 subtract/add the normalized center direction times section +0x2C radius scaled by the matrix first-axis length; 0x1 wins when both are set. Normalize only above float bits 0x34800000; tiny/equal centers keep the original direction. | `0x822120..0x822333`; threshold at `0x9EA27C` is bytes `00 00 80 34`; center/radius/default branch at `0x8222FC..0x822333`. |
| Opaque primary/secondary both use model +0x88. Data flag +0x10 bit 0x10 gives translucent meshes raw squared-center secondary and model primary, bypassing sign/radius adjustment. Otherwise primary equals secondary except eligible non-projected depth-writing ZFill meshes, whose primary is the model key. Batch +1 is sign-extended into element +0x24 priority plane. | `0x8220AC..0x8220B4`, `0x8220CF..0x82211B`, `0x82232F..0x82236B`; eligibility remains supplied by the previously recovered preparation gates. |
| Transparent prefix: primary descending; clone flag (element +8 mask 0x1) descending; signed priority ascending; secondary descending. Unordered float comparisons fall through to subsequent fields. | `0x81EF51..0x81EFBA`; x87 status masks/parity branches at `0x81EF60`/`0x81EF75` and `0x81EFAA`/`0x81EFB7`. |
| Cache flag 0x4000 and two non-null effects allow shader table keys (+0x2C/+0x194, indices element +0x34/+0x38) before identity when type/model differ. Then unsigned model identity, signed element kind, layer (+0x0C) for kinds 0–2, optional shader keys again, and the opaque fallback. | `0x81EFBE..0x81F0C8`; all shader/model/layer comparisons are instruction-checked. |
| Opaque kind order is signed ascending. Kind 0 mesh fallback: layer, shader keys (independent of 0x4000), shared model identity (+0x2C), opt-geometry flag 4, model identity, section bone-combo start (+0x0E; decode corrected in the native preparation batch), current blend, material flags masked 0x1F, lexicographic texture handles/count, unsigned batch address. Kind 1 skips the normal-mesh prefix and begins with current blend. | `0x81EEC7..0x81EF10`, `0x81EAD0..0x81ECF8`; batch-address argument rewrite at `0x81EB09` confirms the final tie-break identity. |
| Kind 3 ribbon fallback uses texture-handle prefix/count then unsigned emitter index. Kind 4 particle fallback uses signed resolved blend, reconstructed material flags from emitter bits 1/2/4, then the texture-handle difference. Kinds 2/5 have no opaque fallback tie-break here. | `0x81ED10..0x81EDDE`, `0x81EDF0..0x81EE84`, dispatcher `0x81EEE2`. |
| Texture comparison is wrapped 32-bit SUB followed by signed SAR 2, rather than unsigned handle order. Heap chooses the right child only when strictly greater and moves a child only when strictly greater than the saved item; complete ties are unstable. | `0x47BF23..0x47BF29`; `0x83DD4D..0x83DD74`, `0x83DDED..0x83DE14` and extraction `0x83DDB0..0x83DE36`. |

Ribbon keys both read model +0x88 at `0x822712`/`0x82271B`; priority is signed
word at `0x822705`. Particle primary reads model +0x88 at `0x82198A`, while
secondary is the caller's argument at `0x821996`; its upstream expression still
needs tracing. Existing comments describing +0x88 as generic view depth do not
override the independently checked squared-origin writers.

**Port.** `Renderer/Wrath335M2ElementOrdering.cs` supplies CPU-only distance-key
functions, decoded sort inputs, both complete base comparators and in-place
retained index-span heap sorting. Eligibility and transformed center/scaled
radius are explicit preparation inputs. Identity values represent native
unsigned address order; texture comparisons retain wrapped arithmetic. They
are never FileDataIDs. Borrowed texture storage is immutable during sorting.
No per-sort allocation or stable insertion-order tie-break is added. These are
policies for the next queue adapters: they do not yet change SceneManager or
M2MeshRenderer's live GPU order, and they exclude additive regrouping.

**Verification.** Full smoke exit **0**, **874 tests**: 66 Render, 615 DX11,
193 Avalonia. Log: `%TEMP%/wteditor-smoke-m2-element-order-20261001.log`.
31 cases cover model/parent keys; near/far/both flags and eye crossings;
tiny/equal threshold behavior; opaque/raw/ZFill/projected/no-write branches;
contradictory later keys establishing comparator precedence; signed priority/
blend and unsigned identities; nullable shader keys/grouping; projected and
mesh fallbacks; texture prefixes/counts/wrap; ribbons; all particle material
flag combinations; NaN/zero fall-through; empty/singleton/tied heap histories;
mixed mesh/ribbon/particle/callback and clone/color index order. Existing GPU
and other-client suites remain green, without claiming new GPU ordering pixels.

**Remaining / next acceptance at this batch.** Retain decoded WotLK batch flags/signed
priority/layer and section center-bone/sort-center/radius/bone-count metadata;
adapt model/parent/view/bone keys and retained object/resource identities without
using asset IDs as sort proxies. Verify signed ranges, transforms/parent histories,
pose versions, shared versus individual identities and unload/reload lifetimes.
Trace particle secondary-key callers, then `0x81F9E0` additive regrouping and
shared mesh/effect/callback water routing/consumption. Acceptance: independent
decoded-input and additive order fixtures, above/below/straddling/viewer-liquid
histories, interleaved GPU pixels and state restoration. Finally trace runtime
bit 0x40 reseeding and accept ordered ZFill control/category/toggle/reload and
depth/color pixels before shadow selectors. Double arithmetic is an adapter for
x87 intermediates; matched numerical/client pixels, original binary provenance
and full rendering closure remain open. See the [Roadmap](#roadmap); archival
semantic tables keep their snapshot and the pending deltas recorded above.

## M2 decoded sort metadata and transform inputs (2026-10-01)

**Rule / evidence.** Build 12340, DX9 SM3; existing IDB names/types/comments
remain hypotheses. Batch flags +0, signed priority +1, layer +0xC and section
bone count +0xE, center bone +0x12, sort center +0x20 and radius +0x2C supply
the recovered comparator/key contract. Supporting sites: `0x8220AC..0x8220B4`,
`0x822120..0x8221CA`, `0x8222A1..0x82236B`, and opaque fallback `0x81EAD0`.
A focused check of `0x8220CF..0x82211B` confirms root +0x10 mask 0x10 still
transforms the selected center bone, while bypassing signed/radius distance.
Model-origin/eligible-parent rules remain at `0x82E345..0x82E3A2` and
`0x82F2F5..0x82F354`. No cache extraction or bulk CPU export was needed.

**Port.** Shared [M2Loader](../../../WoWRenderLib/Loaders/M2Loader.cs) snapshots
the real WowLib profile through the production decode path into nullable
`Submesh.wrath335Sort`, gated to WotLK MPQ roots. Its fields preserve signed
priority and full-width layer/bone values and copy the authored sort center,
not the section average or a computed bounding sphere. DX11 already retains
the parsed submeshes. [Wrath335M2MeshSortInputs](../../../WoWRenderLib.DX11/Renderer/Wrath335M2MeshSortInputs.cs)
composes supplied model-space bones with model/view matrices and scales radius
by the composed first axis. Opaque keys require no bone; translucent keys fail
explicitly if the center bone is unavailable. Each call reads current matrices.
Eligibility and parent keys are supplied, and identity/queue selection is not
inferred. Live GPU draw order is unchanged.

**Verification.** Full smoke exit **0**, **899 tests** (66 Render, 640 DX11,
193 Avalonia); log `%TEMP%/wteditor-smoke-m2-sort-inputs-20261001.log`.
[25 new cases](../../../WoWRenderLib.DX11.Tests/Wrath335M2MeshSortInputsTests.cs)
cover native profile decoding, signed ranges/high layers and bones, selected
section versus average center, source mutation/disposal, root/client isolation,
missing sections, noncommuting transforms, first-axis versus max-axis scaling,
raw/ZFill/projected/no-write paths, missing/full-bone arrays, changing poses,
parent keys and decoded priority/layer heap order. Other-client suites pass.

**Remaining / next acceptance.** Recover the writers/space of native model
+0x98 matrices and their full-bone/billboard/parent evaluation; connect retained
scene poses and model/shared/batch/texture identities without asset-ID proxies.
Verify parent/pose-version, shared/individual and unload/reload histories,
including center bones above the 256-entry GPU palette. Then trace particle
secondary-key callers, `0x81F9E0` additive regrouping and water queue routing/
consumption before live sorted ZFill clones and eligibility reseeding. Matrix
float/double arithmetic is an adapter, not validated x87 numerical parity;
matched native pixels, original binary hash and rendering closure remain open.
Follow the [Roadmap](#roadmap); archival counts retain their snapshot and the
pending port delta above.

## M2 full pose, billboard and retained sort identities (2026-10-01)

**Rule / evidence (32-bit build 12340, DX9 SM3).** Focused live reads of the
existing GUI IDB establish the following contracts. Existing names, types,
prototypes and comments remain hypotheses; no shader re-extraction or bulk
CPU export was required. The original binary SHA-256 remains unpinned.

| Contract | Supporting sites |
| --- | --- |
| Model +0x98 stores evaluated **view-space** bone matrices, directly indexed by authored bone number. The full loop compares against root +0x2C bone count, not 256; parent u16 index is multiplied by 64. Rootless bones start at model +0xF4 (model times view); unanimated bones copy the selected parent, animated bones compose local times parent. The simple path writes +0xF4 to entry zero. Section +0x12 directly selects the sort-center matrix, without a mesh palette lookup. | `0x82F418..0x82F440`, `0x82F837..0x82F846`, `0x82FE41..0x82FF3A`, `0x830292..0x83029D`; simple `0x82E495..0x82E4A2`; consumer `0x822120..0x822144`. |
| Non-root parent flags use exclusive mask-6 modes: 2 normalizes inherited axes then restores root axis lengths; 4 replaces directions with root axes and preserves inherited lengths using sqrt(parent squared length / root squared length), except root squared length <= float 1e-5 uses scale 1; 6 copies root axes. Flag 1 copies root translation, otherwise the authored pivot remains at its pre-adjustment parent position. Decoded flags are ORed with per-bone runtime overrides. | `0x82F837..0x82FBF0`; mode branches/copy `0x82F89E..0x82F8DF`, normalize calls `0x82FAAC..0x82FABC`; override input at the full pose loop's flag selection. |
| Mask 0x78 selects four billboard modes in the evaluated view table. 0x08 swizzles/normalizes animated local rows (y,z,-x), or uses fixed axes (0,0,-1),(1,0,0),(0,1,0) when unanimated. 0x10/0x20/0x40 retain the selected evaluated axis and construct the remaining axes by the recovered XY perpendicular/cross products. All modes retain the pre-adjustment pivot, rescale by evaluated axis lengths, and restore affine components. Full model/view scale therefore affects locked-axis and parent behavior. | `0x82FF3A..0x83029D`; pivot read `0x82FFBC`, spherical branch `0x82FFFB..0x830076`, shared scale/pivot epilogue `0x8301FC..0x830290`. |
| The normalization helper changes a vector only when squared length is **strictly greater** than float bits 0x34800000 (2^-22). Tiny, equal and unordered values pass through. | `0x4C3600..0x4C3642`, especially status/parity gate `0x4C3616..0x4C3623`. |
| Comparator identity fields refer to model instance, shared loaded model, authored batch record and resolved texture handle, not asset IDs. The final batch tie reflects record-address order. | Previously recovered `0x81EFBE..0x81F0C8`, `0x81EAD0..0x81ECF8`; owner/batch dereferences rechecked at `0x81EADA..0x81EB09`; texture handle arithmetic remains `0x47BF23..0x47BF29`. |

**Port.** Shared `M2Loader.ReadAnimation` retains all WotLK MPQ bones, including
parent-only transform flags; the renderer palette cap no longer rejects a valid
CPU skeleton. `M2AnimationPoseCache` keeps full `BoneModelMatrices` alongside the
256-entry byte-indexed GPU `BonePalette`, reuses their storage and versions, and
copies full matrices for frozen poses. `M2Animation` evaluates view-dependent
poses with complete placement/view matrices, applies the native parent modes and
normalization gate, then converts back to model space for existing DX11 consumers.
`M2InstancePacket`/SceneManager and SkyRenderer supply complete transforms and
split root-relative parent poses as well as billboards; ordinary poses/materials
remain shared. The rigid effect-chain evaluator also removes its 256-ancestor cap.

`Wrath335M2RetainedMeshSortAdapter` reads production packet transforms and full
poses, preserves supplied CM2-parent distance/eligibility and resolved shader/
texture inputs, and publishes the existing comparator data. It rejects absent
translucent bones; a WMO placement parent does not imply a CM2 distance parent.
Weak owner tables preserve model/shared/batch identity through packet reorder,
while new loaded arrays/placements get new tokens. `BLPCache.GetCurrentSortIdentity`
uses the resolved uploaded/pending/fallback SRV handle, with retirement before
owned texture release and reset on device/cache teardown. Tokens are aligned
lifetime identities, never FileDataIDs; their ordinal allocation does **not**
reproduce the reference process's heap-address order. These preparation APIs
are ready for the next queue consumer; global GPU submission order is unchanged.

**Verification.** Full smoke exit **0**, **920 tests** (66 Render, 661 DX11,
193 Avalonia); `%TEMP%/wteditor-smoke-m2-retained-pose-20261001.log`.
21 new cases cover real native 501-bone decoding/source disposal; center bone
500 and bounded GPU uploads; pose/version/recycling/frozen/replacement histories;
four billboard modes with scaled/translated view roots and inherited children;
below/equal/above normalization thresholds; exclusive parent modes and rotated
parents with nonuniform scale; same-time camera/placement and WMO-parent motion;
long/noncommuting rigid chains; shared/individual/batch identity, packet reorder,
unload/reload and retired/reused handle generations; explicit CM2-parent key gates
and borrowed resolved shader/texture inputs. Existing 25 metadata/transform,
31 comparator/heap, WARP mesh/state and other-client cases remain green.

**Remaining / next acceptance.** Consume these adapters in scene-wide queue
preparation, resolve actual effect/shader keys and static/no-pose inputs, and
trace particle secondary-key callers and `0x81F9E0` additive regrouping; then
recover mesh/effect/callback water routing/consumption. Preserve all 21 new
histories and add independent additive, interleaved and above/below/straddling/
viewer-liquid fixtures before live sorted ZFill clones and eligibility reseeding.
CM2 attachment transforms/runtime bone overrides, native availability/lifetimes,
singular-root handling, heap-dependent tie reproduction, float/inverse round-trip
versus x87 numerical validation and matched pixels remain open. This batch ports
decoded/default pose contracts, not the complete animation system. Follow the
[Roadmap](#roadmap); archival semantic tables retain their snapshot with this
pending claim/port delta until workstream reconciliation.

## M2 particle keys, additive regrouping and water queues (2026-10-01)

**Rule / evidence.** Build 12340, DX9 SM3; focused reads of the attached GUI IDB.
Existing names/prototypes/comments remain navigation hypotheses. In particular,
the old `0x821930` comment mislabels the emitter secondary key as model-local.
Instructions and caller data flow establish these contracts:

| Rule | Supporting sites |
| --- | --- |
| Particle primary is model +0x88; secondary is squared view-space emitter origin after the authored bone transform, without mesh sign/radius adjustment. Children reuse the parent's secondary. Ribbons use model +0x88 for both keys. | `0x82198A..0x82199C`; emitter origin/bone selection `0x822C46..0x822CA7`, child call `0x822CE3`; ribbons `0x822712/0x82271B`. |
| Base transparent heap runs first. The additive pass requires cache bit 0x80 and more than one admitted resolved-blend-3/10 particle **scene-wide**, including children. Cache bit 0x100 forces particle additive classification but does not change that count. | Count `0x8219A8..0x8219C5`; gate `0x82300E..0x823027`; regroup `0x81F9E0`. |
| Every non-additive element separates groups; consecutive additive entries share a group. Authored mesh/projected/doodad/ribbon blend 3/4 are additive (transparent row `[2,2,2,10,3,4,5]`). Particle runtime +0xD0 is already a resolved Gx blend: 3/10 are additive. Groups sort unsigned ascending; particles precede other kinds and compare signed resolved blend, reconstructed material flags and wrapped texture-pointer difference. Other entries retain the base transparent comparator. | `0x81FA40..0x81FACE`, table `0xA453CC`, conversion `0x81CA20`, comparator `0x81F0E0..0x81F1C2`, flags `0x81CA80`; heap call `0x81FAE7`. |
| Only lighting flags 0x60 refine model selection against a plane. The sphere uses the authored bounds midpoint and radius scaled by the full model-to-view **first** axis. Above is `distance >= -radius`, below is `distance <= radius`, both inclusive; unordered results are false. Crossing models collapse to scene +0x140's side unless cache bit 2 permits both. Particle above selection repeats the sphere predicate independently and does not apply that collapse. | Model `0x821CAB..0x821DDA`, particle `0x822ACF..0x822BBC`; plane fields lighting +0xC4..+0xD0. |
| Normal transparent meshes select each enabled side and require clip bit 2 when crossing. Projected meshes choose below if selected, otherwise above. Ribbons/callbacks choose above or else below. Particles classify opaque using signed resolved blend <=1 and composed alpha >= bits 0x3F7FFF58, then choose above only if selected and runtime emitter +0x134 bit 0x40000 is clear. Opaque bypasses that flag. | Mesh `0x822403..0x82246F`, clip construction in `0x821A20`; ribbon `0x822733..0x82285D`, callback `0x82294D..0x8229E0`, particle `0x8219C8..0x821A16`. |

Lighting selection `0x7C10C0` consumes entity +0x7C bits 0x20/0x40 and +0x80
height: neither underwater bit yields lighting above, underwater-only yields
below, both yield flags 0x60 and world plane `(0,0,1,-height)`. `0x8350A0`
transforms a point on that plane and its normal to view space, normalizing only
above squared length 2^-22 before reconstructing d. Entity flag/height writers
and scene +0x140 provenance remain dependencies for live use. The existing
`0x4F8EA0` frame audit selects below-before-liquid/above-after when viewer liquid
ID is zero and reverses them for nonzero ID; depth is not the selector.

**Port.** `Wrath335M2ElementOrdering` adds emitter/ribbon key helpers and exact
group/comparator/heap policies. `Wrath335M2WaterQueues` implements explicit-input
sphere/plane selection, plane transformation, per-type routing and liquid pass
selection. `Wrath335M2ElementQueues` retains one admitted-element table and
opaque/above/below index lists across frames, counts additive particles globally,
and sorts both transparent queues independently under cache flags. Texture
identity storage is borrowed until sorting completes; additive group fields are
per-sort scratch. Crossing meshes share their source entry. `BlendMode` is the
authored material mode; `ParticleBlendMode` is the resolved runtime Gx blend.
These CPU APIs do not yet change the live renderer's GPU order.

**Verification.** Full smoke exit **0**, **957 tests** (66 Render, 698 DX11,
193 Avalonia); `%TEMP%/wteditor-smoke-m2-additive-water-20261001.log`.
37 new cases cover transformed emitter origins, ribbon keys, separator-preserving
additive blocks, material families, cache/count gates and override independence,
signed blend/material/wrapped texture order, equal-key heap behavior, global
counts across both queues and frame reset; inclusive sphere boundaries, absent/
single flags, unordered planes, bounds midpoint/first-axis radius, crossing
collapse versus particle selection, view-plane normalization, every routing
family and viewer liquid ID. A production packet/full-pose fixture uses bone 500
for both mesh and effect keys in shared queues across two animation frames.

**Remaining / next acceptance (priority order).**

1. Recover actual shader/effect inputs, static/no-pose mesh preparation,
   entity water flags/height and scene +0x140 writers; trace runtime particle
   +0x134 flags and admission/child-emitter lifetimes. The authored particle
   flag 0x40000 means tail and must not substitute for that runtime field.
   Acceptance: live producer fixtures for load/reload, crossing/view changes,
   runtime overrides and admitted versus excluded effects, preserving these
   37 cases and the prior pose/input/comparator coverage.
2. Connect shared live mesh/effect queues to GPU submission with above/below
   clipping and the viewer-liquid frame branch, preserving weather/barrier
   boundaries. Acceptance: interleaved draw traces and crossing pixels on both
   viewer sides, composed alpha boundaries and shared pipeline state restoration.
   Compatible doodad-run aggregation also remains open.
3. Add ordered ZFill clones and model bit 0x40 eligibility reseeding, then shadow
   alpha selectors. Acceptance: independent category/CVar controls, toggle/reload
   and clone/color/depth fixtures. CM2 attachment/runtime bone histories, reference
   allocator ties, x87 numerical captures, original binary hash and matched frame
   parity remain open. Follow the [Roadmap](#roadmap); archival semantic tables
   retain their dated snapshot and this pending delta until reconciliation.

## M2 native mesh preparation and shader selectors (2026-10-01)

**Rule / evidence.** Build 12340, DX9 SM3; focused reads of the same GUI IDB.
Existing IDB names/types/comments remain hypotheses. Two earlier interpretations
are corrected: section +0x0E is the **bone-combo start**, not bone count; the
shader selector's section +0x10 is **bone influences**, not texture count.
Native instruction offsets and the wowlib 0.0.9 section ABI agree (+0x0C count,
+0x0E combo start, +0x10 influences); a contradictory count/index fixture now
verifies the actual decoded comparator input.

| Contract | Supporting sites |
| --- | --- |
| Model runtime bit 0x1000 selects simple animation. Initialization requires exactly one bone and one sequence; bone masks 0x280/0x78 clear; zero model lights, cameras, ribbons, particles and colors. Texture-weight/transform tracks do not disqualify it. The simple writer supplies only view-bone entry zero from full model-to-view. | Initializer `0x834215..0x83425C`; dispatch `0x821AF2..0x821B21`; root write `0x82E495..0x82E4A2`. |
| Opaque mesh fallback compares section +0x0E unsigned. The prior count decode was incorrect even though policy-only comparator tests passed. | `0x81EBAB..0x81EBC2`; owned decoded-section ABI and opposing count/index test. |
| Mesh shader variant index is `lit + 2*(pointLights + 5*(min(influences,2) + 3*min(shadow,2)))`. UNLIT material flag suppresses lights; unlit blend families 5/6 only suppress the lit bit. Point-light count is lighting +0xA4, also the loop bound for view-space light updates. | `0x81F1DC..0x81F22F`, arithmetic `0x81F2DE..0x81F30A`, lit table `0xA45374=[1,1,1,1,1,0,0]`; lighting update `0x8350A0`. |
| Shadow class requires material UNLIT clear, lighting bit 0x10, material 0x100 clear. A nonzero class becomes 1 under lighting bit 8; projected meshes suppress it. Pixel index retains the full class: `shadow + 4*(PCF + 2*shaderAlphaTest)`. Shader alpha testing requires a nonzero alpha class and (caps +0x130 zero or nonzero shadow). Alpha class: opaque 0, key low dword of truncated `composedAlpha*224`, other blends 1. | `0x81F232..0x81F31B`; x87 truncation `0x81F256..0x81F27A`. |
| Variant indices are not comparator identities: the comparator dereferences selected effect-table shader handles. A present effect can have zero entries; a null effect excludes the batch independently of exact shader-ID 0x8000. Other high-bit shader IDs pass that sentinel gate. | Keys `0x81EB43..0x81EB78`; effect creation `0x872DB1/0x872DD0` (90 vertex, 16 pixel entries), destruction `0x873C20`; gates `0x821E97/0x821F97`. |
| Post-query entity +0x7C bits 0x20/0x40 select lighting above/below/crossing. Only crossing writes plane `(0,0,1,-entityHeight)`; unrelated lighting bits and other planes persist. Authored particle bit 0x2000 sets runtime emitter +0x134 bit 0x40000, which forces transparent particles below. Authored 0x40000 (tail) does not supply that runtime flag. | Entity reader/writer `0x7C10C0..0x7C1129`; initializer `0x833D93..0x833D9C`; particle queue reader `0x8219F3..0x821A0A`. |

The doodad query caller `0x7C23F0` supplies entity water flags/height: it uses
terrain liquid queries or group-local liquid queries followed by a world-height
transform, and compares the surface with the definition's upper Z bound. Its
cached bit 0x80 and missing-group retry paths are recovered dependencies, not
ported availability/lifetime contracts. Scene +0x140 provenance remains open.
Water selection consumes authored root bounds/radius (`0x821CBF..0x821D38`),
which must not be replaced by expanded render/culling bounds.

**Port.** Shared decoding retains owned authored `Wrath335M2Bounds`, exact
simple-animation eligibility, bone influences, source shader ID and corrected
bone-combo start; DX11 loading carries those bounds. The retained mesh adapter
uses a stack root identity only for the decoded simple contract and still
rejects higher indices or absent complex poses. `Wrath335M2ShaderSelectors`
implements native variant arithmetic; `Wrath335M2EffectSortTables` resolves
borrowed shader identities without substituting indices. `EntityLighting` and
`InitialParticleWaterFlags` implement the verified producer rules.
`Wrath335M2MeshQueuePreparation.TryAdd` composes decoded data, current packet/
pose, explicit alpha/resource/water inputs, base-material queue classification,
shader-table resolution and current crossing clip state into retained queues.
Absent resources, invisible alpha, sentinel IDs and invalid poses do not append.
This preparation API is not yet a scene-wide producer or GPU consumer.

**Verification.** Full smoke exit **0**, **992 tests** (66 Render, 733 DX11,
193 Avalonia); `%TEMP%/wteditor-smoke-m2-native-preparation-20261001.log`.
35 new cases cover native source mutation/disposal, exact simple gates, authored
bounds versus expanded culling data and DX11 transfer, contradictory count/index
order, influence clamps, shadow/UNLIT/projected/PCF/capability/alpha branches,
resolved shader sharing/zero/missing entries, composed layered alpha, simple
root-only versus missing complex/high-bone inputs, placement/water changes,
recomputed clip flags, entity lighting/plane preservation and authored/runtime
particle flag separation. Prior mesh/effect queue, pose and other-client cases pass.

**Remaining / next acceptance (priority order).**

1. Supply owned live effect tables and resolved texture identities from actual
   effect/program resources, including substitutions and cache/device reloads;
   connect scene-wide mesh preparation and effect admission/children. Acceptance:
   shared-versus-distinct resource keys, missing/fallback programs and load/reload
   histories, preserving the 35 preparation and prior queue/pose fixtures.
2. Implement terrain/group liquid queries and entity cache invalidation/retry;
   recover scene +0x140 and viewer-liquid writers. Acceptance: crossed surfaces,
   transformed WMO placements, missing/loaded groups and repeated frames with
   fresh flags/height/plane. Post-query lighting policy alone does not close this.
3. Connect interleaved GPU queues, water clipping and viewer-liquid scheduling,
   preserving weather/barriers and shared state; then ordered ZFill/reseeding.
   Acceptance: draw traces, both-side crossing pixels, resource/state restoration,
   CVar/category and toggle/reload fixtures. Attachment/runtime bone overrides,
   allocator ties, x87 numerical and shader formula captures, original hash and
   matched frame parity remain open. Follow the [Roadmap](#roadmap); archival
   tables retain their snapshot with these pending deltas and superseded claims
   until milestone reconciliation.

## WMO transition colors, lighting and fog (2026-10-01)

**Rule / evidence.** Build 12340, DX9 SM3. Addresses and instruction/data flow
are authoritative; pre-existing IDB names/types/comments are hypotheses.

- Root MOHD flag 1 sets group runtime bit 2 at `0x7D8421`/`0x7D8426`;
  `0x7ABF87`/`0x7ABF8C` then skips transition attenuation. Root flag 8 gates
  color fix-up at `0x7D7D10`/`0x7D7D14`. These are independent controls.
  Fix-up `0x7D7380` and attenuation `0x7D78C0` use the final transition
  MOBA's inclusive vertex endpoint, not the maximum endpoint across batches.
  Fix-up halves transition RGB without replacing alpha; non-transition RGB
  uses `(value + ((alpha * value) >> 6)) >> 1`, clamped to 255, with alpha 255.
- Attenuation projects along the source portal normal through `0x982FB0`,
  retaining the original point within 0.01 plane tolerance; otherwise the
  projection divides by N dot N. `0x9829B0`/`0x9830D0` use major-axis odd/even
  crossings, including one boundary on the projected vertical axis and
  excluding its opposite.
  Outside projections use closest 3D edge distance (`0x984DB0`). Destination
  MOGI mask 0x48 contributes `1 - 0.15 * max(distance, 0)` when >0.001;
  an interior destination with -1<distance<1 overrides accumulated weight to
  zero. Weight is clamped to 1, RGB blends toward byte 127, and alpha is
  truncated from weight*255 (`0x7D7B96`–`0x7D7C06`). Portal fog distance
  `0x984E50` shares this projection/crossing rule and returns absolute source
  plane evaluation inside (`0x984F3B`), without dividing by normal length.
- Shader-enabled unified transitions in `0x7A9380` install staged fog and
  lighting mode 1 (or material unlit/window mode) for state 9, SrcAlpha/Zero
  (`0x7A9656`, `0x7A968A`, `0x7A96D2`). They then install mode 3 and the
  propagated fog bank for state 7, InvSrcAlpha/One (`0x7A96D7`, `0x7A9745`,
  `0x7A977D`). `0x6A4C30` and tables `0xA2F964`/`0xA2F994` support these
  blend factors. Non-unified groups without primary MOCV dispatch to the
  single-pass exterior renderer (`0x7ABF50` -> `0x7AC6A0`).
  The non-unified/no-MOCV dispatch also selects Unlit mode 0 or outdoor mode 1
  before interpreting batch category, and ignores the material Window flag.
  Unified opaque MOGP flags&0x48 explicitly force staged fog at `0x7A9B02`, even if a node
  carries propagation. Transition material Unfogged remains honored.
- The effect selector at `0x7AFFD2` binds MapObjUOpaque. Extracted
  `shaders/effects/MapObjU.wfx` (cached source hash
  `5cf2804603f5e2020b5cb709e90422f4aa0e4ad991dd25cf15539140f58adffb`)
  pairs MapObjUDiffuse_T1 with MapObjOpaque. The diffuse effect pairs the
  same vertex program with MapObjDiffuse. Matching SM3 evidence is
  [unified VS ordinals 0/1](../../../artifacts/client-335-shaders/programs/vertex/vs_3_0/MapObjUDiffuse_T1.bls.md),
  [opaque PS ordinal 0](../../../artifacts/client-335-shaders/programs/pixel/ps_3_0/MapObjOpaque.bls.md)
  and [diffuse PS ordinal 0](../../../artifacts/client-335-shaders/programs/pixel/ps_3_0/MapObjDiffuse.bls.md).
  `0x7A8940`/`0x7A896B` uploads packed material diffuse 0xFF7F7F7F to c28.
  Lit unified RGB is `saturate(MOCV + (127/255)*saturate(ambient +
  diffuse*Lambert) + c29)`; mode 3 uses MOHD ambient and zero directional
  light, while unlit copies MOCV. The root ambient writer at `0x7D7EC0`–
  `0x7D7EC9` copies header+0x1C into root+0x1A0, independently confirming
  the field read by mode 3. PS RGB is 2*texture*vertex-light followed by fog;
  opaque alpha is vertex alpha, diffuse alpha also includes texture alpha.
  `0x873210` and the VS support the existing view-Z fog transfer. The two
  banks share distances/rate (`0x7F19D4`/`0x7F19E6`/`0x7F19F4`) but keep
  distinct colors. Shadow receiver permutations remain unported.
- The camera fog query `0x7A11BE`–`0x7A11F9` visits retained viewer groups,
  sets interior eligibility only for MOGP flags&0x48==0 (`0x7A11CB` /
  `0x7A11D6`), and minimizes exterior-portal distance across eligible groups.
  With no eligible group, `0x7A121A` forces portal weight to zero. Fog volume
  IDs still come from the first group at `0x7A12B0`, even if the second group
  supplied eligibility. The former live port could activate the base white
  MFOG for an exterior-lit viewer and only searched the first group's portals.
  This is a demonstrated logic mismatch. The later live capture attributes
  the supplied screenshot's wash to final presentation alpha instead.

**Port.** `WMOLoader` uses the final transition endpoint and the shared
`Wrath335PortalPolygon` projection/crossing helper for 12340 attenuation.
The viewer query reuses its established crossing rule; portal fog distance now
uses it too. `WmoMaterialPolicy`/`SceneManager` apply the recovered pass-count
and forced-staged exception, and `wmo.hlsl` uses 127/255 for 12340's c28.
The DX11 WMO loader supplies the version gate for the no-MOCV lighting selector.
`Wrath335WmoInteriorFogQuery` now gates interior fog by strict MOGP interiors
and searches both retained viewer groups while sampling the first group's IDs.
New behavior is gated by `wrath335`; other legacy/modern inputs retain their
prior behavior. Root flag bypasses and authored alpha remain intact.

**Verification.** Full required smoke exit **0**, **744 tests**: 66 Render,
485 DX11, 193 Avalonia. Twenty-two new behavioral cases cover independent root
flags, Stormwind flags 15, concave polygons, asymmetric boundaries, non-unit
normals, 0.01 projection tolerance, nearby-interior override, pass dispatch,
forced staged fog and other-client preservation. Query fixtures cover exterior/
exterior-lit rejection, MOGP/MOGI separation, secondary interior eligibility,
first-group fog IDs, nearest portal across both groups, single-record bypass
and deep interiors. Two live-shader WARP cases
exercise diffuse/opaque transitions at alpha 0, 73/255, 254/255 and 1, with
distinct light/fog banks, fogged/unfogged output, depth writes and LessEqual
second-pass acceptance. Log: `%TEMP%/wteditor-smoke-wmo-transitions-20261001.log`.

**Uncertainty / next acceptance at this batch.** Stormwind flags 15 skip both
color rewrites; these attenuation corrections did not explain the gray layer.
The editor had closed before this batch's input inspection. The subsequent
[presentation audit](#wmo-transition-alpha-and-opaque-gui-presentation-2026-10-01)
records the reopened live scene and demonstrated cause. Native loaded-target
lifetimes, shadows, exact x87/ray reconstruction and matched native pixels
remain open in the [Roadmap](#roadmap).

## WMO transition alpha and opaque GUI presentation (2026-10-01)

**Rule / evidence.** Build 12340, DX9 SM3; names remain unverified navigation
hints. Full control-flow inspection of `0x7A9380` confirms that both SM3
transition draws (`0x7A96D2` / `0x7A977D`) use the same RGBA vertex stream.
The extra stream at `0x7C9D20`, allocated through `0x7CBCB0`, belongs to the
fixed-function fallback, not SM3. Lighting selection at `0x7A8B10` supplies
outdoor mode 1, the window bank in mode 2, and MOHD ambient/zero directional
light in mode 3. The cached unified VS and diffuse/opaque PS preserve MOCV
alpha (diffuse also multiplies texture alpha). State 9 SrcAlpha/Zero followed
by state 7 InvSrcAlpha/One therefore leaves target alpha
`a*a + a*(1-a) = a`, with correct final RGB. Alpha is an internal transition
weight, not opacity of the completed world image.

Read-only inspection of the user's open Release process confirms Client mode
at noon 1440, group 115 of placement 10047, fog enabled and screen glow
disabled. Saved Editor settings enable glow. The configured Client-Noggit
Stormwind root has flags 15 and SHA-256
`C94A0183F1046FC33B0E579C0863A1320C85229B24DD317DA5672C2AD5D7C6D6`;
both color rewrites are bypassed. A WARP scene using these assets, live camera
`(-8947.721,797.8962,95.7058)`, direction
`(0.7229558,-0.652067,-0.22834949)` and far clip 791.6667 reproduces 255
Client scene draws. Its warm RGB is intact, but 295,633 of 451,200 pixels
have alpha below 255. For example, floor RGBA is `(72,38,12,15)` and a beam
is `(95,56,27,70)`. Avalonia imports this as BGRA with alpha; that alpha
allows the UI background into the viewport. The glow composite writes alpha 1,
explaining why the saved Editor mode hides the fault. Fog-disabled scene
readback retains the same affected RGB/alpha.

**Port.** `OpaquePresentationRenderer` and `presentation_opaque.hlsl` write
only alpha after the completed scene in `WowViewerEngine.RenderTo`, before
external-image presentation. Internal targets, native WMO blend factors,
authored alpha, lighting and fog remain intact. The pass owns its GPU states,
binds explicit fullscreen/depth-disabled state, participates in shader reload
and adds one draw to frame statistics. This opaque viewport contract applies
to all externally presented client versions; WMO-specific rules stay versioned.

**Verification.** Full required smoke exit **0**, **746 tests**: 66 Render,
487 DX11, 193 Avalonia. Two new WARP cases verify exact RGB preservation and
opaque alpha in BGRA8 and float targets, including RGB above 1 and inherited
clipping/depth states. Both existing diffuse/opaque transition shader cases now
verify native final alpha and the presentation result across four authored
weights and fog on/off. Log:
`%TEMP%/wteditor-smoke-opaque-presentation-20261001.log`. The captured full
Stormwind scene rendered through the production presentation pass changes
**zero RGB pixels** and has **zero nonopaque pixels** afterward. Temporary
diagnostic/readback outputs are in `%TEMP%/wteditor-stormwind-render`.

**Uncertainty / next task.** The cause and GPU correction are demonstrated;
accept the fix in the user's rebuilt GUI with both modes/glow settings and
matched reference lighting pixels. `UnifiedRender` / `SetLightingMode` are
still partial ports: local-light counts/permutations/constants, shadow
receiving, other material families and fixed-function fallbacks remain.
The original executable hash, streaming lifetimes and full-frame parity remain
open. Follow the [Roadmap](#roadmap); no archival inventory reconciliation or
full-function closure is claimed by this batch.

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

## DayNight fog-rate IDB annotations (2026-10-01)

**Rule / evidence.** Live 32-bit 3.3.5a build 12340 function `0x7ECD00`
receives two stack `float` arguments: absolute fog start and end distances.
Calls `0x7ECDCE`, `0x7ED214` and `0x7ED8D6` confirm the argument order and
float result stores for outdoor, underwater and override fog respectively.
The routine compares `end-start` with `min(farClip,700)-200`; it returns 1.5
when the fog span exceeds that reference span, otherwise
`(1-fogSpan/referenceSpan)*5.5+1.5`. Guard `0xD39044` bit 0 is tested at
`0x7ECD08` and set at `0x7ECD16`; the guarded float scale at `0xD39040`
is written at `0x7ECD1C` and read at `0x7ECD68`. The float far-clip global
at `0xD38B40` is read at `0x7ECD2A` and written at `0x7F2833`.

**Annotation / port location.** Retained the descriptive function identifier
`DayNight__CalcFogRate`; it is not an authenticated original C++ symbol.
Named arguments `fogStartDistance` / `fogEndDistance` and temporaries
`cappedFarClipDistance`, `fogRateReferenceSpan`, `fogDistanceSpan`.
Applied `float` to the far-clip and scale globals, and `unsigned int` to
`g_dnFogRateScaleInitGuard` (formerly `dword_D39044`). Retained the existing
`double` return and x87 temporary types: ST(0) return and float caller stores
do not identify the original source return width. The corresponding existing
port is `Wrath335OutdoorFogEvaluator.CalculateExpansionRate` in
[Wrath335OutdoorFog.cs](../../../WoWRenderLib/Structs/Wrath335OutdoorFog.cs);
this batch changes IDB annotations and documentation only.

**Verification.** Invalidated the four relevant decompiler cache entries
without closing views, then re-decompiled the target and all three callers.
Argument/local names persisted and caller expressions reflected the updated
prototype; global types matched their four-byte accesses. Function bytes
were unchanged. Saved `IDA-WoW-Wrath-2026.08.06.i64` through the GUI MCP lease.
Before/after snapshots are retained under `%USERPROFILE%/.codex/tmp/` as
`DayNight__CalcFogRate-before-20261001.json` and
`DayNight__CalcFogRate-after-20261001.json`. No repository code/test batch was
edited, so no smoke rerun was required.

**Uncertainty / next checks, in order.** (1) Resolve original float-versus-double
return provenance only if required by a numerical-parity question; acceptance
requires independent type evidence, not the existing IDB label or ST(0) alone.
(2) For numerical parity, compare native x87 results and float stores with the
existing evaluator at span boundaries, degenerate denominators and unordered
inputs; this routine contains no finite-input or positive-denominator guard.
No new runtime parity or original-binary hash claim is made. Snapshot inventory
delta: this address's names/types were reviewed; exhaustive CSV/claim tables
remain at their archival snapshot until the next workstream reconciliation.

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
finish neighboring WMO entity links and native cache/availability histories;
connect the retained common mesh/effect/water queues to GPU submission;
then close terrain/WMO/detail/shadow preparation and
their CPU-to-SM3 upload/selector contracts. Keep SceneManager an orchestrator
under [WoWRenderLib.DX11/AGENTS.md](../../../WoWRenderLib.DX11/AGENTS.md).

## Verification

The latest liquid batch passed the full repository smoke runner with exit 0:
**66 Render + 793 DX11 + 193 Avalonia = 1,052 tests**, zero failures/skips,
including 14 new entity/mesh cases, two of them WARP fixtures.
Earlier batches retain their verification totals in their entries above.
Log: C:\Users\Titi\AppData\Local\Temp\wteditor-smoke-liquid-entity-water-20261002.log.
This verifies the implementation and synthetic cases. Whole-client rendering
accuracy remains gated by the extended plan's complete closure and matched
CPU/GPU/frame captures.

## Basic liquid shaders, gradients and native inputs (2026-10-02)

**Rule / evidence.** Read-only live IDA inspection of 32-bit 3.3.5a build 12340,
using the existing GUI database. All IDB names/prototypes/comments remain
navigation hypotheses; addresses, instructions, callers and cached SM3 programs
ground the contracts below. No shader re-extraction or CPU inventory dump.

- `0x8A1FA0`, instructions `0x8A2048..0x8A209F`: LiquidType record +0x38
  selects material 1 water (specular gate chooses its shader pair), material 2
  magma, material 3 procedural water; other material IDs do not create a factory.
  Settings at `0x8A27C0` copy six filenames, Int[0..3] and Float[0..17];
  `0x8A16E6` reads Float[index] at settings +0x318. The port is scoped to MPQ
  3.3.5.12340 and retains settings/slot roles separately from later-client families.
- `0x8A2450` expands frame templates 1..30. Its helper at `0x76F770` is a
  substring search despite the guessed `SStrCmp` name. `0x8A1D60` waits for
  animated-slot residency, then selects from unsigned 32-bit async milliseconds.
  Water slot 0 uses Int[1]; slot 1 and magma slot 0 use 1250 ms; zero period
  becomes one. `0x8A34B0` scrolls magma UV independently per axis using
  `t % uint(trunc(1000/speed)) / period`; the world setup at `0x7997E7..0x79980E`
  supplies the global UV scale 1. Malformed zero-divisor scroll is guarded in the
  port rather than reproducing the native divide fault.
- `0x79B870` admits depth tables only for LiquidMaterial LVF 0/2 and selects
  Int[0]; initialization at `0x79E3C0` establishes min(byte/42,1) and byte/255.
  Terrain emission `0x7CE390` uses white vertex color, flat +Z normal, generated
  XY*0.06 or **unsigned** UV words*3/256 (`0x7CE4B7..0x7CE4BB`). WMO emission
  `0x7A7B00` uses **signed** words/256 (`0x7A7BCB..0x7A7BD5`) or translation-free
  transformed local XY*0.24. `0x793F0C..0x793F44` retains MOMT diffuse alpha
  indoors, white color outdoors and gradient U 1/0 respectively. A missing indoor
  material retains factory white; the DBC-type root flag does not remove its tint.
- Generators `0x8A2BF0` and `0x8A2AC0`, creation `0x8A2E20`: 8x64 BGRA.
  RGB/alpha ramps use byte endpoints and 8.8 fixed steps, row/64, not row/63.
  WMO columns 0..3 hold **river far** RGB at every depth; columns 4..7 are white.
  Ocean's final row darkens HSV value by .9; assignment at `0x9851A9` also
  overwrites alpha with 255. Textures use retained storage and update on palette
  changes. Full x87/HSV rounding captures and sampler/mip lifecycle remain open.
- Draws `0x8A5590` / `0x8A5900` bind gradient to sampler 0 and surface to
  sampler 1, with Float[0]/[1] surface scale/rotation and Float[2] depth scale.
  The DX11 bindings reverse the register numbering while preserving the roles.
  Water is double-sided, source-alpha blended, LEQUAL and depth-read-only on
  clip-capable shader hardware. The material-2 vtable at `0xA595A4+8` resolves
  to `0x8A6090`: one-texture PCT magma, unlit vertex RGB and output alpha one.
  Its native draw inherits blend/depth state; the destination uses opaque/write
  state. Complete native inherited-state/frame closure remains required.
- Specular publisher `0x8A38B0` takes CM2Lighting +0x6C and exponent 6. The
  sun writer `0x781967..0x7819AA` consumes packed DayNight +0xF8; band-9 fill
  at `0x7EBFF0` and index-preserving publish `0x7ED910` establish the source.
  `0x834F60` adds directional specular. The fixed indoor light's constructor
  `0x834A40` leaves it zero; environment `0x7D4F40` writes only white diffuse.
  Liquid specular therefore does not use the WMO helper's invented night ramp.

**Cached formula witnesses (ordinal 0 / no local lights).** Let P be lit vertex
color, G the depth-gradient sample, S the surface sample, H the VS Blinn term
`specRGB * pow(max(N.H,0),6)`, and f the existing fog visibility.

| Program | RGB before fog / alpha | Cached program evidence |
| --- | --- | --- |
| WaterNoSpec | P.rgb*G.rgb + S.rgb / P.a*G.a | [VS](../../../artifacts/client-335-shaders/programs/vertex/vs_3_0/vsLiquidWaterNoSpec.bls.md), [PS](../../../artifacts/client-335-shaders/programs/pixel/ps_3_0/psLiquidWaterNoSpec.bls.md) |
| Water | P.rgb*G.rgb + S.rgb + S.a*(H+.25) / P.a*G.a | [VS](../../../artifacts/client-335-shaders/programs/vertex/vs_3_0/vsLiquidWater.bls.md), [PS](../../../artifacts/client-335-shaders/programs/pixel/ps_3_0/psLiquidWater.bls.md) |
| Magma | vertex.rgb*S.rgb / 1 | [VS](../../../artifacts/client-335-shaders/programs/vertex/vs_3_0/vsLiquidMagma.bls.md), [PS](../../../artifacts/client-335-shaders/programs/pixel/ps_3_0/psLiquidMagma.bls.md) |

All three apply `fogRGB + f*(RGB-fogRGB)` and preserve shader alpha. Water VS
applies ambient + diffuse*saturate(N.L) before interpolation, without normalizing
the transformed normal. Specular is also evaluated per vertex. Surface alpha
affects only the additive glint, never water coverage. The old luminance wave,
invented drift, cubic depth tint and emissive boost are bypassed for these native
programs. Material 3 still reaches the prior approximation; it is not implemented.

**Port / verification.** Shared `WorldLiquidMaterialCatalog`, MH2O/WMO mesh
builders and `WMOLoader` retain native inputs. DX11 `Wrath335Liquid`,
`Wrath335LiquidTextures`, `WorldLiquidRenderer` and `Shaders/liquid.hlsl` consume
them in live scene draws; Settings.Specular reaches material selection.
`Wrath335LiquidTests` adds 29 cases, including 13 WARP readbacks covering basic
programs, exponent-six highlight interpolation, indoor alpha, fog, depth sampling,
gradient update/view reuse, state cleanup and other-client fallback pixels.
Two source-text assertions for the old approximation were removed. Full smoke
exit **0**, **66 Render + 760 DX11 + 193 Avalonia = 1,019**, no failures/skips.
Log: `C:\Users\Titi\AppData\Local\Temp\wteditor-smoke-liquid-native-basic-20261002.log`.

**Remaining / next tasks, in priority order.**

1. **Superseded by the following queue/query batch:** connect list 0/1 and retained liquid identities to the frame graph, then
   terrain/group liquid queries, cache retries and viewer-plane transitions.
   `0x8A20CD..0x8A20E4` selects the list from settings +0x360 (LiquidMaterial
   flags bit 0). `0x8A22A9` qsorts four-byte entries; `0x8A1989..0x8A1998`
   compares their stored pointer before any subsequent words. Unique instance
   pointers therefore sort by address, not material or camera depth. Duplicate
   suppression is instance +0x64. At that shader batch's completion the destination
   still had its single pass/depth sort; allocator order remains unvalidated. Acceptance: queue traces,
   list flag fixtures, retained/reload histories, above/below/straddling effects,
   enter/exit water and pipeline restoration pixels.
2. Port material 3's six-texture procedural resources, wave clocks/geometry,
   ripples and underwater fog/particulate, then MCLQ admission/geometry. Acceptance:
   each selector/formula/resource/state has CPU/BLS evidence and GPU fixtures.
3. Finish portal-dependent WMO color/environment, local-light permutations,
   gradient/sampler/mip precision, and decoded topology/culling/availability.
   Compare real terrain/WMO water, magma/slime and underwater at matched camera,
   time and settings. No matched client capture or complete liquid parity yet.

Archival inventories retain their **2026-09-30 snapshot**; these liquid contracts
are pending semantic/call-site deltas for the workstream reconciliation. The
original binary hash remains unpinned. Names do not establish rendering closure.

## Liquid lists, retained identities and viewer grid query (2026-10-02)

**Rule / evidence.** Focused read-only IDA Local MCP inspection of build 12340;
the resumed lease was attached to the matching GUI database. Existing names,
types, prototypes and comments remain navigation hypotheses. Instructions,
callers and field data flow support the following contracts; no cache extraction
or complete CPU export was needed.

- List admission at `0x8A20CD..0x8A20E4` reads settings +0x360, the copied
  LiquidMaterial flags bit 0. Instance +0x64 suppresses duplicate submission.
  Four-byte entries at `0x8A22A9` sort by their stored unsigned instance pointer
  first (`0x8A1989..0x8A1998`); unique instances do not reach the later comparator
  words. List 0 flush in `0x79A870` precedes outer opaque M2; list 1 flush is at
  `0x790A80`. The renderer now prepares once, partitions by that material flag,
  consumes each flush and retains placement/batch-generation tokens. Shared WMO
  geometry has a distinct identity per placement. Tokens preserve lifetimes,
  **not reference allocator address order**. Other clients retain their late
  depth ordering. GPU timing sums both liquid intervals and excludes them from M2.
- Viewer writer `0x790920`, instructions `0x79093E..0x7909C2`, resets type/depth
  every frame. A selected WMO uses only the primary group, queried in placement
  local coordinates, and stores local surface-minus-eye depth. Group absence or
  query failure does not fall back to terrain. Without a primary WMO,
  `0x7A0B00` queries eligible map objects first, then terrain with floor checking.
  The viewer ID returned at `0x780632`, rather than depth sign, chooses the outer
  `0x4F8EA0` water-pass branch. A small negative depth can accompany a nonzero ID.
- Terrain query `0x7A0820` uses swapped/decreasing XY, the native 17066.666015625
  origin and .23999999463558197 inverse step. Coordinate spills and
  `FISTP(coord-.5)` occur at `0x7A0843..0x7A0868`; the port models normal
  nearest/ties-to-even rounding. Chunk/layer admission follows the rectangle and
  exists mask (`0x7CE1F0`). Height is bilinear over four grid corners
  (`0x7CE0B0`, arithmetic `0x7CE141..0x7CE16C`), independently of draw triangles.
  The first qualifying layer wins, with strict `surface+.01 > eyeZ` and, for
  the viewer, `eyeZ+.01 > terrainHeight`. The current floor producer uses the
  existing CPU terrain raycaster; complete native floor/edge precision remains open.
- WMO query `0x7C8360` floors local XY at inverse step .24000000953674316,
  rejects tile low nibble 15 and interpolates the original grid before portal
  draw clipping. LiquidType flags bit 4 alone enables the .01 tolerance;
  otherwise surface must be strictly above the eye. Zero tile dimensions with
  a nonzero group liquid type return FLT_MAX (`0x7C84E6..0x7C84F7`). Query-only
  decoded grids survive GPU upload without geometry. Group +0x144 remains the
  query type: getter `0x431F30` feeds the draw setup, but indoor remap
  `0x793E77..0x793E9F` changes only its local material selector to 17. Query type
  and flags therefore remain original even when the draw material changes.
- Exterior query `0x7A09D0` excludes placement flag 0x100 and checks inclusive
  placement/model bounds. `0x7AEB90` visits loaded groups in order, excluding
  MOGI flags 0x2000 and requiring the point in MOGI bounds. The queried local
  surface is transformed to world Z. Helper `0x4C2300` writes its input point
  **in place** at `0x4C2353..0x4C235A`; `0x7A0AE5` reads that transformed Z.
  The unused output copy in pseudocode does not imply an untransformed height.

**Port / verification.** Shared MH2O/WMO builders retain `WorldLiquidQueryGrid`
separately from geometry; `WorldLiquidLoader` preserves query-only payloads.
`Wrath335LiquidInstances`, `WorldLiquidBatchOrdering`, `WorldLiquidRenderer` and
SceneManager connect the two draw lists. `Wrath335LiquidQuery` and
`WorldLiquidViewerQuery` supply live `SceneManager.ViewerLiquid` from loaded
sources regardless of liquid draw culling; missing data is retried each frame.
No entity +0x7C cache or underwater rendering consumer is implied by this producer.
The 19 new cases cover list flags/unsigned ordering, retained generations,
bilinear heights, masks/offsets/floor tests, tolerance/crossings, primary-group
retry, local/world depth, original type versus material remap, query-only upload,
reload/allocation behavior and other-client scoping. Three WARP cases exercise
production preparation/flushes, duplicate suppression, two shared-geometry WMO
placements, composed pixels and texture cleanup. Full smoke exit **0**:
**66 Render + 779 DX11 + 193 Avalonia = 1,038**, no failures/skips. Log:
`C:\Users\Titi\AppData\Local\Temp\wteditor-smoke-liquid-queues-viewer-20261002.log`.

**Remaining / next tasks, in priority order.**

1. **Advanced by the following entity/mesh batch:** recover/port entity liquid cache invalidation, linked terrain/group query
   ownership and missing-group retry, plus scene +0x140 and split-cache writers.
   Connect live M2 above/below queues around list 1 using viewer ID, with crossing
   plane clipping. Acceptance: moved/reloaded/temporarily missing entities, camera
   enter/exit and straddling mesh/ribbon/particle GPU traces/pixels/state restoration.
2. Port material 3's six-texture resources, wave geometry/clocks, ripples and
   underwater fog/particulate; then MCLQ admission/geometry. Acceptance: recovered
   selectors/formulas/resource lifetimes and rendered fixtures for each branch.
3. Close query floor/coordinate edges and native FP rounding, absent-MLIQ/group
   type initialization and partial streaming, reference allocator ordering,
   portal-dependent WMO color/environment, local lights, sampler/mip/state
   precision and matched terrain/WMO/underwater captures. Smoke success is not
   full rendering parity. Follow the [Roadmap](#roadmap).

Archival tables retain the **2026-09-30 snapshot** with this queue/query delta
pending workstream reconciliation. Original executable SHA-256 and full closure
remain unpinned/open; no archival counts are presented as live closure.

## Entity liquid cache, scene history and mesh water clipping (2026-10-02)

**Rule / evidence.** Read-only IDA Local MCP inspection of the matching 12340
GUI database; names/types/comments remain unverified navigation hypotheses.
Instructions and field/caller data flow establish these roles:

- Virtual query slot `0xA40320` points to `0x7C23F0`. Entity +0x7C bit 0x80
  bypasses querying. A new query clears 0x60 and sets validity
  (`0x7C2404..0x7C240C`), retaining unrelated bits. Nodes are visited in linked
  order. An absent WMO group clears validity and continues (`0x7C246B`); a later
  hit or terrain node completes the cache. Available-group misses remain cached
  when no missing group was encountered. The first terrain node performs the
  global terrain query **without floor checking** and ends traversal on hit or
  miss (`0x7C2550..0x7C25B1`). A hit sets 0x20; surface <= authored world maximum Z
  sets 0x40, inclusively (`0x7C2508..0x7C2538`, `0x7C2599..0x7C25AE`). Only the
  terrain hit clears bit 8 and resets +0x8C to 1 (`0x7C2586..0x7C259C`). The
  existing `0x7C10C0` lighting policy then supplies above/below/crossing and plane.
- The sample is placement origin X/Y (+0x6C/+0x70) and transformed authored
  bounds minimum Z (+0x50), not the sphere center. Transform writer `0x7B5870`
  stores matrix translation; loaded bound preparation `0x7BDB10` transforms the
  authored model box. WMO sampling uses inverse placement, then transforms the
  local queried surface back to world (`0x7C2492..0x7C2505`). MODR owner creation
  `0x7BF7EC` calls `0x7C0750`, which inserts each owner link at the entity list
  head. Doodad re-enable explicitly clears 0xE0 at `0x7B7011` before relinking.
  Editor move/reload invalidation uses this mask as a host adaptation; the
  complete native spatial-link/invalidation histories are **not** established.
- Scene construction initializes +0x140 to zero (`0x821828`). Update caller
  `0x4FAADF` reaches `0x7831A0`; its final `0x7834E5..0x7834F3` copies global
  viewer ID `0xCD8794` into scene +0x140. Render query `0x79A888 -> 0x790920`
  refreshes that global later. Thus crossing collapse uses the retained
  update-time ID, while the outer pass-order branch `0x4F915B..0x4F91B9` uses
  the refreshed ID. Neither branch uses depth sign. Cache flag 2 comes from
  `M2UseClipPlanes` (registered default 1, `0x402760`); instructions
  `0x4028B5..0x4028C1` set bit 2 before `0x4048EB -> 0x81C6E0` initializes the cache.
- Mesh clipping is enabled by element +8 bit 2. Draw lighting state reads the
  plane at lighting +0xC4, optionally transforms it for the device API, negates
  **all four** components for M2 pass 2, and enables clip slot 0
  (`0x81FD5E..0x81FE41`). An unclipped following element disables the mask
  (`0x81FE4D..0x81FE74`). The DX11 translation computes `SV_ClipDistance0`
  from the signed world plane **after skinning and placement**; a neutral stream
  emits distance 1. Opaque meshes retain the common pass.

**Port / verification.** `Wrath335EntityLiquidCache` accepts explicit ordered
terrain/group nodes and preserves hit/miss/retry state. `WorldLiquidEntityQuery`
retains placement caches and packet output arrays, reads loaded CPU grids outside
draw culling, and invalidates on editor transforms/authored bounds, owner/model
generations and terrain source changes. It currently supplies terrain links for
standalone models and parent MODR links for WMO doodads. `Wrath335LiquidSceneState`
retains the two viewer histories. `M2MeshRenderer` compacts selected instances
into its retained upload scratch and supplies per-instance clip planes.
SceneManager draws the selected transparent mesh half before list 1 and the
other half after it; other-client fallback meshes still draw once. The default
native clip setting is connected; configurable clip-off behavior remains open.

Fourteen new cases cover cached hits/misses, first-link/terrain-stop order,
inclusive maximum, transformed WMO surfaces, terrain lighting reset,
missing-group retry, source reload/unload/move, MODR head order independently of
editor group visibility, authored versus expanded bounds, and viewer ID history.
Two WARP fixtures (1 and 1,025 placements) exercise both clipped halves, moving
skin vertices, sparse partition uploads, actual liquid/mesh composition with
both viewer branches, fallback single submission and post-pass state/clip reset.
Full repository smoke exit **0**: **66 Render + 793 DX11 + 193 Avalonia = 1,052**,
zero failures/skips. Log:
`C:\Users\Titi\AppData\Local\Temp\wteditor-smoke-liquid-entity-water-20261002.log`.

**Remaining / next tasks, in priority order.**

1. Recover/port neighboring WMO spatial links, terrain-versus-WMO link admission,
   native partial-load/enable/transform invalidation histories and group load
   order; retain the explicit retry policy. Acceptance: overlapping placements,
   raycast-linked standalone entities, async group arrival, re-enable and moved
   owners compared with native ordered link/query traces. Preserve these 14 cases.
2. Feed meshes/ribbons/particles into the shared sorted GPU element queues with
   owned live effect/texture keys, additive regrouping and per-type water routing.
   Acceptance: overlapping mesh/effect pixels on both sides and during viewer
   entry/exit, projected/forced-below cases, clip-off CVar history, ordering and
   state restoration across liquid/weather/barrier boundaries. Current mesh
   partitions do **not** establish complete native transparent sorting.
3. Port six-texture procedural material 3, waves, ripples, underwater
   fog/particulate and MCLQ. Acceptance: recovered selectors/resources/clocks,
   lifetime and rendered fixtures for each family; matched fixed camera/time
   terrain/WMO water, magma/slime and underwater captures.
4. Close query floor/edges/FP precision, absent MLIQ/group type initialization,
   reference allocator order, portal-dependent WMO color and local lights.
   The original executable hash and complete rendering closure remain required
   before sign-off. Follow the [Roadmap](#roadmap); smoke success is not parity.

Archival exhaustive tables retain the **2026-09-30 snapshot**; this entity/mesh
delta is pending workstream reconciliation, with no claim of live inventory closure.
