param(
    [string]$NotesFile = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "build\NavisElevation.sln"
$artifactsDir = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifactsDir "publish"

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) {
        throw $message
    }
}

Write-Host "=========================================="
Write-Host " Navis Elevation - Release"
Write-Host "=========================================="

# --- 1. Version -------------------------------------------------------------
$version = (Get-Content (Join-Path $repoRoot "Version.txt") -Raw).Trim()
if (-not $version) {
    throw "Version.txt is empty."
}
$tag = "v$version"
Write-Host "[1/7] Release version: $version (tag $tag)"

# --- 2. Prerequisites -------------------------------------------------------
Write-Host "[2/7] Checking prerequisites..."
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) not found. Install it from https://cli.github.com/ and run 'gh auth login'."
}
gh auth status | Out-Null
Assert-LastExitCode "gh is not authenticated. Run 'gh auth login' first."

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    "D:\Soft\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isinfo.php"
}
Write-Host "      gh CLI OK, ISCC found: $iscc"

# --- 3. Build ----------------------------------------------------------------
Write-Host "[3/7] Building (Release)..."
dotnet build $solution -c Release --verbosity quiet
Assert-LastExitCode "Build failed: NavisElevation.sln (Release)"

$pluginDll = Join-Path $repoRoot "bin\Release\Navis\NavisElevation.dll"
$updaterExe = Join-Path $repoRoot "bin\Release\Updater\NavisElevation.Updater.exe"

if (-not (Test-Path $pluginDll)) {
    throw "Plug-in build output not found: $pluginDll"
}
if (-not (Test-Path $updaterExe)) {
    throw "Updater build output not found: $updaterExe"
}

# --- 4. Collect artifacts -----------------------------------------------------
Write-Host "[4/7] Collecting artifacts..."
if (Test-Path $artifactsDir) {
    Remove-Item $artifactsDir -Recurse -Force
}

$bundleContents = Join-Path $publishDir "Contents\2021"
New-Item -ItemType Directory -Path $bundleContents -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $publishDir "Updater") -Force | Out-Null

# The plug-in assembly: one file, the Core code is compiled into it.
Copy-Item $pluginDll (Join-Path $bundleContents "NavisElevation.dll") -Force

# Button icons. They are also packed into the assembly, so these files are a backup.
Copy-Item (Join-Path $repoRoot "src\NavisElevation.Navis\Resources\*.png") $bundleContents -Force

# The ribbon layout and the localization file live next to the assembly, in a
# language sub-folder: Navisworks looks for them as files, not as resources.
foreach ($lang in @("ru-RU", "en-US")) {
    $langDir = Join-Path $bundleContents $lang
    New-Item -ItemType Directory -Path $langDir -Force | Out-Null
    Copy-Item (Join-Path $repoRoot "src\NavisElevation.Navis\Ribbon\RibbonDefinition.xaml") (Join-Path $langDir "RibbonDefinition.xaml") -Force
    Copy-Item (Join-Path $repoRoot "deploy\NavisElevation.name") (Join-Path $langDir "NavisElevation.name") -Force
}

# Bundle description. Without it Navisworks ignores the plug-in, silently.
$packageSource = Join-Path $repoRoot "deploy\PackageContents.xml"
$packageTarget = Join-Path $publishDir "PackageContents.xml"
Copy-Item $packageSource $packageTarget -Force

# Put the real version into the bundle description.
$packageText = [System.IO.File]::ReadAllText($packageTarget)
$packageText = [regex]::Replace($packageText, 'AppVersion="[^"]*"', "AppVersion=`"$version.0`"")
[System.IO.File]::WriteAllText($packageTarget, $packageText, (New-Object System.Text.UTF8Encoding($false)))

# The updater replaces the plug-in files once Navisworks has been closed.
Copy-Item $updaterExe (Join-Path $publishDir "Updater\NavisElevation.Updater.exe") -Force

Write-Host "      Publish directory: $publishDir"

# --- 5. Payload and installer -------------------------------------------------
Write-Host "[5/7] Packing the update payload and building the installer..."

# The payload repeats the bundle as it is: the updater copies it over the installed
# bundle, so the folder structure has to match.
$payloadDir = Join-Path $artifactsDir "payload"
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "Contents") $payloadDir -Recurse -Force
Copy-Item $packageTarget (Join-Path $payloadDir "PackageContents.xml") -Force

$payloadZip = Join-Path $artifactsDir "NavisElevation-2021.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Архив собирается вручную, а не через Compress-Archive или ZipFile.CreateFromDirectory:
# и то и другое в .NET Framework пишет в записи архива обратные слэши, а это не по
# правилам формата ZIP. Наша распаковка такой архив читает, но другие инструменты
# могут запутаться, поэтому записи делаем с прямыми слэшами, как положено.
$archive = [System.IO.Compression.ZipFile]::Open($payloadZip, 'Create')
try {
    Get-ChildItem $payloadDir -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($payloadDir.Length).TrimStart('\') -replace '\\', '/'
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
}
Write-Host "      Packed NavisElevation-2021.zip"

# The installer for people: it puts the plug-in into the bundle and the updater
# next to it, without administrator rights.
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot "installer\NavisElevation-Setup.iss") | Out-Null
Assert-LastExitCode "Installer compilation failed."

$setupExe = Join-Path $artifactsDir "NavisElevation-$version-setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Installer not found after compilation: $setupExe"
}
Write-Host "      Packed NavisElevation-$version-setup.exe"

# --- 6. Release notes ---------------------------------------------------------
Write-Host "[6/7] Preparing release notes..."
$notesPath = Join-Path $artifactsDir "release-notes.md"
if ($NotesFile -and (Test-Path $NotesFile)) {
    Copy-Item $NotesFile $notesPath -Force
} elseif (Test-Path (Join-Path $PSScriptRoot "release-notes.md")) {
    Copy-Item (Join-Path $PSScriptRoot "release-notes.md") $notesPath -Force
} else {
    # Через cmd: в PowerShell ошибка git в stderr при $ErrorActionPreference = "Stop"
    # обрывает скрипт, а тегов в репозитории может не быть вовсе.
    $lastTag = cmd /c "git describe --tags --abbrev=0 2>nul"
    if ($LASTEXITCODE -ne 0 -or -not $lastTag) {
        $logRange = @("HEAD")
    } else {
        $logRange = @("$lastTag..HEAD")
    }
    $commits = git log --pretty=format:"- %s" @logRange
    $notes = "# Navis Elevation $version`n`n" + ($commits -join "`n") + "`n`n" +
             "Установка: запустите `NavisElevation-$version-setup.exe` и перезапустите Navisworks.`n"
    [System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($true))
}
Write-Host "      Notes: $notesPath"

# --- 7. Publish ----------------------------------------------------------------
Write-Host "[7/7] Publishing..."
$assets = @($payloadZip, $setupExe)

if ($DryRun) {
    Write-Host "      DRY RUN: skipping 'gh release create'. Files that would be uploaded:"
    foreach ($asset in $assets) {
        Write-Host "        $asset"
    }
    Write-Host "      Notes file: $notesPath"
    Write-Host "=========================================="
    Write-Host " DRY RUN completed successfully"
    Write-Host "=========================================="
    exit 0
}

gh release create $tag $assets --title "Navis Elevation $version" --notes-file $notesPath
Assert-LastExitCode "gh release create failed."

Write-Host ""
Write-Host "=========================================="
Write-Host " Release $tag published successfully!"
Write-Host "=========================================="
