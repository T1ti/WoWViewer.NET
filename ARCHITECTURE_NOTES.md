# Architecture notes

## Solution map

`WTEditor.Avalonia` is the active desktop editor host (`net10.0`, Avalonia 12).
It references `WoWRenderLib.DX11`, which in turn uses the rendering-agnostic
`WoWRenderLib` and the WoW data projects (`WoWFormatLib`, `TACTSharp`, `CascLib`,
`DBCD`, and `BLPSharp`).  The solution still includes earlier WPF/OpenGL hosts;
they are useful references, not the current UI path.

```
Avalonia shell and MVVM
  MainWindow -> MainView -> Editor3DView
                         -> Dx11View
                              -> WowViewerEngine (DX11)
                                   -> SceneManager / ShaderManager / caches
                                   -> CASC + FileProvider + WoW format readers
```

## Application/UI flow

- `Program` builds a small DI container and registers `MainViewModel` and
  `MainWindowViewModel` as singletons. `App` resolves the window view model.
- `MainWindowViewModel.CurrentView` starts as `MainViewModel`; Avalonia's
  reflection-based `ViewLocator` maps view-model names to views.
- `MainViewModel` owns an `Editor3DViewModel`. `Editor3DView` captures pointer
  and keyboard input, with automatic AZERTY/QWERTY key selection, and writes
  input state into that view model.
- `Dx11View` uses the same data context. Every Avalonia render-priority frame it
  translates that state to `WoWRenderLib.DX11.InputFrame`, calls
  `engine.Update(delta, input)` then `engine.Render(delta)`, and copies renderer
  statistics back to the view model for the overlay.

## DX11/Avalonia integration

- `Dx11View` owns the Silk D3D11 device and device context. It requires the
  Avalonia compositor to support `D3D11TextureGlobalSharedHandle` import.
- `WowViewerEngine` renders into a BGRA8 shared texture. With `UseKeyedMutex`
  enabled, the engine releases mutex key `1` after rendering and Avalonia imports
  it using `UpdateWithKeyedMutexAsync(acquire: 1, release: 0)`.
- On a size change, the control resizes the engine, discards the imported image,
  and imports the new shared texture handle. `Dx11View.Cleanup` disposes engine
  and D3D resources when removed from the visual tree.
- This host is Windows/D3D11-compositor specific. Unsupported Avalonia graphics
  backends currently only log a message and leave the viewport blank.

## Renderer lifecycle and data flow

1. `WowViewerEngine.Initialize` loads HLSL shaders from `AppContext.BaseDirectory/Shaders`,
   creates `SceneManager`, creates the shared target, then selects a WoW product.
2. Product initialization reads `.build.info` and starts `Services.CASC.Initialize` on a
   background task. It configures the global `WoWFormatLib.FileProvider`, loads the
   default WDT, and preloads its TEX data.
3. Each render frame updates tiles around the camera, processes queued work, then calls
   `SceneManager.RenderScene` using the active camera.
4. The scene queues ADT tiles surrounding the camera (currently view distance 4).
   ADT/WMO/M2/BLP caches parse/decode on background workers and enqueue render-thread
   GPU uploads. `SceneManager.ProcessQueue` limits these uploads to roughly 10 ms per frame.
5. `SceneManager` renders ADT terrain plus instanced WMO and M2 data, performs camera
   frustum visibility checks, and can expose selection/bounding-volume debug state.

## Responsibilities by project

| Area | Main responsibility |
| --- | --- |
| `WTEditor.Avalonia` | Window layout, editor input, composition-surface presentation, telemetry overlay. |
| `WoWRenderLib.DX11` | Direct3D 11 resources, HLSL compilation, camera/input behavior, scene rendering, async asset caches. |
| `WoWRenderLib` | Shared map/model structures, WDT cache, CASC initialization facade, data providers, raycasting. |
| `WoWFormatLib` | Readers for ADT/WDT/M2/WMO and abstraction over local/CASC file access. |
| `TACTSharp` / `CascLib` | Blizzard storage/build/config retrieval and archive access. |
| `DBCD` | DB2/DBC schema and table loading. |

## Current seams to keep in mind

- The editor now exposes the WoW install path, product, build config, and CDN config in
  a session-scoped Settings dialog. The initial defaults still point at a Classic Era
  installation; persistence and product discovery are future configuration work.
- Rendering settings are also session-scoped: camera far plane, movement/mouse settings,
  ADT/WMO/M2 visibility, bounding-volume debugging, and tile streaming radius. The tile
  radius is shared by the load and unload paths and is clamped to the valid map range.
- General settings currently expose `Auto`, `QWERTY`, and `AZERTY`; Auto preserves the
  existing Windows keyboard-layout detection while explicit choices override it.
- UI `MoveSpeed` and `MouseSensitivity` are not passed through; the engine currently
  uses its own movement and mouse constants. Mouse-wheel state is declared but not
  populated by `Editor3DView`.
- The engine exposes only a narrow public control surface (`Initialize`, `Resize`,
  `Update`, `Render`, `SetMovementSpeed`, `SetHasFocus`, stats, and camera). Map,
  scene, selection, and render-toggle operations remain private inside `SceneManager`.
  New editor features will need deliberate engine-level APIs rather than UI access to
  renderer internals.
- Asset caches and `Services.CASC`/`FileProvider` are static/global. The current design
  assumes one active engine/client configuration per process; multi-viewport or product
  switching needs lifecycle/isolation work.
- Shader files are copied from `WoWRenderLib.DX11/Shaders` to the app output, and shader
  lookup depends on that output layout.
- Build verification in the current sandbox is blocked before compilation because legacy
  referenced projects try to read `C:\\Users\\Titi\\AppData\\Local\\Microsoft SDKs`, which
  the sandbox denies. This is an environment-access issue, not a source diagnostic.
