**English** | [한국어](README.md)

# DefenderTray

**Tray tool for Microsoft Defender**

> **This is a personal project and is not affiliated with Microsoft.** Microsoft, Microsoft Defender and Windows are trademarks of Microsoft Corporation; this project is not made or endorsed by Microsoft.

A small Windows tray app that turns Microsoft Defender real-time protection on and off and manages exclusions, scans, quarantine and SmartScreen.
It was built for PC maintenance work: connecting other PCs' disks and doing large copies, backups and restores.

- **The user interface is available in English and Korean.** It follows the system locale (Korean on Korean Windows, English otherwise); change it under **Settings → Language / 언어**.
- The app runs with administrator rights (UAC prompt on start).
- **If Tamper Protection is on**, real-time protection cannot be turned off. The menu item "Tamper Protection: on — how to turn it off" explains what to do.

## Download

Get it from [Releases](https://github.com/Zerojinny/DefenderTray/releases).

| Edition | File | Description |
| --- | --- | --- |
| **Installer (recommended)** | `DefenderTray-<version>-setup.exe` | Installs to `C:\Program Files\DefenderTray` and adds an entry in Settings → Apps. During setup you choose "start with Windows", "Explorer right-click menu", "Start menu shortcuts" (run and uninstall) and "desktop icon". Uninstalling turns back on any protection that was turned off with a timer, and removes the startup task and right-click menu. |
| **Portable** | `DefenderTray-<version>-portable.zip` | Unzip and run `DefenderTray.exe`. Handy on a USB stick for use across several PCs. |

- Both editions keep settings in `HKCU\Software\DefenderTray` and logs in `%LOCALAPPDATA%\DefenderTray`, so you can switch between them without losing settings.
- The app runs elevated. An executable kept in a folder that standard users can modify (USB stick, desktop, etc.) could be replaced, so use the installer on PCs where you keep it.
- The binaries are **not code-signed yet**, so SmartScreen may warn on first run. Verify downloads with `SHA256SUMS.txt`.
- Some security products may flag the app as a potentially unwanted app or hack tool because it can turn Defender off. The full source is in this repository.

### Building release files (developers)
- Locally: `powershell -ExecutionPolicy Bypass -File release.ps1` writes the installer, the portable zip and `SHA256SUMS.txt` to `dist\`.
  Requires Inno Setup 6 in its default location, `C:\Program Files (x86)\Inno Setup 6`.
- GitHub Actions runs the same `release.ps1`.

### Publishing a new version (developers)
1. Bump `AssemblyVersion` and `AssemblyFileVersion` in `src/AssemblyInfo.cs`, then commit and push.
2. Push a tag with the same number: `git tag v1.0.1` → `git push origin v1.0.1`.
3. GitHub Actions (`.github/workflows/release.yml`) builds, tests and publishes the installer, the portable zip and the checksums to Releases.
   Running the workflow manually from the Actions tab builds the files without creating a release.

## Build and test

| Command | What it does |
| --- | --- |
| `build.bat` | Compiles `src\*.cs` into `DefenderTray.exe` and generates the icon (`app.ico`). |
| `test.bat` | Runs the logic tests in `tests\Tests.cs`. They need no UI and no administrator rights. |

- You only need `csc.exe` (C# 5) from .NET Framework 4.x. Visual Studio is not required.
- Close a running DefenderTray from the tray first; otherwise the exe cannot be overwritten.

## Features

### Real-time protection on / off
- The tray icon shows the state: green = on, red = off, gray = unknown.
- **Turn off real-time protection ▸** lets you choose a duration: 30 min / 1 h / 2 h / 4 h / until turned back on manually.
  - When the time is up, protection turns back on automatically. The schedule is saved, so it also survives a reboot.
  - If protection is already off, choosing a duration only changes the time it turns back on.
  - The status line at the top of the menu and the icon tooltip show when protection turns back on and how much time is left.
- **Double-click** the icon to toggle, after a confirmation. Turning off uses the last chosen duration.

### SmartScreen
- **SmartScreen ▸** in the tray menu shows the state of all three parts (apps and files / Microsoft Edge / Microsoft Store apps). It can turn them off for 30 min / 1 h / 2 h / 4 h / until turned back on manually, and turn them back on. "Off" applies to the parts chosen in the settings window; the default is apps and files only.
- **SmartScreen settings** window: on/off per part, and whether to also use Group Policy.

| Part | Regular setting | Group Policy |
| --- | --- | --- |
| Check apps and files | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SmartScreenEnabled` = `Warn` / `Off` | gpedit File Explorer > "Configure Windows Defender SmartScreen" (`Software\Policies\Microsoft\Windows\System`: `EnableSmartScreen`, `ShellSmartScreenLevel`) |
| Microsoft Edge | Edge keeps it in its own profile, so other programs cannot change it | `Software\Policies\Microsoft\Edge\SmartScreenEnabled`. Turning off always uses the policy; restart Edge for it to take effect. |
| Microsoft Store apps | `HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost\EnableWebContentEvaluation` (per user) | none |

- If SmartScreen is changed elsewhere (for example in the Windows Security app), the app shows a "⚠ Changed externally" notification and writes a log entry. This follows the external-change notification setting.
- Settings locked by organization policy are disabled and cannot be changed.
- If the Attachment Manager policy lists file types as low or moderate risk (`LowRiskFileTypes` / `ModRiskFileTypes`, e.g. `.exe`), Windows skips the SmartScreen check for those types even when SmartScreen is on. The settings window warns about this.

### Exclusions
- Process exclusions (a fixed Explorer item plus your own) and file/folder exclusions.
- When adding a folder, you choose the **scan scope**:
  - **Exclude from real-time protection only** (recommended): copying and opening skip scanning, but manual and scheduled scans still scan the folder.
    It is stored as `path\:{ScanTrigger:OnAccess}`, a *contextual exclusion* in Microsoft's documentation. The Windows Security app cannot manage this form.
  - **Exclude from all scans**: the classic behavior.
- Mapped network drives (`Y:`) are stored as their real network path (`\\server\share`) → [docs/network-drive-exclusions.md](docs/network-drive-exclusions.md) (Korean)
- **View current exclusions** window: add, remove, change scan scope, scan the selected folder, convert drive-letter entries.
- Optional Explorer right-click item "Add to Defender exclusions".

### Scans
- **Scan a connected drive ▸**: choose a removable or fixed drive to scan.
- **Suggest a scan for new drives** (in Settings, can be turned on or off): plugging in a USB stick or external disk shows a notification, and clicking it starts a scan.
- **Scheduled scan settings** (in Settings): scan type, day, time, CPU limit, catch-up scans, full-scan scope; the last quick/full scan times; scan now.
  Scheduled scans run together with Windows automatic maintenance, so they may run later than the set time.
  - **Use scheduled scans**: turning this off is the same as `Set-MpPreference -ScanScheduleDay 8`, a regular setting.
  - **Also set via Group Policy (gpedit)**: sets gpedit's "Specify the day of the week to run a scheduled scan" to **Enabled**, with the chosen day or Never.
    - It adds or removes only that entry in the local policy file `%WINDIR%\System32\GroupPolicy\Machine\Registry.pol`. Other policies are preserved, and the file is backed up to `Registry.pol.DefenderTray.bak` before writing.
    - It also bumps the `GPT.INI` version and writes `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Scan\ScheduleDay` immediately.
    - The setting shows as "Enabled" in gpedit and can be reverted there. Unchecking returns it to "Not Configured".
    - The option is locked on PCs where a domain policy sets the same item.

### Quarantine
- Shows quarantined items only: threat name, original location, quarantine time, scheduled automatic deletion, severity.
- **Restore**, three ways:
  - exclude this file only, then restore (recommended)
  - allow the threat, then restore
  - restore a copy to another folder, keeping the item quarantined
- Microsoft offers no supported way to permanently delete a single item, so do that from Protection history in Windows Security. The quarantine retention period can be changed in this window.

### Change watch, notifications, log
- **Internal change notifications** (`✓ Changed by this tool`) and **external change notifications** (`⚠ Changed externally`) can be turned on and off separately.
  External changes are detected from the Defender event log (events 5000/5001/5007). Changes made while the app was not running are reported on the next start.
- **Revert external changes**, each turned on or off separately:
  - remove exclusions added elsewhere
  - restore exclusions removed elsewhere
  - turn real-time protection back on if it was turned off elsewhere

  Reverting stops if the same target needs reverting more than 3 times within 10 minutes. Group Policy changes are never reverted.
- **View log**: filter by internal / external / failed / info entries, and search.

## Menu

As shown in English (the Korean UI has the same structure):

```
● Real-time protection: off — back on at 14:30 (25 min left)
Tamper Protection: off
─────────────
Turn &on real-time protection
Turn &off real-time protection ▸  For 30 min / 1 h / 2 h / 4 h / Until turned on
Smart&Screen ▸                    status (3 parts) / parts to turn off / turn off for… / turn back on / SmartScreen settings…
─────────────
Manage &exclusions ▸
Scan a &connected drive ▸
&Quarantine...
View &log...
─────────────
Se&ttings ▸  Scheduled scan settings... / Notifications ▸ / Revert external changes ▸ /
             Suggest a scan for new drives / Language / 언어 ▸ / Start with Windows
Open Windows &Security
&Refresh
─────────────
E&xit
```
(`&` marks the keyboard shortcut letter.)
## Where things are stored

| What | Where |
| --- | --- |
| Settings (notifications, revert options, timed-off schedule, last choices, language `Language` = auto/ko/en, …) | `HKCU\Software\DefenderTray` |
| Activity log | `%LOCALAPPDATA%\DefenderTray\activity.log` (moved to `activity.old.log` after 1 MB) |
| Start with Windows | Task Scheduler task `DefenderTray` (at logon, highest privileges) |
| Right-click menu | `HKCU\Software\Classes\exefile\shell\DefenderTrayExclude`, `...\Directory\shell\DefenderTrayExclude` |

## Source layout

| File | Contents |
| --- | --- |
| `src/Program.cs` | Entry point, command line (`--exclude-path`, `--register-startup`, `--uninstall-cleanup`, …), single instance |
| `src/TrayContext*.cs` | Tray menu: background status polling, on/off, timed off, new drives / external-change watch / exclusion menu / scans / SmartScreen |
| `src/Defender.cs` | State, settings, exclusions and scans via WMI (`root\Microsoft\Windows\Defender`) |
| `src/DefenderWatch.cs` | Event log parsing; record of changes made by this app (`SelfChanges`) |
| `src/RevertPlan.cs`, `TimedOff.cs`, `DriveWatch.cs`, `ExclusionScope.cs`, `ScanSchedule.cs` | Pure, testable logic |
| `src/ManagedPolicy.cs` | Detects organization policy (Group Policy / MDM) |
| `src/LocalGpo.cs` | Reads and writes the local Group Policy files (Registry.pol, GPT.INI) |
| `src/SmartScreen.cs`, `SmartScreenForm.cs`, `TrayContext.SmartScreen.cs` | SmartScreen state and changes, settings window, tray menu and watch |
| `src/Startup.cs` | Start-with-Windows task |
| `src/Tr.cs` | UI language. Strings are written in code as `Tr.T("Korean", "English")` |
| `src/*Form.cs`, `Dialogs.cs` | Exclusion list, scheduled scans, quarantine and log windows, plus dialogs |
| `installer/DefenderTray.iss`, `release.ps1` | Inno Setup script, release build script |
| `tests/Tests.cs` | Logic tests |

## Organization-managed PCs (Group Policy, Intune, …)

Defender settings that an organization policy sets win over changes made with this app. The app detects this (`src/ManagedPolicy.cs`) and behaves as follows:

| Where | Behavior |
| --- | --- |
| Scheduled scan settings | Policy-locked items are **disabled and show the policy value**. A list at the top shows entries such as "Day: Every day (Group Policy)". After applying, values are read back; anything that did not change is reported and logged as failed. |
| Real-time protection on/off | If policy pins the opposite state, the app does not try and says the setting is locked by organization policy. |
| Quarantine retention | Locked when set by policy; the tooltip explains why. The value is read back after applying. |
| Exclusions | If the policy "Configure local administrator merge behavior for lists" is disabled, the list window and menu warn that added exclusions will not take effect. The "hide exclusions from local admins" policy is shown as well. |

- Group Policy: `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\...` (e.g. `Scan\ScheduleDay`). If `LocalSettingOverride*` is enabled, the local setting wins, so the item is not locked.
- MDM: `HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Defender` (Policy CSP). Only values whose names are certain are mapped. Everything else is verified by reading it back after applying.
- The app has no feature to bypass organization policy. Ask your IT administrator to change it.

## Known limitations
- Watching, reverting and timed re-enable only work while the app is running. Changes made while it was not running are handled on the next start.
- Defender's logs do not record which program changed a setting, so the app cannot tell you.
- Settings managed by company Group Policy may keep the policy value even if changed here.
- Contextual exclusions ("real-time only") need Defender platform 4.18.2205.7 or later.

## License

[MIT](LICENSE)
