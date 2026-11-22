using System;
using System.Management;

namespace DefenderTray
{
    // 보안 인텔리전스(정의) 버전 정보
    class SigInfo
    {
        public string Version;          // 예: 1.463.411.0
        public DateTime? LastUpdated;   // 현지 시각
        public int? AgeDays;            // Defender가 계산한 경과 일수 (모르면 null)
        public string Engine;           // 검사 엔진 버전
        public string Platform;         // 플랫폼(제품) 버전
    }

    // 보호 업데이트 = Windows 보안의 '업데이트 확인'과 같은 동작 (MSFT_MpSignature.Update)
    static class SignatureUpdate
    {
        const string Ns = @"root\Microsoft\Windows\Defender";

        // 경고 기준 선택지 (일). 0 = 경고 안 함
        public static readonly int[] StaleChoices = { 1, 3, 7, 0 };
        public const int DefaultStaleDays = 3;

        public static int StaleDays
        {
            get
            {
                int v;
                string s = AppSettings.Get("SignatureStaleDays");
                return s != null && int.TryParse(s, out v) && v >= 0 ? v : DefaultStaleDays;
            }
            set { AppSettings.Set("SignatureStaleDays", value.ToString()); }
        }

        public static SigInfo Get()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(Ns,
                    "SELECT AntivirusSignatureVersion, AntivirusSignatureLastUpdated, AntivirusSignatureAge, AMEngineVersion, AMProductVersion FROM MSFT_MpComputerStatus"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var r = new SigInfo
                        {
                            Version = mo["AntivirusSignatureVersion"] as string,
                            Engine = mo["AMEngineVersion"] as string,
                            Platform = mo["AMProductVersion"] as string,
                        };
                        string dt = mo["AntivirusSignatureLastUpdated"] as string;
                        if (!string.IsNullOrEmpty(dt))
                            try { r.LastUpdated = ManagementDateTimeConverter.ToDateTime(dt); } catch { }
                        object age = mo["AntivirusSignatureAge"];
                        if (age != null)
                        {
                            long a = Convert.ToInt64(age);
                            if (a >= 0 && a < 65535) r.AgeDays = (int)a;   // 65535 = 한 번도 업데이트 안 됨 등
                        }
                        return r;
                    }
                }
            }
            catch { }
            return null;
        }

        // 업데이트 실행 (몇십 초 ~ 몇 분 걸릴 수 있음). 원본은 Defender 설정(정책)의 순서를 따름
        public static void Update()
        {
            using (var mc = new ManagementClass(Ns, "MSFT_MpSignature", null))
            {
                ManagementBaseObject inParams = mc.GetMethodParameters("Update");
                ManagementBaseObject result = mc.InvokeMethod("Update", inParams, null);
                if (result != null && result["ReturnValue"] != null)
                {
                    uint rv = Convert.ToUInt32(result["ReturnValue"]);
                    if (rv != 0) throw new Exception(ExplainCode(rv));
                }
            }
        }

        public static string ExplainCode(uint rv)
        {
            string hex = string.Format("0x{0:X8}", rv);
            switch (rv)
            {
                case 0x8024402C:
                case 0x80072EE7:
                case 0x80072EFD:
                case 0x80072EE2:
                    return Tr.T("업데이트 서버에 연결할 수 없습니다. 인터넷 연결을 확인하세요. (", "Cannot reach the update server. Check the internet connection. (") + hex + ")";
                case 0x80070652:
                    return Tr.T("다른 업데이트가 진행 중입니다. 잠시 뒤 다시 시도하세요. (", "Another update is in progress. Try again later. (") + hex + ")";
                default:
                    return Tr.T("보호 업데이트 실패 (", "Protection update failed (") + hex + ")";
            }
        }

        // 기준 일수 이상 지났으면 true. 경고 꺼짐(0)·정보 없음이면 false
        public static bool IsStale(SigInfo s, int staleDays, DateTime now)
        {
            if (s == null || staleDays <= 0) return false;
            int? days = s.AgeDays;
            if (!days.HasValue && s.LastUpdated.HasValue) days = (int)(now - s.LastUpdated.Value).TotalDays;
            return days.HasValue && days.Value >= staleDays;
        }

        // "오늘 08:43" / "어제 08:43" / "3일 전 (11-19)"
        public static string WhenLabel(DateTime when, DateTime now)
        {
            int days = (now.Date - when.Date).Days;
            if (days <= 0) return Tr.T("오늘 ", "today ") + when.ToString("HH:mm");
            if (days == 1) return Tr.T("어제 ", "yesterday ") + when.ToString("HH:mm");
            return Tr.F("{0}일 전 ({1})", days == 1 ? "{0} day ago ({1})" : "{0} days ago ({1})", days, when.ToString("MM-dd"));
        }

        public static string StatusLine(SigInfo s, DateTime now)
        {
            if (s == null || string.IsNullOrEmpty(s.Version)) return Tr.T("보호 업데이트: 확인할 수 없음", "Protection updates: unavailable");
            return Tr.T("보호 업데이트: ", "Protection updates: ") + s.Version +
                   (s.LastUpdated.HasValue ? " (" + WhenLabel(s.LastUpdated.Value, now) + ")" : "");
        }

        public static string ChoiceLabel(int days)
        {
            if (days == 0) return Tr.T("경고 안 함", "Don't warn");
            return Tr.F("{0}일 이상 지나면", days == 1 ? "After {0} day" : "After {0} days", days);
        }
    }
}
