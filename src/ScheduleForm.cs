using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DefenderTray
{
    // 예약 검사 설정 창
    class ScheduleForm : Form
    {
        // 언어 설정(Tr.Init) 뒤에 읽히도록 필드 대신 속성으로
        static string[] DayNames { get { return new[] { Tr.T("매일", "Every day"), Tr.T("일요일", "Sunday"), Tr.T("월요일", "Monday"), Tr.T("화요일", "Tuesday"), Tr.T("수요일", "Wednesday"), Tr.T("목요일", "Thursday"), Tr.T("금요일", "Friday"), Tr.T("토요일", "Saturday"), Tr.T("예약 검사 안 함", "No scheduled scan") }; } }
        static readonly string[] Props =
        {
            "ScanParameters", "ScanScheduleDay", "ScanScheduleTime", "ScanOnlyIfIdleEnabled",
            "DisableCatchupQuickScan", "DisableCatchupFullScan", "CheckForSignaturesBeforeRunningScan",
            "ScanAvgCPULoadFactor", "DisableRemovableDriveScanning", "DisableScanningMappedNetworkDrivesForFullScan"
        };

        readonly ComboBox cbType, cbDay;
        readonly DateTimePicker dtTime;
        readonly NumericUpDown nuCpu;
        readonly CheckBox chkGpo;
        int? loadedGpo;   // 로컬 그룹 정책(Registry.pol)에 저장된 '예약된 검사를 실행할 요일 지정' 값
        const string GpoScanKey = @"Software\Policies\Microsoft\Windows Defender\Scan";
        readonly CheckBox chkEnabled, chkIdle, chkCatchQuick, chkCatchFull, chkSig, chkRemovable, chkNetwork;
        readonly Label lblQuick, lblFull, lblPolicy, lblResult;
        readonly Button btnApply;
        Dictionary<string, object> loaded = new Dictionary<string, object>();
        bool loading;

        public ScheduleForm()
        {
            Text = Tr.T("예약 검사 설정", "Scheduled scan settings");
            Font = new Font("Malgun Gothic", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 도킹하지 않은 컨트롤에는 폼 Padding이 적용되지 않으므로 위치로 여백을 줌
            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(12, 10) };

            // 조직 정책(그룹 정책 / MDM)으로 고정된 항목 안내 — LoadValues에서 채움
            lblPolicy = new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 8),
                ForeColor = Color.FromArgb(196, 43, 28), Visible = false
            };
            root.Controls.Add(lblPolicy);

            // --- 예약 검사 ---
            var g1 = NewGroup(Tr.T("예약 검사", "Scheduled scan"));
            var t1 = NewTable();
            chkEnabled = new CheckBox { AutoSize = true, Text = Tr.T("예약 검사 사용(&S)", "Use scheduled scans(&S)"), Margin = new Padding(3, 3, 3, 8), Font = new Font(Font, FontStyle.Bold) };
            t1.Controls.Add(chkEnabled);
            t1.SetColumnSpan(chkEnabled, 2);
            // gpedit '예약된 검사를 실행할 요일 지정'과 같은 로컬 그룹 정책으로도 설정
            chkGpo = new CheckBox { AutoSize = true, Text = Tr.T("그룹 정책(gpedit)으로도 설정(&G)", "Also set via Group Policy (gpedit)(&G)"), Margin = new Padding(22, 0, 3, 8) };
            t1.Controls.Add(chkGpo);
            t1.SetColumnSpan(chkGpo, 2);
            policyTips.SetToolTip(chkGpo,
                Tr.T("gpedit.msc의 '컴퓨터 구성 > 관리 템플릿 > Windows 구성 요소 > Microsoft Defender 바이러스 백신 > 스캔 >\r\n", "Sets gpedit.msc 'Computer Configuration > Administrative Templates > Windows Components > Microsoft Defender Antivirus > Scan >\r\n") +
                Tr.T("예약된 검사를 실행할 요일 지정'을 '사용'으로 설정합니다 (위 체크에 따라 요일 또는 '안 함').\r\n", "Specify the day of the week to run a scheduled scan' to 'Enabled' (with the day, or 'Never', depending on the checkbox above).\r\n") +
                Tr.T("정책은 일반 설정보다 우선하므로 다른 곳에서 바꿔도 유지됩니다. 체크를 풀면 '구성되지 않음'으로 되돌립니다.", "Policy wins over the regular setting, so it stays even if changed elsewhere. Unchecking returns it to 'Not configured'."));
            cbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            cbType.Items.AddRange(new object[] { Tr.T("빠른 검사", "Quick scan"), Tr.T("전체 검사", "Full scan") });
            cbDay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            for (int i = 0; i <= 7; i++) cbDay.Items.Add(DayNames[i]);   // '안 함'은 위 체크박스로
            dtTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 100 };
            nuCpu = new NumericUpDown { Minimum = 5, Maximum = 100, Increment = 5, Width = 70 };
            AddRow(t1, Tr.T("검사 종류(&T):", "Scan type(&T):"), cbType, null);
            AddRow(t1, Tr.T("요일(&D):", "Day(&D):"), cbDay, null);
            AddRow(t1, Tr.T("시각(&M):", "Time(&M):"), dtTime, null);
            AddRow(t1, Tr.T("CPU 사용 한도(&P):", "CPU limit(&P):"), nuCpu, Tr.T("%  (검사 중 평균 CPU 사용률 상한)", "%  (maximum average CPU usage during a scan)"));
            chkIdle = new CheckBox { AutoSize = true, Text = Tr.T("컴퓨터를 사용하지 않을 때만 검사(&I)", "Scan only when the computer is not in use(&I)") };
            chkCatchQuick = new CheckBox { AutoSize = true, Text = Tr.T("놓친 빠른 검사를 다음에 실행(&Q)", "Run missed quick scans later(&Q)") };
            chkCatchFull = new CheckBox { AutoSize = true, Text = Tr.T("놓친 전체 검사를 다음에 실행(&L)", "Run missed full scans later(&L)") };
            chkSig = new CheckBox { AutoSize = true, Text = Tr.T("검사 전에 보안 인텔리전스 업데이트 확인(&U)", "Check for security intelligence updates before scanning(&U)") };
            foreach (var c in new Control[] { chkIdle, chkCatchQuick, chkCatchFull, chkSig })
            {
                t1.Controls.Add(c);
                t1.SetColumnSpan(c, 2);
            }
            g1.Controls.Add(t1);
            root.Controls.Add(g1);

            // --- 전체 검사 범위 ---
            var g2 = NewGroup(Tr.T("전체 검사 범위", "Full scan scope"));
            var t2 = NewTable();
            chkRemovable = new CheckBox { AutoSize = true, Text = Tr.T("이동식 드라이브(USB 등) 포함(&R)", "Include removable drives (USB, etc.)(&R)") };
            chkNetwork = new CheckBox { AutoSize = true, Text = Tr.T("연결된 네트워크 드라이브 포함(&N)", "Include mapped network drives(&N)") };
            foreach (var c in new Control[] { chkRemovable, chkNetwork }) { t2.Controls.Add(c); t2.SetColumnSpan(c, 2); }
            var note2 = new Label
            {
                AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 0),
                Text = Tr.T("실시간 보호가 켜져 있으면 이 설정과 관계없이 파일을 열거나 복사할 때 검사합니다.", "When real-time protection is on, files are scanned when opened or copied regardless of this setting.")
            };
            t2.Controls.Add(note2); t2.SetColumnSpan(note2, 2);
            g2.Controls.Add(t2);
            root.Controls.Add(g2);

            // --- 검사 기록 ---
            var g3 = NewGroup(Tr.T("최근 검사", "Recent scans"));
            var t3 = NewTable();
            lblQuick = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            lblFull = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            AddRow(t3, Tr.T("마지막 빠른 검사:", "Last quick scan:"), lblQuick, null);
            AddRow(t3, Tr.T("마지막 전체 검사:", "Last full scan:"), lblFull, null);
            var scanNow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            var btnQuick = new Button { Text = Tr.T("지금 빠른 검사", "Quick scan now"), AutoSize = true };
            var btnFull = new Button { Text = Tr.T("지금 전체 검사", "Full scan now"), AutoSize = true };
            btnQuick.Click += (s, e) => ScanNow(1);
            btnFull.Click += (s, e) => ScanNow(2);
            scanNow.Controls.Add(btnQuick);
            scanNow.Controls.Add(btnFull);
            t3.Controls.Add(scanNow); t3.SetColumnSpan(scanNow, 2);
            g3.Controls.Add(t3);
            root.Controls.Add(g3);

            var note = new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 8),
                Text = Tr.T("예약 검사는 Windows 자동 유지 관리와 함께 실행되므로, 정한 시각에 PC가 꺼져 있거나 사용 중이면 늦어질 수 있습니다. ", "Scheduled scans run with Windows automatic maintenance, so they may run late if the PC is off or in use at the set time. ") +
                       Tr.T("실제로 실행됐는지는 위 '최근 검사'에서 확인하세요.", "Check 'Recent scans' above to see whether they actually ran.")
            };
            root.Controls.Add(note);

            // --- 아래 버튼 ---
            lblResult = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 0) };
            btnApply = new Button { Text = Tr.T("적용(&A)", "Apply(&A)"), AutoSize = true, Enabled = false };
            var btnClose = new Button { Text = Tr.T("닫기", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            btnApply.Click += (s, e) => Apply();
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;
            var bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(lblResult);
            root.Controls.Add(bottom);

            Controls.Add(root);

            // 값이 바뀌면 [적용] 활성화
            EventHandler changed = (s, e) => { if (!loading) { btnApply.Enabled = true; lblResult.Text = ""; } };
            cbType.SelectedIndexChanged += changed;
            cbDay.SelectedIndexChanged += changed;
            dtTime.ValueChanged += changed;
            nuCpu.ValueChanged += changed;
            foreach (var c in new[] { chkGpo, chkEnabled, chkIdle, chkCatchQuick, chkCatchFull, chkSig, chkRemovable, chkNetwork }) c.CheckedChanged += changed;
            chkEnabled.CheckedChanged += (s, e) => UpdateEnabledState();

            LoadValues();
        }

        static GroupBox NewGroup(string text)
        {
            return new GroupBox { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(560, 0) };
        }

        static TableLayoutPanel NewTable()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        static void AddRow(TableLayoutPanel t, string label, Control c, string suffix)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) });
            if (suffix == null) { c.Anchor = AnchorStyles.Left; t.Controls.Add(c); return; }
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            p.Controls.Add(c);
            p.Controls.Add(new Label { Text = suffix, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 6, 0, 0) });
            t.Controls.Add(p);
        }

        static bool B(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
        static int N(Dictionary<string, object> d, string k, int def) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : def; }
        static TimeSpan T(Dictionary<string, object> d, string k)
        {
            object v;
            try { if (d.TryGetValue(k, out v) && v is string) return ManagementDateTimeConverter.ToTimeSpan((string)v); } catch { }
            return new TimeSpan(2, 0, 0);
        }

        void LoadValues()
        {
            loading = true;
            loaded = Defender.ReadProps("MSFT_MpPreference", Props);
            bool ok = loaded.Count > 0 && loaded["ScanParameters"] != null;

            cbType.SelectedIndex = N(loaded, "ScanParameters", 1) == 2 ? 1 : 0;
            int day = N(loaded, "ScanScheduleDay", 0);
            chkEnabled.Checked = ScanSchedule.IsEnabled(day);
            loadedGpo = LocalGpo.GetDword(GpoScanKey, "ScheduleDay");
            chkGpo.Checked = loadedGpo.HasValue;
            cbDay.SelectedIndex = ScanSchedule.DayIndexFor(day, ScanSchedule.RememberedDay);
            dtTime.Value = DateTime.Today + T(loaded, "ScanScheduleTime");
            nuCpu.Value = Math.Max(5, Math.Min(100, N(loaded, "ScanAvgCPULoadFactor", 50)));
            chkIdle.Checked = B(loaded, "ScanOnlyIfIdleEnabled");
            chkCatchQuick.Checked = !B(loaded, "DisableCatchupQuickScan");
            chkCatchFull.Checked = !B(loaded, "DisableCatchupFullScan");
            chkSig.Checked = B(loaded, "CheckForSignaturesBeforeRunningScan");
            chkRemovable.Checked = !B(loaded, "DisableRemovableDriveScanning");
            chkNetwork.Checked = !B(loaded, "DisableScanningMappedNetworkDrivesForFullScan");
            UpdateEnabledState();

            RefreshScanTimes();

            ApplyPolicyLocks();
            btnApply.Enabled = false;
            lblResult.ForeColor = SystemColors.ControlText;
            lblResult.Text = ok ? "" : Tr.T("설정을 읽지 못했습니다 (관리자 권한 필요)", "Could not read the settings (administrator rights required)");
            loading = false;
        }

        // 예약 검사를 끄면 예약에만 쓰이는 항목을 흐리게 (CPU 한도와 전체 검사 범위는 '지금 검사'에도 쓰여 그대로 둠)
        void UpdateEnabledState()
        {
            bool on = chkEnabled.Checked;
            foreach (Control c in new Control[] { cbType, cbDay, dtTime, chkIdle, chkCatchQuick, chkCatchFull, chkSig })
                c.Enabled = on && !locked.Contains(c);
            foreach (Control c in new Control[] { chkEnabled, chkGpo, nuCpu, chkRemovable, chkNetwork })
                c.Enabled = !locked.Contains(c);
        }

        // ===== 조직 정책으로 고정된 항목 =====
        readonly HashSet<Control> locked = new HashSet<Control>();
        readonly ToolTip policyTips = new ToolTip();

        // 설정 이름 → 화면 컨트롤
        Dictionary<string, Control[]> ControlsFor()
        {
            return new Dictionary<string, Control[]>
            {
                { "ScanParameters", new Control[] { cbType } },
                { "ScanScheduleDay", new Control[] { chkEnabled, cbDay, chkGpo } },
                { "ScanScheduleTime", new Control[] { dtTime } },
                { "ScanAvgCPULoadFactor", new Control[] { nuCpu } },
                { "ScanOnlyIfIdleEnabled", new Control[] { chkIdle } },
                { "DisableCatchupQuickScan", new Control[] { chkCatchQuick } },
                { "DisableCatchupFullScan", new Control[] { chkCatchFull } },
                { "CheckForSignaturesBeforeRunningScan", new Control[] { chkSig } },
                { "DisableRemovableDriveScanning", new Control[] { chkRemovable } },
                { "DisableScanningMappedNetworkDrivesForFullScan", new Control[] { chkNetwork } },
            };
        }

        // 잠긴 항목에는 로컬 값이 아니라 실제로 쓰이는 정책 값을 보여 줌.
        // loaded도 정책 값으로 맞춰서 [적용] 때 바뀐 항목으로 잘못 잡히지 않게 함
        void ShowPolicyValue(string key, object v)
        {
            try
            {
                switch (key)
                {
                    case "ScanParameters": cbType.SelectedIndex = Convert.ToInt32(v) == 2 ? 1 : 0; loaded[key] = (byte)Convert.ToInt32(v); break;
                    case "ScanScheduleDay":
                        int d = Convert.ToInt32(v);
                        chkEnabled.Checked = ScanSchedule.IsEnabled(d);
                        cbDay.SelectedIndex = ScanSchedule.DayIndexFor(d, ScanSchedule.RememberedDay);
                        loaded[key] = (byte)d; break;
                    case "ScanScheduleTime":
                        dtTime.Value = DateTime.Today + (TimeSpan)v;
                        loaded[key] = ManagementDateTimeConverter.ToDmtfTimeInterval((TimeSpan)v); break;
                    case "ScanAvgCPULoadFactor":
                        nuCpu.Value = Math.Max(nuCpu.Minimum, Math.Min(nuCpu.Maximum, Convert.ToInt32(v)));
                        loaded[key] = (byte)Convert.ToInt32(v); break;
                    case "ScanOnlyIfIdleEnabled": chkIdle.Checked = (bool)v; loaded[key] = v; break;
                    case "DisableCatchupQuickScan": chkCatchQuick.Checked = !(bool)v; loaded[key] = v; break;
                    case "DisableCatchupFullScan": chkCatchFull.Checked = !(bool)v; loaded[key] = v; break;
                    case "CheckForSignaturesBeforeRunningScan": chkSig.Checked = (bool)v; loaded[key] = v; break;
                    case "DisableRemovableDriveScanning": chkRemovable.Checked = !(bool)v; loaded[key] = v; break;
                    case "DisableScanningMappedNetworkDrivesForFullScan": chkNetwork.Checked = !(bool)v; loaded[key] = v; break;
                }
            }
            catch { }
        }

        // 정책이 고정한 항목은 잠그고, 무엇이 어떤 값으로 고정됐는지 위쪽에 표시
        void ApplyPolicyLocks()
        {
            locked.Clear();
            var lines = new List<string>();
            foreach (var kv in ControlsFor())
            {
                PolicyValue p = ManagedPolicy.Get(kv.Key);
                if (p == null) continue;
                // 이 PC의 로컬 그룹 정책(gpedit)에서 온 요일 값이면 조직 정책이 아니므로 잠그지 않고 값만 보여 줌
                if (kv.Key == "ScanScheduleDay" && loadedGpo.HasValue && ManagedPolicy.SameValue(p.Value, loadedGpo.Value))
                {
                    // 화면은 정책 값으로, 비교 기준(loaded)은 실제 일반 설정값 그대로 둠.
                    // 그래야 정책만 해제할 때도 일반 설정이 화면 값으로 함께 저장되어 '해제 후 다시 켜짐'이 없음
                    object real;
                    bool had = loaded.TryGetValue(kv.Key, out real);
                    ShowPolicyValue(kv.Key, p.Value);
                    if (had) loaded[kv.Key] = real; else loaded.Remove(kv.Key);
                    continue;
                }
                string desc = p.Value == null ? kv.Key : Describe(kv.Key, p.Value).Replace(" → ", ": ");
                lines.Add("· " + desc + "  (" + p.Source + ")");
                if (p.Value != null) ShowPolicyValue(kv.Key, p.Value);
                foreach (Control c in kv.Value)
                {
                    locked.Add(c);
                    policyTips.SetToolTip(c, Tr.T("조직 정책(", "Locked by organization policy (") + p.Source + Tr.T(")으로 고정된 항목입니다: ", "): ") + desc);
                }
            }
            if (lines.Count > 0)
                lblPolicy.Text = Tr.T("다음 항목은 조직 정책으로 고정되어 있어 여기서 바꿀 수 없습니다. 바꾸려면 IT 관리자에게 문의하세요.\r\n", "These items are locked by organization policy and cannot be changed here. Contact your IT administrator to change them.\r\n") +
                                 string.Join("\r\n", lines.ToArray());
            else if (ManagedPolicy.MdmManaged())
                lblPolicy.Text = Tr.T("이 PC는 MDM(Intune 등)으로 관리되고 있습니다. 일부 항목은 바꿔도 정책 값이 우선할 수 있으니, 적용한 뒤 결과를 확인하세요.", "This PC is managed by MDM (Intune, etc.). Some changes may be overridden by policy, so check the result after applying.");
            lblPolicy.Visible = lines.Count > 0 || ManagedPolicy.MdmManaged();
            UpdateEnabledState();
        }

        public void RefreshScanTimes()
        {
            var st = Defender.ReadProps("MSFT_MpComputerStatus", "QuickScanEndTime", "FullScanEndTime");
            lblQuick.Text = When(st, "QuickScanEndTime");
            lblFull.Text = When(st, "FullScanEndTime");
        }

        static string When(Dictionary<string, object> d, string k)
        {
            object v;
            try
            {
                if (d.TryGetValue(k, out v) && v is string && ((string)v).Length > 0)
                {
                    DateTime t = ManagementDateTimeConverter.ToDateTime((string)v);
                    if (t.Year > 2000)
                    {
                        int days = (int)(DateTime.Now - t).TotalDays;
                        return t.ToString("yyyy-MM-dd HH:mm") + (days == 0 ? Tr.T("  (오늘)", "  (today)")
                                         : Tr.F("  ({0}일 전)", days == 1 ? "  ({0} day ago)" : "  ({0} days ago)", days));
                    }
                }
            }
            catch { }
            return Tr.T("실행한 적 없음", "Never run");
        }

        void Apply()
        {
            var want = new Dictionary<string, object>
            {
                { "ScanParameters", (byte)(cbType.SelectedIndex == 1 ? 2 : 1) },
                { "ScanScheduleDay", (byte)ScanSchedule.ToSetting(chkEnabled.Checked, cbDay.SelectedIndex) },
                { "ScanScheduleTime", new TimeSpan(dtTime.Value.Hour, dtTime.Value.Minute, 0) },
                { "ScanAvgCPULoadFactor", (byte)nuCpu.Value },
                { "ScanOnlyIfIdleEnabled", chkIdle.Checked },
                { "DisableCatchupQuickScan", !chkCatchQuick.Checked },
                { "DisableCatchupFullScan", !chkCatchFull.Checked },
                { "CheckForSignaturesBeforeRunningScan", chkSig.Checked },
                { "DisableRemovableDriveScanning", !chkRemovable.Checked },
                { "DisableScanningMappedNetworkDrivesForFullScan", !chkNetwork.Checked },
            };

            // 바뀐 값만 보냄
            var changes = new Dictionary<string, object>();
            var lines = new List<string>();
            foreach (var kv in want)
            {
                object old;
                loaded.TryGetValue(kv.Key, out old);
                bool same;
                if (kv.Value is TimeSpan) same = T(loaded, kv.Key) == (TimeSpan)kv.Value;
                else if (kv.Value is bool) same = old is bool && (bool)old == (bool)kv.Value;
                else same = old != null && Convert.ToInt32(old) == Convert.ToInt32(kv.Value);
                if (same) continue;
                changes[kv.Key] = kv.Value;
                if (kv.Key == "ScanScheduleDay")
                {
                    int newDay = Convert.ToInt32(kv.Value), oldDay = old == null ? 0 : Convert.ToInt32(old);
                    lines.Add(!ScanSchedule.IsEnabled(newDay) ? Tr.T("예약 검사 → 끔", "Scheduled scan → off")
                        : !ScanSchedule.IsEnabled(oldDay) ? Tr.T("예약 검사 → 켬 (", "Scheduled scan → on (") + DayNames[newDay] + ")"
                        : Describe(kv.Key, kv.Value));
                }
                else lines.Add(Describe(kv.Key, kv.Value));
            }
            // 끌 때는 지금 요일을 기억해 두었다가 다시 켤 때 씀
            if (!chkEnabled.Checked) ScanSchedule.RememberedDay = cbDay.SelectedIndex;

            // 로컬 그룹 정책: 체크되어 있으면 '사용 + 요일(또는 안 함)', 아니면 '구성되지 않음'
            int dayWanted = ScanSchedule.ToSetting(chkEnabled.Checked, cbDay.SelectedIndex);
            int? gpoWanted = chkGpo.Checked ? (int?)dayWanted : null;
            bool gpoChanged = gpoWanted != loadedGpo;
            if (changes.Count == 0 && !gpoChanged) { btnApply.Enabled = false; return; }

            try
            {
                string gpoLine = null;
                if (gpoChanged)
                {
                    // 정책을 먼저 바꿔야, 아래 '다시 읽어 확인'에서 이전 정책 값에 막히지 않음
                    LocalGpo.SetDword(GpoScanKey, "ScheduleDay", gpoWanted);
                    gpoLine = Tr.T("그룹 정책 '예약된 검사를 실행할 요일 지정' → ", "Group Policy 'Specify the day of the week to run a scheduled scan' → ") +
                              (gpoWanted.HasValue ? Tr.T("사용 (", "Enabled (") + DayNames[gpoWanted.Value] + ")" : Tr.T("구성되지 않음", "Not configured"));
                }
                if (changes.Count > 0) Defender.SetPreferences(changes);

                // 적용 뒤 다시 읽어 확인: 값이 그대로이거나 정책이 다른 값으로 고정하면 '실제로는 바뀌지 않음'
                var after = Defender.ReadProps("MSFT_MpPreference", Props);
                var applied = new List<string>();
                var blocked = new List<string>();
                if (gpoLine != null) applied.Add(gpoLine);
                int i = 0;
                foreach (var kv in changes)
                {
                    string line = lines[i++];
                    PolicyValue p = ManagedPolicy.Blocks(kv.Key, kv.Value);
                    bool readBackOk = ManagedPolicy.SameValue(Current(after, kv.Key), kv.Value);
                    if (p == null && readBackOk) { applied.Add(line); continue; }
                    string why = p != null
                        ? Tr.T("조직 정책(", "organization policy (") + p.Source + Tr.T(") 때문에 실제로는 ", ") keeps it at ") + Describe(kv.Key, p.Value).Replace(" → ", ": ") + Tr.T(" 그대로", "")
                        : Tr.T("Defender가 값을 받아들이지 않음", "Defender did not accept the value");
                    blocked.Add(line + "  — " + why);
                }

                foreach (string l in applied) ActivityLog.Write(ActivityLog.Internal, Tr.T("예약 검사 설정: ", "Scheduled scan settings: ") + l);
                foreach (string l in blocked) ActivityLog.Write(ActivityLog.Failed, Tr.T("예약 검사 설정 적용 안 됨: ", "Scheduled scan setting not applied: ") + l);
                TrayContext.NotifyDone(applied);
                LoadValues();
                if (blocked.Count == 0) lblResult.Text = Tr.T("적용했습니다.", "Applied.");
                else
                {
                    lblResult.ForeColor = Color.FromArgb(196, 43, 28);
                    lblResult.Text = applied.Count > 0 ? Tr.T("일부만 적용했습니다.", "Partly applied.") : Tr.T("적용되지 않았습니다.", "Not applied.");
                    MessageBox.Show(this, Tr.T("다음 항목은 실제로 바뀌지 않았습니다.\r\n\r\n", "The following items were not actually changed.\r\n\r\n") + string.Join("\r\n", blocked.ToArray()) +
                        Tr.T("\r\n\r\n조직 정책으로 관리되는 설정은 IT 관리자만 바꿀 수 있습니다.", "\r\n\r\nSettings managed by organization policy can only be changed by your IT administrator."),
                        Tr.T("일부 설정이 적용되지 않음", "Some settings were not applied"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                string msg = Defender.ExplainError(ex);
                ActivityLog.Write(ActivityLog.Failed, Tr.T("예약 검사 설정 변경 — ", "Change scheduled scan settings — ") + msg.Replace("\r\n", " "));
                MessageBox.Show(this, msg, Tr.T("설정 변경 실패", "Setting change failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 다시 읽은 값을 비교용으로 (시각은 TimeSpan으로)
        static object Current(Dictionary<string, object> d, string key)
        {
            if (key == "ScanScheduleTime") return T(d, key);
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        // v는 byte / int / bool / TimeSpan (정책 값은 int로 옴)
        static string Describe(string key, object v)
        {
            switch (key)
            {
                case "ScanParameters": return Tr.T("검사 종류 → ", "Scan type → ") + (Convert.ToInt32(v) == 2 ? Tr.T("전체 검사", "Full scan") : Tr.T("빠른 검사", "Quick scan"));
                case "ScanScheduleDay":
                    int d = Convert.ToInt32(v);
                    return d == ScanSchedule.Never ? Tr.T("예약 검사 → 끔", "Scheduled scan → off") : Tr.T("요일 → ", "Day → ") + (d >= 0 && d < DayNames.Length ? DayNames[d] : d.ToString());
                case "ScanScheduleTime": TimeSpan t = (TimeSpan)v; return Tr.T("시각 → ", "Time → ") + t.Hours.ToString("00") + ":" + t.Minutes.ToString("00");
                case "ScanAvgCPULoadFactor": return Tr.T("CPU 사용 한도 → ", "CPU limit → ") + v + "%";
                case "ScanOnlyIfIdleEnabled": return Tr.T("사용하지 않을 때만 검사 → ", "Scan only when not in use → ") + OnOff((bool)v);
                case "DisableCatchupQuickScan": return Tr.T("놓친 빠른 검사 다음에 실행 → ", "Run missed quick scans later → ") + OnOff(!(bool)v);
                case "DisableCatchupFullScan": return Tr.T("놓친 전체 검사 다음에 실행 → ", "Run missed full scans later → ") + OnOff(!(bool)v);
                case "CheckForSignaturesBeforeRunningScan": return Tr.T("검사 전 업데이트 확인 → ", "Check for updates before scanning → ") + OnOff((bool)v);
                case "DisableRemovableDriveScanning": return Tr.T("전체 검사에 이동식 드라이브 포함 → ", "Include removable drives in full scans → ") + OnOff(!(bool)v);
                case "DisableScanningMappedNetworkDrivesForFullScan": return Tr.T("전체 검사에 네트워크 드라이브 포함 → ", "Include network drives in full scans → ") + OnOff(!(bool)v);
            }
            return key + " → " + v;
        }

        static string OnOff(bool b) { return b ? Tr.T("켬", "on") : Tr.T("끔", "off"); }

        void ScanNow(int type)
        {
            if (type == 2)
            {
                var r = MessageBox.Show(this, Tr.T("전체 검사는 디스크 크기에 따라 몇 시간이 걸릴 수 있고, 그동안 PC가 느려질 수 있습니다.\r\n지금 시작할까요?", "A full scan can take hours depending on disk size, and the PC may be slow meanwhile.\r\nStart now?"),
                    Tr.T("지금 전체 검사", "Full scan now"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }
            if (!TrayContext.RequestTypedScan(type))
            {
                lblResult.Text = Tr.T("트레이 프로그램이 실행 중이 아니어서 검사를 시작할 수 없습니다.", "The tray program is not running, so the scan cannot start.");
                return;
            }
            lblResult.Text = (type == 2 ? Tr.T("전체", "Full") : Tr.T("빠른", "Quick")) + Tr.T(" 검사를 시작했습니다. 끝나면 알려 드립니다.", " scan started. You'll be notified when it finishes.");
        }
    }
}
