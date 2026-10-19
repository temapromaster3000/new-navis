    @echo off
chcp 65001 >nul
    setlocal enabledelayedexpansion

rem Always work from the folder this script lives in: cmd resolves relative paths
rem against the current directory, not the script location.
cd /d "%~dp0"

rem The VS Code "C# Dev Kit" extension adds MSBuild targets built for .NET 10 to the
rem environment of its integrated terminal. The .NET 8 runtime installed here cannot
rem load them, and the build dies with MSB4062 before compilation even starts.
rem Clearing the variables affects this script and its child build process only.
if defined CustomAfterMicrosoftCommonTargets echo [i] Clearing VS Code C# Dev Kit MSBuild variables
set "CSDEVKIT_PROJECTDATA_TASKS_ASSEMBLY="
set "CustomAfterMicrosoftCommonTargets="
set "CustomAfterMicrosoftCommonCrossTargetingTargets="
set "EnableProjectDataOnBuild="
set "EnableProjectDataInProjectFolder="

echo ==========================================
echo Navis Elevation - Build and Deploy
echo ==========================================
echo.

set "SOLUTION=build\NavisElevation.sln"
set "SOURCE_DIR=bin\Debug\Navis"
set "PLUGIN_NAME=NavisElevation"

rem Navisworks 2021 auto-loads plug-ins only from the Plugins folder inside its
rem installation folder, which is writable with admin rights only. The documented
rem per-user location is an AppBundle under %APPDATA%\Autodesk\ApplicationPlugins:
rem a .bundle folder with a PackageContents.xml describing the assembly. Without
rem that file the bundle is skipped silently.
rem Do NOT put the DLL back into %APPDATA%\Autodesk\Navisworks Manage 2021\Plugins -
rem Navisworks 2021 does not scan it, which was verified by the absence of
rem %LOCALAPPDATA%\NavisElevation\plugin.log after a successful deployment.
set "BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\%PLUGIN_NAME%.bundle"
set "TARGET=%BUNDLE%\Contents\2021"
set "PACKAGE_SOURCE=deploy\PackageContents.xml"

echo [i] Project root: %CD%
echo [i] Bundle:        %BUNDLE%
echo [i] Target:        %TARGET%
echo.

if not exist "%PACKAGE_SOURCE%" (
    echo [ERROR] Bundle description not found: %CD%\%PACKAGE_SOURCE%
    echo         Without PackageContents.xml Navisworks ignores the plug-in without any error.
    pause
    exit /b 1
)

if not exist "%SOLUTION%" (
    echo [ERROR] Solution not found: %CD%\%SOLUTION%
    pause
    exit /b 1
)

echo [1/3] Building Navis Elevation (Debug, net48)...
dotnet build "%SOLUTION%" -c Debug --verbosity quiet
if errorlevel 1 ( echo [ERROR] Build failed! & pause & exit /b 1 )
echo [OK] Build successful
echo.

if not exist "%SOURCE_DIR%\%PLUGIN_NAME%.dll" (
    echo [ERROR] Build output not found: %CD%\%SOURCE_DIR%\%PLUGIN_NAME%.dll
    pause
    exit /b 1
)

echo [2/3] Preparing folders...
if not exist "%BUNDLE%" ( mkdir "%BUNDLE%" & echo [OK] Created %BUNDLE% ) else ( echo [OK] Exists %BUNDLE% )
if not exist "%TARGET%" ( mkdir "%TARGET%" & echo [OK] Created %TARGET% ) else ( echo [OK] Exists %TARGET% )
echo.

echo [3/3] Deploying plug-in...
rem Navisworks holds these files while running, so retry for a few seconds.
rem Only the single merged DLL is copied: NavisElevation.dll already contains the
rem Core code, and a second assembly beside it risks failing to resolve on load.
set "COPIED="
for /l %%I in (1,1,10) do (
    if not defined COPIED (
        copy /Y "%SOURCE_DIR%\%PLUGIN_NAME%.dll" "%TARGET%\" >nul 2>nul
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

rem The ribbon layout and the localization file are not inside the assembly: Navisworks
rem looks for them as files in a language sub-folder next to the plug-in. "ru-RU" is for
rem the Russian interface, "en-US" is the fallback. This is what the Autodesk blog on
rem the Navisworks ribbon describes: "the xaml and name file must reside in the
rem sub-folder of the plug-in dll. The sub-folder name needs to be a Language Code".
rem
rem The sub-folder is created next to the assembly, that is in Contents\2021. It was
rem checked on this machine: with the layout there the tab appears, with the layout in
rem the bundle root it does not. If a future Navisworks changes the lookup, the first
rem thing to try is putting these files into the bundle root instead.
set "RIBBON_SRC=src\%PLUGIN_NAME%.Navis\Ribbon\RibbonDefinition.xaml"
set "NAME_SRC=deploy\%PLUGIN_NAME%.name"

for %%L in (ru-RU en-US) do (
    if not exist "%TARGET%\%%L" mkdir "%TARGET%\%%L"

    copy /Y "%RIBBON_SRC%" "%TARGET%\%%L\RibbonDefinition.xaml" >nul 2>nul
    copy /Y "%NAME_SRC%" "%TARGET%\%%L\%PLUGIN_NAME%.name" >nul 2>nul
)

rem A layout of an earlier scheme, lying next to the dll. Not used any more.
if exist "%TARGET%\RibbonDefinition.xaml" del /F /Q "%TARGET%\RibbonDefinition.xaml"

if exist "%TARGET%\ru-RU\RibbonDefinition.xaml" (
    echo [OK] Ribbon layout and name file placed in:
    echo       %TARGET%\ru-RU
    echo       %TARGET%\en-US
) else (
    echo [i] Ribbon files not copied - the tab will not appear
)

rem The bundle description points at the assembly by relative path, so it must be
rem refreshed together with the DLL. Navisworks does not lock this file.
copy /Y "%PACKAGE_SOURCE%" "%BUNDLE%\PackageContents.xml" >nul
if errorlevel 1 (
    echo [ERROR] Could not copy %PACKAGE_SOURCE%
    pause
    exit /b 1
)

rem The old per-user Plugins folder is not scanned by Navisworks 2021. Leaving a
rem DLL there would suggest the plug-in is installed while never being loaded.
set "LEGACY=%APPDATA%\Autodesk\Navisworks Manage 2021\Plugins\%PLUGIN_NAME%"
if exist "%LEGACY%" (
    rmdir /S /Q "%LEGACY%"
    echo [OK] Removed obsolete folder %LEGACY%
)

echo [OK] Deployed to %TARGET%
echo [OK] Bundle description at %BUNDLE%\PackageContents.xml
echo.

echo ==========================================
echo [SUCCESS] Build and deploy completed
echo ==========================================
echo.
echo IMPORTANT: close Navisworks completely and start it again.
echo Plug-ins and bundles are read only at application startup.
echo.
echo If the button does not appear, send this file - it records whether
echo Navisworks loaded the assembly at all:
echo   %%LOCALAPPDATA%%\NavisElevation\plugin.log
echo.
pause
exit /b 0
