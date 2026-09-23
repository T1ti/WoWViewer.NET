## WoWViewer.NET

This is an educational project for learning more about 3D programming and how
World of Warcraft renders its world. The actively maintained application is the
Avalonia-based world editor backed by the Direct3D 11 renderer.

No support is provided or implied when building or using this project.

### Contributing

This project is still in early stages with many things still missing and is not yet ready for external contributions.

### Building

This requires the [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) SDK to build.

Clone the repository with its submodules, then run the smoke-test runner from
the repository root:

```powershell
git submodule update --init --recursive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

The main solution contains the active Avalonia/DX11 application, its tests, and
their transitive project dependencies. Legacy WPF, OpenGL, and viewer projects
remain in the repository for reference but are intentionally excluded from the
solution.

### Running

On Windows, launch the editor from the repository root:

```powershell
dotnet run --project .\WTEditor.Avalonia\WTEditor.Avalonia.csproj
```

### Credits

Many thanks go to [Kruithne for wow.export](https://github.com/Kruithne/wow.export), [Deamon for WebWoWViewer](https://github.com/Deamon87/WebWowViewerCpp), [T1ti for NoggIt](https://github.com/T1ti) as well as all wiki editors this work is based on/has contributed to.
