#define MyAppName "RoamADB Gateway"
#define MyAppVersion "0.1.2-spike"
#define MyAppPublisher "fullmetalsonic"
#define MyAppExeName "RoamADBGateway.exe"

[Setup]
AppId={{CA6495DC-6ECD-4F4E-BB55-6A64231790A5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/fullmetalsonic/roam-adb
AppSupportURL=https://github.com/fullmetalsonic/roam-adb/issues
AppUpdatesURL=https://github.com/fullmetalsonic/roam-adb/releases
DefaultDirName={localappdata}\Programs\RoamADB Gateway
DefaultGroupName=RoamADB Gateway
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts\installer
OutputBaseFilename=RoamADB-Gateway-Setup-0.1.2-spike
SetupIconFile=..\assets\roamadb-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
AppMutex=Local\RoamADB.Gateway.Desktop
VersionInfoVersion=0.1.2.0
VersionInfoDescription=RoamADB Gateway installer
VersionInfoProductName=RoamADB Gateway
VersionInfoProductVersion=0.1.2.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: unchecked

[Files]
Source: "..\artifacts\gateway\win-x64\RoamADBGateway.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD_PARTY_LICENSES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\RoamADB Gateway"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\RoamADB GitHub"; Filename: "https://github.com/fullmetalsonic/roam-adb"
Name: "{autodesktop}\RoamADB Gateway"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "RoamADB Gateway 실행"; Flags: nowait postinstall skipifsilent
