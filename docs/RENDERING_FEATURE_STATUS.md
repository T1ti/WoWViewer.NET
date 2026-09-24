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
| 3.3.5.12340 (MPQ/DBC) | WowLib path-based loading and model textures are connected. Basic M2 skeletal/material animation and placement-owned sequence/time state are active; billboard bones, particles, and ribbons remain. M2 normals now use world space for directional lighting. Terrain renders before WMO/M2 and WotLK M2 material depth flags are applied. Elwynn waterfall visual recheck is pending. | WowLib MCAL decode, layered diffuse rendering, LOD, and editing are connected. Chunk-edge alpha clamping and ordered overlay blending are implemented. The legacy light and vertex-colour product is now clamped at vertices as in Wisp; visual seam and lighting rechecks are pending. | WowLib group/material loading and textures are connected. Exterior normals use world-space lighting. The legacy vertex colour is now clamped before 2x fragment modulation. Alpha-key materials discard low-alpha texels with the WotLK WMO reference, and material clamp flags select texture addressing. Duskwood tree, Stormwind entrance, interior colour, emissive, and material permutations need visual parity work. |
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

## 3.3.5 M2 parity milestones

| Item | Status | Next step |
| --- | --- | --- |
| Skeletal and material animation | Partial | Verify animated doodads and skyboxes in the editor, then cover remaining track and shader cases. |
| Billboard bones | Not yet | Apply camera-facing bone transforms before skinning; verify foliage and effects from multiple camera angles. |
| Per-instance animation selection | Implemented | Each M2 placement owns a sequence index and playback offset. The renderer defaults to the first Stand sequence, groups equal visible frames, and caches bone/material evaluations. The view toggle pauses the clock and draws visible placements in one unskinned, unanimated group without animation evaluation or palette uploads. Add an inspector selector and visually compare distinct sequences. |
| Particles | Not yet | Add an emitter simulation and draw pass using WowLib-parsed M2 emitter data. |
| Ribbons | Not yet | Add ribbon simulation, material sampling, and draw ordering from WowLib-parsed data. |
| Visual comparison | Pending | Compare moving 3.3.5 models and the Elwynn waterfall with Wisp and the client at matching camera/light settings. |

## World environment

| Feature | Status | Current scope / remaining work |
| --- | --- | --- |
| ADT terrain geometry and textures | Partial parity | Layered diffuse/height textures, LOD, editor overlays, culling, and streaming are active. MCAL sampling and composition need the per-version visual checks above. |
| WMO rendering | Partial parity | Groups, materials, instancing, doodad sets, portal visibility, and selection are active. WotLK alpha-key cutout and WMO material clamp flags are connected. Exterior lighting and Duskwood leaf transparency need visual confirmation; interior/material permutations remain. |
| M2 rendering | Partial parity | Static geometry/material combinations and instancing are active. 3.3.5 skeletal/material animation is partial; the milestones above list billboard bones, per-instance selection, particles, ribbons, and visual comparison. World-space M2 normals, terrain-first composition, and WotLK material depth flags need visual confirmation. |
| MH2O liquid rendering | Partial parity | Geometry, material families, LightData colors, and LightParams alpha are active. All-zero named LightData color quartets resolve to the shared non-black client-material palette in both renderer and UI snapshots while retaining the selected LightParams alpha values. The current forward water pass lacks scene-colour/depth refraction. Wisp marks exact ADT liquid materials unfinished, so 3.3.5 terrain-water tint/alpha needs a comparison against the client; WMO liquid can be checked against Wisp. |
| Dynamic time-of-day lighting | Implemented | Light/LightParams and either LightData (builds after 15595) or LightIntBand/LightFloatBand (builds through 15595) are loaded by DBD column name and evaluated on the circular 0–2880 timeline. Legacy Light coordinates and falloff radii are converted from inches; legacy LightSkybox model paths resolve through the MPQ asset registry. Missing tables, columns, band rows, and referenced entries are reported in the console. Map navigation is durable view-model state, replayed whenever the DX11 renderer attaches/restarts, then retained by the engine until content/database initialization finishes and applied on the render thread with dynamic evaluation enabled. Renderer-owned controls are read-only while live lighting is active, and delayed TwoWay control echoes cannot disable dynamic updates. |
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
