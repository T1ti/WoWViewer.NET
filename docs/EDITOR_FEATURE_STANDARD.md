# Editor feature standard

This document defines the user-facing vocabulary, baseline features, and
behavioral contract for WTEditor modes and brush tools. The architecture
documents define the implementation boundaries used to reach this target.

## Vocabulary

- **Mode** is the active editing domain: Select, Terrain, or Texture. Exactly
  one mode owns viewport input at a time.
- **Tool** is an operation inside a mode: Sculpt, Smooth, Flatten, Paint, or
  Colour. The UI should call these tools rather than submodes.
- **Brush** is reusable spatial influence: mask, size, falloff, rotation, and
  sampling. It contains no terrain speed, texture opacity, or domain operation.
- **Target** is the value or asset affected, such as a height, texture, or colour.
- **Stroke** is one pointer gesture. One completed stroke is one undo entry.

Do not use mode, tool, brush, and target interchangeably in code or UI.

## Current implementation audit

| Area | Available now | Material gap |
| --- | --- | --- |
| Select | Object selection, inspection, transforms, shared undo | Multi-selection operations and transform snapping are outside the current tool scope |
| Terrain | Sculpt, Smooth, Flatten; fixed and stroke-centre targets; welded seams; rebuilt normals; undo/redo | Plain LMB does not apply: all operations currently require Shift or Control |
| Texture | Paint/Smooth/Colour panels, brush preview, chunk texture picker, favorite targets, opacity and strength input | No texture mutation, dirty state, persistence, or undo delta |
| Brushes | Hard/Linear/Smooth/Gaussian round and Hard/Smooth square; size and conditional falloff | No custom mask import, rotation, or stroke-spacing contract |
| Persistence | Terrain baseline, dirty tracking, and save handoff | No ADT writer, so edits cannot yet be committed to an ADT file |

Texture remains a preview-only shell until its mutation and undo path exists.

## Standard modes and tools

### Select

Select is the neutral/default mode. It owns picking, selection outlines, the
inspector, and object transforms. It never treats a click as a brush stroke.
Transform drags follow the same begin/update/end/cancel and undo rules as brushes.

### Terrain

| Tool | Primary action | Tool settings | Alternate action |
| --- | --- | --- | --- |
| Sculpt | Raise or lower vertex height | Rate | Invert direction |
| Smooth | Move each vertex toward its local-neighbour average | Rate/strength; optional advanced kernel radius | None |
| Flatten | Move vertices toward one common target plane | Rate; target mode; fixed height when applicable | Sample target |

Flatten target modes should be:

- **Fixed height**: a numeric world-space height.
- **Stroke centre**: height under the cursor when the stroke begins.
- **Brush average**: one height sampled from the affected area at stroke start.

Brush-average flattening is distinct from smoothing. Flatten applies one target
height to every affected vertex and produces a plane. Smooth calculates a
different local average per vertex and preserves broad contours.

Smooth iterations are an implementation parameter. The normal UI should expose
a stable Strength/Rate control. Iterations may live under Advanced if they offer
a useful deterministic quality/performance tradeoff.

Every terrain operation must weld duplicated seam vertices, rebuild affected
normals and bounds, mark dirty resources, and include derived changes in undo.

### Texture

| Tool | Required target | Tool settings | Primary behavior |
| --- | --- | --- | --- |
| Paint | Selected terrain texture/material | Opacity, strength/flow | Add the selected texture while keeping layer weights normalized |
| Smooth | All four texture-layer weights | Strength | Move the complete normalized weight vector toward its local-neighbour average |
| Colour | Selected colour | Opacity, strength/flow; blend mode if supported | Blend vertex colour toward the target colour |

Texture Smooth acts on all four layers together. At each alpha-map sample it
averages the neighbouring weight vectors, blends the current vector toward that
average by brush influence and Strength, clamps negative components, and
renormalizes the result to sum to one. It ignores the selected texture and
Opacity. Smoothing only one selected layer would bias the blend toward that
layer as the other three are renormalized, so that behavior belongs in a
separate specialized tool if a workflow later requires it.

In Paint, Control+LMB is reserved for the terrain-only chunk texture picker.
Erase therefore belongs in an explicit tool/action rather than sharing that gesture.

Opacity is maximum target coverage. Strength/flow controls how quickly repeated
samples approach it. A stationary stroke converges on the opacity limit instead
of accumulating indefinitely or changing with frame rate.

Texture is complete only when target selection, mutation, preview, seam-safe
data handling, dirty tracking, save handoff, and exact undo/redo are connected.

The texture target picker raycasts terrain only. Its popup preserves material
layer order, and clicking a tile makes that material active without implicitly
adding it to the user's palette. Favorites are a separate ordered collection:
the per-tile `+` action adds without duplication, the corner close action removes,
and clicking any favorite selects it. The favorites `+ Add` menu can merge every
unique material from the ADT beneath the current camera position. BLP thumbnails
are cached by file data ID;
renderer layer discovery remains image-framework independent.
Palette entries preserve the exact FileDataID stored by the ADT. Presentation
uses only the filename stem and hides the conventional `_s` suffix; do not
rewrite material identity merely to make an editor label look canonical.
Middle click samples the packed 64×64 chunk alpha maps at the raycast triangle's
barycentrically interpolated UV and immediately selects the dominant material.
Sampling mirrors the shader's bilinear clamp behavior and derives the base-layer
weight from the overlay sum. Equal weights resolve to the higher layer index.

## Standard brush model

Every brush-driven tool presents generic settings in this order:

1. **Mask**: grayscale spatial influence; black `0`, white `1`.
2. **Size**: world-space extent. Choose and display radius or diameter. The
   renderer currently consumes radius, so `Radius` is least ambiguous unless a
   diameter conversion is introduced.
3. **Falloff**: edge-transition width, for parametric masks only.
4. **Rotation**: visible only when rotating the mask changes its result.

The current Falloff value is a normalized full-strength core radius: `0%` fades
across the whole brush and `100%` is nearly hard. Users normally understand
that direction as Hardness. The standard should either label it **Hardness**, or
keep **Falloff** and display `1 - coreRadius`. The latter is recommended because
it preserves the chosen name while making `0%` mean no soft edge and `100%`
mean a transition across the entire radius.

Strength, opacity, terrain rate, smoothing kernel size, target height, texture,
and colour remain tool settings and never enter the generic brush contract.

### Built-in masks

The logical default library is small and task-oriented:

- Hard round: uniform circle for exact coverage.
- Smooth round: circular smoothstep edge; the general-purpose default.
- Gaussian round: centre-weighted soft work.
- Hard square: uniform axis-aligned square.
- Smooth square: square footprint with a smooth edge.

Linear round is a useful advanced predictable ramp and may remain built in.
Avoid generating every shape/curve combination. Each tool declares supported
masks, and a combination exists only when it serves an editing use case.

Custom image masks use the same normalized grayscale sampling contract. Baked
grayscale masks hide parametric Falloff. Rotation appears for custom and
non-radially-symmetric masks. CPU mutation, GPU preview, and thumbnails must
sample the same data.

### Stroke sampling

Texture and other stamp-based tools need world-space spacing so output is
independent of pointer-event and frame frequency. The default is a fraction of
brush size with an Advanced override. Terrain rate tools use elapsed time and
must also remain stable across frame rates.

## Standard input behavior

Editor-wide shortcuts take precedence over camera and tool input:

- `1`, `2`, `3`: Select, Terrain, Texture.
- `Ctrl+Z`: undo.
- `Ctrl+Y` or `Ctrl+Shift+Z`: redo.
- `Escape`: cancel the active gesture; otherwise return to Select.
- Right mouse drag: camera look/orbit.

The scalable pointer convention is:

- LMB applies the primary tool action.
- Control+LMB invokes inverse/erase when defined; Texture Paint reserves it for
  terrain chunk texture picking.
- Shift+LMB samples a target or invokes a documented secondary action.

Current Terrain input uses Shift for positive, Control for negative, and ignores
plain LMB. Migrate it so Smooth and Flatten work with LMB and all tools have an
obvious primary action. Sculpt can use LMB to raise and Control+LMB to lower.

Shortcut dispatch must come from registered mode/command metadata. Do not keep
a shortcut string for display and a separate hard-coded key switch for behavior.

## Standard tool panel

Every brush panel uses the same compact order:

1. mode title and close button;
2. square tool icons and highlighted active tool name;
3. brush mask grid, mask name, and generic settings;
4. tool-specific target and settings;
5. brief contextual controls/help in a tooltip or shared status area.

Labels, slider tracks, and values share one aligned row. Descriptions live in
tooltips. Irrelevant settings are hidden. Each mode retains its settings when
switching; settings may be retained per tool where that improves workflow.

## Completion contract for mutating tools

A tool is complete only when:

- preview and mutation use the same footprint, mask, transform, and target;
- begin/update/end/cancel exists and one stroke creates one global undo entry;
- cancel and undo restore exact authoritative state;
- redo restores the result including normals, bounds, and layer weights;
- seams and shared samples cannot diverge;
- results are stable across frame and pointer-event frequency;
- dirty state compares against the loaded/saved baseline;
- streaming cannot discard edits;
- save handoff includes the resource and clears dirty only after a successful write;
- active mode, tool, target, and modifiers are visible or discoverable;
- focused text/numeric controls do not trigger viewport shortcuts.

## Recommended implementation order

1. Make plain LMB apply Smooth and Flatten; centralize command shortcuts.
2. Resolve Falloff/Hardness direction and Size radius/diameter naming.
3. Replace normal Smooth Iterations UI with stable strength/rate semantics.
4. Complete Texture Paint vertically: target, normalized layer mutation,
   spacing, undo, dirty state, and save handoff.
5. Complete Colour with a colour target and the same lifecycle.
6. Add brush-average Flatten.
7. Add custom mask import, persistence, rotation, and shared sampling.
8. Add the ADT writer needed to persist terrain and texture changes.

This order completes visible tools before adding modes or brush algorithms.
