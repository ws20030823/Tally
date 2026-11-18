; Build with: powershell -ExecutionPolicy Bypass -File .\Installer\Build-Installer.ps1
#ifndef AppVersion
  #define AppVersion "1.0.7"
#endif
#ifndef PublishDir
  #define PublishDir "..\Artifacts\publish\win-x64"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\Artifacts\installer"
#endif

[Setup]
AppId={{A5DD9D8B-CC80-4F25-AE91-761A0BA931D9}
AppName=Tally
AppVersion={#AppVersion}
AppVerName=Tally {#AppVersion}
VersionInfoDescription=Tally Installer
VersionInfoProductName=Tally
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Tally
DefaultGroupName=Tally
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
WizardStyle=modern
SetupIconFile=..\Assets\Icons\Tally.ico
UninstallDisplayIcon={app}\TaskDesk.exe
OutputDir={#InstallerOutputDir}
OutputBaseFilename=Tally-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=TaskDesk.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Tally"; Filename: "{app}\TaskDesk.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Tally"; Filename: "{app}\TaskDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TaskDesk.exe"; Description: "{cm:LaunchProgram,Tally}"; Flags: nowait postinstall skipifsilent

; The installer owns only its application files and shortcuts.
; Never delete Documents\拾序 or the legacy LocalAppData\TaskDesk data directory.
