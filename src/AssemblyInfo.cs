using System.Reflection;

// 이름은 "DefenderTray". "Microsoft Defender용 트레이 도구"는 설명으로만 쓴다
// (Microsoft 상표가 앞에 오면 공식 제품처럼 보이므로).
// 탐색기 '파일 설명', 작업 관리자, 관리자 권한 확인 창은 언어를 바꿀 수 없어 설명을 두 언어로 적는다.
[assembly: AssemblyTitle("DefenderTray")]
[assembly: AssemblyProduct("DefenderTray")]
[assembly: AssemblyDescription("Microsoft Defender용 트레이 도구 / Tray tool for Microsoft Defender. Not affiliated with Microsoft. Microsoft와 관련 없는 개인 프로젝트입니다.")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace DefenderTray
{
    static class AppInfo
    {
        public const string DisplayName = "DefenderTray";
        public static string Description { get { return Tr.T("Microsoft Defender용 트레이 도구", "Tray tool for Microsoft Defender"); } }
        public static string ShortName { get { return Tr.T("이 도구", "this tool"); } }   // 알림 제목처럼 짧아야 하는 곳
    }
}
