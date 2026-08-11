# Repository instructions

## Required smoke verification

After every code, project, configuration, or shader change, run the repository smoke-test runner from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\run-smoke-tests.ps1
```

The runner restores the smoke-test dependencies, builds `WTEditor.Avalonia`, and executes the `WTEditor.Avalonia.Tests` smoke suite. A non-zero result is a blocking failure and must be investigated before considering the change complete.
