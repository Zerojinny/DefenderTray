using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace DefenderTray
{
    // Windows 보안 → 앱 및 브라우저 컨트롤 → 평판 기반 보호의 세 가지 SmartScreen
    enum SsPart { AppsFiles, Edge, Store }

    class SsState
    {
        public bool On;
        public string Detail;     // 상태의 출처 (기본값 / 로컬 설정 / 그룹 정책(이 PC) / 조직 정책 ...)
        public bool OrgLocked;    // 조직 정책으로 고정되어 이 앱에서 바꿀 수 없음
        public bool LocalGpo;     // 이 PC의 로컬 그룹 정책(gpedit)에 설정되어 있음
    }

    // 각 SmartScreen의 상태 읽기·바꾸기.
    //   앱 및 파일 확인: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SmartScreenEnabled = "Warn"(켜짐) / "Off"
    //     그룹 정책: 컴퓨터 구성 > 관리 템플릿 > Windows 구성 요소 > 파일 탐색기 > "Windows Defender SmartScreen 구성"
    //       → Software\Policies\Microsoft\Windows\System : EnableSmartScreen (1/0), ShellSmartScreenLevel ("Warn")
    //   Microsoft Edge: Edge는 설정을 자체 프로필에 보관 → 밖에서는 정책으로만 바꿀 수 있음
    //       → Software\Policies\Microsoft\Edge : SmartScreenEnabled (1/0). Edge를 다시 시작해야 반영.
    //       (정책이 없을 때 상태는 Edge가 HKCU\Software\Microsoft\Edge\SmartScreenEnabled 에 남기는 값으로 표시)
    //   Microsoft Store 앱: HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost : EnableWebContentEvaluation (1/0), 사용자별
    static class SmartScreen
    {
        public const string ExplorerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        public const string SysPolicyKey = @"Software\Policies\Microsoft\Windows\System";
        public const string EdgePolicyKey = @"Software\Policies\Microsoft\Edge";
        public const string EdgeUserKey = @"Software\Microsoft\Edge\SmartScreenEnabled";
        public const string StoreKey = @"Software\Microsoft\Windows\CurrentVersion\AppHost";

        public static readonly SsPart[] All = { SsPart.AppsFiles, SsPart.Edge, SsPart.Store };

        // (하이브 "HKLM"/"HKCU", 키, 값 이름) → 값. 테스트에서 바꿔 끼움
        public static Func<string, string, string, object> Reader = ReadRegistry;
        // 로컬 그룹 정책(Registry.pol)에 저장된 값
        public static Func<string, string, object> GpoReader = LocalGpo.GetValue;

        public static string Name(SsPart p)
        {
            switch (p)
            {
                case SsPart.AppsFiles: return Tr.T("앱 및 파일 확인", "Check apps and files");
                case SsPart.Edge: return "Microsoft Edge";
                default: return Tr.T("Microsoft Store 앱", "Microsoft Store apps");
            }
        }

        static object ReadRegistry(string hive, string key, string name)
        {
            try
            {
                RegistryKey root = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using (var k = root.OpenSubKey(key))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        static int? ToInt(object v)
        {
            if (v == null) return null;
            try { return Convert.ToInt32(v); } catch { return null; }
        }

        public static SsState Get(SsPart p)
        {
            switch (p)
            {
                case SsPart.AppsFiles:
                    return ResolveAppsFiles(Reader("HKLM", SysPolicyKey, "EnableSmartScreen"), GpoReader(SysPolicyKey, "EnableSmartScreen"),
                                            Reader("HKLM", ExplorerKey, "SmartScreenEnabled"));
                case SsPart.Edge:
                    return ResolveEdge(Reader("HKLM", EdgePolicyKey, "SmartScreenEnabled"), GpoReader(EdgePolicyKey, "SmartScreenEnabled"),
                                       Reader("HKCU", EdgePolicyKey, "SmartScreenEnabled"), Reader("HKCU", EdgeUserKey, ""));
                default:
                    return ResolveStore(Reader("HKCU", StoreKey, "EnableWebContentEvaluation"));
            }
        }

        public static Dictionary<SsPart, SsState> GetAll()
        {
            var d = new Dictionary<SsPart, SsState>();
            foreach (SsPart p in All) d[p] = Get(p);
            return d;
        }

        // ----- 상태 해석 (순수 함수, 시험 대상) -----

        // 정책 값이 이 PC의 로컬 그룹 정책과 같으면 '그룹 정책(이 PC)', 아니면 조직 정책으로 봄
        static SsState FromPolicy(object policy, object gpo)
        {
            bool local = gpo != null && ToInt(gpo) == ToInt(policy);
            return new SsState
            {
                On = ToInt(policy) != 0,
                Detail = local ? Tr.T("그룹 정책(이 PC)", "Group Policy (this PC)") : Tr.T("조직 정책", "Organization policy"),
                OrgLocked = !local,
                LocalGpo = local
            };
        }

        public static SsState ResolveAppsFiles(object policy, object gpo, object local)
        {
            if (ToInt(policy).HasValue) return FromPolicy(policy, gpo);
            string s = local as string;
            return new SsState
            {
                On = !string.Equals(s, "Off", StringComparison.OrdinalIgnoreCase),
                Detail = s == null ? Tr.T("기본값", "Default") : Tr.T("로컬 설정", "Local setting")
            };
        }

        public static SsState ResolveEdge(object policyLm, object gpo, object policyCu, object userValue)
        {
            if (ToInt(policyCu).HasValue)   // 사용자 정책은 이 앱이 쓰지 않으므로 조직 정책
                return new SsState { On = ToInt(policyCu) != 0, Detail = Tr.T("조직 정책(사용자)", "Organization policy (user)"), OrgLocked = true };
            if (ToInt(policyLm).HasValue) return FromPolicy(policyLm, gpo);
            int? u = ToInt(userValue);
            return new SsState { On = u != 0, Detail = u.HasValue ? Tr.T("Edge 설정", "Edge setting") : Tr.T("기본값", "Default") };
        }

        public static SsState ResolveStore(object local)
        {
            int? v = ToInt(local);
            return new SsState { On = v != 0, Detail = v.HasValue ? Tr.T("로컬 설정", "Local setting") : Tr.T("기본값", "Default") };
        }

        // ----- 바꾸기 -----

        // viaGpo: 로컬 그룹 정책(gpedit)으로도 설정. Edge는 끌 때 항상 정책을 씀. Store는 해당 정책 없음
        public static void Set(SsPart p, bool on, bool viaGpo)
        {
            SsState st = Get(p);
            if (st.OrgLocked)
                throw new InvalidOperationException(Name(p) + Tr.T("은(는) 조직 정책으로 고정되어 있어 바꿀 수 없습니다. IT 관리자에게 문의하세요.", " is locked by organization policy and cannot be changed. Contact your IT administrator."));

            switch (p)
            {
                case SsPart.AppsFiles:
                    if (viaGpo)
                    {
                        // gpedit "Windows Defender SmartScreen 구성": 켜기 = 사용(경고 표시), 끄기 = 사용 안 함
                        LocalGpo.SetValue(SysPolicyKey, "EnableSmartScreen", on ? 1 : 0);
                        LocalGpo.SetValue(SysPolicyKey, "ShellSmartScreenLevel", on ? (object)"Warn" : null);
                    }
                    else
                    {
                        LocalGpo.SetValue(SysPolicyKey, "EnableSmartScreen", null);
                        LocalGpo.SetValue(SysPolicyKey, "ShellSmartScreenLevel", null);
                    }
                    using (var k = Registry.LocalMachine.CreateSubKey(ExplorerKey))
                        k.SetValue("SmartScreenEnabled", on ? "Warn" : "Off", RegistryValueKind.String);
                    break;

                case SsPart.Edge:
                    // 끄기 = 정책 0. 켜기 = 정책 1(그룹 정책으로) 또는 정책 제거(Edge 자체 설정으로 돌아감)
                    LocalGpo.SetValue(EdgePolicyKey, "SmartScreenEnabled", !on ? (object)0 : viaGpo ? (object)1 : null);
                    break;

                default:
                    using (var k = Registry.CurrentUser.CreateSubKey(StoreKey))
                        k.SetValue("EnableWebContentEvaluation", on ? 1 : 0, RegistryValueKind.DWord);
                    break;
            }
        }

        // ----- 첨부 파일 관리자 정책: 이 목록의 형식은 SmartScreen 확인을 건너뜀 -----
        //   gpedit 사용자 구성 > 관리 템플릿 > Windows 구성 요소 > 첨부 파일 관리자 >
        //     "낮은 위험 파일 형식에 대한 포함 목록"(LowRiskFileTypes), "중간 위험 ..."(ModRiskFileTypes)
        //   2026-11-22 이 PC에서 확인: ModRiskFileTypes에 .exe가 있으면 인터넷에서 받은 .exe도 SmartScreen 창이 뜨지 않음
        public const string AssocKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Associations";

        // ".bat;.cmd;exe" → [".bat", ".cmd", ".exe"] (소문자, 중복 제거)
        public static List<string> ParseTypes(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (string raw in s.Split(';', ','))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t.Length == 0) continue;
                if (!t.StartsWith(".")) t = "." + t;
                if (!list.Contains(t)) list.Add(t);
            }
            return list;
        }

        // 사용자·컴퓨터 정책에서 SmartScreen 확인을 건너뛰는 파일 형식
        public static List<string> BypassedTypes()
        {
            var all = new List<string>();
            foreach (string hive in new[] { "HKCU", "HKLM" })
                foreach (string name in new[] { "LowRiskFileTypes", "ModRiskFileTypes" })
                    foreach (string t in ParseTypes(Reader(hive, AssocKey, name) as string))
                        if (!all.Contains(t)) all.Add(t);
            return all;
        }

        // 그룹 정책 사용 선호 (설정 창의 체크박스, 트레이 메뉴 동작에도 씀)
        public static bool PreferGpo(SsPart p)
        {
            return AppSettings.Get("SsGpo_" + p) == "1";
        }

        public static void SetPreferGpo(SsPart p, bool v)
        {
            AppSettings.Set("SsGpo_" + p, v ? "1" : "0");
        }

        // 트레이 메뉴의 '끄기'가 다루는 항목 (기본: 앱 및 파일 확인)
        public static List<SsPart> Targets
        {
            get
            {
                string s = AppSettings.Get("SsTargets");
                var list = new List<SsPart>();
                if (s == null) { list.Add(SsPart.AppsFiles); return list; }
                foreach (string n in s.Split(','))
                {
                    SsPart p;
                    if (TryParse(n, out p)) list.Add(p);
                }
                return list;
            }
            set
            {
                var names = new List<string>();
                foreach (SsPart p in value) names.Add(p.ToString());
                AppSettings.Set("SsTargets", string.Join(",", names.ToArray()));
            }
        }

        public static bool TryParse(string s, out SsPart p)
        {
            foreach (SsPart x in All)
                if (string.Equals(x.ToString(), (s ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { p = x; return true; }
            p = SsPart.AppsFiles;
            return false;
        }
    }

    // SmartScreen 시간 지정 끄기: 다시 켤 시각과, 그때 다시 켤 항목을 저장
    static class SsTimedOff
    {
        public static DateTime? UntilUtc
        {
            get
            {
                long t;
                string s = AppSettings.Get("SsOffUntil");
                if (string.IsNullOrEmpty(s) || !long.TryParse(s, out t) || t <= 0) return null;
                try { return new DateTime(t, DateTimeKind.Utc); } catch { return null; }
            }
            set { AppSettings.Set("SsOffUntil", value.HasValue ? value.Value.Ticks.ToString() : ""); }
        }

        public static List<SsPart> Parts
        {
            get
            {
                var list = new List<SsPart>();
                foreach (string n in (AppSettings.Get("SsTimedParts") ?? "").Split(','))
                {
                    SsPart p;
                    if (SmartScreen.TryParse(n, out p)) list.Add(p);
                }
                return list;
            }
            set
            {
                var names = new List<string>();
                if (value != null) foreach (SsPart p in value) names.Add(p.ToString());
                AppSettings.Set("SsTimedParts", string.Join(",", names.ToArray()));
            }
        }

        public static void Clear() { UntilUtc = null; Parts = null; }
    }
}
