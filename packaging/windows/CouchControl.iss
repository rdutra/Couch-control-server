; Inno Setup installer for CouchControl.
; Build after running scripts/publish-win-x64.ps1:
;   iscc packaging\windows\CouchControl.iss

#define AppName "CouchControl"
#define AppPublisher "CouchControl"
#define AppVersion GetStringFileInfo("..\..\artifacts\win-x64\CouchControl\agent\CouchControl.Agent.exe", "ProductVersion")
#define PackageRoot "..\..\artifacts\win-x64\CouchControl"

[Setup]
AppId={{E30A9BDF-02D5-4A09-AE35-126B44C03E8B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\CouchControl
DefaultGroupName=CouchControl
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\win-x64
OutputBaseFilename=CouchControlSetup-win-x64
SetupIconFile=..\..\src\CouchControl.Agent\Assets\couchcontrol.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\agent\CouchControl.Agent.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startatlogin"; Description: "Start CouchControl Agent when I sign in"; GroupDescription: "Startup options:"; Flags: unchecked

[Files]
Source: "{#PackageRoot}\agent\*"; DestDir: "{app}\agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\cli\*"; DestDir: "{app}\cli"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\README-INSTALL.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\VERSION"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\CouchControl Agent"; Filename: "{app}\agent\CouchControl.Agent.exe"; WorkingDir: "{app}\agent"
Name: "{group}\CouchControl CLI"; Filename: "{cmd}"; Parameters: "/k ""{app}\cli\CouchControl.Cli.exe"""; WorkingDir: "{app}\cli"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CouchControl.Agent"; ValueData: """{app}\agent\CouchControl.Agent.exe"""; Tasks: startatlogin

[Run]
Filename: "{app}\agent\CouchControl.Agent.exe"; Description: "Launch CouchControl Agent"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\agent"
Type: filesandordirs; Name: "{app}\cli"
Type: files; Name: "{app}\README-INSTALL.md"
Type: files; Name: "{app}\VERSION"
Type: files; Name: "{app}\uninstall.ps1"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v CouchControl.Agent /f"; Flags: runhidden
