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
    // 폴더·파일 제외의 검사 범위.
    //   모든 검사 제외 : "D:\Backup"                            → 실시간·수동·예약 검사 모두 건너뜀
    //   실시간만 제외  : "D:\Backup\:{ScanTrigger:OnAccess}"   → 복사·열기 때만 건너뛰고 수동·예약 검사는 함
    // Microsoft 문서의 "contextual exclusion" 형식 (플랫폼 4.18.2205.7 이상). Windows 보안 앱은 이 형식을 지원하지 않음.
    static class ExclusionScope
    {
        public const string RealtimeSuffix = @"\:{ScanTrigger:OnAccess}";
        const string Marker = @"\:{";

        static int MarkerIndex(string v) { return v == null ? -1 : v.IndexOf(Marker, StringComparison.Ordinal); }

        // 조건을 뺀 실제 경로. "D:\:{...}" → "D:\"
        public static string BasePath(string v)
        {
            int i = MarkerIndex(v);
            if (i < 0) return v;
            string b = v.Substring(0, i);
            return (b.Length == 2 && b[1] == ':') ? b + "\\" : b;
        }

        // 조건 부분 ("\:{...}"), 없으면 ""
        public static string Context(string v)
        {
            int i = MarkerIndex(v);
            return i < 0 ? "" : v.Substring(i);
        }

        public static bool IsRealtimeOnly(string v) { return Context(v) == RealtimeSuffix; }   // 키워드는 대소문자 구분

        public static string Make(string path, bool realtimeOnly)
        {
            if (!realtimeOnly) return path;
            return path.TrimEnd('\\') + RealtimeSuffix;
        }

        // 같은 조건을 유지한 채 경로만 바꿈 (네트워크 경로 변환용)
        public static string WithBase(string v, string newBase)
        {
            string ctx = Context(v);
            return ctx.Length == 0 ? newBase : newBase.TrimEnd('\\') + ctx;
        }

        public static string Label(string v)
        {
            string ctx = Context(v);
            if (ctx.Length == 0) return Tr.T("모든 검사 제외", "All scans");
            if (ctx == RealtimeSuffix) return Tr.T("실시간만 제외", "Real-time only");
            return Tr.T("조건부 ", "Conditional ") + ctx.Substring(2);
        }
    }
}
