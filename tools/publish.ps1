# Publishes self-contained single-file Hatoclip.exe for win-x64 and win-arm64 and zips each one.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
#
# Output (under the repo root, gitignored):
#   publish\win-x64\Hatoclip.exe     publish\Hatoclip-<version>-win-x64.zip
#   publish\win-arm64\Hatoclip.exe   publish\Hatoclip-<version>-win-arm64.zip
# Each zip holds Hatoclip.exe and THIRD-PARTY-NOTICES.txt (copied from the repo root).
#
# Works in Windows PowerShell 5.1 and PowerShell 7.

[CmdletBinding()]
param(
    [string[]] $RuntimeIdentifiers = @('win-x64', 'win-arm64')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Hatoclip\Hatoclip.csproj'
$publishRoot = Join-Path $repoRoot 'publish'
$noticesName = 'THIRD-PARTY-NOTICES.txt'
$notices = Join-Path $repoRoot $noticesName
if (-not (Test-Path -LiteralPath $notices)) {
    throw "$noticesName not found in $repoRoot"
}

# Version comes from <Version> in the csproj.
[xml] $projectXml = Get-Content -LiteralPath $project -Encoding UTF8
$version = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) {
    throw "Could not read <Version> from $project"
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$results = @()
foreach ($rid in $RuntimeIdentifiers) {
    $outDir = Join-Path $publishRoot $rid
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }

    Write-Host ""
    Write-Host "=== Publishing Hatoclip $version for $rid ===" -ForegroundColor Cyan
    & dotnet publish $project -c Release -r $rid --self-contained -p:PublishSingleFile=true -o $outDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)"
    }

    $files = @(Get-ChildItem -LiteralPath $outDir -File -Recurse)
    $exe = Join-Path $outDir 'Hatoclip.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Hatoclip.exe not found in $outDir"
    }

    $extra = @($files | Where-Object { $_.Name -ne 'Hatoclip.exe' })
    if ($extra.Count -gt 0) {
        Write-Warning ("Publish output for $rid contains extra files (included in the zip): " +
            (($extra | ForEach-Object { $_.FullName.Substring($outDir.Length + 1) }) -join ', '))
    }

    # License notices of the bundled runtime and QRCoder go next to the exe (checked above that
    # dotnet publish itself produced nothing else).
    Copy-Item -LiteralPath $notices -Destination (Join-Path $outDir $noticesName)
    $files = @(Get-ChildItem -LiteralPath $outDir -File -Recurse)

    $zip = Join-Path $publishRoot "Hatoclip-$version-$rid.zip"
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    # Zip the publish folder contents (no top-level folder), so the archive holds Hatoclip.exe directly.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $outDir, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $results += [pscustomobject]@{
        RID       = $rid
        ExeBytes  = (Get-Item -LiteralPath $exe).Length
        ExeMB     = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
        ZipBytes  = (Get-Item -LiteralPath $zip).Length
        ZipMB     = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
        Files     = ($files | ForEach-Object { $_.Name }) -join ', '
        Zip       = $zip
    }
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
$results | Format-Table RID, ExeBytes, ExeMB, ZipBytes, ZipMB, Files -AutoSize | Out-String -Width 200 | Write-Host
$results | ForEach-Object { Write-Host $_.Zip }
