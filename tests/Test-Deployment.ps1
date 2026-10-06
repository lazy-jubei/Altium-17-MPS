param([ValidateSet('17')][string]$AltiumVersion = '17')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('EasyEDA-deploy-test-' + [guid]::NewGuid().ToString('N'))
$extensions = Join-Path $testRoot 'Extensions'
New-Item -ItemType Directory -Path $extensions -Force | Out-Null
$registry = Join-Path $extensions 'ExtensionsRegistry.xml'
'<Extensions><Item HRID="KeepMe"><Path>C:\KeepMe</Path></Item></Extensions>' | Set-Content $registry
$cultureBefore = [System.Threading.Thread]::CurrentThread.CurrentCulture
try {
    [System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('fr-FR')
    & (Join-Path $repo 'Deploy.ps1') -AltiumVersion $AltiumVersion -ExtensionsRoot $extensions -Force
    [xml]$xml = Get-Content $registry
    $item = $xml.SelectSingleNode('/Extensions/Item[@HRID="Altium17-PartSearch"]')
    if (-not $item -or $item.ReleasedDate -notmatch '^\d+\.\d+$') { throw 'Locale corrupted the release date.' }
    if (-not $xml.SelectSingleNode('/Extensions/Item[@HRID="KeepMe"]')) { throw 'Existing extension was lost.' }
    $expectedDxp = if ($AltiumVersion -eq '17') { '1.0.5.5' } else { '1.0.16.41' }
    $expectedEdp = if ($AltiumVersion -eq '17') { '10.0.5.5' } else { '1.0.16.41' }
    if ($item.PlatformVersions.DXP.BuildNumber -ne $expectedDxp -or $item.PlatformVersions.EDP.BuildNumber -ne $expectedEdp) {
        throw "Incompatible platform requirements for AD$AltiumVersion."
    }
    $stale = Join-Path $extensions 'Altium17-PartSearch/stale.dll'
    'old' | Set-Content $stale
    $item.Version = '1.0.1.0'
    $item.PlatformVersions.DXP.BuildNumber = '99.0.0.0'
    $item.PlatformVersions.EDP.BuildNumber = '99.0.0.0'
    $xml.Save($registry)
    & (Join-Path $repo 'Deploy.ps1') -AltiumVersion $AltiumVersion -ExtensionsRoot $extensions -Force
    [xml]$xml = Get-Content $registry
    if ($xml.SelectNodes('/Extensions/Item[@HRID="Altium17-PartSearch"]').Count -ne 1) { throw 'Duplicate registry entry.' }
    if ($xml.SelectSingleNode('/Extensions/Item[@HRID="Altium17-PartSearch"]').Version -ne '0.1.0.0') { throw 'Existing registry version was not updated.' }
    $item = $xml.SelectSingleNode('/Extensions/Item[@HRID="Altium17-PartSearch"]')
    if ($item.PlatformVersions.DXP.BuildNumber -ne $expectedDxp -or $item.PlatformVersions.EDP.BuildNumber -ne $expectedEdp) {
        throw 'Existing incompatible platform requirements were not repaired.'
    }
    if (Test-Path $stale) { throw 'Stale dependencies were not removed.' }
    '<invalid' | Set-Content $registry
    $before = (Get-FileHash (Join-Path $extensions 'Altium17-PartSearch/Altium17-PartSearch.dll')).Hash
    $rejected = $false
    try { & (Join-Path $repo 'Deploy.ps1') -AltiumVersion $AltiumVersion -ExtensionsRoot $extensions -Force } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid registry accepted.' }
    if ((Get-FileHash (Join-Path $extensions 'Altium17-PartSearch/Altium17-PartSearch.dll')).Hash -ne $before) { throw 'Failed deploy overwrote the plugin.' }
    Write-Host "PASS: AD$AltiumVersion PowerShell installation, update, platform versions, invariant dates and invalid-registry checks."
} finally {
    [System.Threading.Thread]::CurrentThread.CurrentCulture = $cultureBefore
    Remove-Item $testRoot -Recurse -Force
}
