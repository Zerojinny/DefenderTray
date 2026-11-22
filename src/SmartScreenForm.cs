using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DefenderTray
{
    // SmartScreen 설정 창: 세 가지 SmartScreen을 각각 켜고 끄고, 그룹 정책 사용 여부와 트레이 '끄기' 대상을 정함
    class SmartScreenForm : Form
    {
        class Row
        {
            public SsPart Part;
            public CheckBox On, Gpo, Target;
            public Label State, Note;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Button btnApply;
        readonly Label lblResult;
        bool loading;

        public SmartScreenForm()
        {
            Text = Tr.T("SmartScreen 설정", "SmartScreen settings");
            Font = new Font("Malgun Gothic", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, 12, 12);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(12, 10) };
            root.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 10), ForeColor = SystemColors.GrayText,
                Text = Tr.T("Windows 보안 → 앱 및 브라우저 컨트롤 → 평판 기반 보호의 SmartScreen을 각각 켜고 끕니다. ", "Turns each SmartScreen part under Windows Security → App && browser control → Reputation-based protection on or off. ") +
                       Tr.T("끄면 확인되지 않은 프로그램·사이트에 대한 경고가 나오지 않으니 필요한 동안만 끄세요.", "While it is off, you get no warnings about unrecognized programs and sites, so turn it off only as long as needed.")
            });

            EventHandler changed = (s, e) => { if (!loading) { btnApply.Enabled = true; lblResult.Text = ""; } };
            var tips = new ToolTip();

            foreach (SsPart p in SmartScreen.All)
            {
                var g = new GroupBox
                {
                    Text = SmartScreen.Name(p), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(560, 0)
                };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
                var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                var r = new Row { Part = p };
                r.On = new CheckBox { AutoSize = true, Text = Tr.T("켜기", "On"), Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 12, 3) };
                r.State = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
                line.Controls.Add(r.On);
                line.Controls.Add(r.State);
                flow.Controls.Add(line);

                if (p != SsPart.Store)
                {
                    r.Gpo = new CheckBox
                    {
                        AutoSize = true, Margin = new Padding(22, 0, 3, 3),
                        Text = p == SsPart.AppsFiles ? Tr.T("그룹 정책(gpedit)으로도 설정", "Also set via Group Policy (gpedit)") : Tr.T("켤 때도 그룹 정책으로 고정", "Also lock with Group Policy when turning on")
                    };
                    tips.SetToolTip(r.Gpo, p == SsPart.AppsFiles
                        ? Tr.T("gpedit.msc '컴퓨터 구성 > 관리 템플릿 > Windows 구성 요소 > 파일 탐색기 > Windows Defender SmartScreen 구성'을\r\n", "Sets gpedit.msc 'Computer Configuration > Administrative Templates > Windows Components > File Explorer > Configure Windows Defender SmartScreen'\r\n") +
                          Tr.T("켜기 = 사용(경고), 끄기 = 사용 안 함 으로 설정합니다. 체크를 풀면 '구성되지 않음'으로 되돌립니다.", "to Enabled (warn) when on and Disabled when off. Unchecking returns it to 'Not configured'.")
                        : Tr.T("Edge 정책 SmartScreenEnabled를 켤 때도 1로 고정합니다. 체크를 풀면 켤 때 정책을 지워 Edge 자체 설정으로 돌아갑니다.", "Also pins the Edge policy SmartScreenEnabled to 1 when turning on. Unchecked, turning on removes the policy and Edge uses its own setting."));
                    r.Gpo.CheckedChanged += changed;
                    flow.Controls.Add(r.Gpo);
                }

                string noteText =
                    p == SsPart.Edge ? Tr.T("Edge는 설정을 자체 보관하므로 끌 때는 항상 Edge 정책을 씁니다. Edge를 다시 시작해야 반영됩니다.", "Edge keeps this setting itself, so turning it off always uses the Edge policy. Restart Edge for it to take effect.") :
                    p == SsPart.Store ? Tr.T("현재 사용자에게만 적용됩니다. 해당하는 그룹 정책 항목은 없습니다.", "Applies to the current user only. There is no matching Group Policy item.") : "";
                r.Note = new Label { AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(22, 2, 3, 0), Text = noteText, Visible = noteText.Length > 0 };
                flow.Controls.Add(r.Note);

                // 첨부 파일 관리자 정책으로 SmartScreen을 건너뛰는 형식이 있으면 안내 (켜 두어도 그 형식은 경고가 안 나옴)
                if (p == SsPart.AppsFiles)
                {
                    List<string> skipped = SmartScreen.BypassedTypes();
                    if (skipped.Count > 0)
                        flow.Controls.Add(new Label
                        {
                            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(22, 4, 3, 0),
                            ForeColor = Color.FromArgb(166, 77, 0),
                            Text = Tr.T("⚠ 이 PC는 ", "⚠ This PC skips the SmartScreen check for ") + string.Join(", ", skipped.ToArray()) + Tr.T(" 파일의 SmartScreen 확인을 건너뜁니다. ", " files. ") +
                                   Tr.T("켜 두어도 이 형식은 경고가 나오지 않습니다. (gpedit 사용자 구성 > 관리 템플릿 > Windows 구성 요소 > ", "These types get no warning even when SmartScreen is on. (gpedit User Configuration > Administrative Templates > Windows Components > ") +
                                   Tr.T("첨부 파일 관리자 > 낮은/중간 위험 파일 형식에 대한 포함 목록)", "Attachment Manager > Inclusion list for low/moderate risk file types)")
                        });
                }

                r.On.CheckedChanged += changed;
                g.Controls.Add(flow);
                root.Controls.Add(g);
                rows.Add(r);
            }

            // 트레이 메뉴 '끄기'(시간 지정 포함) 대상
            var gt = new GroupBox
            {
                Text = Tr.T("트레이 메뉴의 'SmartScreen ▸ 끄기'가 다룰 항목", "Parts handled by the tray menu 'SmartScreen ▸ Turn off'"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(560, 0)
            };
            var tflow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            foreach (Row r in rows)
            {
                r.Target = new CheckBox { AutoSize = true, Text = SmartScreen.Name(r.Part), Margin = new Padding(3, 3, 16, 3) };
                r.Target.CheckedChanged += changed;
                tflow.Controls.Add(r.Target);
            }
            gt.Controls.Add(tflow);
            root.Controls.Add(gt);

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

            LoadValues();
        }

        void LoadValues()
        {
            loading = true;
            var all = SmartScreen.GetAll();
            List<SsPart> targets = SmartScreen.Targets;
            foreach (Row r in rows)
            {
                SsState st = all[r.Part];
                r.On.Checked = st.On;
                r.State.Text = Tr.T("지금: ", "Now: ") + (st.On ? Tr.T("켜짐", "On") : Tr.T("꺼짐", "Off")) + "  (" + st.Detail + ")";
                r.State.ForeColor = st.On ? Color.FromArgb(46, 160, 67) : Color.FromArgb(218, 54, 51);
                if (r.Gpo != null)
                    r.Gpo.Checked = r.Part == SsPart.AppsFiles ? (st.LocalGpo || SmartScreen.PreferGpo(r.Part)) : SmartScreen.PreferGpo(r.Part);
                r.Target.Checked = targets.Contains(r.Part);

                r.On.Enabled = !st.OrgLocked;
                if (r.Gpo != null) r.Gpo.Enabled = !st.OrgLocked;
                if (st.OrgLocked)
                {
                    r.Note.Text = Tr.T("조직 정책으로 고정되어 있어 바꿀 수 없습니다. IT 관리자에게 문의하세요.", "Locked by organization policy and cannot be changed. Contact your IT administrator.");
                    r.Note.ForeColor = Color.FromArgb(196, 43, 28);
                    r.Note.Visible = true;
                }
            }
            btnApply.Enabled = false;
            loading = false;
        }

        void Apply()
        {
            var done = new List<string>();
            var failed = new List<string>();
            var targets = new List<SsPart>();

            foreach (Row r in rows)
            {
                if (r.Target.Checked) targets.Add(r.Part);
                SsState st = SmartScreen.Get(r.Part);
                bool wantOn = r.On.Checked;
                bool wantGpo = r.Gpo != null && r.Gpo.Checked;
                if (r.Gpo != null) SmartScreen.SetPreferGpo(r.Part, wantGpo);
                if (st.OrgLocked) continue;

                // 정책 사용 여부가 바뀐 경우도 다시 씀 (Edge는 끌 때 항상 정책)
                bool gpoNow = st.LocalGpo;
                bool gpoWanted = r.Part == SsPart.Edge ? (!wantOn || wantGpo) : wantGpo;
                bool need = st.On != wantOn || (r.Part != SsPart.Store && gpoNow != gpoWanted);
                if (!need) continue;

                string line = "SmartScreen " + SmartScreen.Name(r.Part) + " → " + (wantOn ? Tr.T("켜짐", "on") : Tr.T("꺼짐", "off")) +
                              (r.Part != SsPart.Store && gpoWanted ? Tr.T(" (그룹 정책)", " (Group Policy)") : "");
                try
                {
                    SmartScreen.Set(r.Part, wantOn, wantGpo);
                    if (SmartScreen.Get(r.Part).On != wantOn) { failed.Add(line + Tr.T(" — 적용되지 않음", " — not applied")); continue; }
                    done.Add(line);
                    if (wantOn)
                    {
                        // 켠 항목은 시간 지정 끄기 예약에서 뺌
                        var parts = SsTimedOff.Parts;
                        if (parts.Remove(r.Part)) { SsTimedOff.Parts = parts; if (parts.Count == 0) SsTimedOff.Clear(); }
                    }
                }
                catch (Exception ex) { failed.Add(line + " — " + ex.Message); }
            }
            SmartScreen.Targets = targets;

            foreach (string l in done) ActivityLog.Write(ActivityLog.Internal, l);
            foreach (string l in failed) ActivityLog.Write(ActivityLog.Failed, l);
            TrayContext.SmartScreenChangedHere();
            TrayContext.NotifyDone(done);
            LoadValues();

            if (failed.Count == 0) { lblResult.ForeColor = SystemColors.ControlText; lblResult.Text = Tr.T("적용했습니다.", "Applied."); }
            else
            {
                lblResult.ForeColor = Color.FromArgb(196, 43, 28);
                lblResult.Text = done.Count > 0 ? Tr.T("일부만 적용했습니다.", "Partly applied.") : Tr.T("적용되지 않았습니다.", "Not applied.");
                MessageBox.Show(this, string.Join("\r\n", failed.ToArray()), Tr.T("SmartScreen 설정", "SmartScreen settings"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
