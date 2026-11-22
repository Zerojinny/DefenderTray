using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DefenderTray
{
    // 트레이 아이콘과 메뉴. 기능별로 나뉜 partial 파일:
    //   TrayContext.cs            상태 확인(백그라운드), 켜기/끄기, 시간 지정 끄기, 새 드라이브 감지, 메뉴
    //   TrayContext.Watch.cs      외부 변경 감시·알림·되돌리기
    //   TrayContext.Exclusions.cs 검사 제외 메뉴
    //   TrayContext.Scans.cs      수동 검사, 창 열기
    partial class TrayContext : ApplicationContext
    {

        readonly NotifyIcon tray;
        readonly ToolStripMenuItem statusItem, tamperItem, onItem, offItem, startupItem, driveSuggestItem;
        ToolStripMenuItem exclusionMenu;

        // 자주 쓰는 도구 — { 표시 이름, 실행 파일 경로 }
        static string[][] KnownTools
        {
            get
            {
                return new[]
                {
                    new[] { Tr.T("탐색기 (explorer.exe)", "File Explorer (explorer.exe)"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") },
                };
            }
        }
        readonly System.Windows.Forms.Timer timer;
        readonly Icon iconOn, iconOff, iconUnknown, iconBusy;
        RtpState current = (RtpState)(-1);
        bool busy;
        // 백그라운드 작업 결과를 UI 스레드로 넘기는 통로 (메뉴 핸들 생성 여부와 무관하게 항상 유효)
        readonly SynchronizationContext ui = new WindowsFormsSynchronizationContext();

        void OnUi(Action a)
        {
            ui.Post(_ =>
            {
                try { a(); }
                catch (Exception ex) { try { Balloon(5000, Tr.T("오류", "Error"), ex.Message, ToolTipIcon.Error); } catch { } }
            }, null);
        }

        public TrayContext()
        {
            iconOn = IconFactory.CreateTrayIcon(RtpState.On);
            iconOff = IconFactory.CreateTrayIcon(RtpState.Off);
            iconUnknown = IconFactory.CreateTrayIcon(RtpState.Unknown);
            iconBusy = IconFactory.CreateBusyIcon();

            statusItem = new ToolStripMenuItem(Tr.T("상태 확인 중...", "Checking status...")) { Enabled = false };
            tamperItem = new ToolStripMenuItem(Tr.T("변조 방지: 확인 중...", "Tamper Protection: checking..."), null, (s, e) => GuideTamper()) { Enabled = false };
            onItem = new ToolStripMenuItem(Tr.T("실시간 보호 켜기(&O)", "Turn on real-time protection(&O)"), null, (s, e) => Toggle(true, 0, null));
            offItem = BuildOffMenu();

            // --- 설정 하위 메뉴 ---
            startupItem = new ToolStripMenuItem(Tr.T("Windows 시작 시 실행(&U)", "&Start with Windows"), null, (s, e) => ToggleStartup());
            ownItem = new ToolStripMenuItem(Tr.T("내부 변경 알림(&I)", "Internal change notifications(&I)"), null, (s, e) => ToggleOwnNotify())
            {
                Checked = ownNotify,
                ToolTipText = Tr.T("이 프로그램(트레이 메뉴, 제외 목록 창, 오른쪽 클릭 메뉴)으로 바꾼 내용을 \"", "Shows changes made with this program (tray menu, exclusion list window, right-click menu) as \"") + OwnTitle + Tr.T("\" 알림으로 보여 줍니다.", "\" notifications.")
            };
            watchItem = new ToolStripMenuItem(Tr.T("외부 변경 알림(&N)", "External change notifications(&N)"), null, (s, e) => ToggleWatchNotify())
            {
                Checked = watchNotify,
                ToolTipText = Tr.T("이 프로그램이 아닌 곳에서 실시간 보호를 켜고 끄거나 검사 제외를 바꾸면 \"⚠ 외부에서 변경됨\" 알림으로 알려 줍니다.", "Notifies you with \"⚠ Changed externally\" when real-time protection or exclusions are changed outside this program.")
            };
            var notifyMenu = new ToolStripMenuItem(Tr.T("알림(&A)", "Notifications(&A)"));
            notifyMenu.DropDownItems.Add(ownItem);
            notifyMenu.DropDownItems.Add(watchItem);
            notifyMenu.DropDownItems.Add(new ToolStripSeparator());
            notifyMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("변경에 실패하면 항상 알립니다", "Failures are always notified")) { Enabled = false });

            driveSuggestItem = new ToolStripMenuItem(Tr.T("새 드라이브 연결 시 검사 제안(&D)", "Suggest a scan for new drives(&D)"), null, (s, e) => ToggleDriveSuggest())
            {
                Checked = suggestDriveScan,
                ToolTipText = Tr.T("USB 메모리나 외장 디스크를 연결하면 알림을 띄우고, 알림을 누르면 그 드라이브를 검사합니다.", "Shows a notification when a USB stick or external disk is connected; click it to scan that drive.")
            };

            var settingsMenu = new ToolStripMenuItem(Tr.T("설정(&T)", "Settings(&T)"));
            settingsMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("예약 검사 설정(&K)...", "Sc&heduled scan settings..."), null, (s, e) => ShowSchedule()));
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add(notifyMenu);
            settingsMenu.DropDownItems.Add(BuildRevertMenu());
            settingsMenu.DropDownItems.Add(driveSuggestItem);
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add(BuildLanguageMenu());
            settingsMenu.DropDownItems.Add(startupItem);
            // 설치판에서만: 트레이를 끝내면서 제거 프로그램 실행 (실행 중이라 제거가 막히는 일 없음)
            if (Program.UninstallerPath() != null)
            {
                settingsMenu.DropDownItems.Add(new ToolStripSeparator());
                settingsMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("프로그램 제거(&Z)...", "&Uninstall..."), null, (s, e) =>
                {
                    if (Program.RunUninstaller()) ExitThread();
                }));
            }
            // 작업 스케줄러 조회는 느릴 수 있어 설정 메뉴를 열 때만 확인
            settingsMenu.DropDownOpening += (s, e) => { startupItem.Checked = IsStartupRegistered(); };

            // --- 주 메뉴: 자주 쓰는 것 위주 ---
            var menu = new ContextMenuStrip { ShowItemToolTips = true };
            menu.Items.Add(statusItem);
            menu.Items.Add(tamperItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(onItem);
            menu.Items.Add(offItem);
            menu.Items.Add(BuildSmartScreenMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildExclusionMenu());
            menu.Items.Add(BuildScanMenu());
            menu.Items.Add(new ToolStripMenuItem(Tr.T("격리 항목(&Q)...", "Quarantine(&Q)..."), null, (s, e) => ShowQuarantine()));
            menu.Items.Add(new ToolStripMenuItem(Tr.T("기록 보기(&L)...", "View log(&L)..."), null, (s, e) => ShowLog()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(settingsMenu);
            menu.Items.Add(new ToolStripMenuItem(Tr.T("Windows 보안 열기(&S)", "Open Windows Security(&S)"), null, (s, e) => OpenSecurityCenter()));
            menu.Items.Add(new ToolStripMenuItem(Tr.T("새로 고침(&R)", "Refresh(&R)"), null, (s, e) => Refresh()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Tr.T("종료(&X)", "Exit(&X)"), null, (s, e) => ExitThread()));
            menu.Opening += (s, e) => { UpdateStatusText(); Refresh(); };

            tray = new NotifyIcon
            {
                Icon = iconUnknown,
                Text = Tr.T("Defender 실시간 보호", "Defender real-time protection"),
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) DoubleClickToggle(); };
            tray.BalloonTipClicked += (s, e) => OnBalloonClicked();
            instance = this;

            // 오른쪽 클릭 메뉴가 등록돼 있으면 다시 등록: 메뉴 글자를 지금 언어로, 실행 경로를 지금 exe로 맞춤
            try { if (ShellIntegration.IsRegistered()) ShellIntegration.Register(); } catch { }

            timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (s, e) => Refresh();
            timer.Start();

            Refresh();   // 첫 확인: 프로그램이 꺼져 있던 동안의 변경도 함께 처리
        }

        // ===== 상태 확인 (백그라운드) =====
        // WMI·이벤트 로그·드라이브 조회는 느릴 수 있어 백그라운드에서 하고, 결과만 UI 스레드에서 반영
        bool polling, pollAgain, firstPoll = true;
        bool tamperOn;
        // 켜기/끄기가 끝날 때마다 올림. 그 전에 시작한 확인 결과(바뀌기 전 상태)는 상태 반영에 쓰지 않음
        int toggleVersion;

        void Refresh()
        {
            if (polling) { pollAgain = true; return; }
            polling = true;
            bool startup = firstPoll;
            firstPoll = false;
            int ver = toggleVersion;
            int sver = ssVersion;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                RtpState st = RtpState.Unknown;
                bool tamper = false;
                List<DefenderChange> changes = null;
                List<DriveEntry> drives = null;
                Dictionary<SsPart, SsState> ss = null;
                try { tamper = Defender.IsTamperProtected(); } catch { }
                try { st = Defender.GetState(); } catch { }
                try { changes = DefenderWatch.ReadNew(); } catch { }
                try { drives = DriveWatch.Snapshot(); } catch { }
                try { ss = SmartScreen.GetAll(); } catch { }
                OnUi(() =>
                {
                    polling = false;
                    bool stale = ver != toggleVersion;
                    HandleSmartScreen(ss, sver != ssVersion);
                    ApplyPoll(st, tamper, changes, drives, startup, stale);
                    if (pollAgain || stale || sver != ssVersion) { pollAgain = false; Refresh(); }
                });
            });
        }

        void ApplyPoll(RtpState st, bool tamper, List<DefenderChange> changes, List<DriveEntry> drives, bool startup, bool stale)
        {
            tamperOn = tamper;
            tamperItem.Text = tamper ? Tr.T("변조 방지: 켜짐 — 끄는 방법 보기", "Tamper Protection: on — how to turn it off") : Tr.T("변조 방지: 꺼짐", "Tamper Protection: off");
            tamperItem.Enabled = tamper;
            tamperItem.ForeColor = tamper ? Color.FromArgb(218, 54, 51) : SystemColors.GrayText;

            // 이벤트·드라이브는 놓치면 안 되므로 항상 처리. 상태는 켜기/끄기 도중·직전 값이면 건너뜀
            HandleChanges(changes, startup);
            HandleDrives(drives);
            if (!busy && !stale)
            {
                ApplyState(st);
                CheckTimedOff(st);
            }
            UpdateStatusText();
        }

        void ApplyState(RtpState st)
        {
            if (st == current) return;

            // 이 프로그램이 바꾼 경우(-2)는 항상 알림. 외부 변경은 이벤트 로그 감시가 출처와 함께 알리므로,
            // 감시 알림이 꺼져 있거나 로그를 못 읽을 때만 여기서 알림
            bool fromToggle = current == (RtpState)(-2);
            bool notify = current != (RtpState)(-1) && st != RtpState.Unknown &&
                          (fromToggle || !watchNotify || !DefenderWatch.Available);
            current = st;

            switch (st)
            {
                case RtpState.On: tray.Icon = iconOn; statusItem.ForeColor = Color.FromArgb(46, 160, 67); break;
                case RtpState.Off: tray.Icon = iconOff; statusItem.ForeColor = Color.FromArgb(218, 54, 51); break;
                default: tray.Icon = iconUnknown; statusItem.ForeColor = SystemColors.GrayText; break;
            }
            onItem.Enabled = st != RtpState.On;
            offItem.Enabled = st != RtpState.Unknown;   // 꺼져 있어도 '끄는 기간'을 바꿀 수 있게

            if (notify)
            {
                if (fromToggle)
                {
                    var lines = new List<string> { st == RtpState.On ? Tr.T("실시간 보호 켜짐", "Real-time protection on") : Tr.T("실시간 보호 꺼짐", "Real-time protection off") };
                    if (toggleNote != null) lines.Add(toggleNote);
                    NotifyDone(lines);
                }
                else // 외부 변경 알림을 꺼 둔 경우 등: 출처를 모르는 단순 상태 알림
                    Balloon(2000, "Microsoft Defender",
                        st == RtpState.On ? Tr.T("실시간 보호가 켜졌습니다.", "Real-time protection is on.") : Tr.T("실시간 보호가 꺼졌습니다.", "Real-time protection is off."),
                        st == RtpState.On ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
            toggleNote = null;
        }

        // 상태 줄과 트레이 툴팁: 꺼짐이면 언제 다시 켜지는지 함께 표시
        void UpdateStatusText()
        {
            string state = current == RtpState.On ? Tr.T("켜짐", "on") : current == RtpState.Off ? Tr.T("꺼짐", "off") : Tr.T("알 수 없음", "unknown");
            if (busy) return;
            string extra = "";
            string tip = "";
            DateTime? until = TimedOff.GetUntilUtc();
            if (current == RtpState.Off && until.HasValue)
            {
                DateTime local = until.Value.ToLocalTime();
                extra = Tr.F(" — {0}에 다시 켜짐 ({1})", " — back on at {0} ({1})",
                             TimedOff.WhenLabel(local, DateTime.Now), TimedOff.RemainingLabel(until.Value, DateTime.UtcNow));
                tip = Tr.F(" ({0}에 켜짐)", " (on at {0})", TimedOff.WhenLabel(local, DateTime.Now));
            }
            statusItem.Text = Tr.T("● 실시간 보호: ", "● Real-time protection: ") + state + extra;
            string t = Tr.T("Defender 실시간 보호: ", "Defender real-time protection: ") + state + tip;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;   // NotifyIcon 툴팁은 63자 제한
        }

        // ===== 켜기 / 끄기 =====
        string toggleNote;   // 완료 알림에 덧붙일 설명 (예: "30분 뒤 다시 켜짐")

        // offMinutes: 끌 때 다시 켤 때까지의 분 (0 = 직접 켤 때까지). note: 완료 알림에 덧붙일 줄
        void Toggle(bool enable, int offMinutes, string note)
        {
            if (busy) return;

            // 조직 정책이 실시간 보호를 반대 상태로 고정했으면 시도하지 않고 이유를 알림
            PolicyValue pol = ManagedPolicy.Blocks("DisableRealtimeMonitoring", !enable);
            if (pol != null)
            {
                bool pinnedOff = pol.Value is bool && (bool)pol.Value;
                string msg = Tr.F("조직 정책({0})으로 실시간 보호가 '{1}'으로 고정되어 있어 {2} 수 없습니다. IT 관리자에게 문의하세요.",
                                  "Organization policy ({0}) keeps real-time protection {1}, so it cannot be turned {2}. Contact your IT administrator.",
                                  pol.Source,
                                  pinnedOff ? Tr.T("꺼짐", "off") : Tr.T("켜짐", "on"),
                                  enable ? Tr.T("켤", "on") : Tr.T("끌", "off"));
                ActivityLog.Write(ActivityLog.Failed, (enable ? Tr.T("실시간 보호 켜기", "Turn on real-time protection") : Tr.T("실시간 보호 끄기", "Turn off real-time protection")) + " — " + msg);
                Balloon(6000, Tr.T("변경할 수 없음", "Cannot change"), msg, ToolTipIcon.Warning);
                return;
            }

            busy = true;
            onItem.Enabled = offItem.Enabled = false;
            tray.Icon = iconBusy;
            tray.Text = enable ? Tr.T("실시간 보호 켜는 중...", "Turning on real-time protection...") : Tr.T("실시간 보호 끄는 중...", "Turning off real-time protection...");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                bool tamperBlocked = false;
                try
                {
                    Defender.SetRealtime(enable);
                    // 반영될 때까지 잠시 대기하며 확인
                    for (int i = 0; i < 10; i++)
                    {
                        Thread.Sleep(500);
                        if (Defender.GetState() == (enable ? RtpState.On : RtpState.Off)) break;
                    }
                    if (Defender.GetState() != (enable ? RtpState.On : RtpState.Off))
                    {
                        if (Defender.IsTamperProtected()) tamperBlocked = true;
                        else error = Tr.T("설정이 반영되지 않았습니다. 조직 정책(그룹 정책·Intune 등)이나 다른 보안 관리 도구가 막고 있을 수 있습니다.", "The setting did not take effect. Organization policy (Group Policy, Intune, etc.) or another security management tool may be blocking it.");
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                OnUi(() =>
                {
                    busy = false;
                    toggleVersion++;          // 진행 중이던 확인 결과는 버림
                    current = (RtpState)(-2); // 강제 갱신
                    string what = enable ? Tr.T("실시간 보호 켜기", "Turn on real-time protection") : Tr.T("실시간 보호 끄기", "Turn off real-time protection");
                    if (tamperBlocked) ActivityLog.Write(ActivityLog.Failed, what + Tr.T(" — 변조 방지가 막음", " — blocked by Tamper Protection"));
                    else if (error != null) ActivityLog.Write(ActivityLog.Failed, what + " — " + error);
                    else
                    {
                        if (enable) { TimedOff.SetUntilUtc(null); TimedOff.ActiveMinutes = null; }
                        else
                        {
                            TimedOff.SetUntilUtc(offMinutes > 0 ? (DateTime?)DateTime.UtcNow.AddMinutes(offMinutes) : null);
                            TimedOff.ActiveMinutes = offMinutes;
                        }
                        ActivityLog.Write(ActivityLog.Internal, (enable ? Tr.T("실시간 보호 켜짐", "Real-time protection on") : Tr.T("실시간 보호 꺼짐", "Real-time protection off")) +
                            (!enable && offMinutes > 0 ? " (" + TimedOff.DurationLabel(offMinutes) + Tr.T(" 뒤 다시 켬)", ", then back on)") : "") +
                            (note != null ? " — " + note : ""));
                        toggleNote = !enable && offMinutes > 0
                            ? Tr.F("{0} 뒤({1}) 다시 켜집니다", "Turns back on in {0} (at {1})",
                                   TimedOff.DurationLabel(offMinutes), TimedOff.WhenLabel(DateTime.Now.AddMinutes(offMinutes), DateTime.Now))
                            : note;
                    }
                    Refresh();
                    if (tamperBlocked) GuideTamper();
                    else if (error != null)
                        Balloon(5000, Tr.T("변경 실패", "Change failed"), error, ToolTipIcon.Error);
                });
            });
        }

        // ===== 시간 지정 끄기 =====
        ToolStripMenuItem BuildOffMenu()
        {
            var m = new ToolStripMenuItem(Tr.T("실시간 보호 끄기(&F)", "Turn off real-time protection(&F)"));
            foreach (int minutes in TimedOff.Choices)
            {
                if (minutes == 0) m.DropDownItems.Add(new ToolStripSeparator());
                int min = minutes;
                string text = minutes == 0 ? Tr.T("직접 켤 때까지 끄기(&N)", "Until turned on(&N)") : Tr.F("{0} 동안 끄기", "For {0}", TimedOff.DurationLabel(minutes));
                m.DropDownItems.Add(new ToolStripMenuItem(text, null, (s, e) => TurnOffFor(min)) { Tag = min });
            }
            // 실제로 그 기간을 골라 꺼져 있을 때만 체크 표시
            m.DropDownOpening += (s, e) =>
            {
                bool off = current == RtpState.Off;
                int? active = TimedOff.ActiveMinutes;
                foreach (ToolStripItem it in m.DropDownItems)
                {
                    var mi = it as ToolStripMenuItem;
                    if (mi == null || !(mi.Tag is int)) continue;
                    int min = (int)mi.Tag;
                    mi.Checked = TimedOff.IsChecked(min, off, active);
                    mi.ToolTipText = off
                        ? (min == 0 ? Tr.T("자동으로 다시 켜지 않습니다", "It will not turn back on automatically") : Tr.T("지금부터 ", "Change it to turn back on ") + TimedOff.DurationLabel(min) + Tr.T(" 뒤 다시 켜지도록 바꿉니다", " from now"))
                        : null;
                }
                m.ToolTipText = Tr.T("트레이 아이콘을 더블클릭해 끌 때는 마지막으로 고른 기간(", "Double-clicking the tray icon to turn off uses the last chosen duration (") + TimedOff.DurationLabel(TimedOff.LastMinutes) + Tr.T(")을 씁니다", ")");
            };
            return m;
        }

        void TurnOffFor(int minutes)
        {
            TimedOff.LastMinutes = minutes;   // 더블클릭 기본값으로 기억
            if (current == RtpState.Off)
            {
                // 이미 꺼져 있으면 다시 켜질 시각만 바꿈 (진행 중인 확인 결과로 예약이 지워지지 않도록 버전 올림)
                toggleVersion++;
                TimedOff.SetUntilUtc(minutes > 0 ? (DateTime?)DateTime.UtcNow.AddMinutes(minutes) : null);
                TimedOff.ActiveMinutes = minutes;
                string line = minutes > 0
                    ? Tr.T("실시간 보호: ", "Real-time protection: back on in ") + TimedOff.DurationLabel(minutes) + Tr.T(" 뒤(", " (at ") + TimedOff.WhenLabel(DateTime.Now.AddMinutes(minutes), DateTime.Now) + Tr.T(") 다시 켜짐", ")")
                    : Tr.T("실시간 보호: 직접 켤 때까지 꺼 둠", "Real-time protection: off until turned on manually");
                ActivityLog.Write(ActivityLog.Internal, line);
                NotifyDone(new[] { line });
                UpdateStatusText();
                return;
            }
            Toggle(false, minutes, null);
        }

        void DoubleClickToggle()
        {
            RtpState st = current == RtpState.On || current == RtpState.Off ? current : Defender.GetState();
            if (st == RtpState.Unknown) return;
            bool enable = st == RtpState.Off;
            int minutes = TimedOff.LastMinutes;
            string q = enable
                ? Tr.T("실시간 보호를 켜시겠습니까?", "Turn on real-time protection?")
                : Tr.T("실시간 보호를 끄시겠습니까?\r\n끄면 PC가 위협에 노출됩니다.\r\n\r\n", "Turn off real-time protection?\r\nThe PC will be exposed to threats while it is off.\r\n\r\n") +
                  (minutes > 0 ? Tr.F("{0} 뒤 자동으로 다시 켜집니다.", "It turns back on automatically in {0}.", TimedOff.DurationLabel(minutes)) : Tr.T("직접 켤 때까지 꺼 둡니다.", "It stays off until you turn it back on.")) +
                  Tr.T("\r\n(기간은 메뉴의 '실시간 보호 끄기'에서 고를 수 있습니다)", "\r\n(Choose the duration under 'Turn off real-time protection' in the menu)");
            var r = MessageBox.Show(q, Tr.T("Defender 실시간 보호", "Defender real-time protection"),
                MessageBoxButtons.YesNo,
                enable ? MessageBoxIcon.Question : MessageBoxIcon.Warning,
                enable ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            if (enable) Toggle(true, 0, null);
            else Toggle(false, minutes, null);
        }

        // 예약 시각이 지났으면 다시 켬. 그 사이 다른 곳에서 켜졌으면 예약만 지움
        void CheckTimedOff(RtpState st)
        {
            if (busy) return;
            // 다른 곳에서 켜졌으면 예약과 체크 표시를 지움
            if (st == RtpState.On && (TimedOff.ActiveMinutes.HasValue || TimedOff.GetUntilUtc().HasValue))
            {
                TimedOff.SetUntilUtc(null);
                TimedOff.ActiveMinutes = null;
                return;
            }
            DateTime? until = TimedOff.GetUntilUtc();
            if (!until.HasValue) return;
            if (st != RtpState.Off || !TimedOff.IsDue(until, DateTime.UtcNow)) return;

            TimedOff.SetUntilUtc(null);
            ActivityLog.Write(ActivityLog.Info, Tr.T("예약한 시간이 되어 실시간 보호를 다시 켭니다", "The scheduled time has come; turning real-time protection back on"));
            Toggle(true, 0, Tr.T("예약한 시간이 되어 다시 켰습니다", "Turned back on at the scheduled time"));
        }

        // ===== 새 드라이브 연결 시 검사 제안 =====
        static string DriveTitle { get { return Tr.T("새 드라이브 연결됨", "New drive connected"); } }
        bool suggestDriveScan = AppSettings.Get("SuggestDriveScan") != "0";
        HashSet<string> knownDrives;          // null = 아직 기준 목록 없음
        List<string> pendingDriveScan;        // 알림을 누르면 검사할 드라이브

        void HandleDrives(List<DriveEntry> drives)
        {
            if (drives == null) return;
            var roots = new List<string>();
            foreach (DriveEntry d in drives) roots.Add(d.Root);
            if (knownDrives == null) { knownDrives = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase); return; }

            List<DriveEntry> added = DriveWatch.Added(knownDrives, drives);
            knownDrives = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);   // 빠진 드라이브는 다시 꽂으면 또 알림
            if (added.Count == 0) return;

            var lines = new List<string>();
            pendingDriveScan = new List<string>();
            foreach (DriveEntry d in added)
            {
                lines.Add(d.Describe());
                pendingDriveScan.Add(d.Root);
                ActivityLog.Write(ActivityLog.Info, Tr.T("새 드라이브 연결: ", "New drive connected: ") + d.Describe());
            }
            if (!suggestDriveScan) return;
            string tail = current == RtpState.Off
                ? Tr.T("실시간 보호가 꺼져 있습니다. 클릭하면 검사합니다.", "Real-time protection is off. Click to scan.")
                : Tr.T("클릭하면 검사합니다.", "Click to scan.");
            Balloon(10000, DriveTitle, Summarize(lines, tail), current == RtpState.Off ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void ToggleDriveSuggest()
        {
            suggestDriveScan = !suggestDriveScan;
            driveSuggestItem.Checked = suggestDriveScan;
            AppSettings.Set("SuggestDriveScan", suggestDriveScan ? "1" : "0");
            Balloon(2500, Tr.T("새 드라이브 검사 제안", "New drive scan suggestion"),
                suggestDriveScan ? Tr.T("USB나 외장 디스크를 연결하면 검사할지 알려 드립니다.", "You'll be asked whether to scan when a USB stick or external disk is connected.") : Tr.T("새 드라이브 검사 제안을 껐습니다.", "New drive scan suggestions turned off."),
                ToolTipIcon.Info);
        }

        void OnBalloonClicked()
        {
            if (lastBalloonTitle == DriveTitle && pendingDriveScan != null && pendingDriveScan.Count > 0)
            {
                var targets = pendingDriveScan;
                pendingDriveScan = null;
                StartScans(targets);
                return;
            }
            ShowWatchDetail();
        }

        // ===== 화면 언어 =====
        // 메뉴 이름은 두 언어를 함께 적어, 모르는 언어로 바뀌어도 찾을 수 있게 함. 언어 이름은 각 언어로 표시.
        ToolStripMenuItem BuildLanguageMenu()
        {
            var m = new ToolStripMenuItem("Language / 언어(&L)");
            string sys = Tr.SystemIsKorean() ? "한국어" : "English";
            var items = new[]
            {
                new KeyValuePair<string, string>(Tr.Auto, Tr.T("자동 (시스템: ", "Automatic (system: ") + sys + ")"),
                new KeyValuePair<string, string>(Tr.Korean, "한국어"),
                new KeyValuePair<string, string>(Tr.English, "English"),
            };
            foreach (var kv in items)
            {
                string value = kv.Key;
                var it = new ToolStripMenuItem(kv.Value, null, (s, e) => ChangeLanguage(value)) { Checked = Tr.Setting == value };
                m.DropDownItems.Add(it);
            }
            return m;
        }

        void ChangeLanguage(string setting)
        {
            if (setting == Tr.Setting) return;
            bool newKo = Tr.Resolve(setting, Tr.SystemIsKorean());
            Tr.Setting = setting;
            ActivityLog.Write(ActivityLog.Info, "Language → " + setting);
            if (newKo == Tr.Ko) return;   // 표시 언어가 그대로면 다시 시작할 필요 없음 (예: 자동 → 한국어, 시스템이 한국어)

            // 메뉴·창이 이미 만들어져 있으므로 다시 시작해서 반영
            var r = MessageBox.Show(
                newKo ? "언어를 한국어로 바꾸려면 프로그램을 다시 시작해야 합니다. 지금 다시 시작할까요?"
                      : "The program needs to restart to switch to English. Restart now?",
                AppInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Restart();
        }

        void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restarted " + Process.GetCurrentProcess().Id) { UseShellExecute = true });
                ExitThread();
            }
            catch (Exception ex)
            {
                Balloon(5000, AppInfo.DisplayName, ex.Message, ToolTipIcon.Error);
            }
        }

        // ===== 기타 =====
        static void OpenSecurityCenter()
        {
            try { Process.Start("windowsdefender://threat"); } catch { }
        }

        static void OpenTamperSettings()
        {
            // 바이러스 및 위협 방지 '설정 관리' 화면 (변조 방지 토글 위치)
            try { Process.Start("windowsdefender://threatsettings"); }
            catch { try { Process.Start("windowsdefender://"); } catch { } }
        }

        void GuideTamper()
        {
            string msg =
                Tr.T("변조 방지(Tamper Protection)가 켜져 있습니다.\r\n", "Tamper Protection is on.\r\n") +
                Tr.T("이 기능이 켜져 있으면 어떤 프로그램·명령어·정책으로도 실시간 보호를 끌 수 없습니다. ", "While it is on, no program, command or policy can turn off real-time protection. ") +
                Tr.T("설계상 막혀 있어 우회할 방법이 없으며, 사람이 직접 꺼야 합니다.\r\n\r\n", "This is blocked by design and cannot be bypassed; a person has to turn it off.\r\n\r\n") +
                Tr.T("끄는 방법:\r\n", "How to turn it off:\r\n") +
                Tr.T("  1. [확인]을 누르면 'Windows 보안 → 바이러스 및 위협 방지 설정' 화면이 열립니다.\r\n", "  1. Click [OK] to open 'Windows Security → Virus & threat protection settings'.\r\n") +
                Tr.T("  2. '바이러스 및 위협 방지 설정'의 [설정 관리]를 엽니다.\r\n", "  2. Under 'Virus & threat protection settings', open [Manage settings].\r\n") +
                Tr.T("  3. '변조 방지' 토글을 끕니다. (UAC 확인이 나올 수 있습니다)\r\n", "  3. Turn off the 'Tamper Protection' toggle. (A UAC prompt may appear)\r\n") +
                Tr.T("  4. 그 뒤 이 프로그램으로 실시간 보호를 켜고 끌 수 있습니다.\r\n\r\n", "  4. After that, this program can turn real-time protection on and off.\r\n\r\n") +
                Tr.T("참고: 회사·학교처럼 조직에서 관리하는 PC는 이 토글이 비활성화되어 끌 수 없을 수 있습니다. ", "Note: on PCs managed by an organization, such as a company or school, this toggle may be disabled. ") +
                Tr.T("그 경우 관리자만 정책으로 변경할 수 있습니다.", "In that case only an administrator can change it by policy.");

            var r = MessageBox.Show(msg, Tr.T("변조 방지 끄기 안내", "How to turn off Tamper Protection"),
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r == DialogResult.OK) OpenTamperSettings();
        }

        static bool IsStartupRegistered() { return Startup.IsRegistered(); }

        void ToggleStartup()
        {
            try
            {
                if (Startup.IsRegistered()) Startup.Unregister();
                else Startup.Register();
            }
            catch (Exception ex)
            {
                Balloon(4000, Tr.T("시작 프로그램", "Startup"), ex.Message, ToolTipIcon.Error);
            }
            startupItem.Checked = Startup.IsRegistered();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            iconOn.Dispose(); iconOff.Dispose(); iconUnknown.Dispose(); iconBusy.Dispose();
            base.ExitThreadCore();
        }
    }
}
