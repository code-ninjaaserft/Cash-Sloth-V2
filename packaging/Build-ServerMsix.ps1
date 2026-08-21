[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.5.0.0',
    [string]$Publisher = 'CN=CashSloth Internal',
    [string]$CertificatePath,
    [securestring]$CertificatePassword
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repositoryRoot 'artifacts\server-msix'
$publishDirectory = Join-Path $artifactRoot 'layout'
$assetsDirectory = Join-Path $publishDirectory 'Assets'
$manifestTemplate = Join-Path $PSScriptRoot 'CashSloth.Server.Package\Package.appxmanifest'
$manifestPath = Join-Path $publishDirectory 'AppxManifest.xml'
$packagePath = Join-Path $artifactRoot "CashSloth.Server_${Version}_x64.msix"
$cloudflaredDirectory = Join-Path $repositoryRoot 'tools\cloudflared'
if (-not $artifactRoot.StartsWith($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Packaging output resolved outside the repository.'
}

& (Join-Path $cloudflaredDirectory 'Get-Cloudflared.ps1') -DestinationDirectory $cloudflaredDirectory

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

dotnet publish (Join-Path $repositoryRoot 'src\CashSloth.Server\CashSloth.Server.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace('Version="1.0.0.0"', ('Version="{0}"' -f $Version))
$manifest = $manifest.Replace('Publisher="CN=CashSloth Internal"', ('Publisher="{0}"' -f $Publisher))
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding utf8

[System.IO.Directory]::CreateDirectory($assetsDirectory) | Out-Null
Add-Type -AssemblyName System.Drawing
$sourceLogo = Join-Path $repositoryRoot 'src\CashSloth.App\Assets\CashSlothLogo.png'
function Write-Logo([string]$name, [int]$width, [int]$height) {
    $source = [System.Drawing.Image]::FromFile($sourceLogo)
    try {
        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $width, $height)
            }
            finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $assetsDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
    finally { $source.Dispose() }
}
Write-Logo 'StoreLogo.png' 50 50
Write-Logo 'Square44x44Logo.png' 44 44
Write-Logo 'Square150x150Logo.png' 150 150
Write-Logo 'Wide310x150Logo.png' 310 150

function Find-WindowsSdkTool([string]$toolName) {
    $command = Get-Command $toolName -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitsRoot)) { return $null }
    return Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$toolName" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

$makeAppx = Find-WindowsSdkTool 'makeappx.exe'
if (-not $makeAppx) { throw 'makeappx.exe was not found. Install the Windows SDK.' }
& $makeAppx pack /d $publishDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed.' }

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Warning "MSIX was created but is unsigned. Re-run with -CertificatePath; release packages must be signed."
}
else {
    $signTool = Find-WindowsSdkTool 'signtool.exe'
    if (-not $signTool) { throw 'signtool.exe was not found. Install the Windows SDK.' }
    if ($CertificatePassword) {
        $plainPassword = [System.Net.NetworkCredential]::new('', $CertificatePassword).Password
        try { & $signTool sign /fd SHA256 /f $CertificatePath /p $plainPassword $packagePath }
        finally { $plainPassword = $null }
    }
    else {
        & $signTool sign /fd SHA256 /f $CertificatePath $packagePath
    }
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }
    & $signTool verify /pa /all $packagePath
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
}

Write-Host "Package created: $packagePath"
