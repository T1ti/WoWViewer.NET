# DX11 asset-streaming modernization plan

Updated: 2026-09-07

## Goals and constraints

- Keep frames responsive by never intentionally starting optional synchronous work after the
  current frame budget is spent.
- Keep parsing and decoding independent from Avalonia's presentation cadence.
- Keep all DX11 immediate-context work on the render thread.
- Prefer one small reusable pipeline over asset-specific worker implementations.
- Do not add asynchronous states for inexpensive CPU transformations.
- Preserve exact cache ownership during tile unload/re-entry.

## Completed foundation

- ADT, BLP, M2, and WMO parsing/decoding use asynchronous Channel consumers without polling.
- Parsed-result channels are bounded, limiting prepared data waiting for GPU upload.
- ADT admission is bounded to 16 in-flight tiles and desired tiles are queued camera-first.
- GPU uploads run repeated round-robin passes across asset types until a dynamic frame deadline.
- Unload and scene-population work is incremental and has reserved portions of the deadline.
- ADTs use a testable 750 ms per-object unload grace timestamp.
- Re-entry cancels pending unload and stale ADT parse decisions can be safely retried.
- Alpha maps pack directly from wowlib spans without temporary per-layer arrays.
- Tile teardown no longer performs duplicate model releases, full cache sweeps, or repeated
  global scene-list removal scans.

## Ordered remaining work

### 1. Cheap stale-work elimination

Status: implemented 2026-09-07; runtime movement validation remains.

- Skip BLP, M2, and WMO requests that have lost all owners before their expensive worker starts.
- Requeue a skipped request if an owner returned while the skip result was in transit.
- Keep queued-result draining cheap and outside the one-GPU-upload-per-cache allowance.

Exit criterion: moving away from queued assets prevents their decode/parse functions from running,
while rapid re-entry cannot leave a permanent placeholder.

### 2. Lightweight streaming measurements

Status: implemented 2026-09-07; representative movement captures remain.

- Record per-pipeline pending, active, completed, skipped, and failed counts.
- Record last and maximum parse/decode duration without allocations on the hot path.
- Include the metrics in performance capture JSON and the live metrics panel.
- Keep background CPU duration separate from render-thread frame timing.

Exit criterion: a movement capture identifies queue growth, stale work, parse/decode latency, and
render-thread upload cost by asset category.

### 3. Bounded request admission

- Add finite request capacities for BLP, M2, and WMO after real queue-depth measurements establish
  sensible limits.
- When capacity is reached, retain a deduplicated deferred request rather than throwing from a
  render-thread load call.
- Prefer nearby/visible requests when choosing which deferred item to admit.

Exit criterion: request memory is bounded and saturation cannot cause an asset to remain unloaded.

### 4. Explicit ownership

- Replace mutable parent-ID lists with a small reference-counted owner abstraction.
- Preserve overlapping old/new tile lifetimes for the same numeric parent.
- Support multiple completion subscribers without callback replacement.
- Add tests for acquire/release balance, re-entry, failure, and overlapping generations.

Exit criterion: every acquisition has exactly one release and no delayed release can evict a newly
acquired asset.

### 5. Engine-owned lifecycle

- Move static caches and `cachedDevice` into an engine-owned streaming service.
- Make shutdown asynchronous and deterministic with `IAsyncDisposable`.
- Cancel queued work before releasing renderer/device resources.

Exit criterion: renderer restart and multiple sequential engine instances cannot share stale cache,
worker, callback, or device state.

### 6. Memory budgets and pooling

- Measure prepared ADT/BLP/M2/WMO memory by bytes, not only item count.
- Add byte-based prepared-result and resident-cache budgets.
- Pool only large transient managed buffers whose allocation rate is demonstrated in captures.
- Avoid pooling data retained by resident assets or native wowlib ownership.

Exit criterion: traversal has a predictable memory ceiling without increasing frame latency.

### 7. Measured concurrency

- Keep ADT parsing single-worker until wowlib filesystem concurrency is verified.
- Benchmark BLP decode and model preparation with one worker versus limited parallel workers.
- Increase concurrency only when throughput improves without frame-time or memory regression.

Exit criterion: worker counts are justified by capture data for the active client and hardware.

### 8. Movement regression coverage

- Add a deterministic streaming-state stress test for range oscillation and rapid direction changes.
- Add an opt-in Release movement benchmark alongside the existing steady-state benchmark.
- Compare median, P95, P99, and maximum frame time, queue depth, skipped work, and reload counts.

Exit criterion: tile-boundary traversal is repeatable and detects frame-spike, starvation, ownership,
and reload regressions.

## Deliberately deferred designs

- A large all-purpose `AssetStreamingCoordinator` is deferred until ownership and measured admission
  requirements justify it. The current shared queue remains the common primitive.
- Cost-predictive or weighted scheduling is deferred until metrics show that rotating one upload per
  cache still causes starvation or deadline overruns.
- `ArrayPool<T>` and multiple workers are not defaults; both can increase retained memory and
  contention when introduced without measurements.
