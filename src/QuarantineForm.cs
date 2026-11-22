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
    // 격리 항목 창
    class QuarantineForm : Form
    {
        static readonly Color GoneColor = SystemColors.GrayText;
        readonly ListView list;
        readonly Label summary;
        readonly Button btnRestore, btnOpen, btnApplyPurge;
        readonly NumericUpDown nuPurge;
        readonly SynchronizationContext ui = new WindowsFormsSynchronizationContext();
        int purgeDays = 90;
        int generation;
        bool busy;

        public QuarantineForm()
        {
            Text = Tr.T("격리 항목", "Quarantine");
            Font = new Font("Malgun Gothic", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 400);
            MinimumSize = new Size(640, 300);
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HideSelection = false, MultiSelect = false, ShowItemToolTips = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            list.Columns.Add(Tr.T("위협 이름", "Threat name"), 220);
            list.Columns.Add(Tr.T("원래 위치", "Original location"), 300);
            list.Columns.Add(Tr.T("격리 시각", "Quarantined"), 140);
            list.Columns.Add(Tr.T("자동 삭제 예정", "Auto delete"), 120);
            list.Columns.Add(Tr.T("심각도", "Severity"), 70);
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.DoubleClick += (s, e) => RestoreSelected();
            list.Resize += (s, e) => FitPathColumn();

            // 위쪽 버튼
            btnRestore = new Button { Text = Tr.T("복원(&R)...", "Restore(&R)..."), AutoSize = true, Enabled = false };
            btnOpen = new Button { Text = Tr.T("원래 폴더 열기(&O)", "Open original folder(&O)"), AutoSize = true, Enabled = false };
            var btnHistory = new Button { Text = Tr.T("Windows 보안 보호 기록(&H)", "Windows Security protection history(&H)"), AutoSize = true, Margin = new Padding(18, 3, 3, 3) };
            btnRestore.Click += (s, e) => RestoreSelected();
            btnOpen.Click += (s, e) => OpenFolder();
            btnHistory.Click += (s, e) => OpenProtectionHistory();
            var tips = new ToolTip();
            tips.SetToolTip(btnHistory, Tr.T("항목별 영구 삭제는 Windows 보안의 보호 기록에서 할 수 있습니다", "Items can be deleted permanently one by one in Protection history in Windows Security"));
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 6, 6, 2) };
            top.Controls.AddRange(new Control[] { btnRestore, btnOpen, btnHistory });

            // 아래: 요약 + 보관 기간 + 새로 고침/닫기
            summary = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 16, 4) };
            var lblPurge = new Label { Text = Tr.T("보관 기간(&K):", "Re&tention:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 2, 4) };
            nuPurge = new NumericUpDown { Minimum = 0, Maximum = 3650, Width = 70, Margin = new Padding(0, 5, 2, 0) };
            var lblDays = new Label { Text = Tr.T("일 (0 = 자동 삭제 안 함)", "days (0 = never delete)"), AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 4, 4) };
            btnApplyPurge = new Button { Text = Tr.T("적용(&A)", "Apply(&A)"), AutoSize = true, Enabled = false };
            nuPurge.ValueChanged += (s, e) => { btnApplyPurge.Enabled = (int)nuPurge.Value != purgeDays; };
            btnApplyPurge.Click += (s, e) => ApplyPurge();
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            left.Controls.AddRange(new Control[] { summary, lblPurge, nuPurge, lblDays, btnApplyPurge });

            var btnRefresh = new Button { Text = Tr.T("새로 고침(&F)", "Refresh(&F)"), AutoSize = true };
            var btnClose = new Button { Text = Tr.T("닫기", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            btnRefresh.Click += (s, e) => Reload();
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Right, WrapContents = false };
            right.Controls.Add(btnClose);
            right.Controls.Add(btnRefresh);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 6, 8, 6) };
            bottom.Controls.Add(left);    // Fill 먼저
            bottom.Controls.Add(right);

            Controls.Add(list);           // Fill 먼저
            Controls.Add(top);
            Controls.Add(bottom);

            Reload();
        }

        QuarantineItem Selected()
        {
            return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as QuarantineItem : null;
        }

        void UpdateButtons()
        {
            var it = Selected();
            btnRestore.Enabled = it != null && !busy;
            btnOpen.Enabled = it != null && it.IsFile;
        }

        // 목록 읽기: WMI는 빠르고, MpCmdRun 대조는 느릴 수 있어 백그라운드에서
        public void Reload()
        {
            int gen = ++generation;
            var pref = Defender.ReadProps("MSFT_MpPreference", "QuarantinePurgeItemsAfterDelay");
            object pv;
            if (pref.TryGetValue("QuarantinePurgeItemsAfterDelay", out pv) && pv != null) purgeDays = Convert.ToInt32(pv);
            nuPurge.Value = Math.Min(nuPurge.Maximum, purgeDays);
            btnApplyPurge.Enabled = false;

            // 조직 정책으로 보관 기간이 고정돼 있으면 잠그고 이유를 툴팁으로
            PolicyValue pol = ManagedPolicy.Get("QuarantinePurgeItemsAfterDelay");
            nuPurge.Enabled = pol == null;
            purgeTip.SetToolTip(nuPurge, pol == null ? null
                : Tr.T("조직 정책(", "Locked by organization policy (") + pol.Source + Tr.T(")으로 ", ") at ") + PurgeText(Convert.ToInt32(pol.Value)) + Tr.T(" 고정되어 바꿀 수 없습니다", "; it cannot be changed"));

            summary.ForeColor = SystemColors.ControlText;
            summary.Text = Tr.T("격리 목록을 읽는 중…", "Reading the quarantine list…");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<QuarantineItem> items = null;
                string error = null;
                try
                {
                    items = Quarantine.GetItems();
                    Quarantine.MarkInList(items);
                }
                catch (Exception ex) { error = ex.Message; }
                ui.Post(__ =>
                {
                    if (gen != generation || IsDisposed) return;
                    Fill(items, error);
                }, null);
            });
        }

        void Fill(List<QuarantineItem> items, string error)
        {
            list.BeginUpdate();
            list.Items.Clear();
            int active = 0, gone = 0;
            if (items != null)
            {
                foreach (var it in items)
                {
                    bool isGone = it.InList == false;
                    if (isGone) gone++; else active++;
                    string purge = purgeDays <= 0 ? Tr.T("자동 삭제 안 함", "Never") : it.Quarantined.AddDays(purgeDays).ToString("yyyy-MM-dd");
                    var row = new ListViewItem(new[]
                    {
                        it.Name, it.Path, it.Quarantined.ToString("yyyy-MM-dd HH:mm"),
                        isGone ? Tr.T("격리 목록에 없음", "Not in quarantine") : purge, Quarantine.SeverityText(it.Severity)
                    })
                    {
                        Tag = it,
                        ToolTipText = it.Name + "\r\n" + it.Path +
                            Tr.T("\r\n탐지: ", "\r\nDetected: ") + it.Detected.ToString("yyyy-MM-dd HH:mm") +
                            (string.IsNullOrEmpty(it.Process) ? "" : Tr.T("\r\n탐지 당시 프로세스: ", "\r\nProcess at detection: ") + it.Process) +
                            (isGone ? Tr.T("\r\n실제 격리 목록에는 없습니다 (이미 복원되었거나 삭제되었을 수 있음)", "\r\nNot in the actual quarantine list (it may already have been restored or deleted)") : "")
                    };
                    if (isGone) row.ForeColor = GoneColor;
                    else if (it.Severity >= 4) row.UseItemStyleForSubItems = false;
                    if (!isGone && it.Severity >= 4) row.SubItems[4].ForeColor = Color.FromArgb(196, 43, 28);
                    list.Items.Add(row);
                }
            }
            if (list.Items.Count == 0)
                list.Items.Add(new ListViewItem(new[] { error != null ? Tr.T("(목록을 읽지 못했습니다)", "(could not read the list)") : Tr.T("(격리 항목 없음)", "(no quarantined items)"), error ?? "", "", "", "" }) { ForeColor = SystemColors.GrayText });
            list.EndUpdate();

            summary.Text = error != null ? Tr.T("목록을 읽지 못했습니다", "Could not read the list") :
                Tr.T("격리 ", "Quarantined: ") + active + Tr.T("개", "") + (gone > 0 ? Tr.T(" · 목록에 없음 ", " · not in list: ") + gone + Tr.T("개", "") : "");
            UpdateButtons();
        }

        void RestoreSelected()
        {
            var it = Selected();
            if (it == null || busy) return;
            RestoreMode? mode = RestoreDialog.Ask(this, it);
            if (mode == null) return;

            string folder = null;
            if (mode == RestoreMode.CopyTo)
            {
                using (var dlg = new FolderBrowserDialog { Description = Tr.T("사본을 복원할 폴더 선택 (검사 제외 폴더를 권장)", "Choose a folder for the restored copy (an excluded folder is recommended)"), ShowNewFolderButton = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    folder = dlg.SelectedPath;
                }
            }

            busy = true;
            UseWaitCursor = true;
            UpdateButtons();
            summary.Text = Tr.T("복원하는 중…", "Restoring…");
            RestoreMode m = mode.Value;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                try
                {
                    if (m == RestoreMode.AllowThreat)
                    {
                        Quarantine.AllowThreat(it.ThreatId);
                        ActivityLog.Write(ActivityLog.Internal, Tr.T("위협 허용: ", "Allowed threat: ") + it.Name);
                    }
                    else if (m == RestoreMode.ExcludeFile)
                        Defender.AddExclusionPath(it.Path);    // 모든 검사에서 제외 (기록은 자동)
                    Quarantine.Restore(it, folder);
                }
                catch (Exception ex) { error = Defender.ExplainError(ex); }

                ui.Post(__ =>
                {
                    if (IsDisposed) return;
                    busy = false;
                    UseWaitCursor = false;
                    string how = m == RestoreMode.AllowThreat ? Tr.T("위협 허용 후 복원", "restore after allowing the threat") : m == RestoreMode.ExcludeFile ? Tr.T("파일 제외 후 복원", "restore after excluding the file") : Tr.T("사본 복원", "restore a copy");
                    if (error != null)
                    {
                        ActivityLog.Write(ActivityLog.Failed, Tr.T("격리 항목 복원 (", "Restore quarantined item (") + how + ") — " + it.Path + " — " + error.Replace("\r\n", " "));
                        MessageBox.Show(this, error, Tr.T("복원 실패", "Restore failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Reload();
                        return;
                    }
                    string dest = folder ?? it.Path;
                    ActivityLog.Write(ActivityLog.Internal, Tr.T("격리 항목 복원 (", "Restore quarantined item (") + how + "): " + it.Name + " — " + dest);
                    TrayContext.NotifyDone(new[] { Tr.T("격리 항목 복원 (", "Restore quarantined item (") + how + ")", dest });
                    Reload();
                }, null);
            });
        }

        void OpenFolder()
        {
            var it = Selected();
            if (it == null || !it.IsFile) return;
            try
            {
                string dir = Path.GetDirectoryName(it.Path);
                if (File.Exists(it.Path)) Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
                else if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                else summary.Text = Tr.T("원래 폴더가 없습니다 (드라이브가 연결되어 있지 않을 수 있음): ", "The original folder does not exist (the drive may not be connected): ") + dir;
            }
            catch { }
        }

        static void OpenProtectionHistory()
        {
            try { Process.Start("windowsdefender://threathistory"); }
            catch { try { Process.Start("windowsdefender://threat"); } catch { } }
        }

        readonly ToolTip purgeTip = new ToolTip();

        static string PurgeText(int days) { return days == 0 ? Tr.T("자동 삭제 안 함", "Never delete") : days + Tr.T("일", " days"); }

        void ApplyPurge()
        {
            int days = (int)nuPurge.Value;
            try
            {
                Defender.SetPreferences(new Dictionary<string, object> { { "QuarantinePurgeItemsAfterDelay", (uint)days } });
                string line = Tr.T("격리 항목 보관 기간 → ", "Quarantine retention → ") + PurgeText(days);

                // 적용 뒤 다시 읽어 확인 (조직 정책이 덮어쓰면 그대로임)
                object now;
                Defender.ReadProps("MSFT_MpPreference", "QuarantinePurgeItemsAfterDelay").TryGetValue("QuarantinePurgeItemsAfterDelay", out now);
                PolicyValue pol = ManagedPolicy.Blocks("QuarantinePurgeItemsAfterDelay", days);
                if (pol != null || !ManagedPolicy.SameValue(now, days))
                {
                    string why = pol != null ? Tr.T("조직 정책(", "Organization policy (") + pol.Source + Tr.T(") 때문에 ", ") keeps it at ") + PurgeText(Convert.ToInt32(pol.Value)) + Tr.T(" 그대로입니다", "")
                                             : Tr.T("Defender가 값을 받아들이지 않았습니다", "Defender did not accept the value");
                    ActivityLog.Write(ActivityLog.Failed, line + Tr.T(" — 적용 안 됨: ", " — not applied: ") + why);
                    MessageBox.Show(this, line + Tr.T("\r\n\r\n실제로는 바뀌지 않았습니다: ", "\r\n\r\nIt was not actually changed: ") + why + ".", Tr.T("설정이 적용되지 않음", "Setting not applied"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Reload();
                    return;
                }
                ActivityLog.Write(ActivityLog.Internal, line);
                TrayContext.NotifyDone(new[] { line });
                Reload();
            }
            catch (Exception ex)
            {
                string msg = Defender.ExplainError(ex);
                ActivityLog.Write(ActivityLog.Failed, Tr.T("격리 항목 보관 기간 변경 — ", "Change quarantine retention — ") + msg.Replace("\r\n", " "));
                MessageBox.Show(this, msg, Tr.T("설정 변경 실패", "Setting change failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void FitPathColumn()
        {
            int other = 0;
            for (int i = 0; i < list.Columns.Count; i++) if (i != 1) other += list.Columns[i].Width;
            int w = list.ClientSize.Width - other - 4;
            if (w > 200) list.Columns[1].Width = w;
        }

        protected override void OnShown(EventArgs e)
        {
            list.Columns[2].Width = TextRenderer.MeasureText("2026-11-10 08:01", list.Font).Width + 20;
            list.Columns[3].Width = TextRenderer.MeasureText(Tr.T("격리 목록에 없음", "Not in quarantine"), list.Font).Width + 20;
            list.Columns[4].Width = TextRenderer.MeasureText(Tr.T("알 수 없음", "Unknown"), list.Font).Width + 20;
            FitPathColumn();
            base.OnShown(e);
        }
    }
}
