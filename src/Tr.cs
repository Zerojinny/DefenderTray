using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DefenderTray
{
    // 화면 언어 (한국어 / 영어).
    //   설정 HKCU\Software\DefenderTray\Language = "auto"(기본) / "ko" / "en"
    //   auto: 시스템 로케일이나 표시 언어가 한국어면 한국어, 아니면 영어
    // 문구는 Tr.T("한국어", "English") 형태로 코드에 함께 적는다 (번역 누락을 코드에서 바로 볼 수 있게).
    // 프로그램 시작 시 Program.Main에서 Init()을 가장 먼저 부른다. 바꾸면 앱을 다시 시작해야 반영된다.
    static class Tr
    {
        public const string Auto = "auto", Korean = "ko", English = "en";
        const string Key = "Language";

        // 테스트에서 직접 정할 수 있게 공개. 기본은 한국어 (Init 전에 쓰이는 경우 대비)
        public static bool Ko = true;

        public static string T(string ko, string en) { return Ko ? ko : EnglishMnemonic(en); }

        // 한국어식 단축키 표기 "Add program(&P)..." → 영어식 "Add &program..." (그 글자가 낱말에 있을 때만)
        public static string EnglishMnemonic(string s)
        {
            if (s == null) return s;
            int i = s.IndexOf("(&", StringComparison.Ordinal);
            if (i < 0 || i + 3 >= s.Length || s[i + 3] != ')') return s;
            char key = s[i + 2];
            string head = s.Substring(0, i), tail = s.Substring(i + 4);
            int pos = head.IndexOf(char.ToUpperInvariant(key));
            if (pos < 0) pos = head.IndexOf(char.ToLowerInvariant(key));
            if (pos < 0) return s;   // 낱말에 없는 글자면 원래 표기 유지
            return head.Substring(0, pos) + "&" + head.Substring(pos) + tail;
        }

        public static string F(string ko, string en, params object[] args) { return string.Format(T(ko, en), args); }

        // 저장된 설정: auto / ko / en
        public static string Setting
        {
            get
            {
                string s = AppSettings.Get(Key);
                return s == Korean || s == English ? s : Auto;
            }
            set { AppSettings.Set(Key, value == Korean || value == English ? value : Auto); }
        }

        public static void Init()
        {
            Ko = Resolve(Setting, SystemIsKorean());
        }

        // 설정과 시스템 언어로 실제 언어를 정함 (시험 대상)
        public static bool Resolve(string setting, bool systemKorean)
        {
            if (setting == Korean) return true;
            if (setting == English) return false;
            return systemKorean;
        }

        [DllImport("kernel32.dll")] static extern ushort GetSystemDefaultLangID();
        [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();

        // 시스템 로케일(유니코드가 아닌 프로그램용 언어) 또는 Windows 표시 언어가 한국어인지
        public static bool SystemIsKorean()
        {
            try
            {
                const int LANG_KOREAN = 0x12;
                if ((GetSystemDefaultLangID() & 0x3FF) == LANG_KOREAN) return true;
                if ((GetUserDefaultUILanguage() & 0x3FF) == LANG_KOREAN) return true;
            }
            catch { }
            return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ko";
        }
    }
}
