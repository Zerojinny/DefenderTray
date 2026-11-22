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
        // ===== 외부 변경 감시 =====
        readonly ToolStripMenuItem watchItem, ownItem;
        bool watchNotify = AppSettings.Get("WatchNotify") != "0";
        bool ownNotify = AppSettings.Get("OwnNotify") != "0";

        void ToggleOwnNotify()
        {
            ownNotify = !ownNotify;
            ownItem.Checked = ownNotify;
            AppSettings.Set("OwnNotify", ownNotify ? "1" : "0");
            Balloon(2500, Tr.T("내부 변경 알림", "Internal change notifications"),
                ownNotify ? Tr.T("이 프로그램으로 바꾼 내용을 알려 드립니다.", "You'll be notified of changes made with this program.") : Tr.T("내부 변경 알림을 껐습니다. 변경에 실패하면 계속 알려 드립니다.", "Internal change notifications turned off. Failures are still notified."),
                ToolTipIcon.Info);
        }
        string watchTitle, watchDetail;
        bool watchHasExclusion;

        // 모든 풍선 알림은 여기로 — 클릭했을 때 어떤 알림이었는지 알기 위해 제목을 기억
        string lastBalloonTitle;
        void Balloon(int ms, string title, string text, ToolTipIcon icon)
        {
            lastBalloonTitle = title;
            tray.ShowBalloonTip(ms, title, text, icon);
        }

        void ToggleWatchNotify()
        {
            watchNotify = !watchNotify;
            watchItem.Checked = watchNotify;
            AppSettings.Set("WatchNotify", watchNotify ? "1" : "0");
            Balloon(2500, Tr.T("외부 변경 알림", "External change notifications"),
                watchNotify ? Tr.T("다른 곳에서 Defender 설정이 바뀌면 알려 드립니다.", "You'll be notified when Defender settings are changed elsewhere.") : Tr.T("외부 변경 알림을 껐습니다.", "External change notifications turned off."),
                ToolTipIcon.Info);
        }

        // 알림 제목 — 이 프로그램이 한 변경과 외부 변경을 한눈에 구분
        public static string OwnTitle { get { return Tr.T("✓ 이 도구에서 변경함", "✓ Changed by this tool"); } }
        static string ExternalTitle { get { return Tr.T("⚠ 외부에서 변경됨 (이 도구 아님)", "⚠ Changed externally (not by this tool)"); } }
        static string ExternalStartupTitle { get { return Tr.T("⚠ 꺼져 있던 동안 외부에서 변경됨", "⚠ Changed externally while this tool was not running"); } }

        static TrayContext instance;

        // 이 프로그램으로 한 변경의 완료 알림 (트레이 메뉴, 제외 목록 창, 오른쪽 클릭 메뉴 공통)
        public static void NotifyDone(IList<string> lines)
        {
            if (instance == null || !instance.ownNotify || lines == null || lines.Count == 0) return;
            instance.Balloon(4000, OwnTitle, Summarize(lines, null), ToolTipIcon.Info);
        }

        static string Summarize(IList<string> lines, string tail)
        {
            var sb = new StringBuilder();
            int shown = Math.Min(3, lines.Count);
            for (int i = 0; i < shown; i++)
            {
                string t = lines[i];
                sb.AppendLine(t.Length > 60 ? t.Substring(0, 57) + "..." : t);
            }
            if (lines.Count > shown) sb.AppendLine(Tr.T("외 ", "and ") + (lines.Count - shown) + Tr.T("건", " more"));
            if (tail != null) sb.Append(tail);
            return sb.ToString().TrimEnd();
        }

        // changes: 백그라운드에서 DefenderWatch.ReadNew()로 읽은 새 변경.
        // 알림을 꺼 둔 동안에도 읽은 위치는 계속 앞으로 옮기므로, 다시 켰을 때 쌓인 알림이 몰려오지 않음
        void HandleChanges(List<DefenderChange> changes, bool startup)
        {
            if (changes == null || changes.Count == 0) return;

            var external = new List<DefenderChange>();
            var mineLines = new List<string>();
            bool anyExclusion = false, externalExclusion = false;
            foreach (DefenderChange c in changes)
            {
                if (c.IsExclusion) anyExclusion = true;
                if (c.Mine) mineLines.Add(c.Text);   // 오른쪽 클릭 프로세스가 이미 기록함
                else
                {
                    external.Add(c);
                    if (c.IsExclusion) externalExclusion = true;
                    ActivityLog.Write(ActivityLog.External, c.Text, c.Time);   // 실제 변경 시각으로 기록
                }
            }
            // 켜 둔 항목에 한해 외부 변경을 원래대로 돌려놓음
            List<string> reverted = RevertExternal(external);

            if (logForm != null && !logForm.IsDisposed && (external.Count > 0 || reverted.Count > 0)) logForm.Reload();
            if (anyExclusion && listForm != null && !listForm.IsDisposed) listForm.Reload();

            // 외부 변경이 더 중요하므로 우선 표시. 없으면 오른쪽 클릭 메뉴 등으로 한 변경의 완료 알림
            if (external.Count == 0 || !watchNotify)
            {
                if (!startup) NotifyDone(mineLines);
                return;
            }

            var lines = new List<string>();
            var detail = new List<string>();
            foreach (DefenderChange c in external)
            {
                lines.Add(c.Text);
                detail.Add((startup ? c.Time.ToString("MM-dd HH:mm") : c.Time.ToString("HH:mm:ss")) + "  " + c.Text);
            }

            string title, body;
            if (reverted.Count > 0)
            {
                title = RevertTitle;
                body = Summarize(reverted, Tr.T("클릭하면 자세히 보기", "Click for details"));
                detail.Add("");
                detail.Add(Tr.T("되돌린 내용:", "Reverted:"));
                detail.AddRange(reverted);
            }
            else
            {
                title = startup ? ExternalStartupTitle : ExternalTitle;
                body = (!startup && external.Count == 1 && external[0].IsRtpOn)
                    ? Tr.T("실시간 보호 켜짐\r\n(Windows 자동 복구 또는 다른 프로그램)\r\n클릭하면 자세히 보기", "Real-time protection on\r\n(Windows automatic recovery or another program)\r\nClick for details")
                    : Summarize(lines, Tr.T("클릭하면 자세히 보기", "Click for details"));
            }

            watchTitle = title;
            watchDetail = string.Join("\r\n", detail.ToArray());
            watchHasExclusion = externalExclusion;
            Balloon(8000, title, body, ToolTipIcon.Warning);
        }

        // ===== 외부 변경 되돌리기 =====
        static string RevertTitle { get { return Tr.T("↩ 외부 변경을 되돌림", "↩ External change reverted"); } }
        bool revertAdd = AppSettings.Get("RevertAdd") == "1";
        bool revertRemove = AppSettings.Get("RevertRemove") == "1";
        bool revertRtp = AppSettings.Get("RevertRtpOff") == "1";
        ToolStripMenuItem revertAddItem, revertRemoveItem, revertRtpItem;

        // 같은 대상을 10분 안에 3번 넘게 되돌려야 하면 그룹 정책 등과 다투는 것이므로 멈춤
        readonly Dictionary<string, List<DateTime>> revertHistory = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);

        bool AllowRevert(string key)
        {
            List<DateTime> h;
            if (!revertHistory.TryGetValue(key, out h)) revertHistory[key] = h = new List<DateTime>();
            DateTime now = DateTime.Now;
            h.RemoveAll(t => now - t > TimeSpan.FromMinutes(10));
            if (h.Count >= 3) return false;
            h.Add(now);
            return true;
        }

        // 되돌린 결과를 한 줄씩 돌려줌 (실패 포함)
        List<string> RevertExternal(List<DefenderChange> external)
        {
            var result = new List<string>();
            if (external.Count == 0 || !(revertAdd || revertRemove || revertRtp)) return result;

            foreach (DefenderChange c in RevertPlan.Exclusions(external, revertAdd, revertRemove))
            {
                string key = RevertPlan.Key(c);
                string label = c.Text + (c.Added ? Tr.T(" → 다시 삭제", " → removed again") : Tr.T(" → 다시 추가", " → added back"));
                if (!AllowRevert(key))
                {
                    result.Add(c.Text + Tr.T(" → 계속 다시 바뀌어 되돌리기를 멈춤", " → keeps changing back, stopped reverting"));
                    ActivityLog.Write(ActivityLog.Failed, Tr.T("외부 변경 되돌리기 중단 (10분 안에 3번 넘게 반복): ", "Stopped reverting external change (more than 3 times in 10 minutes): ") + c.Text);
                    continue;
                }
                try
                {
                    Defender.SetExclusion(c.Kind, c.Value, !c.Added);   // 처음 변경이 추가였다면 원래는 없었음
                    result.Add(label);
                    ActivityLog.Write(ActivityLog.Info, Tr.T("↩ 외부 변경 되돌림: ", "↩ External change reverted: ") + label);
                }
                catch (Exception ex)
                {
                    result.Add(c.Text + Tr.T(" → 되돌리기 실패: ", " → revert failed: ") + Defender.ExplainError(ex).Replace("\r\n", " "));
                }
            }

            // 실시간 보호: 마지막 외부 변경이 "꺼짐"이고 지금도 꺼져 있으면 다시 켬
            if (revertRtp)
            {
                if (RevertPlan.LastRtpWasOff(external) && Defender.GetState() == RtpState.Off)
                {
                    if (!AllowRevert("RTP"))
                    {
                        result.Add(Tr.T("실시간 보호 꺼짐 → 계속 다시 꺼져 되돌리기를 멈춤", "Real-time protection off → keeps turning off, stopped reverting"));
                        ActivityLog.Write(ActivityLog.Failed, Tr.T("외부 변경 되돌리기 중단 (10분 안에 3번 넘게 반복): 실시간 보호 꺼짐", "Stopped reverting external change (more than 3 times in 10 minutes): real-time protection off"));
                    }
                    else
                    {
                        try
                        {
                            Defender.SetRealtime(true);
                            result.Add(Tr.T("실시간 보호 꺼짐 → 다시 켬", "Real-time protection off → turned back on"));
                            ActivityLog.Write(ActivityLog.Internal, Tr.T("실시간 보호 켜짐", "Real-time protection on"));
                            ActivityLog.Write(ActivityLog.Info, Tr.T("↩ 외부 변경 되돌림: 실시간 보호 꺼짐 → 다시 켬", "↩ External change reverted: real-time protection off → turned back on"));
                        }
                        catch (Exception ex)
                        {
                            string msg = Defender.ExplainError(ex).Replace("\r\n", " ");
                            result.Add(Tr.T("실시간 보호 꺼짐 → 되돌리기 실패: ", "Real-time protection off → revert failed: ") + msg);
                            ActivityLog.Write(ActivityLog.Failed, Tr.T("실시간 보호 켜기 (외부 변경 되돌리기) — ", "Turn on real-time protection (revert external change) — ") + msg);
                        }
                    }
                }
            }
            return result;
        }

        ToolStripMenuItem BuildRevertMenu()
        {
            var m = new ToolStripMenuItem(Tr.T("외부 변경 되돌리기(&G)", "Revert external changes(&G)"));
            revertAddItem = new ToolStripMenuItem(Tr.T("외부에서 추가한 제외 삭제(&A)", "Remove exclusions added elsewhere(&A)"), null, (s, e) => ToggleRevert("RevertAdd"))
            {
                Checked = revertAdd,
                ToolTipText = Tr.T("다른 곳에서 검사 제외를 추가하면 바로 삭제합니다.", "When an exclusion is added elsewhere, it is removed right away.")
            };
            revertRemoveItem = new ToolStripMenuItem(Tr.T("외부에서 삭제한 제외 복구(&R)", "Restore exclusions removed elsewhere(&R)"), null, (s, e) => ToggleRevert("RevertRemove"))
            {
                Checked = revertRemove,
                ToolTipText = Tr.T("다른 곳에서 검사 제외를 삭제하면 다시 추가합니다.", "When an exclusion is removed elsewhere, it is added back.")
            };
            revertRtpItem = new ToolStripMenuItem(Tr.T("외부에서 끈 실시간 보호 다시 켜기(&P)", "Turn real-time protection back on if turned off elsewhere(&P)"), null, (s, e) => ToggleRevert("RevertRtpOff"))
            {
                Checked = revertRtp,
                ToolTipText = Tr.T("다른 곳(Windows 보안 앱 포함)에서 실시간 보호를 끄면 다시 켭니다. 끌 때는 이 프로그램을 쓰세요.", "If real-time protection is turned off elsewhere (including the Windows Security app), it is turned back on. Use this program to turn it off.")
            };
            m.DropDownItems.Add(revertAddItem);
            m.DropDownItems.Add(revertRemoveItem);
            m.DropDownItems.Add(revertRtpItem);
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add(new ToolStripMenuItem(Tr.T("이 프로그램이 실행 중일 때만 동작합니다", "Works only while this program is running")) { Enabled = false });
            m.DropDownItems.Add(new ToolStripMenuItem(Tr.T("그룹 정책으로 바뀐 제외는 되돌리지 않습니다", "Exclusions changed by Group Policy are not reverted")) { Enabled = false });
            return m;
        }

        void ToggleRevert(string key)
        {
            bool on;
            string what;
            if (key == "RevertAdd") { on = revertAdd = !revertAdd; revertAddItem.Checked = on; what = Tr.T("외부에서 추가한 제외 삭제", "Remove exclusions added elsewhere"); }
            else if (key == "RevertRemove") { on = revertRemove = !revertRemove; revertRemoveItem.Checked = on; what = Tr.T("외부에서 삭제한 제외 복구", "Restore exclusions removed elsewhere"); }
            else { on = revertRtp = !revertRtp; revertRtpItem.Checked = on; what = Tr.T("외부에서 끈 실시간 보호 다시 켜기", "Turn real-time protection back on if turned off elsewhere"); }
            AppSettings.Set(key, on ? "1" : "0");
            ActivityLog.Write(ActivityLog.Info, Tr.T("설정: ", "Setting: ") + what + (on ? Tr.T(" 켬", " on") : Tr.T(" 끔", " off")));
            Balloon(3000, Tr.T("외부 변경 되돌리기", "Revert external changes"), what + (on ? Tr.T("를 켰습니다.\r\n이 프로그램이 실행 중일 때만 동작합니다.", " turned on.\r\nWorks only while this program is running.") : Tr.T("를 껐습니다.", " turned off.")), ToolTipIcon.Info);
        }

        void ShowWatchDetail()
        {
            if (watchDetail == null || lastBalloonTitle != watchTitle) return;
            string text = watchDetail + Tr.T("\r\n\r\n이 프로그램이 아닌 곳(Windows 보안, PowerShell, 다른 프로그램, Windows 자동 복구 등)에서 바뀐 내용입니다.", "\r\n\r\nThese changes were made outside this program (Windows Security, PowerShell, another program, Windows automatic recovery, etc.).");
            if (watchHasExclusion)
            {
                var r = MessageBox.Show(text + Tr.T("\r\n\r\n현재 제외 목록을 열까요?", "\r\n\r\nOpen the current exclusion list?"), watchTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r == DialogResult.Yes) ShowExclusionList();
            }
            else MessageBox.Show(text, watchTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
