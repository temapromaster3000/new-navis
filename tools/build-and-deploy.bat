@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ==========================================
echo Navis Elevation - Build and Deploy
echo ==========================================
echo.

set "SOLUTION=build\NavisElevation.sln"
set "PLUGIN_DIR=%APPDATA%\Autodesk\Navisworks Manage 2021\Plugins"

if not exist "%SOLUTION%" (
    echo [ERROR] Solution not found: %SOLUTION%
    pause
    exit /b 1
)

echo [1/3] Building Navis Elevation (Debug, net48)...
dotnet build "%SOLUTION%" -c Debug --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build failed! & pause & exit /b 1 )
echo [OK] Build successful
echo.

set "SOURCE_DIR=bin\Debug\Navis"

if not exist "%SOURCE_DIR%\NavisElevation.dll" (
    echo [ERROR] Build output not found: %SOURCE_DIR%\NavisElevation.dll
    pause
    exit /b 1
)

echo [2/3] Preparing plugin folder...
if not exist "%PLUGIN_DIR%" (
    mkdir "%PLUGIN_DIR%"
    echo [OK] Created %PLUGIN_DIR%
) else (
    echo [OK] Folder exists: %PLUGIN_DIR%
)
echo.

echo [3/3] Deploying plugin...
rem Navisworks is still running locks these files, so retry for a few seconds.
set "COPIED="
for /l %%I in (1,1,10) do (
    if not defined COPIED (
        copy /Y "%SOURCE_DIR%\NavisElevation*.dll" "%PLUGIN_DIR%\" >nul 2>nul
        if not errorlevel 1 set "COPIED=1"
        if not defined COPIED (
            echo     attempt %%I failed, file locked - retrying...
            timeout /t 1 /nobreak >nul
        )
    )
)

if not defined COPIED (
    echo [ERROR] Could not copy files - close Navisworks and run this script again.
    pause
    exit /b 1
)

echo [OK] Deployed to %PLUGIN_DIR%
echo.
echo ==========================================
echo [SUCCESS] Build and deploy completed
echo ==========================================
echo.
echo Start Navisworks Manage 2021 to load the plugin.
echo If the button does not appear, check the ribbon for the plugin tab.
echo.
pause
exit /b 0
