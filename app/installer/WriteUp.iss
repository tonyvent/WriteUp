; ============================================================================
; WriteUp installer (Inno Setup 6 — free, https://jrsoftware.org/isinfo.php)
;
; Don't compile this directly — run app\make-installer.cmd, which publishes
; the single-file exe first and passes the version in from the .csproj.
; Output: app\installer\output\WriteUp-Setup-<version>.exe
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Fixed AppId so newer installers upgrade in place instead of installing twice.
AppId={{7E9C2B54-58C1-4A31-9B7D-2C4E1F0A6D11}
AppName=WriteUp
AppVersion={#AppVersion}
AppPublisher=Dynamic Engineering
DefaultDirName={autopf}\WriteUp
DefaultGroupName=WriteUp
DisableProgramGroupPage=yes
; Per-user install by default (no admin/UAC needed — good for locked-down
; workstations). Holding a dialog open lets admins choose all-users instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=output
OutputBaseFilename=WriteUp-Setup-{#AppVersion}
SetupIconFile=..\WriteUp\Assets\logo.ico
UninstallDisplayIcon={app}\WriteUp.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\WriteUp\publish\WriteUp.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\WriteUp"; Filename: "{app}\WriteUp.exe"
Name: "{autodesktop}\WriteUp"; Filename: "{app}\WriteUp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WriteUp.exe"; Description: "Launch WriteUp now"; Flags: nowait postinstall skipifsilent

; Note: user data (settings under %AppData%\WriteUp and any kept session
; folders) is intentionally NOT removed on uninstall.
