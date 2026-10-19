; Builds BetterTalk-Setup.exe from the folder made by:
;   dotnet publish src/Aac.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
; Run:  iscc /DAppVersion=1.0.0 installer\BetterTalk.iss      (output: installer\Output\BetterTalk-Setup.exe)
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B0C5B7A-7C1E-4C1B-9E55-2B6A2F4D9A31}
AppName=BetterTalk
AppVersion={#AppVersion}
AppPublisher=BetterTalk
DefaultDirName={localappdata}\Programs\BetterTalk
DefaultGroupName=BetterTalk
; Installs for the current user only, so no administrator password is needed.
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
OutputDir=Output
OutputBaseFilename=BetterTalk-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\BetterTalk.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Updating over a running copy: close it first, and start it again afterwards.
CloseApplications=force
RestartApplications=yes
LicenseFile=..\LICENSE

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BetterTalk"; Filename: "{app}\BetterTalk.exe"
Name: "{autodesktop}\BetterTalk"; Filename: "{app}\BetterTalk.exe"

[Run]
Filename: "{app}\BetterTalk.exe"; Description: "Start BetterTalk now"; Flags: nowait postinstall skipifsilent

; Settings, buttons and downloaded voices live in %APPDATA%\BetterTalk and %LOCALAPPDATA%\BetterTalk and are
; deliberately left alone when the app is uninstalled.
