#define AppName "Navis Elevation"
#define AppURL "https://github.com/temapromaster3000/new-navis"
#define RepoOwner "temapromaster3000"
#define RepoName "new-navis"

#ifndef AppVersion
  #define AppVersion Trim(FileRead(FileOpen("..\..\Version.txt"), 0))
#endif

[Setup]
AppId={{7C3D9E42-1B5A-4F38-9E60-2D8C4A7B1F35}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#RepoOwner}
AppPublisherURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\NavisElevation.bundle
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=..\..\artifacts
OutputBaseFilename=NavisElevation-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.PluginBusy=Navisworks сейчас запущен, и файлы плагина заняты: пока программа открыта, Windows не даёт их перезаписать.%n%nУстановка всё равно продолжится, но новая версия плагина заработает только после следующего запуска Navisworks.
english.PluginBusy=Navisworks is currently running and the plug-in files are locked: while the program is open, Windows does not allow overwriting them.%n%nThe installation will continue anyway, but the new version of the plug-in will only work after the next start of Navisworks.
russian.InstalledOk=Navis Elevation {#AppVersion} установлен.%n%nВкладка «Navis Elevation» появится на ленте при следующем запуске Navisworks.%n%nNavisworks читает плагины только при старте, поэтому его нужно перезапустить.
english.InstalledOk=Navis Elevation {#AppVersion} installed.%n%nThe «Navis Elevation» tab will appear on the ribbon at the next start of Navisworks.%n%nNavisworks reads plug-ins only at start, so it has to be restarted.

[Files]
; --- Сборка плагина, иконки кнопок и разметка ленты (папки ru-RU и en-US) ---
Source: "..\..\artifacts\publish\Contents\2021\*"; DestDir: "{app}\Contents\2021"; Flags: ignoreversion recursesubdirs
; --- Описание бандла: без него Navisworks не подхватывает плагин, молча ---
Source: "..\..\artifacts\publish\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
; --- Программа-обновлятель: подменяет файлы после закрытия Navisworks ---
Source: "..\..\artifacts\publish\Updater\NavisElevation.Updater.exe"; DestDir: "{userappdata}\NavisElevation"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\NavisElevation\staging"
Type: files; Name: "{userappdata}\NavisElevation\update-pending.txt"
Type: files; Name: "{userappdata}\NavisElevation\updater-log.txt"
Type: files; Name: "{userappdata}\NavisElevation\NavisElevation.Updater.exe"
Type: files; Name: "{userappdata}\NavisElevation\NavisElevation.Updater.pdb"

[Code]
var
  PluginBusy: Boolean;

{ Проверяем, не заняты ли файлы плагина: пока Navisworks открыт, он держит сборку
  открытой, и Windows не даёт её перезаписать. Предупреждаем, но установку не
  останавливаем: файлы всё равно заменятся, просто не сразу. }
function IsPluginBusy: Boolean;
var
  PluginPath: String;
  File: TFileStream;
begin
  Result := False;
  PluginPath := ExpandConstant('{app}') + '\Contents\2021\NavisElevation.dll';

  if not FileExists(PluginPath) then
    Exit;

  try
    File := TFileStream.Create(PluginPath, fmOpenReadWrite or fmShareExclusive);
    File.Free;
  except
    Result := True;
  end;
end;

function InitializeSetup: Boolean;
begin
  PluginBusy := IsPluginBusy;
  if PluginBusy then
    MsgBox(CustomMessage('PluginBusy'), mbInformation, MB_OK);
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MsgBox(CustomMessage('InstalledOk'), mbInformation, MB_OK);
end;
