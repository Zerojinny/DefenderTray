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
    partial class TrayContext
    {
        // ===== 연결된 드라이브 수동 검사 =====
        ToolStripMenuItem BuildScanMenu()
        {
            var m = new ToolStripMenuItem(Tr.T("연결된 드라이브 수동 검사(&C)", "Scan a connected drive(&C)"));
            m.DropDownOpening += (s, e) => RebuildScanMenu(m);
            RebuildScanMenu(m);
            return m;
        }

        void RebuildScanMenu(ToolStripMenuItem m)
        {
            m.DropDownItems.Clear();
            int count = 0;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    string label = "";
                    try { label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : " (" + d.VolumeLabel + ")"; } catch { }
                    string typeTxt = d.DriveType == DriveType.Removable ? Tr.T("이동식", "Removable") : Tr.T("고정", "Fixed");
                    string root = d.RootDirectory.FullName;
                    var item = new ToolStripMenuItem(root + label + " — " + typeTxt + Tr.T(" 검사", " — scan"));
                    item.Click += (s, e) => StartScan(root);
                    m.DropDownItems.Add(item);
                    count++;
                }
                catch { }
            }
            if (count == 0)
                m.DropDownItems.Add(new ToolStripMenuItem(Tr.T("(검사할 드라이브 없음)", "(no drives to scan)")) { Enabled = false });
        }

        void StartScan(string root) { StartScans(new[] { root }); }

        // 제외 목록 창 등에서 검사 요청. 트레이가 없으면 false
        public static bool RequestScan(IList<string> targets)
        {
            if (instance == null || targets == null || targets.Count == 0) return false;
            instance.StartScans(targets);
            return true;
        }

        // 빠른 검사(1) / 전체 검사(2) 시작. 트레이가 없으면 false
        public static bool RequestTypedScan(int scanType)
        {
            if (instance == null) return false;
            TrayContext t = instance;
            string what = scanType == 2 ? Tr.T("전체 검사", "Full scan") : Tr.T("빠른 검사", "Quick scan");
            t.Balloon(3000, Tr.T("검사 시작", "Scan started"), what + Tr.T("를 시작했습니다. 완료되면 알려 드립니다.", " started. You'll be notified when it finishes."), ToolTipIcon.Info);
            ActivityLog.Write(ActivityLog.Info, what + Tr.T(" 시작", " started"));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                try { Defender.StartScanOfType(scanType); }
                catch (Exception ex) { error = ex.Message; }
                t.OnUi(() =>
                {
                    if (error != null)
                    {
                        ActivityLog.Write(ActivityLog.Failed, what + " — " + error);
                        t.Balloon(6000, Tr.T("검사 실패", "Scan failed"), error, ToolTipIcon.Error);
                    }
                    else
                    {
                        ActivityLog.Write(ActivityLog.Info, what + Tr.T(" 완료", " finished"));
                        t.Balloon(5000, Tr.T("검사 완료", "Scan finished"), what + Tr.T("가 끝났습니다.\r\n발견된 위협은 Windows 보안 → 보호 기록에서 확인하세요.", " finished.\r\nCheck Windows Security → Protection history for any threats found."), ToolTipIcon.Info);
                    }
                    if (scheduleForm != null && !scheduleForm.IsDisposed) scheduleForm.RefreshScanTimes();
                });
            });
            return true;
        }

        static ScheduleForm scheduleForm;
        static QuarantineForm quarantineForm;

        static void ShowQuarantine()
        {
            if (quarantineForm == null || quarantineForm.IsDisposed)
            {
                quarantineForm = new QuarantineForm();
                quarantineForm.Show();
            }
            else
            {
                quarantineForm.Reload();
                if (quarantineForm.WindowState == FormWindowState.Minimized) quarantineForm.WindowState = FormWindowState.Normal;
            }
            quarantineForm.TopMost = true;
            quarantineForm.Activate();
            quarantineForm.TopMost = false;
        }

        static void ShowSchedule()
        {
            if (scheduleForm == null || scheduleForm.IsDisposed)
            {
                scheduleForm = new ScheduleForm();
                scheduleForm.Show();
            }
            else if (scheduleForm.WindowState == FormWindowState.Minimized) scheduleForm.WindowState = FormWindowState.Normal;
            scheduleForm.TopMost = true;
            scheduleForm.Activate();
            scheduleForm.TopMost = false;
        }

        // 여러 위치를 차례로 검사 (동시에 여러 검사를 걸면 Defender가 거부할 수 있음)
        void StartScans(IList<string> targets)
        {
            var list = new List<string>(targets);
            string what = list.Count == 1 ? list[0] : list.Count + Tr.T("개 위치", " locations");
            Balloon(3000, Tr.T("검사 시작", "Scan started"), what + Tr.T(" 검사를 시작했습니다. 완료되면 알려 드립니다.", ": scan started. You'll be notified when it finishes."), ToolTipIcon.Info);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var failed = new List<string>();
                foreach (string root in list)
                {
                    ActivityLog.Write(ActivityLog.Info, root + Tr.T(" 검사 시작", ": scan started"));
                    try
                    {
                        Defender.StartCustomScan(root);
                        ActivityLog.Write(ActivityLog.Info, root + Tr.T(" 검사 완료", ": scan finished"));
                    }
                    catch (Exception ex)
                    {
                        ActivityLog.Write(ActivityLog.Failed, root + Tr.T(" 검사 — ", ": scan — ") + ex.Message);
                        failed.Add(root + " — " + ex.Message);
                    }
                }
                OnUi(() =>
                {
                    if (failed.Count > 0)
                        Balloon(6000, Tr.T("검사 실패", "Scan failed"), Summarize(failed, null), ToolTipIcon.Error);
                    else
                        Balloon(5000, Tr.T("검사 완료", "Scan finished"), what + Tr.T(" 검사가 끝났습니다.\r\n발견된 위협은 Windows 보안 → 보호 기록에서 확인하세요.", ": scan finished.\r\nCheck Windows Security → Protection history for any threats found."), ToolTipIcon.Info);
                });
            });
        }
    }
}
