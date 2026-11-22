using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DefenderTray
{
    // SmartScreen: 상태 표시, 시간 지정 끄기, 다시 켜기, 외부 변경 감지
    partial class TrayContext
    {
        ToolStripMenuItem ssMenu;
        readonly Dictionary<SsPart, ToolStripMenuItem> ssStatusItems = new Dictionary<SsPart, ToolStripMenuItem>();
        ToolStripMenuItem ssTargetsItem;

        Dictionary<SsPart, bool> ssKnown;   // 마지막으로 본 켜짐/꺼짐 (외부 변경 비교용). null = 아직 없음
        int ssVersion;                      // 이 앱이 바꿀 때마다 올림 → 그 전에 시작한 확인 결과는 버림

        ToolStripMenuItem BuildSmartScreenMenu()
        {
            ssMenu = new ToolStripMenuItem("SmartScreen(&M)");
            foreach (SsPart p in SmartScreen.All)
            {
                var it = new ToolStripMenuItem(SmartScreen.Name(p)) { Enabled = false };
                ssStatusItems[p] = it;
                ssMenu.DropDownItems.Add(it);
            }
            ssMenu.DropDownItems.Add(new ToolStripSeparator());
            ssTargetsItem = new ToolStripMenuItem(Tr.T("끌 항목:", "Parts to turn off:")) { Enabled = false };
            ssMenu.DropDownItems.Add(ssTargetsItem);
            foreach (int minutes in TimedOff.Choices)
            {
                int min = minutes;
                string text = minutes == 0 ? Tr.T("직접 켤 때까지 끄기(&N)", "Until turned on(&N)") : Tr.F("{0} 동안 끄기", "Turn off for {0}", TimedOff.DurationLabel(minutes));
                ssMenu.DropDownItems.Add(new ToolStripMenuItem(text, null, (s, e) => SsTurnOff(min)));
            }
            ssMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("다시 켜기(&O)", "Turn back on(&O)"), null, (s, e) => SsTurnOn(null)));
            ssMenu.DropDownItems.Add(new ToolStripSeparator());
            ssMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("SmartScreen 설정(&S)...", "SmartScreen settings(&S)..."), null, (s, e) => ShowSmartScreenSettings()));
            ssMenu.DropDownOpening += (s, e) => UpdateSmartScreenMenu(SmartScreen.GetAll());
            return ssMenu;
        }

        void UpdateSmartScreenMenu(Dictionary<SsPart, SsState> ss)
        {
            DateTime? until = SsTimedOff.UntilUtc;
            List<SsPart> timed = SsTimedOff.Parts;
            foreach (SsPart p in SmartScreen.All)
            {
                SsState st = ss[p];
                string when = !st.On && until.HasValue && timed.Contains(p)
                    ? Tr.F(" — {0}에 다시 켜짐", " — back on at {0}", TimedOff.WhenLabel(until.Value.ToLocalTime(), DateTime.Now))
                    : "";
                ToolStripMenuItem it = ssStatusItems[p];
                it.Text = "● " + SmartScreen.Name(p) + ": " + (st.On ? Tr.T("켜짐", "on") : Tr.T("꺼짐", "off")) + when + "  (" + st.Detail + ")";
                it.ForeColor = st.On ? Color.FromArgb(46, 160, 67) : Color.FromArgb(218, 54, 51);
            }
            // 첨부 파일 관리자 정책으로 건너뛰는 형식 안내
            List<string> skipped = SmartScreen.BypassedTypes();
            ssStatusItems[SsPart.AppsFiles].ToolTipText = skipped.Count == 0 ? null
                : Tr.T("이 PC는 ", "This PC skips the SmartScreen check for ") + string.Join(", ", skipped.ToArray()) + Tr.T(" 파일의 SmartScreen 확인을 건너뜁니다 (첨부 파일 관리자 정책)", " files (Attachment Manager policy)");
            if (skipped.Count > 0) ssStatusItems[SsPart.AppsFiles].Text += "  · " + string.Join(" ", skipped.ToArray()) + Tr.T(" 제외", " excluded");
            var names = new List<string>();
            foreach (SsPart p in SmartScreen.Targets) names.Add(SmartScreen.Name(p));
            ssTargetsItem.Text = Tr.T("끌 항목: ", "Parts to turn off: ") + (names.Count == 0 ? Tr.T("(없음 — SmartScreen 설정에서 고르세요)", "(none — choose them in SmartScreen settings)") : string.Join(", ", names.ToArray()));
        }

        // 이 앱에서 SmartScreen을 바꾼 뒤 호출: 외부 변경으로 잘못 알리지 않도록 기준 상태를 새로 잡음
        void SsResync()
        {
            ssVersion++;
            ssKnown = new Dictionary<SsPart, bool>();
            foreach (var kv in SmartScreen.GetAll()) ssKnown[kv.Key] = kv.Value.On;
        }

        // 설정 창에서 바꾼 뒤 호출
        public static void SmartScreenChangedHere()
        {
            if (instance != null) instance.SsResync();
        }

        // parts를 켜거나 끔. 결과 줄(실패 포함)을 돌려줌
        List<string> SsApply(IEnumerable<SsPart> parts, bool on, string note, out List<SsPart> changed)
        {
            var lines = new List<string>();
            var failed = new List<string>();
            changed = new List<SsPart>();
            foreach (SsPart p in parts)
            {
                SsState st = SmartScreen.Get(p);
                if (st.On == on) continue;
                bool gpo = SmartScreen.PreferGpo(p);
                try
                {
                    SmartScreen.Set(p, on, gpo);
                    bool nowOn = SmartScreen.Get(p).On;
                    string line = "SmartScreen " + SmartScreen.Name(p) + " → " + (on ? Tr.T("켜짐", "on") : Tr.T("꺼짐", "off")) +
                                  (gpo && p != SsPart.Store ? Tr.T(" (그룹 정책)", " (Group Policy)") : p == SsPart.Edge && !on ? Tr.T(" (Edge 정책)", " (Edge policy)") : "");
                    if (nowOn != on)
                    {
                        failed.Add(line + Tr.T(" — 적용되지 않음", " — not applied"));
                        ActivityLog.Write(ActivityLog.Failed, line + Tr.T(" — 적용되지 않음", " — not applied"));
                        continue;
                    }
                    changed.Add(p);
                    lines.Add(line + (note != null ? " — " + note : ""));
                    ActivityLog.Write(ActivityLog.Internal, line + (note != null ? " — " + note : ""));
                }
                catch (Exception ex)
                {
                    string msg = ex is InvalidOperationException ? ex.Message : SmartScreen.Name(p) + ": " + ex.Message;
                    failed.Add(msg);
                    ActivityLog.Write(ActivityLog.Failed, "SmartScreen " + SmartScreen.Name(p) + " " + (on ? Tr.T("켜기", "turn on") : Tr.T("끄기", "turn off")) + " — " + msg);
                }
            }
            SsResync();
            if (failed.Count > 0) Balloon(6000, Tr.T("SmartScreen 변경 실패", "SmartScreen change failed"), Summarize(failed, null), ToolTipIcon.Error);
            return lines;
        }

        void SsTurnOff(int minutes)
        {
            List<SsPart> targets = SmartScreen.Targets;
            if (targets.Count == 0)
            {
                Balloon(4000, "SmartScreen", Tr.T("끌 항목이 없습니다. 'SmartScreen 설정'에서 끌 항목을 고르세요.", "No parts to turn off. Choose them in 'SmartScreen settings'."), ToolTipIcon.Info);
                return;
            }
            List<SsPart> changed;
            var lines = SsApply(targets, false, null, out changed);

            // 다시 켤 예약: 이번에 끈 항목 + 이미 꺼져 있던 대상도 함께 (직접 켤 때까지면 예약 없음)
            if (minutes > 0)
            {
                var parts = new List<SsPart>();
                foreach (SsPart p in targets) if (!SmartScreen.Get(p).On) parts.Add(p);
                SsTimedOff.Parts = parts;
                SsTimedOff.UntilUtc = DateTime.UtcNow.AddMinutes(minutes);
                lines.Add(Tr.F("{0} 뒤({1}) 다시 켜집니다", "Turns back on in {0} (at {1})",
                               TimedOff.DurationLabel(minutes), TimedOff.WhenLabel(DateTime.Now.AddMinutes(minutes), DateTime.Now)));
                ActivityLog.Write(ActivityLog.Internal, "SmartScreen: " + Tr.F("{0} 뒤 다시 켜도록 예약", "scheduled to turn back on in {0}", TimedOff.DurationLabel(minutes)));
            }
            else SsTimedOff.Clear();
            NotifyDone(lines);
        }

        // parts == null 이면 '끌 항목' 전체를 다시 켬
        void SsTurnOn(List<SsPart> parts, string note = null)
        {
            List<SsPart> changed;
            var lines = SsApply(parts ?? SmartScreen.Targets, true, note, out changed);
            SsTimedOff.Clear();
            NotifyDone(lines);
        }

        // 백그라운드 확인 결과 처리: 외부 변경 알림, 예약 시각이 되면 다시 켜기
        void HandleSmartScreen(Dictionary<SsPart, SsState> ss, bool stale)
        {
            if (ss == null || stale) return;

            if (ssKnown == null)
            {
                ssKnown = new Dictionary<SsPart, bool>();
                foreach (var kv in ss) ssKnown[kv.Key] = kv.Value.On;
            }
            else
            {
                var lines = new List<string>();
                foreach (var kv in ss)
                {
                    bool before;
                    if (ssKnown.TryGetValue(kv.Key, out before) && before != kv.Value.On)
                    {
                        string line = "SmartScreen " + SmartScreen.Name(kv.Key) + " " + (kv.Value.On ? Tr.T("켜짐", "on") : Tr.T("꺼짐", "off")) + "  (" + kv.Value.Detail + ")";
                        lines.Add(line);
                        ActivityLog.Write(ActivityLog.External, line);
                    }
                    ssKnown[kv.Key] = kv.Value.On;
                }
                if (lines.Count > 0 && watchNotify)
                {
                    watchTitle = ExternalTitle;
                    watchDetail = DateTime.Now.ToString("HH:mm:ss") + "  " + string.Join("\r\n" + DateTime.Now.ToString("HH:mm:ss") + "  ", lines.ToArray());
                    watchHasExclusion = false;
                    Balloon(8000, ExternalTitle, Summarize(lines, Tr.T("클릭하면 자세히 보기", "Click for details")), ToolTipIcon.Warning);
                }
            }

            // 시간 지정 끄기: 예약 시각이 지나면, 그 사이 다른 곳에서 켜지지 않은 항목만 다시 켬
            if (TimedOff.IsDue(SsTimedOff.UntilUtc, DateTime.UtcNow))
            {
                var parts = new List<SsPart>();
                foreach (SsPart p in SsTimedOff.Parts) if (!ss[p].On) parts.Add(p);
                SsTimedOff.Clear();
                if (parts.Count > 0)
                {
                    ActivityLog.Write(ActivityLog.Info, Tr.T("예약한 시간이 되어 SmartScreen을 다시 켭니다", "The scheduled time has come; turning SmartScreen back on"));
                    SsTurnOn(parts, Tr.T("예약한 시간이 되어 다시 켬", "turned back on at the scheduled time"));
                }
            }
        }

        static SmartScreenForm ssForm;

        static void ShowSmartScreenSettings()
        {
            if (ssForm == null || ssForm.IsDisposed)
            {
                ssForm = new SmartScreenForm();
                ssForm.Show();
            }
            else if (ssForm.WindowState == FormWindowState.Minimized) ssForm.WindowState = FormWindowState.Normal;
            ssForm.TopMost = true;
            ssForm.Activate();
            ssForm.TopMost = false;
        }
    }
}
