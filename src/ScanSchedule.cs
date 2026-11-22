using System;

namespace DefenderTray
{
    // 예약 검사 켜기/끄기와 요일 값 변환.
    // Defender의 ScanScheduleDay: 0 = 매일, 1~7 = 일~토요일, 8 = 안 함.
    // 끌 때는 8로 저장하고, 다시 켤 때 쓰도록 마지막 요일을 HKCU\Software\DefenderTray\LastScanDay 에 기억
    static class ScanSchedule
    {
        public const int Never = 8;
        const string LastKey = "LastScanDay";

        public static bool IsEnabled(int setting) { return setting >= 0 && setting <= 7; }

        // 화면에 보여 줄 요일 (0~7). 꺼져 있으면 기억해 둔 요일, 그것도 없으면 매일
        public static int DayIndexFor(int setting, int remembered)
        {
            if (IsEnabled(setting)) return setting;
            return IsEnabled(remembered) ? remembered : 0;
        }

        // 저장할 값
        public static int ToSetting(bool enabled, int dayIndex)
        {
            return enabled ? (IsEnabled(dayIndex) ? dayIndex : 0) : Never;
        }

        public static int RememberedDay
        {
            get { int d; string s = AppSettings.Get(LastKey); return s != null && int.TryParse(s, out d) ? d : -1; }
            set { AppSettings.Set(LastKey, value.ToString()); }
        }
    }
}
