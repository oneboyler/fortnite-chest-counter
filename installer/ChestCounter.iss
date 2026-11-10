; Installer for Chest Counter: release\ChestCounter-Setup.exe
; release.bat builds it when Inno Setup 6 (https://jrsoftware.org/isinfo.php) is installed.
;
; It installs for the current user only, into %LOCALAPPDATA%\Programs\Chest Counter:
;   - no admin prompt
;   - the app can keep its data folder next to the exe, and its own updater can swap the program files
;     (neither works under Program Files)
; The files are the same ones that go into ChestCounter-win-x64.zip.

#define AppName "Chest Counter"
#define AppExe "Chest Counter.exe"
#ifndef Src
  #define Src SourcePath + "..\..\release\Chest Counter"
#endif
#ifndef Out
  #define Out SourcePath + "..\..\release"
#endif
; the version comes from the exe itself (ChestCounter.csproj <Version>), e.g. 1.5.0.0 -> 1.5.0
#define FullVer GetVersionNumbersString(AddBackslash(Src) + AppExe)
#define AppVersion Copy(FullVer, 1, RPos(".", FullVer) - 1)

[Setup]
; Never change AppId: Windows uses it to recognise an existing install. Installed.cs has the same id.
AppId={{8F2B6C1E-4D7A-4B93-9E5F-2C1A7D3E6B40}
AppName={#AppName}
AppVersion={#AppVersion}
; no version in the name: the in-app updater changes the version later (Installed.cs keeps DisplayVersion right)
AppVerName={#AppName}
AppPublisher=oneboyler
AppPublisherURL=https://github.com/oneboyler/fortnite-chest-counter
AppSupportURL=https://github.com/oneboyler/fortnite-chest-counter/issues
AppUpdatesURL=https://github.com/oneboyler/fortnite-chest-counter/releases/latest
VersionInfoVersion={#FullVer}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#Out}
OutputBaseFilename=ChestCounter-Setup
SetupIconFile={#SourcePath}..\ChestCounter\chest.ico
; the two exes are already compressed inside, so a heavier setting only costs build time
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
ArchitecturesAllowed=x64compatible
; the running copy is closed by [Code] below (it's a tray app with no window for Windows to close)
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Src}\*"; DestDir: "{app}"; Excludes: "\data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; files a later in-app update may have added to the program's own subfolder
Type: filesandordirs; Name: "{app}\app"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'Chest Counter';

{ Asks the copy running from the install folder to close, and waits for it.
  Only versions from 1.5 on understand --exit; an older exe would just start, so it's left alone. }
procedure CloseRunningCopy();
var
  Exe: String;
  MS, LS: Cardinal;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Exe) then exit;
  if not GetVersionNumbers(Exe, MS, LS) then exit;
  if ((MS shr 16) < 1) or (((MS shr 16) = 1) and ((MS and $FFFF) < 5)) then exit;
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningCopy();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningCopy();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cmd, DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { "Start with Windows" (set from the app's tray menu): remove it, but only if it points at this copy }
    if RegQueryStringValue(HKCU, RunKey, RunValue, Cmd) then
      if Pos(Lowercase(ExpandConstant('{app}\')), Lowercase(Cmd)) > 0 then
        RegDeleteValue(HKCU, RunKey, RunValue);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { The counted games are the one thing that can't be rebuilt once Fortnite deletes old replays: ask. }
    DataDir := ExpandConstant('{app}\data');
    if DirExists(DataDir) and not UninstallSilent() then
      if MsgBox('Also delete your counted games and settings?' + #13#10#13#10 +
                'Choose No to keep them in:' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
        RemoveDir(ExpandConstant('{app}'));
      end;
  end;
end;
