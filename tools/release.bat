@echo off
chcp 65001 >nul
echo ==========================================
echo Navis Elevation - Release
echo ==========================================
echo.
echo This script builds the plug-in and the updater, packs the update
echo payload, builds the installer and publishes a GitHub Release.
echo.
echo Run it with -DryRun to see the files without publishing anything.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
echo.
pause
