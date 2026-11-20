; Netie Space installer (Inno Setup 6). Per-user: no admin, no HKLM, no UserChoice.
; Build: scripts\build-installer.ps1 (passes /DMyAppVersion from the csproj and /DDistDir).
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef DistDir
  #define DistDir "..\dist\NetieSpace"
#endif
#ifndef OutDir
  #define OutDir "..\dist\installer"
#endif

#define MyAppName "Netie Space"
#define MyAppExe "NetieSpace.exe"

[Setup]
; Stable id. Never change: it is how upgrades and uninstall find this install.
AppId={{6F2B8C1E-4D7A-4E53-9B0A-5C3D1E7A9F42}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Netie
DefaultDirName={localappdata}\NetieSpace
DefaultGroupName=Netie
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutDir}
OutputBaseFilename=NetieSpace-Setup
SetupIconFile=..\src\NetieSpace\Assets\netie.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Netie Space"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\Assets\netie.ico"
Name: "{userprograms}\Netie PDF"; Filename: "{app}\{#MyAppExe}"; Parameters: "--app pdf"; AppUserModelID: "Netie.Pdf"; IconFilename: "{app}\Assets\apps\netie-pdf.ico"
Name: "{userprograms}\Netie Image"; Filename: "{app}\{#MyAppExe}"; Parameters: "--app image"; AppUserModelID: "Netie.Image"; IconFilename: "{app}\Assets\apps\netie-image.ico"
Name: "{userprograms}\Netie Video"; Filename: "{app}\{#MyAppExe}"; Parameters: "--app video"; AppUserModelID: "Netie.Video"; IconFilename: "{app}\Assets\apps\netie-video.ico"

[Run]
Filename: "{app}\{#MyAppExe}"; Parameters: "--register-apps"; Flags: runhidden waituntilterminated; StatusMsg: "Registering Netie apps..."
Filename: "{app}\{#MyAppExe}"; Description: "Start Netie Space"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExe}"; Parameters: "--unregister-apps"; Flags: runhidden waituntilterminated; RunOnceId: "NetieUnregisterApps"

[UninstallDelete]
; App writes caches/logs next to the exe; leave nothing behind.
Type: filesandordirs; Name: "{app}"
