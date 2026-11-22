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
    // 사용자별 설정 저장소 (HKCU\Software\DefenderTray)
    static class AppSettings
    {
        const string KeyPath = @"Software\DefenderTray";

        public static string Get(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v == null ? null : v.ToString();
                }
            }
            catch { return null; }
        }

        public static void Set(string name, string value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(KeyPath)) k.SetValue(name, value); } catch { }
        }

        public static string[] GetLines(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return k == null ? null : k.GetValue(name) as string[];
            }
            catch { return null; }
        }

        public static void SetLines(string name, string[] lines)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(KeyPath)) k.SetValue(name, lines, RegistryValueKind.MultiString); } catch { }
        }
    }
}
