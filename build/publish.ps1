[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\publish')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$runtimeIdentifiers = @('win-x64', 'linux-x64', 'osx-arm64')
$projects = @(
    @{ Name = 'app'; Path = 'src\Danslicer.App\Danslicer.App.csproj' },
    @{ Name = 'cli'; Path = 'src\Danslicer.Cli\Danslicer.Cli.csproj' }
)

foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    foreach ($project in $projects) {
        $projectPath = Join-Path $repositoryRoot $project.Path
        $publishDirectory = Join-Path $outputRootPath "$runtimeIdentifier\$($project.Name)"

        if (Test-Path -LiteralPath $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }

        Write-Host "Publishing $($project.Name) for $runtimeIdentifier -> $publishDirectory"
        dotnet publish $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --self-contained true `
            --output $publishDirectory `
            -p:PublishSingleFile=false `
            -p:DebugSymbols=false `
            -p:DebugType=None

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $($project.Name) / $runtimeIdentifier"
        }
    }
}

Write-Host "Published all targets under $outputRootPath"
