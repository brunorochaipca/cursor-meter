; Per-user installer. The program lives in LocalAppData\Programs so it does not
; share a folder with %LOCALAPPDATA%\CursorMeter\offset.txt.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "1.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\CursorMeter.App\bin\Release\net8.0-windows\win-x64\publish"
#endif

#define MyAppName "CursorMeter"
#define MyAppPublisher "brunorochaipca"
#define MyAppURL "https://github.com/brunorochaipca/cursor-meter"

[Setup]
AppId={{B7E4C1A2-9D35-4F68-8A10-6C2E5D9B4F71}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=CursorMeter-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\CursorMeter
UninstallDisplayIcon={app}\{#MyAppName}.exe
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#VersionInfoVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Duas barras na barra de tarefas: Cursor Models e Other Models.

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o CursorMeter quando o Windows iniciar"; GroupDescription: "Opções adicionais:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\CursorMeter.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CursorMeter"; Filename: "{app}\CursorMeter.exe"; Comment: "Cursor Models | Other Models"
Name: "{userstartup}\CursorMeter"; Filename: "{app}\CursorMeter.exe"; Comment: "Cursor Models | Other Models"; Tasks: startup

[Run]
Filename: "{app}\CursorMeter.exe"; Description: "Iniciar o CursorMeter"; Flags: nowait postinstall skipifsilent
