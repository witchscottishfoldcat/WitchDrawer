; WitchDrawer Inno Setup script
; Build: ISCC.exe WitchDrawer.iss
; Produces a Windows installer (-Setup.exe) alongside the portable zip.
;
; Preprocessor vars:
;   MyAppVersion   — passed via /DMyAppVersion=1.3.2 (matches Directory.Build.props)
;   PublishDir     — the dotnet publish output folder containing WitchDrawer.App.exe
;
; Package the complete publish directory. New installations are per-user so
; drag/drop and updates both run with ordinary user permissions. The updater
; elevates only its file replacement step for existing protected installations.

#ifndef MyAppVersion
  #define MyAppVersion "1.3.11"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish\v1.3.11"
#endif

#define MyAppName "WitchDrawer"
#define MyAppPublisher "witchscottishfoldcat"
#define MyAppURL "https://github.com/witchscottishfoldcat/WitchDrawer"
#define MyAppExeName "WitchDrawer.App.exe"

[Setup]
AppId={{8F3C2A1E-7B4D-4E6F-9A2C-WITCHDRAWER111}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases/latest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=WitchDrawer-Setup-v{#MyAppVersion}-x64
SetupIconFile=..\src\WitchDrawer.App\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Keep user data (SQLite db in LocalAppData) on uninstall — the app stores
; boxes/items there, so we never touch it from the installer.

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "开机自启动"; GroupDescription: "其他选项:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autostartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--silent"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure the app is closed before uninstalling (it hides to tray and holds
; file locks on the exe).
Filename: "{cmd}"; Parameters: "/c taskkill /IM ""{#MyAppExeName}"" /F 2>nul"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; Remove installed files but leave user data (in LocalAppData) intact.
Type: filesandordirs; Name: "{app}"
