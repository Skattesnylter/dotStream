; dotStream installer - Inno Setup 6
;
; Built by .github/workflows/release.yml, which passes the version and paths:
;
;   ISCC /DAppVersion=0.2.0 /DSourceDir=..\publish\folder /DOutputDir=..\artifacts installer\dotstream.iss
;
; Per-user install by default (no elevation): dotStream writes only to %APPDATA%
; and needs no driver, so there is nothing to justify a UAC prompt.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\folder"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName       "dotStream"
#define AppPublisher  "Thomas Blix"
#define AppExeName    "dotStream.exe"
#define AppUrl        "https://github.com/Skattesnylter/dotStream"

[Setup]
AppId={{8F3C1E52-6A4D-4B7A-9C21-4E0B6D5F72A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; Per-user, so no UAC prompt. Users who want a machine-wide install can run the
; setup elevated and it will offer that instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\DotStream.App\Resources\dotstream.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
InfoAfterFile=..\NOTICE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "norwegian"; MessagesFile: "compiler:Languages\Norwegian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "Start dotStream when I sign in"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Deliberately does NOT remove %APPDATA%\dotStream - a reinstall should find the
; user's deck layout where they left it. Removing settings on uninstall is a
; decision for the user, not the installer.
Type: dirifempty; Name: "{app}"
