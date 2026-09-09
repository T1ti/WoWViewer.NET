# Brush tool architecture

This is the extension contract for brush-driven modes in `WTEditor.Avalonia`.
It complements `EDITOR_ARCHITECTURE.md` with the shared brush boundary used by
Terrain and Texture.

## State ownership

- `BrushSettingsViewModel` owns the selected brush preset, size, and falloff
  size. Each tool owns an instance and declares its supported presets, so tools
  retain their settings without requiring identical brush libraries.
- Tool view models own domain settings. Terrain owns speed and terrain
  operation parameters; Texture owns opacity, strength, and the Paint/Smooth/Colour
  submode.
- Brush-driven tool view models inherit `BrushToolViewModelBase`, which owns
  the shared brush, submode selection, and panel lifecycle. Derived types keep
  only domain-specific settings and typed operation mapping.
- `MainViewModel` copies the active tool's brush into the viewport input bridge.
  Slider changes are configuration changes and never create undo records.
- Renderer `BrushInput` is the tool-independent contract: footprint, edge
  profile, size, and falloff size. Do not add speed, strength, opacity, texture
  selection, or operation modes to it.

Falloff is a capability, not a user toggle. `ToolSubModeViewModel.UsesFalloff`
declares whether an operation can use it, and a brush preset declares whether
it has a parametric edge. The UI shows the Falloff slider only when both are
true. A hard or baked-image mask therefore cannot expose a meaningless slider.

## Shared presentation

Brush-driven panels use `ToolSubModeSelector` for submodes and
`BrushSettingsControl` for mask selection and generic brush settings. Both panels live
in the same movable/resizable tool host in `MainView`. New modes should compose
these controls rather than copying their XAML.

Submodes are square icon buttons with descriptive tooltips. Brush choices are
square grayscale mask previews sampled from the actual CPU algorithm: black is
zero influence and white is full influence. They are not decorative glyphs.

Texture discovery is backed by `IClientFileCatalogService`. It creates one
cached snapshot from the active filesystem's authoritative `EnumeratePaths()`
inventory and replaces that snapshot when the client filesystem changes. UI
features must filter this catalog instead of walking the community listfile and
calling `Exists` for every candidate. The texture browser's flat search and
`tileset/` folder explorer are projections of the same catalog; palette items
retain their canonical full path even when the UI displays only the file name.

## Mask and falloff model

For a normalized brush coordinate `p`, effective spatial influence is:

```text
influence(p) = footprint(p) * edgeProfile(distance(p), falloff)
```

Circle uses Euclidean distance; Square uses Chebyshev distance. The falloff
value is the normalized full-strength core radius. The built-in profiles are:

- Hard: `1` inside the footprint and `0` outside. No Falloff slider.
- Linear: constant-rate fade from the white core to zero at the boundary.
- Smooth: smoothstep fade with zero slope at the core and outer edge.
- Gaussian: normalized bell-shaped fade that still reaches exactly zero at
  the boundary.

A “Smooth round” brush is therefore a circular footprint with a white core and
a smoothstep radial transition to black. It is not a smoothing operation: the
name describes the mask curve. Terrain Smooth is the domain operation applied
through that mask.

Size and strength are not redundant with the mask. Size maps the normalized
mask into world space. The mask distributes influence spatially. Tool strength
scales or rates the operation without changing that distribution. For future
texture application, opacity should be the stroke's maximum blend coverage,
while strength controls how quickly repeated samples approach that coverage:

```text
targetCoverage = opacity / 255 * influence(p)
step = 1 - exp(-rate * strength * deltaTime)
coverage = lerp(currentCoverage, targetCoverage, step)
```

This makes opacity and strength semantically distinct instead of multiplying
two interchangeable sliders. Terrain speed remains its units-per-second rate.

## Custom image masks

Custom masks should enter through an `IBrushMaskSource`-style abstraction, not
as another shape enum or a branch in terrain editing. The proposed import
contract is:

- square, single-channel linear intensity; black `0`, white `1`;
- alpha multiplies luminance when present;
- bilinear sampling in normalized brush coordinates and zero outside bounds;
- a stable asset ID/path plus a generated thumbnail cache;
- default `BakedOnly` falloff policy, so the image is used exactly as authored;
- optional `ParametricEdge` metadata only when a custom preset is explicitly
  authored to combine its image with the tool-defined falloff profile.

CPU tools sample the shared mask interface; GPU tools upload the same image as
a single-channel shader resource. The projected preview and mutation paths must
consume the same source and sampler rules. Arbitrary image import is the next
step; the current built-ins establish the contract without pretending that a
renderer texture-upload/persistence path already exists.

## Renderer rules

`SceneManager.UpdateBrushPreview` is the single projected-cursor path for all
brush tools. `BrushMath` owns footprint distance and falloff influence for CPU
operations; the ADT shader mirrors those rules for visual outlines. A new tool
provides a preview colour and its domain operation—it must not introduce a
second cursor raycast, ring renderer, or tool-specific brush shader.

Non-circular brushes may use a conservative spatial intersection radius while
retaining their actual operation radius. Square brushes use a circumscribed
circle for chunk culling and Chebyshev distance for per-sample influence.

Terrain mutation operates through `TerrainSurfaceEditor`. Each pass reads a
stable world-space height snapshot, welds duplicate vertices by position across
chunk and loaded-tile boundaries, then publishes the same result to every copy.
After positions change, triangle normals are accumulated across the same welded
surface before vertex upload. Keep seam handling and normal rebuilding in this
shared layer rather than implementing either concern inside Sculpt, Smooth, or
Flatten.

Flatten supports a numeric world height and a stroke-centre target. The latter
captures the terrain hit height once when the stroke begins. A brush-wide
average would still be a flatten operation because every affected point moves
toward one common plane; Smooth instead uses a different local-neighbour average
for each vertex and therefore preserves large-scale terrain contours.

## Adding a brush-driven mode

1. Register the typed mode, capabilities, icon, and shortcut together in
   `EditorModeDefinitions` and `EditorModeId`.
2. Create a mode-specific view model derived from `BrushToolViewModelBase`,
   declare its supported presets, and expose icon-bearing
   `ToolSubModeViewModel` entries with explicit falloff capability.
3. Compose the shared submode and brush controls in the mode panel; place only
   domain settings in its Tool Settings section.
4. Add typed domain input beside `BrushInput` in `InputFrame`.
5. Reuse `UpdateBrushPreview`, `BrushMath`, and `WorldChunkRange`; implement a
   domain mutation pipeline only when the tool begins changing persisted data.
6. Add tests for supported presets, falloff profiles, clamping, typed registration, brush
   influence, gesture grouping, undo/redo, and dirty-state transitions as
   appropriate.
