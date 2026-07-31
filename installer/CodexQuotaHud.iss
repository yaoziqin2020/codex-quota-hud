#define StableAppId "{{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}"

[Setup]
AppId={#StableAppId}
AppName=Codex Quota HUD
AppVersion={#AppVersion}
AppPublisher=老姚
DefaultDirName={localappdata}\Programs\CodexQuotaHud
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputBaseFilename=CodexQuotaHud-Setup-v{#AppVersion}
SetupIconFile={#RepositoryRoot}\src\CodexQuotaHud.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\CodexQuotaHud.App.exe
CloseApplications=no
RestartApplications=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "{#ChineseLanguageFile}"

[CustomMessages]
english.StartupTask=Start Codex Quota HUD automatically when I sign in
chinesesimp.StartupTask=登录 Windows 时自动启动 Codex Quota HUD
english.DesktopTask=Create a desktop shortcut
chinesesimp.DesktopTask=创建桌面快捷方式
english.PreviewDesktopTask=Create a Developer Preview desktop shortcut
chinesesimp.PreviewDesktopTask=创建“开发预览”桌面快捷方式
english.PurgeSettingsTask=Also remove personal settings and preview window state
chinesesimp.PurgeSettingsTask=同时删除个人设置和预览窗口状态
english.LifecycleFailure=Codex Quota HUD could not be prepared safely.
chinesesimp.LifecycleFailure=无法安全准备 Codex Quota HUD。

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:DesktopTask}"; Flags: checkedonce
Name: "previewdesktopicon"; Description: "{cm:PreviewDesktopTask}"; Flags: unchecked

[Files]
Source: "{#PublishedDir}\CodexQuotaHud.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"
Name: "{autodesktop}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"; Tasks: desktopicon
Name: "{autodesktop}\Codex Quota HUD 开发预览"; Filename: "{app}\CodexQuotaHud.App.exe"; Parameters: "--preview"; Tasks: previewdesktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexQuotaHud"; ValueData: """{app}\CodexQuotaHud.App.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\CodexQuotaHud.App.exe"; Description: "{cm:LaunchProgram,Codex Quota HUD}"; Flags: nowait postinstall skipifsilent; Check: MayLaunchInstalledApp

[Code]
const
  RunRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallRegistryKey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1';

var
  SetupLifecyclePath: String;
  UninstallLifecyclePath: String;
  LegacyBackupPath: String;
  LegacyPrepared: Boolean;
  InstallCompleted: Boolean;
  PurgeSettingsCheckBox: TNewCheckBox;
  UninstallPurgeAttempted: Boolean;

function CoCreateGuid(var Guid: TGuid): LongInt;
  external 'CoCreateGuid@ole32.dll stdcall';

function StringFromGUID2(
  var Guid: TGuid;
  GuidString: String;
  MaxCount: Integer): Integer;
  external 'StringFromGUID2@ole32.dll stdcall';

function NewGuidText(): String;
var
  Guid: TGuid;
  GuidLength: Integer;
  GuidString: String;
begin
  if CoCreateGuid(Guid) <> 0 then
    RaiseException('Windows could not create a migration identifier.');

  SetLength(GuidString, 39);
  GuidLength := StringFromGUID2(Guid, GuidString, 39);
  if GuidLength <> 39 then
    RaiseException('Windows could not format a migration identifier.');

  SetLength(GuidString, GuidLength - 1);
  Result := Copy(GuidString, 2, 36);
end;

function RunLifecycle(
  const HelperPath: String;
  const Action: String;
  const BackupPath: String;
  var ErrorText: String): Boolean;
var
  Parameters: String;
  ResultCode: Integer;
begin
  Parameters := '-ExecutionPolicy Bypass -NoProfile -NonInteractive -File ' +
    AddQuotes(HelperPath) + ' -Action ' + Action + ' -InstallPath ' +
    AddQuotes(ExpandConstant('{app}'));
  if BackupPath <> '' then
    Parameters := Parameters + ' -LegacyBackupPath ' + AddQuotes(BackupPath);

  Result := Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
  if not Result then
  begin
    ErrorText := CustomMessage('LifecycleFailure') + ' ' +
      Format('PowerShell could not start (%d).', [ResultCode]);
    exit;
  end;

  Result := ResultCode = 0;
  if not Result then
    ErrorText := CustomMessage('LifecycleFailure') + ' ' +
      Format('The lifecycle helper exited with code %d.', [ResultCode]);
end;

procedure RemoveManagedSelections();
begin
  DeleteFile(ExpandConstant('{autodesktop}\Codex Quota HUD.lnk'));
  DeleteFile(ExpandConstant(
    '{autodesktop}\Codex Quota HUD 开发预览.lnk'));
  RegDeleteValue(HKCU, RunRegistryKey, 'CodexQuotaHud');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorText: String;
  ExactInstalledExecutable: String;
  IsLegacyInstall: Boolean;
begin
  Result := '';
  NeedsRestart := False;
  SetupLifecyclePath := ExpandConstant('{tmp}\installer-lifecycle.ps1');

  try
    ExtractTemporaryFile('installer-lifecycle.ps1');
  except
    Result := CustomMessage('LifecycleFailure') + ' ' + GetExceptionMessage;
    exit;
  end;

  ExactInstalledExecutable := ExpandConstant(
    '{localappdata}\Programs\CodexQuotaHud\CodexQuotaHud.App.exe');
  IsLegacyInstall :=
    (not RegKeyExists(HKCU, UninstallRegistryKey)) and
    FileExists(ExactInstalledExecutable);

  LegacyBackupPath := '';
  if IsLegacyInstall then
    LegacyBackupPath := ExpandConstant(
      '{localappdata}\Programs\CodexQuotaHud.legacy-backup.') +
      NewGuidText();

  if not RunLifecycle(
    SetupLifecyclePath,
    'PrepareInstall',
    LegacyBackupPath,
    ErrorText) then
  begin
    Result := ErrorText;
    exit;
  end;

  LegacyPrepared := IsLegacyInstall;
  RemoveManagedSelections();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorText: String;
begin
  if CurStep = ssDone then
  begin
    if not RunLifecycle(
      SetupLifecyclePath,
      'CommitInstall',
      LegacyBackupPath,
      ErrorText) then
      RaiseException(ErrorText);

    InstallCompleted := True;
  end;
end;

function MayLaunchInstalledApp(): Boolean;
begin
  Result := InstallCompleted;
end;

procedure DeinitializeSetup();
var
  ErrorText: String;
begin
  if LegacyPrepared and (not InstallCompleted) then
  begin
    if not RunLifecycle(
      SetupLifecyclePath,
      'RollbackInstall',
      LegacyBackupPath,
      ErrorText) then
      Log('Legacy migration rollback failed: ' + ErrorText);
  end;
end;

function InitializeUninstall(): Boolean;
var
  ErrorText: String;
  InstalledHelperPath: String;
begin
  Result := False;
  UninstallLifecyclePath := ExpandConstant(
    '{tmp}\installer-lifecycle.' + NewGuidText() + '.ps1');
  InstalledHelperPath := ExpandConstant(
    '{app}\scripts\installer-lifecycle.ps1');
  if not CopyFile(
    InstalledHelperPath,
    UninstallLifecyclePath,
    True) then
  begin
    MsgBox(
      CustomMessage('LifecycleFailure') + ' The helper could not be extracted.',
      mbError,
      MB_OK);
    exit;
  end;

  if not RunLifecycle(
    UninstallLifecyclePath,
    'PrepareUninstall',
    '',
    ErrorText) then
  begin
    MsgBox(ErrorText, mbError, MB_OK);
    exit;
  end;

  Result := True;
end;

procedure InitializeUninstallProgressForm();
begin
  PurgeSettingsCheckBox := TNewCheckBox.Create(UninstallProgressForm);
  PurgeSettingsCheckBox.Parent := UninstallProgressForm.InnerPage;
  PurgeSettingsCheckBox.Left := 0;
  PurgeSettingsCheckBox.Top :=
    UninstallProgressForm.ProgressBar.Top +
    UninstallProgressForm.ProgressBar.Height + ScaleY(16);
  PurgeSettingsCheckBox.Width :=
    UninstallProgressForm.InnerPage.ClientWidth;
  PurgeSettingsCheckBox.Caption := CustomMessage('PurgeSettingsTask');
  PurgeSettingsCheckBox.Checked := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorText: String;
begin
  if (CurUninstallStep = usPostUninstall) and
    (not UninstallPurgeAttempted) and
    PurgeSettingsCheckBox.Checked then
  begin
    UninstallPurgeAttempted := True;
    if not RunLifecycle(
      UninstallLifecyclePath,
      'PurgeSettings',
      '',
      ErrorText) then
      MsgBox(ErrorText, mbError, MB_OK);
  end;
end;

procedure DeinitializeUninstall();
begin
  if UninstallLifecyclePath <> '' then
    DeleteFile(UninstallLifecyclePath);
end;
