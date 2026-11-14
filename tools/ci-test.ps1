# =========================================================================
#  Social Server - one-shot CI run
#
#  build -> self-test -> start a clean instance in a temp folder ->
#  API/static tests (node tools/test-web.mjs) -> stop -> report
#
#  usage:
#    powershell -ExecutionPolicy Bypass -File tools\ci-test.ps1
#    ... -Port 18080 -Keep          keep the temp folder for inspection
# =========================================================================
[CmdletBinding()]
param(
    [int]$Port = 18080,
    [switch]$Keep,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = New-Object 'System.Collections.Generic.List[string]'
$failed = 0

function Step([string]$text) {
    Write-Host ''
    Write-Host ('== ' + $text) -ForegroundColor Cyan
}

function Fail([string]$text) {
    $script:failed++
    $results.Add('FAIL ' + $text)
    Write-Host ('FAIL ' + $text) -ForegroundColor Red
}

function Pass([string]$text) {
    $results.Add('PASS ' + $text)
    Write-Host ('PASS ' + $text) -ForegroundColor Green
}

# ---------------------------------------------------------------- build

if (-not $SkipBuild) {
    Step 'build'
    & (Join-Path $root 'build.cmd') | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'build'; exit 1 }
    Pass 'build'
} else {
    Pass 'build (skipped)'
}

$bin = Join-Path $root 'bin'
$exe = Join-Path $bin 'SocialServer.exe'
if (-not (Test-Path $exe)) { Fail 'SocialServer.exe missing'; exit 1 }

# ------------------------------------------------------------ self test

Step 'self-test'
$self = & $exe --selftest 2>&1
$selfText = ($self | Out-String)
if ($selfText -match 'ALL TESTS PASSED') { Pass 'crypto/storage self-test' }
else { Fail 'self-test'; Write-Host $selfText }

# --------------------------------------------------------------- instance

Step 'clean instance'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('social-ci-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $temp | Out-Null
Copy-Item -Path (Join-Path $bin '*') -Destination $temp -Recurse -Force
Remove-Item -Path (Join-Path $temp 'data') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $temp 'logs') -Recurse -Force -ErrorAction SilentlyContinue

# pre-seed config.ini from the example, with the captcha off so an automated
# client can register. Everything else stays at the shipped defaults.
$cfgPath = Join-Path $temp 'config.ini'
$example = Join-Path $temp 'config.example.ini'
if (Test-Path $example) {
    # guest mode on as well, so the suite can exercise the read-only session
    (Get-Content $example -Raw) -replace 'requireCaptcha=true', 'requireCaptcha=false' `
        -replace '(?m)^guestMode=false', 'guestMode=true' |
        Set-Content -Path $cfgPath -Encoding UTF8
} else {
    Remove-Item -Path $cfgPath -ErrorAction SilentlyContinue
}

$proc = Start-Process -FilePath (Join-Path $temp 'SocialServer.exe') -ArgumentList @('--no-console', '--port', $Port, '--admin-port', ($Port + 10)) `
    -WorkingDirectory $temp -PassThru -WindowStyle Hidden

$base = 'http://127.0.0.1:' + $Port
$adminBase = 'http://127.0.0.1:' + ($Port + 10)
$ready = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    try {
        $r = Invoke-WebRequest -Uri ($base + '/api/health') -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}
if (-not $ready) {
    Fail 'server did not start'
    try { Stop-Process -Id $proc.Id -Force } catch { }
    exit 1
}
Pass ('server started on ' + $base)

$adminReady = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    try {
        $r = Invoke-WebRequest -Uri ($adminBase + '/api/health') -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $adminReady = $true; break }
    } catch { }
}
if ($adminReady) { Pass ('admin board started on ' + $adminBase) } else { Fail 'admin board did not start' }

# fresh install should have generated config + secrets
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw
    if ($cfg -match 'serverSecret=[0-9a-f]{32,}') { Pass 'config.ini generated with secrets' }
    else { Fail 'config.ini has no generated secret' }
} else { Fail 'config.ini was not created' }

# ------------------------------------------------------------- api tests

Step 'api + static tests'
Push-Location $root
try {
    & node 'tools/test-web.mjs' $base $adminBase 2>&1 | Out-Host
    if ($LASTEXITCODE -eq 0) { Pass 'node api test suite' } else { Fail 'node api test suite' }
} catch {
    Fail ('node api test suite: ' + $_.Exception.Message)
} finally {
    Pop-Location
}

# restart survival: data written by this run must be readable after restart
Step 'restart'
try { Stop-Process -Id $proc.Id -Force } catch { }
Start-Sleep -Milliseconds 800
$proc2 = Start-Process -FilePath (Join-Path $temp 'SocialServer.exe') -ArgumentList @('--no-console', '--port', $Port, '--admin-port', ($Port + 10)) `
    -WorkingDirectory $temp -PassThru -WindowStyle Hidden
$ready2 = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    try {
        $r = Invoke-WebRequest -Uri ($base + '/api/health') -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { $ready2 = $true; break }
    } catch { }
}
if ($ready2) {
    $health = (Invoke-WebRequest -Uri ($base + '/api/health') -UseBasicParsing).Content | ConvertFrom-Json
    if ($health.users -ge 2) { Pass ('data survived restart (' + $health.users + ' users)') }
    else { Fail ('data lost after restart (users=' + $health.users + ')') }
} else { Fail 'server did not restart' }
try { Stop-Process -Id $proc2.Id -Force } catch { }

# ------------------------------------------------------------------ done

Step 'summary'
$results | ForEach-Object { Write-Host ('  ' + $_) }
Write-Host ''
if ($failed -eq 0) {
    Write-Host 'CI: ALL CHECKS PASSED' -ForegroundColor Green
} else {
    Write-Host ('CI: ' + $failed + ' CHECK(S) FAILED') -ForegroundColor Red
    $logDir = Join-Path $temp 'logs'
    if (Test-Path $logDir) {
        Write-Host '--- server log tail ---' -ForegroundColor Yellow
        Get-ChildItem $logDir -Filter '*.log' | ForEach-Object { Get-Content $_.FullName -Tail 40 | Write-Host }
    }
}

if ($Keep) { Write-Host ('kept: ' + $temp) } else { Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue }
exit $(if ($failed -eq 0) { 0 } else { 1 })

