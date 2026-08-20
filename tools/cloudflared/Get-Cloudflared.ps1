[CmdletBinding()]
param(
    [string]$DestinationDirectory = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$version = '2026.8.2'
$expectedHash = 'c29eee2b121f5436a642eed69fd9767da7e7b8c510fa50aaa130337f931357b5'
$downloadUrl = "https://github.com/cloudflare/cloudflared/releases/download/$version/cloudflared-windows-amd64.exe"
$resolvedDestination = [System.IO.Path]::GetFullPath($DestinationDirectory)
[System.IO.Directory]::CreateDirectory($resolvedDestination) | Out-Null
$targetPath = Join-Path $resolvedDestination 'cloudflared.exe'
$temporaryPath = Join-Path $resolvedDestination 'cloudflared.download.tmp'

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $temporaryPath -UseBasicParsing
    $actualHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "cloudflared SHA-256 mismatch. Expected $expectedHash, received $actualHash."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $temporaryPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "cloudflared Authenticode signature is not valid: $($signature.StatusMessage)"
    }
    if ($signature.SignerCertificate.Subject -notmatch 'Cloudflare') {
        throw "cloudflared signer is unexpected: $($signature.SignerCertificate.Subject)"
    }

    Move-Item -LiteralPath $temporaryPath -Destination $targetPath -Force
    Set-Content -LiteralPath "$targetPath.sha256" -Value $expectedHash -Encoding ascii
    Write-Host "Verified cloudflared $version at $targetPath"
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
