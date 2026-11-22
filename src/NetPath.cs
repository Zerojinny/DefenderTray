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
    // 네트워크 드라이브 문자(Y:) → 실제 네트워크 경로(\\서버\공유) 변환.
    // Microsoft 문서 권장: "Don't exclude mapped network drives. Specify the actual network path instead."
    // 자세한 내용: docs/network-drive-exclusions.md
    static class NetPath
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        // "Y:\a\b" → "\\서버\공유\a\b". 네트워크 드라이브가 아니면 null
        public static string ToUnc(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':') return null;
            string letter = char.ToUpperInvariant(path[0]).ToString();
            string remote = null;

            // 1) 사용자 레지스트리의 영구 연결 정보 (관리자 권한 프로그램에서도 읽힘)
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Network\" + letter))
                    if (k != null) remote = k.GetValue("RemotePath") as string;
            }
            catch { }

            // 2) 현재 세션에서 보이는 연결 (임시 연결 포함)
            if (string.IsNullOrEmpty(remote))
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    if (WNetGetConnection(letter + ":", sb, ref len) == 0) remote = sb.ToString();
                }
                catch { }
            }

            if (string.IsNullOrEmpty(remote) || !remote.StartsWith(@"\\")) return null;
            string rest = path.Length > 2 ? path.Substring(2) : "";
            return (remote.TrimEnd('\\') + rest).TrimEnd('\\');
        }
    }
}
