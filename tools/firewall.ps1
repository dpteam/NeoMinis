# =========================================================================
#  Social Server - Windows Firewall setup
#
#  Adds inbound TCP allow rules for the web port (and its fallbacks) so the
#  site can be reached from the network / the internet. Rules are scoped to
#  SocialServer.exe and recreated on every run.
#
#  Requires admin rights: the script relaunches itself with a UAC prompt.
#
#  usage (double click firewall.cmd, or):
#    powershell -ExecutionPolicy Bypass -File tools\firewall.ps1
#    ... -PublicAny   allow the port from anywhere (direct expose, no proxy)
#    ... -LanOnly     allow the port from private networks only
#    ... -Remove      delete the Social Server rules
# =========================================================================
[CmdletBinding()]
param(
    [switch]$PublicAny,
    [switch]$LanOnly,
    [switch]$Remove,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\SocialServer.exe'
$ini = Join-Path $root 'bin\config.ini'
$reportPath = Join-Path $env:TEMP 'social-firewall.log'
$report = New-Object 'System.Collections.Generic.List[string]'

function Add-Line([string]$text) {
    $report.Add($text)
    Write-Host $text
}

function Save-Report {
    try { Set-Content -Path $reportPath -Value $report -Encoding UTF8 } catch { }
}

# --------------------------------------------------------------- elevation

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    if ($PublicAny) { $argList += '-PublicAny' }
    if ($LanOnly) { $argList += '-LanOnly' }
    if ($Remove) { $argList += '-Remove' }
    if ($NoPause) { $argList += '-NoPause' }

    Write-Host 'Admin rights are required - a UAC prompt will appear (press "Yes").' -ForegroundColor Yellow
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null
    } catch {
        Write-Host "Elevation cancelled: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host 'Run this file as administrator manually.' -ForegroundColor Red
    }
    Start-Sleep -Seconds 1
    exit
}

# ------------------------------------------------------------------- config

function Read-IniValues([string]$path) {
    $map = @{}
    $section = ''
    if (-not (Test-Path $path)) { return $map }
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*\[(.+?)\]\s*$') { $section = $Matches[1].ToLowerInvariant(); continue }
        if ($line -match '^\s*([A-Za-z0-9_.]+)\s*=\s*(.*?)\s*$') {
            $key = $Matches[1].ToLowerInvariant()
            $map[$key] = $Matches[2]
            if ($section) { $map[$section + '.' + $key] = $Matches[2] }
        }
    }
    return $map
}

$values = Read-IniValues $ini
$port = 8080
$adminPort = 8090
$fallbacks = @()
$adminFallbacks = @()
if ($values.ContainsKey('server.port') -and $values['server.port']) { $port = [int]$values['server.port'] }
if ($values.ContainsKey('server.adminport') -and $values['server.adminport']) { $adminPort = [int]$values['server.adminport'] }
if ($values.ContainsKey('server.portfallbacks') -and $values['server.portfallbacks']) {
    $fallbacks = $values['server.portfallbacks'].Split(',') | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
}
if ($values.ContainsKey('server.adminportfallbacks') -and $values['server.adminportfallbacks']) {
    $adminFallbacks = $values['server.adminportfallbacks'].Split(',') | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
}
$adminEnabled = $true
if ($values.ContainsKey('server.adminenabled') -and $values['server.adminenabled'] -match '^(false|0|no|off)$') { $adminEnabled = $false }

$ports = @($port) + $fallbacks | Select-Object -Unique
if ($adminEnabled) { $ports = @($ports) + @($adminPort) + $adminFallbacks | Select-Object -Unique }

$bind = if ($values.ContainsKey('server.bind')) { $values['server.bind'] } else { '0.0.0.0' }
$adminBind = if ($values.ContainsKey('server.adminbind')) { $values['server.adminbind'] } else { '0.0.0.0' }

# --------------------------------------------------------------- addresses

$lanRemote = @(
    'LocalSubnet',
    '127.0.0.1/32',
    '10.0.0.0/8',
    '172.16.0.0/12',
    '192.168.0.0/16',
    '169.254.0.0/16',
    '100.64.0.0/10'
)
$remote = if ($PublicAny) { @('Any') } else { $lanRemote }

Add-Line ''
Add-Line '=== Social Server - firewall setup ==='
Add-Line ('config : ' + $(if (Test-Path $ini) { $ini } else { 'not found, using defaults' }))
Add-Line ('exe    : ' + $(if (Test-Path $exe) { $exe } else { 'NOT FOUND (rules will be port-only)' }))
Add-Line ('ports  : ' + ($ports -join ', ') + '   public bind=' + $bind + '   admin bind=' + $adminBind)
Add-Line ('source : ' + $(if ($PublicAny) { 'Any (public expose)' } else { 'LAN / private only' }))
Add-Line ''

$group = 'Social Server'

# ---------------------------------------------------------------- removal

if ($Remove) {
    $existing = Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue
    if (-not $existing) {
        Add-Line 'No Social Server rules found, nothing to remove.'
    } else {
        foreach ($rule in $existing) {
            try {
                Remove-NetFirewallRule -Name $rule.Name -ErrorAction Stop
                Add-Line ('removed : ' + $rule.Name)
            } catch {
                Add-Line ('FAILED  : ' + $rule.Name + ' - ' + $_.Exception.Message)
            }
        }
    }
    Add-Line ''
    Add-Line ('report  : ' + $reportPath)
    Save-Report
    if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit
}

# ---------------------------------------------------------------- install

if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
    Add-Line 'New-NetFirewallRule is not available (Windows XP / Vista without WinRM).'
    Add-Line 'Use netsh manually, for example:'
    Add-Line ('  netsh firewall add portopening TCP ' + $port + ' "Social Server"')
    Save-Report
    if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}

foreach ($p in $ports) {
    $name = 'Social Server TCP ' + $p
    try {
        Remove-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Out-Null
        $params = @{
            DisplayName = $name
            Group       = $group
            Direction   = 'Inbound'
            Action      = 'Allow'
            Protocol    = 'TCP'
            LocalPort   = $p
            Profile     = 'Any'
            RemoteAddress = $remote
            Description = 'Social Server web interface'
        }
        if (Test-Path $exe) { $params['Program'] = $exe }
        New-NetFirewallRule @params | Out-Null
        Add-Line ('added   : ' + $name)
    } catch {
        Add-Line ('FAILED  : ' + $name + ' - ' + $_.Exception.Message)
    }
}

Add-Line ''
Add-Line 'Rules are grouped as "Social Server" - remove them with:'
Add-Line '  tools\firewall.cmd -Remove'
Add-Line ''
Add-Line ('report  : ' + $reportPath)
Save-Report
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
