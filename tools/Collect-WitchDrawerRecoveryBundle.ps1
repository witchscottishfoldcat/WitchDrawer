[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string[]]$DataDirectories = @(
        (Join-Path $env:LOCALAPPDATA 'WitchDrawer'),
        'D:\WD'
    ),

    [string[]]$SourceFolders = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$outputDrive = [IO.Path]::GetPathRoot($resolvedOutputRoot)
if ([string]::IsNullOrWhiteSpace($outputDrive)) {
    throw "The recovery bundle output directory is invalid: $OutputRoot"
}

$sourceDrives = @($DataDirectories + $SourceFolders) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($_)) } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique

if ($sourceDrives -contains $outputDrive) {
    throw "The recovery bundle must be written to a different drive than the data source. Current output drive: $outputDrive"
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$bundleName = 'WitchDrawer-Recovery-{0:yyyyMMdd-HHmmss}' -f (Get-Date)
$bundleDirectory = Join-Path $resolvedOutputRoot $bundleName
New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null

function Write-BundleText {
    param(
        [Parameter(Mandatory = $true)] [string]$RelativePath,
        [Parameter(Mandatory = $true)] [object]$Value
    )

    $targetPath = Join-Path $bundleDirectory $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
    $Value | Out-File -LiteralPath $targetPath -Encoding utf8
}

function Copy-BundleFileIfPresent {
    param(
        [Parameter(Mandatory = $true)] [string]$SourcePath,
        [Parameter(Mandatory = $true)] [string]$RelativeDestination
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        return
    }

    $destinationPath = Join-Path $bundleDirectory $RelativeDestination
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    try {
        Copy-Item -LiteralPath $SourcePath -Destination $destinationPath -Force
    }
    catch {
        Write-BundleText -RelativePath ('errors\copy-{0}.txt' -f ([IO.Path]::GetFileName($SourcePath))) `
            -Value $_.Exception.ToString()
    }
}

function Write-FileSystemInventory {
    param(
        [Parameter(Mandatory = $true)] [string]$RootPath,
        [Parameter(Mandatory = $true)] [string]$RelativeDestination
    )

    $destinationPath = Join-Path $bundleDirectory $RelativeDestination
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null

    if (-not (Test-Path -LiteralPath $RootPath)) {
        [pscustomobject]@{
            Path = $RootPath
            Exists = $false
            Kind = ''
            Length = $null
            LastWriteTimeUtc = $null
            Attributes = ''
        } | Export-Csv -LiteralPath $destinationPath -NoTypeInformation -Encoding utf8
        return
    }

    Get-ChildItem -LiteralPath $RootPath -Force -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName
                Exists = $true
                Kind = if ($_.PSIsContainer) { 'Directory' } else { 'File' }
                Length = if ($_.PSIsContainer) { $null } else { $_.Length }
                LastWriteTimeUtc = $_.LastWriteTimeUtc
                Attributes = $_.Attributes.ToString()
            }
        } |
        Export-Csv -LiteralPath $destinationPath -NoTypeInformation -Encoding utf8
}

$volumeReport = try {
    Get-Volume | Select-Object DriveLetter, FileSystem, DriveType, HealthStatus, Size, SizeRemaining
}
catch {
    $_.Exception.ToString()
}
Write-BundleText -RelativePath 'system\volumes.txt' -Value $volumeReport
Write-BundleText -RelativePath 'system\environment.txt' -Value @(
    "CollectedAt: $(Get-Date -Format o)"
    "User: $([Environment]::UserName)"
    "Computer: $([Environment]::MachineName)"
    "PowerShell: $($PSVersionTable.PSVersion)"
)

$dataIndex = 0
foreach ($dataDirectory in $DataDirectories | Select-Object -Unique) {
    if ([string]::IsNullOrWhiteSpace($dataDirectory)) {
        continue
    }

    $root = [IO.Path]::GetFullPath($dataDirectory)
    $leafName = [IO.Path]::GetFileName($root.TrimEnd('\'))
    $safeName = "Data-{0}-{1}" -f $dataIndex, $leafName
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        $safeName = "Data-$dataIndex"
    }

    Copy-BundleFileIfPresent -SourcePath (Join-Path $root 'witchdrawer.db') `
        -RelativeDestination (Join-Path $safeName 'witchdrawer.db')
    Copy-BundleFileIfPresent -SourcePath (Join-Path $root 'witchdrawer.db-wal') `
        -RelativeDestination (Join-Path $safeName 'witchdrawer.db-wal')
    Copy-BundleFileIfPresent -SourcePath (Join-Path $root 'witchdrawer.db-shm') `
        -RelativeDestination (Join-Path $safeName 'witchdrawer.db-shm')

    $logsDirectory = Join-Path $root 'logs'
    if (Test-Path -LiteralPath $logsDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $logsDirectory -File -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                Copy-BundleFileIfPresent -SourcePath $_.FullName `
                    -RelativeDestination (Join-Path $safeName (Join-Path 'logs' $_.Name))
            }
    }

    Write-FileSystemInventory -RootPath (Join-Path $root 'Boxes') `
        -RelativeDestination (Join-Path $safeName 'boxes-inventory.csv')
    $dataIndex++
}

$sourceIndex = 0
foreach ($sourceFolder in $SourceFolders | Select-Object -Unique) {
    if ([string]::IsNullOrWhiteSpace($sourceFolder)) {
        continue
    }

    Write-FileSystemInventory -RootPath ([IO.Path]::GetFullPath($sourceFolder)) `
        -RelativeDestination (Join-Path 'sources' ('source-{0}-inventory.csv' -f $sourceIndex))
    $sourceIndex++
}

Write-BundleText -RelativePath 'README.txt' -Value @(
    'This recovery bundle contains WitchDrawer database copies, logs, volume information, and file metadata inventories.'
    'It does not contain user file contents. Database and logs may contain filenames and full paths.'
    'Send the entire ZIP to the developer for analysis. Do not modify the source computer data.'
)

$zipPath = Join-Path $resolvedOutputRoot ("$bundleName.zip")
Compress-Archive -Path (Join-Path $bundleDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

Write-Host "Recovery bundle created: $zipPath"
