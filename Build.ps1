#Requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$AltiumInstallDir = $env:ALTIUM_INSTALL_DIR)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Shared.ps1')
if (-not $AltiumInstallDir) { $AltiumInstallDir = 'C:\Program Files (x86)\Altium\AD17' }
Invoke-Cmd 'dotnet' @('build', (Join-Path $PSScriptRoot 'Altium17-PartSearch/Altium17-PartSearch.csproj'), '-c', $Configuration, '--nologo', "-p:AltiumInstallDir=$AltiumInstallDir")
