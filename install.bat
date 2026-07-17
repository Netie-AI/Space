@echo off
setlocal
set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
cd /d "%~dp0"

echo.
echo  Netie Space Installer
echo  =====================
echo.

call :StopNetieSpaceIfRunning

echo  Building local embed sidecar (Rust, optional)...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\build-netie-embed.ps1"

powershell -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -RegisterStartup -MaxRetries 5 -RetryDelaySeconds 2
if errorlevel 1 (
    echo.
    echo  Install failed after retrying. See errors above.
    pause
    exit /b 1
)

echo.
echo  Bundling captions + Remove BG tools if missing...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\ensure-ai-tools.ps1"

echo  Registering AirGPT connector (if AirGPT present)...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\register-airgpt-plugin.ps1"
echo.

echo  Installing complete!
echo.
echo  - Portable app: dist\NetieSpace\NetieSpace.exe
echo  - Auto-starts on every Windows login
echo  - No .NET install needed on this or other laptops
echo.
echo  Copy the entire dist\NetieSpace folder to any Windows 10/11 laptop.
echo.
pause
exit /b 0

:StopNetieSpaceIfRunning
REM Keep redirects OUTSIDE parenthesized IF blocks — otherwise tasklist sees "2>nul" as an argument.
tasklist /FI "IMAGENAME eq NetieSpace.exe" /NH 2>nul | find /I "NetieSpace.exe" >nul
if errorlevel 1 exit /b 0

echo  Stopping running NetieSpace.exe...
taskkill /IM NetieSpace.exe /T >nul 2>&1
timeout /T 3 /NOBREAK >nul

tasklist /FI "IMAGENAME eq NetieSpace.exe" /NH 2>nul | find /I "NetieSpace.exe" >nul
if errorlevel 1 exit /b 0

echo  NetieSpace.exe did not close gracefully; forcing it to stop...
taskkill /F /IM NetieSpace.exe /T >nul 2>&1
timeout /T 2 /NOBREAK >nul
exit /b 0
