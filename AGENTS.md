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

## Required smoke verification

After every code, project, configuration, or shader change, run the repository smoke-test runner from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

The runner restores the smoke-test dependencies, builds `WTEditor.Avalonia`, and executes the `WTEditor.Avalonia.Tests` smoke suite. A non-zero result is a blocking failure and must be investigated before considering the change complete.
