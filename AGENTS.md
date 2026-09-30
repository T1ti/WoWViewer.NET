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

## 3.3.5 shader reference work

Use `docs/CLIENT_335_RENDERING_ACCURACY_PLAN.md` for the complete DX9 SM3 audit.
Before inspecting client BLS files, read the saved evidence in
`artifacts/client-335-shaders/index.md`, `permutations.csv`, `manifest.json`,
and the relevant `programs/.../*.bls.md` and content-addressed `.asm`/`.json`.
If the cache is missing or sources changed, use `build/extract-client-shaders.ps1`;
its workflow is documented in `build/ClientShaderTools/README.md`. Cache `AllDx9`
once to retain fallback programs, then focus on `SM3`. Do not repeatedly extract
or dump unchanged shaders. Use `-VerifyHashes` for an intentional integrity audit
and `-Force` only for a known invalidation/recovery need. Persist recovered
selector/formula/constant/state findings in the reference ledgers. Disassembly
and successful compilation do not establish original HLSL or rendering parity.

CPU scene preparation and submission are part of the same audit. Start with
`docs/reference/client-335/CPU_SCENE_AUDIT.md` and `cpu-evidence.json`, then
reuse saved functions/instructions from `artifacts/client-335-shaders/ida`.
Keep the complete function ledger, direct-call/call-site tables, unresolved
targets, and port/capture states current. Refresh affected IDA exports when
the reference or its analysis changes; do not repeatedly decompile unchanged
functions. The original binary hash and full rendering closure remain required
before sign-off.

Always keep an explicit `Roadmap` section in the rendering plan and CPU audit
notes. After each batch, record the verified changes, remaining gaps, next
concrete tasks in priority order, and their acceptance checks. Keep the lighting
plan and rendering status linked to that roadmap; never equate a passing smoke
suite or a complete export with full client parity.

Pre-existing IDA function names were guessed. Treat every existing IDB name,
type, prototype, and comment as an unverified hypothesis, including helper
names that appear precise. Identify evidence by build and address; recover
semantic roles from instructions, callers, field writers, constants, and data
flow. Keep name authority separate from analysis/port status in the manifests
and `docs/reference/client-335/cpu-semantic-claims.csv`. Name searches are
navigation aids and must never exclude unnamed or misnamed rendering paths.

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
