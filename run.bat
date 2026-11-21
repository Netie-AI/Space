@echo off
setlocal
set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
cd /d "%~dp0"

set "DIST_EXE=%~dp0dist\NetieSpace\NetieSpace.exe"
set "DEV_DLL=%~dp0src\NetieSpace\bin\Release\net8.0-windows10.0.19041.0\win-x64\NetieSpace.dll"
set "DEV_DLL2=%~dp0src\NetieSpace\bin\Release\net8.0-windows10.0.19041.0\NetieSpace.dll"

REM Prefer self-contained portable build - no .NET runtime popup
if exist "%DIST_EXE%" (
    echo Starting Netie Space ^(portable^)...
    start "" "%DIST_EXE%"
    goto :done
)

REM Build portable if dotnet available
where dotnet >nul 2>&1
if not errorlevel 1 (
    echo Building portable package first time...
    powershell -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1"
    if exist "%DIST_EXE%" (
        start "" "%DIST_EXE%"
        goto :done
    )
)

REM Fallback: launch via dotnet host
if exist "%DEV_DLL%" (
    echo Starting via dotnet host...
    dotnet exec "%DEV_DLL%"
    goto :done
)
if exist "%DEV_DLL2%" (
    echo Starting via dotnet host...
    dotnet exec "%DEV_DLL2%"
    goto :done
)

echo.
echo  Netie Space not built yet.
echo  Run: install.bat
echo.
pause
exit /b 1

:done
echo.
echo  Netie Space started. Check the system tray for the Netie Space icon.
echo  Select a file in Explorer or Desktop, then press Space to preview.
echo.
