; 游戏时间守护 0.1.0 安装脚本。请保持 UTF-8 BOM，Inno Setup 才能正确显示中文。

#define MyAppName "游戏时间守护"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "GameTimeGuard"
#define MyAppExeName "GameTimeGuard.Tray.exe"
#define MyServiceExe "GameTimeGuard.Service.exe"

[Setup]
AppId={{A7B1E4C2-6D30-4F18-9C5A-2E8B7D0F1A44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GameTimeGuard
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=GameTimeGuard-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupMutex=GameTimeGuardSetup
UninstallDisplayName={#MyAppName} GameTimeGuard
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=管理孩子的每日游戏时间和允许时段
LicenseFile=..\LICENSE
InfoBeforeFile=before.txt
CloseApplications=force
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "..\artifacts\publish\service\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\artifacts\publish\tray\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GameTimeGuard"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\GameTimeGuard"

[Icons]
Name: "{autoprograms}\游戏时间守护"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--wizard"; Description: "现在设置家长密码和默认规则"; Flags: nowait postinstall skipifsilent

[Code]
procedure InstallService;
var
  ResultCode: Integer;
  App, Sc, DataDir: String;
begin
  DataDir := ExpandConstant('{commonappdata}\GameTimeGuard');
  ForceDirectories(DataDir);
  Exec(ExpandConstant('{sys}\icacls.exe'),
    '"' + DataDir + '" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  App := ExpandConstant('{app}\{#MyServiceExe}');
  Sc := ExpandConstant('{sys}\sc.exe');
  Exec(Sc, 'stop GameTimeGuard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
  Exec(Sc, 'delete GameTimeGuard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  if (not Exec(Sc, 'create GameTimeGuard binPath= "' + App + '" start= auto DisplayName= "游戏时间守护" obj= LocalSystem', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
  begin
    MsgBox('没有创建成功 Windows 服务。请确认是用管理员安装的。', mbError, MB_OK);
    exit;
  end;
  Exec(Sc, 'description GameTimeGuard "管理每天的游戏时间、允许时段，并在超时后结束游戏进程。"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'failure GameTimeGuard reset= 86400 actions= restart/5000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'failureflag GameTimeGuard 1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'start GameTimeGuard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'create GameTimeGuardWatch binPath= "' + App + ' --watchdog" start= auto DisplayName= "游戏时间守护看门狗" obj= LocalSystem', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'description GameTimeGuardWatch "如果主服务被停掉或删掉，就把它重新注册并启动。"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'failure GameTimeGuardWatch reset= 86400 actions= restart/5000/restart/5000/restart/5000', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(Sc, 'start GameTimeGuardWatch', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Create /TN GameTimeGuardEnsure /SC MINUTE /MO 1 /RU SYSTEM /RL HIGHEST /F /TR "\"' + App + '\" --ensure"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallService;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if not FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
  begin
    MsgBox('找不到托盘程序，无法核对家长密码。请按 README 里的「彻底移除」用管理员手动处理。', mbError, MB_OK);
    exit;
  end;
  if not Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--authorize-uninstall', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('无法启动卸载验证。', mbError, MB_OK);
    exit;
  end;
  if ResultCode <> 0 then
  begin
    MsgBox('未通过家长密码验证，已取消卸载。', mbError, MB_OK);
    exit;
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN GameTimeGuardEnsure /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop GameTimeGuardWatch', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop GameTimeGuard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1200);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete GameTimeGuardWatch', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete GameTimeGuard', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    RegDeleteKeyIncludingSubkeys(HKLM, 'SOFTWARE\GameTimeGuard');
  end;
end;
