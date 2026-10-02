# WTEditor rendering feature status

Last updated: 2026-10-02. This is the working rendering ledger for future
client-version work. Update the version matrix, open issues, and next steps
when a fix lands or a visual comparison changes their status. WowLib remains
the only client-file reader and parser; Wisp is a rendering reference for 3.3.5.

This file is the maintained implementation ledger for the active Avalonia/DX11
editor. Update it whenever a rendering feature changes status. “Implemented”
means the feature is connected to live editor rendering and covered by the
repository smoke suite; it does not imply parity with every WoW client era.

## Roadmap

Active work is the liquid replacement. Basic MPQ 3.3.5.12340 water/specular-water
and magma now use recovered SM3 formulas, native animated slots, generated
gradients, depth/UV inputs and indoor material alpha. Material-flag list 0/1,
retained placement identities and decoded-grid viewer queries are connected.
Retained terrain/MODR entity queries, scene-side history and live mesh water
interleaving/clipping are connected. Full smoke: 1,052, exit 0, including 14 new
entity/mesh cases (two WARP fixtures). Complete liquid parity remains open:
allocator order, neighboring WMO links/native cache histories, shared effect
ordering, procedural water, ripples, underwater,
local lights and matched client captures. See the
[entity/mesh audit](reference/client-335/CPU_SCENE_AUDIT.md#entity-liquid-cache-scene-history-and-mesh-water-clipping-2026-10-02)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

The preceding WMO transition audit corrects 12340 attenuation/portal-distance
polygon rules, the final transition endpoint, packed material diffuse 127/255,
non-unified/no-MOCV pass dispatch and forced-staged unified opaque exterior fog.
Interior MFOG now requires a strict MOGP interior among retained viewer groups;
portal weighting considers both groups while volume IDs remain from the first.
Full smoke exits 0 with **744 tests** (66 Render, 485 DX11, 193 Avalonia),
including 22 new CPU/WARP transition/fog cases. See the
[compact CPU entry](reference/client-335/CPU_SCENE_AUDIT.md#wmo-transition-colors-lighting-and-fog-2026-10-01).
The subsequent live inspection traces the Client-only Stormwind gray layer to
transition alpha reaching the imported GUI image. Editor glow masked this by
writing opaque alpha. `WowViewerEngine.RenderTo` now finishes with an alpha-only
opaque pass, preserving every scene RGB channel and internal material alpha.
Full smoke exits 0 with **746 tests** (66 Render, 487 DX11, 193 Avalonia).
The captured full scene changes zero RGB pixels and has opaque alpha throughout
after the pass. See the
[presentation audit](reference/client-335/CPU_SCENE_AUDIT.md#wmo-transition-alpha-and-opaque-gui-presentation-2026-10-01).
Rebuilt GUI acceptance and matched native pixels remain open; unified lighting
still lacks local-light/material/shadow/fallback coverage.

The preceding batch adds six-plane portal sphere admission and per-instance fog to
the static 12340 WMO doodad lighting port. Repeated callback frusta form a union;
unbucketed/final group consumers retain call-time state and append order. The
first accepting owner selects staged/current fog independently of baked MODD
versus sunlight. Full smoke exits 0 with **718 tests** (66 Render, 459 DX11,
193 Avalonia), including 18 new cases and extended live M2 shader WARP fog pixels.
See the [compact CPU entry](reference/client-335/CPU_SCENE_AUDIT.md#doodad-portal-sphere-admission-and-instance-fog-2026-10-01).
The subsequent exterior doodad batch replaces the adapter with scene-wide
sphere depth buckets and cropped-frustum/static-volume/terrain-sphere admission,
before the current band's horizon updates. Terrain rejection consumes pending
eligibility; frustum/volume failures retain portal retry. Bucket draws retain
the previous portal-written staged/current fog bank. Full smoke exits 0 with
**778 tests** (66 Render, 519 DX11, 193 Avalonia), including 32 new cases.
See the [compact CPU entry](reference/client-335/CPU_SCENE_AUDIT.md#exterior-doodad-depth-buckets-and-terrain-sphere-admission-2026-10-01).
The next batch ports loaded WMO doodad size categories, detail-scaled bucket/
portal prefilters and CPU distance/alpha eligibility. Fresh exterior definitions
start with staged fog; rejected submissions retain portal-written history.
Full smoke exits 0 with **812 tests** (66 Render, 553 DX11, 193 Avalonia),
including 34 new cases. That batch retained CPU opacity for the following GPU
consumer audit. See the
[size/fade audit](reference/client-335/CPU_SCENE_AUDIT.md#doodad-size-categories-cpu-fade-gates-and-initial-fog-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).
The default non-shadow WMO doodad GPU fade path now composes instance/material
alpha, partitions faded draw groups and selects recovered blend/cutout state
while retaining authored depth flags. Full smoke exits 0 with **827 tests**
(66 Render, 568 DX11, 193 Avalonia), including 15 new cases and extended WARP
fade, cutout and depth-restoration pixels. That batch left ZFill, shadow variants,
layered dispatch and native lifetimes open for the following audit. Native global
ordering and matched client captures remain open. See the
[GPU fade audit](reference/client-335/CPU_SCENE_AUDIT.md#doodad-gpu-fade-alpha-and-material-state-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).
Underwater/dynamic lights, entity MOCV, shadows and matched client pixels remain open.

The following layered-material batch uses the base material to select native WMO
doodad queues, independently of depth flags, while each layer retains its own
blend/cutout/depth state. Translucent bases exclude full-alpha instances from
native instancing. Full smoke exits 0 with **841 tests** (66 Render, 582 DX11,
193 Avalonia), including 14 new cases and extended scene/WARP fixtures.
ZFill control/eligibility/clone state is recovered but its port requires sorted
element queues and model eligibility reseeding. M2 mesh submission has since
moved into its renderer; next recover those contracts, then shadow selectors/native lifetimes.
See the [layer/ZFill audit](reference/client-335/CPU_SCENE_AUDIT.md#doodad-layered-material-partition-and-zfill-dependency-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

M2 mesh renderer batch: constants, bone palette, dynamic instance uploads and
mesh depth states now belong to `M2MeshRenderer`. SceneManager retains CPU
preparation/frame order and consumes returned telemetry. Full smoke exits 0
with **843 tests** (66 Render, 584 DX11, 193 Avalonia). WARP coverage verifies
both material phases, 1,025-instance uploads, pose versions, intervening pipeline
state and failure cleanup. At that batch native sorted keys/comparator and
shared mesh/effect/water queues remained next, followed by ZFill eligibility
lifetimes. See the [renderer audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-mesh-submission-renderer-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

The subsequent batch ports CPU-only model/mesh distance rules, full base transparent/
opaque comparator fallbacks and native heap mechanics. Full smoke exits 0 with
**874 tests** (66 Render, 615 DX11, 193 Avalonia), including 31 ordering cases.
This is a tested policy foundation awaiting live decoded/pose/identity adapters,
water queues and additive regrouping; GPU order and live feature status are
unchanged. Those adapters and particle secondary-key/additive producers precede
ZFill eligibility reseeding. See the [ordering audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-element-keys-comparator-and-heap-policy-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

The decoded-input batch retains authored WotLK MPQ mesh sort flags, signed
priority/layer and section center-bone/center/radius/bone-count values. Its
explicit bone/model/view adapter reads current transforms and rejects missing
translucent center bones. Full smoke exits 0 with **899 tests** (66 Render,
640 DX11, 193 Avalonia), including 25 new decoding/transform cases. Native
pose/billboard/full-bone mapping and identity adaptation were its next dependency.
See the [decoded-input audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-decoded-sort-metadata-and-transform-inputs-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

The retained-pose batch now keeps all WotLK CPU bones beside the 256-entry GPU
palette, corrects live billboard/root-relative parent inputs and native parent/
normalization gates, and provides lifetime-owned packet/pose/resource sort inputs.
Full smoke exits 0 with **920 tests** (66 Render, 661 DX11, 193 Avalonia), including
21 new cases. The following CPU queue batch ports particle/ribbon keys, additive
regrouping and per-type water routing into retained shared queues. Full smoke exits
0 with **957 tests** (66 Render, 698 DX11, 193 Avalonia), including 37 new cases.
Native mesh preparation now retains authored water bounds and simple-animation
eligibility, selects resolved shader-table identities, and composes alpha/water
routing into retained queues. Entity lighting and initial particle water flags are
ported; section +0x0E is corrected to bone-combo start. Full smoke exits 0 with
**992 tests** (66 Render, 733 DX11, 193 Avalonia), including 35 new cases.
Mesh water partitions/clipping now consume live terrain/MODR entity queries and
viewer-dependent ordering. Global shared mesh/effect sorting, neighboring WMO
links/native cache histories, live effect tables, attachment/runtime overrides,
reference allocator ties and matched pixels remain
open; see the [pose/identity audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-full-pose-billboard-and-retained-sort-identities-2026-10-01)
and [queue audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-particle-keys-additive-regrouping-and-water-queues-2026-10-01)
and [preparation audit](reference/client-335/CPU_SCENE_AUDIT.md#m2-native-mesh-preparation-and-shader-selectors-2026-10-01)
and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).

The preceding portal batch connects the 12340 terrain clip buffer to exterior WMO
box rejection. Loaded terrain edge updates follow each depth band's WMO tests;
hole chunks erase unprotected columns and updated unbucketed groups bypass the
gate. Static CPU volumes remain separate. Full smoke exits 0 with **674 tests**
(66 Render, 417 DX11, 191 Avalonia), including 35 new cases. Evidence and
remaining limitations are recorded in the
[CPU entry](reference/client-335/CPU_SCENE_AUDIT.md#terrain-clip-buffer-and-depth-band-feed-2026-09-30).
Five originally listed function contracts remain incomplete; separate horizon
sources and terrain/streaming adaptation remain gaps, not dependency closure.
The next work is protected world-horizon sources, native terrain bounds/
availability, callback/doodad gates and GPU volume
consumers and native activation/streaming lifetimes, followed by global exterior
view/distance and viewer-liquid/frame
ordering. Continue all CPU preparation and DX9 SM3 workstreams using the
[extended roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap) and
[CPU roadmap](reference/client-335/CPU_SCENE_AUDIT.md#roadmap).
For a new thread, use the [handoff](CLIENT_335_RENDERING_ACCURACY_PLAN.md#start-here-in-a-new-thread)
and [working loop](CLIENT_335_RENDERING_ACCURACY_PLAN.md#working-loop).
Record batch evidence once in the CPU notes, keep the roadmap's next task current,
and update this ledger when implementation or capture status changes.

Viewport controls: Client mode defaults to Ultra, and its mode switch is confined
to viewport Advanced Rendering. Client settings remain editable in either mode.
Editor fog defaults off and affects only Editor mode. Client mode excludes
viewport content filters, diagnostics, fog suppression, distances/streaming,
percentage limits and manual lighting/time. Game glow and editor glow use separate
saved settings. Client fog stays enabled and the implemented game CVars remain
authoritative. Mode switches preserve both banks; viewport overrides are disabled
in Client mode. Other-client baseline fallbacks and native streaming/LOD/fades
remain incomplete; these editor controls do not establish rendering parity.
Verification: full smoke exits 0, **524 tests** (66 Render, 267 DX11, 191 Avalonia).

## Version matrix

| Client | Content and models | Terrain | WMO | Current verification |
| --- | --- | --- | --- | --- |
| 3.3.5.12340 (MPQ/DBC) | WowLib path-based loading and model textures are connected. Basic M2 skeletal/material animation, placement-owned sequence/time state, camera-facing billboard bones, and first ribbon and particle draw passes are active; full effect parity remains. M2 normals now use world space for directional lighting. Terrain renders before WMO/M2 and WotLK M2 material depth flags are applied. Opaque M2s draw before water, translucent M2s and effects after it. WotLK full-screen glow is enabled. Elwynn waterfall visual recheck is pending. | WowLib MCAL decode, layered diffuse rendering, LOD, and editing are connected. Chunk-edge alpha clamping and ordered overlay blending are implemented. The legacy light and vertex-colour product is now clamped at vertices as in Wisp; visual seam and lighting rechecks are pending. | WowLib group/material loading and textures are connected. Exterior normals use world-space lighting. The legacy vertex colour is now clamped before 2x fragment modulation. Alpha-key materials discard low-alpha texels with the WotLK WMO reference, and material clamp flags select texture addressing. Duskwood tree, Stormwind entrance, interior colour, emissive, and material permutations need visual parity work. | User confirmed the white instance portal particle plane in the editor on 2026-09-24. The Stormwind lighthouse beam was reported too yellow over terrain and incorrectly covered by distant water on 2026-09-25; the new pass order and glow need an in-editor comparison. |
| Classic 1.15 (CASC/DB2) | Content loading and rendering connected. Version-specific appearance needs a regression pass. | User reported a similar chunk seam. The common MCAL edge and overlay corrections apply; visual recheck is pending. | Rendering connected; compare exterior and interior materials after the shared normal-space correction. | User report of terrain seam; no controlled image comparison recorded yet. |
| Classic 1.60 (CASC/DB2) | Content loading and rendering connected. | No prominent seam reported by user. Height-texture weighting remains on its existing path; non-height overlays use ordered composition and edge clamping. | Rendering connected; compare lighting after the shared normal-space correction. | User reports terrain looked better than 1.15; visual regression pass still needed. |

Other WoW builds are not yet assigned a rendering-parity status. The matrix
records known coverage, not a claim that every feature below is era-correct.

## 12340 CPU scene audit (2026-09-30)

The [extended rendering plan](CLIENT_335_RENDERING_ACCURACY_PLAN.md) covers
all CPU scene preparation and render submission alongside DX9 SM3 shaders.
The [CPU dossier](reference/client-335/CPU_SCENE_AUDIT.md) records 93 cached
complete function decompilations, 71 scene roots, 418 direct relationships,
and 603 call sites. Recursive closure and thirteen indirect/global-pointer targets
remain open.

The 3.3.5.12340 MPQ camera query now traverses wowlib's decoded WMO BSP,
preserving reached leaves, face identity/order, duplicate suppression, the
8192-face cap, and the default digest-leaf rejection/fallback rules. CPU data
is prepared once and traversal scratch is reused. Other clients keep their
existing query path. A build-scoped per-placement caller now uses exact geometry
caps, last equal group hits, the strict normalized portal tolerance, native
polygon projection/boundaries, exterior rejection and one winning geometry seed.
Scene selection now uses insertion order across GPU asset buckets and separate
running normal/updated-transform pools, including equal-hit replacement and
exterior clearing. Runtime flags and stored 12340 ADT MODF bounds remain separate
from file flags. Shared primary/secondary group pairs seed WMO portal culling
independently of fog data, without repeating each placement's BSP query.
Scene portal masks and separate sky/exterior view unions now prepare
before sky submission. Closed views skip sky; open views apply the recovered
DX9 window scissor rounding across dome, clouds, celestials, stars and skyboxes.
Closed interiors clear to current fog color, and the WMO pass reuses prepared
masks. The camera-on-portal path now shares the native asymmetric polygon test,
clips against normalized world planes with ±0.0001 classification, omits near
rather than far, and preserves original portal plane coefficients. Directed
MOGP ranges, null references, previous-group back edges, inclusive depth 10,
strict rectangle degeneracy, loaded always-draw callback rejection and the
scene-wide exterior-window seeds have regression coverage. Exterior CPU queues
now preserve placement/group arrival order across GPU asset buckets, rebucket
updated placements outdoors and use live visible-group overlap when enclosed.
Interior/exterior visits share placement cache generations; accepted exterior
polygons collect globally, separately from primary interior windows/complements.
Map-specific static CPU occluders now reject bucketed MOGI spheres and offset
world portal polygons, with updated-placement and cached interior bypasses.
The terrain clip buffer now gates exterior WMO boxes and receives loaded edge
updates after each depth band, retaining hole and unbucketed exceptions.
The Stormwind fixture retains seven facade polygons and eleven actual one-way
links; those two named facades have paired links in the inspected asset.
This is a partial port: native streaming/group availability, global exterior
consumers/occlusion, WDT bounds, viewer liquids, blend-sky models, x87 rounding,
entity MOCV lighting and captures remain open.
Pre-existing IDA names are unverified hypotheses; address-based findings are in
[cpu-semantic-claims.csv](reference/client-335/cpu-semantic-claims.csv).
The full smoke batch passed 494 tests, including 87 added CPU behavioral cases
and four mode/persistence cases.

The persistent Client/Editor toggle separates effective Wrath CVar distances
from custom editor distances. Client mode currently applies recovered farclip
validation (including memory/map rules), near clip and portal culling, and
removes editor pixel-culling/terrain-LOD thresholds. Editor values survive mode
switches. [render-cvars.csv](reference/client-335/render-cvars.csv) records the
remaining option/consumer frontier. Full Client-mode parity, native LOD/fades,
all CVar effects and explicit whole-map Editor demand remain open.

The client frame audit also confirms liquid-dependent ordering of M2 passes
1/2 and a common sorted mesh/ribbon/particle/callback list. The editor's
current asset/effect loops and water order do not yet reproduce that system.
Next CPU batches establish viewer-liquid/plane selection, alpha/eligibility
and animation/bounds policies before wiring the recovered frame/element queues
into the extracted M2 mesh renderer. Terrain, WMO,
detail, shadow, sky/weather, FFX, and resource preparation remain required
workstreams with their own CPU-to-shader contracts.

## WMO CPU/GPU audit (2026-09-25)

The 3.3.5 path uses Wisp for group preprocessing and draw state. SIDN uses
Wisp's halved byte-space c29 at the vertex, followed by 2x texture modulation;
the Noggit night pulse timing is retained. The earlier Noggit full-strength
final-color addition made pale SIDN materials clip to white at night. A
same-scene client capture is still needed to settle visual parity. Wisp and Noggit
also differ on shader 6: Wisp blends the two textures with the second MOCV
alpha, while Noggit includes the second texture's alpha and the primary MOCV
alpha. The active legacy shader 6 path follows Wisp pending a matched client
capture. The modern shader path remains separate.

Verified in code and smoke tests: primary/secondary MOCV decoding in BGRA
order; size-based MOGP child traversal, including unaligned chunks; Wisp's
transition vertex fix-up and portal attenuation; UV0 fallback when legacy UV1
is absent; batch category, lighting bank, SIDN clock/cache, sampler addressing,
two-sided culling, blend 9/7 transition passes, and depth test/write state.
The viewport can display invisible MOPY/MPY2 collision faces as cached,
two-sided opaque gray triangles with dark edges in one DX11 draw per group.
The WMO vertex and constant-buffer byte offsets are checked against the HLSL
declarations. Both shader stages compile with `fxc`. This is static and
synthetic verification; a same-scene GPU capture against a 3.3.5 client is
still needed for visual parity.

TODO(WMO):

1. The [WoWDev 3.3.5 WMO rendering page](https://wowdev.wiki/WMO/Rendering)
   describes BSP/MOPY based barycentric
   MOCV lighting queries for entities inside interior groups. Wisp implements
   camera visibility and collision BSP queries but has no equivalent lighting
   query for dynamic entities. Entity BSP/MOCV lighting remains open. Static
   12340 WMO doodad mesh lighting now uses its MODD baseline and MOGI owner
   classification; its native mesh callback does not invoke a floor query.
   Portal callbacks now retain sphere frusta, and the first accepting group
   selects staged/current fog per instance. Exterior spatial admission and retained
   portal-written fog history are ported. Fresh staged fog, loaded WMO size/detail
   prefilters, CPU distance/alpha eligibility and default non-shadow GPU fade
   and layered base-material partition are now connected. Sorted ZFill clones,
   shadow alpha variants,
   native ordering/availability and matched client pixels remain open. See the
   [layer/ZFill entry](reference/client-335/CPU_SCENE_AUDIT.md#doodad-layered-material-partition-and-zfill-dependency-2026-10-01)
   and [rendering roadmap](CLIENT_335_RENDERING_ACCURACY_PLAN.md#roadmap).
   Keep that static baseline separate from the remaining entity lighting audit.
   The page also lists detail/render/trans MOPY debug modes. Normal editor
   rendering already consumes MOBA index ranges and the MOGP transition,
   interior, and exterior batch counts. Its collision overlay reads MOPY/MPY2
   `F_COLLISION` (0x08), `F_RENDER` (0x20), and the collision-only material
   sentinel; other per-triangle MOPY flags are not consulted during standard
   batch draws or exposed as overlays. A MOBA material ID of 0xFF is not yet
   resolved through the first MOPY face as Wisp does. Wisp decodes all MOPY
   records and uses `F_COLLISION`, `F_RENDER`, and `F_DETAIL` for viewer
   collision queries, but has no equivalent detail/render/trans face-category
   visualizers.
2. Decode the client-specific programs for waterWindow, submarineWindow,
   parallax, shader 23, and the other material families beyond Wisp's basic
   diffuse combine. Shader 23 currently contains a zero first-layer placeholder;
   its layer weights are guarded against a zero denominator but are not a
   decoded client program.
3. Apply Wisp's shader-0 opaque/alpha-texture promotion once BLP alpha metadata
   is available during WMO material setup. The two-texture missing-stage
   promotion for legacy shaders 3, 5, and 6 is implemented.
4. Add WMO directional shadow receiving and finish interior/portal fog. MOMT
   `Unfogged` bypasses 3.3.5 distance fog. The camera WMO now supplies blended
   distances/rate to the staged fog bank and current color to its interior WMO
   batches. The 12340 viewer BSP traversal is now ported; full viewer caller,
   entity lighting queries, and exact propagated group mode remain. WMO specular remains disabled until its material
   program and intensity have a reliable reference.
5. Replace the generic magenta fallback for absent optional material stages
   with per-shader neutral textures or a decoded shader fallback. A missing
   authored base texture should still remain visibly diagnostic.
6. Validate modern group MOCV/MOTV presence and material semantics against
   representative CASC assets from each supported version. Report group-byte
   read failures instead of silently using neutral colors and zero UVs.
7. Compare the same WMO, camera, world time, and portal view in the editor,
   Wisp, Noggit, and the 3.3.5 client. In particular, adjudicate shader-6
   alpha, Env/EnvMetal reflection coordinates, SIDN, and transition gradients
   using captured pixels rather than static shader inspection alone.

## Open visual issues and next work

1. Revisit the 3.3.5 Stormwind entrance and adjacent terrain in the editor:
   confirm exterior WMO batches use consistent lighting and inspect both sides
   of several MCNK boundaries. Compare the same camera, time, and light settings
   with the supplied reference images.
2. Repeat the terrain boundary check on Classic 1.15 and 1.60. If a seam
   remains, inspect the adjacent WowLib-decoded MCAL edge texels, layer IDs,
   diffuse UV scale, and MCCV colors before changing shared shader behavior.
3. Complete 3.3.5 M2 animation coverage and verify transparency, animated
   materials, doodads, and skyboxes with a moving model in the editor. Revisit
   the Elwynn waterfall and confirm its transparent texels show terrain behind
   them and that later liquid/model draws remain visible through the water.
   Recheck `stormwind_lighthousebeam_01.m2` at the supplied camera angle over
   terrain and water. Compare the beam's colour and width with the client after
   the new M2/water ordering and glow pass; any remaining difference may need
   the missing distance fog or more exact client glow parameters. General
   sorted mesh/ribbon/particle interleaving remains open; mesh water partitions
   and crossing-plane clipping are now connected.
4. Extend WMO parity by material family and interior/exterior lighting mode,
   starting with an in-editor Duskwood tree recheck for black cutout cards and
   leaf edges. Then add fog and sky/environment effects that are still listed
   below.
5. Compare 3.3.5 terrain, M2, WMO, and liquid at fixed camera positions and
   LightData times against the client/Wisp. Recheck the legacy vertex-light
   clamp and world-space M2 normal fixes on hills, rotated doodads, and WMOs.
   Basic 12340 water/magma now use recovered two-texture/one-texture SM3
   programs, which do not require scene-color/depth buffers. Procedural material
   3 has a separate six-texture program and remains open. Compare terrain and
   WMO liquids against the client; passing WARP formulas is not frame parity.
6. Check the 3.3.5 noon light direction on slopes and model faces. Wisp's
   reference noon ray vector is `(-0.5613, -0.5613, -0.6082)`; our DX11 shaders
   use `(0.5613, 0.5613, 0.6082)` toward the light. The prior vertical-only
   inversion lit the opposite horizontal sides in the editor. Compare the same
   camera and in-game time against the client.
7. Add 3.3.5 terrain specular lighting. In the Elwynn waterfall comparison,
   the client has bright sunlit rock highlights that disappear when its
   specular option is disabled; the editor's rock remains flat. Wisp's terrain
   shader only computes diffuse Lambert lighting, despite its light descriptor
   carrying a specular colour. Wisp uses that colour in its WMO
   Specular/Metal path, so its WMO half-vector calculation is a starting point
   but not an exact terrain reference. Determine the client's terrain
   highlight mask, exponent, and light colour from matched on/off captures or
   terrain shader evidence before implementing a version-scoped effect.
   Evaluate the stream's reflection separately as a liquid-material issue.
8. Add terrain cubemap reflection for MCLY `0x400` layers in the limited
   Northrend areas that use it. Preserve MCLY layer flags through WowLib's ADT
   data into the renderer; Wisp also uses `0x80` for per-layer unlit selection.
   This cubemap feature does not explain the Elwynn rock highlights.

## 3.3.5 M2 animations, particles, and ribbons

This section tracks the WotLK MPQ implementation. The white instance portal
has a successful in-editor visual check; that result does not establish parity
for every M2 effect or other client versions.

### Done

- **Per-instance animation state:** Each M2 placement owns a sequence index
  and playback offset. The first Stand sequence is the default. Visible
  placements with the same frame share material evaluation and, where possible,
  bone palettes. Unchanged frames reuse cached results.
- **Animation toggle and visibility:** Disabling animations retains the last
  evaluated bone and material pose for each visible placement. A placement
  without a prior pose samples frame zero once and reuses it. Paused particles
  keep drawing their last rendered mesh without emission or bone resampling;
  a newly visible emitter builds once at the paused scene time. Ribbons stop
  updating and drawing while paused. The independent particle toggle suppresses
  particle submissions, mesh rebuilds, and draws.
  Both effects are submitted only after M2 distance, frustum, portal, and
  projected-pixel culling. Animation/ribbon and particle distances are separate
  0–100% limits of the M2 model distance, defaulting to 50% and 20%; placements
  beyond the animation limit share cached frame-zero poses so they remain
  instanced. The global animation toggle preserves the last pose, sharing one
  snapshot across placements that used the same evaluated pose.
  Camera-centered skyboxes freeze with the global animation toggle but do not
  use world-placement distance limits. The profiler counts particle/ribbon
  geometry and draw submission in their own CPU category; M2 command submission
  measures mesh draws. Effect vertices and indices append to streaming buffers
  instead of discarding both buffers for every emitter. Live particle evaluation
  reuses point scratch storage and vertex/index arrays when counts match to
  reduce Gen0 allocation pressure.
- **Bone and material paths:** Skeletal and material tracks drive live M2
  rendering. Spherical and cylindrical billboard bones face the camera while
  preserving the pivot and child transforms. Animated skybox M2s use the same
  evaluator.
- **Ordinary particles:** Plane and sphere emitters render head and tail quads
  with texture atlas cells, lifetime colour/alpha/scale, deterministic spawn
  variation, drag, gravity, bounded wind, and supported spin/alignment flags.
  Constant, step, and linear emission rates integrate across sequence loops and
  instantaneous rate changes. Bone motion is sampled at birth; WorldSpace
  particles retain their birth bone orientation. Stationary bone chains with
  burst-velocity flags are admitted. Particle-only M2s and the blue, red, and
  white sparkler emitters are accepted for rendering. WotLK M2 blend IDs,
  including additive mode 4, map to the intended DX11 blend states.
  Emitter schedules are cached per immutable animation/emitter/sequence;
  emitter meshes rebuild lazily for changed frames or views, and additive
  particles skip the unnecessary depth sort.
- **White instance portal visual check (2026-09-24):** Both sphere emitters
  generate rings in the model's Y/Z opening plane. An installed-client asset
  geometry check found particles across the opening at several animation times.
  The user confirmed the editor result looks correct against the supplied
  client reference; the rotating-beam appearance is resolved.
- **Initial ribbon path:** WotLK ribbons reconstruct historical edges with
  bone transforms, widths, colour/alpha, gravity, visibility, and flipbook UVs.
  Only visible placements build ribbon meshes; unchanged frames reuse them.

### Remaining

- **Animation parity:** Retain and evaluate Hermite/Bezier tangents; those
  tracks currently use Wisp's linear fallback. Check animated materials,
  transparency, billboard orientation, moving doodads, distinct per-instance
  sequences, and skybox clock mapping against the client at matched views.
- **Particle simulation:** Match client pool capacity and state replay.
  Implement animated lifespan, gravity, and enable state; emission-rate
  variation; and particle colour-table overrides. Lifespan variation currently
  does not change a WotLK particle's life, and the zeroed twinkle lookup table
  selects the low scale without hiding particles.
- **Particle emitter families and motion:** Add spline paths, child models,
  world-query effects, and moving-placement WorldSpace history. Moving bone
  chains with inherited burst velocity remain gated. Recheck other constrained
  sphere emitters and twinkling models visually in the editor.
- **Ribbon composition:** Handle priority planes, multiple texture/material
  slots, and ordering with translucent M2 geometry. Compare ribbon effects in
  the editor against the client and Wisp.
- **Cross-version validation:** Check shared M2 animation and effect behavior
  on other supported WoW versions before marking it era-correct.

## World environment

| Feature | Status | Current scope / remaining work |
| --- | --- | --- |
| ADT terrain geometry and textures | Partial parity | Layered diffuse/height textures, LOD, editor overlays, culling, and streaming are active. MCAL sampling and composition need the per-version visual checks above. |
| WMO rendering | Partial parity | Groups, materials, instancing, doodad sets, portal visibility, and selection are active. The 3.3.5 path keeps MOGI and MOGP flags separate, uses downward viewer-group hits, projected nested portal rectangles, and the client packed MOBA batch bounds. Portal-less MPQ WMOs still cull interior groups outdoors; a real floor hit supersedes bounds-only candidates. Native static CPU occluders gate exterior group spheres and world portal polygons, preserving updated-placement and cached interior bypasses. Terrain clip-buffer box rejection and depth-band edge updates are connected, including hole erasure and unbucketed exceptions. Instance grouping and modern CASC batch bounds remain. Exact terrain fraction/rounding, native streaming/group availability and terrain combined bounds, protected horizon sources, GPU volume occlusion, and portal views for outdoor terrain, M2, liquid, and doodads remain. The user's reference/editor exterior captures still require a matched regression view. WotLK alpha-key cutout and WMO material clamp flags are connected; interior/material permutations remain. |
| M2 rendering | Partial parity | Static geometry/material combinations and instancing are active. 3.3.5 skeletal/material animation, billboard bones, per-instance selection, and first ribbon and particle draw passes are active. The white instance portal particle plane is visually confirmed; broader effect parity and visual comparison remain. Opaque and translucent M2 submeshes are submitted on opposite sides of the liquid pass without repeating animation evaluation. Hermite and Bezier tracks currently use Wisp's linear fallback because their tangents are not retained. World-space M2 normals and WotLK material depth flags need broader visual confirmation. |
| MH2O / WMO liquid rendering | Partial parity | MPQ 3.3.5.12340 basic water/specular-water/magma use recovered SM3 equations, native animated slots/clocks, 8x64 gradients, depth/UV rules and indoor alpha. Sun band 9 feeds exterior specular; the fixed indoor light contributes zero specular. Material flags split lists; list 0 draws before opaque M2, with retained placement/generation identities and duplicate suppression. Decoded bilinear grids feed the viewer type/depth query, retaining original WMO query type/tolerance independently of draw remaps. WARP verifies shader/blend/fog/state and list/shared-placement pixels; other clients retain their fallback. Retained terrain/MODR entity caches, update/render viewer histories and mesh water partitions/clipping are connected; WARP covers both viewer branches, moving skins, sparse uploads and state reset. Allocator order, neighboring WMO links/native cache histories, shared effect sorting, configurable clip-off behavior, portal-dependent WMO color, local lights, procedural material 3, waves/ripples, underwater fog/particulate, MCLQ and matched captures remain open. Basic water does not require scene-color/depth refraction resources. |
| Dynamic time-of-day lighting | Implemented | Light/LightParams and either LightData (builds after 15595) or LightIntBand/LightFloatBand (builds through 15595) are loaded by DBD column name and evaluated on the circular 0–2880 timeline. Legacy Light coordinates and falloff radii are converted from inches; legacy LightSkybox model paths resolve through the MPQ asset registry. Missing tables, columns, band rows, and referenced entries are reported in the console. Map navigation is durable view-model state, replayed whenever the DX11 renderer attaches/restarts, then retained by the engine until content/database initialization finishes and applied on the render thread with dynamic evaluation enabled. Renderer-owned controls are read-only while live lighting is active, and delayed TwoWay control echoes cannot disable dynamic updates. |
| Local radial lights | Implemented | Light falloff volumes are blended from the camera in renderer-native center-origin GameCoords space. |
| Zone lighting | Implemented | ZoneLight polygons use the same center-origin camera space, including vertical bounds, transition distance, and priority ordering. |
| LightData sky colors | Implemented | Top, middle, band 1/2, smog, and fog colors drive the modern camera-oriented gradient. The 3.3.5 path uses a shared-vertex dome with client band colors and additive blending. The user confirmed the temporal color seam is fixed in the editor; a matched client capture remains. |
| LightParams skybox model override | Implemented | LightSkybox is resolved by named columns and its SkyboxFileDataID is rendered as a camera-centred M2. The later-client flag 0x4 fog-color override remains separate from 3.3.5, where the traced dome keeps sampled sky bands. |
| Multiple skybox crossfades | Implemented | Distinct default, zone, and local skyboxes are retained together, their opacity weights are interpolated through each spatial blend, and every active model is submitted. |
| Animated skybox models | Partial parity | Active 3.3.5 skybox M2s use bone and material tracks through the same cached evaluator as world M2s. The animation toggle retains the last pose, or evaluates frame zero once for a newly visible skybox. LightSkybox flag 0x1 maps the lighting day onto the default sequence; other skyboxes use the scene animation clock. This clock mapping still needs comparison with the 3.3.5 client. |
| Celestial skybox override | Not yet | CelestialSkyboxFileDataID is decoded but not submitted. |
| Sun, moons, and stars | Partial parity | 3.3.5 sun and moon1 textured billboards draw before the dome with client orbit curves, horizon fade, and alpha blending. A wowlib-resolved stars M2 draws first with the client night fade and a live scene animation clock. The user's reference/editor comparison supports sun/moon size and orientation; the user reports that stars look good in the editor. Moon02 weather/override tint, exact sky depth/scissor, and matched client pixels remain. Modern clients retain their prior path. |
| Sun and moon glare | Partial parity | Client glare BLPs load through wowlib. Time, angle, cloud coverage, skybox opacity, and smoothed occlusion response feed an additive pass after world effects and before FFX glow. Nonblocking GPU occlusion queries are implemented. The user supplied an editor capture showing sun glare; matched client visibility, intensity, and weather behavior remain open. |
| Cloud layers | Partial parity | The 3.3.5 cloud palette, fixed cap mesh, density lookup, four-octave process-random noise, eight-row CPU texture updates, two GPU textures, and alpha-blended cap pass are connected. The client `SkyCloudLOD` CVar selects 128/256/512/1024 textures via a saved 0–3 Settings control; CPU preparation occurs off the render thread before a one-time GPU swap. The user supplied day/night cloud captures and confirmed the LOD control works. Matched client pixels, high-LOD frame cost, mip filtering, and weather remain open. |
| Distance/height/sun fog | Partial | 3.3.5 distance fog reaches terrain, WMO, M2, and liquids with the BLS visibility curve. The client `farclip`/`farClipOverride` CVars are active. Legacy unified opaque WMO batches stay fogged even with MOMT `Unfogged`; transparent and non-unified batches honor it. The portal walk records the interior-propagated bit per group, clears it at MOGP `0x48`, and uses it for the current/staged fog bank; transition batches use staged fog on their first pass before the propagated second pass. Non-unified groups without primary MOCV colors use staged fog, matching the client's `ExtRender` dispatch. Placements with different propagated masks render in separate batches. The client shader 0/blend 0 WMO promotion to `MapObjOpaque` follows wowlib BLP alpha depth, removing unintended texture-alpha weighting from the transition pass. Modern WMO behavior retains its prior path. Wowlib MFOG volumes and the camera-WMO portal distance feed blended fog distances/rate. Legacy M2 mesh blend families select scene, black, white, or byte-gray fog; modern M2s retain their previous path. The user's exterior WMO comparison needs a matched repeat. Exact terrain fraction/rounding, native streaming/group availability, global exterior portal-view and terrain horizon culling, M2 per-instance fog tint, underwater, effect fog, weather overrides, and later-client height/sun fog remain. The modern skybox flag 0x4 path still uses SkyFogColor rather than EndFogColor. |
| Color grading | Not yet | LightData color-grading FileDataIDs are not applied. |

## Rendering infrastructure

| Feature | Status | Current scope / remaining work |
| --- | --- | --- |
| DX11 frame presentation | Implemented | Keyed-mutex presentation and resize-safe render targets are active. |
| Hierarchical frustum/distance culling | Implemented | Terrain, WMO, M2, and liquid passes expose workload telemetry. |
| Sky-pass telemetry | Implemented | Sky draw calls, submitted indices, and CPU submission time are tracked by SceneManager. |
| Shadows | Not yet | No terrain/model shadow-map pass is present. |
| Reflections | Not yet | No planar, cubemap, or screen-space reflection pass is present. |
| Post-processing | Partial parity | 3.3.5 MPQ scenes use a Wisp-style half-resolution Gaussian glow/composite pass; this needs a client visual check and parameter calibration. Exposure, tone mapping, anti-aliasing, and client color grading are not present. Modern clients keep the existing direct rendering path. |
