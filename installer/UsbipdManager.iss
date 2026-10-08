; Inno Setup 6 script for USBIPD Manager. Normally compiled by build.ps1 --install:
;   ISCC /DAppVersion=1.2.3 /DSourceExe=<published UsbipdManager.exe> [/DAppIcon=<app.ico>]
;        [/DSignToolName=<name> "/S<name>=<sign command using $f>"] /O<output dir> UsbipdManager.iss
; The command-line arguments used below (--exit, --register-autostart, --unregister-autostart) are the
; contract from docs/PLAN.md 6.1; keep them in sync with the app.

#ifndef AppVersion
  #error AppVersion is not defined. Pass /DAppVersion=MAJOR.MINOR.PATCH to ISCC (build.ps1 --install does this).
#endif
#ifndef SourceExe
  #error SourceExe is not defined. Pass /DSourceExe=<path to the published UsbipdManager.exe> to ISCC.
#endif
#if !FileExists(SourceExe)
  #pragma error "SourceExe file not found: " + SourceExe
#endif
#ifdef AppIcon
  #if !FileExists(AppIcon)
    #pragma error "AppIcon file not found: " + AppIcon
  #endif
#endif

#define AppName "USBIPD Manager"
#define AppExeName "UsbipdManager.exe"
#define AppDataFolder "UsbipdManager"

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{18E8A57D-BA06-4CD6-AE1A-2E50CBFF7730}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Minh Viet
AppCopyright=© 2026 Minh Viet
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=UsbipdManager-Setup-{#AppVersion}
#ifdef AppIcon
SetupIconFile={#AppIcon}
#endif
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; The user-area shortcut cleanup in [UninstallDelete] is intentional.
UsedUserAreasWarning=no
#ifdef SignToolName
; build.ps1 defines the sign tool with /S<name>=<command>; it signs this setup exe and the uninstaller.
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start with Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--register-autostart"; StatusMsg: "Registering autostart..."; Flags: runhidden waituntilterminated; Tasks: autostart
; The app requires elevation; runascurrentuser reuses Setup's elevated token instead of failing as the original user.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitApp"
Filename: "{app}\{#AppExeName}"; Parameters: "--unregister-autostart"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterAutostart"

[UninstallDelete]
; Shortcuts the app itself may have created from Settings (assumed to use the same name as the installer's).
Type: files; Name: "{userdesktop}\{#AppName}.lnk"
Type: files; Name: "{commondesktop}\{#AppName}.lnk"
Type: files; Name: "{userprograms}\{#AppName}.lnk"
Type: files; Name: "{commonprograms}\{#AppName}.lnk"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExePath: String;
  ResultCode: Integer;
begin
  Result := '';
  // Ask a running instance to return its devices to Windows and quit before its exe is replaced.
  ExePath := ExpandConstant('{app}\{#AppExeName}');
  if FileExists(ExePath) then
  begin
    if Exec(ExePath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log(Format('%s --exit returned %d', [ExePath, ResultCode]))
    else
      Log(Format('%s --exit could not be started: %s', [ExePath, SysErrorMessage(ResultCode)]));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    if MsgBox('Also remove settings and logs?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\{#AppDataFolder}'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\{#AppDataFolder}'), True, True, True);
    end;
  end;
end;
