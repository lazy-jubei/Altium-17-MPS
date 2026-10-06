#Requires -Version 5.1
param([string]$AltiumInstallDir = $env:ALTIUM_INSTALL_DIR, [string]$ExtensionsRoot, [switch]$Force)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/Build.ps1" -AltiumInstallDir $AltiumInstallDir
& "$PSScriptRoot/Package.ps1"
& "$PSScriptRoot/Deploy.ps1" -ExtensionsRoot $ExtensionsRoot -Force:$Force
