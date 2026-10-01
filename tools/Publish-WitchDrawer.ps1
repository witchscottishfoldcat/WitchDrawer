[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\WitchDrawer.App\WitchDrawer.App.csproj'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$projectVersion = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
} elseif ($Version -ne $projectVersion) {
    throw "Requested version '$Version' does not match Directory.Build.props version '$projectVersion'. Update the project version and release notes before packaging."
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Version must be a numeric semantic version, but was '$Version'."
}

$publishDir = Join-Path $repoRoot "publish\v$Version"
$zipPath = Join-Path $repoRoot "publish\WitchDrawer-v$Version-$RuntimeIdentifier.zip"
$shaPath = "$zipPath.sha256"
$installerPath = Join-Path $repoRoot "publish\WitchDrawer-Setup-v$Version-x64.exe"
$installerShaPath = "$installerPath.sha256"

New-Item -ItemType Directory -Path (Split-Path $publishDir) -Force | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$appExe = Join-Path $publishDir 'WitchDrawer.App.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Publish output is missing $appExe."
}

# Package every publish output. This remains correct if a future runtime or
# framework requires files beside the executable.
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + (Split-Path $zipPath -Leaf) |
    Set-Content -LiteralPath $shaPath -NoNewline

if (-not $SkipInstaller) {
    $isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $isccPath = $isccCommand.Source
    } else {
        $knownIsccPaths = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        )
        $isccPath = $knownIsccPaths |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }

    if (-not $isccPath) {
        throw 'Inno Setup 6 (ISCC.exe) is required to produce the installable Setup.exe. Use -SkipInstaller only for portable-package checks.'
    }

    & $isccPath "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $repoRoot 'installer\WitchDrawer.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Inno Setup completed but did not produce $installerPath."
    }

    (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + (Split-Path $installerPath -Leaf) |
        Set-Content -LiteralPath $installerShaPath -NoNewline
}

Write-Host "Portable package: $zipPath"
Write-Host "Portable SHA-256: $shaPath"
if (-not $SkipInstaller) {
    Write-Host "Installer: $installerPath"
    Write-Host "Installer SHA-256: $installerShaPath"
}
