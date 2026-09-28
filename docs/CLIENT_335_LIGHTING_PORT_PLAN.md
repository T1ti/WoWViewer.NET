# 3.3.5 client lighting port plan

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
  material-shader reference. The directory contains 592 BLS variants and five
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
| Celestials | The 3.3.5 sun and moon1 submit textured DX11 billboards before the dome, using wowlib MPQ paths and the async BLP cache. The stars M2 now loads by its MPQ path and draws ahead of both bodies with the client night fade. Moon02 is loaded but omitted in the normal clear state because its client tint RGB is zero. | The user's reference/editor screenshots support the sun and moon disc size and orientation. Stars still need a live visual comparison; cloud interaction, sun/moon glare, weather/override tint, and exact sky depth/scissor state remain. |
| Clouds | The 3.3.5 palette identifies rows 10/11/12 as emissive/body/ambient. The fixed cloud-cap mesh and startup density lookup are ported as tested CPU data; no GPU cloud pass exists yet. | `DNClouds__BuildMesh` (`0x7F20E0`) builds a 12-ring cap (177 vertices, 374 strip indices). `GenerateTexture` (`0x7EFD00`) uses four-octave value noise, a fixed permutation, per-process random gradient values, the density lookup, and incremental texture updates. Complete and validate those steps before drawing the cap. |
| Fog | Fog fields are read into `WorldSkyLighting`; the DX11 material shaders do not apply fog. | `DayNight_UpdateColorsAndFogRate` (`0x7ECD80`), `UpdateFog` (`0x7F16F0`), and `CShaderEffect__SetFogParams` (`0x873210`) define 3.3.5 distance fog. WMO MFOG, portal blending, material `Unfogged`, and underwater state also need wiring. |
| Surface lighting | Global ambient/diffuse reaches terrain, M2, WMO, and liquid shaders. | Audit terrain specular/shadow, M2 lights, WMO local and interior lighting, and material flags against the client before altering shared shaders. The existing rendering ledger already marks WMO interior queries, directional shadows, and terrain specular as open. |

The table distinguishes known mismatches from areas still awaiting a complete
reference trace. A value being loaded or shown in the Lighting window is not
evidence that it affects rendered pixels.

### Compact 3.3.5 rule record and implementation status

| Client evidence | Rule | Current implementation and remaining check |
| --- | --- | --- |
| `0x7EAE70`, `0x7EB070` | Light bands wrap over 2880 game-time units. The int sampler interpolates RGB components, truncates each to a byte, and writes opaque alpha. | Legacy band snapshots and the requested-time sampler use circular keys, component truncation, and `0xFF` alpha. Compare float precision against a client capture. |
| `0x7EAEF0`, `0x7EBFF0`, `0x7ECD80`, `0x7ECD00`, `0x7F16F0` | Float band 0 is divided by 36; band 1 is clamped to [-1, 1]. Outdoor fog floors band end at 10, optionally derives a rate, then limits end by far clip. | Band conversion and a pure 3.3.5 outdoor fog evaluator are implemented with numeric fixtures. It still needs the active client far clip, renderer/shader integration, and portal/underwater transitions. |
| `0x7ECB30` | Use the last exact zero-coordinate Light row for the current map, otherwise record ID 1. | Implemented for the 3.3.5 profile; later clients retain their existing selection. |
| `0x7ED0A0`, `0x7F1360`, `0x7EE5D0` | Process radial lights farthest first. For source positions within `0.33333334`, compare inner falloff radius instead. | Implemented for the 3.3.5 profile with focused overlap tests. Verify exact heap behavior at equal priority and dynamic override sources. |
| `0x7EE750` | The shadow modulation alpha is the red byte of packed int band 8, normalized by 255 when exposed as opacity. | The 3.3.5 data path extracts this byte; the modern scalar field remains separate. Shadow receiving on materials is still open. |
| `0x7EFAE0` | Cloud row 10 is emissive, 11 body, 12 ambient; use the moon before 04:50 and after 22:10, sun otherwise. Weather reduces the emissive scale. | The 3.3.5 palette and editor labels are corrected; other legacy clients retain their prior cloud mapping. Cloud shading and weather rendering remain open. Row 13 stays an unassigned 3.3.5 value until its consumer is traced. |
| `0x7F2470`, `0x7F0530`, `0x7ED2D0`, `0xAF4B78` | The sky has 24 azimuth segments and seven elevation rings. Its elevation uses the client's cubic cosine approximation. Glow colors are sampled at vertices, and each CImVector interpolation truncates RGB after subtracting the 0.5 floor bias. | Implemented in the 3.3.5 dome path, with geometry and glow-boundary tests. The user confirmed the attached screenshot's temporal color seam is fixed in the editor. Matched client capture validation remains open. |
| `0x7F09B0` | A drawable skybox above 0.99 opacity suppresses stars, celestial bodies, dome, and clouds when its combine flag (bit 1) is clear. | Dome, stars, sun, and moon1 share the 3.3.5 gate; threshold, combine, and readiness boundaries have focused tests. The cloud pass and separate blend-sky override remain open. |
| `0x7F0530`, `0x7F3230`, `0x7F30C0`, `0x7ECF20` | The traced 3.3.5 sky pipeline reads skybox flag 1 for full-day animation and flag 2 for combine. `DNSky::SetColors` reads sampled sky bands directly; no flag-4 fog-color replacement appears in these functions. | The 3.3.5 dome now retains sampled sky bands when flag 4 is present; the existing later-client color-override path remains separate. Check other flag consumers before declaring flag 4 globally unused. |

The legacy requested-time sampler retains the raw channel keys and memoizes
one evaluated time per LightParams ID. The union-key snapshots remain available
for diagnostics and other pre-LightData clients; the 3.3.5 profile samples the
raw curves so it does not interpolate already-truncated bytes a second time.
The latest complete smoke check passed 15 core, 149 DX11, and 178 Avalonia tests
(342 total). The dome and celestial sky shaders compiled as VS 5.0 and PS 5.0
with the Windows HLSL compiler. This establishes CPU behavior and build
compatibility. The user confirmed the dome seam fix and provided qualitative
reference/editor comparisons for sun and moon discs; controlled matched-time,
matched-camera pixel measurements and a visual stars check remain open.

### Client function coverage ledger

This ledger covers every 3.3.5 function currently identified for this port.
**Traced** means the relevant rule was checked in IDA; **anchored** means the
function was located but its complete behavior still needs analysis.
**Ported** means the stated subset has focused tests or a shader compile;
**partial** means only some outputs or branches are used. The dome seam was
visually verified by the user in the editor. The user also compared sun and
moon discs with reference-client screenshots; cloud/star layers and glare
prevent full scene parity. Update this table whenever a function
is traced, implemented, or capture-verified; retain TODO rows until resolved.

| Client function (build 12340 address) | Evidence | Engine status and remaining proof |
| --- | --- | --- |
| `CGWorldFrame__UpdateDayNight` (`0x4F8410`), `DayNight__Update` (`0x7816F0`), `DayNight__SetColors` (`0x7F3230`) | Anchored frame/update chain. | **Partial:** timed world catalog and directional settings exist. Trace exact update order, weather/spell overrides, map time override, and every renderer input. |
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
| `DayNight__RenderSky` (`0x7F09B0`) | Traced drawable opaque skybox suppression, order, viewport scissor. | **Partial:** stars, sun/moon1, and dome share the suppression gate and draw in client order. Blend-sky override, scissor, clouds, and matched layer captures remain. |
| `DayNight__GetOverrideSky` (`0x7F30C0`), `AnimateSkyModel` (`0x7ECF20`), `DayNight__DrawSkyModel` (`0x7F08C0`) | Traced flags stored on sky model, full-day animation flag 1, and two scene draw phases. | **Partial:** skybox M2 animation clock and async draw exist; compare sequence refresh threshold, pass ordering, client material results, and any other flag-4 consumers. |
| `DayNight__SetPlanets` (`0x7EECC0`), `DayNightInitialize` (`0x7F2790`) | Traced sun, moon1, and moon02 polar/azimuth/size curves; cubic direction and radius 12; 1.7-day moon02 phase; prominence ramp; texture paths `Textures\sunCenter.blp`, `Textures\moon.blp`, `Textures\moon02.blp`. | **Partial render, qualitative visual check:** typed frame evaluator and numeric fixtures pass; all three client paths resolve once through wowlib and stream through the BLP cache. Sun and moon1 draw. The user's reference/editor images support their size, direction, and orientation; matched-time color, weather tint, and billboard pixels remain. |
| `DayNight__BuildCelestialQuad` (`0x7EDBE0`), `DayNight__ClipCelestialQuadToHorizon` (`0x7EDEE0`), `DayNight__RenderCelestialBody` (`0x9AC660`) | Traced four/six-vertex quad, full size, camera-height horizon clip, 0.4-unit vertex-alpha fade, alpha blend, and disabled fog/lighting/depth-write. | **Partial render, qualitative visual check:** horizon/fade fixtures and the sun/moon1 GPU draw pass exist, with client alpha blending and no depth write or fog. The user reports body size and orientation look accurate. Verify exact depth range, sampler/filter state, tint, and matched pixels. Moon02 clear-state RGB stays zero; weather/override color remains TODO. |
| `BuildOrthonormalBasisFromDirection` (`0x9ABB60`) | Traced normalized view-forward basis, its `1e-5` horizontal fallback, and cross-product third axis. The 4/6 client quad indices are triangle strips `[0,1,2,3]` and `[0,1,4,5,2,3]`. | **Ported subset, qualitative visual check:** basis fixtures pass and DX11 uses equivalent triangle-list topology. The user reports matching sun/moon orientation; verify UV handedness at cardinal headings in controlled captures. |
| `DayNight__DNStars__Init` (`0x9ABB00`), `DayNight__DNStars__UpdateImpl` (`0x7EE0D0`), `DNStars__Render` (`0x9ABD50`) | Traced the client model request `Environments\Stars\stars.mdl` (the extracted 3.3.5 asset is `Stars.m2`), camera-centered update, and alpha byte `(int)(curve*254+1)` from keys `(0.125,1)`, `(0.1875,0)`, `(0.9375,0)`, `(1,1)`. Render skips alpha bytes below 2 and uses byte/255 model opacity in two M2 scene passes before the sun. | **Partial render:** the MPQ M2 path resolves through wowlib; the animated M2 sky renderer centers it on the camera, applies the byte/255 night fade, and draws before sun/moon. Alpha and wrap tests pass. Compare model orientation, animation/material phases, and transition captures with the client. |
| `DayNight__DNClouds__SetupBumpMap` (`0x7EFAE0`) | Traced row 10/11/12 colors and sun/moon handoff. | **Ported data, rendering TODO:** palette tests pass; cloud shading and weather inputs remain. |
| `DayNight__DNClouds__constructor` (`0x7F04B0`), `DayNight__DNClouds__InitNoiseTables` (`0x7ED250`) | Traced four octaves, two mips, 8 rows per update, scroll phase speed 2; the fixed permutation is at `0xAF4A70`, gradients are 256 process-random values in [-1,1], and fade is `(1-cos(k*pi/256))/2`. | **TODO:** port the sampled noise and incremental update state. Keep the random gradient distribution; capture a seed when comparing exact cloud pixels. |
| `DayNight__DNClouds__BuildMesh` (`0x7F20E0`), `DayNight__DNClouds__BuildDensityLUT` (`0x7EDB50`) | Traced 12-ring/16-azimuth cap, ring alpha bytes, UV disc, 177 vertices/374 strip indices, and one-time lookup `trunc(255 - 0.96^(k*(255-coverageByte)/256)*255)` with startup coverage 0.6. | **Ported CPU data:** mesh and density LUT have focused topology/byte tests. Upload fixed geometry and connect the lookup to generated cloud pixels; inspect runtime edge behavior against the client. |
| `DayNight__DNClouds__Update` (`0x7F1010`), `DayNight__DNClouds__GenerateTexture` (`0x7EFD00`), `DayNight__DNClouds__Render` (`0x9ACD40`) | Traced four-octave 3D value noise, LUT and per-row RGBA shading, 8-row incremental updates, ping-pong textures, then a no-depth-write alpha-blended cap after the dome. The 16-bit scroll phase changes only after a full texture pass. | **TODO:** port noise/texture updates and draw the cap with the client ordering and GPU states; verify LOD, weather response, and frame budget. |
| `DayNight__DNClouds__WorldToTexture` (`0x7EF920`), `DayNight__DNClouds__SampleDensity` (`0x7EFA30`), `DayNight__DNClouds__SampleInverseDensity` (`0x7F1020`), `DayNight__DNClouds__Collide` (`0x7EDA90`) | Density sampling uses the generated 8-bit buffer at a projected/clamped texel and scales by 1/255; inverse density returns one minus that sample. Transform and collision paths are anchored. | **TODO:** complete world-to-texture and collision tracing, then use the same density buffer for cloud/glare interaction. |
| `DayNight_InitializeSunGlare` (`0x7EE150`), `DayNight__InitializeMoonGlare` (`0x7EE230`), `DayNight_InterpolateValue` (`0x7EF6E0`) | Traced `Textures\sunGlare.blp` and `Textures\moonGlare.blp`; time, angle, visibility, skybox, and cloud response alter glare size/alpha. | **TODO:** implement the client glare response and textures after cloud coverage/visibility inputs are available; the user's sun comparison shows this missing effect. |
| `DayNight_CelestialBody__Render` (`0x9AC3C0`), `DayNight__IsSunVisibleFromPoint` (`0x9ABC60`) | Traced an occlusion query or world/M2 visibility test for glare. | **TODO:** connect scene visibility so glare cannot show through occluders; verify near clouds and terrain at matched camera views. |
| `DayNight__DrawTexturedViewports` (`0x7F0870`), `Gx_DrawTexturedViewport` (`0x9AC400`), `CGWorldFrame__OnWorldRender` (`0x4F8EA0`) | Traced post-world sun/moon glare quads before the FFX glow composite, with additive blend and disabled fog, depth test/write, lighting, and cull. | **TODO:** add an ordered DX11 pass after world/particles and before glow; test that glare responds to occlusion and cloud cover. |
| `DayNight_UpdateColorsAndFogRate` (`0x7ECD80`), `DayNight__CalcFogRate` (`0x7ECD00`) | Traced band-end floor 10, expansion threshold 27.777779, rate based on `min(farClip, 700) - 200`, then band-end replacement with far clip. | **Ported CPU subset:** pure 3.3.5 evaluator and boundary fixtures pass. Feed validated active far clip and the expansion-mode decision into the renderer frame. |
| `DayNight__UpdateFog` (`0x7F16F0`) | Traced base `min(bandEnd, farClip)`, start-scalar multiplication, override selection, underwater target, portal weight, and update effects. | **Partial CPU subset:** base outdoor distances are modeled; override, portal, liquid, update flags, and material fog application remain. |
| `CShaderEffect__SetFogParams` (`0x873210`), `CMapObj__QueryViewerFog_PortalDist` (`0x7A1150`) | Traced CPU shader constants `-fogCoordScale/(end-start)`, `end/(end-start)`, rate, and RGB/255; portal query is anchored. | **TODO:** exact shader transfer, WMO MFOG, portals, and `Unfogged` participation. |
| `CWorldScene__UpdateFogStateForBatch` (`0x7A8440`), `DayNight__BuildUnderwaterFogFromMFOG` (`0x7ED1B0`) | Anchored transitions. | **TODO:** volume selection and liquid/WMO interactions. |
| `CM2Lighting__SetupSunlight` (`0x835280`), `CMapChunk__SelectLights` (`0x7B3F30`) | Anchored material-light selection. | **TODO:** trace directional/local terms and validate against BLS variants. |
| `CMapDoodadDef__SelectLights` (`0x7C1150`), `CMapObj__ApplyLightToGxDevice` (`0x834B50`), `DayNight__UpdateAreaLightInfluence` (`0x77EED0`) | Anchored WMO/doodad/area-light sources. | **TODO:** local/interior lighting, polygon influence, material flags, and per-object behavior. |
| `CWorldParam__Initialize` (`0x78E400`), `CWorldParam__FarClip` (`0x78D780`), `World__SetFarClip` (`0x780800`), `World__ValidateFarClip` (`0x780770`) | Traced `farclip` CVar default 350, minimum 183.33333, and map/memory/override dependent maxima 791.66669 or 1583.3334. | **TODO:** apply this version-specific validation to camera, draw distance, and fog together. Existing editor terrain/model distances are separate controls and are not an exact CVar port. |
| `CWorldParam__FarClipOverride` (`0x78DC30`), `CWorldParam__NearClip` (`0x78D7A0`), `World__SetNearClip` (`0x77F490`) | Traced `farClipOverride` default 0. Although a `nearclip` CVar is registered with default 0.2, its callback calls `World__SetNearClip`, which unconditionally writes 0.2. | **TODO:** wire `farClipOverride` with farclip/fog; keep the fixed 0.2 near plane named in code, with no editable nearclip control. |
| `CWorldParam__EnvironmentDetail` (`0x78DC60`) | Traced `environmentDetail` default 1.0, clamped 0.5–1.5 before fade-distance scaling. | **TODO:** connect cloud/scene fade behavior before showing a control. |
| `DayNightInitialize` (`0x7F2790`), `CWorldParam__CloudLODCallback` (`0x7F1CD0`), `DNClouds__AllocForLOD` (`0x7F1B10`) | Traced `SkyCloudLOD` CVar default 0, clamp to integer 0–3, texture sizes 128/256/512/1024, and cloud-buffer reallocation on change. | **TODO:** implement cloud LOD and resource lifetime before exposing this client control. |
| `FFXEffectGlow__constructor` (`0x8BFE80`), `FFX__CVarCallback` (`0x8C02A0`) | Traced `ffxGlow` CVar and on/off callback. | **Partial:** existing editor glow toggle is now also in Settings; compare the actual 3.3.5 effect strength/composite before calling it verified. |

### Client-configurable controls

Only settings proven to be client configuration or CVars should appear as
client-like controls. Client-fixed sky geometry, curve keys, byte bias, and
skybox threshold remain named constants in `Wrath335SkyReference`, not sliders.
The `ffxGlow` CVar is exposed through the existing saved glow toggle in the
viewport and Settings window. The editor already exposes terrain/model draw
distances, but they are separate optimization controls; do not label either as
an exact `farclip` equivalent until the client clamp and fog behavior are wired.
Track `farclip`, `farClipOverride`, `environmentDetail`, and `SkyCloudLOD` above and add their
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
   their material/shader callers. Validate each material family before editing
   the common HLSL terms.
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
