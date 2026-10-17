#define AppVersion "0.3.0-alpha"
#define RepoRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{7750B688-9311-4D2D-846D-4080B0CA769D}
AppName=DIPlayer
AppVersion={#AppVersion}
AppPublisher=DIPlayer contributors
AppPublisherURL=https://github.com/GeekyKrank/DIPlayer
AppSupportURL=https://github.com/GeekyKrank/DIPlayer/issues
DefaultDirName={localappdata}\Programs\DIPlayer
DefaultGroupName=DIPlayer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#RepoRoot}\dist
OutputBaseFilename=DIPlayer-Setup-{#AppVersion}-x64
SetupIconFile={#RepoRoot}\src\DIPlayer\Assets\diplayer.ico
UninstallDisplayIcon={app}\DIPlayer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
CloseApplicationsFilter=DIPlayer.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#RepoRoot}\dist\DIPlayer-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DIPlayer"; Filename: "{app}\DIPlayer.exe"; WorkingDir: "{app}"; IconFilename: "{app}\diplayer.ico"
Name: "{autodesktop}\DIPlayer"; Filename: "{app}\DIPlayer.exe"; WorkingDir: "{app}"; IconFilename: "{app}\diplayer.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\DIPlayer.exe"; Description: "Launch DIPlayer"; Flags: nowait postinstall skipifsilent

; Playlists, covers and settings in LocalAppData\DIPlayer are deliberately
; outside the installation directory and preserved during uninstall/update.
