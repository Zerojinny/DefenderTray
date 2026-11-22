# 네트워크 드라이브와 Defender 검사 제외

DefenderTray(Microsoft Defender용 트레이 도구)에서 네트워크 드라이브(예: `Y:`, `Z:`)를 검사 제외로 다룰 때 참고한 Microsoft 문서와, 그 내용을 앱에 어떻게 반영했는지 정리한 문서입니다.

- 문서 확인일: 2026-11-19
- 인용문은 확인일 기준 Microsoft Learn 원문입니다. 문서는 바뀔 수 있으니 중요한 판단 전에 링크에서 다시 확인하세요.

## 요약

| 항목 | 내용 | 근거 |
| --- | --- | --- |
| 제외 경로 지정 | 드라이브 문자(`Y:\`) 대신 실제 네트워크 경로(`\\서버\공유`)로 지정 | Microsoft 공식 문서 (아래 1) |
| 전체 검사 | 사용자가 연결한 네트워크 드라이브는 전체 검사 대상이 아님 (기본값) | Microsoft 공식 문서 (아래 2) |
| 실시간 보호 | 켜져 있으면 네트워크 공유의 파일도 검사 범위에 포함 | Microsoft 공식 문서 (아래 2) |
| UNC 제외가 드라이브 문자 접근에도 적용되는지 | 공식 문서에서 찾지 못함 — 직접 시험 필요 | 커뮤니티 Q&A만 있음 (아래 3) |

## 1. 제외 경로는 실제 네트워크 경로로

**문서:** [Exclusions in Microsoft Defender Antivirus](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-exclusions-overview)
**위치:** "Important points about exclusions" 항목

> Don't exclude mapped network drives. Specify the actual network path instead.

(연결된 네트워크 드라이브를 제외하지 마세요. 대신 실제 네트워크 경로를 지정하세요.)

문서에 이유는 적혀 있지 않습니다. 같은 문서의 "System environment variables" 항목에 따르면 Defender 서비스는 사용자 계정이 아니라 시스템 계정(LocalSystem)으로 동작합니다.

> Because the Microsoft Defender Antivirus service runs as the LocalSystem account, an environment variable in an exclusion resolves to its **system** account location, which is often different from the **user** account location you might expect.

사용자가 연결한 드라이브 문자는 사용자 세션에만 있으므로 시스템 계정에서는 보이지 않을 수 있습니다. 이 설명은 문서에 나온 내용이 아니라 추정입니다. 다만 관리자 권한으로 실행되는 DefenderTray에서 `Y:`가 보이지 않았던 현상과 성격이 같습니다.

### 같은 문서의 관련 내용

- **프로세스 제외:** "그 프로세스가 여는 파일"을 실시간 보호와 예약 검사에서 건너뜁니다.
  > A process exclusion tells Microsoft Defender Antivirus to skip the files that the process opens.
- **전체 경로 권장:** 이름만(`FastCopy.exe`) 등록하면 어디서 실행되든, 이동식 디스크에서 실행돼도 같은 이름이면 모두 제외됩니다.
  > Whenever possible, use the full path.
- **함께 꺼지는 보호:** 프로세스 제외는 그 프로세스에 대한 네트워크 보호와 공격 표면 감소(ASR) 규칙 검사도 끕니다.
  > Process exclusions on any platform also prevent network protection and attack surface reduction (ASR) rules from inspecting traffic or enforcing rules for the excluded processes.
- **와일드카드 제한:** 드라이브 문자 자리에는 와일드카드를 쓸 수 없습니다.
  > You can't use a wildcard in place of a drive letter.

## 2. 네트워크 드라이브의 검사 범위

**문서:** [Configure scanning options for Microsoft Defender Antivirus](https://learn.microsoft.com/en-us/defender-endpoint/configure-advanced-scan-types-microsoft-defender-antivirus)

"Scanning mapped network drives" 항목:

> On all supported operating systems, only the network drives that are mapped at system level are scanned. User-level mapped network drives aren't scanned. User-level mapped network drives are those that a user maps in their session manually and using their own credentials.

(시스템 수준에서 연결된 네트워크 드라이브만 검사됩니다. 사용자가 자기 세션에서 자기 자격 증명으로 연결한 드라이브는 검사되지 않습니다.)

이 항목은 같은 문서 표의 **"Run full scan on mapped network drives"**(기본값: Disabled, PowerShell `-DisableScanningMappedNetworkDrivesForFullScan`) 설정 바로 다음에 나오므로, **전체 검사**에 관한 설명으로 읽힙니다.

표 아래 Note:

> If real-time protection is turned on, files are scanned before they're accessed and executed. The scanning scope includes all files, such as files on mounted removable media, like USB drives. If the device performing the scan has real-time protection or on-access protection turned on, the scan also includes network shares.

(실시간 보호가 켜져 있으면 파일을 열거나 실행하기 전에 검사하며, 네트워크 공유도 검사 범위에 포함됩니다.)

같은 표의 관련 설정:

| 설정 | 기본값 | PowerShell 매개변수 |
| --- | --- | --- |
| Run full scan on mapped network drives | Disabled | `-DisableScanningMappedNetworkDrivesForFullScan` |
| Scan network files | Disabled | `-DisableScanningNetworkFiles` |

**해석:** FastCopy 등으로 네트워크 드라이브의 파일을 복사할 때 느려지는 것은 전체 검사가 아니라 실시간 보호 때문일 가능성이 큽니다. 이 경우 프로세스 제외(FastCopy)나 실제 네트워크 경로로 지정한 폴더 제외가 효과가 있습니다.

## 3. 공식 문서에서 확인하지 못한 것

`\\서버\공유`로 제외를 등록하면 같은 위치에 `Y:\`로 접근할 때도 제외가 적용되는지는 공식 문서에서 찾지 못했습니다.

비슷한 질문이 [Microsoft Q&A: Windows Defender Exclude Mapped Drive, does it also exclude when users accesses via UNC?](https://learn.microsoft.com/en-us/answers/questions/4225790/windows-defender-exclude-mapped-drive-does-it-also)에 있습니다. 여기 달린 답변은 그룹 정책 "Specify paths to exclude from Microsoft Defender Antivirus scans"에 `\\server\share\path` 형식으로 넣으라고 안내합니다. 하지만 이것은 커뮤니티 답변이며 Microsoft 공식 문서가 아닙니다.

**확인 방법:** 네트워크 드라이브에서 큰 파일을 복사해 실제 네트워크 경로로 제외하기 전과 후의 속도를 비교해 보세요.

## 앱에 반영한 내용

| 기능 | 동작 | 코드 |
| --- | --- | --- |
| 네트워크 경로 변환 | `Y:\a` → `\\서버\공유\a`. 사용자 레지스트리 `HKCU\Network\<문자>\RemotePath`를 먼저 읽고, 없으면 `WNetGetConnection`으로 현재 세션의 연결을 확인합니다. | `NetPath.ToUnc` |
| 폴더 제외 추가 시 자동 변환 | 트레이 메뉴의 "폴더 추가..."와 탐색기 오른쪽 클릭 메뉴로 네트워크 드라이브 폴더를 추가하면 실제 네트워크 경로로 등록하고, 변환했다고 알려 줍니다. | `Defender.AddExclusionPathResolved` |
| 기존 항목 변환 | 제외 목록 창 위쪽에 "네트워크 드라이브 문자로 등록된 제외 N개" 안내 줄과 **실제 경로로 변환** 버튼이 나타납니다. 새 경로를 먼저 추가한 뒤 드라이브 문자 항목을 삭제하므로, 중간에 실패해도 기존 제외는 남습니다. | `ExclusionListForm.ConvertDriveLetterEntries` |
| 상태 표시 | 관리자 권한 프로그램에는 사용자 세션의 드라이브 문자가 보이지 않으므로, 실제 네트워크 경로로 바꿔 백그라운드에서 확인합니다. | `ExclusionListForm.NetworkTarget` |

**적용하지 않은 것:** 프로세스 제외(실행 파일 경로)는 변환하지 않습니다. 위 Microsoft 문장은 경로 제외를 다루는 맥락이고, 네트워크에서 실행한 프로세스 경로가 어떻게 매칭되는지는 문서에서 확인하지 못했습니다.

### 알려진 제한

- **임시 연결 드라이브:** "로그인할 때 다시 연결"을 켜지 않고 연결한 드라이브는 사용자 레지스트리에 저장되지 않습니다. 관리자 권한 세션에서는 `WNetGetConnection`으로도 보이지 않을 수 있어 변환되지 않습니다. 이때는 탐색기 주소 표시줄에 `\\서버\공유`를 직접 입력해 연 폴더를 오른쪽 클릭해서 추가하세요.
- **폴더 선택 창:** 관리자 권한으로 열리는 폴더 선택 창에는 네트워크 드라이브 문자가 보이지 않을 수 있습니다. 대신 '네트워크'에서 서버를 찾거나 탐색기 오른쪽 클릭 메뉴를 사용하세요.

## 참고 문서

- [Exclusions in Microsoft Defender Antivirus](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-exclusions-overview) — Microsoft Learn
- [Configure scanning options for Microsoft Defender Antivirus](https://learn.microsoft.com/en-us/defender-endpoint/configure-advanced-scan-types-microsoft-defender-antivirus) — Microsoft Learn
- [Microsoft Q&A: Windows Defender Exclude Mapped Drive, does it also exclude when users accesses via UNC?](https://learn.microsoft.com/en-us/answers/questions/4225790/windows-defender-exclude-mapped-drive-does-it-also) — 커뮤니티 답변 (공식 문서 아님)
