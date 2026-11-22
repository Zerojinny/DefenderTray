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
    // 탐색기 오른쪽 클릭 메뉴 통합 (HKCU\Software\Classes — 관리자 아니어도 현재 사용자에 적용)
    static class ShellIntegration
    {
        const string ExeVerb = @"Software\Classes\exefile\shell\DefenderTrayExclude";
        const string DirVerb = @"Software\Classes\Directory\shell\DefenderTrayExclude";

        public static bool IsRegistered()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ExeVerb)) return k != null; }
            catch { return false; }
        }

        public static void Register()
        {
            string exe = Application.ExecutablePath;
            CreateVerb(ExeVerb, Tr.T("Defender 검사 제외에 추가", "Add to Defender exclusions"), "\"" + exe + "\" --exclude-process \"%1\"", exe);
            CreateVerb(DirVerb, Tr.T("Defender 검사 제외에 추가(폴더)", "Add to Defender exclusions (folder)"), "\"" + exe + "\" --exclude-path \"%1\"", exe);
        }

        static void CreateVerb(string basePath, string title, string command, string iconExe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(basePath))
            {
                k.SetValue(null, title);
                k.SetValue("Icon", iconExe + ",0");
            }
            using (var c = Registry.CurrentUser.CreateSubKey(basePath + @"\command"))
                c.SetValue(null, command);
        }

        public static void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(ExeVerb, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(DirVerb, false); } catch { }
        }
    }
}
