; Windows installer for Git Graph, built by the Release workflow with Inno Setup:
;   iscc /DAppVersion=1.2.3 /DSourceDir=<win-x64 publish folder> /DOutputDir=<folder> packaging\windows\gitgraph.iss
; Installs for all users (admin) or just the current user, adds a Start menu entry, an optional desktop
; shortcut and an uninstaller.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{6C1D9A52-3F0B-4E5C-9A7E-8B2F4D1C7E90}
AppName=Git Graph
AppVersion={#AppVersion}
AppVerName=Git Graph {#AppVersion}
AppPublisher=benshi
AppPublisherURL=https://github.com/BenLocal/git-graph
AppSupportURL=https://github.com/BenLocal/git-graph/issues
DefaultDirName={autopf}\Git Graph
DefaultGroupName=Git Graph
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\..\src\GitGraph.App\Assets\Icons\gitgraph.ico
UninstallDisplayIcon={app}\GitGraph.exe
OutputDir={#OutputDir}
OutputBaseFilename=GitGraph-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Git Graph"; Filename: "{app}\GitGraph.exe"
Name: "{autodesktop}\Git Graph"; Filename: "{app}\GitGraph.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GitGraph.exe"; Description: "{cm:LaunchProgram,Git Graph}"; Flags: nowait postinstall skipifsilent
