; Per-user installer. The program lives in LocalAppData\Programs so it does not
; share a folder with %LOCALAPPDATA%\CursorMeter\offset.txt.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "1.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\CursorMeter.App\bin\Release\net8.0\win-x64\publish"
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
SetupIconFile=..\assets\CursorMeter.ico
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

[Code]
// Reads the Start11 DisplayVersion from the 64-bit uninstall key, if present.
function ReadStart11Version(var Version: String): Boolean;
begin
  Result := RegQueryStringValue(HKLM64,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Stardock Start11',
    'DisplayVersion', Version);
  if not Result then
    Result := RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Stardock Start11',
      'DisplayVersion', Version);
end;

// Major version number parsed from a dotted string such as "2.0.2.0".
function MajorVersion(const Version: String): Integer;
var
  Dot: Integer;
  Head: String;
begin
  Dot := Pos('.', Version);
  if Dot > 0 then
    Head := Copy(Version, 1, Dot - 1)
  else
    Head := Version;
  Result := StrToIntDef(Head, 0);
end;

// The Island edition is made for Start11 v3: before it, the taskbar covers the bars after Start or
// Task View is closed with the mouse. Say so, and point to the Mainland edition.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Version: String;
  ErrorCode: Integer;
begin
  Result := True;
  if CurPageID <> wpSelectTasks then
    Exit;
  if not ReadStart11Version(Version) or (MajorVersion(Version) >= 3) then
    Exit;

  if MsgBox(
    'Foi detetado o Start11 ' + Version + '.' + #13#10#13#10 +
    'A edição Island não é totalmente compatível com o Start11 v1 e v2. ' +
    'Em vez desta edição, use a Mainland, pensada para estas versões.' + #13#10#13#10 +
    'Sim: abrir a página da edição Mainland e sair (recomendado).' + #13#10 +
    'Não: instalar a Island mesmo assim.',
    mbInformation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', '{#MyAppURL}/releases', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

// Installing over a Mainland install in experimental administrator mode: drop its marker and its
// elevated logon task, so the Island never starts elevated.
procedure CurStepChanged(CurStep: TSetupStep);
var
  MarkerPath: String;
  ResultCode: Integer;
begin
  if CurStep <> ssInstall then
    Exit;
  MarkerPath := ExpandConstant('{app}\experimental-admin');
  if FileExists(MarkerPath) then
  begin
    DeleteFile(MarkerPath);
    ShellExec('runas', 'schtasks.exe', '/Delete /TN "CursorMeter" /F',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;