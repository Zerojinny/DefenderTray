using System;
using System.Collections.Generic;

namespace DefenderTray.Tests
{
    // 화면·관리자 권한·레지스트리 없이 돌릴 수 있는 순수 로직 시험.
    // 실행: test.bat  (실패가 있으면 종료 코드 1)
    static class TestMain
    {
        static int passed, failed;

        static void Eq<T>(T expected, T actual, string what)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual)) { passed++; return; }
            failed++;
            Console.WriteLine("FAIL  " + what + "\n      expected: " + expected + "\n      actual:   " + actual);
        }

        static void True(bool cond, string what) { Eq(true, cond, what); }

        static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Tr.Ko = true;   // 기존 시험은 한국어 문구 기준
            ExclusionScopeTests();
            WatchParseTests();
            DescribeTests();
            RevertPlanTests();
            TimedOffTests();
            DriveWatchTests();
            ScanScheduleTests();
            ManagedPolicyTests();
            LocalGpoTests();
            SmartScreenTests();
            LanguageTests();
            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ALL PASSED (" + passed + ")" : failed + " FAILED, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }

        // ----- 검사 범위(조건부 제외) -----
        static void ExclusionScopeTests()
        {
            const string rt = @"\:{ScanTrigger:OnAccess}";
            Eq(@"D:\Backup" + rt, ExclusionScope.Make(@"D:\Backup", true), "Make realtime");
            Eq(@"D:\Backup" + rt, ExclusionScope.Make(@"D:\Backup\", true), "Make realtime trims trailing slash");
            Eq(@"D:\" + rt.Substring(1), ExclusionScope.Make(@"D:\", true), "Make realtime on drive root keeps one backslash");
            Eq(@"D:\Backup", ExclusionScope.Make(@"D:\Backup", false), "Make all-scans is unchanged");

            Eq(@"D:\Backup", ExclusionScope.BasePath(@"D:\Backup" + rt), "BasePath strips context");
            Eq(@"D:\", ExclusionScope.BasePath(@"D:" + rt), "BasePath of drive root");
            Eq(@"\\srv\share", ExclusionScope.BasePath(@"\\srv\share" + rt), "BasePath of UNC");
            Eq(@"D:\Backup", ExclusionScope.BasePath(@"D:\Backup"), "BasePath of plain path");

            True(ExclusionScope.IsRealtimeOnly(@"D:\a" + rt), "IsRealtimeOnly true");
            True(!ExclusionScope.IsRealtimeOnly(@"D:\a"), "IsRealtimeOnly false for plain");
            True(!ExclusionScope.IsRealtimeOnly(@"D:\a\:{ScanTrigger:onaccess}"), "keyword is case sensitive");

            Eq(@"\\srv\share\a" + rt, ExclusionScope.WithBase(@"Y:\a" + rt, @"\\srv\share\a"), "WithBase keeps context");
            Eq(@"\\srv\share\a", ExclusionScope.WithBase(@"Y:\a", @"\\srv\share\a"), "WithBase plain");

            Eq("실시간만 제외", ExclusionScope.Label(@"D:\a" + rt), "Label realtime");
            Eq("모든 검사 제외", ExclusionScope.Label(@"D:\a"), "Label plain");
            Eq("조건부 {ScanType:quick}", ExclusionScope.Label(@"D:\a\:{ScanType:quick}"), "Label other context");
        }

        // ----- 이벤트 5007 값 해석 -----
        static void WatchParseTests()
        {
            string kind, value; bool policy;
            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\Y:\ = 0x0", out kind, out value, out policy), "parse path");
            Eq("Paths", kind, "kind Paths"); Eq(@"Y:\", value, "value Y:\\"); Eq(false, policy, "not policy");

            // 192.0.2.x 는 문서·예시용으로 예약된 주소 (RFC 5737)
            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\\\192.0.2.10\share = 0x0", out kind, out value, out policy), "parse UNC");
            Eq(@"\\192.0.2.10\share", value, "UNC value");

            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Processes\C:\Program Files (x86)\Example App 9.0\Manager.exe = 0x0", out kind, out value, out policy), "parse process with spaces");
            Eq("Processes", kind, "kind Processes");
            Eq(@"C:\Program Files (x86)\Example App 9.0\Manager.exe", value, "process value");

            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\Paths\D:\Data = 0", out kind, out value, out policy), "parse policy");
            Eq(true, policy, "policy flag");

            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\D:\B\:{ScanTrigger:OnAccess} = 0x0", out kind, out value, out policy), "parse contextual");
            Eq(@"D:\B\:{ScanTrigger:OnAccess}", value, "contextual value kept whole");

            // 격리 복원 중 Defender가 스스로 넣는 임시 제외 (실제 기록 값)
            True(DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\TemporaryPaths\\?\C:\DefTest\eicar-test.txt = 0x7B", out kind, out value, out policy), "parse temporary path");
            Eq("TemporaryPaths", kind, "kind TemporaryPaths");
            True(!DefenderWatch.IsTrackedKind(kind), "TemporaryPaths is not tracked");
            True(DefenderWatch.IsTrackedKind("Paths") && DefenderWatch.IsTrackedKind("Processes"), "Paths/Processes tracked");

            True(!DefenderWatch.Parse(@"HKLM\SOFTWARE\Microsoft\Windows Defender\UX Configuration\ToastOrSsoTrigger = 0x1", out kind, out value, out policy), "non-exclusion ignored");
            True(!DefenderWatch.Parse("", out kind, out value, out policy), "empty ignored");
        }

        static void DescribeTests()
        {
            Eq(@"폴더·파일 제외 추가: C:\temp", DefenderWatch.Describe("Paths", true, @"C:\temp"), "describe add path");
            Eq(@"프로세스 제외 삭제: C:\a.exe", DefenderWatch.Describe("Processes", false, @"C:\a.exe"), "describe remove process");
            Eq(@"폴더·파일 제외 (실시간만 제외) 추가: D:\B", DefenderWatch.Describe("Paths", true, @"D:\B\:{ScanTrigger:OnAccess}"), "describe contextual");
        }

        // ----- 외부 변경 되돌리기 계획 -----
        static DefenderChange Ex(string kind, string value, bool added, bool policy = false)
        {
            return new DefenderChange { IsExclusion = true, Kind = kind, Value = value, Added = added, Policy = policy };
        }

        static void RevertPlanTests()
        {
            // 추가 후 삭제 → 첫 변경(추가) 기준으로 '원래 없었음' → 삭제 대상 하나
            var plan = RevertPlan.Exclusions(new[] { Ex("Paths", @"C:\x", true), Ex("Paths", @"C:\x\", false) }, true, true);
            Eq(1, plan.Count, "add then remove collapses to one");
            Eq(true, plan[0].Added, "first change decides (added)");

            Eq(0, RevertPlan.Exclusions(new[] { Ex("Paths", @"C:\x", true) }, false, true).Count, "revertAdd off skips added");
            Eq(1, RevertPlan.Exclusions(new[] { Ex("Paths", @"C:\x", false) }, false, true).Count, "revertRemove on keeps removed");
            Eq(0, RevertPlan.Exclusions(new[] { Ex("Paths", @"C:\x", true, true) }, true, true).Count, "policy changes are never reverted");
            Eq(2, RevertPlan.Exclusions(new[] { Ex("Paths", @"C:\x", true), Ex("Processes", @"C:\x", true) }, true, true).Count, "kind is part of key");

            var on = new DefenderChange { IsRtpOn = true };
            var off = new DefenderChange { IsRtpOff = true };
            True(RevertPlan.LastRtpWasOff(new[] { on, off }), "last rtp off");
            True(!RevertPlan.LastRtpWasOff(new[] { off, on }), "last rtp on");
            True(!RevertPlan.LastRtpWasOff(new[] { Ex("Paths", "a", true) }), "no rtp change");
        }

        // ----- 시간 지정 끄기 -----
        static void TimedOffTests()
        {
            Eq("30분", TimedOff.DurationLabel(30), "label 30");
            Eq("1시간", TimedOff.DurationLabel(60), "label 60");
            Eq("1시간 30분", TimedOff.DurationLabel(90), "label 90");
            Eq("직접 켤 때까지", TimedOff.DurationLabel(0), "label 0");

            var now = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);
            True(!TimedOff.IsDue(null, now), "no schedule");
            True(!TimedOff.IsDue(now.AddMinutes(1), now), "not yet");
            True(TimedOff.IsDue(now, now), "exactly due");
            True(TimedOff.IsDue(now.AddMinutes(-5), now), "overdue (e.g. after reboot)");

            // 체크 표시: 실제로 그 기간을 골라 꺼져 있을 때만
            True(!TimedOff.IsChecked(30, false, null), "on, nothing chosen: no check");
            True(!TimedOff.IsChecked(30, false, 30), "on again (stale value): no check");
            True(!TimedOff.IsChecked(30, true, null), "off from elsewhere: no check");
            True(TimedOff.IsChecked(30, true, 30), "off for 30m: 30m checked");
            True(!TimedOff.IsChecked(60, true, 30), "off for 30m: 1h not checked");
            True(TimedOff.IsChecked(0, true, 0), "off until manual: that item checked");

            Eq("30분 남음", TimedOff.RemainingLabel(now.AddMinutes(30), now), "remaining 30m");
            Eq("1시간 5분 남음", TimedOff.RemainingLabel(now.AddMinutes(65), now), "remaining 65m");
            Eq("2시간 남음", TimedOff.RemainingLabel(now.AddMinutes(120), now), "remaining 120m");
            Eq("1분 안에", TimedOff.RemainingLabel(now.AddSeconds(20), now), "remaining <1m");

            var localNow = new DateTime(2026, 11, 20, 23, 0, 0);
            Eq("23:30", TimedOff.WhenLabel(localNow.AddMinutes(30), localNow), "same day time");
            Eq("11-21 01:00", TimedOff.WhenLabel(localNow.AddHours(2), localNow), "next day shows date");
        }

        // ----- 예약 검사 켜기/끄기 -----
        static void ScanScheduleTests()
        {
            True(ScanSchedule.IsEnabled(0), "0 (매일) is enabled");
            True(ScanSchedule.IsEnabled(7), "7 (토요일) is enabled");
            True(!ScanSchedule.IsEnabled(8), "8 (안 함) is disabled");

            Eq(8, ScanSchedule.ToSetting(false, 3), "off → 8");
            Eq(3, ScanSchedule.ToSetting(true, 3), "on keeps day");
            Eq(0, ScanSchedule.ToSetting(true, 8), "on with invalid day → 매일");

            Eq(3, ScanSchedule.DayIndexFor(3, 5), "enabled shows its own day");
            Eq(5, ScanSchedule.DayIndexFor(8, 5), "disabled shows remembered day");
            Eq(0, ScanSchedule.DayIndexFor(8, -1), "disabled, nothing remembered → 매일");
        }

        // ----- 조직 정책 감지 (레지스트리 대신 가짜 값) -----
        static void WithPolicy(Dictionary<string, object> values, Action test)
        {
            var saved = ManagedPolicy.Reader;
            ManagedPolicy.Reader = (key, name) => { object v; return values.TryGetValue(key + "|" + name, out v) ? v : null; };
            try { test(); } finally { ManagedPolicy.Reader = saved; }
        }

        static void ManagedPolicyTests()
        {
            string scan = ManagedPolicy.GpoRoot + @"\Scan|", rtp = ManagedPolicy.GpoRoot + @"\Real-Time Protection|", mdm = ManagedPolicy.MdmRoot + "|";

            WithPolicy(new Dictionary<string, object>(), () =>
            {
                Eq(null, ManagedPolicy.Get("ScanScheduleDay"), "no policy → null");
                True(!ManagedPolicy.LocalExclusionsIgnored(), "no merge policy");
            });

            WithPolicy(new Dictionary<string, object> { { scan + "ScheduleDay", 0 } }, () =>
            {
                PolicyValue p = ManagedPolicy.Get("ScanScheduleDay");
                True(p != null && p.Source == "그룹 정책", "GPO ScheduleDay detected");
                Eq(0, (int)p.Value, "GPO value 매일");
                True(ManagedPolicy.Blocks("ScanScheduleDay", 8) != null, "turning off is blocked by GPO 매일");
                Eq(null, ManagedPolicy.Blocks("ScanScheduleDay", 0), "same value is not blocked");
            });

            WithPolicy(new Dictionary<string, object> { { scan + "ScheduleDay", 0 }, { scan + "LocalSettingOverrideScheduleDay", 1 } }, () =>
                Eq(null, ManagedPolicy.Get("ScanScheduleDay"), "local override allowed → not locked"));

            WithPolicy(new Dictionary<string, object> { { scan + "ScheduleTime", 120 } }, () =>
                Eq(TimeSpan.FromHours(2), (TimeSpan)ManagedPolicy.Get("ScanScheduleTime").Value, "GPO minutes → 02:00"));

            WithPolicy(new Dictionary<string, object> { { rtp + "DisableRealtimeMonitoring", 0 } }, () =>
            {
                True(ManagedPolicy.Blocks("DisableRealtimeMonitoring", true) != null, "GPO forces RTP on → turning off blocked");
                Eq(null, ManagedPolicy.Blocks("DisableRealtimeMonitoring", false), "turning on is fine");
            });

            WithPolicy(new Dictionary<string, object> { { mdm + "AllowRealtimeMonitoring", 1 } }, () =>
            {
                PolicyValue p = ManagedPolicy.Get("DisableRealtimeMonitoring");
                True(p != null && p.Source == "MDM(Intune 등)", "MDM RTP detected");
                Eq(false, (bool)p.Value, "MDM Allow=1 → Disable=false (inverse)");
            });

            WithPolicy(new Dictionary<string, object> { { mdm + "ScheduleScanDay", 8 } }, () =>
                Eq(8, (int)ManagedPolicy.Get("ScanScheduleDay").Value, "MDM ScheduleScanDay"));

            WithPolicy(new Dictionary<string, object> { { ManagedPolicy.GpoRoot + "|DisableLocalAdminMerge", 1 } }, () =>
                True(ManagedPolicy.LocalExclusionsIgnored(), "DisableLocalAdminMerge=1 → local exclusions ignored"));

            True(ManagedPolicy.SameValue((byte)45, 45), "byte vs int");
            True(!ManagedPolicy.SameValue(true, 1), "bool vs int differ");
        }

        // ----- 로컬 그룹 정책 파일 (Registry.pol / GPT.INI) -----
        static void LocalGpoTests()
        {
            const string scan = @"Software\Policies\Microsoft\Windows Defender\Scan";
            var entries = new List<PolEntry>
            {
                new PolEntry { Key = @"Software\Policies\Microsoft\Windows\WindowsUpdate", Value = "ProductVersion", Type = 1, Data = System.Text.Encoding.Unicode.GetBytes("Windows 11\0") },
                new PolEntry { Key = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", Value = "", Type = 0, Data = new byte[0] },
                new PolEntry { Key = @"Software\Policies\Microsoft\Windows\DeviceGuard", Value = "**del.LsaCfgFlags", Type = 1, Data = new byte[] { 0x20, 0, 0, 0 } },
            };
            byte[] bytes = PolFile.Build(entries);
            Eq("PReg", System.Text.Encoding.ASCII.GetString(bytes, 0, 4), "header");
            var back = PolFile.Parse(bytes);
            Eq(3, back.Count, "roundtrip count");
            Eq("ProductVersion", back[0].Value, "roundtrip value name");
            Eq("", back[1].Value, "empty value name kept");
            byte[] again = PolFile.Build(back);
            Eq(Convert.ToBase64String(bytes), Convert.ToBase64String(again), "roundtrip bytes identical");

            True(PolFile.SetDword(back, scan, "ScheduleDay", 8), "add ScheduleDay");
            Eq(8, PolFile.GetDword(back, scan.ToUpperInvariant(), "scheduleday").Value, "get is case-insensitive");
            True(!PolFile.SetDword(back, scan, "ScheduleDay", 8), "same value → no change");
            True(PolFile.SetDword(back, scan, "ScheduleDay", 0), "replace value");
            Eq(4, back.Count, "replace keeps one entry");
            True(PolFile.SetDword(back, scan, "ScheduleDay", null), "remove");
            Eq(3, back.Count, "others preserved after remove");
            Eq(null, PolFile.GetDword(back, scan, "ScheduleDay"), "removed");
            True(!PolFile.SetDword(back, scan, "ScheduleDay", null), "remove missing → no change");
            Eq(0, PolFile.Parse(new byte[0]).Count, "empty file → no entries");

            // GPT.INI: 컴퓨터 버전(아래 16비트)만 올리고 사용자 버전은 유지
            string ini = "[General]\r\ngPCMachineExtensionNames=[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]\r\nVersion=65663\r\n";
            True(GptIni.Bump(ini).Contains("Version=65664"), "bump machine version (65663 → 65664)");
            string fresh = GptIni.Bump("");
            True(fresh.StartsWith("[General]") && fresh.Contains("Version=1") && fresh.Contains(GptIni.RegistryExt), "new gpt.ini");
            string noExt = GptIni.Bump("[General]\r\ngPCMachineExtensionNames=[{F312195E-3D9D-447A-A3F5-08DFFA24735E}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]\r\nVersion=3\r\n");
            True(noExt.Contains("=" + GptIni.RegistryExt + "[{F312195E"), "registry extension added, existing kept");
            True(noExt.Contains("Version=4"), "version bumped");
        }

        // ----- SmartScreen 상태 해석 -----
        static void SmartScreenTests()
        {
            // 앱 및 파일 확인
            SsState s = SmartScreen.ResolveAppsFiles(null, null, null);
            True(s.On && s.Detail == "기본값" && !s.OrgLocked, "apps: nothing set → on (default)");
            True(!SmartScreen.ResolveAppsFiles(null, null, "Off").On, "apps: local Off");
            True(SmartScreen.ResolveAppsFiles(null, null, "Warn").On, "apps: local Warn → on");
            s = SmartScreen.ResolveAppsFiles(0, 0, "Warn");
            True(!s.On && s.LocalGpo && !s.OrgLocked && s.Detail == "그룹 정책(이 PC)", "apps: local GPO off wins over local Warn");
            s = SmartScreen.ResolveAppsFiles(1, null, "Off");
            True(s.On && s.OrgLocked && s.Detail == "조직 정책", "apps: policy not in Registry.pol → org lock");
            True(SmartScreen.ResolveAppsFiles(1, 0, null).OrgLocked, "apps: policy differs from local GPO → org lock");

            // Edge
            True(SmartScreen.ResolveEdge(null, null, null, null).On, "edge: default on");
            s = SmartScreen.ResolveEdge(null, null, null, 0);
            True(!s.On && s.Detail == "Edge 설정", "edge: user turned off in Edge");
            s = SmartScreen.ResolveEdge(0, 0, null, 1);
            True(!s.On && s.LocalGpo, "edge: local policy 0 wins over Edge setting");
            True(SmartScreen.ResolveEdge(null, null, 1, 0).OrgLocked, "edge: user policy → org lock");

            // Store
            True(SmartScreen.ResolveStore(null).On, "store: default on");
            True(!SmartScreen.ResolveStore(0).On, "store: 0 → off");

            // Registry.pol 문자열 값 (ShellSmartScreenLevel)
            var e = new List<PolEntry>();
            True(PolFile.SetValue(e, SmartScreen.SysPolicyKey, "ShellSmartScreenLevel", "Warn"), "set REG_SZ");
            Eq((uint)1, e[0].Type, "REG_SZ type");
            Eq("Warn", (string)PolFile.GetValue(PolFile.Parse(PolFile.Build(e)), SmartScreen.SysPolicyKey, "ShellSmartScreenLevel"), "REG_SZ roundtrip");
            True(!PolFile.SetValue(e, SmartScreen.SysPolicyKey, "ShellSmartScreenLevel", "Warn"), "same string → no change");
            True(PolFile.SetValue(e, SmartScreen.SysPolicyKey, "EnableSmartScreen", 0), "set DWORD 0");
            Eq(0, (int)PolFile.GetValue(e, SmartScreen.SysPolicyKey, "EnableSmartScreen"), "DWORD 0 read back");

            // 첨부 파일 관리자 형식 목록
            var types = SmartScreen.ParseTypes(".bat;.cmd;.au3;.a3x;.exe");
            Eq(5, types.Count, "parse 5 types (this PC's ModRiskFileTypes)");
            True(types.Contains(".exe"), "contains .exe");
            var odd = SmartScreen.ParseTypes(" EXE ; .Bat,,.exe");
            Eq(2, odd.Count, "normalize case, missing dot, duplicates, empty");
            True(odd.Contains(".exe") && odd.Contains(".bat"), "normalized values");
            Eq(0, SmartScreen.ParseTypes(null).Count, "null → empty");

            SsPart p;
            True(SmartScreen.TryParse(" edge ", out p) && p == SsPart.Edge, "parse part name");
            True(!SmartScreen.TryParse("nope", out p), "unknown part");
        }

        // ----- 화면 언어 -----
        static void LanguageTests()
        {
            // 설정 + 시스템 언어 → 실제 언어
            True(Tr.Resolve(Tr.Auto, true), "auto + Korean system → Korean");
            True(!Tr.Resolve(Tr.Auto, false), "auto + other system → English");
            True(Tr.Resolve(Tr.Korean, false), "explicit Korean wins");
            True(!Tr.Resolve(Tr.English, true), "explicit English wins");
            True(!Tr.Resolve("garbage", false), "unknown setting → auto");

            // 영어식 단축키 표기
            Eq("Add &program...", Tr.EnglishMnemonic("Add program(&P)..."), "mnemonic moved into word");
            Eq("&Refresh", Tr.EnglishMnemonic("Refresh(&R)"), "mnemonic at start");
            Eq("Type(&K):", Tr.EnglishMnemonic("Type(&K):"), "letter not in word → unchanged");
            Eq("App && browser", Tr.EnglishMnemonic("App && browser"), "escaped ampersand untouched");
            Eq("Plain text", Tr.EnglishMnemonic("Plain text"), "no mnemonic");

            Tr.Ko = false;
            try
            {
                Eq("Real-time only", ExclusionScope.Label(@"D:\a\:{ScanTrigger:OnAccess}"), "en: scope label");
                Eq("30 min", TimedOff.DurationLabel(30), "en: 30 min");
                Eq("1 h 30 min", TimedOff.DurationLabel(90), "en: 90 min");
                Eq("Until turned on", TimedOff.DurationLabel(0), "en: until turned on");
                var now = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);
                Eq("1 h 5 min left", TimedOff.RemainingLabel(now.AddMinutes(65), now), "en: remaining");
                Eq(@"File/folder exclusion added: C:\temp", DefenderWatch.Describe("Paths", true, @"C:\temp"), "en: describe");
                Eq(@"File/folder exclusion (Real-time only) removed: D:\B", DefenderWatch.Describe("Paths", false, @"D:\B\:{ScanTrigger:OnAccess}"), "en: describe contextual");
                Eq(@"E:\ (USB) — Removable", new DriveEntry { Root = @"E:\", Label = "USB", Removable = true }.Describe(), "en: drive");
                Eq("Group Policy (this PC)", SmartScreen.ResolveAppsFiles(0, 0, null).Detail, "en: SmartScreen detail");
                Eq("Tray tool for Microsoft Defender", AppInfo.Description, "en: description");
                Eq("Turns back on in 30 min (at 14:30)", Tr.F("{0} 뒤({1}) 다시 켜집니다", "Turns back on in {0} (at {1})", "30 min", "14:30"), "en: format");
            }
            finally { Tr.Ko = true; }
            Eq("Microsoft Defender용 트레이 도구", AppInfo.Description, "ko: description");
            Eq("DefenderTray", AppInfo.DisplayName, "name is the same in every language");
        }

        // ----- 새 드라이브 감지 -----
        static void DriveWatchTests()
        {
            var after = new[]
            {
                new DriveEntry { Root = @"C:\" }, new DriveEntry { Root = @"E:\", Label = "USB", Removable = true }
            };
            var added = DriveWatch.Added(new[] { @"c:\" }, after);
            Eq(1, added.Count, "one new drive (case-insensitive)");
            Eq(@"E:\", added[0].Root, "new drive is E:");
            Eq(0, DriveWatch.Added(new[] { @"C:\", @"E:\" }, after).Count, "no new drives");
            Eq(@"E:\ (USB) — 이동식", after[1].Describe(), "describe drive");
        }
    }
}
