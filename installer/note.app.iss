
#ifndef AppVersion
  #error Pass the version, e.g. ISCC /DAppVersion=0.8.0 installer\note.app.iss (build.ps1 does this)
#endif
#define AppExe "note.app.exe"
#define Build AddBackslash(SourcePath) + "..\dist\v" + AppVersion + "-portable"

[Setup]
AppId={{EC4700BE-39F4-441A-84E2-E408D1F309F0}
AppName=note.
AppVersion={#AppVersion}
AppVerName=note. {#AppVersion}
AppPublisher=flac.app Studios
DefaultDirName={autopf}\note.app
DefaultGroupName=note.
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\NoteappFinal.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=note.
OutputDir=..\dist
OutputBaseFilename=note.app-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Build}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Build}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\note."; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\note."; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "note.app"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,note.}"; Flags: nowait postinstall skipifsilent
