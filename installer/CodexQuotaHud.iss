#define StableAppId "{{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}"
#ifdef InternalTestRoot
#define EffectiveAppId "CQH.Test." + InternalTestId
#else
#define EffectiveAppId StableAppId
#endif

[Setup]
AppId={#EffectiveAppId}
AppName=Codex Quota HUD
AppVersion={#AppVersion}
AppPublisher=老姚
UsePreviousSetupType=yes
#ifdef InternalTestRoot
DefaultDirName={#InternalTestRoot}\LocalAppData\Programs\CodexQuotaHud
#else
DefaultDirName={localappdata}\Programs\CodexQuotaHud
#endif
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
english.DesignerComponentName=Install Skin Designer
chinesesimp.DesignerComponentName=安装皮肤设计器
english.DesignerComponentDescription=Optional visual editor for creating and exporting Codex Quota HUD skins. It is not required to run or import skins.
chinesesimp.DesignerComponentDescription=用于创建并导出 Codex Quota HUD 皮肤的可选可视化编辑器。运行主程序或导入皮肤无需安装此组件。
english.NormalInstallType=Normal installation
chinesesimp.NormalInstallType=标准安装
english.CustomInstallType=Custom installation
chinesesimp.CustomInstallType=自定义安装
english.DesignerShortcutName=Codex Quota HUD 皮肤设计器
chinesesimp.DesignerShortcutName=Codex Quota HUD 皮肤设计器
english.PurgeSettingsTask=Also remove personal settings and preview window state
chinesesimp.PurgeSettingsTask=同时删除个人设置和预览窗口状态
english.UninstallOptionsTitle=Codex Quota HUD uninstall options
chinesesimp.UninstallOptionsTitle=Codex Quota HUD 卸载选项
english.UninstallOptionsText=Choose what to remove, then continue to the final uninstall confirmation.
chinesesimp.UninstallOptionsText=选择需要删除的内容，然后继续到最终卸载确认。
english.ContinueUninstall=Continue
chinesesimp.ContinueUninstall=继续
english.CancelUninstall=Cancel
chinesesimp.CancelUninstall=取消
english.LifecycleFailure=Codex Quota HUD could not be prepared safely.
chinesesimp.LifecycleFailure=无法安全准备 Codex Quota HUD。
english.LaunchAfterInstall=Launch Codex Quota HUD
chinesesimp.LaunchAfterInstall=启动 Codex Quota HUD
english.GuidCreateFailure=Windows could not create a migration identifier.
chinesesimp.GuidCreateFailure=Windows 无法创建迁移标识符。
english.GuidFormatFailure=Windows could not format the migration identifier.
chinesesimp.GuidFormatFailure=Windows 无法格式化迁移标识符。
english.PowerShellStartFailure=PowerShell could not start (code %1).
chinesesimp.PowerShellStartFailure=无法启动 PowerShell（代码 %1）。
english.LifecycleExitFailure=The lifecycle helper exited with code %1.
chinesesimp.LifecycleExitFailure=生命周期助手已退出，代码为 %1。
english.HelperExtractFailure=The lifecycle helper could not be extracted: %1
chinesesimp.HelperExtractFailure=无法提取生命周期助手：%1
english.HelperCopyFailure=The lifecycle helper could not be copied from %1 to %2.
chinesesimp.HelperCopyFailure=无法将生命周期助手从 %1 复制到 %2。
english.LaunchFailure=Codex Quota HUD could not be launched (code %1).
chinesesimp.LaunchFailure=无法启动 Codex Quota HUD（代码 %1）。

[Types]
Name: "normal"; Description: "{cm:NormalInstallType}"
Name: "custom"; Description: "{cm:CustomInstallType}"; Flags: iscustom

[Components]
Name: "designer"; Description: "{cm:DesignerComponentName}"; Types: custom

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"
Name: "desktopicon"; Description: "{cm:DesktopTask}"

[Files]
Source: "{#PublishedDir}\CodexQuotaHud.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishedDir}\designer\CodexQuotaHud.SkinDesigner.exe"; DestDir: "{app}\designer"; Components: designer; Flags: ignoreversion
#ifdef InternalTestRoot
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; Flags: dontcopy
#else
Source: "{#RepositoryRoot}\scripts\installer-lifecycle-production.ps1"; DestDir: "{app}\scripts"; DestName: "installer-lifecycle.ps1"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle-production.ps1"; DestName: "installer-lifecycle.ps1"; Flags: dontcopy
#endif

[Icons]
#ifdef InternalTestRoot
Name: "{#InternalTestRoot}\Shell\StartMenu\Programs\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"
Name: "{#InternalTestRoot}\Shell\StartMenu\Programs\{cm:DesignerShortcutName}"; Filename: "{app}\designer\CodexQuotaHud.SkinDesigner.exe"; Components: designer
Name: "{#InternalTestRoot}\Shell\Desktop\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"; Tasks: desktopicon
#else
Name: "{autoprograms}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"
Name: "{autoprograms}\{cm:DesignerShortcutName}"; Filename: "{app}\designer\CodexQuotaHud.SkinDesigner.exe"; Components: designer
Name: "{autodesktop}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"; Tasks: desktopicon
#endif

[InstallDelete]
#ifdef InternalTestRoot
Type: files; Name: "{#InternalTestRoot}\Shell\Desktop\Codex Quota HUD 开发预览.lnk"
#else
Type: files; Name: "{autodesktop}\Codex Quota HUD 开发预览.lnk"
#endif

[Registry]
#ifdef InternalTestRoot
Root: HKCU; Subkey: "Software\CodexQuotaHud.Tests\{#InternalTestId}\Run"; ValueType: string; ValueName: "CodexQuotaHud.InternalTest.{#InternalTestId}"; ValueData: """{app}\CodexQuotaHud.App.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
#else
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexQuotaHud"; ValueData: """{app}\CodexQuotaHud.App.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
#endif

[Code]
const
#ifdef InternalTestRoot
  RunRegistryKey = 'Software\CodexQuotaHud.Tests\{#InternalTestId}\Run';
  RunRegistryValueName = 'CodexQuotaHud.InternalTest.{#InternalTestId}';
  UninstallRegistryKey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\CQH.Test.{#InternalTestId}_is1';
#else
  RunRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunRegistryValueName = 'CodexQuotaHud';
  UninstallRegistryKey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1';
#endif

var
  SetupLifecyclePath: String;
  UninstallLifecyclePath: String;
  LegacyBackupPath: String;
  LegacyShellStatePath: String;
  DesignerBackupPath: String;
  LegacyPrepared: Boolean;
  DesignerRemovalAttempted: Boolean;
  InstallCompleted: Boolean;
  LaunchAfterInstallCheckBox: TNewCheckBox;
  DesignerDescriptionLabel: TNewStaticText;
  PurgeSettingsRequested: Boolean;
  UninstallPrepareAttempted: Boolean;
  UninstallPurgeAttempted: Boolean;
  UninstallFinalizeAttempted: Boolean;

function CoCreateGuid(var Guid: TGuid): LongInt;
  external 'CoCreateGuid@ole32.dll stdcall';

function StringFromGUID2(
  var Guid: TGuid;
  GuidString: String;
  MaxCount: Integer): Integer;
  external 'StringFromGUID2@ole32.dll stdcall';

#ifdef InternalTestRoot
function GetFileAttributesW(FileName: String): Integer;
  external 'GetFileAttributesW@kernel32.dll stdcall';
#endif

function NewGuidText(): String;
var
  Guid: TGuid;
  GuidLength: Integer;
  GuidString: String;
begin
  if CoCreateGuid(Guid) <> 0 then
    RaiseException(CustomMessage('GuidCreateFailure'));

  SetLength(GuidString, 39);
  GuidLength := StringFromGUID2(Guid, GuidString, 39);
  if GuidLength <> 39 then
    RaiseException(CustomMessage('GuidFormatFailure'));

  SetLength(GuidString, GuidLength - 1);
  Result := Copy(GuidString, 2, 36);
end;

#ifdef InternalTestRoot
procedure RejectInternalDiagnosticReparsePoint(const Path: String);
var
  Attributes: Integer;
begin
  if not FileExists(Path) and not DirExists(Path) then
    exit;

  Attributes := GetFileAttributesW(Path);
  if (Attributes <> -1) and ((Attributes and $400) <> 0) then
    RaiseException('Refusing internal diagnostic reparse point: ' + Path);
end;

function RunInternalLifecycle(
  const HelperPath: String;
  const Action: String;
  const Parameters: String;
  var ResultCode: Integer;
  var ErrorText: String): Boolean;
var
  DiagnosticRoot: String;
  PersistentHelperPath: String;
  WrapperPath: String;
  ProcessLogPath: String;
  PowerShellPath: String;
  SourceHash: String;
  PersistentHash: String;
  InvocationRecord: String;
  WrapperContent: String;
begin
  Result := False;
  try
    DiagnosticRoot := '{#InternalTestRoot}\diagnostics';
    PersistentHelperPath := DiagnosticRoot + '\installer-lifecycle.ps1';
    WrapperPath := DiagnosticRoot + '\lifecycle-run.cmd';
    ProcessLogPath := DiagnosticRoot + '\lifecycle-process.log';
    PowerShellPath := ExpandConstant(
      '{sysnative}\WindowsPowerShell\v1.0\powershell.exe');

    RejectInternalDiagnosticReparsePoint('{#InternalTestRoot}');
    RejectInternalDiagnosticReparsePoint(DiagnosticRoot);
    if not ForceDirectories(DiagnosticRoot) then
      RaiseException(
        'Could not create the fixed internal diagnostic directory.');
    RejectInternalDiagnosticReparsePoint('{#InternalTestRoot}');
    RejectInternalDiagnosticReparsePoint(DiagnosticRoot);
    RejectInternalDiagnosticReparsePoint(PersistentHelperPath);
    RejectInternalDiagnosticReparsePoint(WrapperPath);
    RejectInternalDiagnosticReparsePoint(ProcessLogPath);

    if not CopyFile(HelperPath, PersistentHelperPath, False) then
      RaiseException('Could not persist the internal lifecycle helper.');
    RejectInternalDiagnosticReparsePoint(PersistentHelperPath);
    SourceHash := GetSHA256OfFile(HelperPath);
    PersistentHash := GetSHA256OfFile(PersistentHelperPath);
    if (SourceHash = '') or
      (CompareText(SourceHash, PersistentHash) <> 0) then
      RaiseException('Persisted internal lifecycle helper hash mismatch.');

    InvocationRecord :=
      'Action=' + Action + #13#10 +
      'HelperPath=' + PersistentHelperPath + #13#10 +
      'Parameters=' + Parameters + #13#10 +
      'SourceHelperSHA256=' + SourceHash + #13#10 +
      'PersistentHelperSHA256=' + PersistentHash + #13#10;
    if not SaveStringToFile(ProcessLogPath, InvocationRecord, True) then
      RaiseException('Could not append the internal lifecycle invocation.');
    RejectInternalDiagnosticReparsePoint(ProcessLogPath);

    WrapperContent :=
      '@echo off' + #13#10 +
      AddQuotes(PowerShellPath) + ' ' + Parameters + ' 1>>' +
      AddQuotes(ProcessLogPath) + ' 2>&1' + #13#10 +
      'set "LifecycleExit=%errorlevel%"' + #13#10 +
      'exit /b %LifecycleExit%' + #13#10;
    if not SaveStringToFile(WrapperPath, WrapperContent, False) then
      RaiseException('Could not write the fixed internal lifecycle wrapper.');
    RejectInternalDiagnosticReparsePoint(WrapperPath);

    Result := Exec(
      ExpandConstant('{cmd}'),
      '/D /S /C ' + AddQuotes(WrapperPath),
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);
  except
    ErrorText := GetExceptionMessage;
  end;
end;
#endif

function RunLifecycle(
  const HelperPath: String;
  const Action: String;
  const BackupPath: String;
  const ShellStatePath: String;
  var ErrorText: String): Boolean;
var
  Parameters: String;
  ResultCode: Integer;
  ExecutionHelperPath: String;
begin
#ifdef InternalTestRoot
  ExecutionHelperPath :=
    '{#InternalTestRoot}\diagnostics\installer-lifecycle.ps1';
#else
  ExecutionHelperPath := HelperPath;
#endif
  Parameters := '-ExecutionPolicy Bypass -NoProfile -NonInteractive -File ' +
    AddQuotes(ExecutionHelperPath) + ' -Action ' + Action + ' -InstallPath ' +
    AddQuotes(ExpandConstant('{app}'));
  if BackupPath <> '' then
  begin
    if (Action = 'PrepareDesignerComponentRemoval') or
      (Action = 'CommitDesignerComponentRemoval') or
      (Action = 'RollbackDesignerComponentRemoval') then
      Parameters := Parameters + ' -DesignerBackupPath ' +
        AddQuotes(BackupPath)
    else
      Parameters := Parameters + ' -LegacyBackupPath ' +
        AddQuotes(BackupPath);
  end;
  if ShellStatePath <> '' then
    Parameters := Parameters + ' -LegacyShellStatePath ' +
      AddQuotes(ShellStatePath);
#ifdef InternalTestRoot
  Parameters := Parameters + ' -InternalTestMode -LocalAppDataRoot ' +
    AddQuotes('{#InternalTestRoot}\LocalAppData') +
    ' -InternalErrorDiagnosticPath ' +
    AddQuotes('{#InternalTestRoot}\lifecycle-error.json');
  if (Action = 'SnapshotLegacyState') or
    (Action = 'DiscardLegacyState') or
    (Action = 'CompensateLegacyInstall') or
    (Action = 'PrepareDesignerComponentRemoval') or
    (Action = 'CommitDesignerComponentRemoval') or
    (Action = 'RollbackDesignerComponentRemoval') then
    Parameters := Parameters + ' -InternalShellRootPath ' +
      AddQuotes('{#InternalTestRoot}\Shell');
#ifdef InternalCleanupFailureStage
  if ((CompareText('{#InternalCleanupFailureStage}', 'LegacyCommit') = 0) and
      (Action = 'CommitInstall')) or
    ((CompareText(
      '{#InternalCleanupFailureStage}',
      'DesignerAfterPayloadDelete') = 0) and
      (Action = 'CommitDesignerComponentRemoval')) then
    Parameters := Parameters + ' -InternalCleanupFailureStage ' +
      AddQuotes('{#InternalCleanupFailureStage}');
#endif
#endif

#ifdef InternalTestRoot
  Result := RunInternalLifecycle(
    HelperPath,
    Action,
    Parameters,
    ResultCode,
    ErrorText);
#else
  Result := Exec(
    ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'),
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
#endif
  if not Result then
  begin
    ErrorText := FmtMessage(
      CustomMessage('PowerShellStartFailure'), [IntToStr(ResultCode)]);
    exit;
  end;

  Result := ResultCode = 0;
  if not Result then
    ErrorText := FmtMessage(
      CustomMessage('LifecycleExitFailure'), [IntToStr(ResultCode)]);
end;

procedure InitializeWizard();
var
  DescriptionGap: Integer;
  DescriptionHeight: Integer;
begin
  DescriptionGap := ScaleY(8);
  DescriptionHeight := ScaleY(44);
  WizardForm.ComponentsList.Height :=
    WizardForm.ComponentsList.Height - DescriptionGap - DescriptionHeight;
  DesignerDescriptionLabel := TNewStaticText.Create(WizardForm);
  DesignerDescriptionLabel.Parent := WizardForm.SelectComponentsPage;
  DesignerDescriptionLabel.Left := WizardForm.ComponentsList.Left;
  DesignerDescriptionLabel.Top := WizardForm.ComponentsList.Top +
    WizardForm.ComponentsList.Height + DescriptionGap;
  DesignerDescriptionLabel.Width := WizardForm.ComponentsList.Width;
  DesignerDescriptionLabel.Height := DescriptionHeight;
  DesignerDescriptionLabel.AutoSize := False;
  DesignerDescriptionLabel.WordWrap := True;
  DesignerDescriptionLabel.Caption :=
    CustomMessage('DesignerComponentDescription');

  LaunchAfterInstallCheckBox := TNewCheckBox.Create(WizardForm);
  LaunchAfterInstallCheckBox.Parent := WizardForm.FinishedPage;
  LaunchAfterInstallCheckBox.Left := WizardForm.RunList.Left;
  LaunchAfterInstallCheckBox.Top := WizardForm.RunList.Top;
  LaunchAfterInstallCheckBox.Width := WizardForm.RunList.Width;
  LaunchAfterInstallCheckBox.Caption :=
    CustomMessage('LaunchAfterInstall');
  LaunchAfterInstallCheckBox.Checked := True;
end;

procedure LaunchInstalledApp();
var
  ResultCode: Integer;
begin
  if WizardSilent or (not LaunchAfterInstallCheckBox.Checked) then
    exit;

  if not Exec(
    ExpandConstant('{app}\CodexQuotaHud.App.exe'),
    '',
    '',
    SW_SHOWNORMAL,
    ewNoWait,
    ResultCode) then
    MsgBox(
      FmtMessage(
        CustomMessage('LaunchFailure'), [IntToStr(ResultCode)]),
      mbError,
      MB_OK);
end;

procedure RemoveManagedSelections();
begin
#ifdef InternalTestRoot
  DeleteFile('{#InternalTestRoot}\Shell\Desktop\Codex Quota HUD.lnk');
  DeleteFile(
    '{#InternalTestRoot}\Shell\Desktop\Codex Quota HUD 开发预览.lnk');
#else
  DeleteFile(ExpandConstant('{autodesktop}\Codex Quota HUD.lnk'));
  DeleteFile(ExpandConstant(
    '{autodesktop}\Codex Quota HUD 开发预览.lnk'));
#endif
  RegDeleteValue(HKCU, RunRegistryKey, RunRegistryValueName);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorText: String;
  ExactInstalledExecutable: String;
  MigrationGuid: String;
  IsLegacyInstall: Boolean;
begin
  Result := '';
  NeedsRestart := False;
  SetupLifecyclePath := ExpandConstant('{tmp}\installer-lifecycle.ps1');

  try
    ExtractTemporaryFile('installer-lifecycle.ps1');
  except
    Result := FmtMessage(
      CustomMessage('HelperExtractFailure'), [GetExceptionMessage]);
    exit;
  end;

  ExactInstalledExecutable := ExpandConstant(
    '{app}\CodexQuotaHud.App.exe');
  IsLegacyInstall :=
    (not RegKeyExists(HKCU, UninstallRegistryKey)) and
    FileExists(ExactInstalledExecutable);

  LegacyBackupPath := '';
  LegacyShellStatePath := '';
  DesignerBackupPath := '';
  if IsLegacyInstall then
  begin
    MigrationGuid := NewGuidText();
    LegacyBackupPath := ExtractFileDir(ExpandConstant('{app}')) +
      '\CodexQuotaHud.legacy-backup.' +
      MigrationGuid;
    LegacyShellStatePath := ExtractFileDir(ExpandConstant('{app}')) +
      '\CodexQuotaHud.legacy-shell-state.' +
      MigrationGuid;
  end;

  if not WizardIsComponentSelected('designer') then
  begin
    MigrationGuid := NewGuidText();
    DesignerBackupPath := ExtractFileDir(ExpandConstant('{app}')) +
      '\CodexQuotaHud.designer-removal-backup.' +
      MigrationGuid;
  end;

  if not RunLifecycle(
    SetupLifecyclePath,
    'PrepareInstall',
    LegacyBackupPath,
    '',
    ErrorText) then
  begin
    Result := ErrorText;
    exit;
  end;

  LegacyPrepared := IsLegacyInstall;
  if IsLegacyInstall and (not RunLifecycle(
    SetupLifecyclePath,
    'SnapshotLegacyState',
    '',
    LegacyShellStatePath,
    ErrorText)) then
  begin
    Result := ErrorText;
    exit;
  end;

  if DesignerBackupPath <> '' then
  begin
    DesignerRemovalAttempted := True;
    if not RunLifecycle(
      SetupLifecyclePath,
      'PrepareDesignerComponentRemoval',
      DesignerBackupPath,
      '',
      ErrorText) then
    begin
      Result := ErrorText;
      exit;
    end;
  end;

  RemoveManagedSelections();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorText: String;
begin
  if CurStep = ssDone then
  begin
    InstallCompleted := True;
    if not RunLifecycle(
      SetupLifecyclePath,
      'CommitInstall',
      LegacyBackupPath,
      '',
      ErrorText) then
      Log('Legacy install backup cleanup failed: ' + ErrorText);

    if DesignerRemovalAttempted and (not RunLifecycle(
      SetupLifecyclePath,
      'CommitDesignerComponentRemoval',
      DesignerBackupPath,
      '',
      ErrorText)) then
      Log('Designer component cleanup failed: ' + ErrorText);

    if LegacyPrepared and (not RunLifecycle(
      SetupLifecyclePath,
      'DiscardLegacyState',
      '',
      LegacyShellStatePath,
      ErrorText)) then
      Log('Legacy shell state cleanup failed: ' + ErrorText);

    LaunchInstalledApp();
  end;
end;

procedure DeinitializeSetup();
var
  ErrorText: String;
begin
  if DesignerRemovalAttempted and (not InstallCompleted) then
  begin
    if not RunLifecycle(
      SetupLifecyclePath,
      'RollbackDesignerComponentRemoval',
      DesignerBackupPath,
      '',
      ErrorText) then
      Log('Designer component rollback failed: ' + ErrorText);
  end;

  if LegacyPrepared and (not InstallCompleted) then
  begin
    if not RunLifecycle(
      SetupLifecyclePath,
      'CompensateLegacyInstall',
      '',
      LegacyShellStatePath,
      ErrorText) then
      Log('Legacy shell compensation failed: ' + ErrorText);

    if not RunLifecycle(
      SetupLifecyclePath,
      'RollbackInstall',
      LegacyBackupPath,
      '',
      ErrorText) then
      Log('Legacy migration rollback failed: ' + ErrorText);
  end;
end;

function HasCommandLineParameter(const Expected: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Expected) = 0 then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function ShowUninstallOptions(): Boolean;
var
  OptionsForm: TSetupForm;
  InfoLabel: TNewStaticText;
  PurgeSettingsCheckBox: TNewCheckBox;
  ContinueButton: TNewButton;
  CancelButton: TNewButton;
  ButtonWidth: Integer;
begin
  OptionsForm := CreateCustomForm(
    ScaleX(460), ScaleY(150), False, True);
  try
    OptionsForm.Caption := CustomMessage('UninstallOptionsTitle');

    InfoLabel := TNewStaticText.Create(OptionsForm);
    InfoLabel.Parent := OptionsForm;
    InfoLabel.Left := ScaleX(12);
    InfoLabel.Top := ScaleY(12);
    InfoLabel.Width := OptionsForm.ClientWidth - ScaleX(24);
    InfoLabel.Height := ScaleY(36);
    InfoLabel.AutoSize := False;
    InfoLabel.WordWrap := True;
    InfoLabel.Caption := CustomMessage('UninstallOptionsText');

    PurgeSettingsCheckBox := TNewCheckBox.Create(OptionsForm);
    PurgeSettingsCheckBox.Parent := OptionsForm;
    PurgeSettingsCheckBox.Left := ScaleX(12);
    PurgeSettingsCheckBox.Top := ScaleY(56);
    PurgeSettingsCheckBox.Width := OptionsForm.ClientWidth - ScaleX(24);
    PurgeSettingsCheckBox.Caption := CustomMessage('PurgeSettingsTask');
    PurgeSettingsCheckBox.Checked := PurgeSettingsRequested;

    ContinueButton := TNewButton.Create(OptionsForm);
    ContinueButton.Parent := OptionsForm;
    ContinueButton.Caption := CustomMessage('ContinueUninstall');
    ContinueButton.Top := OptionsForm.ClientHeight - ScaleY(33);
    ContinueButton.Height := ScaleY(23);
    ContinueButton.ModalResult := mrOk;
    ContinueButton.Default := True;

    CancelButton := TNewButton.Create(OptionsForm);
    CancelButton.Parent := OptionsForm;
    CancelButton.Caption := CustomMessage('CancelUninstall');
    CancelButton.Top := ContinueButton.Top;
    CancelButton.Height := ContinueButton.Height;
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    ButtonWidth := OptionsForm.CalculateButtonWidth([ContinueButton.Caption,
      CancelButton.Caption]);
    ContinueButton.Width := ButtonWidth;
    CancelButton.Width := ButtonWidth;
    CancelButton.Left := OptionsForm.ClientWidth - ButtonWidth - ScaleX(10);
    ContinueButton.Left := CancelButton.Left - ButtonWidth - ScaleX(6);
    OptionsForm.ActiveControl := ContinueButton;

    Result := OptionsForm.ShowModal() = mrOk;
    if Result then
      PurgeSettingsRequested := PurgeSettingsCheckBox.Checked;
  finally
    OptionsForm.Free();
  end;
end;

function InitializeUninstall(): Boolean;
var
  InstalledHelperPath: String;
begin
  Result := False;
  PurgeSettingsRequested :=
    HasCommandLineParameter('/PURGESETTINGS');
  if (not HasCommandLineParameter('/SILENT')) and
    (not HasCommandLineParameter('/VERYSILENT')) and
    (not ShowUninstallOptions()) then
    exit;

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
      FmtMessage(
        CustomMessage('HelperCopyFailure'), [InstalledHelperPath,
          UninstallLifecyclePath]),
      mbError,
      MB_OK);
    exit;
  end;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ErrorText: String;
begin
  if (CurUninstallStep = usUninstall) and
    (not UninstallPrepareAttempted) then
  begin
    UninstallPrepareAttempted := True;
    if not RunLifecycle(
      UninstallLifecyclePath,
      'PrepareUninstall',
      '',
      '',
      ErrorText) then
      RaiseException(ErrorText);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if (not UninstallPurgeAttempted) and
      PurgeSettingsRequested then
    begin
      UninstallPurgeAttempted := True;
      if not RunLifecycle(
        UninstallLifecyclePath,
        'PurgeSettings',
        '',
        '',
        ErrorText) then
        MsgBox(ErrorText, mbError, MB_OK);
    end;

    if not UninstallFinalizeAttempted then
    begin
      UninstallFinalizeAttempted := True;
      if not RunLifecycle(
        UninstallLifecyclePath,
        'FinalizeUninstall',
        '',
        '',
        ErrorText) then
        MsgBox(ErrorText, mbError, MB_OK);
    end;
  end;
end;

procedure DeinitializeUninstall();
begin
  if UninstallLifecyclePath <> '' then
    DeleteFile(UninstallLifecyclePath);
end;
