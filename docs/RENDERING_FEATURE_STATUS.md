# WTEditor rendering feature status

Last updated: 2026-09-24. This is the working rendering ledger for future
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
| 3.3.5.12340 (MPQ/DBC) | WowLib path-based loading and model textures are connected. M2 skeletal/material animation is being added; particles and ribbons remain. Terrain now renders before WMO/M2, and WotLK M2 material depth flags are applied. Elwynn waterfall batches were confirmed from WowLib as alpha blend 2 with flags `0x14`; visual recheck is pending. | WowLib MCAL decode, layered diffuse rendering, LOD, and editing are connected. Chunk-edge alpha clamping and ordered overlay blending are implemented; visual seam recheck is pending. | WowLib group/material loading and textures are connected. Exterior normals now use world-space lighting and older material families share one lighting term. Alpha-key materials now discard low-alpha texels with the WotLK WMO reference, and the material clamp flags select texture addressing. Visual rechecks of Duskwood tree and Stormwind entrance are pending; interior color, emissive, and other material permutations need further parity work. | User screenshots establish the prior WMO lighting mismatch, terrain seam, waterfall depth artifact, and Duskwood tree cutout failure. WowLib confirms three alpha-key materials in `duskworldtree.wmo`, with transparent texels in their leaf textures. Smoke tests verify build and deterministic state policy, not final appearance. |
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
4. Extend WMO parity by material family and interior/exterior lighting mode,
   starting with an in-editor Duskwood tree recheck for black cutout cards and
   leaf edges. Then add fog and sky/environment effects that are still listed
   below.

## World environment

| Feature | Status | Current scope / remaining work |
| --- | --- | --- |
| ADT terrain geometry and textures | Partial parity | Layered diffuse/height textures, LOD, editor overlays, culling, and streaming are active. MCAL sampling and composition need the per-version visual checks above. |
| WMO rendering | Partial parity | Groups, materials, instancing, doodad sets, portal visibility, and selection are active. WotLK alpha-key cutout and WMO material clamp flags are connected. Exterior lighting and Duskwood leaf transparency need visual confirmation; interior/material permutations remain. |
| M2 rendering | Partial parity | Static geometry/material combinations and instancing are active. 3.3.5 skeletal/material animation work is underway. Terrain-first composition and WotLK material depth flags address the Elwynn waterfall transparency artifact. Particles, ribbons, and broader version verification remain. |
| MH2O liquid rendering | Implemented | Geometry, material families, LightData colors, and LightParams alpha are active. All-zero named LightData color quartets resolve to the shared non-black client-material palette in both renderer and UI snapshots while retaining the selected LightParams alpha values. Advanced client liquid shaders remain partial. |
| Dynamic time-of-day lighting | Implemented | Light/LightData/LightParams are loaded by DBD column name and evaluated on the circular 0–2880 timeline. Map navigation is durable view-model state, replayed whenever the DX11 renderer attaches/restarts, then retained by the engine until content/database initialization finishes and applied on the render thread with dynamic evaluation enabled. Renderer-owned controls are read-only while live lighting is active, and delayed TwoWay control echoes cannot disable dynamic updates. |
| Local radial lights | Implemented | Light falloff volumes are blended from the camera in renderer-native center-origin GameCoords space. |
| Zone lighting | Implemented | ZoneLight polygons use the same center-origin camera space, including vertical bounds, transition distance, and priority ordering. |
| LightData sky colors | Implemented | Top, middle, band 1/2, smog, and fog colors drive a camera-oriented gradient sky pass. |
| LightParams skybox model override | Implemented | LightSkybox is resolved by named columns and its SkyboxFileDataID is rendered as a camera-centred static M2. Flag 0x4 flattens the cone to the available sky-fog color. |
| Multiple skybox crossfades | Implemented | Distinct default, zone, and local skyboxes are retained together, their opacity weights are interpolated through each spatial blend, and every active model is submitted. |
| Animated skybox models | Partial | 3.3.5 M2 animation playback work is underway; the LightSkybox time-animation flag is not yet implemented. |
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
| Post-processing | Not yet | Bloom, exposure, tone mapping, anti-aliasing, and client color grading are not present. |
