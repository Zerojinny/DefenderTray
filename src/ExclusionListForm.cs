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
    // ===== 현재 검사 제외 목록 창 =====
    class ExclusionListForm : Form
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
        const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        static readonly Color MissingColor = Color.FromArgb(196, 43, 28);

        readonly ListView list;
        readonly ImageList images;
        readonly Label summary, missingLabel;
        readonly ListViewGroup grpProc, grpPath;

        public ExclusionListForm()
        {
            Text = Tr.T("현재 검사 제외 목록", "Current scan exclusions");
            Font = new Font("Malgun Gothic", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 460);
            MinimumSize = new Size(640, 300);
            ShowInTaskbar = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int iconPx = Math.Max(16, (int)Math.Round(16 * DeviceDpi / 96.0));
            images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(iconPx, iconPx) };

            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                SmallImageList = images,
                ShowItemToolTips = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            list.Columns.Add(Tr.T("이름", "Name"), 200);
            list.Columns.Add(Tr.T("전체 경로", "Full path"), 440);
            list.Columns.Add(Tr.T("상태", "Status"), 110);
            list.Columns.Add(Tr.T("검사 범위", "Scan scope"), 120);
            grpProc = new ListViewGroup("proc", Tr.T("프로세스 제외 — 이 프로그램이 여는 파일은 실시간·예약 검사하지 않음", "Process exclusions — files opened by these programs are skipped by real-time and scheduled scans"));
            grpPath = new ListViewGroup("path", Tr.T("폴더·파일 제외 — 이 위치의 파일은 검사 범위에 따라 검사하지 않음", "File/folder exclusions — files here are skipped according to the scan scope"));
            list.Groups.Add(grpProc);
            list.Groups.Add(grpPath);
            list.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) CopySelected();
                else if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveSelected(); }
            };
            list.DoubleClick += (s, e) => OpenSelectedLocation();
            list.SelectedIndexChanged += (s, e) => UpdateRemoveButton();
            list.Resize += (s, e) => FitPathColumn();

            summary = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };

            var btnRefresh = new Button { Text = Tr.T("새로 고침(&R)", "Refresh(&R)"), AutoSize = true };
            var btnCopy = new Button { Text = Tr.T("경로 복사(&C)", "Copy path(&C)"), AutoSize = true };
            var btnOpen = new Button { Text = Tr.T("위치 열기(&O)", "Open location(&O)"), AutoSize = true };
            var btnClose = new Button { Text = Tr.T("닫기", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            btnRefresh.Click += (s, e) => Reload();
            btnCopy.Click += (s, e) => CopySelected();
            btnOpen.Click += (s, e) => OpenSelectedLocation();
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Right,
                WrapContents = false
            };
            buttons.Controls.AddRange(new Control[] { btnClose, btnCopy, btnOpen, btnRefresh });

            missingLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 4, 4), ForeColor = MissingColor };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 6, 8, 6) };
            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            left.Controls.Add(summary);
            left.Controls.Add(missingLabel);
            // Fill 영역을 먼저 추가해야 오른쪽 버튼 줄이 가려지지 않음 (WinForms 도킹 순서)
            bottom.Controls.Add(left);
            bottom.Controls.Add(buttons);

            // 위쪽 도구 줄: 추가/제거 + 사용 안내
            var btnAddProc = new Button { Text = Tr.T("실행 파일 추가(&P)...", "Add program(&P)..."), AutoSize = true, UseVisualStyleBackColor = true };
            var btnAddPath = new Button { Text = Tr.T("폴더 추가(&F)...", "Add folder(&F)..."), AutoSize = true, UseVisualStyleBackColor = true };
            btnRemove = new Button { Text = Tr.T("제거(&D)", "Re&move"), AutoSize = true, UseVisualStyleBackColor = true, Enabled = false };
            btnAddProc.Click += (s, e) => AddProcess();
            btnAddPath.Click += (s, e) => AddPath();
            btnRemove.Click += (s, e) => RemoveSelected();
            // 검사 범위 바꾸기 / 선택 위치 검사 — 앞의 추가·제거 묶음과 간격을 둠
            btnScope = new Button { Text = Tr.T("검사 범위 바꾸기(&M)...", "C&hange scan scope..."), AutoSize = true, UseVisualStyleBackColor = true, Enabled = false, Margin = new Padding(18, 3, 3, 3) };
            btnScan = new Button { Text = Tr.T("검사(&S)", "Scan(&S)"), AutoSize = true, UseVisualStyleBackColor = true, Enabled = false };
            btnScope.Click += (s, e) => ChangeScope();
            btnScan.Click += (s, e) => ScanSelected();
            var tips = new ToolTip();
            tips.SetToolTip(btnRemove, Tr.T("선택한 항목을 제외에서 뺍니다 (Delete 키)", "Removes the selected items from exclusions (Delete key)"));
            tips.SetToolTip(btnScope, Tr.T("선택한 폴더 제외를 '실시간만 제외' ↔ '모든 검사 제외'로 바꿉니다", "Switches the selected folder exclusions between 'Real-time only' and 'All scans'"));
            tips.SetToolTip(btnScan, Tr.T("선택한 폴더를 Defender로 검사합니다. '실시간만 제외' 항목만 실제로 검사됩니다", "Scans the selected folders with Defender. Only 'Real-time only' items are actually scanned"));
            tips.SetToolTip(list, Tr.T("더블클릭: 탐색기에서 보기 · Delete: 제거 · Ctrl+C: 경로 복사", "Double-click: show in Explorer · Delete: remove · Ctrl+C: copy path"));
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(6, 6, 6, 2)
            };
            toolbar.Controls.AddRange(new Control[] { btnAddProc, btnAddPath, btnRemove, btnScope, btnScan });

            // 드라이브 문자로 등록된 네트워크 제외가 있을 때만 나타나는 안내 줄
            infoBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 6, 10, 6),
                BackColor = SystemColors.Info,
                Visible = false
            };
            infoLabel = new Label { Dock = DockStyle.Fill, ForeColor = SystemColors.InfoText, TextAlign = ContentAlignment.MiddleLeft };
            var btnConvert = new Button { Text = Tr.T("실제 경로로 변환(&N)", "Convert to real path(&N)"), AutoSize = true, Dock = DockStyle.Right, UseVisualStyleBackColor = true };
            btnConvert.Click += (s, e) => ConvertDriveLetterEntries();
            infoBar.Controls.Add(infoLabel);   // Fill 먼저
            infoBar.Controls.Add(btnConvert);

            Controls.Add(list);
            Controls.Add(infoBar);
            // 조직 정책 경고 줄 (Reload에서 내용·표시 결정)
            policyBar = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 44, Visible = false,
                Padding = new Padding(10, 4, 10, 4), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(253, 231, 233), ForeColor = Color.FromArgb(164, 38, 44)
            };
            Controls.Add(policyBar);
            Controls.Add(toolbar);
            Controls.Add(bottom);

            Reload();
        }

        readonly Button btnRemove, btnScope, btnScan;

        // ===== 추가 / 제거 =====
        List<ListViewItem> RemovableSelection()
        {
            var r = new List<ListViewItem>();
            foreach (ListViewItem it in list.SelectedItems)
                if (it.Tag is string) r.Add(it);
            return r;
        }

        // 선택한 항목 중 폴더·파일 제외만
        List<ListViewItem> PathSelection()
        {
            var r = new List<ListViewItem>();
            foreach (ListViewItem it in RemovableSelection())
                if (it.Group == grpPath) r.Add(it);
            return r;
        }

        void UpdateRemoveButton()
        {
            btnRemove.Enabled = RemovableSelection().Count > 0;
            bool anyPath = PathSelection().Count > 0;
            btnScope.Enabled = anyPath;
            btnScan.Enabled = anyPath;
        }

        // 선택한 폴더 제외의 검사 범위를 바꿈: 새 형식을 먼저 추가한 뒤 기존 항목 삭제
        void ChangeScope()
        {
            var items = PathSelection();
            if (items.Count == 0) return;

            bool allRealtime = true;
            foreach (ListViewItem it in items) if (!ExclusionScope.IsRealtimeOnly((string)it.Tag)) allRealtime = false;
            // 선택이 모두 '실시간만'이면 '모든 검사'로, 아니면 '실시간만'으로
            bool toRealtime = !allRealtime;
            string target = toRealtime ? Tr.T("실시간만 제외", "Real-time only") : Tr.T("모든 검사 제외", "All scans");

            var plan = new List<KeyValuePair<string, string>>();
            var sb = new StringBuilder();
            foreach (ListViewItem it in items)
            {
                string v = (string)it.Tag;
                string nv = ExclusionScope.Make(ExclusionScope.BasePath(v), toRealtime);
                if (string.Equals(nv, v, StringComparison.Ordinal)) continue;
                plan.Add(new KeyValuePair<string, string>(v, nv));
                sb.AppendLine(ExclusionScope.BasePath(v) + "   " + ExclusionScope.Label(v) + " → " + target);
            }
            if (plan.Count == 0) return;

            string note = toRealtime
                ? Tr.T("실시간만 제외: 복사·열기 때는 검사하지 않고, 수동 검사와 예약 검사에서는 검사합니다.\r\n(Windows 보안 앱의 제외 목록에서는 이 형식을 관리할 수 없습니다)", "Real-time only: copying and opening skip scanning; manual and scheduled scans still scan.\r\n(The Windows Security app cannot manage this form in its exclusion list)")
                : Tr.T("모든 검사 제외: 실시간 보호, 수동 검사, 예약 검사 모두 건너뜁니다.", "All scans: real-time protection, manual scans and scheduled scans all skip it.");
            var r = MessageBox.Show(this, Tr.T("검사 범위를 \"", "Change scan scope to \"") + target + Tr.T("\"(으)로 바꿉니다.\r\n\r\n", "\".\r\n\r\n") + sb + "\r\n" + note,
                Tr.T("검사 범위 바꾸기", "Change scan scope"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            var done = new List<string>();
            var failed = new List<string>();
            foreach (var p in plan)
            {
                try
                {
                    Defender.AddExclusionPath(p.Value);      // 먼저 추가 — 실패해도 기존 제외는 남음
                    Defender.RemoveExclusionPath(p.Key);
                    done.Add(ExclusionScope.BasePath(p.Key) + " → " + target);
                }
                catch (Exception ex) { failed.Add(ExclusionScope.BasePath(p.Key) + " : " + Defender.ExplainError(ex)); }
            }
            AfterChange(plan.Count == 1 ? plan[0].Value : null, done.Count + Tr.T("개 항목의 검사 범위를 바꿨습니다.", " item(s): scan scope changed."));
            TrayContext.NotifyDone(done);
            if (failed.Count > 0)
                MessageBox.Show(this, Tr.T("일부 항목을 바꾸지 못했습니다.\r\n\r\n", "Some items could not be changed.\r\n\r\n") + string.Join("\r\n", failed.ToArray()),
                    Tr.T("검사 범위 바꾸기", "Change scan scope"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 선택한 폴더를 Defender로 검사. '모든 검사 제외' 항목은 검사해도 건너뛰므로 먼저 안내
        void ScanSelected()
        {
            var items = PathSelection();
            if (items.Count == 0) return;

            var blocked = new List<string>();
            var targets = new List<string>();
            foreach (ListViewItem it in items)
            {
                string v = (string)it.Tag;
                string b = ExclusionScope.BasePath(v);
                if (ExclusionScope.IsRealtimeOnly(v)) targets.Add(NetworkTarget(b) ?? b);
                else blocked.Add(b);
            }

            if (blocked.Count > 0)
            {
                var r = MessageBox.Show(this,
                    Tr.T("다음 항목은 '모든 검사 제외'라서 검사해도 Defender가 건너뜁니다.\r\n\r\n", "These items are 'All scans' exclusions, so Defender skips them even when scanned.\r\n\r\n") +
                    string.Join("\r\n", blocked.ToArray()) + "\r\n\r\n" +
                    Tr.T("검사하려면 먼저 [검사 범위 바꾸기]로 '실시간만 제외'로 바꾸세요.", "To scan them, first switch them to 'Real-time only' with [Change scan scope].") +
                    (targets.Count > 0 ? Tr.T("\r\n\r\n나머지 ", "\r\n\r\nScan only the remaining ") + targets.Count + Tr.T("개 항목만 검사할까요?", " item(s)?") : ""),
                    Tr.T("검사", "Scan"), targets.Count > 0 ? MessageBoxButtons.OKCancel : MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (targets.Count == 0 || r != DialogResult.OK) return;
            }

            if (!TrayContext.RequestScan(targets))
            {
                summary.Text = Tr.T("트레이 프로그램이 실행 중이 아니어서 검사를 시작할 수 없습니다.", "The tray program is not running, so the scan cannot start.");
                return;
            }
            summary.Text = targets.Count + Tr.T("개 위치 검사를 시작했습니다. 끝나면 알림으로 알려 드립니다.", " location(s): scan started. You'll be notified when it finishes.");
            summary.ForeColor = SystemColors.ControlText;
            missingLabel.Text = "";
        }

        void AddProcess()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Tr.T("제외에 추가할 실행 파일 선택", "Choose a program to exclude"),
                Filter = Tr.T("실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*", "Programs (*.exe)|*.exe|All files (*.*)|*.*")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!ConfirmProcessExclusion(this, dlg.FileName)) return;
                try
                {
                    Defender.AddExclusionProcess(dlg.FileName);
                    AfterChange(dlg.FileName, Path.GetFileName(dlg.FileName) + Tr.T(" 을(를) 제외에 추가했습니다.", " added to exclusions."));
                    TrayContext.NotifyDone(new[] { DefenderWatch.Describe("Processes", true, dlg.FileName) });
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        void AddPath()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = Tr.T("검사에서 제외할 폴더 선택\r\n네트워크 드라이브가 안 보이면 '네트워크'에서 서버를 찾거나, 탐색기에서 폴더를 오른쪽 클릭해 추가하세요.", "Choose a folder to exclude from scans\r\nIf a network drive is not listed, find the server under 'Network', or right-click the folder in Explorer to add it."),
                ShowNewFolderButton = false
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                bool? realtimeOnly = ScopeDialog.Ask(this, dlg.SelectedPath);
                if (realtimeOnly == null) return;
                try
                {
                    string registered = Defender.AddExclusionPathResolved(dlg.SelectedPath, realtimeOnly.Value);
                    string shownPath = ExclusionScope.BasePath(registered);
                    bool converted = !string.Equals(shownPath.TrimEnd('\\'), dlg.SelectedPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    AfterChange(registered, converted
                        ? Tr.T("네트워크 드라이브라 실제 경로로 추가했습니다: ", "Network drive, so it was added as the real path: ") + shownPath
                        : Tr.T("폴더를 제외에 추가했습니다: ", "Folder added to exclusions: ") + shownPath);
                    TrayContext.NotifyDone(converted
                        ? new[] { DefenderWatch.Describe("Paths", true, registered), Tr.T("(네트워크 드라이브 ", "(network drive ") + dlg.SelectedPath + Tr.T(" → 실제 경로로 등록)", " → registered as the real path)") }
                        : new[] { DefenderWatch.Describe("Paths", true, registered) });
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        void RemoveSelected()
        {
            var items = RemovableSelection();
            if (items.Count == 0) return;

            var sb = new StringBuilder();
            foreach (ListViewItem it in items)
            {
                string v = (string)it.Tag;
                sb.AppendLine(it.Group == grpPath ? ExclusionScope.BasePath(v) + "  (" + ExclusionScope.Label(v) + ")" : v);
            }
            var r = MessageBox.Show(this,
                Tr.T("다음 ", "Remove the following ") + items.Count + Tr.T("개 항목을 제외 목록에서 제거합니다. 제거하면 다시 검사 대상이 됩니다.\r\n\r\n", " item(s) from exclusions? They will be scanned again.\r\n\r\n") + sb,
                Tr.T("검사 제외 제거", "Remove exclusions"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            var failed = new List<string>();
            var done = new List<string>();
            foreach (ListViewItem it in items)
            {
                string v = (string)it.Tag;
                bool proc = it.Group == grpProc;
                try
                {
                    if (proc) Defender.RemoveExclusionProcess(v);
                    else Defender.RemoveExclusionPath(v);
                    done.Add(DefenderWatch.Describe(proc ? "Processes" : "Paths", false, v));
                }
                catch (Exception ex) { failed.Add(v + " : " + Defender.ExplainError(ex)); }
            }
            AfterChange(null, done.Count + Tr.T("개 항목을 제외에서 제거했습니다.", " item(s) removed from exclusions."));
            TrayContext.NotifyDone(done);
            if (failed.Count > 0)
                MessageBox.Show(this, Tr.T("일부 항목을 제거하지 못했습니다.\r\n\r\n", "Some items could not be removed.\r\n\r\n") + string.Join("\r\n", failed.ToArray()),
                    Tr.T("검사 제외 제거", "Remove exclusions"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 목록을 다시 읽고, 추가한 항목을 선택해 보여 주며, 결과 문구를 아래쪽에 표시
        void AfterChange(string selectValue, string notice)
        {
            Reload();
            if (selectValue != null)
            {
                foreach (ListViewItem it in list.Items)
                {
                    if (string.Equals(it.Tag as string, selectValue.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(it.Tag as string, selectValue, StringComparison.OrdinalIgnoreCase))
                    {
                        it.Selected = true;
                        it.Focused = true;
                        it.EnsureVisible();
                        break;
                    }
                }
            }
            list.Focus();
            summary.Text = notice;
            summary.ForeColor = SystemColors.ControlText;
            missingLabel.Text = "";
        }

        void ShowError(Exception ex)
        {
            MessageBox.Show(this, Defender.ExplainError(ex), Tr.T("변경 실패", "Change failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // 탐색기(explorer.exe)를 제외하려 할 때 경고. 계속하면 true
        public static bool ConfirmProcessExclusion(IWin32Window owner, string path)
        {
            if (!string.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase)) return true;
            var r = MessageBox.Show(owner,
                Tr.T("탐색기(explorer.exe)를 제외하면 탐색기로 복사·실행·열람하는 거의 모든 파일이 ", "Excluding File Explorer (explorer.exe) removes almost every file you copy, run or open with Explorer ") +
                Tr.T("실시간 검사에서 빠집니다. 사실상 보호를 크게 넓게 끄는 효과라 권장하지 않습니다.\r\n\r\n", "from real-time scanning. That effectively turns protection off very broadly, so it is not recommended.\r\n\r\n") +
                Tr.T("복사 속도 때문이라면 FastCopy 같은 복사 프로그램만 제외하는 편이 훨씬 안전합니다.\r\n\r\n", "If copy speed is the concern, excluding only a copy tool such as FastCopy is much safer.\r\n\r\n") +
                Tr.T("그래도 탐색기를 제외하시겠습니까?", "Exclude File Explorer anyway?"),
                Tr.T("탐색기 제외 — 주의", "Exclude File Explorer — caution"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return r == DialogResult.Yes;
        }

        readonly Panel infoBar;
        Label policyBar;
        readonly Label infoLabel;
        readonly List<string> driveLetterEntries = new List<string>();

        void ConvertDriveLetterEntries()
        {
            if (driveLetterEntries.Count == 0) return;
            var sb = new StringBuilder();
            foreach (string v in driveLetterEntries)
                sb.AppendLine(ExclusionScope.BasePath(v) + "  →  " + NetPath.ToUnc(ExclusionScope.BasePath(v)));
            var r = MessageBox.Show(this,
                Tr.T("다음 제외 항목을 실제 네트워크 경로로 바꿉니다.\r\n", "The following exclusions will be changed to real network paths.\r\n") +
                Tr.T("(새 경로를 추가한 뒤 드라이브 문자 항목을 삭제합니다)\r\n\r\n", "(The new path is added first, then the drive-letter entry is removed)\r\n\r\n") + sb +
                Tr.T("\r\nMicrosoft 문서는 연결된 네트워크 드라이브 대신 실제 네트워크 경로로 제외하도록 권장합니다.", "\r\nMicrosoft recommends excluding the real network path instead of a mapped network drive."),
                Tr.T("네트워크 제외 변환", "Convert network exclusions"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;

            var failed = new List<string>();
            var done = new List<string>();
            foreach (string v in driveLetterEntries)
            {
                string uncBase = NetPath.ToUnc(ExclusionScope.BasePath(v));
                if (uncBase == null) continue;
                string unc = ExclusionScope.WithBase(v, uncBase);   // 검사 범위(조건)는 그대로 유지
                try
                {
                    Defender.AddExclusionPath(unc);      // 먼저 추가 — 실패해도 기존 제외는 남음
                    done.Add(DefenderWatch.Describe("Paths", true, unc));
                    Defender.RemoveExclusionPath(v);
                    done.Add(DefenderWatch.Describe("Paths", false, v));
                }
                catch (Exception ex) { failed.Add(v + " : " + Defender.ExplainError(ex)); }
            }
            Reload();
            TrayContext.NotifyDone(done);
            if (failed.Count > 0)
                MessageBox.Show(this, Tr.T("일부 항목을 변환하지 못했습니다.\r\n\r\n", "Some items could not be converted.\r\n\r\n") + string.Join("\r\n", failed.ToArray()),
                    Tr.T("네트워크 제외 변환", "Convert network exclusions"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 네트워크 경로 확인 결과를 UI 스레드로 넘기는 통로, 새로 고침 세대 번호(이전 결과 무시용)
        readonly SynchronizationContext ui = new WindowsFormsSynchronizationContext();
        int generation;
        int missingCount, offlineCount, pendingCount;

        public void Reload()
        {
            generation++;
            missingCount = offlineCount = pendingCount = 0;

            list.BeginUpdate();
            list.Items.Clear();
            images.Images.Clear();

            string[] procs = Clean(Defender.GetExclusionProcesses());
            string[] paths = Clean(Defender.GetExclusionPaths());
            bool denied = procs == null || paths == null;
            if (denied) { procs = new string[0]; paths = new string[0]; }

            foreach (string p in procs) AddRow(p, grpProc, true);
            foreach (string p in paths) AddRow(p, grpPath, false);

            if (CountIn(grpProc) == 0) AddEmpty(grpProc, denied);
            if (CountIn(grpPath) == 0) AddEmpty(grpPath, denied);

            list.EndUpdate();

            // 드라이브 문자(Y:\...)로 등록된 네트워크 폴더 제외 찾기
            driveLetterEntries.Clear();
            foreach (string p in paths)
            {
                string b = ExclusionScope.BasePath(p);
                if (b.Length >= 2 && b[1] == ':' && NetPath.ToUnc(b) != null) driveLetterEntries.Add(p);
            }
            infoLabel.Text = driveLetterEntries.Count == 0 ? "" :
                string.Format(Tr.T("네트워크 드라이브 문자로 등록된 제외 {0}개 — 실제 네트워크 경로로 변환을 권장합니다.", "{0} exclusion(s) use a mapped drive letter — converting to the real network path is recommended."),
                    driveLetterEntries.Count);
            infoBar.Visible = driveLetterEntries.Count > 0;

            // 조직 정책: 로컬 제외 무시 / 제외 목록 숨김
            bool ignored = ManagedPolicy.LocalExclusionsIgnored();
            bool hidden = ManagedPolicy.ExclusionsHidden();
            policyBar.Text = ignored
                ? Tr.T("조직 정책이 이 PC에서 추가한 제외를 무시하도록 설정되어 있습니다. 여기서 추가한 제외는 저장되지만 실제로는 적용되지 않습니다. (그룹 정책: 로컬 관리자 병합 사용 안 함)", "Organization policy ignores exclusions added on this PC. Exclusions added here are saved but do not take effect. (Group Policy: local administrator merge disabled)")
                : hidden ? Tr.T("조직 정책으로 제외 목록이 숨겨져 있어 관리자 권한으로도 볼 수 없습니다.", "Organization policy hides the exclusion list, even from administrators.") : "";
            policyBar.Visible = ignored || hidden;

            UpdateRemoveButton();
            summary.ForeColor = SystemColors.ControlText;
            int rtOnly = 0;
            foreach (string p in paths) if (ExclusionScope.IsRealtimeOnly(p)) rtOnly++;
            summary.Text = denied
                ? (hidden ? Tr.T("조직 정책으로 제외 목록이 숨겨져 있습니다.", "Organization policy hides the exclusion list.") : Tr.T("목록을 읽으려면 관리자 권한이 필요합니다.", "Administrator rights are required to read the list."))
                : string.Format(Tr.T("프로세스 {0}개 · 폴더/파일 {1}개 (실시간만 {2} · 모든 검사 {3})", "Processes: {0} · Files/folders: {1} (real-time only {2} · all scans {3})"),
                    procs.Length, paths.Length, rtOnly, paths.Length - rtOnly);
            UpdateProblemLabel();
        }

        void UpdateProblemLabel()
        {
            var parts = new List<string>();
            if (missingCount > 0) parts.Add(Tr.T("경로 없음 ", "Missing path: ") + missingCount + Tr.T("개", ""));
            if (offlineCount > 0) parts.Add(Tr.T("네트워크 연결 안 됨 ", "Network offline: ") + offlineCount + Tr.T("개", ""));
            if (pendingCount > 0) parts.Add(Tr.T("네트워크 확인 중 ", "Checking network: ") + pendingCount + Tr.T("개", ""));
            missingLabel.ForeColor = (missingCount + offlineCount) > 0 ? MissingColor : SystemColors.GrayText;
            missingLabel.Text = parts.Count > 0 ? "·  " + string.Join(" · ", parts.ToArray()) : "";
        }

        // 네트워크 경로면 실제로 확인할 경로(UNC)를, 아니면 null을 돌려줌.
        // 관리자 권한 프로그램에는 일반 세션에서 연결한 네트워크 드라이브 문자가 보이지 않으므로
        // HKCU\Network\<문자>\RemotePath 에 저장된 \\서버\공유 주소로 바꿔서 확인한다.
        static string NetworkTarget(string value)
        {
            if (value.StartsWith(@"\\")) return value;
            if (value.Length < 2 || value[1] != ':') return null;
            string unc = NetPath.ToUnc(value);
            if (unc != null) return unc;
            try
            {
                if (new DriveInfo(value.Substring(0, 1)).DriveType == DriveType.Network) return value;
            }
            catch { }
            return null;
        }

        static bool DriveRootExists(string value)
        {
            if (value.Length < 2 || value[1] != ':') return true;
            try { return Directory.Exists(value.Substring(0, 2) + "\\"); } catch { return false; }
        }

        // 빈 값 제거. Defender가 권한 부족 안내("N/A: ...")를 돌려주면 null
        static string[] Clean(string[] values)
        {
            var r = new List<string>();
            foreach (string v in values)
            {
                if (string.IsNullOrEmpty(v)) continue;
                if (v.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)) return null;
                r.Add(v);
            }
            return r.ToArray();
        }

        int CountIn(ListViewGroup g) { return g.Items.Count; }

        void AddEmpty(ListViewGroup g, bool denied)
        {
            var it = new ListViewItem(new[] { denied ? Tr.T("(읽을 수 없음)", "(cannot read)") : Tr.T("(없음)", "(none)"), "", "", "" }, g) { ForeColor = SystemColors.GrayText };
            list.Items.Add(it);
        }

        static readonly Color RealtimeOnlyColor = Color.FromArgb(0, 120, 60);

        void AddRow(string raw, ListViewGroup g, bool isProcess)
        {
            // 폴더 제외는 조건("\:{...}")을 떼어 실제 경로로 표시·확인하고, Tag에는 등록된 값 그대로 보관
            string value = isProcess ? raw : ExclusionScope.BasePath(raw);
            string scope = isProcess ? Tr.T("실시간·예약", "Real-time + scheduled") : ExclusionScope.Label(raw);
            bool hasPath = value.IndexOf('\\') >= 0 || (value.Length > 1 && value[1] == ':');
            string name = hasPath ? Path.GetFileName(value.TrimEnd('\\', '/')) : value;
            if (string.IsNullOrEmpty(name)) name = value; // 드라이브 루트(Y:\) 등

            string net = hasPath ? NetworkTarget(value) : null;
            bool isDir = false;
            string status;
            Color statusColor = SystemColors.GrayText;
            string tip = value;

            if (!hasPath) status = Tr.T("이름 기준", "By name");
            else if (net != null)
            {
                // 네트워크는 응답이 느릴 수 있어 백그라운드에서 확인
                status = Tr.T("네트워크 확인 중…", "Checking network…");
                pendingCount++;
                if (!string.Equals(net, value, StringComparison.OrdinalIgnoreCase))
                {
                    tip = value + "\r\n→ " + net;
                    if (!isProcess) tip += Tr.T("\r\n드라이브 문자로 등록됨 — 실제 경로로 변환을 권장합니다", "\r\nRegistered with a drive letter — converting to the real path is recommended");
                }
            }
            else
            {
                isDir = Directory.Exists(value);
                if (isDir || File.Exists(value)) status = isDir ? Tr.T("폴더 있음", "Folder exists") : Tr.T("파일 있음", "File exists");
                else if (!DriveRootExists(value)) { status = Tr.T("드라이브 연결 안 됨", "Drive not connected"); statusColor = MissingColor; missingCount++; }
                else { status = Tr.T("경로 없음", "Path not found"); statusColor = MissingColor; missingCount++; }
            }

            if (!isProcess)
                tip += ExclusionScope.IsRealtimeOnly(raw)
                    ? Tr.T("\r\n검사 범위: 실시간 보호에서만 제외 (수동·예약 검사는 함)", "\r\nScan scope: excluded from real-time protection only (manual and scheduled scans still scan)")
                    : ExclusionScope.Context(raw).Length == 0
                        ? Tr.T("\r\n검사 범위: 모든 검사에서 제외", "\r\nScan scope: excluded from all scans")
                        : Tr.T("\r\n등록 값: ", "\r\nStored value: ") + raw;

            var it = new ListViewItem(new[] { name, value, status, scope }, g)
            {
                ToolTipText = tip,
                Tag = raw,
                UseItemStyleForSubItems = false
            };
            it.SubItems[2].ForeColor = statusColor;
            it.SubItems[3].ForeColor = isProcess ? SystemColors.GrayText
                : ExclusionScope.IsRealtimeOnly(raw) ? RealtimeOnlyColor : SystemColors.ControlText;

            bool treatAsFolder = isDir || (!isProcess && !Path.HasExtension(value));
            // 네트워크 경로는 아이콘 조회 때 네트워크에 접근하지 않도록 확장자/폴더 기준 아이콘 사용
            Icon ic = GetShellIcon(value, treatAsFolder, net != null);
            if (ic != null)
            {
                images.Images.Add(ic.ToBitmap());
                it.ImageIndex = images.Images.Count - 1;
                ic.Dispose();
            }
            list.Items.Add(it);

            if (net != null) CheckNetworkAsync(it, net, generation);
        }

        void CheckNetworkAsync(ListViewItem it, string target, int gen)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int kind; // 1 = 폴더, 2 = 파일, 0 = 없음/연결 안 됨
                try { kind = Directory.Exists(target) ? 1 : File.Exists(target) ? 2 : 0; }
                catch { kind = 0; }

                ui.Post(__ =>
                {
                    if (gen != generation || IsDisposed || it.ListView == null) return;
                    pendingCount--;
                    if (kind == 1) it.SubItems[2].Text = Tr.T("네트워크 · 폴더 있음", "Network · folder exists");
                    else if (kind == 2) it.SubItems[2].Text = Tr.T("네트워크 · 파일 있음", "Network · file exists");
                    else
                    {
                        it.SubItems[2].Text = Tr.T("네트워크 연결 안 됨", "Network offline");
                        it.SubItems[2].ForeColor = MissingColor;
                        offlineCount++;
                    }
                    UpdateProblemLabel();
                }, null);
            });
        }

        // 실제 파일이 없어도 확장자/폴더 기준 아이콘을 받아옴
        static Icon GetShellIcon(string path, bool treatAsFolder, bool noDiskAccess)
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;
            uint attr = 0;
            string query = path;
            if (noDiskAccess || (!File.Exists(path) && !Directory.Exists(path)))
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
                attr = treatAsFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
                if (!treatAsFolder && !Path.HasExtension(path)) query = path + ".exe";
            }
            IntPtr r = SHGetFileInfo(query, attr, ref info, (uint)Marshal.SizeOf(info), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            Icon copy = (Icon)Icon.FromHandle(info.hIcon).Clone();
            DestroyIcon(info.hIcon);
            return copy;
        }

        IEnumerable<string> SelectedValues()
        {
            foreach (ListViewItem it in list.SelectedItems)
                if (it.Tag is string) yield return DisplayValue(it);
        }

        // 화면·복사·열기에 쓰는 값: 폴더 제외는 조건을 뗀 실제 경로
        string DisplayValue(ListViewItem it)
        {
            string v = (string)it.Tag;
            return it.Group == grpPath ? ExclusionScope.BasePath(v) : v;
        }

        void CopySelected()
        {
            var vals = new List<string>(SelectedValues());
            if (vals.Count == 0)
                foreach (ListViewItem it in list.Items) if (it.Tag is string) vals.Add(DisplayValue(it));
            if (vals.Count == 0) return;
            try { Clipboard.SetText(string.Join("\r\n", vals.ToArray())); } catch { }
            summary.Text = vals.Count + Tr.T("개 경로를 클립보드에 복사했습니다.", " path(s) copied to the clipboard.");
            summary.ForeColor = SystemColors.ControlText;
            missingLabel.Text = "";
        }

        void OpenSelectedLocation()
        {
            foreach (string v in SelectedValues())
            {
                try
                {
                    string net = NetworkTarget(v);
                    if (net != null)
                    {
                        // 네트워크 경로: 존재 확인(느릴 수 있음) 없이 실제 주소로 탐색기에 맡김
                        bool looksFile = Path.HasExtension(net);
                        Process.Start("explorer.exe", looksFile ? "/select,\"" + net + "\"" : "\"" + net + "\"");
                    }
                    else if (Directory.Exists(v)) Process.Start("explorer.exe", "\"" + v + "\"");
                    else if (File.Exists(v)) Process.Start("explorer.exe", "/select,\"" + v + "\"");
                    else
                    {
                        summary.Text = Tr.T("경로가 없어 열 수 없습니다: ", "Path not found, cannot open: ") + v;
                        summary.ForeColor = MissingColor;
                        missingLabel.Text = "";
                    }
                }
                catch { }
                break; // 첫 번째 선택 항목만 연다
            }
        }

        protected override void OnShown(EventArgs e)
        {
            // 열 너비를 먼저 맞춘 뒤 Shown 이벤트를 알림 (경로 열은 남은 폭 사용)
            list.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
            if (list.Columns[0].Width < 160) list.Columns[0].Width = 160;
            // 상태 문구는 나중에(네트워크 확인 후) 바뀌므로 가장 긴 문구 기준으로 고정
            int statusW = 0;
            foreach (string s in new[] { Tr.T("네트워크 · 폴더 있음", "Network · folder exists"), Tr.T("네트워크 연결 안 됨", "Network offline"), Tr.T("드라이브 연결 안 됨", "Drive not connected"), Tr.T("네트워크 확인 중…", "Checking network…") })
                statusW = Math.Max(statusW, TextRenderer.MeasureText(s, list.Font).Width);
            list.Columns[2].Width = statusW + 24;
            int scopeW = 0;
            foreach (string s in new[] { Tr.T("실시간만 제외", "Real-time only"), Tr.T("모든 검사 제외", "All scans"), Tr.T("실시간·예약", "Real-time + scheduled") })
                scopeW = Math.Max(scopeW, TextRenderer.MeasureText(s, list.Font).Width);
            list.Columns[3].Width = scopeW + 24;
            FitPathColumn();
            base.OnShown(e);
        }

        void FitPathColumn()
        {
            if (list.Columns.Count < 4) return;
            int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[2].Width - list.Columns[3].Width - 4;
            if (w > 200) list.Columns[1].Width = w;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) images.Dispose();
            base.Dispose(disposing);
        }
    }
}
