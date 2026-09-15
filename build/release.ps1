[CmdletBinding()]
param(
    [ValidateRange(0, 65534)]
    [int] $BuildNumber = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot 'releases'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
if ($BuildNumber -eq 0) {
    $numbers = @(Get-ChildItem -LiteralPath $releaseRoot -Directory |
        Where-Object Name -Match '^\d+$' | ForEach-Object { [int] $_.Name })
    $BuildNumber = if ($numbers.Count) { ($numbers | Measure-Object -Maximum).Maximum + 1 } else { 1 }
}
if ($BuildNumber -gt 65534) { throw 'Build number exceeds the file-version limit.' }
$releaseDirectory = Join-Path $releaseRoot "$BuildNumber"
if (Test-Path -LiteralPath $releaseDirectory) { throw "Build $BuildNumber already exists; refusing to overwrite it." }

# A new staging directory for every attempt avoids deleting or mixing older output.
$stage = Join-Path $repositoryRoot "artifacts\release\$BuildNumber-$([Guid]::NewGuid().ToString('N'))"
$bundleName = "Danslicer-build-$BuildNumber-win-x64"
$bundle = Join-Path $stage $bundleName
$buildArtifacts = Join-Path $stage 'build'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$revision = (git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to read source revision.' }
$dirty = @(git -C $repositoryRoot status --porcelain).Count -gt 0
$version = "1.0.0.$BuildNumber"
dotnet publish (Join-Path $repositoryRoot 'src\Danslicer.App\Danslicer.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    --artifacts-path $buildArtifacts --output $bundle `
    -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugSymbols=false -p:DebugType=None `
    "-p:Version=$version" "-p:AssemblyVersion=$version" "-p:FileVersion=$version" `
    "-p:InformationalVersion=$version+build.$BuildNumber" --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
foreach ($required in @('Danslicer.App.exe', 'Danslicer.App.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'SkiaSharp.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $bundle $required))) { throw "Portable bundle is missing $required" }
}
$manifest = [ordered]@{
    BuildNumber = $BuildNumber
    Version = $version
    Runtime = 'win-x64'
    Configuration = 'Release'
    SelfContained = $true
    SourceRevision = $revision
    IncludesUncommittedChanges = $dirty
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
}
# Only runtime files enter the ZIP; release notes and provenance stay beside it.
Get-ChildItem -LiteralPath $bundle -Recurse -File |
    Where-Object {
        $_.Extension -in @('.pdb', '.xml', '.dbg') -or
        $_.Name -like 'mscordaccore*.dll' -or
        $_.Name -in @('mscordbi.dll', 'createdump.exe', 'Microsoft.DiaSymReader.Native.amd64.dll')
    } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName }

$archive = Join-Path $stage "$bundleName.zip"
Compress-Archive -LiteralPath $bundle -DestinationPath $archive -CompressionLevel Optimal
New-Item -ItemType Directory -Path $releaseDirectory | Out-Null
Copy-Item -LiteralPath $archive -Destination $releaseDirectory
$finalArchive = Join-Path $releaseDirectory "$bundleName.zip"
$hash = (Get-FileHash -LiteralPath $finalArchive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $bundleName.zip" | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'BUILD-INFO.json') -Encoding utf8
Write-Host "Release created: $finalArchive"
