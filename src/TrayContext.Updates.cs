using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace DefenderTray
{
    partial class TrayContext
    {
        // ===== 보호 업데이트 (보안 인텔리전스) =====
        ToolStripMenuItem sigItem, sigUpdateItem;
        SigInfo lastSig;
        bool sigUpdating;
        DateTime sigNextReadUtc = DateTime.MinValue;   // 정의 정보는 자주 바뀌지 않아 1분마다(메뉴를 열면 바로) 읽음
        static string SigTitle { get { return Tr.T("보호 업데이트", "Protection updates"); } }
        static string SigStaleTitle { get { return Tr.T("보호 업데이트가 오래됨", "Protection updates are out of date"); } }

        void BuildSigItems()
        {
            sigItem = new ToolStripMenuItem(Tr.T("보호 업데이트: 확인 중...", "Protection updates: checking...")) { Enabled = false };
            sigUpdateItem = new ToolStripMenuItem(Tr.T("보호 업데이트 확인(&P)", "Check for &protection updates"), null, (s, e) => StartSigUpdate())
            {
                ToolTipText = Tr.T("Windows 보안의 '업데이트 확인'과 같습니다. 최신 보안 인텔리전스(정의)를 받습니다.", "Same as 'Check for updates' in Windows Security. Downloads the latest security intelligence.")
            };
        }

        ToolStripMenuItem BuildStaleMenu()
        {
            var m = new ToolStripMenuItem(Tr.T("오래된 보호 업데이트 경고(&W)", "Out-of-date update &warning"))
            {
                ToolTipText = Tr.T("마지막 보호 업데이트 뒤 정한 기간이 지나면 알림을 띄웁니다. 알림을 누르면 바로 업데이트합니다.", "Shows a notification when protection updates are older than the chosen period. Click it to update.")
            };
            foreach (int d in SignatureUpdate.StaleChoices)
            {
                if (d == 0) m.DropDownItems.Add(new ToolStripSeparator());
                int days = d;
                string text = SignatureUpdate.ChoiceLabel(d) + (d == SignatureUpdate.DefaultStaleDays ? Tr.T(" (기본)", " (default)") : "");
                m.DropDownItems.Add(new ToolStripMenuItem(text, null, (s, e) => SetStaleDays(days)) { Tag = days });
            }
            m.DropDownOpening += (s, e) =>
            {
                int cur = SignatureUpdate.StaleDays;
                foreach (ToolStripItem it in m.DropDownItems)
                {
                    var mi = it as ToolStripMenuItem;
                    if (mi != null && mi.Tag is int) mi.Checked = (int)mi.Tag == cur;
                }
            };
            return m;
        }

        void SetStaleDays(int days)
        {
            SignatureUpdate.StaleDays = days;
            AppSettings.Set("SignatureWarnedAt", "");   // 기준이 바뀌면 다시 경고할 수 있게
            Balloon(2500, SigTitle,
                days == 0 ? Tr.T("오래된 보호 업데이트 경고를 껐습니다.", "Out-of-date update warnings turned off.")
                          : Tr.F("마지막 업데이트 뒤 {0}일이 지나면 알려 드립니다.", days == 1 ? "You'll be notified when updates are {0} day old." : "You'll be notified when updates are {0} days old.", days),
                ToolTipIcon.Info);
            HandleSig(lastSig, true);
        }

        // Refresh()가 이 값을 보고 이번 확인에서 정의 정보를 읽을지 정함
        bool SigReadDue()
        {
            if (DateTime.UtcNow < sigNextReadUtc) return false;
            sigNextReadUtc = DateTime.UtcNow.AddMinutes(1);
            return true;
        }

        void HandleSig(SigInfo sig, bool allowWarn)
        {
            if (sig != null) lastSig = sig;
            sig = lastSig;
            bool stale = SignatureUpdate.IsStale(sig, SignatureUpdate.StaleDays, DateTime.Now);
            sigItem.Text = SignatureUpdate.StatusLine(sig, DateTime.Now) + (stale ? Tr.T(" — 오래됨", " — out of date") : "");
            sigItem.ForeColor = stale ? Color.FromArgb(200, 110, 0) : SystemColors.GrayText;
            sigItem.ToolTipText = sig == null ? null :
                Tr.T("엔진 ", "Engine ") + sig.Engine + Tr.T(" · 플랫폼 ", " · Platform ") + sig.Platform;

            if (!allowWarn || !stale || sigUpdating) return;
            // 같은 경고는 하루에 한 번만 (프로그램을 다시 시작해도)
            long ticks;
            string s = AppSettings.Get("SignatureWarnedAt");
            if (s != null && long.TryParse(s, out ticks) && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TimeSpan.FromHours(20)) return;
            AppSettings.Set("SignatureWarnedAt", DateTime.UtcNow.Ticks.ToString());
            string when = sig.LastUpdated.HasValue ? SignatureUpdate.WhenLabel(sig.LastUpdated.Value, DateTime.Now) : Tr.T("알 수 없음", "unknown");
            ActivityLog.Write(ActivityLog.Info, SigStaleTitle + " — " + Tr.T("마지막 업데이트 ", "last updated ") + when);
            Balloon(10000, SigStaleTitle,
                Tr.F("마지막 업데이트: {0}\r\n클릭하면 지금 업데이트합니다.", "Last updated: {0}\r\nClick to update now.", when),
                ToolTipIcon.Warning);
        }

        void StartSigUpdate()
        {
            if (sigUpdating) return;
            sigUpdating = true;
            sigUpdateItem.Enabled = false;
            sigUpdateItem.Text = Tr.T("보호 업데이트 확인 중...", "Checking for protection updates...");
            Balloon(2500, SigTitle, Tr.T("업데이트를 확인하는 중입니다. 끝나면 알려 드립니다.", "Checking for updates. You'll be notified when it's done."), ToolTipIcon.Info);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                SigInfo before = SignatureUpdate.Get();
                string error = null;
                try { SignatureUpdate.Update(); }
                catch (Exception ex) { error = DescribeUpdateError(ex); }
                SigInfo after = SignatureUpdate.Get();

                OnUi(() =>
                {
                    sigUpdating = false;
                    sigUpdateItem.Enabled = true;
                    sigUpdateItem.Text = Tr.T("보호 업데이트 확인(&P)", "Check for &protection updates");
                    sigNextReadUtc = DateTime.UtcNow.AddMinutes(1);
                    HandleSig(after, false);

                    if (error != null)
                    {
                        ActivityLog.Write(ActivityLog.Failed, SigTitle + " — " + error);
                        Balloon(6000, Tr.T("보호 업데이트 실패", "Protection update failed"), error, ToolTipIcon.Error);
                        return;
                    }
                    var lines = new List<string>();
                    if (before != null && after != null && before.Version != after.Version)
                        lines.Add(Tr.T("보안 인텔리전스 ", "Security intelligence ") + before.Version + " → " + after.Version);
                    if (before != null && after != null && before.Engine != after.Engine)
                        lines.Add(Tr.T("엔진 ", "Engine ") + before.Engine + " → " + after.Engine);
                    if (lines.Count == 0)
                    {
                        string v = after != null ? after.Version : "";
                        ActivityLog.Write(ActivityLog.Info, SigTitle + Tr.T(" — 이미 최신 ", " — already up to date ") + v);
                        Balloon(4000, SigTitle, Tr.T("이미 최신입니다. (", "Already up to date. (") + v + ")", ToolTipIcon.Info);
                    }
                    else
                    {
                        AppSettings.Set("SignatureWarnedAt", "");
                        ActivityLog.Write(ActivityLog.Internal, SigTitle + " — " + string.Join(", ", lines.ToArray()));
                        Balloon(5000, Tr.T("✓ 보호 업데이트 완료", "✓ Protection updated"), string.Join("\r\n", lines.ToArray()), ToolTipIcon.Info);
                    }
                });
            });
        }

        // WMI 오류에 Defender 오류 코드가 들어 있으면 그 코드로 설명
        static string DescribeUpdateError(Exception ex)
        {
            var me = ex as ManagementException;
            if (me != null && me.ErrorInformation != null)
            {
                foreach (string name in new[] { "error_Code", "StatusCode" })
                {
                    try
                    {
                        object v = me.ErrorInformation[name];
                        if (v != null && Convert.ToInt64(v) != 0) return SignatureUpdate.ExplainCode(unchecked((uint)Convert.ToInt64(v)));
                    }
                    catch { }
                }
            }
            return ex.Message;
        }
    }
}
