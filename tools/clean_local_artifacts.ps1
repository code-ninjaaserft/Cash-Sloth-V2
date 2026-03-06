param(
    [switch]$IncludePackageCaches
)

$ErrorActionPreference = "Stop"

$paths = @(
    ".vs",
    "build",
    "out",
    "src/CashSloth.App/bin",
    "src/CashSloth.App/obj"
)

if ($IncludePackageCaches) {
    $paths += @(
        ".nuget",
        ".dotnet-home",
        ".appdata",
        ".userprofile"
    )
}

foreach ($path in $paths) {
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
        Write-Host "Removed $path"
    }
}

Write-Host "Done."
