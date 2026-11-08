@echo off
rem ============================================================
rem  Social Server - firewall rules (asks for UAC "Yes")
rem  Ports are read from bin\config.ini.
rem
rem  extra options:
rem    firewall.cmd -PublicAny   allow the web port from anywhere
rem    firewall.cmd -LanOnly     allow the web port from the LAN only
rem    firewall.cmd -Remove      delete the Social Server rules
rem ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0firewall.ps1" %*
endlocal
