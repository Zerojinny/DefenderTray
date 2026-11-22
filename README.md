[English](README.en.md) | **한국어**

# DefenderTray

**Microsoft Defender용 트레이 도구**

> **Microsoft와 관련 없는 개인 프로젝트입니다.** Microsoft, Microsoft Defender, Windows는 Microsoft Corporation의 상표이며, 이 프로젝트는 Microsoft가 만들거나 보증한 것이 아닙니다.

Microsoft Defender 실시간 보호를 트레이 아이콘에서 켜고 끄고, 검사 제외·검사·격리 항목·SmartScreen을 관리하는 작은 프로그램입니다.
다른 PC의 디스크를 연결해 대량 복사·백업·복구를 자주 하는 PC 유지보수 작업에 맞춰 만들었습니다.

- **화면 언어는 한국어와 영어**를 지원합니다. 시스템 로케일(또는 Windows 표시 언어)이 한국어면 한국어, 아니면 영어로 시작하며, **설정 → Language / 언어**에서 바꿀 수 있습니다.
- 관리자 권한으로 실행됩니다 (실행할 때 UAC 확인).
- **변조 방지(Tamper Protection)가 켜져 있으면** 실시간 보호를 끌 수 없습니다. 메뉴의 "변조 방지: 켜짐 — 끄는 방법 보기"를 참고하세요.

## 내려받기

[Releases](https://github.com/Zerojinny/DefenderTray/releases)에서 받습니다.

| 버전 | 파일 | 설명 |
| --- | --- | --- |
| **설치판 (권장)** | `DefenderTray-<버전>-setup.exe` | `C:\Program Files\DefenderTray`에 설치하고 제거(설정 → 앱)를 등록합니다. 'Windows 시작 시 실행', '오른쪽 클릭 메뉴', '시작 메뉴 바로가기'(실행·제거), '바탕 화면 아이콘'을 설치할 때 고릅니다. 제거하면 시간 지정으로 꺼 둔 보호를 다시 켜고 시작 프로그램·오른쪽 클릭 메뉴를 정리합니다. |
| **포터블판** | `DefenderTray-<버전>-portable.zip` | 압축을 풀고 `DefenderTray.exe`를 실행합니다. USB 등에 넣어 여러 PC에서 쓸 때 편합니다. |

- 두 버전 모두 설정은 `HKCU\Software\DefenderTray`, 기록은 `%LOCALAPPDATA%\DefenderTray`에 저장하므로 서로 옮겨도 설정이 이어집니다.
- 이 앱은 관리자 권한으로 실행됩니다. 일반 사용자도 고칠 수 있는 폴더(USB, 바탕 화면 등)에 둔 실행 파일은 바꿔치기될 위험이 있으니, 계속 쓸 PC에는 설치판을 권합니다.
- 아직 코드 서명이 없어 처음 실행할 때 SmartScreen 경고가 나올 수 있습니다. 받은 파일은 `SHA256SUMS.txt`로 확인할 수 있습니다.

### 배포 파일 만들기 (개발자)
- 로컬: `powershell -ExecutionPolicy Bypass -File release.ps1` → `dist\`에 설치판·포터블판·`SHA256SUMS.txt`
  (Inno Setup 6이 필요합니다. 기본 위치 `C:\Program Files (x86)\Inno Setup 6`)
- GitHub Actions도 같은 `release.ps1`을 씁니다.

### 새 버전 올리기 (개발자)
1. `src/AssemblyInfo.cs`의 `AssemblyVersion`·`AssemblyFileVersion`을 올리고 커밋·푸시합니다.
2. 같은 번호로 태그를 붙여 푸시합니다: `git tag v1.0.1` → `git push origin v1.0.1`
3. GitHub Actions(`.github/workflows/release.yml`)가 빌드·시험 후 설치판·포터블판·체크섬을 Releases에 올립니다.
   Actions 탭에서 수동 실행하면 릴리스 없이 결과물만 만들어 시험할 수 있습니다.

## 빌드와 시험

| 명령 | 하는 일 |
| --- | --- |
| `build.bat` | `src\*.cs`를 컴파일해 `DefenderTray.exe`를 만듭니다. 아이콘(`app.ico`)도 함께 생성합니다. |
| `test.bat` | 화면·관리자 권한 없이 돌릴 수 있는 로직 시험(`tests\Tests.cs`)을 실행합니다. |

- .NET Framework 4.x에 들어 있는 `csc.exe`(C# 5)만 있으면 됩니다. Visual Studio는 필요 없습니다.
- 실행 중인 DefenderTray가 있으면 exe를 덮어쓸 수 없으니 먼저 트레이에서 종료하세요.

## 기능

### 실시간 보호 켜기 / 끄기
- 트레이 아이콘 색으로 상태를 보여 줍니다 (초록 = 켜짐, 빨강 = 꺼짐, 회색 = 알 수 없음).
- **실시간 보호 끄기 ▸** 에서 기간을 고릅니다: 30분 / 1시간 / 2시간 / 4시간 / 직접 켤 때까지.
  - 기간이 지나면 자동으로 다시 켭니다. 예약 시각은 저장되므로 재부팅 후에도 지켜집니다.
  - 이미 꺼져 있을 때 기간을 고르면 다시 켜질 시각만 바뀝니다.
  - 메뉴 맨 위 상태 줄과 아이콘 툴팁에 다시 켜질 시각과 남은 시간이 나옵니다.
- 아이콘 **더블클릭**: 확인 후 켜기/끄기. 끌 때는 마지막으로 고른 기간을 씁니다.

### SmartScreen
- 트레이 **SmartScreen ▸**: 세 가지(앱 및 파일 확인 / Microsoft Edge / Microsoft Store 앱) 상태 표시,
  30분·1시간·2시간·4시간·직접 켤 때까지 끄기, 다시 켜기. 끄기는 설정 창에서 고른 "끌 항목"에 적용됩니다 (기본: 앱 및 파일 확인).
- **SmartScreen 설정** 창: 항목별 켜기/끄기와 그룹 정책 사용 여부.

| 항목 | 일반 설정 | 그룹 정책 |
| --- | --- | --- |
| 앱 및 파일 확인 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SmartScreenEnabled` = `Warn` / `Off` | gpedit 파일 탐색기 > "Windows Defender SmartScreen 구성" (`Software\Policies\Microsoft\Windows\System`: `EnableSmartScreen`, `ShellSmartScreenLevel`) |
| Microsoft Edge | Edge가 프로필에 보관 → 밖에서 못 바꿈 | `Software\Policies\Microsoft\Edge\SmartScreenEnabled` (끌 때는 항상 정책, Edge 재시작 필요) |
| Microsoft Store 앱 | `HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost\EnableWebContentEvaluation` (사용자별) | 없음 |

- 다른 곳(Windows 보안 앱 등)에서 바뀌면 "⚠ 외부에서 변경됨" 알림과 기록을 남깁니다 (외부 변경 알림 설정을 따름).
- 조직 정책으로 고정된 항목은 잠기고 바꿀 수 없습니다.
- 첨부 파일 관리자 정책의 낮은/중간 위험 파일 형식 목록(`LowRiskFileTypes` / `ModRiskFileTypes`, 예: `.exe`)에 든 형식은 SmartScreen이 켜져 있어도 Windows가 확인을 건너뜁니다. 설정 창이 이를 알려 줍니다.

### 검사 제외 관리
- 프로세스 제외(탐색기 고정 항목 + 사용자 지정), 폴더·파일 제외.
- 폴더를 추가할 때 **검사 범위**를 고릅니다.
  - **실시간 보호에서만 제외 (권장)**: 복사·열기 때만 건너뛰고 수동·예약 검사는 합니다.
    등록 형식은 `경로\:{ScanTrigger:OnAccess}` (Microsoft 문서의 contextual exclusion). Windows 보안 앱에서는 관리할 수 없습니다.
  - **모든 검사에서 제외**: 기존 방식.
- 네트워크 드라이브(`Y:`)는 실제 네트워크 경로(`\\서버\공유`)로 바꿔 등록합니다 → [docs/network-drive-exclusions.md](docs/network-drive-exclusions.md)
- **현재 제외 목록 보기** 창: 추가, 제거, 검사 범위 바꾸기, 선택 폴더 검사, 드라이브 문자 항목 변환.
- 탐색기 오른쪽 클릭 메뉴에 "Defender 검사 제외에 추가"를 넣을 수 있습니다.

### 검사
- **연결된 드라이브 수동 검사 ▸**: 이동식·고정 드라이브를 골라 검사.
- **새 드라이브 연결 시 검사 제안** (설정, 켜고 끌 수 있음): USB나 외장 디스크를 연결하면 알림을 띄우고, 알림을 누르면 검사합니다.
- **예약 검사 설정** (설정): 검사 종류·요일·시각·CPU 한도·놓친 검사·전체 검사 범위, 마지막 빠른/전체 검사 시각, 지금 검사.
  예약 검사는 Windows 자동 유지 관리와 함께 실행되어 정한 시각보다 늦어질 수 있습니다.
  - **예약 검사 사용**: 끄면 `Set-MpPreference -ScanScheduleDay 8`과 같습니다 (일반 설정).
  - **그룹 정책(gpedit)으로도 설정**: gpedit의 "예약된 검사를 실행할 요일 지정"을 **사용**(요일 또는 안 함)으로 씁니다.
    로컬 정책 파일 `%WINDIR%\System32\GroupPolicy\Machine\Registry.pol`의 해당 항목만 추가·삭제하고(다른 정책은 보존, 쓰기 전 `Registry.pol.DefenderTray.bak` 백업),
    `GPT.INI` 버전을 올리며, 레지스트리 `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Scan\ScheduleDay`에도 바로 반영합니다.
    gpedit 화면에서도 "사용"으로 보이고 gpedit에서 되돌릴 수 있습니다. 체크를 풀면 "구성되지 않음"으로 돌아갑니다.
    도메인 정책이 같은 항목을 정한 PC에서는 잠깁니다.

### 격리 항목
- 격리된 항목만 보여 줍니다 (위협 이름, 원래 위치, 격리 시각, 자동 삭제 예정일, 심각도).
- **복원**: 이 파일만 제외 후 복원(권장) / 위협 허용 후 복원 / 다른 폴더로 사본 복원(격리 유지).
- 항목별 영구 삭제는 공식 방법이 없어 Windows 보안의 보호 기록에서 합니다. 격리 보관 기간은 이 창에서 바꿀 수 있습니다.

### 변경 감시·알림·기록
- **내부 변경 알림** (`✓ 이 도구에서 변경함`)과 **외부 변경 알림** (`⚠ 외부에서 변경됨`)을 따로 켜고 끕니다.
  외부 변경은 Defender 이벤트 로그(5000/5001/5007)로 감지하며, 프로그램이 꺼져 있던 동안의 변경도 다음 실행 때 알려 줍니다.
- **외부 변경 되돌리기** (각각 켜고 끔): 외부에서 추가한 제외 삭제 / 삭제한 제외 복구 / 끈 실시간 보호 다시 켜기.
  10분 안에 같은 대상을 3번 넘게 되돌려야 하면 멈춥니다. 그룹 정책 변경은 되돌리지 않습니다.
- **기록 보기**: 내부·외부·실패·정보 기록을 걸러 보고 검색합니다.

## 메뉴 구성

```
● 실시간 보호: 꺼짐 — 14:30에 다시 켜짐 (25분 남음)
변조 방지: 꺼짐
─────────────
실시간 보호 켜기(O)
실시간 보호 끄기(F) ▸  30분 / 1시간 / 2시간 / 4시간 / 직접 켤 때까지
SmartScreen(M) ▸       상태 3줄 / 끌 항목 / 30분 ~ 직접 켤 때까지 끄기 / 다시 켜기 / SmartScreen 설정...
─────────────
검사 제외 관리(E) ▸
연결된 드라이브 수동 검사(C) ▸
격리 항목(Q)...
기록 보기(L)...
─────────────
설정(T) ▸  예약 검사 설정(K)... / 알림(A) ▸ / 외부 변경 되돌리기(G) ▸ /
           새 드라이브 연결 시 검사 제안(D) / Language / 언어(L) ▸ / Windows 시작 시 실행(U)
Windows 보안 열기(S)
새로 고침(R)
─────────────
종료(X)
```

## 저장 위치

| 내용 | 위치 |
| --- | --- |
| 설정 (알림, 되돌리기, 끄기 예약, 마지막 선택, 언어 `Language` = auto/ko/en 등) | `HKCU\Software\DefenderTray` |
| 활동 기록 | `%LOCALAPPDATA%\DefenderTray\activity.log` (1MB 넘으면 `activity.old.log`) |
| 시작 시 실행 | 작업 스케줄러 `DefenderTray` (로그온 시, 가장 높은 권한) |
| 오른쪽 클릭 메뉴 | `HKCU\Software\Classes\exefile\shell\DefenderTrayExclude`, `...\Directory\shell\DefenderTrayExclude` |

## 소스 구성

| 파일 | 내용 |
| --- | --- |
| `src/Program.cs` | 진입점, 명령줄(`--exclude-path`, `--register-startup`, `--uninstall-cleanup` 등), 단일 실행 |
| `src/TrayContext*.cs` | 트레이 메뉴. 상태 확인(백그라운드)·켜기/끄기·시간 지정 끄기·새 드라이브 / 외부 변경 감시 / 제외 메뉴 / 검사 |
| `src/Defender.cs` | WMI(`root\Microsoft\Windows\Defender`)로 상태·설정·제외·검사 |
| `src/DefenderWatch.cs` | 이벤트 로그 해석, 이 프로그램이 한 변경 기록(`SelfChanges`) |
| `src/RevertPlan.cs`, `TimedOff.cs`, `DriveWatch.cs`, `ExclusionScope.cs`, `ScanSchedule.cs` | 시험 가능한 순수 로직 |
| `src/ManagedPolicy.cs` | 조직 정책(그룹 정책 / MDM) 감지 |
| `src/LocalGpo.cs` | 로컬 그룹 정책 파일(Registry.pol, GPT.INI) 읽기·쓰기 |
| `src/SmartScreen.cs`, `SmartScreenForm.cs`, `TrayContext.SmartScreen.cs` | SmartScreen 상태·변경, 설정 창, 트레이 메뉴·감시 |
| `src/Startup.cs` | Windows 시작 시 실행 (작업 스케줄러) |
| `src/Tr.cs` | 화면 언어. 문구는 코드에 `Tr.T("한국어", "English")`로 함께 적음 |
| `src/*Form.cs`, `Dialogs.cs` | 제외 목록, 예약 검사, 격리 항목, 기록 창과 대화 상자 |
| `installer/DefenderTray.iss`, `release.ps1` | Inno Setup 설치 스크립트, 배포 파일 만들기 |
| `tests/Tests.cs` | 로직 시험 |

## 조직에서 관리하는 PC (그룹 정책 · Intune 등)

조직 정책이 정한 Defender 설정은 이 앱으로 바꿔도 정책 값이 우선합니다. 앱은 이를 감지해 다음처럼 동작합니다 (`src/ManagedPolicy.cs`).

| 위치 | 동작 |
| --- | --- |
| 예약 검사 설정 | 정책으로 고정된 항목은 **잠그고 정책 값을 표시**하며, 위쪽에 "요일: 매일 (그룹 정책)"처럼 목록을 보여 줍니다. 적용한 뒤 값을 다시 읽어, 실제로 바뀌지 않은 항목은 경고와 실패 기록으로 남깁니다. |
| 실시간 보호 켜기/끄기 | 정책이 반대 상태로 고정했으면 시도하지 않고 "조직 정책으로 고정되어 있어 끌 수 없습니다"라고 알립니다. |
| 격리 항목 보관 기간 | 정책으로 고정되면 잠그고 툴팁으로 이유를 보여 줍니다. 적용 후 다시 읽어 확인합니다. |
| 검사 제외 | 정책 "로컬 관리자 병합 동작 구성"이 사용 안 함이면 목록 창과 메뉴에 "추가해도 적용되지 않음" 경고. 제외 목록 숨김 정책도 표시합니다. |

- 그룹 정책: `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\...` (예: `Scan\ScheduleDay`). `LocalSettingOverride*`가 켜져 있으면 로컬 설정이 이기므로 잠그지 않습니다.
- MDM: `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Defender` (Policy CSP). 값 이름이 확실한 항목만 대응시켰고, 나머지는 적용 후 다시 읽어 확인합니다.
- 조직 정책을 우회하는 기능은 넣지 않습니다. 바꾸려면 IT 관리자에게 요청해야 합니다.

## 알려진 제한
- 이 프로그램이 실행 중일 때만 감시·되돌리기·끄기 예약 복구가 동작합니다 (꺼져 있던 동안의 변경은 다음 실행 때 처리).
- 누가(어떤 프로그램이) 설정을 바꿨는지는 Defender 기록에 남지 않아 알 수 없습니다.
- 회사 그룹 정책으로 관리되는 설정은 여기서 바꿔도 정책 값이 우선할 수 있습니다.
- 조건부 제외("실시간만 제외")는 Defender 플랫폼 4.18.2205.7 이상이 필요합니다.

## 라이선스

[MIT](LICENSE)

