using System;
using System.Collections.Generic;
using System.IO;

namespace DefenderTray
{
    class DriveEntry
    {
        public string Root;     // "E:\"
        public string Label;    // 볼륨 이름 (없으면 "")
        public bool Removable;  // 이동식(USB 메모리 등). USB 외장 HDD/SSD는 보통 '고정'으로 보임

        public string Describe()
        {
            return Root + (string.IsNullOrEmpty(Label) ? "" : " (" + Label + ")") + " — " + (Removable ? Tr.T("이동식", "Removable") : Tr.T("고정", "Fixed"));
        }
    }

    // 새로 연결된 드라이브 감지: 주기적으로 드라이브 목록을 찍어 이전 목록과 비교
    static class DriveWatch
    {
        // 준비된 이동식·고정 드라이브 (네트워크·광학 드라이브는 응답이 느릴 수 있어 제외)
        public static List<DriveEntry> Snapshot()
        {
            var list = new List<DriveEntry>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    string label = "";
                    try { label = d.VolumeLabel ?? ""; } catch { }
                    list.Add(new DriveEntry { Root = d.RootDirectory.FullName, Label = label, Removable = d.DriveType == DriveType.Removable });
                }
                catch { }
            }
            return list;
        }

        // before에 없던 드라이브 (드라이브 문자 기준, 대소문자 무시)
        public static List<DriveEntry> Added(IEnumerable<string> beforeRoots, IEnumerable<DriveEntry> after)
        {
            var known = new HashSet<string>(beforeRoots, StringComparer.OrdinalIgnoreCase);
            var added = new List<DriveEntry>();
            foreach (DriveEntry d in after)
                if (!known.Contains(d.Root)) added.Add(d);
            return added;
        }
    }
}
