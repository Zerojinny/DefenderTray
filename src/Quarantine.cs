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
    // 격리 항목 조회·복원
    //   목록: WMI MSFT_MpThreatDetection 중 ThreatStatusID = 3(격리됨), 이름은 MSFT_MpThreat
    //   확인: MpCmdRun -Restore -ListAll (실제 격리 목록, 관리자 권한)
    //   복원: MpCmdRun -Restore -FilePath <원래 경로> [-Path <폴더>]
    //   항목별 영구 삭제는 공식 방법이 없음 → Windows 보안 보호 기록에서
    class QuarantineItem
    {
        public long ThreatId;
        public string Name;
        public int Severity;
        public string Path;        // 파일이면 원래 경로, 아니면 리소스 원문
        public bool IsFile;
        public DateTime Detected;
        public DateTime Quarantined;
        public string Process;
        public bool? InList;       // MpCmdRun 격리 목록에 있는지 (null = 확인 못 함)
    }

    static class Quarantine
    {
        const string Ns = @"root\Microsoft\Windows\Defender";

        public static List<QuarantineItem> GetItems()
        {
            var names = new Dictionary<long, KeyValuePair<string, int>>();
            try
            {
                using (var s = new ManagementObjectSearcher(Ns, "SELECT ThreatID, ThreatName, SeverityID FROM MSFT_MpThreat"))
                    foreach (ManagementObject mo in s.Get())
                        names[Convert.ToInt64(mo["ThreatID"])] = new KeyValuePair<string, int>(
                            (mo["ThreatName"] ?? "").ToString(), mo["SeverityID"] == null ? 0 : Convert.ToInt32(mo["SeverityID"]));
            }
            catch { }

            var list = new List<QuarantineItem>();
            using (var s = new ManagementObjectSearcher(Ns,
                "SELECT ThreatID, Resources, InitialDetectionTime, RemediationTime, ProcessName FROM MSFT_MpThreatDetection WHERE ThreatStatusID = 3"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    long id = Convert.ToInt64(mo["ThreatID"]);
                    KeyValuePair<string, int> nm;
                    names.TryGetValue(id, out nm);
                    DateTime det = Dmtf(mo["InitialDetectionTime"]) ?? DateTime.MinValue;
                    DateTime rem = Dmtf(mo["RemediationTime"]) ?? det;
                    string proc = (mo["ProcessName"] ?? "").ToString();
                    string[] res = mo["Resources"] as string[] ?? new string[0];
                    foreach (string r in res)
                    {
                        // "file:_H:\backup\a.exe" → 종류 file, 경로 H:\backup\a.exe
                        int sep = r.IndexOf(":_", StringComparison.Ordinal);
                        string type = sep > 0 ? r.Substring(0, sep) : "";
                        string path = sep > 0 ? r.Substring(sep + 2) : r;
                        list.Add(new QuarantineItem
                        {
                            ThreatId = id,
                            Name = string.IsNullOrEmpty(nm.Key) ? Tr.T("(위협 ID ", "(threat ID ") + id + ")" : nm.Key,
                            Severity = nm.Value,
                            Path = path,
                            IsFile = string.Equals(type, "file", StringComparison.OrdinalIgnoreCase),
                            Detected = det,
                            Quarantined = rem,
                            Process = proc
                        });
                    }
                }
            }
            list.Sort((a, b) => b.Quarantined.CompareTo(a.Quarantined));
            return list;
        }

        static DateTime? Dmtf(object v)
        {
            try { if (v is string && ((string)v).Length > 0) return ManagementDateTimeConverter.ToDateTime((string)v); } catch { }
            return null;
        }

        // 실제 격리 목록과 대조. 실패하면 InList = null 로 둠
        public static void MarkInList(List<QuarantineItem> items)
        {
            string output;
            int rc = Run("-Restore -ListAll", out output);
            if (rc != 0) return;
            foreach (var it in items)
                it.InList = output.IndexOf(it.Path, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Restore(QuarantineItem it, string toFolder)
        {
            string args = "-Restore -FilePath \"" + it.Path + "\"";
            if (toFolder != null) args += " -Path \"" + toFolder.TrimEnd('\\') + "\"";
            string output;
            int rc = Run(args, out output);
            if (rc != 0)
            {
                string msg = output.Trim();
                if (msg.Length > 400) msg = msg.Substring(0, 400) + "…";
                throw new Exception(Tr.T("MpCmdRun 복원 실패 (코드 ", "MpCmdRun restore failed (code ") + rc + ")" + (msg.Length > 0 ? "\r\n" + msg : ""));
            }
        }

        // Windows 보안 앱의 "장치에서 허용"과 같은 효과: 이 위협 ID를 허용(6)으로 지정
        public static void AllowThreat(long threatId)
        {
            using (var mc = new ManagementClass(Ns, "MSFT_MpPreference", null))
            {
                ManagementBaseObject p = mc.GetMethodParameters("Add");
                p["ThreatIDDefaultAction_Ids"] = new long[] { threatId };
                p["ThreatIDDefaultAction_Actions"] = new byte[] { 6 };
                ManagementBaseObject result = mc.InvokeMethod("Add", p, null);
                if (result != null && result["ReturnValue"] != null && Convert.ToUInt32(result["ReturnValue"]) != 0)
                    throw new Exception(string.Format(Tr.T("위협 허용 등록 실패 (0x{0:X8})", "Failed to allow threat (0x{0:X8})"), Convert.ToUInt32(result["ReturnValue"])));
            }
        }

        public static string MpCmdRunPath()
        {
            try
            {
                string root = @"C:\ProgramData\Microsoft\Windows Defender\Platform";
                if (Directory.Exists(root))
                {
                    string[] dirs = Directory.GetDirectories(root);
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    for (int i = dirs.Length - 1; i >= 0; i--)
                    {
                        string p = System.IO.Path.Combine(dirs[i], "MpCmdRun.exe");
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
        }

        static int Run(string args, out string output)
        {
            var psi = new ProcessStartInfo(MpCmdRunPath(), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try { psi.StandardOutputEncoding = psi.StandardErrorEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch { }
            using (var p = Process.Start(psi))
            {
                string err = null;
                var t = new Thread(() => { err = p.StandardError.ReadToEnd(); }) { IsBackground = true };
                t.Start();
                string o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } output = o; return -1; }
                t.Join(2000);
                output = o + (err ?? "");
                return p.ExitCode;
            }
        }

        public static string SeverityText(int s)
        {
            switch (s)
            {
                case 1: return Tr.T("낮음", "Low");
                case 2: return Tr.T("보통", "Moderate");
                case 4: return Tr.T("높음", "High");
                case 5: return Tr.T("심각", "Severe");
                default: return Tr.T("알 수 없음", "Unknown");
            }
        }
    }
}
