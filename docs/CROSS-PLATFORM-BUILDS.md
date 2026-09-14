# Cross-platform publishing

Danslicer targets portable `net10.0`; it does not use a Windows-only target framework or
`EnableWindowsTargeting`. Release bundles are self-contained directories so the target machine
does not need a separately installed .NET runtime.

From the repository root, publish the desktop app and CLI for all supported runtime identifiers:

```powershell
.\build\publish.ps1
```

The outputs are written to `artifacts/publish/<rid>/app` and
`artifacts/publish/<rid>/cli`, where `<rid>` is `win-x64`, `linux-x64`, or `osx-arm64`.
Use `-Configuration Debug` or `-OutputRoot <path>` to override the defaults.

The equivalent individual command is:

```powershell
dotnet publish src/Danslicer.App/Danslicer.App.csproj -c Release -r <rid> --self-contained true --artifacts-path artifacts/build -o artifacts/publish/<rid>/app -p:PublishSingleFile=false -p:DebugSymbols=false -p:DebugType=None
dotnet publish src/Danslicer.Cli/Danslicer.Cli.csproj -c Release -r <rid> --self-contained true --artifacts-path artifacts/build -o artifacts/publish/<rid>/cli -p:PublishSingleFile=false -p:DebugSymbols=false -p:DebugType=None
```

The separate `artifacts/build` intermediate tree prevents Release publishing from replacing the
restore assets used by the normal Debug `dotnet build` and `dotnet test` workflow.

These commands cross-publish on Windows, but that proves only that assets resolve and compile for
each target. Run the Linux and macOS bundles on real target hardware before calling those runtime
paths verified. The 3Dconnexion SpaceMouse integration is intentionally Windows-only: the app
checks the operating system before creating its annotated Windows Raw Input backend, so other platforms omit
the connection and its status-bar indicator.
