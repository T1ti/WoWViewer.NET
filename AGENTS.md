# Repository instructions

## Scope and architecture

For WTEditor work, use `WTEditor.Avalonia`, `WoWRenderLib.DX11`, and their
dependencies. Tests are in `WTEditor.Avalonia.Tests`, `WoWRenderLib.DX11.Tests`,
and `WoWRenderLib.Tests`. Ignore the legacy viewer, OpenGL, and WPF projects
unless targeted explicitly.

Keep Avalonia views in XAML and view-specific wiring, state and commands in view
models, and application logic in services. Scope version-specific WoW behavior
to the affected clients and verify shared behavior elsewhere.

## Asset identity

- MPQ assets use the path supplied at load time; a missing path is a bug. Never
  show an MPQ FileDataID or call a synthetic cache ID a FileDataID.
- CASC FileDataIDs are authoritative when available. Show a source path only if
  it came from the client, never from an external listfile.
- Display a client path and CASC ID as `path/to/file.ext(56474)`, or the sole
  available value. MPQ displays its path alone.
- The optional user listfile provides hints only; asset existence, loading, and
  indexing must work without it.
- CASC 6.0 through build 21796 used `FileDataComplete.db2` for ID-to-name
  mappings. Filename loading continued through 8.2 (build 30080); after that,
  load by FileDataID only. Apply each rule only to supporting clients.

## Tests

After each completed batch of code, project, configuration, shader, or test
edits, run the full smoke check from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

The default runner restores, builds `WTEditor.Avalonia`, and runs all three
suites; a non-zero exit blocks completion. During development, use `-Scope Render`,
`-Scope DX11`, or `-Scope Avalonia` for relevant tests. Do not rerun an unchanged
state or run a duplicate build; documentation-only edits need no run.

Find tests with narrow `rg` searches. Add focused behavioral coverage, not
duplicate or implementation-only assertions. Capture full test output in a
temporary log; report exit code and test totals, reading failure sections only
as needed. Await command completion inside one tool orchestration without
exposing progress polls; use verbose output only to diagnose failures.
