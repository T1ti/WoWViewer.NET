# 3.3.5 client lighting port plan

The complete DX9 Shader Model 3.0 audit and reusable shader-cache workflow are
in [CLIENT_335_RENDERING_ACCURACY_PLAN.md](CLIENT_335_RENDERING_ACCURACY_PLAN.md).
This lighting ledger remains part of that plan's environment workstream.

## Goal and reference

Reproduce the visible outdoor and indoor environment produced by the connected
32-bit `Wow.exe` 3.3.5 build 12340 IDA database in WTEditor. This includes
light selection and interpolation, directional and local lighting, the sky,
stars, sun, moons, clouds, weather blends, skyboxes, distance and interior fog,
underwater transitions, and their effects on terrain, M2s, WMOs, and liquids.
Keep wowlib as the client-file reader, preserve editor diagnostics and wireframe
views, and isolate 3.3.5 rules from later client formats and shaders.

The reference database is `IDA-WoW-Wrath-2026.08.06.i64` (`Wow.exe`, image base
`0x400000`); strings at `0x9F5208` and `0x9F5200` identify `3.3.5` and
`12340`. Addresses below are anchors for this database, not portable symbols.
IDA comments and decompiler output are leads; confirm ambiguous casts, bit
flags, and shader formulas against disassembly or a client capture before
encoding them as behavior. Keep a compact rule/constant/fixture record here as
each area is resolved instead of retaining bulk pseudocode.

### Shader evidence

- Use the extracted 3.3.5 client shaders at
  `E:\WoWModding\aExtractedClients\WOTLK ClientFiles\shaders` as the primary
  material-shader reference. The directory contains 592 BLS containers and five
  WFX effect descriptions. Parse each BLS container, select the matching
  client backend/profile and variant, then compare its constants and
  instructions with the DX11 translation. Keep the chosen shader path,
  variant, and bytecode hash in each fixture. The file formats are described
  at [BLS](https://wowdev.wiki/BLS) and [WFX](https://wowdev.wiki/WFX).
- [Wisp's 3.3.5 GLSL shaders](https://gitlab.com/Natsirt867/wisp/-/tree/main/src/backend_gl/shaders?ref_type=heads)
  provide a readable cross-check for terrain, M2, WMO, liquid, combiners, and
  fog. They do not cover the procedural sky, sun, or cloud cap. Treat their
  formulas as corroboration until matched to client bytecode or IDA.
- [Benilla world shaders](https://github.com/samwhosung/benilla/tree/main/crates/benilla-world/src/shaders)
  and [asset shaders](https://github.com/samwhosung/benilla/tree/main/crates/benilla-assets/src/shaders)
  provide a readable 1.12 sky, cloud, celestial, and material baseline.
  Differences in 3.3.5 geometry, timing, colors, and blend state must come
  from the 3.3.5 client trace. Do not copy 1.12 behavior into modern paths.
- The extracted BLS inventory does not name sky, cloud, or sun programs; use
  IDA's sky/celestial/cloud render paths and captures as the primary evidence
  for those effects. Trace any fixed-function state into the DX11 equivalent.

## Current integration and verified gaps

| Area | Current path | Reference finding and action |
| --- | --- | --- |
| Client tables | `WorldLightingCatalogLoader` reads Light, LightParams, legacy bands through wowlib-backed DBCD; `WorldLightingCatalog` evaluates time and position. | The 3.3.5 profile now samples independent bands at the requested time, truncates packed RGB bytes, and divides float band 0 by 36. Continue auditing the other bands, LightParams variants, and derived colors. |
| Local lights | The 3.3.5 profile blends farther radial lights first and uses inner falloff radius for nearly coincident positions. | Verify dynamic override lights, polygon lights, and exact equal-priority heap behavior against `DayNight__CompareAreaLightPriority` (`0x7ED0A0`) and `DayNight__SeedAndBlendAreaLights` (`0x7F1360`). |
| Default light | The 3.3.5 profile selects the last exact zero-position map row and falls back to Light record ID 1. | Confirm loaded record order on a real 3.3.5 client; `DayNight_BuildLightRefsForContinent` (`0x7ECB30`) is the source. |
| Time and direction | Circular 0..2880 time, a versioned 3.3.5 cubic sun direction, and sky glow exist in `DayNight`. | Compare client interpolation precision, map time-of-day override, sun azimuth update timing, and celestial use of the raw direction against matched captures. |
| Sky | The 3.3.5 path renders the client's shared-vertex dome with additive blending; later clients retain the analytical gradient. The user confirmed the temporal color seam is fixed in the editor. | `DNSky__Build` (`0x7F2470`) makes a 24-segment, seven-ring dome (122 vertices, 300 strip indices); `DNSky__SetColors` (`0x7F0530`) sets per-ring azimuth glow and overlays. The dome uses the client's elevation approximation and byte-truncated vertex colors. Match viewport scissor, weather/spell overlays, and reference captures before calling it complete. |
| Skyboxes | Multiple weighted M2 layers and basic flag handling exist. The 3.3.5 path now suppresses the procedural dome for a drawable, non-combining skybox above 0.99 opacity. | `DayNight__RenderSky` (`0x7F09B0`) is the source. Reconcile layer order, flags, animation, the distinct blend-sky override, and the legacy MPQ source path. |
| Celestials | The 3.3.5 sun and moon1 submit textured DX11 billboards before the dome, using wowlib MPQ paths and the async BLP cache. The stars M2 loads by its MPQ path and draws ahead of both bodies with the client night fade and a live animation clock. Moon02 is loaded but omitted in the normal clear state because its client tint RGB is zero. | The user's reference/editor screenshots support sun and moon disc size and orientation, and the user reports that stars look good in the latest editor capture. Controlled matched-time star pixels, weather/override tint, and exact sky depth/scissor state remain. |
| Clouds | The 3.3.5 palette identifies rows 10/11/12 as emissive/body/ambient. The fixed cap mesh, startup density lookup, four-octave process-random noise, incremental CPU texture generator, two GPU textures, and ordered alpha-blended draw pass are implemented. `SkyCloudLOD` 0–3 now selects the client texture sizes; a changed LOD is generated off the render thread before GPU resources swap. | The user supplied editor captures showing the cloud cap at day and night and confirmed the LOD control works. These are qualitative checks; weather, client mip filtering, matched pixels, and high-LOD frame cost remain open. |
| Sun and moon glare | The client glare BLPs load through wowlib and render additively after world effects, before FFX glow. CPU time/angle/cloud response and nonblocking GPU occlusion queries are connected. | The user supplied an editor capture showing sun glare. Compare intensity, occlusion through terrain/M2/WMO, cloud response, and client query behavior in matched views. |
| WMO portal culling | The 3.3.5 DX11 path uses a downward viewer-group ray limited by the first loaded-terrain hit. Legacy viewer selection requires a WMO triangle or portal polygon hit; MPQ group triangles are restricted to MOBR collision-BSP face references. A nearer portal selects its camera-side group and, where interior, the opposite-side group. MOGI root flags and bounds seed outdoor traversal, MOGP flags select geometry-hit groups, and projected portal rectangles and batch frusta cull draws. Portal-less MPQ WMOs still run exterior/always-draw group culling. Legacy batch culling uses wowlib's packed MOBA box. Placement batching separates fog states and portal-propagated group masks. | Client `RRenderThruPortals`, `CullMapObjDefGroupFromExterior`, `LocateViewer3`, `CMap__VectorIntersectTerrain`, `CMapObj__VectorIntersectPortal`, and `CullBatch` were traced. The ray scans MOBR face triangles rather than walking the client's MOBN BSP. Exact geometry/portal tie order and placement flags/priority remain TODO. Exterior `ClipBufferCull`, global exterior portal views for terrain/M2, and per-doodad/liquid portal frusta remain TODO. The user's exterior captures still need matched camera and time regression. |
| Fog | The 3.3.5 outdoor fog evaluator feeds a DX11 fog buffer. ADT, WMO, M2, and liquid vertex shaders use the client BLS power transfer; versioned `farclip`/`farClipOverride` controls are active. Wowlib MFOG records and MOGP IDs feed a camera-WMO query before terrain rendering. This query stops at loaded terrain and requires a WMO face or portal polygon hit before activating interior fog. A nearer portal can select exterior even above an interior face. Eligible volumes blend farthest first; portal distance controls common fog distances/rate with staged outdoor color for terrain/exterior WMO and current blended color for camera-interior WMO. Legacy unified opaque WMOs ignore MOMT `Unfogged` as the client does; transparent and non-unified batches retain the flag. Portal traversal records the client's interior-propagated bit for each visited group, clears it at MOGP `0x48`, and uses it for current/staged fog choice, including transition passes. Legacy M2 blend modes select scene, black, white, or byte-gray fog color while modern M2s keep their prior scene-color behavior. | Repeat the user's exterior WMO fog captures and camera-above-interior case at matched positions and times. The camera-WMO query scans collision-BSP face references without traversing MOBN; portal near-side selection is implemented, but fog-volume sampling still uses the primary viewer group only. M2's per-instance mode-1 fog tint and enable scalar, underwater liquid flags, weather/spell/override fog, M2 effect fog, and WMO fog parity outside the portal-enabled path remain. |
| Surface lighting | Global ambient/diffuse reaches terrain, M2, WMO, and liquid shaders. | Audit terrain specular/shadow, M2 lights, WMO local and interior lighting, and material flags against the client before altering shared shaders. The existing rendering ledger already marks WMO interior queries, directional shadows, and terrain specular as open. |
| Terrain specular | Terrain currently applies ambient and direct light but has no verified 3.3.5 specular term. | Trace the terrain BLS vertex/pixel variants and the client's terrain material/state setup. Identify whether specular is vertex or pixel based, which Light band drives it, and how texture, shadow, and fog passes combine before enabling it. Keep later-client terrain logic separate. |
| M2 specular | M2 shading has diffuse material permutations, but its specular contribution is disabled. | Trace 3.3.5 M2 material flags, `CM2Lighting__SetupSunlight` (`0x835280`), local light selection, and relevant BLS programs. Port the exact color, exponent, normal/view transform, blend participation, and unfogged/shadow interaction with focused material fixtures. Check modern-client paths independently. |
| WMO specular | The existing WMO shader has a disabled approximate Wisp specular branch. | Trace `CMapObj__ApplyLightToGxDevice` (`0x834B50`), interior/exterior material selection, and `MapObjSpecular` BLS variants. Replace the approximation with the 3.3.5 program, including the proper light bank and material flags; compare interior and exterior captures before enabling. Preserve modern permutations. |

The table distinguishes known mismatches from areas still awaiting a complete
reference trace. A value being loaded or shown in the Lighting window is not
evidence that it affects rendered pixels.

### Compact 3.3.5 rule record and implementation status

| Client evidence | Rule | Current implementation and remaining check |
| --- | --- | --- |
| `0x7EAE70`, `0x7EB070` | Light bands wrap over 2880 game-time units. The int sampler interpolates RGB components, truncates each to a byte, and writes opaque alpha. | Legacy band snapshots and the requested-time sampler use circular keys, component truncation, and `0xFF` alpha. Compare float precision against a client capture. |
| `0x7EAEF0`, `0x7EBFF0`, `0x7ECD80`, `0x7ECD00`, `0x7F16F0` | Float band 0 is divided by 36; band 1 is clamped to [-1, 1]. Outdoor fog floors band end at 10, optionally derives a rate, then limits end by far clip. | Band conversion, active validated far clip, and a per-frame outdoor fog buffer are implemented for 3.3.5. Numeric fixtures and DX11 shader compilation pass. Portal/underwater and override paths remain. |
| `0x7ECB30` | Use the last exact zero-coordinate Light row for the current map, otherwise record ID 1. | Implemented for the 3.3.5 profile; later clients retain their existing selection. |
| `0x7ED0A0`, `0x7F1360`, `0x7EE5D0` | Process radial lights farthest first. For source positions within `0.33333334`, compare inner falloff radius instead. | Implemented for the 3.3.5 profile with focused overlap tests. Verify exact heap behavior at equal priority and dynamic override sources. |
| `0x7EE750` | The shadow modulation alpha is the red byte of packed int band 8, normalized by 255 when exposed as opacity. | The 3.3.5 data path extracts this byte; the modern scalar field remains separate. Shadow receiving on materials is still open. |
| `0x7EFAE0` | Cloud row 10 is emissive, 11 body, 12 ambient; use the moon before 04:50 and after 22:10, sun otherwise. Weather reduces the emissive scale. | The 3.3.5 palette, moon/sun handoff, and cloud shading are connected; other legacy clients retain their prior cloud mapping. Weather state is still absent. Row 13 stays an unassigned 3.3.5 value until its consumer is traced. |
| `0x7F2470`, `0x7F0530`, `0x7ED2D0`, `0xAF4B78` | The sky has 24 azimuth segments and seven elevation rings. Its elevation uses the client's cubic cosine approximation. Glow colors are sampled at vertices, and each CImVector interpolation truncates RGB after subtracting the 0.5 floor bias. | Implemented in the 3.3.5 dome path, with geometry and glow-boundary tests. The user confirmed the attached screenshot's temporal color seam is fixed in the editor. Matched client capture validation remains open. |
| `0x7F09B0` | A drawable skybox above 0.99 opacity suppresses stars, celestial bodies, dome, and clouds when its combine flag (bit 1) is clear. | Dome, stars, sun, moon1, and cloud cap share the 3.3.5 gate; threshold, combine, and readiness boundaries have focused tests. The separate blend-sky override remains open. |
| `0x7F0530`, `0x7F3230`, `0x7F30C0`, `0x7ECF20` | The traced 3.3.5 sky pipeline reads skybox flag 1 for full-day animation and flag 2 for combine. `DNSky::SetColors` reads sampled sky bands directly; no flag-4 fog-color replacement appears in these functions. | The 3.3.5 dome now retains sampled sky bands when flag 4 is present; the existing later-client color-override path remains separate. Check other flag consumers before declaring flag 4 globally unused. |

The legacy requested-time sampler retains the raw channel keys and memoizes
one evaluated time per LightParams ID. The union-key snapshots remain available
for diagnostics and other pre-LightData clients; the 3.3.5 profile samples the
raw curves so it does not interpolate already-truncated bytes a second time.
The latest complete smoke check passed 27 core, 165 DX11, and 184 Avalonia tests
(376 total, exit code 0). The dome, celestial, cloud, glare-query, and changed
fog material shaders compiled with the Windows HLSL compiler. This establishes
CPU behavior and build compatibility. The user confirmed the dome seam fix,
provided qualitative reference/editor comparisons for sun and moon discs, and
reports that stars look good, that clouds and glare are visible, and that cloud
LOD changes work. Controlled matched-time, matched-camera pixel measurements
of stars, clouds, glare, portals, and the complete scene remain open.

### Client function coverage ledger

This ledger covers every 3.3.5 function currently identified for this port.
**Traced** means the relevant rule was checked in IDA; **anchored** means the
function was located but its complete behavior still needs analysis.
**Ported** means the stated subset has focused tests or a shader compile;
**partial** means only some outputs or branches are used. The dome seam was
visually verified by the user in the editor. The user also compared sun and
moon discs with reference-client screenshots and reports that stars look good;
clouds and glare have qualitative editor captures. Update this table
whenever a function is traced, implemented, or capture-verified; retain TODO
rows until resolved.

| Client function (build 12340 address) | Evidence | Engine status and remaining proof |
| --- | --- | --- |
| `CGWorldFrame__UpdateDayNight` (`0x4F8410`), `DayNight__Update` (`0x7816F0`), `DayNight__SetColors` (`0x7F3230`) | Anchored frame/update chain; `SetColors` copies the sampled sun color into both glare tint RGB bytes. | **Partial:** timed world catalog, direction, and shared sun/moon glare tint exist. Trace exact update order, glare tint alpha, weather/spell overrides, map time override, and every renderer input. |
| `DayNight__FindLightBandBlendInterval` (`0x7EAE70`), `DayNight_SampleLightIntBandColor` (`0x7EB070`) | Traced circular 2880-unit sampling, packed-channel truncation, opaque alpha. | **Ported subset:** requested-time 3.3.5 bands and byte fixtures pass; compare values at wrap/key boundaries with a client capture. |
| `DayNight_SampleLightFloatBandValue` (`0x7EAEF0`), `DayNight__FillColorArrayFromLightParams` (`0x7EBFF0`) | Traced float sampling, band-0 /36 conversion, band-1 clamp to [-1, 1], and default fog rate 1. | **Partial:** band conversion and a pure 3.3.5 outdoor fog evaluator exist; trace all six field consumers and connect the active far clip. |
| `DayNight_BuildLightRefsForContinent` (`0x7ECB30`) | Traced last exact zero-position row and Light ID 1 fallback. | **Ported subset:** selection tests pass; verify actual DBC record order and dynamic additions. |
| `DayNight__CompareAreaLightPriority` (`0x7ED0A0`), `DayNight__SeedAndBlendAreaLights` (`0x7F1360`), `DayNight__BlendAreaLightWithDistanceFalloff` (`0x7EE5D0`) | Traced farthest-first blend and near-position inner-radius tie rule. | **Partial:** spatial order/overlap tests pass; equal-priority heap order, polygon lights, and temporary overrides remain. |
| `DayNight__DeriveInteriorColors_MidPlus16` (`0x7EE750`) | Traced shadow opacity from the red byte of packed int band 8. | **Ported data, rendering TODO:** extraction test passes; other derived interior colors and material shadow modulation remain. |
| `DayNight__InterpTable` (`0x7ED3B0`) | Traced wrapped linear interpolation. | **Ported subset:** sky time/azimuth curves use named client tables; compare exact float precision and other table consumers. |
| `DayNight__SetDirection` (`0x7EEA90`), `CM2Light__SetDirection` (`0x834AE0`) | Traced four-key phi curve, fixed theta, cubic cosine for each component, and M2 sunlight normalization. | **Ported subset:** 3.3.5 catalog samples the client polynomial and exports a unit vector toward the light; raw ray remains available for celestial work. Modern catalog retains its prior curve. Numeric and profile-isolation tests pass; compare live client captures and other raw-ray consumers. |
| `DayNight__UpdateLighting` (`0x7F3920`) | Traced sky azimuth and weather weight before `SetColors`, `SetDirection`, and `SetPlanets`. | **Partial:** catalog and dome use one sampled clock; investigate the client's copied-direction azimuth timing, storm blend, and planet update order. |
| `DayNight__LerpCImVector` (`0x7ED2D0`) | Traced byte truncation after subtracting `g_dnTimeFloorBias=0.5`. | **Ported subset:** dome vertex colors use it; cloud/spell overlays and other packed-color consumers remain. |
| `DayNight__DNSky__Build` (`0x7F2470`) | Traced 24-segment, seven-ring positions and cubic elevation approximation. | **Ported:** 122 shared vertices/240 visible triangles; watertight topology and position tests pass. Matched camera-pitch captures remain. |
| `DayNight__DNSky__SetColors` (`0x7F0530`) | Traced glow curves, band assignment, fog ring, and vertex coloring; it reads sampled sky bands without a skybox flag-4 color replacement. | **Partial:** base colors, glow, packed rounding, and flag-4 independence pass focused tests. The user visually confirmed the temporal color seam fix in the editor. Cloud and spell color overlays remain. |
| `DayNight__DNSky__Render` (`0x9ACB00`) | Traced scale, additive blend, disabled lighting/fog/cull/depth-write. | **Partial:** dedicated DX11 dome and shader compile; it now draws after the sun and moon1. Verify exact depth/scissor behavior and client pixel parity. |
| `DayNight__RenderSky` (`0x7F09B0`) | Traced drawable opaque skybox suppression, order, viewport scissor. | **Partial:** stars, sun/moon1, dome, and cloud cap share the suppression gate and draw in client order. Blend-sky override, scissor, and matched layer captures remain. |
| `DayNight__GetOverrideSky` (`0x7F30C0`), `AnimateSkyModel` (`0x7ECF20`), `DayNight__DrawSkyModel` (`0x7F08C0`) | Traced flags stored on sky model, full-day animation flag 1, and two scene draw phases. | **Partial:** skybox M2 animation clock and async draw exist; compare sequence refresh threshold, pass ordering, client material results, and any other flag-4 consumers. |
| `DayNight__SetPlanets` (`0x7EECC0`), `DayNightInitialize` (`0x7F2790`) | Traced sun, moon1, and moon02 polar/azimuth/size curves; cubic direction and radius 12; 1.7-day moon02 phase; prominence ramp; texture paths `Textures\sunCenter.blp`, `Textures\moon.blp`, `Textures\moon02.blp`. | **Partial render, qualitative visual check:** typed frame evaluator and numeric fixtures pass; all three client paths resolve once through wowlib and stream through the BLP cache. Sun and moon1 draw. The user's reference/editor images support their size, direction, and orientation; matched-time color, weather tint, and billboard pixels remain. |
| `DayNight__BuildCelestialQuad` (`0x7EDBE0`), `DayNight__ClipCelestialQuadToHorizon` (`0x7EDEE0`), `DayNight__RenderCelestialBody` (`0x9AC660`) | Traced four/six-vertex quad, full size, camera-height horizon clip, 0.4-unit vertex-alpha fade, alpha blend, and disabled fog/lighting/depth-write. | **Partial render, qualitative visual check:** horizon/fade fixtures and the sun/moon1 GPU draw pass exist, with client alpha blending and no depth write or fog. The user reports body size and orientation look accurate. Verify exact depth range, sampler/filter state, tint, and matched pixels. Moon02 clear-state RGB stays zero; weather/override color remains TODO. |
| `BuildOrthonormalBasisFromDirection` (`0x9ABB60`) | Traced normalized view-forward basis, its `1e-5` horizontal fallback, and cross-product third axis. The 4/6 client quad indices are triangle strips `[0,1,2,3]` and `[0,1,4,5,2,3]`. | **Ported subset, qualitative visual check:** basis fixtures pass and DX11 uses equivalent triangle-list topology. The user reports matching sun/moon orientation; verify UV handedness at cardinal headings in controlled captures. |
| `DayNight__DNStars__Init` (`0x9ABB00`), `DayNight__DNStars__UpdateImpl` (`0x7EE0D0`), `DNStars__Render` (`0x9ABD50`) | Traced the client model request `Environments\Stars\stars.mdl` (the extracted 3.3.5 asset is `Stars.m2`), camera-centered update, and alpha byte `(int)(curve*254+1)` from keys `(0.125,1)`, `(0.1875,0)`, `(0.9375,0)`, `(1,1)`. Render skips alpha bytes below 2 and uses byte/255 model opacity in two M2 scene passes before the sun. | **Partial render, qualitative visual check:** the MPQ M2 path resolves through wowlib; the animated M2 sky renderer centers it on the camera, applies the byte/255 night fade, and draws before sun/moon. Alpha and wrap tests pass; the user reports stars look good in the editor. Compare animation/material phases and transition captures with the client. |
| `DayNight__DNClouds__SetupBumpMap` (`0x7EFAE0`) | Traced row 10/11/12 colors and sun/moon handoff. | **Partial render:** palette and handoff tests pass, and the generated cloud RGB uses those sampled colors. Weather reduction is coded but has no live weather source yet; compare shading with the client. |
| `DayNight__DNClouds__constructor` (`0x7F04B0`), `DayNight__DNClouds__InitNoiseTables` (`0x7ED250`) | Traced four octaves, two mips, 8 rows per update, scroll phase speed 2; the fixed permutation is at `0xAF4A70`, gradients are 256 process-random values in [-1,1], and fade is `(1-cos(k*pi/256))/2`. | **Ported subset:** pure CPU noise, fixed permutation, cosine fade, process-random gradients, two texture allocations, and row-update state have focused tests. Capture a seed for exact client cloud pixels and verify mip filtering. |
| `DayNight__DNClouds__BuildMesh` (`0x7F20E0`), `DayNight__DNClouds__BuildDensityLUT` (`0x7EDB50`) | Traced 12-ring/16-azimuth cap, ring alpha bytes, UV disc, 177 vertices/374 strip indices, and one-time lookup `trunc(255 - 0.96^(k*(255-coverageByte)/256)*255)` with startup coverage 0.6. | **Ported subset:** mesh and density LUT have focused topology/byte tests; fixed geometry is uploaded and the lookup supplies generated alpha. Inspect runtime edge behavior against the client. |
| `DayNight__DNClouds__Update` (`0x7F1010`), `DayNight__DNClouds__GenerateTexture` (`0x7EFD00`), `DayNight__DNClouds__Render` (`0x9ACD40`) | Traced four-octave 3D value noise, LUT and per-row RGBA shading, 8-row incremental updates, ping-pong textures, then a no-depth-write alpha-blended cap after the dome. The 16-bit scroll phase changes only after a full texture pass. | **Partial render:** two GPU textures receive only changed rows; the active texture switches after a full new phase, and the cap draws after the dome. LOD changes prepare pixels on a worker and replace GPU resources once. CPU update, handoff, and shader compile tests pass. Verify weather response, generated pixels, and frame budget in client/editor captures. |
| `DayNight__DNClouds__WorldToTexture` (`0x7EF920`), `DayNight__DNClouds__SampleDensity` (`0x7EFA30`), `DayNight__DNClouds__SampleInverseDensity` (`0x7F1020`), `DayNight__DNClouds__Collide` (`0x7EDA90`) | Density sampling uses the generated 8-bit buffer at a projected/clamped texel and scales by 1/255; inverse density returns one minus that sample. The projection uses a unit sphere centered 0.7071 below the camera. | **Partial:** the projection and sampled density feed glare; geometric tests pass. Verify client UV orientation, edge clamping, and visibility at the cloud cap boundary. |
| `DayNight_InitializeSunGlare` (`0x7EE150`), `DayNight__InitializeMoonGlare` (`0x7EE230`), `DayNight_InterpolateValue` (`0x7EF6E0`), `DayNight_ComputeCloudDensityResponse` (`0x7F1040`) | Traced `Textures\sunGlare.blp` and `Textures\moonGlare.blp`; time, angle, visibility, skybox, and cloud response alter glare size/alpha. Sun response is `1-density`; moon response is `1-abs(2*density-1)`. | **Partial render:** both client textures load through wowlib; tested time curves, angle scaling, cloud response, and rise/fall smoothing feed the DX11 additive pass. Tint alpha and matched visual intensity remain unverified. |
| `DayNight_CelestialBody__Render` (`0x9AC3C0`), `DayNight__IsSunVisibleFromPoint` (`0x9ABC60`) | Traced an occlusion query or world/M2 visibility test for glare; the client divides visible samples by projected quad area. | **Partial:** two nonblocking DX11 occlusion queries per body use the completed result and normalized projected area. The CPU world/M2 fallback is not implemented. Verify query visibility through terrain, M2, WMO, and portal transitions in matched views. |
| `DayNight__DrawTexturedViewports` (`0x7F0870`), `Gx_DrawTexturedViewport` (`0x9AC400`), `CGWorldFrame__OnWorldRender` (`0x4F8EA0`) | Traced post-world sun/moon glare quads before the FFX glow composite, with additive blend and disabled fog, depth test/write, lighting, and cull. | **Partial render:** DX11 submits both textured glare quads after world/effects and before editor FFX glow with additive blending. Verify the new pass in an editor capture and compare occlusion and cloud cover. |
| `ConsoleCmd_SkySunGlare` (`0x7ECE40`) | Traced the `SkySunGlare` console command, which toggles both sun and moon glare; both start enabled at client initialization. | **Ported default:** both glare passes are enabled. This is a console command, rather than a client config/CVar setting, so it is not added to Settings under the user's control rule. |
| `CMapObj__RRenderThruPortals` (`0x7AC060`), `CMapObj__ProjectAndClipPortalPolygon` (`0x7A9090`), `CWorldScene__ProjectAndClipWorldPolygon` (`0x7A85E0`) | Traced signed MOPR side, eye-on-polygon full view, projected rectangle overlap, depth limit 10, five camera clip planes without the far plane, 12 input vertices, legacy max-Y intersection behavior, and the propagated-fog flag clearing at MOGP `0x48`. | **Partial port:** per-placement projected portal caches and nested rectangles feed group and batch masks. Viewer bounds candidates no longer override a real nearer floor hit. The propagated bit now follows each interior portal path and is cleared at MOGP `0x48`; exact clip-volume early-out, polygon epsilon, and portal-view buffer remain TODO. |
| `CMapObj__GetGroupFlags` (`0x7AE7B0`), `CWorldScene__CullMapObjDefGroupFromExterior` (`0x7B3A10`), `CMapObj__RenderPortalOcclusionPass` (`0x7AD1F0`), `CMapObj__CullBatch` (`0x7A7630`), `CMapObjDef__GroupRenderCallback_EnqueueLiquid` (`0x799310`) | Traced MOGI root flags and bounds for exterior/always-draw seeds, MOGP header flags for viewer checks, six signed MOBA box coordinates passed to the batch frustum culler, and WMO liquid submission through the visible-group callback. `GetGroupFlags` reads MOGI; the two flag sets cannot be substituted. Exterior group culling also calls `CWorldScene__ClipBufferCull` for depth-sorted entries. | **Partial port:** wowlib MOGI/MOGP sources remain separate; exterior/always-draw rules run even for zero-link MPQ WMOs, and MPQ batch culling uses the packed MOBA box. WMO liquids now use the group portal mask. Modern CASC keeps its prior derived-bounds path. Client clip-buffer occlusion and exterior portal-view propagation to terrain, M2, and doodads remain TODO. |
| `CMapObj__AddPortalView` (`0x7A8F20`), `CWorldScene__CullMapObjDefGroupFromExterior` (`0x7B3A10`), `CMapObj__RRenderThruPortals` (`0x7AC060`) | An interior portal whose target MOGI flags include `0x10008` emits a portal view and stops recursion. The view contains a projected rectangle and maximum projected vertex distance. Exterior group candidates are then culled against that view and, for depth-sorted entries, the terrain clip buffer before being seeded from outdoors with propagation 0. | **TODO:** replace the current direct marking of a linked exterior group with the client's portal-view exterior seed and depth gate. Propagate portal views to terrain, M2, and doodads. This is a remaining source of the reported facade visibility mismatch. |
| `CWorldScene__ClipBufferCull` (`0x78FDC0`), `CMapChunk__UpdateCameraFacingVertexRows` (`0x7CFB10`) | Traced a 384-column horizontal terrain horizon buffer. Exterior WMO group AABBs are rejected when their projected highest point lies behind the buffer in every covered column; the test is disabled outside the client's pitch/world-enable gate. Terrain camera-facing rows populate the buffer before this cull. | **TODO:** port or match the horizon occlusion stage before declaring exterior facades fully culled. Keep its gate and update order; an unverified GPU occlusion substitute could change both latency and silhouettes. |
| `CMapObj__RenderGroup` (`0x7ABF50`), `CMapObj__UnifiedRender` (`0x7A9380`), `CMapObj__ExtRender` (`0x7AC6A0`), `CMapObj__IntRender` (`0x7AC9F0`) | `RenderGroup` chooses `IntRender` when the group has primary MOCV colors and `ExtRender` otherwise; both delegate to `UnifiedRender` when MOHD flag `0x2` is set. The non-unified `ExtRender` uses staged fog regardless of portal propagation. Unified opaque batches (`index >= transBatchCount`) apply fog regardless of MOMT `F_UNFOG=0x2`; unified transparent batches and both non-unified renderers honor the flag. Unified opaque MOGP `flags&0x48` forces staged outdoor fog; strict interior uses the propagated current/staged selector. Shader-enabled transition batches draw first with staged fog, then with the propagated bank. | **Partial port:** the versioned material/category policy and pass-level fog drive DX11 WMO batches. Portal-derived propagation chooses the current/staged fog bank where the client uses it; non-unified groups without MOCV remain on staged outdoor fog. Fallback paths still use group flags. Fixed-function fallback passes, shadow receiving, and matched fog pixels remain TODO. |
| `CMapObjDef__GroupRenderCallback_EnqueueLiquid` (`0x799310`), `CWorldScene__RenderMapObjDefGroups` (`0x7964A0`), `CMapObj__RenderPortalOcclusionPass` (`0x7AD1F0`), `CMapObj__RRenderThruPortalsFromExterior` (`0x7AD350`) | Interior traversal starts with propagation 1, exterior traversal with 0; each visited group clears it at MOGP `0x48`. The callback ORs the surviving bit into render-node flag `0x8000` across visits, and the group renderer restores that bit before drawing WMO batches. | **Ported for the portal-enabled 3.3.5 path:** the visibility scratch records the bit per group, placement batching keeps distinct masks separate, and WMO fog selection consumes it. Verify its appearance in matched interior/exterior captures. Modern WMO logic is unchanged. |
| `PumpBlpTextureAsync` (`0x4B7BD0`), `CMapObj__IsRenderFlagSet` (`0x4B54F0`), the three WMO renderers above, `MapObjDiffuse`/`MapObjOpaque` ps_3_0 BLS | The texture's runtime flag 0x1 is set only for BLP alpha depth 0. For shader 0 with blend mode 0, an alpha-bearing BLP selects shader 4. Both pixel programs produce the same fogged RGB; shader 0 outputs texture alpha times vertex alpha, while shader 4 outputs vertex alpha. GuardTower materials 9 (`MM_STRMWND_WALL_04.BLP`) and 12 (`MM_STRMWND_WALL_05.BLP`) are both shader 0/blend 0 and both BLPs declare alpha depth 8. Material 12 also appears in the interior group's transition range. | **Ported selection:** wowlib's decoded alpha depth is published with each asynchronously uploaded BLP, and legacy WMO draws use it to select shader 4. Modern materials retain their prior mapping. This removes texture-alpha weighting from the GuardTower transition pass after load; a matched screenshot is still needed to verify the remaining exterior fog difference. The BLP alpha pixels alone do not explain an opaque exterior batch's RGB because the client shader programs share that RGB formula. |
| `CWorldScene__LocateViewer3` (`0x795D40`), `CMap__VectorIntersectTerrain` (`0x7A39F0`), `CMap__LocateViewerMapObjs` (`0x7D59B0`) | Verified a 1760-unit downward camera ray capped at the nearest terrain hit, nearest WMO group hit, exterior rejection, and portal-side second viewer group near a tie. | **Partial port:** loaded terrain caps the 3.3.5 WMO viewer ray independently of terrain rendering and distance settings. A legacy group must have a real triangle or portal hit, even when the camera is above its bounds; bounds-only selection remains limited to the modern path. Portal overrides select near and secondary interior groups before terrain draws. Exact tie resolution and placement flags/priority remain TODO. |
| `CMapObj__VectorIntersectPortal` (`0x7AF280`), `NTempest__Intersect` plane/polygon forms (`0x982FB0`, `0x9830D0`) | Verified the portal query intersects group MOGI bounds, intersects the ray with portal planes using a 0.0001 parallel threshold and 0.1 near-plane tolerance, tests the polygon in its major-axis projection, and assigns near/other groups from signed plane distance and MOPR side. A portal at or before the best geometry hit can override the face-selected viewer group; exterior near-side rejects the WMO as a containing interior. | **Partial port:** 3.3.5 viewer location and portal traversal use that near-side override, secondary interior group, and plane thresholds. Exact portal ordering and client 1.05 initial segment fraction remain to match. Modern viewer selection is unchanged. |
| `CMapObjGroup__GetTris` (`0x7CB0C0`), `CMapObjGroup__BspWalkRay` (`0x7CA180`), `CMapObjGroup__EmitLeafFaces` (`0x7C9A00`), `CMapObjGroup__TestRayFace_FlagGated` (`0x7C6C30`) | Verified MOBN leaf traversal through MOBR face references, MOPY flag gating, and nearest face intersection. The camera query passes flag 0, so the face gate excludes only the transient visited bit. | **Partial port:** MPQ viewer queries test only the MOBR-referenced face set, with the client's 0.002 barycentric edge tolerance. The DX11 ray still scans those faces without MOBN spatial traversal or exact tie order. Later-client geometry retains its existing path. |
| `CMapObj__TestGroupBounds` (`0x7AE880`), `NTempest__Intersect` (`0x983490`) | Verified the camera ray is broadphase-tested against the root MOGI group box before the collision BSP; the triangle intersection accepts both face orientations and a 0.002 barycentric edge tolerance. | **Ported viewer checks:** 3.3.5 viewer selection uses the MOGI group box and the client's face-edge tolerance. Modern group queries retain their prior bounds and strict edges. Exact BSP traversal and tie order remain TODO. |
| `DayNight_UpdateColorsAndFogRate` (`0x7ECD80`), `DayNight__CalcFogRate` (`0x7ECD00`) | Traced band-end floor 10, expansion threshold 27.777779, rate based on `min(farClip, 700) - 200`, then band-end replacement with far clip. | **Ported outdoor subset:** sampled data and active validated far clip feed the per-frame fog buffer; boundary fixtures pass. Weather and override sources remain. |
| `DayNight__UpdateFog` (`0x7F16F0`) | Traced base `min(bandEnd, farClip)`, start-scalar multiplication, override selection, underwater target, portal weight, and update effects. The portal blend publishes common distances/rate to both fog banks, but staged RGB stays outdoor while current RGB blends. | **Partial:** outdoor and camera-WMO portal distances/rate feed materials with staged/current color selection. Override, underwater liquid, and update effects remain. |
| `CShaderEffect__SetFogParams` (`0x873210`), terrain and `MapObjDiffuse_T1` BLS vertex programs | Traced CPU scale/bias/rate setup. Both compiled programs emit `min(pow(max(viewZ * scale + bias, 0), rate), 1)` as fog visibility. | **Ported outdoor subset:** ADT, WMO, M2, and liquid shaders interpolate client-style vertex fog visibility, blend RGB, and preserve alpha. All changed shader stages compiled with `fxc`; WMO/M2 `Unfogged` material flags are active. Visual comparison remains. |
| `CMapObj__QueryViewerFog_PortalDist` (`0x7A1150`), `CMapObj__BlendViewerFogParams` (`0x7A0CD0`), `CImVector__LerpRgbByAlpha` (`0x6ACC50`) | Traced MFOG record 0 base, first viewer group's four fog IDs, radius/flag filtering, farthest-first then nearest-last blends, and byte RGB interpolation. | **Partial port:** wowlib MFOG records and MOGP IDs feed the live primary-camera-WMO query; pure volume and color-order fixtures pass. Viewer location now records the portal-side second group, but fog sampling still uses the first as traced; liquid state remains. |
| `CMapObj__DistFromClosestExtPortal` (`0x7D8010`, `0x7D77C0`), `NTempest__DistanceFromPolygon` (`0x984E50`) | Traced group graph depth 3, MOGI exterior mask `0x48`, immediate-backtrack skip, polygon distance, and strict 25-unit portal range. | **Partial port:** tested local-space polygon distance feeds the live portal blend; exact Tempest polygon tolerances remain to compare. |
| `CWorldScene__UpdateFogStateForBatch` (`0x7A8440`), `DayNight__BuildUnderwaterFogFromMFOG` (`0x7ED1B0`) | Traced staged fog for terrain/exterior WMO, current fog color for camera-interior WMO, and an underwater target with minimum end 30 and expansion rate. | **Partial port:** the renderer chooses one camera WMO, stages outdoor RGB with blended distances/rate for the scene, and binds current RGB for its portal-propagated interior WMO batches. Underwater liquid flags and exact viewer placement remain TODO. |
| `CM2SceneRender__SetupLighting` (`0x81FB10`), `g_m2BlendModeFogModeTable` (`0xA45390`) | Traced blend modes 0–6 to fog modes `{1,1,1,2,2,3,4}`. Mode 1 uses the M2 render-state fog RGB, modes 2/3/4 use black/white/byte-gray, and material `Unfogged` or a nonpositive render-state fog-enable scalar disables fog. `CM2SceneRender__DrawRibbon` and `DrawParticle` call this setup, while `CRibbonEmitter__Render` (`0x980B70`) applies its own per-material fog flag. | **Partial port:** the 3.3.5 M2 mesh shader chooses the blend-family fog RGB; modern M2 fog remains on its previous path. Both changed M2 shader stages compile with `fxc`, and table/flag fixtures pass. The per-instance mode-1 RGB/enable scalar and ribbon/particle fog participation still need a client-state trace and live port. |
| `CM2Lighting__SetupSunlight` (`0x835280`), `CMapChunk__SelectLights` (`0x7B3F30`) | Anchored material-light selection. | **TODO:** trace directional/local terms and validate against BLS variants. |
| Terrain, M2, and MapObj BLS specular variants; `CM2Lighting__SetupSunlight` (`0x835280`), `CMapObj__ApplyLightToGxDevice` (`0x834B50`) | Shader bytecode is available locally; the exact 3.3.5 specular transfer and material participation have not yet been decoded. | **TODO:** maintain separate terrain, M2, and WMO specular work items with shader constants, light source, blend/fog/shadow order, visual captures, and modern-profile regression checks. WMO's approximate specular branch stays disabled. |
| `CMapDoodadDef__SelectLights` (`0x7C1150`), `CMapObj__ApplyLightToGxDevice` (`0x834B50`), `DayNight__UpdateAreaLightInfluence` (`0x77EED0`) | Anchored WMO/doodad/area-light sources. | **TODO:** local/interior lighting, polygon influence, material flags, and per-object behavior. |
| `CWorldParam__Initialize` (`0x78E400`), `CWorldParam__FarClip` (`0x78D780`), `World__SetFarClip` (`0x780800`), `World__ValidateFarClip` (`0x780770`) | Traced `farclip` CVar default 350, minimum 183.33333, and map/memory/override dependent maxima 791.66669 or 1583.3334. | **Ported subset:** the 3.3.5 settings control, map/memory-dependent validator, camera far plane, culling distances, and outdoor fog share the effective value. Windows physical memory is queried once; a failed query falls back to the expanded-memory branch. |
| `CWorldParam__FarClipOverride` (`0x78DC30`), `CWorldParam__NearClip` (`0x78D7A0`), `World__SetNearClip` (`0x77F490`) | Traced `farClipOverride` default 0. Although a `nearclip` CVar is registered with default 0.2, its callback calls `World__SetNearClip`, which unconditionally writes 0.2. | **Ported subset:** saved `farClipOverride` setting and fixed 3.3.5 near plane 0.2 reach the camera. No editable nearclip control is exposed. |
| `CWorldParam__EnvironmentDetail` (`0x78DC60`) | Traced `environmentDetail` default 1.0, clamped 0.5–1.5 before fade-distance scaling. | **TODO:** connect cloud/scene fade behavior before showing a control. |
| `DayNightInitialize` (`0x7F2790`), `CWorldParam__CloudLODCallback` (`0x7F1CD0`), `DNClouds__AllocForLOD` (`0x7F1B10`) | Traced `SkyCloudLOD` CVar default 0, clamp to integer 0–3, texture sizes 128/256/512/1024, and cloud-buffer reallocation on change. | **Ported subset:** saved Settings control clamps 0–3 and reaches the versioned renderer. A new LOD generates on a worker and the pair of GPU textures is replaced on readiness. Settings persistence and size tests pass; measure LOD transitions and compare high-LOD pixels with the client. |
| `FFXEffectGlow__constructor` (`0x8BFE80`), `FFX__CVarCallback` (`0x8C02A0`) | Traced `ffxGlow` CVar and on/off callback. | **Partial:** existing editor glow toggle is now also in Settings; compare the actual 3.3.5 effect strength/composite before calling it verified. |

### Client-configurable controls

Only settings proven to be client configuration or CVars should appear as
client-like controls. Client-fixed sky geometry, curve keys, byte bias, and
skybox threshold remain named constants in `Wrath335SkyReference`, not sliders.
The `ffxGlow` CVar is exposed through the existing saved glow toggle in the
viewport and Settings window. The 3.3.5 `SkyCloudLOD` CVar is exposed as a
saved 0–3 setting, and a changed value selects the matching cloud texture size.
The editor already exposes terrain/model draw
distances, but they are separate optimization controls; do not label either as
an exact `farclip` equivalent until the client clamp and fog behavior are wired.
Track `farclip`, `farClipOverride`, and `environmentDetail` above and add their
controls as their effects become implemented. The registered `nearclip` CVar
does not change the 3.3.5 world near plane and must not be exposed as a live
lighting option. Keep later-client
rendering settings independent where behavior differs.

## Implementation sequence

### 0. Freeze reproducible reference cases and extract a compact IDA dossier

1. Record the client and editor camera transform, map/area, time, weather,
   far clip, FOV, resolution, graphics options, and color settings for matched
   captures. Include clear dawn/noon/dusk/midnight, local-light falloffs,
   skybox transitions, Northrend fog, a WMO portal/interior, and underwater.
2. Trace from `CGWorldFrame__UpdateDayNight` (`0x4F8410`) through
   `DayNight__Update` (`0x7816F0`), `SetColors` (`0x7F3230`), and
   `RenderSky` (`0x7F09B0`) to the terrain, M2, WMO, and liquid draw paths.
   For each observed rule, store the input units, output range, default,
   ordering, source address, version, and a small expected-value fixture.
3. Resolve client structures and constants needed by later steps: the 18 int
   and six float band meanings; LightParams fields and weather variants;
   eleven hardcoded Northrend polygon lights (`0x77EED0`); sky/planet/star
   curves; cloud permutation, LOD frequency tables, and noise rounding;
   fog constants and shader math. Use narrow named-function lookups, batched
   decompilation, and cross-references; inspect disassembly only where the
   decompiler is ambiguous. Cache the results in this document or a small
   adjacent fixture file, with database address and build attached.
   For material shaders, inspect the matching WFX pass and BLS profile/variant
   first; use Wisp and Benilla as secondary readable references.
4. Audit existing rendering tests and the current feature ledger. Establish
   a baseline capture and CPU/GPU frame-time and allocation measurements.

**Exit:** a rule inventory links every visible 3.3.5 effect to source evidence,
an engine integration point, and at least one validation case. Mark unknowns
explicitly; do not fill them with modern-client assumptions.

### 1. Build a version-aware environment evaluator

1. Keep `WoWRenderLib` responsible for decoded, CPU-only client data. Keep
   `WowlibFileSystem`, `WowlibDBCProvider`, and wowlib WMO/M2/BLP readers as the
   only client-file access paths. Use DBD metadata to identify columns, while
   validating each build's actual schema. Reconcile the separate catalog and
   one-row LightData loaders so they expose consistent typed values.
2. Introduce an immutable environment-frame contract containing typed ambient,
   direct, specular/shadow, sky bands, skybox layers, celestial, cloud, fog,
   liquid, and active-light information. Keep the existing
   `WorldLightingSettings`/`SceneManager` facade usable during migration.
   Carry source presence separately from zero-valued colors.
3. For 3.3.5, implement client band time wrapping, component conversion and
   truncation, float-band-0 distance units, LightParams choice (including
   clear/storm and underwater variants), map/default fallback, distance and
   polygon priority, weather blending, manual overrides, and map time override.
   Audit the current legacy cloud-band names against the client's actual index
   use before feeding the cloud shader. Keep historical client quirks when
   they visibly affect 3.3.5.
4. Keep the later LightData path separate where semantics differ. Gate by
   detected client capabilities/build, not one broad `legacy` boolean. MPQ
   assets retain the path supplied at load time. CASC FileDataIDs are
   authoritative when available; a source path is shown only when provided
   by the client. A user listfile remains optional. Respect the 6.0–21796
   `FileDataComplete.db2` name mapping, filename loading through build 30080,
   and ID-only loading afterward, only for applicable clients.

**Exit:** focused CPU tests reproduce reference time, byte, unit, fallback,
weather, local-light order, and overlap fixtures; existing modern loader and
lighting tests remain green.

### 2. Match the sky dome and skybox compositor

1. Put a dedicated sky-dome renderer and cached geometry in
   `WoWRenderLib.DX11/Renderer`. Port the 3.3.5 ring positions, vertex colors,
   azimuth glow, fog ring, scissor/depth range, and draw order from
   `DNSky__Build`, `DNSky__SetColors`, and `DNSky__Render` (`0x9ACB00`).
   Keep the modern sky path independently selectable.
2. Match skybox weights and flag behavior, including combining/suppression,
   flag 0x1 model animation, and the distinct celestial skybox where supported
   by that client. Verify flag 0x4 separately; the traced 3.3.5 dome path does
   not apply the later-client fog-color override. Reuse M2 caches and keep any MPQ
   synthetic cache key internal; UI labels must show the real MPQ path.
3. Restore DX11 depth, blend, rasterizer, shader-resource, and scissor state
   expected by the following terrain pass. Expose draw and submission stats.

**Exit:** matched clear-sky captures at several times and camera pitches,
including a local skybox crossfade, show correct horizon and overlap behavior.

### 3. Add sun, moons, stars, and glare

1. Extract the 3.3.5 sun/moon position, size, tint, night intensity, star
   opacity, and occlusion curves. `DayNightInitialize` (`0x7F2790`) names the
   client sun/moon textures and stars M2; load them by their MPQ paths through
   wowlib. Use the existing M2 animation machinery for stars when compatible.
2. Add a celestial renderer for textured billboards, horizon clipping, sky
   depth/order, and the relevant sun/moon glare and visibility tests. Drive
   the directional light and sky-facing effects from one evaluated clock.
3. Verify specular and shadow color against `DayNight__Update` and
   `CM2Lighting` before applying them across material passes.

**Exit:** reference-position and visibility fixtures plus dawn/noon/night
captures agree without illuminating surfaces from a below-horizon sun.

### 4. Add the procedural cloud cap

1. Extract the fixed noise permutation and LOD/frequency constants from IDA.
   Reproduce the density LUT (`0x7EDB50`), ring mesh (`0x7F20E0`), bump-light
   selection (`0x7EFAE0`), and texture generator (`0x7EFD00`). The client uses
   the moon at night and sun by day for cloud highlights. Confirm the exact
   mapping of legacy cloud body/emissive/ambient band indices.
2. Allocate cloud textures and mesh once per LOD/map. Follow the client's
   default eight-row incremental update and ping-pong behavior; upload changed
   rows only. Integrate cloud opacity, weather, skybox suppression, and blend
   state after the dome, matching `DNClouds__Render` (`0x9ACD40`).
3. Use a fixed seed for deterministic tests. The client initializes part of
   its cloud gradient from process RNG, so ordinary client runs cannot be
   expected to have identical cloud pixels without reproducing its seed.
   Compare geometry, statistics, colors, motion, and density distribution as
   well as seeded synthetic outputs.

**Exit:** clouds respond correctly to time, weather, LOD, and density with no
texture recreation or synchronous I/O in the frame loop.

### 5. Apply client fog to every affected pass

1. Port outdoor start/end/color/rate derivation and expansion behavior from
   `DayNight_UpdateColorsAndFogRate`, `DayNight__CalcFogRate` (`0x7ECD00`),
   `UpdateFog`, and `CShaderEffect__SetFogParams`. Determine the actual shader
   transfer function from the client shader path; do not infer it solely from
   the CPU constant setup. Tie the far clip to the active camera setting.
2. Carry wowlib-decoded WMO root MFOG records and group fog IDs into CPU scene
   structures. Implement nearest-last fog-volume blending and portal-distance
   transitions from `CMapObj__QueryViewerFog_PortalDist` (`0x7A1150`). Apply
   staged/current fog selection, WMO `Unfogged`, and underwater MFOG/liquid
   rules only where the client does (`0x7A8440`, `0x7ED1B0`).
3. Share typed fog parameters across terrain, M2, WMO, liquid, and effect
   shaders, but preserve each material's participation and pass order. Keep
   sky and celestial passes on their client-specific fog paths. Test fog
   boundaries, portal transitions, far clip changes, and disabled fog.

**Exit:** matched outdoor, interior, underwater, and unfogged-material captures
have the right colors and distance response; fog does not leak across passes.

### 6. Complete light application across scene materials

1. Trace and port 3.3.5 global sunlight, terrain specular/shadow response,
   M2 material/light behavior, WMO MOLT local lights, doodad and group interior
   lighting/MOCV queries, WMO directional shadow receiving, and liquid light
   inputs. Start with `CM2Lighting__SetupSunlight` (`0x835280`),
   `CMapChunk__SelectLights` (`0x7B3F30`), `CMapDoodadDef__SelectLights`
   (`0x7C1150`), and `CMapObj__ApplyLightToGxDevice` (`0x834B50`), then follow
   their material/shader callers. Track terrain, M2, and WMO specular as three
   separate shader/material tasks, including light-bank selection, exponent,
   blend order, and the material flags that enable each term. Validate each
   family before editing common HLSL terms.
2. Keep renderer-specific constant buffers and state in focused renderers.
   `SceneManager` only orders passes and publishes the immutable frame. Batch
   shared light/fog updates once per frame, and maintain existing culling,
   instancing, terrain LOD, effect caches, glow, and diagnostic views.
3. Add weather/spell visual inputs and client overrides as explicit editor
   state only where those systems are present. Default absent effects to the
   reference clear-state behavior rather than inventing data.

**Exit:** matched exterior/interior and material-family captures show correct
lighting across terrain, M2, WMO, doodads, and water without modern-client
changes caused by 3.3.5 constants.

### 7. Integrate editor controls and prove compatibility

1. Extend immutable renderer-to-UI projections and `LightingViewModel` for
   final sampled environment values and source contributions. Keep XAML in
   views, commands/state in view models, and client logic in services. Show
   source paths/IDs according to the repository asset-identity rules.
2. Preserve fixed time, local-clock mode, map navigation, wireframe, geometry
   toggles, profiling, and background loading. Check map switch, device resize,
   resource disposal, and deferred asset readiness.
3. Run a compatibility matrix on 3.3.5 MPQ plus representative Classic 1.15,
   Classic 1.60, and available later CASC clients. For each, check loading
   without a user listfile, sky/lighting/fog presence, asset labels, and
   absence of 3.3.5-only effects on unsupported builds.

**Exit:** editor controls reflect final rendered state, version boundaries are
explicit, and the existing modern render paths still load and render.

## Verification and change discipline

- Make one architectural or behavioral move per batch. After each completed
  code, configuration, shader, or test batch, run the repository-root smoke
  command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1`.
  Capture the full output in a temporary log, await completion, and report
  exit code and suite totals. Use `-Scope Render`, `-Scope DX11`, or
  `-Scope Avalonia` for relevant development checks; an unchanged state needs
  no duplicate run. A nonzero full smoke result blocks completion.
- Add focused behavior tests for client-derived math and boundaries, shader
  compile/layout checks where needed, and integration tests that prove a
  parameter changes the intended pixels or draw state. Do not add tests that
  merely repeat the implementation.
- Compare matched client and editor captures by named regions: sky/horizon,
  celestial, cloud, terrain, WMO exterior/interior, M2, and water. Record
  differences and set numerical tolerances from the captured reference noise
  and precision before calling an area visually complete. Static tests alone
  do not establish render parity.
- Track CPU/GPU pass timings, draw and submitted-index counts, Gen0 allocation,
  cloud upload bandwidth, and resource lifetimes against the phase-0 baseline.
  Preserve performance work unless a measured change is required for visible
  accuracy; keep debug/wireframe modes separate from ordinary rendering.
- Update `docs/RENDERING_FEATURE_STATUS.md` as each feature is implemented and
  visually verified. Do not mark a feature complete from data loading alone.

This document is the plan and compact reference index. Update it as the port
advances, and keep unverified behavior clearly marked.
