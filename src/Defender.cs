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
    enum RtpState { On, Off, Unknown }

    static class Defender
    {
        const string Ns = @"root\Microsoft\Windows\Defender";

        public static RtpState GetState()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(Ns,
                    "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object v = mo["RealTimeProtectionEnabled"];
                        if (v is bool) return (bool)v ? RtpState.On : RtpState.Off;
                    }
                }
            }
            catch { }
            return RtpState.Unknown;
        }

        public static bool IsTamperProtected()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(Ns,
                    "SELECT IsTamperProtected FROM MSFT_MpComputerStatus"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object v = mo["IsTamperProtected"];
                        if (v is bool) return (bool)v;
                    }
                }
            }
            catch { }
            return false;
        }

        // enable = true 이면 실시간 보호 켜기
        public static void SetRealtime(bool enable)
        {
            SelfChanges.Record(enable ? "on" : "off", "RTP", "");
            using (var mc = new ManagementClass(Ns, "MSFT_MpPreference", null))
            {
                ManagementBaseObject inParams = mc.GetMethodParameters("Set");
                inParams["DisableRealtimeMonitoring"] = !enable;
                ManagementBaseObject result = mc.InvokeMethod("Set", inParams, null);
                if (result != null && result["ReturnValue"] != null)
                {
                    uint rv = Convert.ToUInt32(result["ReturnValue"]);
                    if (rv != 0)
                        throw new Exception(string.Format(Tr.T("MSFT_MpPreference.Set 실패 (0x{0:X8})", "MSFT_MpPreference.Set failed (0x{0:X8})"), rv));
                }
            }
        }

        public static string[] GetExclusionProcesses() { return GetStringArray("ExclusionProcess"); }
        public static string[] GetExclusionPaths() { return GetStringArray("ExclusionPath"); }

        static string[] GetStringArray(string prop)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(Ns,
                    "SELECT " + prop + " FROM MSFT_MpPreference"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object v = mo[prop];
                        if (v is string[]) return (string[])v;
                    }
                }
            }
            catch { }
            return new string[0];
        }

        static void AddOrRemove(string method, string param, string value)
        {
            // 이 프로그램(오른쪽 클릭 메뉴로 뜬 프로세스 포함)이 한 변경을 기록 → 외부 변경 알림에서 제외
            string kind = DefenderWatch.KindOf(param);
            bool add = method == "Add";
            SelfChanges.Record(add ? "add" : "remove", kind, value);
            try
            {
                using (var mc = new ManagementClass(Ns, "MSFT_MpPreference", null))
                {
                    ManagementBaseObject inParams = mc.GetMethodParameters(method);
                    inParams[param] = new string[] { value };
                    ManagementBaseObject result = mc.InvokeMethod(method, inParams, null);
                    if (result != null && result["ReturnValue"] != null)
                    {
                        uint rv = Convert.ToUInt32(result["ReturnValue"]);
                        if (rv != 0)
                            throw new Exception(string.Format(Tr.T("MSFT_MpPreference.{0} 실패 (0x{1:X8})", "MSFT_MpPreference.{0} failed (0x{1:X8})"), method, rv));
                    }
                }
                ActivityLog.Write(ActivityLog.Internal, DefenderWatch.Describe(kind, add, value));
            }
            catch (Exception ex)
            {
                ActivityLog.Write(ActivityLog.Failed, DefenderWatch.Describe(kind, add, value) + " — " + ExplainError(ex).Replace("\r\n", " "));
                throw;
            }
        }

        // ===== 예약 검사 설정 =====
        // 지정한 속성 값을 읽음 (MSFT_MpPreference 또는 MSFT_MpComputerStatus). 못 읽으면 빈 사전
        public static Dictionary<string, object> ReadProps(string cls, params string[] names)
        {
            var r = new Dictionary<string, object>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(Ns, "SELECT " + string.Join(",", names) + " FROM " + cls))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        foreach (string n in names) r[n] = mo[n];
                        break;
                    }
                }
            }
            catch { }
            return r;
        }

        // 여러 설정을 한 번에 바꿈. 값은 bool / byte / TimeSpan(시각)
        public static void SetPreferences(Dictionary<string, object> values)
        {
            if (values.Count == 0) return;
            using (var mc = new ManagementClass(Ns, "MSFT_MpPreference", null))
            {
                ManagementBaseObject inParams = mc.GetMethodParameters("Set");
                foreach (var kv in values)
                    inParams[kv.Key] = kv.Value is TimeSpan
                        ? (object)ManagementDateTimeConverter.ToDmtfTimeInterval((TimeSpan)kv.Value)
                        : kv.Value;
                ManagementBaseObject result = mc.InvokeMethod("Set", inParams, null);
                if (result != null && result["ReturnValue"] != null)
                {
                    uint rv = Convert.ToUInt32(result["ReturnValue"]);
                    if (rv != 0)
                        throw new Exception(string.Format(Tr.T("MSFT_MpPreference.Set 실패 (0x{0:X8})", "MSFT_MpPreference.Set failed (0x{0:X8})"), rv));
                }
            }
        }

        // ScanType: 1 = 빠른 검사, 2 = 전체 검사
        public static void StartScanOfType(int scanType)
        {
            using (var mc = new ManagementClass(Ns, "MSFT_MpScan", null))
            {
                ManagementBaseObject inParams = mc.GetMethodParameters("Start");
                inParams["ScanType"] = (byte)scanType;
                ManagementBaseObject result = mc.InvokeMethod("Start", inParams, null);
                if (result != null && result["ReturnValue"] != null)
                {
                    uint rv = Convert.ToUInt32(result["ReturnValue"]);
                    if (rv != 0) throw new Exception(string.Format(Tr.T("MSFT_MpScan.Start 실패 (0x{0:X8})", "MSFT_MpScan.Start failed (0x{0:X8})"), rv));
                }
            }
        }

        // 외부 변경 되돌리기용: 종류(Paths/Processes/Extensions/IpAddresses)의 제외가 있도록(present) 또는 없도록
        public static void SetExclusion(string kind, string value, bool present)
        {
            string param =
                kind == "Paths" ? "ExclusionPath" :
                kind == "Processes" ? "ExclusionProcess" :
                kind == "Extensions" ? "ExclusionExtension" :
                kind == "IpAddresses" ? "ExclusionIpAddress" : null;
            if (param == null) throw new Exception(Tr.T("알 수 없는 제외 종류: ", "Unknown exclusion type: ") + kind);
            AddOrRemove(present ? "Add" : "Remove", param, value);
        }

        public static void AddExclusionProcess(string value) { AddOrRemove("Add", "ExclusionProcess", value); }
        public static void RemoveExclusionProcess(string value) { AddOrRemove("Remove", "ExclusionProcess", value); }
        public static void AddExclusionPath(string value) { AddOrRemove("Add", "ExclusionPath", value); }

        // 폴더/파일 제외 추가. 네트워크 드라이브 문자면 실제 네트워크 경로로 바꿔 등록하고, 등록한 값을 돌려줌.
        // realtimeOnly = true 이면 실시간 보호에서만 제외(수동·예약 검사는 함)
        public static string AddExclusionPathResolved(string path, bool realtimeOnly)
        {
            string unc = NetPath.ToUnc(path);
            string target = ExclusionScope.Make(unc ?? path, realtimeOnly);
            AddExclusionPath(target);
            if (unc != null)
                ActivityLog.Write(ActivityLog.Info, Tr.T("네트워크 드라이브 ", "Network drive ") + path + Tr.T(" → 실제 경로 ", " → registered as real path ") + unc + Tr.T(" 로 등록", ""));
            return target;
        }

        // 제외 변경 실패 시 사용자에게 보여 줄 문구 (변조 방지가 원인이면 그렇게 안내)
        public static string ExplainError(Exception ex)
        {
            bool tamper = false;
            try { tamper = IsTamperProtected(); } catch { }
            return tamper
                ? Tr.T("변조 방지가 켜져 있어 제외를 변경할 수 없습니다.\r\n먼저 변조 방지를 끄세요.", "Tamper Protection is on, so exclusions cannot be changed.\r\nTurn off Tamper Protection first.")
                : ex.Message;
        }
        public static void RemoveExclusionPath(string value) { AddOrRemove("Remove", "ExclusionPath", value); }

        // ScanType: 3 = CustomScan (ScanPath 지정)
        public static void StartCustomScan(string path)
        {
            using (var mc = new ManagementClass(Ns, "MSFT_MpScan", null))
            {
                ManagementBaseObject inParams = mc.GetMethodParameters("Start");
                inParams["ScanPath"] = path;
                inParams["ScanType"] = (byte)3;
                mc.InvokeMethod("Start", inParams, null);
            }
        }
    }
}
