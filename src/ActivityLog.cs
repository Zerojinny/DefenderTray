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
    // 활동 기록 파일: %LOCALAPPDATA%\DefenderTray\activity.log
    // 한 줄 = "yyyy-MM-dd HH:mm:ss<TAB>구분<TAB>내용". 구분: 내부 / 외부 / 실패 / 정보
    // 알림 켜기/끄기와 관계없이 항상 기록. 1MB가 넘으면 activity.old.log로 넘기고 새로 시작.
    static class ActivityLog
    {
        public const string Internal = "내부", External = "외부", Failed = "실패", Info = "정보";
        const long MaxBytes = 1024 * 1024;
        static readonly object gate = new object();

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DefenderTray"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "activity.log"); } }
        public static string OldFilePath { get { return Path.Combine(Folder, "activity.old.log"); } }

        public static void Write(string kind, string text) { Write(kind, text, DateTime.Now); }

        public static void Write(string kind, string text, DateTime time)
        {
            string line = time.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + kind + "\t" +
                          (text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\t', ' ') + "\r\n";
            byte[] bytes = new UTF8Encoding(false).GetBytes(line);
            lock (gate)
            {
                // 오른쪽 클릭 메뉴 프로세스와 동시에 쓸 수 있으므로 잠깐씩 재시도
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Directory.CreateDirectory(Folder);
                        var fi = new FileInfo(FilePath);
                        if (fi.Exists && fi.Length > MaxBytes)
                        {
                            File.Copy(FilePath, OldFilePath, true);
                            File.Delete(FilePath);
                        }
                        using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                            fs.Write(bytes, 0, bytes.Length);
                        return;
                    }
                    catch (IOException) { Thread.Sleep(50); }
                    catch { return; }
                }
            }
        }

        public class Entry { public DateTime Time; public string Kind, Text; }

        // 최근 것부터. 이전 파일(activity.old.log)까지 포함
        public static List<Entry> ReadAll()
        {
            var list = new List<Entry>();
            foreach (string path in new[] { OldFilePath, FilePath })
            {
                string[] lines;
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                        lines = sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
                catch { continue; }
                foreach (string l in lines)
                {
                    string[] p = l.Split(new[] { '\t' }, 3);
                    DateTime t;
                    if (p.Length < 3 || !DateTime.TryParse(p[0], out t)) continue;
                    list.Add(new Entry { Time = t, Kind = p[1], Text = p[2] });
                }
            }
            list.Reverse();
            return list;
        }

        public static void Clear()
        {
            lock (gate)
            {
                try { File.Delete(FilePath); } catch { }
                try { File.Delete(OldFilePath); } catch { }
            }
        }
    }
}
