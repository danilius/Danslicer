# Build preference

Unless the user specifies otherwise, build the app in Debug with output at
`F:\Git Repos\Danslicer\src\Danslicer.App\bin\Debug\net10.0`.

# Releases

For an explicitly requested release, publish in Release configuration as a
self-contained portable ZIP. Do not use an installer for now: users should only
need to download the ZIP, extract it, and run the app.

Place release ZIPs in `F:\Git Repos\Danslicer\releases\<build number>`.
Use `build/release.ps1` for Windows x64 releases. It assigns the next unused
numeric build number by default and refuses to overwrite an existing build.

Keep release ZIPs minimal: app and required runtime/native dependencies only.
Exclude debug symbols, XML API documentation, tests, logs, capture output and
build metadata. Put checksums, build information and validation notes beside the ZIP.
