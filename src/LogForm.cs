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
    // 활동 기록 보기 창
    class LogForm : Form
    {
        static readonly Color ExternalColor = Color.FromArgb(166, 77, 0);
        static readonly Color FailedColor = Color.FromArgb(196, 43, 28);
        const int MaxRows = 5000;

        readonly ListView list;
        readonly ComboBox kindFilter;
        readonly TextBox search;
        readonly Label summary;
        readonly System.Windows.Forms.Timer watch;
        DateTime lastStamp;
        long lastSize = -1;

        public LogForm()
        {
            Text = Tr.T("기록 — ", "Log — ") + AppInfo.DisplayName;
            Font = new Font("Malgun Gothic", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 460);
            MinimumSize = new Size(560, 300);
            ShowInTaskbar = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                ShowItemToolTips = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            list.Columns.Add(Tr.T("시각", "Time"), 160);
            list.Columns.Add(Tr.T("구분", "Type"), 80);
            list.Columns.Add(Tr.T("내용", "Details"), 540);
            list.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) CopySelected();
                else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in list.Items) it.Selected = true; }
            };
            list.Resize += (s, e) => FitTextColumn();

            // 위쪽: 구분 필터 + 검색
            var lblKind = new Label { Text = Tr.T("구분(&K):", "&Type:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 2, 0) };
            kindFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 4, 12, 0) };
            kindFilter.Items.AddRange(new object[] { Tr.T("전체", "All"), Tr.T("✓ 내부 (", "✓ Internal (") + AppInfo.ShortName + ")", Tr.T("⚠ 외부", "⚠ External"), Tr.T("✕ 실패", "✕ Failed"), Tr.T("정보", "Info") });
            kindFilter.SelectedIndex = 0;
            kindFilter.SelectedIndexChanged += (s, e) => Reload();
            var lblSearch = new Label { Text = Tr.T("검색(&S):", "Search(&S):"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 2, 0) };
            search = new TextBox { Width = 260, Margin = new Padding(0, 4, 4, 0) };
            search.TextChanged += (s, e) => Reload();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 6, 6, 4) };
            top.Controls.AddRange(new Control[] { lblKind, kindFilter, lblSearch, search });

            // 아래쪽: 요약 + 버튼
            summary = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
            var btnRefresh = new Button { Text = Tr.T("새로 고침(&R)", "Refresh(&R)"), AutoSize = true };
            var btnCopy = new Button { Text = Tr.T("복사(&C)", "Copy(&C)"), AutoSize = true };
            var btnFolder = new Button { Text = Tr.T("폴더 열기(&O)", "Open folder(&O)"), AutoSize = true };
            var btnClear = new Button { Text = Tr.T("기록 지우기(&D)...", "Clear &log..."), AutoSize = true };
            var btnClose = new Button { Text = Tr.T("닫기", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            btnRefresh.Click += (s, e) => Reload();
            btnCopy.Click += (s, e) => CopySelected();
            btnFolder.Click += (s, e) => OpenFolder();
            btnClear.Click += (s, e) => ClearLog();
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Right, WrapContents = false };
            buttons.Controls.AddRange(new Control[] { btnClose, btnClear, btnFolder, btnCopy, btnRefresh });
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            left.Controls.Add(summary);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 6, 8, 6) };
            bottom.Controls.Add(left);      // Fill 먼저
            bottom.Controls.Add(buttons);

            Controls.Add(list);             // Fill 먼저
            Controls.Add(top);
            Controls.Add(bottom);

            // 기록 파일이 바뀌면 자동으로 다시 읽음
            watch = new System.Windows.Forms.Timer { Interval = 2000 };
            watch.Tick += (s, e) => { if (FileChanged()) Reload(); };
            watch.Start();

            Reload();
        }

        bool FileChanged()
        {
            try
            {
                var fi = new FileInfo(ActivityLog.FilePath);
                DateTime stamp = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
                long size = fi.Exists ? fi.Length : 0;
                return stamp != lastStamp || size != lastSize;
            }
            catch { return false; }
        }

        static string KindLabel(string kind)
        {
            if (kind == ActivityLog.Internal) return Tr.T("✓ 내부", "✓ Internal");
            if (kind == ActivityLog.External) return Tr.T("⚠ 외부", "⚠ External");
            if (kind == ActivityLog.Failed) return Tr.T("✕ 실패", "✕ Failed");
            return kind;
        }

        string SelectedKind()
        {
            switch (kindFilter.SelectedIndex)
            {
                case 1: return ActivityLog.Internal;
                case 2: return ActivityLog.External;
                case 3: return ActivityLog.Failed;
                case 4: return ActivityLog.Info;
                default: return null;
            }
        }

        public void Reload()
        {
            try
            {
                var fi = new FileInfo(ActivityLog.FilePath);
                lastStamp = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
                lastSize = fi.Exists ? fi.Length : 0;
            }
            catch { }

            List<ActivityLog.Entry> all = ActivityLog.ReadAll();
            string kind = SelectedKind();
            string q = search.Text.Trim();
            int ext = 0, fail = 0, shown = 0;

            list.BeginUpdate();
            list.Items.Clear();
            foreach (ActivityLog.Entry e in all)
            {
                if (kind != null && e.Kind != kind) continue;
                if (q.Length > 0 && e.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (e.Kind == ActivityLog.External) ext++;
                if (e.Kind == ActivityLog.Failed) fail++;
                if (shown >= MaxRows) continue;
                var it = new ListViewItem(new[] { e.Time.ToString("yyyy-MM-dd HH:mm:ss"), KindLabel(e.Kind), e.Text })
                {
                    ToolTipText = e.Text,
                    UseItemStyleForSubItems = true
                };
                if (e.Kind == ActivityLog.External) it.ForeColor = ExternalColor;
                else if (e.Kind == ActivityLog.Failed) it.ForeColor = FailedColor;
                else if (e.Kind == ActivityLog.Info) it.ForeColor = SystemColors.GrayText;
                list.Items.Add(it);
                shown++;
            }
            if (shown == 0)
                list.Items.Add(new ListViewItem(new[] { "", "", all.Count == 0 ? Tr.T("(기록 없음)", "(no entries)") : Tr.T("(조건에 맞는 기록 없음)", "(no matching entries)") }) { ForeColor = SystemColors.GrayText });
            list.EndUpdate();

            int matched = 0;
            foreach (ActivityLog.Entry e in all)
                if ((kind == null || e.Kind == kind) && (q.Length == 0 || e.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) matched++;
            string s = (kind == null && q.Length == 0)
                ? string.Format(Tr.T("전체 {0}건", "{0} total"), all.Count)
                : string.Format(Tr.T("{0}건 / 전체 {1}건", "{0} of {1}"), matched, all.Count);
            if (ext > 0) s += Tr.T(" · 외부 ", " · external ") + ext;
            if (fail > 0) s += Tr.T(" · 실패 ", " · failed ") + fail;
            if (matched > MaxRows) s += Tr.T(" (최근 ", " (showing latest ") + MaxRows + Tr.T("건만 표시)", ")");
            summary.Text = s;
            summary.ForeColor = SystemColors.ControlText;
        }

        void CopySelected()
        {
            var sb = new StringBuilder();
            var source = list.SelectedItems.Count > 0 ? (System.Collections.IEnumerable)list.SelectedItems : list.Items;
            foreach (ListViewItem it in source)
                if (it.SubItems[0].Text.Length > 0)
                    sb.AppendLine(it.SubItems[0].Text + "\t" + it.SubItems[1].Text + "\t" + it.SubItems[2].Text);
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); } catch { }
            summary.Text = Tr.T("클립보드에 복사했습니다.", "Copied to the clipboard.");
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(ActivityLog.Folder);
                if (File.Exists(ActivityLog.FilePath)) Process.Start("explorer.exe", "/select,\"" + ActivityLog.FilePath + "\"");
                else Process.Start("explorer.exe", "\"" + ActivityLog.Folder + "\"");
            }
            catch { }
        }

        void ClearLog()
        {
            var r = MessageBox.Show(this, Tr.T("기록을 모두 지웁니다. 되돌릴 수 없습니다.\r\n\r\n", "This deletes the whole log and cannot be undone.\r\n\r\n") + ActivityLog.FilePath,
                Tr.T("기록 지우기", "Clear log"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.OK) return;
            ActivityLog.Clear();
            Reload();
        }

        void FitTextColumn()
        {
            int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - 4;
            if (w > 200) list.Columns[2].Width = w;
        }

        protected override void OnShown(EventArgs e)
        {
            list.Columns[0].Width = TextRenderer.MeasureText("2026-11-19 22:10:09", list.Font).Width + 20;
            list.Columns[1].Width = TextRenderer.MeasureText(Tr.T("✓ 내부", "✓ Internal"), list.Font).Width + 24;
            FitTextColumn();
            base.OnShown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }
    }
}
