# Repository instructions

## Relevant projects

For WTEditor work, only `WTEditor.Avalonia`, `WoWRenderLib.DX11`, and their
transitive dependencies are relevant. Build `WTEditor.Avalonia` and its smoke
tests. The legacy `WoWViewer.NET`, `WoWViewer.NET.DX11`, `WoWRenderLib.OpenGL`,
and `WTEditor.WPF` projects can be ignored unless a task explicitly targets
them.

## Avalonia architecture

Respect the MVVM pattern in `WTEditor.Avalonia`: keep presentation state and
commands in view models, keep views focused on XAML and view-specific wiring,
and keep application logic in services or other non-view classes. Avoid
placing business logic in Avalonia code-behind.

## WoW version compatibility

The editor aims to support multiple versions of World of Warcraft. When
implementing a feature or fix for a specific version, preserve compatibility
with other supported versions. Keep version-specific behavior scoped to the
versions that need it, and verify that shared behavior still works elsewhere.

## Asset identity, loading, and display

- MPQ clients are path based. Use the filepath supplied when the asset was
  loaded. Every loaded MPQ asset has a filepath because MPQ assets can only be
  read by path; a missing path is a caller or bookkeeping error, not a normal
  asset state. Never describe a synthetic in-process cache ID as a FileDataID,
  and never show a FileDataID for an MPQ client.
- CASC clients use FileDataIDs as the authoritative identity when available.
  Show a filepath as the asset's source only when that path came from the
  client's own data. Do not infer a source path from an external listfile.
- In UI and console messages, display a client-provided path and CASC
  FileDataID together as `path/to/file.ext(56474)`. If only one is available,
  show only that value. The parenthesized ID is only for CASC clients; an MPQ
  path appears alone.
- The external listfile is optional, user supplied metadata. It may provide
  clearly identified informational hints or quality-of-life features, but
  must never determine whether an asset exists, how it is loaded, or how
  client assets are indexed. Loading must work without the listfile.
- Account for the format transitions: from the introduction of CASC in 6.0
  through build 21796, `FileDataComplete.db2` supplied FileDataID-to-name
  mappings. Through 8.2 (build 30080), CASC could still load many assets by
  filename as well as by FileDataID. After that build, load CASC assets by
  FileDataID only. Keep these version-specific paths scoped to the clients
  that support them.

## Required smoke verification

After every code, project, configuration, or shader change, run the repository smoke-test runner from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

The runner restores the smoke-test dependencies, builds `WTEditor.Avalonia`, and executes the `WTEditor.Avalonia.Tests` smoke suite. A non-zero result is a blocking failure and must be investigated before considering the change complete.
