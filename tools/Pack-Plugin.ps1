param(
    [string]$Project = "plugins\samples\Clock\WitchDrawer.Clock.csproj",
    [string]$OutputDirectory = "publish\plugins"
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = if ([IO.Path]::IsPathRooted($Project)) { [IO.Path]::GetFullPath($Project) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $Project)) }
$manifestPath = Join-Path (Split-Path -Parent $projectPath) 'plugin.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.id -cnotmatch '^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$' -or $manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid plugin id or version.' }
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory)) }
$staging = Join-Path $outputRoot ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
& dotnet publish $projectPath --configuration Release --output $staging --self-contained false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Plugin publish failed.' }
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $staging 'plugin.json') -Force
if (Test-Path -LiteralPath (Join-Path $staging 'WitchDrawer.PluginSdk.dll')) { throw 'Plugin ZIP must not include the shared SDK assembly; set Private=false on the SDK reference.' }
if (-not (Test-Path -LiteralPath (Join-Path $staging $manifest.entryAssembly))) { throw 'Plugin entry assembly is missing.' }
$zipPath = Join-Path $outputRoot ($manifest.id + '-v' + $manifest.version + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $zipPath) { throw "Output already exists: $zipPath. Choose another output directory or version." }
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath)
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zipPath + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($zipPath)) -Encoding ascii
Write-Output "Plugin ZIP: $zipPath"
Write-Output "SHA-256: $hash"
# Keep the complete staging output for inspection; nothing is uploaded automatically.
