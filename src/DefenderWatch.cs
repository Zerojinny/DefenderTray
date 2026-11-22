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
    // 이 프로그램이 직접 한 변경 기록. 오른쪽 클릭 메뉴로 뜬 별도 프로세스의 변경도 함께 남도록 레지스트리에 저장.
    // 이벤트 로그의 변경이 여기 기록과 맞으면(같은 내용, 2분 이내) 외부 변경으로 알리지 않음.
    static class SelfChanges
    {
        const string Name = "RecentChanges";
        static readonly TimeSpan Keep = TimeSpan.FromDays(7);
        static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

        public static void Record(string op, string kind, string value)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                var keep = new List<string>();
                foreach (string line in AppSettings.GetLines(Name) ?? new string[0])
                {
                    DateTime t;
                    if (TryTime(line, out t) && now - t < Keep) keep.Add(line);
                }
                // 형식: 시각|프로세스ID|동작|종류|값  (프로세스ID로 오른쪽 클릭 메뉴처럼 별도 프로세스가 한 변경을 구분)
                keep.Add(now.Ticks + "|" + Process.GetCurrentProcess().Id + "|" + op + "|" + kind + "|" + (value ?? ""));
                if (keep.Count > 200) keep.RemoveRange(0, keep.Count - 200);
                AppSettings.SetLines(Name, keep.ToArray());
            }
            catch { }
        }

        // 이 프로그램이 한 변경이면 true, pid = 변경한 프로세스 ID
        public static bool IsMine(DateTime eventUtc, string op, string kind, string value, out int pid)
        {
            pid = 0;
            foreach (string line in AppSettings.GetLines(Name) ?? new string[0])
            {
                string[] p = line.Split(new[] { '|' }, 5);
                DateTime t;
                if (p.Length < 5 || !TryTime(line, out t)) continue;
                if (p[2] != op || !string.Equals(p[3], kind, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Norm(p[4]), Norm(value), StringComparison.OrdinalIgnoreCase)) continue;
                if (Math.Abs((eventUtc - t).TotalMinutes) > Window.TotalMinutes) continue;
                int.TryParse(p[1], out pid);
                return true;
            }
            return false;
        }

        static string Norm(string v) { return (v ?? "").Trim().TrimEnd('\\'); }

        static bool TryTime(string line, out DateTime t)
        {
            t = DateTime.MinValue;
            int bar = line.IndexOf('|');
            long ticks;
            if (bar <= 0 || !long.TryParse(line.Substring(0, bar), out ticks)) return false;
            try { t = new DateTime(ticks, DateTimeKind.Utc); return true; } catch { return false; }
        }
    }

    class DefenderChange
    {
        public DateTime Time;      // 현지 시각
        public string Text;        // 사용자에게 보여 줄 한 줄
        public bool IsRtpOn, IsRtpOff;
        public bool IsExclusion;
        public string Kind, Value;   // 제외 종류(Paths/Processes/...)와 값
        public bool Added;           // 제외 추가(true) / 삭제(false)
        public bool Policy;          // 그룹 정책 경로의 변경
        public bool Mine;          // 이 프로그램의 다른 프로세스(오른쪽 클릭 메뉴)가 한 변경
    }

    // Defender 이벤트 로그(Operational)를 읽어 이 프로그램 밖에서 일어난 설정 변경을 찾음.
    //   5000 = 실시간 보호 켜짐, 5001 = 꺼짐, 5007 = 설정 변경(이전 값 / 새 값이 레지스트리 경로로 기록됨)
    static class DefenderWatch
    {
        const string LogName = "Microsoft-Windows-Windows Defender/Operational";
        const string LastKey = "LastEventRecord";

        // 마지막으로 로그를 읽는 데 성공했는지 (실패하면 상태 폴링 알림으로 대신함)
        public static bool Available = true;

        public static string KindOf(string param)
        {
            switch (param)
            {
                case "ExclusionPath": return "Paths";
                case "ExclusionProcess": return "Processes";
                case "ExclusionExtension": return "Extensions";
                case "ExclusionIpAddress": return "IpAddresses";
                default: return param;
            }
        }

        // 지난번에 읽은 뒤로 새로 기록된 외부 변경. 처음 실행이면 기준점만 잡고 빈 목록을 돌려줌
        public static List<DefenderChange> ReadNew()
        {
            var result = new List<DefenderChange>();
            try
            {
                long newest = NewestRecordId();
                long last;
                string saved = AppSettings.Get(LastKey);
                if (saved == null || !long.TryParse(saved, out last) || last > newest)
                {
                    // 첫 실행이거나 로그가 지워짐 → 지금부터 감시
                    AppSettings.Set(LastKey, newest.ToString());
                    Available = true;
                    return result;
                }
                if (last == newest) { Available = true; return result; }

                string xpath = "*[System[(EventID=5000 or EventID=5001 or EventID=5007) and EventRecordID>" + last + "]]";
                long max = last;
                using (var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName, xpath)))
                {
                    for (EventRecord r = reader.ReadEvent(); r != null; r = reader.ReadEvent())
                    {
                        using (r)
                        {
                            if (r.RecordId.HasValue && r.RecordId.Value > max) max = r.RecordId.Value;
                            DefenderChange c = Interpret(r);
                            if (c != null) result.Add(c);
                        }
                    }
                }
                AppSettings.Set(LastKey, Math.Max(max, newest).ToString());
                Available = true;
            }
            catch { Available = false; }
            return result;
        }

        static long NewestRecordId()
        {
            using (var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName) { ReverseDirection = true }))
            using (EventRecord r = reader.ReadEvent())
                return r != null && r.RecordId.HasValue ? r.RecordId.Value : 0;
        }

        static DefenderChange Interpret(EventRecord r)
        {
            DateTime local = r.TimeCreated.HasValue ? r.TimeCreated.Value : DateTime.Now;
            DateTime utc = local.ToUniversalTime();

            if (r.Id == 5000 || r.Id == 5001)
            {
                bool on = r.Id == 5000;
                int rtpPid;
                if (SelfChanges.IsMine(utc, on ? "on" : "off", "RTP", "", out rtpPid)) return null; // 트레이가 직접 알림
                return new DefenderChange { Time = local, IsRtpOn = on, IsRtpOff = !on, Text = on ? Tr.T("실시간 보호 켜짐", "Real-time protection on") : Tr.T("실시간 보호 꺼짐", "Real-time protection off") };
            }

            if (r.Id == 5007 && r.Properties.Count >= 4)
            {
                string oldV = Prop(r, 2), newV = Prop(r, 3);
                string kind = null, value = null;
                bool policy = false, added;
                if (oldV.Length == 0 && Parse(newV, out kind, out value, out policy)) added = true;
                else if (newV.Length == 0 && Parse(oldV, out kind, out value, out policy)) added = false;
                else return null; // 제외 이외의 설정 변경은 무시
                if (!IsTrackedKind(kind)) return null;

                int pid = 0;
                bool mine = !policy && SelfChanges.IsMine(utc, added ? "add" : "remove", kind, value, out pid);
                // 트레이 프로세스 자신이 한 변경은 이미 알렸으므로 건너뜀
                if (mine && pid == Process.GetCurrentProcess().Id) return null;
                return new DefenderChange
                {
                    Time = local,
                    IsExclusion = true,
                    Mine = mine,
                    Kind = kind,
                    Value = value,
                    Added = added,
                    Policy = policy,
                    Text = Describe(kind, added, value) + (policy ? Tr.T(" (그룹 정책)", " (Group Policy)") : "")
                };
            }
            return null;
        }

        // 알림에 쓰는 한 줄 — 이 프로그램의 변경과 외부 변경 모두 같은 형식
        public static string Describe(string kind, bool added, string value)
        {
            if (kind == "Paths" && ExclusionScope.Context(value).Length > 0)
                return Tr.T("폴더·파일 제외 (", "File/folder exclusion (") + ExclusionScope.Label(value) + ")" + (added ? Tr.T(" 추가: ", " added: ") : Tr.T(" 삭제: ", " removed: ")) + ExclusionScope.BasePath(value);
            string what =
                kind == "Paths" ? Tr.T("폴더·파일 제외", "File/folder exclusion") :
                kind == "Processes" ? Tr.T("프로세스 제외", "Process exclusion") :
                kind == "Extensions" ? Tr.T("확장자 제외", "Extension exclusion") :
                kind == "IpAddresses" ? Tr.T("IP 주소 제외", "IP address exclusion") : kind + Tr.T(" 제외", " exclusion");
            return what + (added ? Tr.T(" 추가: ", " added: ") : Tr.T(" 삭제: ", " removed: ")) + value;
        }

        static string Prop(EventRecord r, int i)
        {
            object v = r.Properties[i].Value;
            return v == null ? "" : v.ToString().Trim();
        }

        // "HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths\Y:\ = 0x0" → kind=Paths, value=Y:\
        // 감시하는 제외 종류. Defender가 격리 복원 중에 스스로 잠깐 넣는 "TemporaryPaths" 같은 내부 항목은
        // 사용자가 관리하는 제외가 아니므로 외부 변경으로 보지 않음 (2026-11-20 복원 시험에서 확인)
        internal static bool IsTrackedKind(string kind)
        {
            return kind == "Paths" || kind == "Processes" || kind == "Extensions" || kind == "IpAddresses";
        }

        internal static bool Parse(string reg, out string kind, out string value, out bool policy)
        {
            kind = value = null;
            policy = false;
            const string marker = @"\Exclusions\";
            int i = reg.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int pol = reg.IndexOf(@"\Policies\", StringComparison.OrdinalIgnoreCase);
            policy = pol >= 0 && pol < i;
            string rest = reg.Substring(i + marker.Length);
            int slash = rest.IndexOf('\\');
            if (slash <= 0) return false;
            kind = rest.Substring(0, slash);
            string v = rest.Substring(slash + 1);
            int eq = v.LastIndexOf(" = ", StringComparison.Ordinal);
            if (eq >= 0) v = v.Substring(0, eq);
            value = v;
            return v.Length > 0;
        }
    }
}
