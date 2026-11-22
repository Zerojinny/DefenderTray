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
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Tr.Init();   // 화면 언어는 다른 무엇보다 먼저 정함
            string[] args = Environment.GetCommandLineArgs(); // [0] = exe 경로
            // 언어를 바꿔 다시 시작할 때: 이전 프로세스가 끝나야 중복 실행 방지(mutex)에 걸리지 않음
            if (args.Length >= 3 && args[1] == "--restarted")
            {
                int pid;
                if (int.TryParse(args[2], out pid))
                    try { using (var old = System.Diagnostics.Process.GetProcessById(pid)) old.WaitForExit(15000); } catch { }
                args = new[] { args[0] };
            }
            if (args.Length >= 2)
            {
                string cmd = args[1].ToLowerInvariant();
                if (cmd == "--exclude-process" && args.Length >= 3) { OneShotExclude(args[2], true); return; }
                if (cmd == "--exclude-path" && args.Length >= 3) { OneShotExclude(args[2], false); return; }
                if (cmd == "--register-shell") { try { ShellIntegration.Register(); } catch { } return; }
                if (cmd == "--unregister-shell") { ShellIntegration.Unregister(); return; }
                if (cmd == "--make-icon" && args.Length >= 3) { try { IconFactory.WriteAppIco(args[2]); } catch { } return; }
                // 설치 프로그램이 호출하는 명령
                if (cmd == "--register-startup") { try { Startup.Register(); } catch { } return; }
                if (cmd == "--unregister-startup") { try { Startup.Unregister(); } catch { } return; }
                if (cmd == "--uninstall-cleanup") { UninstallCleanup(); return; }
                // 시작 메뉴 '제거' 바로가기용. Windows 시작 메뉴는 unins000.exe를 직접 가리키는 바로가기를 숨기므로 앱을 거쳐 실행
                if (cmd == "--uninstall") { RunUninstaller(); return; }
            }

            // UI 스레드 예외로 앱이 꺼지지 않도록 (컨트롤 생성 전에 설정해야 함)
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try { MessageBox.Show(Tr.T("예기치 않은 오류가 발생했지만 프로그램은 계속 실행됩니다.\r\n\r\n", "An unexpected error occurred, but the program keeps running.\r\n\r\n") + e.Exception.Message,
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
            };

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }

        const string MutexName = @"Local\DefenderTray_SingleInstance";

        // 설치판이면 같은 폴더에 Inno Setup 제거 프로그램이 있음. 포터블판이면 null
        public static string UninstallerPath()
        {
            try
            {
                string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "unins000.exe");
                return System.IO.File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        public static bool RunUninstaller()
        {
            string p = UninstallerPath();
            if (p == null)
            {
                MessageBox.Show(Tr.T("설치판이 아니어서 제거 프로그램이 없습니다. 포터블판은 폴더를 지우면 됩니다.", "This is not an installed copy, so there is no uninstaller. For the portable edition, just delete the folder."),
                    AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true }); return true; }
            catch (Exception ex)
            {
                MessageBox.Show(Tr.T("제거 프로그램을 실행하지 못했습니다.\r\n", "Could not start the uninstaller.\r\n") + ex.Message, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 제거할 때 설치 프로그램이 호출: 이 앱이 없어지면 아무도 처리하지 않을 것들을 정리.
        //   - '시간 지정 끄기'로 꺼 둔 실시간 보호·SmartScreen은 다시 켬 (예약한 앱이 사라지므로)
        //   - 시작 프로그램 작업, 탐색기 오른쪽 클릭 메뉴 제거
        // 사용자가 일부러 정한 검사 제외·예약 검사·그룹 정책 설정은 건드리지 않음.
        static void UninstallCleanup()
        {
            try
            {
                if (TimedOff.GetUntilUtc().HasValue && Defender.GetState() == RtpState.Off)
                {
                    Defender.SetRealtime(true);
                    ActivityLog.Write(ActivityLog.Info, Tr.T("제거: 시간 지정으로 꺼 둔 실시간 보호를 다시 켬", "Uninstall: turned real-time protection back on (it was off on a timer)"));
                }
                TimedOff.SetUntilUtc(null);
                TimedOff.ActiveMinutes = null;
            }
            catch { }
            try
            {
                foreach (SsPart p in SsTimedOff.Parts)
                    if (!SmartScreen.Get(p).On)
                    {
                        SmartScreen.Set(p, true, SmartScreen.PreferGpo(p));
                        ActivityLog.Write(ActivityLog.Info, Tr.T("제거: 시간 지정으로 꺼 둔 SmartScreen ", "Uninstall: turned SmartScreen ") + SmartScreen.Name(p) + Tr.T("을(를) 다시 켬", " back on (it was off on a timer)"));
                    }
                SsTimedOff.Clear();
            }
            catch { }
            try { Startup.Unregister(); } catch { }
            try { ShellIntegration.Unregister(); } catch { }
            ActivityLog.Write(ActivityLog.Info, Tr.T("제거: 시작 프로그램 작업과 오른쪽 클릭 메뉴를 정리함", "Uninstall: removed the startup task and the right-click menu"));
        }

        static bool TrayIsRunning()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        // 오른쪽 클릭 메뉴 등에서 호출되는 1회성 제외 추가
        static void OneShotExclude(string target, bool isProcess)
        {
            Application.EnableVisualStyles();
            try
            {
                string msg;
                if (isProcess)
                {
                    Defender.AddExclusionProcess(target);
                    msg = Tr.T("프로세스를 Defender 검사 제외에 추가했습니다:\r\n\r\n", "Process added to Defender exclusions:\r\n\r\n") + target;
                }
                else
                {
                    bool? realtimeOnly = ScopeDialog.Ask(null, target);
                    if (realtimeOnly == null) return;
                    string registered = Defender.AddExclusionPathResolved(target, realtimeOnly.Value);
                    string shown = ExclusionScope.BasePath(registered);
                    string scope = Tr.T("\r\n검사 범위:  ", "\r\nScan scope:  ") + ExclusionScope.Label(registered);
                    msg = string.Equals(shown.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                        ? Tr.T("폴더를 Defender 검사 제외에 추가했습니다:\r\n\r\n", "Folder added to Defender exclusions:\r\n\r\n") + shown + scope
                        : Tr.T("네트워크 드라이브라서 Microsoft 권장대로 실제 네트워크 경로로 등록했습니다.\r\n\r\n", "It is a network drive, so the real network path was registered as Microsoft recommends.\r\n\r\n") +
                          Tr.T("선택한 위치:  ", "Selected location:  ") + target + Tr.T("\r\n등록한 경로:  ", "\r\nRegistered path:  ") + shown + scope;
                }
                // 트레이가 실행 중이면 트레이가 Defender 기록을 읽어 "✓ 이 도구에서 변경함" 알림을 띄움
                // (다른 추가 방법과 같은 알림). 트레이가 없을 때만 대화 상자로 알림
                if (!TrayIsRunning())
                    MessageBox.Show(msg, Tr.T("Defender 검사 제외", "Defender exclusions"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                string msg = Defender.IsTamperProtected()
                    ? Tr.T("변조 방지가 켜져 있어 제외를 추가할 수 없습니다.\r\n먼저 변조 방지를 끄세요.", "Tamper Protection is on, so the exclusion cannot be added.\r\nTurn off Tamper Protection first.")
                    : ex.Message;
                MessageBox.Show(msg, Tr.T("추가 실패", "Add failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
