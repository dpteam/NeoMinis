@echo off
setlocal
cd /d "%~dp0bin"
if not exist "SocialServer.exe" (
  echo SocialServer.exe not found, run build.cmd first.
  exit /b 1
)
SocialServer.exe %*
endlocal
