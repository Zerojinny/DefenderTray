using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DefenderTray
{
    // 로컬 그룹 정책(gpedit.msc)과 같은 방식으로 컴퓨터 정책 값을 쓰고 지움.
    //   gpedit은 %WINDIR%\System32\GroupPolicy\Machine\Registry.pol 에 정책을 저장하고, 그 내용을
    //   HKLM\SOFTWARE\Policies\... 레지스트리에 반영함. 이 클래스는 둘 다 같게 맞춤:
    //     1) Registry.pol 항목 추가/삭제 (다른 정책 항목은 그대로 보존, 쓰기 전 백업)
    //     2) GPT.INI 컴퓨터 버전 올림 (그룹 정책이 다시 적용되도록)
    //     3) 레지스트리 값 즉시 반영 (gpupdate를 기다리지 않음)
    //   따라서 gpedit 화면에서도 "사용 / 값"으로 보이고, gpedit에서 되돌릴 수도 있음.
    class PolEntry
    {
        public string Key, Value;
        public uint Type;        // REG_SZ = 1, REG_DWORD = 4 ...
        public byte[] Data;
    }

    // Registry.pol (PReg) 파일 형식:
    //   "PReg" + 버전(DWORD 1), 이어서 항목마다 [키\0;값이름\0;형식;크기;데이터]
    //   괄호·세미콜론과 문자열은 UTF-16LE, 형식·크기는 DWORD
    static class PolFile
    {
        static readonly byte[] Header = { 0x50, 0x52, 0x65, 0x67, 0x01, 0x00, 0x00, 0x00 };

        public static List<PolEntry> Parse(byte[] b)
        {
            var list = new List<PolEntry>();
            if (b == null || b.Length == 0) return list;
            if (b.Length < 8 || b[0] != 0x50 || b[1] != 0x52 || b[2] != 0x65 || b[3] != 0x67)
                throw new InvalidDataException(Tr.T("Registry.pol 형식이 아닙니다", "Not a Registry.pol file"));
            int p = 8;
            while (p < b.Length)
            {
                Expect(b, ref p, '[');
                string key = ReadZ(b, ref p);
                Expect(b, ref p, ';');
                string val = ReadZ(b, ref p);
                Expect(b, ref p, ';');
                uint type = ReadU32(b, ref p);
                Expect(b, ref p, ';');
                uint size = ReadU32(b, ref p);
                Expect(b, ref p, ';');
                if (p + size > b.Length) throw new InvalidDataException(Tr.T("Registry.pol 데이터가 잘렸습니다", "Registry.pol data is truncated"));
                var data = new byte[size];
                Buffer.BlockCopy(b, p, data, 0, (int)size);
                p += (int)size;
                Expect(b, ref p, ']');
                list.Add(new PolEntry { Key = key, Value = val, Type = type, Data = data });
            }
            return list;
        }

        public static byte[] Build(List<PolEntry> entries)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(Header, 0, Header.Length);
                foreach (PolEntry e in entries)
                {
                    WriteChar(ms, '[');
                    WriteZ(ms, e.Key);
                    WriteChar(ms, ';');
                    WriteZ(ms, e.Value);
                    WriteChar(ms, ';');
                    WriteU32(ms, e.Type);
                    WriteChar(ms, ';');
                    WriteU32(ms, (uint)(e.Data == null ? 0 : e.Data.Length));
                    WriteChar(ms, ';');
                    if (e.Data != null) ms.Write(e.Data, 0, e.Data.Length);
                    WriteChar(ms, ']');
                }
                return ms.ToArray();
            }
        }

        static void Expect(byte[] b, ref int p, char c)
        {
            if (p + 2 > b.Length || b[p] != (byte)c || b[p + 1] != 0)
                throw new InvalidDataException(Tr.T("Registry.pol 형식 오류 (위치 ", "Registry.pol format error (offset ") + p + ")");
            p += 2;
        }

        static string ReadZ(byte[] b, ref int p)
        {
            int start = p;
            while (p + 1 < b.Length && (b[p] != 0 || b[p + 1] != 0)) p += 2;
            string s = Encoding.Unicode.GetString(b, start, p - start);
            p += 2;   // 끝 \0
            return s;
        }

        static uint ReadU32(byte[] b, ref int p)
        {
            if (p + 4 > b.Length) throw new InvalidDataException(Tr.T("Registry.pol 형식 오류", "Registry.pol format error"));
            uint v = BitConverter.ToUInt32(b, p);
            p += 4;
            return v;
        }

        static void WriteChar(Stream s, char c) { s.WriteByte((byte)c); s.WriteByte(0); }
        static void WriteZ(Stream s, string str)
        {
            byte[] t = Encoding.Unicode.GetBytes(str ?? "");
            s.Write(t, 0, t.Length);
            s.WriteByte(0); s.WriteByte(0);
        }
        static void WriteU32(Stream s, uint v) { byte[] t = BitConverter.GetBytes(v); s.Write(t, 0, 4); }

        // 같은 키·값 이름 항목을 바꾸거나(값 있음) 지움(null). 바뀌었으면 true
        public static bool SetDword(List<PolEntry> entries, string key, string value, int? data)
        {
            return SetValue(entries, key, value, data.HasValue ? (object)data.Value : null);
        }

        // data: int(REG_DWORD) / string(REG_SZ) / null(삭제)
        public static bool SetValue(List<PolEntry> entries, string key, string value, object data)
        {
            int idx = entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase) &&
                                             string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase));
            if (data == null)
            {
                if (idx < 0) return false;
                entries.RemoveAt(idx);
                return true;
            }
            PolEntry entry = data is string
                ? new PolEntry { Key = key, Value = value, Type = 1, Data = Encoding.Unicode.GetBytes((string)data + "\0") }
                : new PolEntry { Key = key, Value = value, Type = 4, Data = BitConverter.GetBytes(Convert.ToInt32(data)) };
            if (idx >= 0)
            {
                PolEntry old = entries[idx];
                if (old.Type == entry.Type && old.Data != null && Convert.ToBase64String(old.Data) == Convert.ToBase64String(entry.Data)) return false;
                entries[idx] = entry;
            }
            else entries.Add(entry);
            return true;
        }

        // 저장된 값: int(REG_DWORD) / string(REG_SZ) / null
        public static object GetValue(List<PolEntry> entries, string key, string value)
        {
            foreach (PolEntry e in entries)
            {
                if (!string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase) || e.Data == null) continue;
                if (e.Type == 4 && e.Data.Length == 4) return BitConverter.ToInt32(e.Data, 0);
                if (e.Type == 1) return Encoding.Unicode.GetString(e.Data).TrimEnd('\0');
            }
            return null;
        }

        public static int? GetDword(List<PolEntry> entries, string key, string value)
        {
            foreach (PolEntry e in entries)
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase) &&
                    e.Type == 4 && e.Data != null && e.Data.Length == 4)
                    return BitConverter.ToInt32(e.Data, 0);
            return null;
        }
    }

    // GPT.INI: [General] Version= 의 아래 16비트가 컴퓨터 정책 버전.
    // 레지스트리 정책을 쓰는 경우 gPCMachineExtensionNames에 관리 템플릿 확장이 있어야 함
    static class GptIni
    {
        public const string RegistryExt = "[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{D02B1F72-3407-48AE-BA88-E8213C6761F1}]";

        public static string Bump(string text)
        {
            var lines = new List<string>((text ?? "").Replace("\r\n", "\n").Split('\n'));
            lines.RemoveAll(l => l.Length == 0);
            if (lines.Count == 0 || !lines[0].Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase)) lines.Insert(0, "[General]");

            int verIdx = lines.FindIndex(l => l.StartsWith("Version=", StringComparison.OrdinalIgnoreCase));
            uint ver = 0;
            if (verIdx >= 0) uint.TryParse(lines[verIdx].Substring(8).Trim(), out ver);
            uint machine = (ver & 0xFFFF) + 1;
            if (machine > 0xFFFF) machine = 1;
            uint next = (ver & 0xFFFF0000) | machine;
            if (verIdx >= 0) lines[verIdx] = "Version=" + next; else lines.Add("Version=" + next);

            int extIdx = lines.FindIndex(l => l.StartsWith("gPCMachineExtensionNames=", StringComparison.OrdinalIgnoreCase));
            if (extIdx < 0) lines.Insert(1, "gPCMachineExtensionNames=" + RegistryExt);
            else if (lines[extIdx].IndexOf("35378EAC-683F-11D2-A89A-00C04FBBCFA2", StringComparison.OrdinalIgnoreCase) < 0)
                lines[extIdx] = "gPCMachineExtensionNames=" + RegistryExt + lines[extIdx].Substring("gPCMachineExtensionNames=".Length);

            return string.Join("\r\n", lines.ToArray()) + "\r\n";
        }
    }

    static class LocalGpo
    {
        static string Dir { get { return Path.Combine(Environment.SystemDirectory, "GroupPolicy"); } }
        public static string PolPath { get { return Path.Combine(Dir, @"Machine\Registry.pol"); } }
        static string IniPath { get { return Path.Combine(Dir, "gpt.ini"); } }

        static List<PolEntry> Load()
        {
            return File.Exists(PolPath) ? PolFile.Parse(File.ReadAllBytes(PolPath)) : new List<PolEntry>();
        }

        // 로컬 그룹 정책에 저장된 값 (없으면 null). key는 HKLM 아래 경로 (예: SOFTWARE\Policies\Microsoft\Windows Defender\Scan)
        public static int? GetDword(string key, string value)
        {
            try { return PolFile.GetDword(Load(), key, value); } catch { return null; }
        }

        public static object GetValue(string key, string value)
        {
            try { return PolFile.GetValue(Load(), key, value); } catch { return null; }
        }

        // 로컬 그룹 정책 값을 쓰거나(null이면 '구성되지 않음'으로) 지움
        public static void SetDword(string key, string value, int? data)
        {
            SetValue(key, value, data.HasValue ? (object)data.Value : null);
        }

        // data: int / string / null. Registry.pol과 레지스트리를 함께 맞춤
        public static void SetValue(string key, string value, object data)
        {
            List<PolEntry> entries = Load();
            if (PolFile.SetValue(entries, key, value, data))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PolPath));
                if (File.Exists(PolPath)) File.Copy(PolPath, PolPath + ".DefenderTray.bak", true);   // 직전 상태 백업
                File.WriteAllBytes(PolPath, PolFile.Build(entries));

                string ini = File.Exists(IniPath) ? File.ReadAllText(IniPath) : "";
                File.WriteAllText(IniPath, GptIni.Bump(ini), Encoding.ASCII);
            }

            // 레지스트리에도 바로 반영 (그룹 정책 갱신을 기다리지 않음)
            if (data == null)
            {
                using (var k = Registry.LocalMachine.OpenSubKey(key, true))
                    if (k != null) k.DeleteValue(value, false);
            }
            else
            {
                using (var k = Registry.LocalMachine.CreateSubKey(key))
                {
                    if (data is string) k.SetValue(value, (string)data, RegistryValueKind.String);
                    else k.SetValue(value, Convert.ToInt32(data), RegistryValueKind.DWord);
                }
            }
        }
    }
}
