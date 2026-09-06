# WTEditor icon guidelines

## Source of truth

All custom Avalonia UI icons must be defined in
`WTEditor.Avalonia/Presentation/EditorIcons.cs`. Views consume those geometries
with `x:Static`; view models that expose an icon must return an `EditorIcons`
geometry. Do not duplicate path data in XAML or view models.

The catalog is a locally maintained WTEditor icon family. It is not currently
copied from an external icon library. When adapting an external icon, record its
library, icon name, URL, and license in a comment beside the catalog entry. Do
not mix multiple external families without first migrating the complete set.

## Existing icon sources

Use this preference order when a new icon is needed:

1. [Fluent Icons for Avalonia](https://avaloniaui.github.io/icons.html) is the
   preferred source. It provides paths already formatted as Avalonia
   `StreamGeometry` resources and visually matches Avalonia's Fluent theme.
2. [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)
   is the upstream catalog and source for SVG variants when the Avalonia page
   does not contain a suitable entry. System icons are MIT licensed; preserve
   the applicable license and notice when importing them.
3. [Google Material Symbols](https://fonts.google.com/icons) may be considered
   when Fluent has no adequate metaphor. Material icons are Apache-2.0 licensed.
4. [Lucide](https://lucide.dev/icons/) is an ISC-licensed fallback for concepts
   missing from both Fluent and Material.

Prefer one family for the complete application. WTEditor should use Fluent
Regular icons when suitable replacements are available. Do not introduce a
Material or Lucide icon beside Fluent icons solely because it is convenient;
adapt its proportions and weight or migrate the related group together.

For every imported icon, add a source comment in `EditorIcons.cs`, for example:

```csharp
// Fluent UI System Icons: globe_24_regular, MIT
// https://github.com/microsoft/fluentui-system-icons
public static Geometry World { get; } = Geometry.Parse("...");
```

The native window/application icon is the only exception: Avalonia loads it
from `WTEditor.Avalonia/Assets/avalonia-logo.ico`. Replace that temporary asset
with a WTEditor-branded multi-resolution `.ico` derived from the same visual
family; do not use it as the source for toolbar icons.

## Visual language

- Use a `24 x 24` coordinate space.
- Use simple, filled silhouettes with a consistent apparent weight.
- Keep the main shape inside approximately `2..22` on both axes so icons have
  comparable optical padding.
- Prefer one clear metaphor over decorative detail. Icons must remain
  recognizable at `14–20 px`.
- Use the control's `Foreground`; do not embed colors, gradients, shadows, or
  raster imagery in a UI icon.
- Avoid mixing emoji, Unicode pictograms, font glyphs, outlined icons, and
  filled icons.
- Represent editor concepts literally where possible: a globe for a world, a
  castle/building for WMO, cells for a grid, and a triangulated square for
  wireframe.

## Naming and use

- Name catalog members after the concept, not the screen where they first
  appear: `World`, `Wmo`, `Grid`, `Settings`.
- Reuse an existing member whenever the meaning is the same.
- In XAML, add `xmlns:icons="using:WTEditor.Avalonia.Presentation"` and bind with:

  ```xml
  <PathIcon Width="16" Height="16"
            Data="{x:Static icons:EditorIcons.Wmo}"/>
  ```

- In view models, assign `EditorIcons.Name`; never call `Geometry.Parse` for UI
  icon metadata outside the catalog.
- Every icon-only control must have a concise tooltip or accessible label.
- Use `14 px` for tabs, `16 px` for compact toolbar controls, and `20 px` for
  primary editor-mode controls unless a component establishes another shared
  size.

## Adding or changing an icon

1. Check whether an existing catalog icon communicates the same action.
2. Add or replace one geometry in `EditorIcons.cs` and inspect it at every size
   where it is used.
3. Search `WTEditor.Avalonia` for inline `PathIcon` data, `Geometry.Parse`, emoji,
   and symbol-font glyphs before review. New custom icon definitions should
   exist only in the catalog.
4. Verify checked, unchecked, hover, disabled, and high-contrast foreground
   states where applicable.
5. Run the repository smoke-test runner after any corresponding XAML or code
   change.
