#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

[Setup]
AppId={{8D2F6C1B-4A7E-4C55-9B1D-DA2C51DE0001}
AppName=DARKSIDE
AppVersion={#AppVersion}
AppPublisher=DARKSIDE
DefaultDirName={localappdata}\Programs\DARKSIDE
DefaultGroupName=DARKSIDE
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=DARKSIDE-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DarkSideLauncher.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DARKSIDE"; Filename: "{app}\DarkSideLauncher.exe"
Name: "{autodesktop}\DARKSIDE"; Filename: "{app}\DarkSideLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DarkSideLauncher.exe"; Description: "Launch DARKSIDE"; Flags: nowait postinstall
