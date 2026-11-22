; DefenderTray (Microsoft Defender용 트레이 도구 / Tray tool for Microsoft Defender) — Inno Setup 6 설치 스크립트
; 빌드: build.bat 다음에 ISCC.exe installer\DefenderTray.iss [/DAppVersion=1.0.0]
; 이 파일은 UTF-8(BOM)로 저장해야 한글이 깨지지 않는다.
; 설치 화면 언어는 Windows 표시 언어에 맞춰 한국어/영어 중 자동 선택 (ShowLanguageDialog=auto).

#define AppExe "DefenderTray.exe"
#define AppName "DefenderTray"
; 1.1.0 이하에서 쓰던 표시 이름 (예전 바로가기를 지우는 데만 씀)
#define NameKo "Microsoft Defender용 트레이 도구"
#define NameEn "Tray Tool for Microsoft Defender"
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + "..\" + AppExe)
#endif

[Setup]
; AppId는 바꾸지 말 것 (업그레이드·제거가 이 값으로 같은 프로그램을 찾음)
AppId={{FFA14B06-8264-456C-9EE0-550AD0FD4B8F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Zerojinny
AppPublisherURL=https://github.com/Zerojinny/DefenderTray
AppSupportURL=https://github.com/Zerojinny/DefenderTray/issues
AppUpdatesURL=https://github.com/Zerojinny/DefenderTray/releases
AppComments=Microsoft Defender용 트레이 도구 / Tray tool for Microsoft Defender. Not affiliated with Microsoft.
; 관리자만 쓸 수 있는 Program Files에 설치 (관리자 권한으로 실행되는 앱이므로)
DefaultDirName={autopf}\DefenderTray
; 시작 메뉴 폴더 하나에 실행·제거 바로가기를 모음 (폴더 선택 화면은 생략)
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
; 설정 → 앱 목록 이름
UninstallDisplayName={#AppName}
; 실행 중이면 설치·제거 전에 종료하라고 안내
AppMutex=Local\DefenderTray_SingleInstance
OutputDir=..\dist
OutputBaseFilename=DefenderTray-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto
VersionInfoVersion={#AppVersion}
VersionInfoDescription=DefenderTray Setup
VersionInfoProductName=DefenderTray

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.AppDescription=Microsoft Defender용 트레이 도구
english.AppDescription=Tray tool for Microsoft Defender
korean.TaskStartup=Windows 시작 시 실행
english.TaskStartup=Start with Windows
korean.TaskShellMenu=탐색기 오른쪽 클릭 메뉴에 'Defender 검사 제외에 추가' 넣기
english.TaskShellMenu=Add 'Add to Defender exclusions' to the Explorer right-click menu
korean.TaskStartMenu=시작 메뉴에 바로가기 만들기 (실행·제거)
english.TaskStartMenu=Create Start menu shortcuts (run and uninstall)
korean.AskDeleteSettings=이 도구의 설정과 활동 기록도 지울까요?
english.AskDeleteSettings=Also delete this tool's settings and activity log?
korean.SettingsLabel=설정
english.SettingsLabel=Settings
korean.LogLabel=기록
english.LogLabel=Log
korean.DefenderKept=Defender의 검사 제외·예약 검사·SmartScreen 설정은 그대로 남습니다.
english.DefenderKept=Defender exclusions, scheduled scan and SmartScreen settings are kept.

[Tasks]
Name: "startup"; Description: "{cm:TaskStartup}"
Name: "shellmenu"; Description: "{cm:TaskShellMenu}"; Flags: unchecked
; 바로가기 (시작 메뉴는 기본 체크). 끄면 제거는 설정 → 앱 또는 트레이 메뉴 '설정 → 프로그램 제거'로 함
Name: "startmenu"; Description: "{cm:TaskStartMenu}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppDescription}"; Tasks: startmenu
; Windows 시작 메뉴는 제거 프로그램(unins000.exe)을 직접 가리키는 바로가기를 숨기고, 항목이 하나 남은 폴더는 펼쳐 버림
; → 앱의 --uninstall을 거쳐 실행하게 해서 폴더와 '제거' 항목이 보이게 함
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; IconFilename: "{uninstallexe}"; Tasks: startmenu
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppDescription}"; Tasks: desktopicon

[InstallDelete]
; 바로가기 이름이 설치 언어에 따라 달라지므로, 예전 것(두 언어, 1.0.0의 시작 메뉴 바로 아래 바로가기 포함)을 먼저 지우고 새로 만든다.
; 체크를 푼 바로가기도 이것으로 사라진다 (Inno Setup은 체크를 풀어도 기존 바로가기를 남겨 두므로).
Type: files; Name: "{autoprograms}\{#NameKo}.lnk"
Type: filesandordirs; Name: "{autoprograms}\{#NameKo}"
Type: filesandordirs; Name: "{autoprograms}\{#NameEn}"
Type: files; Name: "{autodesktop}\{#NameKo}.lnk"
Type: files; Name: "{autodesktop}\{#NameEn}.lnk"
Type: filesandordirs; Name: "{autoprograms}\{#AppName}"
Type: files; Name: "{autodesktop}\{#AppName}.lnk"

[Run]
; 시작 프로그램·오른쪽 클릭 메뉴는 설치된 exe 경로로 등록 (포터블판에서 옮겨 와도 경로가 바뀜)
Filename: "{app}\{#AppExe}"; Parameters: "--register-startup"; Tasks: startup; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-startup"; Tasks: not startup; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--register-shell"; Tasks: shellmenu; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Tasks: not shellmenu; Flags: runhidden waituntilterminated
; 설치 후 실행은 원래 사용자 권한으로 돈다. 이 앱은 관리자 권한(requireAdministrator)이 필요하므로
; CreateProcess로는 오류 740이 남 → shellexec로 실행해 탐색기 더블클릭처럼 권한 상승을 거치게 함
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; 시간 지정으로 꺼 둔 보호를 다시 켜고, 시작 프로그램·오른쪽 클릭 메뉴를 지움
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "DefenderTrayCleanup"

[Code]
// 제거가 끝나면 설정(레지스트리)과 활동 기록을 지울지 묻는다. 조용한 제거에서는 남겨 둠.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    if MsgBox(CustomMessage('AskDeleteSettings') + #13#10 + #13#10 +
              CustomMessage('SettingsLabel') + ': HKCU\Software\DefenderTray' + #13#10 +
              CustomMessage('LogLabel') + ': ' + ExpandConstant('{localappdata}\DefenderTray') + #13#10 + #13#10 +
              CustomMessage('DefenderKept'),
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\DefenderTray');
      DelTree(ExpandConstant('{localappdata}\DefenderTray'), True, True, True);
    end;
  end;
end;
