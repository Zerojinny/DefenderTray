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
    // 폴더 제외를 추가할 때 검사 범위를 고르는 작은 대화 상자
    static class ScopeDialog
    {
        // true = 실시간만 제외, false = 모든 검사 제외, null = 취소
        public static bool? Ask(IWin32Window owner, string path)
        {
            bool lastAll = AppSettings.Get("PathScope") == "all";
            using (var f = new Form
            {
                Text = Tr.T("검사 범위 선택", "Choose scan scope"),
                Font = new Font("Malgun Gothic", 10f),
                AutoScaleMode = AutoScaleMode.Font,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = owner == null,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
                TopMost = owner == null
            })
            {
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
                var where = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 0, 0, 10), Text = Tr.T("제외할 위치:  ", "Location to exclude:  ") + path };
                var rbRt = new RadioButton { AutoSize = true, Text = Tr.T("실시간 보호에서만 제외 (권장)(&R)", "Exclude from real-time protection only (recommended)(&R)"), Checked = !lastAll, Margin = new Padding(0, 0, 0, 0) };
                var dRt = new Label { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 10),
                    Text = Tr.T("복사하거나 열 때는 검사하지 않아 빠르고, 수동 검사와 예약 검사에서는 검사합니다.", "Copying and opening files skip scanning, so they stay fast; manual and scheduled scans still scan them.") };
                var rbAll = new RadioButton { AutoSize = true, Text = Tr.T("모든 검사에서 제외(&A)", "Exclude from all scans(&A)"), Checked = lastAll, Margin = new Padding(0, 0, 0, 0) };
                var dAll = new Label { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 0, 0, 12),
                    Text = Tr.T("실시간 보호, 수동 검사, 예약 검사 모두 건너뜁니다. Windows 보안 앱에서도 보이는 기존 방식입니다.", "Real-time protection, manual scans and scheduled scans all skip it. This is the classic behavior, also shown in the Windows Security app.") };
                var ok = new Button { Text = Tr.T("추가", "Add"), AutoSize = true, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = Tr.T("취소", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(0) };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                flow.Controls.AddRange(new Control[] { where, rbRt, dRt, rbAll, dAll, buttons });
                f.Controls.Add(flow);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                AppSettings.Set("PathScope", rbAll.Checked ? "all" : "realtime");
                return rbRt.Checked;
            }
        }
    }

    enum RestoreMode { AllowThreat, ExcludeFile, CopyTo }

    // 복원 방식을 고르는 대화 상자
    static class RestoreDialog
    {
        public static RestoreMode? Ask(IWin32Window owner, QuarantineItem it)
        {
            using (var f = new Form
            {
                Text = Tr.T("격리 항목 복원", "Restore quarantined item"),
                Font = new Font("Malgun Gothic", 10f),
                AutoScaleMode = AutoScaleMode.Font,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            })
            {
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(12, 10), Margin = new Padding(0, 0, 12, 12) };
                Func<string, Color, Padding, Label> L = (text, color, m) =>
                    new Label { AutoSize = true, MaximumSize = new Size(560, 0), Text = text, ForeColor = color, Margin = m };
                flow.Controls.Add(L(Tr.T("위협:  ", "Threat:  ") + it.Name + Tr.T("  (심각도 ", "  (severity ") + Quarantine.SeverityText(it.Severity) + ")", SystemColors.ControlText, new Padding(0, 0, 0, 2)));
                flow.Controls.Add(L(Tr.T("원래 위치:  ", "Original location:  ") + it.Path, SystemColors.ControlText, new Padding(0, 0, 0, 10)));
                flow.Controls.Add(L(Tr.T("믿을 수 있는 파일일 때만 복원하세요. 그냥 복원하면 실시간 보호가 다시 격리할 수 있어 아래 방식 중 하나를 고릅니다.", "Restore only files you trust. A plain restore may be quarantined again by real-time protection, so choose one of the options below."),
                    Color.FromArgb(166, 77, 0), new Padding(0, 0, 0, 10)));

                // 범위가 가장 좁은 '이 파일만 제외'를 기본·권장으로
                var rbExcl = new RadioButton { AutoSize = true, Text = Tr.T("이 파일만 제외하고 원래 위치로 복원 (권장)(&E)", "Exclude this file only and restore to the original location (recommended)(&E)"), Checked = true, Margin = new Padding(0) };
                var rbAllow = new RadioButton { AutoSize = true, Text = Tr.T("이 위협을 허용하고 원래 위치로 복원(&A)", "Allow this threat and restore to the original location(&A)"), Margin = new Padding(0) };
                var rbCopy = new RadioButton { AutoSize = true, Text = Tr.T("다른 폴더로 사본 복원 — 격리는 유지(&C)", "Restore a copy to another folder — keep it quarantined(&C)"), Margin = new Padding(0) };
                flow.Controls.Add(rbExcl);
                flow.Controls.Add(L(Tr.T("이 파일 경로 하나만 모든 검사에서 제외합니다. 다른 위치의 같은 위협은 계속 탐지합니다.", "Excludes only this file path from all scans. The same threat elsewhere is still detected."), SystemColors.GrayText, new Padding(20, 0, 0, 8)));
                flow.Controls.Add(rbAllow);
                flow.Controls.Add(L(Tr.T("Windows 보안 앱의 '장치에서 허용'과 같습니다. 이 PC 어디서든 같은 위협 이름은 앞으로 탐지하지 않습니다.", "Same as 'Allow on device' in the Windows Security app. The same threat name will no longer be detected anywhere on this PC."), SystemColors.GrayText, new Padding(20, 0, 0, 8)));
                flow.Controls.Add(rbCopy);
                flow.Controls.Add(L(Tr.T("원본은 격리된 채로 두고 사본만 꺼냅니다. 고른 폴더가 제외 폴더가 아니면 사본이 바로 다시 격리될 수 있습니다.", "Keeps the original quarantined and extracts only a copy. If the chosen folder is not excluded, the copy may be quarantined again right away."), SystemColors.GrayText, new Padding(20, 0, 0, 12)));
                if (!it.IsFile)
                {
                    rbAllow.Enabled = rbExcl.Enabled = rbCopy.Enabled = false;
                    flow.Controls.Add(L(Tr.T("파일이 아닌 항목이라 이 앱에서는 복원할 수 없습니다. Windows 보안의 보호 기록을 사용하세요.", "This item is not a file, so this app cannot restore it. Use Protection history in Windows Security."), Color.FromArgb(196, 43, 28), new Padding(0, 0, 0, 8)));
                }

                var ok = new Button { Text = Tr.T("복원", "Restore"), AutoSize = true, DialogResult = DialogResult.OK, Enabled = it.IsFile };
                var cancel = new Button { Text = Tr.T("취소", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                flow.Controls.Add(buttons);
                f.Controls.Add(flow);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Padding = new Padding(0, 0, 12, 12);

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                if (rbAllow.Checked) return RestoreMode.AllowThreat;
                if (rbCopy.Checked) return RestoreMode.CopyTo;
                return RestoreMode.ExcludeFile;
            }
        }
    }
}
