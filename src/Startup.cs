using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace DefenderTray
{
    // Windows 시작 시 실행: 관리자 권한 앱이므로 레지스트리 Run 대신 '가장 높은 권한' 작업 스케줄러 작업을 씀.
    // 트레이 메뉴와 설치 프로그램(--register-startup / --unregister-startup)이 함께 사용.
    static class Startup
    {
        const string TaskName = "DefenderTray";

        static int RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit(10000);
                return p.ExitCode;
            }
        }

        public static bool IsRegistered()
        {
            try { return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0; } catch { return false; }
        }

        // 지금 실행 중인 exe 경로로 등록 (이미 있으면 덮어씀 → 포터블에서 설치판으로 옮겨도 경로가 갱신됨)
        public static void Register()
        {
            string exe = Application.ExecutablePath;
            int rc = RunSchtasks("/Create /TN \"" + TaskName + "\" /TR \"\\\"" + exe + "\\\"\" /SC ONLOGON /RL HIGHEST /F");
            if (rc != 0) throw new Exception(Tr.T("작업 스케줄러 등록 실패 (코드 ", "Task Scheduler registration failed (code ") + rc + ")");
        }

        public static void Unregister()
        {
            if (IsRegistered()) RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
        }
    }
}
