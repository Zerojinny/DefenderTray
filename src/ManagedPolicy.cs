using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace DefenderTray
{
    // 조직 정책(그룹 정책 / MDM)이 이 앱이 바꾸는 Defender 설정을 고정하고 있는지 확인.
    //
    // 정책이 정한 항목은 MSFT_MpPreference(= Set-MpPreference)로 바꿔도 실제로는 정책 값이 쓰임.
    // 단, 그룹 정책의 "Configure local setting override for ..."(LocalSettingOverride*)가 켜져 있으면 로컬 설정이 이김.
    //
    //   그룹 정책: HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\<하위 키>\<값>
    //   MDM(Intune 등, Policy CSP): HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Defender\<값>
    //     MDM 값 이름·의미가 그룹 정책과 다른 항목은 확실한 것만 대응시킴. 나머지는 적용 후 다시 읽어 확인.
    class PolicyValue
    {
        public string Source;    // "그룹 정책" / "MDM(Intune 등)"
        public object Value;     // MSFT_MpPreference 기준으로 바꾼 값 (bool / int / TimeSpan)
    }

    static class ManagedPolicy
    {
        public const string GpoRoot = @"SOFTWARE\Policies\Microsoft\Windows Defender";
        public const string MdmRoot = @"SOFTWARE\Microsoft\PolicyManager\current\device\Defender";
        static string GpoSource { get { return Tr.T("그룹 정책", "Group Policy"); } }
        static string MdmSource { get { return Tr.T("MDM(Intune 등)", "MDM (Intune, etc.)"); } }

        // (키 경로, 값 이름) → 값. 테스트에서는 사전으로 바꿔 끼움
        public static Func<string, string, object> Reader = ReadRegistry;

        enum Conv { Same, Bool, InverseBool, MinutesToTime }

        class Map
        {
            public string Pref, GpoKey, GpoName, OverrideName, MdmName;
            public Conv GpoConv, MdmConv;
        }

        static readonly Map[] Maps =
        {
            new Map { Pref = "ScanParameters", GpoKey = "Scan", GpoName = "ScanParameters", OverrideName = "LocalSettingOverrideScanParameters", MdmName = "ScanParameter" },
            new Map { Pref = "ScanScheduleDay", GpoKey = "Scan", GpoName = "ScheduleDay", OverrideName = "LocalSettingOverrideScheduleDay", MdmName = "ScheduleScanDay" },
            new Map { Pref = "ScanScheduleTime", GpoKey = "Scan", GpoName = "ScheduleTime", OverrideName = "LocalSettingOverrideScheduleTime", MdmName = "ScheduleScanTime", GpoConv = Conv.MinutesToTime, MdmConv = Conv.MinutesToTime },
            new Map { Pref = "ScanAvgCPULoadFactor", GpoKey = "Scan", GpoName = "AvgCPULoadFactor", OverrideName = "LocalSettingOverrideAvgCPULoadFactor", MdmName = "AvgCPULoadFactor" },
            new Map { Pref = "ScanOnlyIfIdleEnabled", GpoKey = "Scan", GpoName = "ScanOnlyIfIdle", GpoConv = Conv.Bool },
            new Map { Pref = "DisableCatchupQuickScan", GpoKey = "Scan", GpoName = "DisableCatchupQuickScan", GpoConv = Conv.Bool },
            new Map { Pref = "DisableCatchupFullScan", GpoKey = "Scan", GpoName = "DisableCatchupFullScan", GpoConv = Conv.Bool },
            new Map { Pref = "CheckForSignaturesBeforeRunningScan", GpoKey = "Scan", GpoName = "CheckForSignaturesBeforeRunningScan", GpoConv = Conv.Bool },
            new Map { Pref = "DisableRemovableDriveScanning", GpoKey = "Scan", GpoName = "DisableRemovableDriveScanning", GpoConv = Conv.Bool,
                      MdmName = "AllowFullScanRemovableDriveScanning", MdmConv = Conv.InverseBool },
            new Map { Pref = "DisableScanningMappedNetworkDrivesForFullScan", GpoKey = "Scan", GpoName = "DisableScanningMappedNetworkDrivesForFullScan", GpoConv = Conv.Bool,
                      MdmName = "AllowFullScanOnMappedNetworkDrives", MdmConv = Conv.InverseBool },
            new Map { Pref = "QuarantinePurgeItemsAfterDelay", GpoKey = "Quarantine", GpoName = "PurgeItemsAfterDelay" },
            new Map { Pref = "DisableRealtimeMonitoring", GpoKey = "Real-Time Protection", GpoName = "DisableRealtimeMonitoring", GpoConv = Conv.Bool,
                      OverrideName = "LocalSettingOverrideDisableRealtimeMonitoring", MdmName = "AllowRealtimeMonitoring", MdmConv = Conv.InverseBool },
        };

        static object ReadRegistry(string keyPath, string name)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(keyPath))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        static object Convert(object raw, Conv c)
        {
            int n;
            try { n = System.Convert.ToInt32(raw); } catch { return null; }
            switch (c)
            {
                case Conv.Bool: return n != 0;
                case Conv.InverseBool: return n == 0;
                case Conv.MinutesToTime: return TimeSpan.FromMinutes(n);
                default: return n;
            }
        }

        // 이 설정을 정책이 고정하고 있으면 그 값, 아니면 null (로컬 재정의가 허용된 경우도 null)
        public static PolicyValue Get(string pref)
        {
            foreach (Map m in Maps)
            {
                if (m.Pref != pref) continue;
                string gpoKey = GpoRoot + "\\" + m.GpoKey;
                object raw = Reader(gpoKey, m.GpoName);
                if (raw != null)
                {
                    object ov = m.OverrideName == null ? null : Reader(gpoKey, m.OverrideName);
                    bool overrideAllowed = ov != null && Convert(ov, Conv.Same) is int && (int)Convert(ov, Conv.Same) == 1;
                    if (!overrideAllowed) return new PolicyValue { Source = GpoSource, Value = Convert(raw, m.GpoConv) };
                }
                if (m.MdmName != null)
                {
                    object mraw = Reader(MdmRoot, m.MdmName);
                    if (mraw != null) return new PolicyValue { Source = MdmSource, Value = Convert(mraw, m.MdmConv) };
                }
                return null;
            }
            return null;
        }

        // 조직이 로컬 관리자의 제외를 무시하게 했는지 ("Configure local administrator merge behavior for lists" 사용 안 함)
        public static bool LocalExclusionsIgnored()
        {
            object v = Reader(GpoRoot, "DisableLocalAdminMerge");
            try { return v != null && System.Convert.ToInt32(v) == 1; } catch { return false; }
        }

        // 조직이 로컬 관리자에게 제외 목록을 숨겼는지 (그러면 관리자 권한으로도 목록이 "N/A..."로 옴)
        public static bool ExclusionsHidden()
        {
            object v = Reader(GpoRoot, "HideExclusionsFromLocalAdmins");
            try { return v != null && System.Convert.ToInt32(v) == 1; } catch { return false; }
        }

        // MDM(Intune 등)이 Defender를 관리 중인지 (값 이름을 일일이 대응시키지 못한 항목 경고용)
        public static bool MdmManaged()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(MdmRoot))
                {
                    if (k == null) return false;
                    foreach (string n in k.GetValueNames())
                        if (!n.EndsWith("_ProviderSet", StringComparison.OrdinalIgnoreCase) &&
                            !n.EndsWith("_WinningProvider", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        // 원하는 값과 정책 값이 다르면 그 정책 (= 적용해도 실제로는 바뀌지 않음), 아니면 null
        public static PolicyValue Blocks(string pref, object wanted)
        {
            PolicyValue p = Get(pref);
            if (p == null || p.Value == null) return null;
            return SameValue(p.Value, wanted) ? null : p;
        }

        public static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == b;
            if (a is TimeSpan || b is TimeSpan)
                return a is TimeSpan && b is TimeSpan && (TimeSpan)a == (TimeSpan)b;
            if (a is bool || b is bool)
                return a is bool && b is bool && (bool)a == (bool)b;
            try { return System.Convert.ToInt64(a) == System.Convert.ToInt64(b); } catch { return Equals(a, b); }
        }
    }
}
