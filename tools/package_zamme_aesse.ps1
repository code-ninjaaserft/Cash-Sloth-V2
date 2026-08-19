param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd-HHmm"),
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$csproj = Join-Path $repoRoot "src\CashSloth.App\CashSloth.App.csproj"
$featureConfig = Join-Path $repoRoot "src\CashSloth.App\CashSloth.Features.zamme-aesse.json"
$outputRoot = Join-Path $repoRoot "artifacts\zamme-aesse"
$publishDir = Join-Path $outputRoot "publish"
$zipPath = Join-Path $outputRoot "cashsloth-zamme-aesse-$Version-windows-x64.zip"

if (-not (Test-Path -LiteralPath $csproj)) {
    throw "App project not found: $csproj"
}

if (-not (Test-Path -LiteralPath $featureConfig)) {
    throw "Feature config not found: $featureConfig"
}

$resolvedRepoRoot = [System.IO.Path]::GetFullPath("$repoRoot")
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($outputRoot)
if (-not $resolvedOutputRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean output outside repository: $resolvedOutputRoot"
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @("publish", $csproj, "-c", "Release", "-o", $publishDir, "--no-restore")
$restoreArgs = @("restore", $csproj)
if (-not $FrameworkDependent) {
    $restoreArgs += @("-r", "win-x64")
    $publishArgs += @("-r", "win-x64", "--self-contained", "true")
}

dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$coreDll = Join-Path $publishDir "CashSlothCore.dll"
if (-not (Test-Path -LiteralPath $coreDll)) {
    $coreDllCandidate = Get-ChildItem -Path (Join-Path $repoRoot "src\CashSloth.App\bin\Release") -Recurse -Filter "CashSlothCore.dll" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $coreDllCandidate) {
        throw "CashSlothCore.dll was not copied to the publish output and no Release build copy was found."
    }

    Copy-Item -LiteralPath $coreDllCandidate.FullName -Destination $coreDll -Force
}

Copy-Item -LiteralPath $featureConfig -Destination (Join-Path $publishDir "CashSloth.Features.json") -Force

$notes = @"
CashSloth Zamme Aesse
=====================

Start: CSV2.exe

This package is intentionally feature-gated for the Zamme Aesse flow.
Visible: Shop/catalog main page, History, Settings.
Hidden by CashSloth.Features.json: Presets, Accounts, Event networking, Customer Display, Onboarding.
Enabled by CashSloth.Features.json: keep laptop awake, soft kiosk fullscreen, password-gated exit, Windows lock on exit.

To unlock features for a controlled device, edit CashSloth.Features.json next to CSV2.exe and restart the app.
This is a local product-mode switch, not a security or licensing boundary.

Kiosk exit password:
- On the first kiosk exit, CashSloth asks you to set the exit password.
- Later exits require that password.
- After an allowed exit, Windows is locked and needs the Windows account password/PIN again.
"@

Set-Content -LiteralPath (Join-Path $publishDir "README-ZAMME-AESSE.txt") -Value $notes -Encoding UTF8
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Package created:"
Write-Host $zipPath
