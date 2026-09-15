# Portable releases

Release ZIPs belong in `F:\Git Repos\Danslicer\releases\<build number>`.
For now, do not create installers. A user downloads the ZIP, extracts the entire
folder, and runs `Danslicer.App.exe`. Include the .NET runtime in the bundle.

## Windows x64

Run `./build/release.ps1` from PowerShell. The script publishes in Release mode,
assigns the next numeric build number, embeds it in the executable version, and
creates a ZIP, SHA-256 checksum, and build manifest. Only runtime files go into
the ZIP; metadata and validation stay beside it. Debug symbols and XML API
documentation are omitted. To choose a number explicitly,
use `./build/release.ps1 -BuildNumber 12`. Existing release folders are never
overwritten. Build 1 begins this sequence; assembly/file versions are `1.0.0.<build>`.

Build manifests record the source commit and whether uncommitted changes were
included. Packaging uses the current working tree. Intermediate output is kept
under `artifacts/release` so ordinary Debug build assets remain separate.

Before distribution, run the test suite, extract the ZIP, and smoke-test that
extracted executable. Keep validation results beside the ZIP. Creation of a local
release does not upload it to GitHub or another public service.

Other platform publish directories can be produced using `build/publish.ps1`;
see `CROSS-PLATFORM-BUILDS.md`. They require validation on their target OS before
distribution.
