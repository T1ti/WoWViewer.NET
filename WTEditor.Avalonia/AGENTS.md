# WTEditor.Avalonia architecture rules

These rules supplement the repository-level instructions for work under this
project.

## View and control boundaries

- Keep Avalonia controls focused on visual-tree lifecycle, platform interop,
  input/event forwarding, and invoking collaborators.
- Do not construct inspector, selection, telemetry, or other UI display models
  inside controls. Put renderer-to-UI mappings in `Presentation` and name them
  explicitly as `*DisplayProjection`, `*DisplayData`, or another term that makes
  their presentation purpose clear.
- Do not expose renderer containers or mutable renderer state to view models.
  Project them into immutable application or presentation records first.
- Keep commands and persistent presentation state in view models. Keep
  renderer/platform implementation in `Rendering` or the render library.

## Growth rules

- Before extending `Dx11View`, decide whether the change is GPU/composition
  lifecycle orchestration. If not, add or extend a focused collaborator instead.
- Keep conversions one-way and explicit: view-model input to renderer contracts
  belongs in a named input projection; renderer statistics to profiling records
  belongs in a snapshot factory.
- Prefer focused internal classes over partial control files. A partial file may
  hide size but does not create an architectural boundary.
- Add tests against extracted projections and factories directly instead of
  reaching through a control solely to exercise data creation.
- Preserve renderer hot-path behavior: avoid per-frame allocations unless the
  resulting data is consumed that frame, and keep low-frequency UI publication
  throttled independently from rendering.

## Verification

- Follow the root `AGENTS.md` smoke-test command after every code, project, or
  configuration change.
