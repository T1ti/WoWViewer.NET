# WTEditor rendering feature status

Last updated: 2026-09-25. This is the working rendering ledger for future
client-version work. Update the version matrix, open issues, and next steps
when a fix lands or a visual comparison changes their status. WowLib remains
the only client-file reader and parser; Wisp is a rendering reference for 3.3.5.

This file is the maintained implementation ledger for the active Avalonia/DX11
editor. Update it whenever a rendering feature changes status. “Implemented”
means the feature is connected to live editor rendering and covered by the
repository smoke suite; it does not imply parity with every WoW client era.

## Version matrix

| Client | Content and models | Terrain | WMO | Current verification |
| --- | --- | --- | --- | --- |
| 3.3.5.12340 (MPQ/DBC) | WowLib path-based loading and model textures are connected. Basic M2 skeletal/material animation, placement-owned sequence/time state, camera-facing billboard bones, and first ribbon and particle draw passes are active; full effect parity remains. M2 normals now use world space for directional lighting. Terrain renders before WMO/M2 and WotLK M2 material depth flags are applied. Opaque M2s draw before water, translucent M2s and effects after it. WotLK full-screen glow is enabled. Elwynn waterfall visual recheck is pending. | WowLib MCAL decode, layered diffuse rendering, LOD, and editing are connected. Chunk-edge alpha clamping and ordered overlay blending are implemented. The legacy light and vertex-colour product is now clamped at vertices as in Wisp; visual seam and lighting rechecks are pending. | WowLib group/material loading and textures are connected. Exterior normals use world-space lighting. The legacy vertex colour is now clamped before 2x fragment modulation. Alpha-key materials discard low-alpha texels with the WotLK WMO reference, and material clamp flags select texture addressing. Duskwood tree, Stormwind entrance, interior colour, emissive, and material permutations need visual parity work. | User confirmed the white instance portal particle plane in the editor on 2026-09-24. The Stormwind lighthouse beam was reported too yellow over terrain and incorrectly covered by distant water on 2026-09-25; the new pass order and glow need an in-editor comparison. |
| Classic 1.15 (CASC/DB2) | Content loading and rendering connected. Version-specific appearance needs a regression pass. | User reported a similar chunk seam. The common MCAL edge and overlay corrections apply; visual recheck is pending. | Rendering connected; compare exterior and interior materials after the shared normal-space correction. | User report of terrain seam; no controlled image comparison recorded yet. |
| Classic 1.60 (CASC/DB2) | Content loading and rendering connected. | No prominent seam reported by user. Height-texture weighting remains on its existing path; non-height overlays use ordered composition and edge clamping. | Rendering connected; compare lighting after the shared normal-space correction. | User reports terrain looked better than 1.15; visual regression pass still needed. |

Other WoW builds are not yet assigned a rendering-parity status. The matrix
records known coverage, not a claim that every feature below is era-correct.

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
   interleaving of translucent water and M2s at different depths remains open.
4. Extend WMO parity by material family and interior/exterior lighting mode,
   starting with an in-editor Duskwood tree recheck for black cutout cards and
   leaf edges. Then add fog and sky/environment effects that are still listed
   below.
5. Compare 3.3.5 terrain, M2, WMO, and liquid at fixed camera positions and
   LightData times against the client/Wisp. Recheck the legacy vertex-light
   clamp and world-space M2 normal fixes on hills, rotated doodads, and WMOs.
   Liquid currently uses a simple forward water pass without the reference
   scene-colour/depth inputs. Wisp also records exact ADT liquid materials as
   unfinished, so compare terrain water with the 3.3.5 client itself before
   tuning tint and alpha. Use Wisp's WMO liquid path for WMO water checks.
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
| WMO rendering | Partial parity | Groups, materials, instancing, doodad sets, portal visibility, and selection are active. WotLK alpha-key cutout and WMO material clamp flags are connected. Exterior lighting and Duskwood leaf transparency need visual confirmation; interior/material permutations remain. |
| M2 rendering | Partial parity | Static geometry/material combinations and instancing are active. 3.3.5 skeletal/material animation, billboard bones, per-instance selection, and first ribbon and particle draw passes are active. The white instance portal particle plane is visually confirmed; broader effect parity and visual comparison remain. Opaque and translucent M2 submeshes are submitted on opposite sides of the liquid pass without repeating animation evaluation. Hermite and Bezier tracks currently use Wisp's linear fallback because their tangents are not retained. World-space M2 normals and WotLK material depth flags need broader visual confirmation. |
| MH2O liquid rendering | Partial parity | Geometry, material families, LightData colors, and LightParams alpha are active. All-zero named LightData color quartets resolve to the shared non-black client-material palette in both renderer and UI snapshots while retaining the selected LightParams alpha values. Water now draws between opaque and translucent M2 submeshes, preventing distant water from washing over a nearer additive beam. Water and transparent M2s still lack general per-depth interleaving, and the forward water pass lacks scene-colour/depth refraction. Wisp marks exact ADT liquid materials unfinished, so 3.3.5 terrain-water tint/alpha needs a comparison against the client; WMO liquid can be checked against Wisp. |
| Dynamic time-of-day lighting | Implemented | Light/LightParams and either LightData (builds after 15595) or LightIntBand/LightFloatBand (builds through 15595) are loaded by DBD column name and evaluated on the circular 0–2880 timeline. Legacy Light coordinates and falloff radii are converted from inches; legacy LightSkybox model paths resolve through the MPQ asset registry. Missing tables, columns, band rows, and referenced entries are reported in the console. Map navigation is durable view-model state, replayed whenever the DX11 renderer attaches/restarts, then retained by the engine until content/database initialization finishes and applied on the render thread with dynamic evaluation enabled. Renderer-owned controls are read-only while live lighting is active, and delayed TwoWay control echoes cannot disable dynamic updates. |
| Local radial lights | Implemented | Light falloff volumes are blended from the camera in renderer-native center-origin GameCoords space. |
| Zone lighting | Implemented | ZoneLight polygons use the same center-origin camera space, including vertical bounds, transition distance, and priority ordering. |
| LightData sky colors | Implemented | Top, middle, band 1/2, smog, and fog colors drive a camera-oriented gradient sky pass. |
| LightParams skybox model override | Implemented | LightSkybox is resolved by named columns and its SkyboxFileDataID is rendered as a camera-centred M2. Flag 0x4 flattens the cone to the available sky-fog color. |
| Multiple skybox crossfades | Implemented | Distinct default, zone, and local skyboxes are retained together, their opacity weights are interpolated through each spatial blend, and every active model is submitted. |
| Animated skybox models | Partial parity | Active 3.3.5 skybox M2s use bone and material tracks through the same cached evaluator as world M2s. The animation toggle retains the last pose, or evaluates frame zero once for a newly visible skybox. LightSkybox flag 0x1 maps the lighting day onto the default sequence; other skyboxes use the scene animation clock. This clock mapping still needs comparison with the 3.3.5 client. |
| Celestial skybox override | Not yet | CelestialSkyboxFileDataID is decoded but not submitted. |
| Sun, moons, and stars | Not yet | Directional exterior lighting is active, but celestial discs and star fields are not rendered. |
| Cloud layers | Not yet | LightData cloud colors/density and cloud textures are not rendered. |
| Distance/height/sun fog | Not yet | The current passes do not consume the full LightData fog parameter set, so skybox flag 0x4 uses SkyFogColor rather than EndFogColor. |
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
