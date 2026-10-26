; Aether Control - Inno Setup script.
; Built by .github/workflows/release.yml from the MSBuild Release output (which contains
; AetherControl.pri - see the note in release.yml). Local build:
;   ISCC.exe /DMyAppVersion=1.0.0 /DSourceDir=<repo>\src\AetherControl.App\bin\x64\Release\net10.0-windows10.0.19041.0 builder\AetherControl.iss

#ifndef MyAppVersion
  #error Pass /DMyAppVersion=x.y.z (must match the csproj <Version>)
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<MSBuild Release output folder>
#endif

#define MyAppName "Aether Control"
#define MyAppPublisher "theantipopau"
#define MyAppURL "https://github.com/theantipopau/aether-control"
#define MyAppExeName "AetherControl.exe"
; Must match AutostartService.TaskName.
#define AutostartTask "AetherControl"

[Setup]
; Never change AppId: upgrades find the existing install by it.
AppId={{8E4A1F72-3B9C-4D2E-A11C-7C6F0E9D5B30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=AetherControl-{#MyAppVersion}-x64-Setup
SetupIconFile=..\src\AetherControl.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The app itself requires elevation (hardware driver, fan control), so install machine-wide.
PrivilegesRequired=admin
; Restart Manager closes a running copy before files are replaced on upgrade.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; runascurrentuser: postinstall entries otherwise run as the original non-elevated user, and the
; app's requireAdministrator manifest then fails CreateProcess with code 740 (seen on 0.9.8).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; `--exit` runs the same ordered shutdown as tray -> Exit, which hands every fan back to BIOS
; control. Plain taskkill would only re-hide a tray-minimised window; /F would skip the fan reset.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopApp"
; "Start with Windows" is a Task Scheduler entry (AutostartService), not a Run key - remove it,
; or it would keep trying to launch a deleted exe at every sign-in.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#AutostartTask}"" /F"; Flags: runhidden; RunOnceId: "RemoveAutostart"

; Settings and history in %APPDATA%\Aether Control are kept on uninstall, so a reinstall keeps them.

[Code]
// Upgrade: close the running copy cleanly (fans back to BIOS) before files are replaced.
// CloseApplications above is only the fallback for builds older than 1.0 without --exit.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
