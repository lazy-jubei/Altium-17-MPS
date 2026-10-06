# Shared helpers - dot-source this file; do not run directly.

function Write-Header([string]$Text) {
    Write-Host ""
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "  $('-' * $Text.Length)" -ForegroundColor DarkGray
}
function Write-Ok([string]$Text)   { Write-Host "  [OK]   $Text" -ForegroundColor Green  }
function Write-Info([string]$Text) { Write-Host "  [ ]   $Text" -ForegroundColor Gray   }
function Write-Warn([string]$Text) { Write-Host "  [!!]  $Text" -ForegroundColor Yellow }
function Write-Fail([string]$Text) { Write-Host "  [ERR] $Text" -ForegroundColor Red    }

function Invoke-Cmd([string]$Exe, [string[]]$ArgList) {
    Write-Info "$([System.IO.Path]::GetFileName($Exe)) $($ArgList -join ' ')"
    $result = & $Exe @ArgList 2>&1
    if ($LASTEXITCODE -ne 0) {
        $result | Write-Host -ForegroundColor DarkGray
        throw "Command failed (exit $LASTEXITCODE)"
    }
    $result | Where-Object { $_ -match 'error|warning' } |
        ForEach-Object { Write-Host "     $_" -ForegroundColor DarkGray }
}

function Assert-AltiumTarget([string]$Directory, [string]$AltiumVersion, [switch]$RequireManifest) {
    $manifestPath = Join-Path $Directory 'Altium17-PartSearch.target.json'
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Missing target manifest in $Directory. Rebuild for AD17."
    }
    $target = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $framework = 'net48'
    $architecture = 'x86'
    if ($AltiumVersion -ne '17' -or $target.altiumVersion -ne '17' -or $target.framework -ne $framework -or $target.architecture -ne $architecture) {
        throw "Package target does not match AD$AltiumVersion ($framework/$architecture). Rebuild with the matching -AltiumVersion."
    }
}

# Refuse ambiguous installations rather than deploying to the most recently modified one.
function Find-AltiumExtRoot([string]$ExtensionsRoot) {
    if ($ExtensionsRoot) {
        if (-not (Test-Path (Join-Path $ExtensionsRoot 'ExtensionsRegistry.xml'))) {
            throw "No ExtensionsRegistry.xml in $ExtensionsRoot"
        }
        return (Resolve-Path $ExtensionsRoot).Path
    }
    $altiumBase = 'C:\ProgramData\Altium'
    if (-not (Test-Path $altiumBase)) { return $null }
    $candidates = @(Get-ChildItem $altiumBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^Altium Designer \{[0-9A-Fa-f\-]+\}$' } |
        ForEach-Object { Join-Path $_.FullName 'Extensions' } |
        Where-Object { Test-Path (Join-Path $_ 'ExtensionsRegistry.xml') })
    if ($candidates.Count -gt 1) { throw 'Multiple Altium installations found. Select one with -ExtensionsRoot.' }
    if ($candidates.Count -eq 0) { return $null }
    return $candidates[0]
}
