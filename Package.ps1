#Requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Shared.ps1')
$bin = Join-Path $PSScriptRoot "Altium17-PartSearch/bin/$Configuration"
Assert-AltiumTarget $bin '17' -RequireManifest
$files = @('Altium17-PartSearch.dll','Altium17-PartSearch.Ins','Altium17-PartSearch.rcs','Altium17-PartSearch.target.json')
foreach ($name in $files) { if (-not (Test-Path (Join-Path $bin $name) -PathType Leaf)) { throw "Missing built artifact: $name" } }
$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
foreach ($name in $files) { Copy-Item (Join-Path $bin $name) $dist }
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$archive = Join-Path $release 'Altium-17-MPS.zip'
$archiveFiles = @($dist, (Join-Path $PSScriptRoot 'Deploy.ps1'), (Join-Path $PSScriptRoot '_Shared.ps1'), (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'README.md'))
$toolsParent = Join-Path ([System.IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
$toolsStage = Join-Path $toolsParent 'tools'
try {
    New-Item -ItemType Directory -Path $toolsStage -Force | Out-Null
    Get-ChildItem (Join-Path $PSScriptRoot 'tools') -File | Where-Object { $_.Extension -in '.py','.sh' } | ForEach-Object { Copy-Item $_.FullName $toolsStage }
    Compress-Archive -Path ($archiveFiles + @($toolsStage)) -DestinationPath $archive -Force
} finally { if (Test-Path $toolsParent) { Remove-Item $toolsParent -Recurse -Force } }
Write-Ok "Release archive: $archive"
