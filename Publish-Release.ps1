#Requires -Version 5.1
<#
.SYNOPSIS
    Build Release + obfuscation de CopilotBuddy.exe
.DESCRIPTION
    1. Vérifie que obfuscar est installé (dotnet global tool)
    2. Build Release (dotnet build -c Release)
    3. Lance l'obfuscation (obfuscar obfuscar.xml)
    4. Output final : bin\Release\net10.0-windows7.0\obfuscated\
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir 'CopilotBuddy.csproj'
$ObfuscarXml = Join-Path $ProjectDir 'obfuscar.xml'
$OutPath     = Join-Path $ProjectDir 'bin\x86\Release\net10.0-windows7.0\obfuscated'

# ── 1. Vérifier obfuscar ────────────────────────────────────────────────────
$obfuscarExe = "$env:USERPROFILE\.dotnet\tools\obfuscar.console.exe"
if (-not (Test-Path $obfuscarExe)) {
    Write-Host '[SETUP] obfuscar non trouvé — installation en cours...'
    dotnet tool install --global Obfuscar.GlobalTool
    if ($LASTEXITCODE -ne 0) { throw 'Échec installation Obfuscar.GlobalTool' }
    Write-Host '[SETUP] obfuscar installé.'
}

# ── 2. Build Release ────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[BUILD] dotnet build Release x86...'
Push-Location $ProjectDir
try {
    dotnet build $ProjectFile -c Release /p:Platform=x86
    if ($LASTEXITCODE -ne 0) { throw 'Échec du build Release' }
} finally {
    Pop-Location
}
Write-Host '[BUILD] OK.'

# ── 3. Obfuscation ──────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[OBFUSCAR] Obfuscation en cours...'
Push-Location $ProjectDir
try {
    & $obfuscarExe $ObfuscarXml
    if ($LASTEXITCODE -ne 0) { throw 'Échec obfuscar' }
} finally {
    Pop-Location
}
Write-Host '[OBFUSCAR] OK.'

# ── 4. Résultat ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "═══════════════════════════════════════════════"
Write-Host " Output obfusqué : $OutPath"
Write-Host " Distribuer ce dossier (pas bin\Release\ direct)"
Write-Host "═══════════════════════════════════════════════"
