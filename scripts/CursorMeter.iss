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
DisableReadyMemo=yes
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

[Run]
Filename: "{app}\CursorMeter.exe"; Description: "Iniciar o CursorMeter"; Flags: nowait postinstall skipifsilent

[Code]
const
  BCM_SETSHIELD = $0000160C;
  ERROR_CANCELLED = 1223;
  TOKEN_QUERY = 8;
  TokenElevationClass = 20;

  // -1 until the user picks a card. 0 = without admin, 1 = with admin.
  ChoiceNone = -1;
  ChoiceWithoutAdmin = 0;
  ChoiceWithAdmin = 1;
  // Same two slots when a Mainland install already exists: update it, or uninstall it first.
  ChoiceUpdate = 0;
  ChoiceUninstallFirst = 1;

  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7E4C1A2-9D35-4F68-8A10-6C2E5D9B4F71}_is1';

  // What the elevated step does with the CursorMeter scheduled task.
  TaskRemove = 0;
  TaskOnDemand = 1;
  TaskAtLogon = 2;

var
  Start11Detected: Boolean;
  Start11Version: String;
  ExperimentalAdminChosen: Boolean;
  PrivilegedStepDone: Boolean;
  // A Mainland install is already there. ExistingAdmin: it runs in administrator mode.
  ExistingMainland: Boolean;
  ExistingAdmin: Boolean;
  ExistingDir: String;
  AdminSelection: Integer;
  UpdatingChoice: Boolean;
  CardPanel: array[0..1] of TPanel;
  CardAccent: array[0..1] of TPanel;
  CardRadio: array[0..1] of TNewRadioButton;
  CardText: array[0..1] of TNewStaticText;
  AdminInfo: TNewStaticText;
  NextButtonLeft: Integer;
  NextButtonWidth: Integer;

function SendMessageInt(hWnd: Integer; Msg: Cardinal; wParam, lParam: Integer): Integer;
  external 'SendMessageW@user32.dll stdcall';
function GetCurrentProcess: Integer;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function OpenProcessToken(ProcessHandle: Integer; DesiredAccess: Cardinal; var TokenHandle: Integer): Boolean;
  external 'OpenProcessToken@advapi32.dll stdcall';
function GetTokenInformation(TokenHandle: Integer; TokenInformationClass: Integer;
  var TokenInformation: Cardinal; TokenInformationLength: Cardinal; var ReturnLength: Cardinal): Boolean;
  external 'GetTokenInformation@advapi32.dll stdcall';
function CloseHandle(Handle: Integer): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

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

// True when Start11 is installed and older than v3 (the case the admin mode is for).
function Start11BeforeV3: Boolean;
begin
  Result := Start11Detected and (MajorVersion(Start11Version) < 3);
end;

// True when the user chose experimental administrator mode.
function ExperimentalAdmin: Boolean;
begin
  Result := ExperimentalAdminChosen;
end;

// True when this process token is elevated. IsAdmin is not used: on Windows with UAC it can
// report membership of Administrators while the installer itself is still medium integrity.
function IsSetupElevated: Boolean;
var
  Token: Integer;
  Elevation, Returned: Cardinal;
begin
  Result := False;
  if not OpenProcessToken(GetCurrentProcess, TOKEN_QUERY, Token) then
    Exit;
  if GetTokenInformation(Token, TokenElevationClass, Elevation, 4, Returned) then
    Result := Elevation <> 0;
  CloseHandle(Token);
end;

function CursorMeterTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('schtasks.exe', '/Query /TN "CursorMeter"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
end;

// True when the Mainland edition is installed. Island shares the AppId but not the display name.
function MainlandInstalled: Boolean;
var
  DisplayName: String;
begin
  Result := RegQueryStringValue(HKCU, UninstallKey, 'DisplayName', DisplayName)
    and (Pos('Mainland', DisplayName) > 0);
end;

// Fills ExistingMainland, ExistingAdmin and ExistingDir from the current install, if any.
procedure DetectExistingInstall;
begin
  ExistingMainland := MainlandInstalled;
  ExistingAdmin := False;
  ExistingDir := '';
  if not ExistingMainland then
    Exit;
  if RegQueryStringValue(HKCU, UninstallKey, 'Inno Setup: App Path', ExistingDir) then
    ExistingAdmin := FileExists(AddBackslash(ExistingDir) + 'experimental-admin') and CursorMeterTaskExists;
end;

procedure SetInstallShield(ShowShield: Boolean);
var
  Flag: Integer;
begin
  if ShowShield then
    Flag := 1
  else
    Flag := 0;
  SendMessageInt(WizardForm.NextButton.Handle, BCM_SETSHIELD, 0, Flag);
  if ShowShield then
  begin
    WizardForm.NextButton.Width := NextButtonWidth + ScaleX(20);
    WizardForm.NextButton.Left := NextButtonLeft - ScaleX(20);
  end
  else
  begin
    WizardForm.NextButton.Width := NextButtonWidth;
    WizardForm.NextButton.Left := NextButtonLeft;
  end;
end;

// The Install button stays hidden until a mode is chosen. Admin mode puts the UAC shield on it.
procedure UpdateInstallButton;
var
  OnReady, Chosen: Boolean;
begin
  OnReady := WizardForm.CurPageID = wpReady;
  Chosen := AdminSelection <> ChoiceNone;
  if OnReady then
  begin
    if not ExistingMainland then
      WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall)
    else if AdminSelection = ChoiceUninstallFirst then
      WizardForm.NextButton.Caption := 'Desinstalar'
    else
      WizardForm.NextButton.Caption := 'Atualizar';
    WizardForm.NextButton.Visible := Chosen;
    WizardForm.NextButton.Enabled := Chosen;
    // The shield marks the click that raises a UAC: Instalar in admin mode, or Desinstalar when the
    // installed version has an elevated task to remove.
    if ExistingMainland then
      SetInstallShield(Chosen and (AdminSelection = ChoiceUninstallFirst) and ExistingAdmin)
    else
      SetInstallShield(Chosen and (AdminSelection = ChoiceWithAdmin));
  end
  else
  begin
    // Next is also the Finish button, so the shield must not carry over past this page.
    SetInstallShield(False);
    if WizardForm.CurPageID < wpPreparing then
    begin
      WizardForm.NextButton.Visible := True;
      WizardForm.NextButton.Enabled := True;
    end;
  end;
end;

// Marks one card as chosen: radio, accent strip, and the Install button (with the UAC shield).
procedure SelectChoice(Index: Integer);
var
  I: Integer;
begin
  if UpdatingChoice then
    Exit;
  UpdatingChoice := True;
  try
    AdminSelection := Index;
    if ExistingMainland then
      // Updating keeps the installed mode; uninstalling first starts from scratch.
      ExperimentalAdminChosen := (Index = ChoiceUpdate) and ExistingAdmin
    else
      ExperimentalAdminChosen := Index = ChoiceWithAdmin;
    for I := 0 to 1 do
    begin
      CardRadio[I].Checked := I = Index;
      CardAccent[I].Visible := I = Index;
    end;
  finally
    UpdatingChoice := False;
  end;
  UpdateInstallButton;
end;

procedure CardClick(Sender: TObject);
begin
  SelectChoice(TComponent(Sender).Tag);
end;

// One selectable card: radio with the title, a description, and an accent strip when chosen.
procedure CreateCard(Index: Integer; const Title, Description: String; TopPos, Height: Integer);
begin
  CardPanel[Index] := TPanel.Create(WizardForm);
  CardPanel[Index].Parent := WizardForm.ReadyPage;
  CardPanel[Index].Left := 0;
  CardPanel[Index].Top := TopPos;
  CardPanel[Index].Width := WizardForm.ReadyMemo.Width;
  CardPanel[Index].Height := Height;
  CardPanel[Index].BevelOuter := bvLowered;
  CardPanel[Index].ParentBackground := False;
  CardPanel[Index].Tag := Index;
  CardPanel[Index].OnClick := @CardClick;

  CardAccent[Index] := TPanel.Create(WizardForm);
  CardAccent[Index].Parent := CardPanel[Index];
  CardAccent[Index].Left := 0;
  CardAccent[Index].Top := 0;
  CardAccent[Index].Width := ScaleX(4);
  CardAccent[Index].Height := Height;
  CardAccent[Index].BevelOuter := bvNone;
  CardAccent[Index].ParentBackground := False;
  CardAccent[Index].Color := clHighlight;
  CardAccent[Index].Visible := False;

  CardRadio[Index] := TNewRadioButton.Create(WizardForm);
  CardRadio[Index].Parent := CardPanel[Index];
  CardRadio[Index].Left := ScaleX(14);
  CardRadio[Index].Top := ScaleY(8);
  CardRadio[Index].Width := CardPanel[Index].Width - ScaleX(28);
  CardRadio[Index].Height := ScaleY(20);
  CardRadio[Index].Caption := Title;
  CardRadio[Index].Font.Style := [fsBold];
  CardRadio[Index].Tag := Index;
  CardRadio[Index].Checked := False;
  CardRadio[Index].OnClick := @CardClick;

  CardText[Index] := TNewStaticText.Create(WizardForm);
  CardText[Index].Parent := CardPanel[Index];
  CardText[Index].Left := ScaleX(32);
  CardText[Index].Top := ScaleY(30);
  CardText[Index].Width := CardPanel[Index].Width - ScaleX(46);
  CardText[Index].AutoSize := False;
  CardText[Index].WordWrap := True;
  CardText[Index].Height := Height - ScaleY(36);
  CardText[Index].Caption := Description;
  CardText[Index].Tag := Index;
  CardText[Index].OnClick := @CardClick;
end;

procedure CreateAdminChoice;
var
  TopPos: Integer;
  AdminTitle, AdminText, PlainTitle, PlainText: String;
begin
  TopPos := 0;
  if Start11BeforeV3 then
  begin
    AdminInfo := TNewStaticText.Create(WizardForm);
    AdminInfo.Parent := WizardForm.ReadyPage;
    AdminInfo.Left := 0;
    AdminInfo.Top := 0;
    AdminInfo.Width := WizardForm.ReadyMemo.Width;
    AdminInfo.Height := ScaleY(64);
    AdminInfo.AutoSize := False;
    AdminInfo.WordWrap := True;
    AdminInfo.Caption :=
      'Foi detetado o Start11 ' + Start11Version + '. Com esta versão, abrir e fechar o menu Iniciar ou a Vista de Tarefas ' +
      'com o rato deixa a barra de tarefas por cima das barras do CursorMeter até ao próximo clique. ' +
      'Por isso recomendamos o modo administrador (experimental), que as mantém sempre visíveis.';
    TopPos := AdminInfo.Height + ScaleY(8);
  end;

  AdminTitle := 'Com permissão de administrador';
  AdminText := 'Mantém as barras por cima da barra de tarefas (modo experimental). ' +
    'Pede permissão de administrador uma vez, ao clicar em Instalar.';
  PlainTitle := 'Sem permissão de administrador';
  PlainText := 'Não pede permissão de administrador.';
  if Start11BeforeV3 then
  begin
    AdminTitle := AdminTitle + ' (recomendado)';
    PlainText := PlainText + ' Com o Start11 anterior ao v3, as barras podem ficar tapadas até ao próximo clique.';
  end
  else
  begin
    PlainTitle := PlainTitle + ' (recomendado)';
    PlainText := PlainText + ' Sem o Start11 anterior ao v3 não é preciso o modo administrador.';
  end;

  // Recommended mode first.
  if Start11BeforeV3 then
  begin
    CreateCard(ChoiceWithAdmin, AdminTitle, AdminText, TopPos, ScaleY(72));
    CreateCard(ChoiceWithoutAdmin, PlainTitle, PlainText, TopPos + ScaleY(80), ScaleY(72));
  end
  else
  begin
    CreateCard(ChoiceWithoutAdmin, PlainTitle, PlainText, TopPos, ScaleY(72));
    CreateCard(ChoiceWithAdmin, AdminTitle, AdminText, TopPos + ScaleY(80), ScaleY(72));
  end;
end;

// Returns Value as a PowerShell single-quoted string literal.
function PsQuote(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '''', '''''', True);
  Result := '''' + Result + '''';
end;

function XmlEscape(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '&', '&amp;', True);
  StringChangeEx(Result, '<', '&lt;', True);
  StringChangeEx(Result, '>', '&gt;', True);
end;

// Same task as StartupShortcut.SetScheduledTaskEnabled in the app. The task always exists in
// administrator mode so the app can start elevated through it without a UAC prompt; the logon
// trigger is what "start with Windows" turns on and off.
function BuildTaskXml(LogonEnabled: Boolean): String;
var
  WorkDir, ExePath, UserName, Enabled: String;
begin
  WorkDir := XmlEscape(RemoveBackslashUnlessRoot(WizardDirValue));
  ExePath := XmlEscape(AddBackslash(RemoveBackslashUnlessRoot(WizardDirValue)) + 'CursorMeter.exe');
  UserName := XmlEscape(ExpandConstant('{username}'));
  if LogonEnabled then
    Enabled := 'true'
  else
    Enabled := 'false';
  Result :=
    '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <Triggers><LogonTrigger><Enabled>' + Enabled + '</Enabled><UserId>' + UserName + '</UserId></LogonTrigger></Triggers>' + #13#10 +
    '  <Principals><Principal id="Author"><UserId>' + UserName + '</UserId>' +
    '<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>' + #13#10 +
    '  <Settings>' +
    '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' +
    '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
    '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' +
    '<AllowHardTerminate>true</AllowHardTerminate>' +
    '<StartWhenAvailable>false</StartWhenAvailable>' +
    '<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' +
    '<AllowStartOnDemand>true</AllowStartOnDemand>' +
    '<Enabled>true</Enabled><Hidden>false</Hidden>' +
    '<RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>' +
    '<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority>' +
    '</Settings>' + #13#10 +
    '  <Actions Context="Author"><Exec><Command>' + ExePath + '</Command>' +
    '<WorkingDirectory>' + WorkDir + '</WorkingDirectory></Exec></Actions>' + #13#10 +
    '</Task>';
end;

// Script that registers (or removes) the elevated task. Without -Elevated it asks for
// elevation itself with Start-Process -Verb RunAs: from Setup, ShellExecute "runas" can be refused
// with error 5 and no UAC prompt in setups where this path works. Exit code 0 = done,
// 1223 = UAC declined, 100 = the task command failed, other = Win32 error starting the prompt.
// Body is the elevated part of the script. It must not reference Setup-only functions when it is
// used by the uninstaller, so the body builders below are kept separate from this wrapper.
function BuildElevationScript(const ScriptPath, Body: String): String;
begin
  Result :=
    'param([switch]$Elevated)' + #13#10 +
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    'if (-not $Elevated) {' + #13#10 +
    '  try {' + #13#10 +
    '    $proc = Start-Process -FilePath ''powershell.exe'' -ArgumentList ' +
      PsQuote('-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -Elevated') +
      ' -Verb RunAs -Wait -PassThru -WindowStyle Hidden' + #13#10 +
    '    exit $proc.ExitCode' + #13#10 +
    '  } catch {' + #13#10 +
    '    $e = $_.Exception' + #13#10 +
    '    while ($e -and -not ($e -is [System.ComponentModel.Win32Exception])) { $e = $e.InnerException }' + #13#10 +
    '    if ($e) { exit $e.NativeErrorCode } else { exit 1 }' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10 +
    'try {' + #13#10 +
    Body +
    '  exit 0' + #13#10 +
    '} catch {' + #13#10 +
    '  exit 100' + #13#10 +
    '}' + #13#10;
end;

function RemoveTaskBody: String;
begin
  Result := '  schtasks.exe /Delete /TN ''CursorMeter'' /F | Out-Null' + #13#10 +
    '  if ($LASTEXITCODE -ne 0) { exit 100 }' + #13#10;
end;

function CreateTaskBody(Mode: Integer): String;
begin
  // schtasks wants the XML file in UTF-16, which Setup cannot write, so PowerShell writes it.
  Result :=
    '  $xml = @''' + #13#10 +
    BuildTaskXml(Mode = TaskAtLogon) + #13#10 +
    '''@' + #13#10 +
    '  $path = Join-Path $env:TEMP ''cursormeter-task.xml''' + #13#10 +
    '  [IO.File]::WriteAllText($path, $xml, [Text.Encoding]::Unicode)' + #13#10 +
    '  schtasks.exe /Create /TN ''CursorMeter'' /XML $path /F | Out-Null' + #13#10 +
    '  $code = $LASTEXITCODE' + #13#10 +
    '  Remove-Item $path -ErrorAction SilentlyContinue' + #13#10 +
    '  if ($code -ne 0) { exit 100 }' + #13#10;
end;

// Runs Body elevated and returns the script exit code in ErrorCode (see BuildElevationScript).
function RunPrivilegedBody(const Body: String; var ErrorCode: Integer): Boolean;
var
  ScriptPath, PowerShell, Params: String;
begin
  Result := False;
  ScriptPath := ExpandConstant('{%TEMP}\cursormeter-elevate.ps1');
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not SaveStringToFile(ScriptPath, BuildElevationScript(ScriptPath, Body), False) then
  begin
    ErrorCode := 2;
    Exit;
  end;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"';
  if IsSetupElevated then
    Params := Params + ' -Elevated';
  if not Exec(PowerShell, Params, '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
    Log('Could not start PowerShell: ' + IntToStr(ErrorCode))
  else
    Log('Privileged step exit code ' + IntToStr(ErrorCode));
  DeleteFile(ScriptPath);
  Result := ErrorCode = 0;
end;

function RunPrivilegedStep(Mode: Integer; var ErrorCode: Integer): Boolean;
begin
  if Mode = TaskRemove then
    Result := RunPrivilegedBody(RemoveTaskBody, ErrorCode)
  else
    Result := RunPrivilegedBody(CreateTaskBody(Mode), ErrorCode);
end;

// Cards for an existing Mainland install: update it (keeps the mode) or uninstall it first.
procedure CreateExistingChoice;
var
  ModeText, UpdateText, UninstallText: String;
begin
  if ExistingAdmin then
    ModeText := 'com permissão de administrador'
  else
    ModeText := 'sem permissão de administrador';

  AdminInfo := TNewStaticText.Create(WizardForm);
  AdminInfo.Parent := WizardForm.ReadyPage;
  AdminInfo.Left := 0;
  AdminInfo.Top := 0;
  AdminInfo.Width := WizardForm.ReadyMemo.Width;
  AdminInfo.Height := ScaleY(48);
  AdminInfo.AutoSize := False;
  AdminInfo.WordWrap := True;
  AdminInfo.Caption :=
    'O CursorMeter Mainland já está instalado, ' + ModeText + '. Para mudar o modo de administrador ' +
    'é preciso desinstalar primeiro a versão instalada.';

  UpdateText := 'Mantém a instalação ' + ModeText + '. Não pede permissão de administrador.';
  UninstallText := 'Remove a versão instalada e depois deixa escolher o modo de administrador.';
  if ExistingAdmin then
    UninstallText := UninstallText + ' Pede permissão de administrador uma vez, para remover a tarefa agendada.';

  CreateCard(ChoiceUpdate, 'Atualizar (manter o modo atual)', UpdateText, AdminInfo.Height + ScaleY(8), ScaleY(72));
  CreateCard(ChoiceUninstallFirst, 'Desinstalar primeiro a versão instalada', UninstallText,
    AdminInfo.Height + ScaleY(88), ScaleY(72));
end;

procedure UpdateReadyPageText;
begin
  if ExistingMainland then
  begin
    WizardForm.PageNameLabel.Caption := 'O CursorMeter já está instalado';
    WizardForm.PageDescriptionLabel.Caption := 'Escolha se atualiza ou se desinstala primeiro a versão instalada.';
  end
  else
  begin
    WizardForm.PageNameLabel.Caption := 'Selecione o modo de instalação';
    WizardForm.PageDescriptionLabel.Caption :=
      'Escolha se o CursorMeter pede permissão de administrador.';
  end;
end;

const
  UninstallFailedMessage =
    'Não foi possível desinstalar a versão instalada. Desinstale-a pelas Definições do Windows e volte a abrir o instalador.';

// Removes the elevated task, asking for the UAC right away. A plain delete is tried first, in case
// the task allows it. Shows its own message and returns False when the task is still there.
function RemoveElevatedTask: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not CursorMeterTaskExists then
    Exit;
  Exec('schtasks.exe', '/Delete /TN "CursorMeter" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not CursorMeterTaskExists then
    Exit;
  if RunPrivilegedStep(TaskRemove, ResultCode) then
    Exit;
  Result := False;
  if ResultCode = ERROR_CANCELLED then
    MsgBox('A permissão de administrador foi recusada. Nada foi desinstalado.', mbInformation, MB_OK)
  else
    MsgBox('Não foi possível remover a tarefa agendada do modo administrador (erro ' + IntToStr(ResultCode) +
      '). Nada foi desinstalado.', mbError, MB_OK);
end;

// Uninstalls the installed Mainland. With administrator mode the UAC comes first, as soon as the
// button is clicked, and the uninstaller only runs once the task is gone. Shows its own messages.
function UninstallExisting: Boolean;
var
  ResultCode, Waited: Integer;
  Command: String;
begin
  Result := False;
  if not RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', Command) then
  begin
    MsgBox(UninstallFailedMessage, mbError, MB_OK);
    Exit;
  end;
  Command := RemoveQuotes(Command);

  if ExistingAdmin and not RemoveElevatedTask then
    Exit;

  // Silent: the choice was made here. A silent uninstaller never raises a UAC; the task is
  // already gone, and is checked again below in case it was not.
  if not Exec(Command, '/SILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Could not start the uninstaller: ' + IntToStr(ResultCode));
    MsgBox(UninstallFailedMessage, mbError, MB_OK);
    Exit;
  end;
  // The uninstaller can hand over to a temporary copy and return early; wait until it is gone.
  Waited := 0;
  while MainlandInstalled and (Waited < 120000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  if MainlandInstalled then
  begin
    MsgBox(UninstallFailedMessage, mbError, MB_OK);
    Exit;
  end;

  // Safety net: a task that came back or was not part of administrator mode.
  if CursorMeterTaskExists then
    MsgBox('A versão instalada foi removida, mas a tarefa agendada "CursorMeter" não foi apagada. ' +
      'Pode apagá-la no Agendador de Tarefas.', mbInformation, MB_OK);
  Result := True;
end;

// After uninstalling first, the page turns into the normal choice for a fresh install.
procedure ShowFreshChoice;
var
  I: Integer;
begin
  for I := 0 to 1 do
    CardPanel[I].Visible := False;
  AdminInfo.Visible := False;
  ExistingMainland := False;
  ExistingAdmin := False;
  ExperimentalAdminChosen := False;
  PrivilegedStepDone := False;
  AdminSelection := ChoiceNone;
  CreateAdminChoice;
  UpdateReadyPageText;
  UpdateInstallButton;
end;

procedure InitializeWizard;
begin
#ifdef TestStart11Version
  // Test builds only (ISCC /DTestStart11Version=2.0): pretend that Start11 is installed.
  Start11Detected := True;
  Start11Version := '{#TestStart11Version}';
#else
  Start11Detected := ReadStart11Version(Start11Version);
#endif
  DetectExistingInstall;
  // Updating keeps the installed mode. This is also what a silent update does, since the cards
  // never show: the task already exists, so there is nothing to ask or register again.
  ExperimentalAdminChosen := ExistingAdmin;
  PrivilegedStepDone := ExistingAdmin;
  AdminSelection := ChoiceNone;
  UpdatingChoice := False;
  NextButtonLeft := WizardForm.NextButton.Left;
  NextButtonWidth := WizardForm.NextButton.Width;
  if ExistingMainland then
    CreateExistingChoice
  else
    CreateAdminChoice;
end;

// An update keeps the folder, and keeps the startup choice of an administrator install (that one
// lives in the scheduled task, which the app toggles itself).
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if not ExistingMainland then
    Exit;
  if PageID = wpSelectDir then
    Result := True
  else if PageID = wpSelectTasks then
    Result := ExistingAdmin;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    UpdateReadyPageText;
    // Setup fills and shows its own summary each time this page opens, so hide it again here.
    WizardForm.ReadyLabel.Visible := False;
    WizardForm.ReadyMemo.Visible := False;
  end;
  UpdateInstallButton;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode, Mode: Integer;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  if AdminSelection = ChoiceNone then
  begin
    Result := False;
    Exit;
  end;

  if ExistingMainland then
  begin
    if AdminSelection = ChoiceUninstallFirst then
    begin
      // Stay on this page: it turns into the normal choice once the old version is gone.
      Result := False;
      if UninstallExisting then
        ShowFreshChoice;
    end;
    // Updating: the mode (and its task) stays as it is, so there is no UAC here.
    Exit;
  end;

  ExperimentalAdminChosen := AdminSelection = ChoiceWithAdmin;
  if not ExperimentalAdminChosen then
    Exit;

  // The only UAC prompt comes here, when Instalar is clicked. The files still install per user in
  // this same wizard; the elevated task is registered now and later starts the app without UAC.
  if WizardIsTaskSelected('startup') then
    Mode := TaskAtLogon
  else
    Mode := TaskOnDemand;
  if RunPrivilegedStep(Mode, ErrorCode) then
  begin
    PrivilegedStepDone := True;
    Exit;
  end;

  Result := False;
  if ErrorCode = ERROR_CANCELLED then
    MsgBox('A permissão de administrador foi recusada. A instalação não foi iniciada.',
      mbInformation, MB_OK)
  else if ErrorCode = 100 then
    MsgBox('Não foi possível registar a tarefa agendada do modo administrador. ' +
      'A instalação não foi iniciada.', mbError, MB_OK)
  else
    MsgBox('Não foi possível pedir a permissão de administrador (erro ' + IntToStr(ErrorCode) + ': ' +
      SysErrorMessage(ErrorCode) + '). A instalação não foi iniciada.',
      mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  MarkerPath: String;
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
      if not ExperimentalAdminChosen and CursorMeterTaskExists then
      begin
        Exec('schtasks.exe', '/Delete /TN "CursorMeter" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        // An elevated task may need elevation to delete; this is the only prompt without admin mode.
        if CursorMeterTaskExists then
          RunPrivilegedStep(TaskRemove, ResultCode);
      end;
    end;
    Exit;
  end;

  // Marker file read by UiAccess.IsConfigured in the app. The logon task was handled on Instalar.
  if (CurStep = ssPostInstall) and ExperimentalAdminChosen and PrivilegedStepDone then
    SaveStringToFile(ExpandConstant('{app}\experimental-admin'), '', False);
end;

// The task is created elevated, so a plain schtasks /Delete can be refused. Try without a prompt
// first and only ask for elevation (one UAC) when the task is still there. Silent uninstalls never
// prompt.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  if not CursorMeterTaskExists then
    Exit;
  Exec('schtasks.exe', '/Delete /TN "CursorMeter" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if CursorMeterTaskExists and not UninstallSilent then
    RunPrivilegedStep(TaskRemove, ResultCode);
end;
