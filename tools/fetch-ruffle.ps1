# ============================================================================
#  Fetches the Ruffle Flash emulator into public/assets/vendor/ruffle/
#
#  Ruffle is a WASM reimplementation of the Flash player: ~28 MB of .wasm plus
#  a few .js files. That is far too much to keep in git, so the build does not
#  carry it - this script downloads it once from the npm registry and unpacks
#  only the runtime files. Everything is then served from our own origin, so
#  the browser never talks to a CDN and the CSP stays strict.
#
#  Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-ruffle.ps1
#  Re-run with -Force to replace an existing copy.
# ============================================================================
param(
    [string]$Version = "0.7.1",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dest = Join-Path $root "public\assets\vendor\ruffle"
$marker = Join-Path $dest "ruffle.js"

if ((Test-Path $marker) -and -not $Force) {
    Write-Host "Ruffle is already in public\assets\vendor\ruffle (use -Force to refresh)."
    exit 0
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ruffle-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    $url = "https://registry.npmjs.org/@ruffle-rs/ruffle/-/ruffle-$Version.tgz"
    $tgz = Join-Path $tmp "ruffle.tgz"
    Write-Host "downloading $url"
    Invoke-WebRequest $url -OutFile $tgz -UseBasicParsing -TimeoutSec 180

    Write-Host "unpacking"
    tar -xzf $tgz -C $tmp
    if ($LASTEXITCODE -ne 0) { throw "tar failed (Windows 10+ ships bsdtar)" }

    $src = Join-Path $tmp "package"
    if (-not (Test-Path (Join-Path $src "ruffle.js"))) { throw "unexpected tarball layout" }

    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest | Out-Null

    # the runtime: ruffle.js, the core bundles, the wasm modules and the licences
    Get-ChildItem $src -File | Where-Object {
        ($_.Extension -in @(".js", ".wasm")) -or ($_.Name -like "LICENSE*")
    } | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $dest $_.Name)
    }

    # source maps are only useful while debugging Ruffle itself
    Get-ChildItem $dest -Filter "*.map" -File -ErrorAction SilentlyContinue | Remove-Item -Force

    $total = (Get-ChildItem $dest -File | Measure-Object -Property Length -Sum).Sum
    $count = (Get-ChildItem $dest -File).Count
    Write-Host ("ruffle {0}: {1} files, {2:N1} MB in public\assets\vendor\ruffle" -f $Version, $count, ($total / 1MB))
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
