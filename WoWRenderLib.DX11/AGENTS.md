# DX11 rendering architecture rules

These rules apply to changes under `WoWRenderLib.DX11` and supplement the
repository-level instructions.

## Keep SceneManager an orchestrator

- `Managers/SceneManager.cs` owns frame ordering, shared scene state, and the
  stable public facade used by `WowViewerEngine`. Do not add resource creation,
  mesh generation, per-pass submission loops, file parsing, or editing algorithms
  to that file.
- Keep existing workflow-specific facade code in the matching partial file:
  streaming and object lifetime in `SceneManager.Streaming.cs`, and terrain
  selection/editing/raycasting in `SceneManager.TerrainEditing.cs`.
- A new render pass belongs in a dedicated `Renderer/*Renderer.cs` class. That
  class owns and disposes its GPU resources, encapsulates its pipeline state, and
  returns a small statistics value to `SceneManager`.
- A renderer that changes shared DX11 state must restore the state required by the
  following pass, or make the required post-state explicit in its API and XML
  documentation.

## Put policy and data outside orchestration code

- Put deterministic culling, batching, coordinate, and scheduling calculations in
  small side-effect-free policy/helper types. Add focused smoke tests for boundary
  conditions before wiring them into a render or streaming loop.
- Put client-file and database decoding in `Loaders` or the shared `WoWRenderLib`
  project. Renderers consume decoded structures; they do not open or parse files.
- Keep CPU-side structures free of DX11 handles. GPU representations and upload
  state belong in `WoWRenderLib.DX11`.
- Prefer explicit constructor dependencies and result/statistics records over
  reaching through `SceneManager` from a renderer or loader.

## Agent-friendly change discipline

- Search all callers before moving a public member. Preserve the `SceneManager`
  facade when extraction can avoid a broad, unrelated call-site migration.
- Make one architectural move per change set and avoid mixing it with formatting
  churn. Do not rewrite an entire large file when a focused extraction is enough.
- Preserve existing telemetry when extracting a pass. New passes must expose draw
  counts, submitted work, and CPU culling/submission timings where applicable.
- Reuse per-frame scratch collections and long-lived GPU buffers. Avoid per-object
  allocations, temporary GPU resources, synchronous file I/O, and shader
  compilation in frame loops.
- Treat resource ownership as part of the API: the type that creates a COM/GPU
  resource disposes it, while borrowed render targets or shared cache resources
  must be documented and not disposed by the borrower.
- After any code, project, configuration, or shader change, run the root smoke-test
  command required by the repository `AGENTS.md`. A failing runner blocks
  completion.
