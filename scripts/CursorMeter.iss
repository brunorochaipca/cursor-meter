; Per-user installer. The program lives in LocalAppData\Programs so it does not
; share a folder with %LOCALAPPDATA%\CursorMeter\offset.txt.

; This branch is the Mainland edition: beta for Start11 v1/v2 with the experimental administrator
; mode. The stable Island edition lives on main and shares the AppId, so one replaces the other.
#ifndef Edition
  #define Edition "Mainland"
#endif
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
AppName={#MyAppName} {#Edition}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#Edition} {#AppVersion}
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
OutputBaseFilename=CursorMeter-{#Edition}-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\CursorMeter
SetupIconFile=..\assets\CursorMeter.ico
UninstallDisplayIcon={app}\{#MyAppName}.exe
UninstallDisplayName={#MyAppName} {#Edition}
VersionInfoVersion={#VersionInfoVersion}
VersionInfoProductName={#MyAppName} {#Edition}
VersionInfoDescription=Duas barras na barra de tarefas: Cursor Models e Other Models.

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o CursorMeter quando o Windows iniciar"; GroupDescription: "Opções adicionais:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\CursorMeter.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CursorMeter"; Filename: "{app}\CursorMeter.exe"; Comment: "Cursor Models | Other Models"
Name: "{userstartup}\CursorMeter"; Filename: "{app}\CursorMeter.exe"; Comment: "Cursor Models | Other Models"; Tasks: startup; Check: not ExperimentalAdmin

[UninstallDelete]
Type: files; Name: "{app}\experimental-admin"

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""CursorMeter"" /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "DelCursorMeterTask"

[Run]
Filename: "{app}\CursorMeter.exe"; Description: "Iniciar o CursorMeter"; Flags: nowait postinstall skipifsilent

[Code]
var
  Start11Detected: Boolean;
  Start11Version: String;
  ExperimentalAdminChosen: Boolean;

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

// True when the user chose experimental administrator mode (Start11 before v3 kept in place).
function ExperimentalAdmin: Boolean;
begin
  Result := ExperimentalAdminChosen;
end;

procedure InitializeWizard;
begin
  Start11Detected := ReadStart11Version(Start11Version);
  ExperimentalAdminChosen := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Choice: Integer;
begin
  Result := True;
  if (CurPageID <> wpSelectTasks) or not Start11Detected or (MajorVersion(Start11Version) >= 3) then
    Exit;

  Choice := MsgBox(
    'Foi detetado o Start11 ' + Start11Version + '.' + #13#10#13#10 +
    'Com o Start11 anterior ao v3, abrir e fechar o menu Iniciar ou a Vista de Tarefas com o rato deixa a barra de tarefas por cima das barras do CursorMeter. ' +
    'Recomendamos instalar com permissão de administrador (modo experimental), que mantém as barras sempre visíveis. ' +
    'O CursorMeter passa a pedir essa permissão ao iniciar.' + #13#10#13#10 +
    'Sim: instalar com permissão de administrador (recomendado).' + #13#10 +
    'Não: instalar sem esse modo (as barras podem ficar tapadas até ao próximo clique).' + #13#10 +
    'Cancelar: parar a instalação.',
    mbConfirmation, MB_YESNOCANCEL);
  case Choice of
    IDYES: ExperimentalAdminChosen := True;
    IDNO: ExperimentalAdminChosen := False;
    IDCANCEL: Result := False;
  end;
end;

// Builds the Task Scheduler XML for a HighestAvailable logon task that starts CursorMeter.
function BuildTaskXml: String;
var
  ExePath, WorkDir, UserName: String;
begin
  ExePath := ExpandConstant('{app}\CursorMeter.exe');
  WorkDir := ExpandConstant('{app}');
  UserName := ExpandConstant('{username}');
  Result :=
    '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>' + UserName + '</UserId></LogonTrigger></Triggers>' + #13#10 +
    '  <Principals><Principal id="Author"><UserId>' + UserName + '</UserId>' +
    '<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>' + #13#10 +
    '  <Settings>' +
    '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' +
    '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
    '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' +
    '<AllowHardTerminate>true</AllowHardTerminate>' +
    '<StartWhenAvailable>false</StartWhenAvailable>' +
    '<AllowStartOnDemand>true</AllowStartOnDemand>' +
    '<Enabled>true</Enabled><Hidden>false</Hidden>' +
    '<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority>' +
    '</Settings>' + #13#10 +
    '  <Actions Context="Author"><Exec><Command>' + ExePath + '</Command>' +
    '<WorkingDirectory>' + WorkDir + '</WorkingDirectory></Exec></Actions>' + #13#10 +
    '</Task>';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  XmlPath, MarkerPath: String;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // A previous experimental install (Mainland) left a marker and an elevated logon task.
    // Drop them unless this install opts in again, so the other edition never starts elevated.
    MarkerPath := ExpandConstant('{app}\experimental-admin');
    if FileExists(MarkerPath) then
    begin
      DeleteFile(MarkerPath);
      if not ExperimentalAdminChosen then
        ShellExec('runas', 'schtasks.exe', '/Delete /TN "CursorMeter" /F',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
    Exit;
  end;

  if CurStep <> ssPostInstall then
    Exit;
  if not ExperimentalAdminChosen then
    Exit;

  // Marker file read by UiAccess.IsConfigured in the app.
  SaveStringToFile(ExpandConstant('{app}\experimental-admin'), '', False);

  // Register the elevated logon task so UIAccess starts without a UAC prompt when "startup" is chosen.
  if WizardIsTaskSelected('startup') then
  begin
    XmlPath := ExpandConstant('{tmp}\cursormeter-task.xml');
    if SaveStringToFile(XmlPath, BuildTaskXml, False) then
    begin
      if not ShellExec('runas', 'schtasks.exe',
        '/Create /TN "CursorMeter" /XML "' + XmlPath + '" /F',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        MsgBox('Não foi possível registar o arranque automático com permissões de administrador. ' +
          'Pode ativá-lo depois no menu do CursorMeter.', mbInformation, MB_OK);
    end;
  end;
end;
