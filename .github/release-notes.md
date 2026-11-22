**한국어** · [English](#english)

**DefenderTray** — Microsoft Defender용 트레이 도구입니다. **Microsoft와 관련 없는 개인 프로젝트입니다.**

## 이번 버전 (1.1.1)

- **이름을 DefenderTray로 정리했습니다.** "Microsoft Defender용 트레이 도구"는 설명으로만 씁니다. Microsoft 상표가 앞에 오면 공식 제품처럼 보일 수 있어서입니다. 실행 파일 이름, 설정·기록 위치는 그대로입니다.
- 주요 기능: 실시간 보호 켜기/끄기(시간 지정 끄기 포함), 검사 제외 관리(실시간만 제외 포함), SmartScreen 켜기/끄기, 예약 검사 설정, 격리 항목 복원, 외부 변경 감시·알림·되돌리기, 새 드라이브 검사 제안, 한국어/영어 화면.

## 받을 파일

| 파일 | 설명 |
| --- | --- |
| `DefenderTray-<버전>-setup.exe` | **설치판 (권장).** `C:\Program Files\DefenderTray`에 설치하고 제거 기능을 등록합니다. 설치할 때 'Windows 시작 시 실행', '오른쪽 클릭 메뉴', '시작 메뉴 바로가기', '바탕 화면 아이콘'을 고릅니다. |
| `DefenderTray-<버전>-portable.zip` | **포터블판.** 압축을 풀고 `DefenderTray.exe`를 실행합니다. 설치하지 않습니다. |
| `SHA256SUMS.txt` | 받은 파일이 손상되거나 바뀌지 않았는지 확인하는 해시값 |

- 두 버전 모두 설정은 `HKCU\Software\DefenderTray`, 기록은 `%LOCALAPPDATA%\DefenderTray`에 저장하므로 서로 옮겨도 설정이 이어집니다.
- 포터블판은 일반 사용자도 고칠 수 있는 폴더에 두면, 관리자 권한으로 실행되는 이 앱이 바꿔치기될 수 있습니다. 계속 쓸 PC라면 설치판을 권합니다.
- 관리자 권한이 필요합니다. 변조 방지(Tamper Protection)가 켜져 있으면 실시간 보호를 끌 수 없습니다.
- 아직 **코드 서명이 없어** 처음 실행할 때 SmartScreen 경고("Windows의 PC 보호")가 나올 수 있습니다. "추가 정보 → 실행"을 누르면 됩니다.
- Defender를 끄는 기능이 있어 일부 보안 프로그램이 이 앱을 잠재적으로 원치 않는 앱이나 해킹 도구로 표시할 수 있습니다. 소스는 이 저장소에서 확인할 수 있습니다.
- 사용법은 [README](https://github.com/Zerojinny/DefenderTray#readme)를 참고하세요.

---

<a id="english"></a>
**English**

**DefenderTray** — tray tool for Microsoft Defender. **This is a personal project and is not affiliated with Microsoft.**

## What's new in 1.1.1

- **The name is now simply DefenderTray.** "Tray tool for Microsoft Defender" is used only as a description, because putting the Microsoft trademark first could make it look like an official product. The executable name and the settings/log locations are unchanged.
- Main features: real-time protection on/off (including timed off), exclusions (including real-time-only scope), SmartScreen on/off, scheduled scan settings, quarantine restore, external-change watch/notifications/revert, new-drive scan suggestion, English/Korean UI.

## Downloads

| File | Description |
| --- | --- |
| `DefenderTray-<version>-setup.exe` | **Installer (recommended).** Installs to `C:\Program Files\DefenderTray` and registers an uninstaller. During setup you choose "start with Windows", "Explorer right-click menu", "Start menu shortcuts" and "desktop icon". |
| `DefenderTray-<version>-portable.zip` | **Portable.** Unzip and run `DefenderTray.exe`; nothing is installed. |
| `SHA256SUMS.txt` | Hashes to verify the downloads |

- Both editions keep settings in `HKCU\Software\DefenderTray` and logs in `%LOCALAPPDATA%\DefenderTray`, so you can switch between them.
- The app runs elevated. A portable copy kept in a folder that standard users can modify could be replaced, so use the installer on PCs where you keep it.
- Administrator rights are required. If Tamper Protection is on, real-time protection cannot be turned off.
- The binaries are **not code-signed yet**, so SmartScreen ("Windows protected your PC") may warn on first run. Click "More info → Run anyway".
- Because it can turn Defender off, some security products may flag it as a potentially unwanted app or hack tool. The full source is in this repository.
- See the [English README](https://github.com/Zerojinny/DefenderTray/blob/main/README.en.md) for details.
