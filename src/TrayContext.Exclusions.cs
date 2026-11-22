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
        // ===== 검사 제외 관리 =====
        ToolStripMenuItem BuildExclusionMenu()
        {
            exclusionMenu = new ToolStripMenuItem(Tr.T("검사 제외 관리(&E)", "Manage exclusions(&E)"));
            exclusionMenu.DropDownOpening += (s, e) => RebuildExclusionMenu();
            RebuildExclusionMenu();
            return exclusionMenu;
        }

        static string FindExcludedEntry(string[] excluded, string path)
        {
            string name = Path.GetFileName(path);
            foreach (string e in excluded)
            {
                if (string.IsNullOrEmpty(e)) continue;
                if (string.Equals(e, path, StringComparison.OrdinalIgnoreCase)) return e;
                if (string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        void RebuildExclusionMenu()
        {
            exclusionMenu.DropDownItems.Clear();
            string[] excluded = Defender.GetExclusionProcesses();

            // 조직 정책이 로컬 제외를 무시하면 맨 위에 경고 (추가는 되지만 적용되지 않음)
            if (ManagedPolicy.LocalExclusionsIgnored())
            {
                exclusionMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("⚠ 조직 정책이 이 PC의 제외를 무시합니다 — 추가해도 적용되지 않음", "⚠ Organization policy ignores exclusions on this PC — additions have no effect"))
                {
                    Enabled = false,
                    ToolTipText = Tr.T("그룹 정책 '로컬 관리자 병합 동작 구성'이 사용 안 함으로 설정되어 있습니다. IT 관리자에게 문의하세요.", "The Group Policy 'Configure local administrator merge behavior for lists' is disabled. Contact your IT administrator.")
                });
                exclusionMenu.DropDownItems.Add(new ToolStripSeparator());
            }

            foreach (string[] tool in KnownTools)
            {
                string label = tool[0];
                string path = tool[1];
                string match = FindExcludedEntry(excluded, path);
                var item = new ToolStripMenuItem(label)
                {
                    Checked = match != null,
                    CheckOnClick = false,
                    ToolTipText = path
                };
                string p = path;
                item.Click += (s, e) => ToggleProcessExclusion(label, p);
                exclusionMenu.DropDownItems.Add(item);
            }

            // 알려진 도구 외에 사용자가 추가한 프로세스 제외 — 클릭하면 해제
            List<string> others = new List<string>();
            foreach (string e in excluded)
                if (!string.IsNullOrEmpty(e) && !MatchesKnownTool(e)) others.Add(e);
            if (others.Count > 0)
            {
                exclusionMenu.DropDownItems.Add(new ToolStripSeparator());
                exclusionMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("사용자 지정 제외 (클릭 = 해제)", "Custom exclusions (click = remove)")) { Enabled = false });
                foreach (string e in others)
                {
                    string entry = e;
                    var it = new ToolStripMenuItem(entry) { Checked = true, CheckOnClick = false, ToolTipText = entry };
                    it.Click += (s, ev) => RemoveCustomExclusion(entry);
                    exclusionMenu.DropDownItems.Add(it);
                }
            }

            exclusionMenu.DropDownItems.Add(new ToolStripSeparator());
            exclusionMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("실행 파일 추가...", "Add program..."), null, (s, e) => AddProcessViaPicker()));
            exclusionMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("폴더 추가...", "Add folder..."), null, (s, e) => AddPathViaPicker()));
            exclusionMenu.DropDownItems.Add(new ToolStripMenuItem(Tr.T("현재 제외 목록 보기...", "View current exclusions..."), null, (s, e) => ShowExclusionList()));

            exclusionMenu.DropDownItems.Add(new ToolStripSeparator());
            var shellItem = new ToolStripMenuItem(Tr.T("탐색기 오른쪽 클릭 메뉴에 추가", "Add to Explorer right-click menu"))
            {
                Checked = ShellIntegration.IsRegistered(),
                CheckOnClick = false,
                ToolTipText = Tr.T("켜면 탐색기에서 실행 파일·폴더를 우클릭해 바로 제외에 추가할 수 있습니다.", "When on, you can right-click a program or folder in Explorer to add it to exclusions.")
            };
            shellItem.Click += (s, e) => ToggleShellIntegration();
            exclusionMenu.DropDownItems.Add(shellItem);
        }

        static bool MatchesKnownTool(string entry)
        {
            foreach (string[] tool in KnownTools)
            {
                string name = Path.GetFileName(tool[1]);
                if (string.Equals(entry, tool[1], StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void RemoveCustomExclusion(string entry)
        {
            try
            {
                Defender.RemoveExclusionProcess(entry);
                NotifyDone(new[] { DefenderWatch.Describe("Processes", false, entry) });
            }
            catch (Exception ex)
            {
                Balloon(5000, Tr.T("변경 실패", "Change failed"), ex.Message, ToolTipIcon.Error);
            }
        }

        void AddPathViaPicker()
        {
            using (var dlg = new FolderBrowserDialog
            {
                // 관리자 권한 창에는 네트워크 드라이브 문자(Y: 등)가 보이지 않을 수 있음
                Description = Tr.T("검사에서 제외할 폴더 선택\r\n네트워크 드라이브가 안 보이면 '네트워크'에서 서버를 찾거나, 탐색기에서 폴더를 오른쪽 클릭해 추가하세요.", "Choose a folder to exclude from scans\r\nIf a network drive is not listed, find the server under 'Network', or right-click the folder in Explorer to add it."),
                ShowNewFolderButton = false
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                bool? realtimeOnly = ScopeDialog.Ask(null, dlg.SelectedPath);
                if (realtimeOnly == null) return;
                try
                {
                    string registered = Defender.AddExclusionPathResolved(dlg.SelectedPath, realtimeOnly.Value);
                    bool converted = !string.Equals(ExclusionScope.BasePath(registered).TrimEnd('\\'), dlg.SelectedPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    NotifyDone(converted
                        ? new[] { DefenderWatch.Describe("Paths", true, registered), Tr.T("(네트워크 드라이브 ", "(network drive ") + dlg.SelectedPath + Tr.T(" → 실제 경로로 등록)", " → registered as the real path)") }
                        : new[] { DefenderWatch.Describe("Paths", true, registered) });
                }
                catch (Exception ex)
                {
                    string msg = Defender.IsTamperProtected()
                        ? Tr.T("변조 방지가 켜져 있어 제외를 변경할 수 없습니다.", "Tamper Protection is on, so exclusions cannot be changed.")
                        : ex.Message;
                    Balloon(5000, Tr.T("변경 실패", "Change failed"), msg, ToolTipIcon.Error);
                }
            }
        }

        void ToggleShellIntegration()
        {
            try
            {
                if (ShellIntegration.IsRegistered())
                {
                    ShellIntegration.Unregister();
                    Balloon(3000, Tr.T("오른쪽 클릭 메뉴", "Right-click menu"), Tr.T("탐색기 오른쪽 클릭 메뉴에서 제거했습니다.", "Removed from the Explorer right-click menu."), ToolTipIcon.Info);
                }
                else
                {
                    ShellIntegration.Register();
                    Balloon(4000, Tr.T("오른쪽 클릭 메뉴", "Right-click menu"),
                        Tr.T("추가했습니다. 이제 실행 파일이나 폴더를 탐색기에서 오른쪽 클릭하면 'Defender 검사 제외에 추가'가 나옵니다. (추가 시 UAC 확인이 뜹니다)", "Added. Right-click a program or folder in Explorer to see 'Add to Defender exclusions'. (A UAC prompt appears when adding)"),
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Balloon(5000, Tr.T("오른쪽 클릭 메뉴", "Right-click menu"), ex.Message, ToolTipIcon.Error);
            }
        }

        void ToggleProcessExclusion(string label, string path)
        {
            try
            {
                string[] excluded = Defender.GetExclusionProcesses();
                string match = FindExcludedEntry(excluded, path);
                if (match != null)
                {
                    Defender.RemoveExclusionProcess(match);
                    NotifyDone(new[] { DefenderWatch.Describe("Processes", false, match) });
                }
                else
                {
                    if (!ExclusionListForm.ConfirmProcessExclusion(null, path)) return;
                    if (!File.Exists(path))
                    {
                        var r = MessageBox.Show(
                            Tr.T("해당 경로에 실행 파일이 없습니다:\r\n", "No program at this path:\r\n") + path + "\r\n\r\n" +
                            Tr.T("경로가 달라도 파일 이름(", "You can still add an exclusion by file name (") + Path.GetFileName(path) + Tr.T(") 기준으로 제외를 추가할 수 있습니다. 계속할까요?", ") even if the path differs. Continue?"),
                            Tr.T("파일 없음", "File not found"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (r != DialogResult.Yes) return;
                    }
                    Defender.AddExclusionProcess(path);
                    NotifyDone(new[] { DefenderWatch.Describe("Processes", true, path) });
                }
            }
            catch (Exception ex)
            {
                string msg = Defender.IsTamperProtected()
                    ? Tr.T("변조 방지가 켜져 있어 제외를 변경할 수 없습니다.\r\n먼저 변조 방지를 끄세요.", "Tamper Protection is on, so exclusions cannot be changed.\r\nTurn off Tamper Protection first.")
                    : ex.Message;
                Balloon(5000, Tr.T("변경 실패", "Change failed"), msg, ToolTipIcon.Error);
            }
        }

        void AddProcessViaPicker()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Tr.T("제외에 추가할 실행 파일 선택", "Choose a program to exclude"),
                Filter = Tr.T("실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*", "Programs (*.exe)|*.exe|All files (*.*)|*.*")
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                if (!ExclusionListForm.ConfirmProcessExclusion(null, dlg.FileName)) return;
                try
                {
                    Defender.AddExclusionProcess(dlg.FileName);
                    NotifyDone(new[] { DefenderWatch.Describe("Processes", true, dlg.FileName) });
                }
                catch (Exception ex)
                {
                    Balloon(5000, Tr.T("변경 실패", "Change failed"), ex.Message, ToolTipIcon.Error);
                }
            }
        }

        static ExclusionListForm listForm;
        static LogForm logForm;

        static void ShowLog()
        {
            if (logForm == null || logForm.IsDisposed)
            {
                logForm = new LogForm();
                logForm.Show();
            }
            else
            {
                logForm.Reload();
                if (logForm.WindowState == FormWindowState.Minimized) logForm.WindowState = FormWindowState.Normal;
            }
            logForm.TopMost = true;
            logForm.Activate();
            logForm.TopMost = false;
        }

        static void ShowExclusionList()
        {
            if (listForm == null || listForm.IsDisposed)
            {
                listForm = new ExclusionListForm();
                listForm.Show();
            }
            else
            {
                listForm.Reload();
                if (listForm.WindowState == FormWindowState.Minimized) listForm.WindowState = FormWindowState.Normal;
            }
            // 트레이 앱은 창이 뒤로 숨기 쉬우므로 앞으로 가져옴
            listForm.TopMost = true;
            listForm.Activate();
            listForm.TopMost = false;
        }
    }
}
