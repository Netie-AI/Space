@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install-dotnet.ps1" %*
pause
