using System;
using System.Collections.Generic;

namespace DefenderTray
{
    // 시간 지정 끄기: "30분 뒤 다시 켜기" 같은 예약을 저장·판단.
    // 예약 시각은 UTC ticks로 HKCU\Software\DefenderTray\TimedOffUntil 에 저장 → 재부팅·재실행 후에도 유지
    static class TimedOff
    {
        const string Key = "TimedOffUntil";
        const string LastKey = "LastOffMinutes";

        // 메뉴에 보여 줄 끄기 기간(분). 0 = 직접 켤 때까지
        public static readonly int[] Choices = { 30, 60, 120, 240, 0 };

        public static DateTime? GetUntilUtc()
        {
            long ticks;
            string s = AppSettings.Get(Key);
            if (string.IsNullOrEmpty(s) || !long.TryParse(s, out ticks) || ticks <= 0) return null;
            try { return new DateTime(ticks, DateTimeKind.Utc); } catch { return null; }
        }

        public static void SetUntilUtc(DateTime? untilUtc)
        {
            AppSettings.Set(Key, untilUtc.HasValue ? untilUtc.Value.Ticks.ToString() : "");
        }

        // 지금 적용 중인 끄기 기간 (메뉴로 실제로 껐을 때만 기록, 켜지면 지움). null = 이 앱의 기간 선택으로 꺼진 상태 아님
        public static int? ActiveMinutes
        {
            get
            {
                int m;
                string s = AppSettings.Get("ActiveOffMinutes");
                return s != null && int.TryParse(s, out m) ? (int?)m : null;
            }
            set { AppSettings.Set("ActiveOffMinutes", value.HasValue ? value.Value.ToString() : ""); }
        }

        // 메뉴 항목 체크 여부: 꺼져 있고, 그 기간을 실제로 골라 끈 경우에만
        public static bool IsChecked(int choiceMinutes, bool rtpOff, int? activeMinutes)
        {
            return rtpOff && activeMinutes.HasValue && activeMinutes.Value == choiceMinutes;
        }

        // 더블클릭으로 끌 때 쓰는 기간 (마지막으로 메뉴에서 고른 값, 기본 30분)
        public static int LastMinutes
        {
            get
            {
                int m;
                string s = AppSettings.Get(LastKey);
                return s != null && int.TryParse(s, out m) && m >= 0 ? m : 30;
            }
            set { AppSettings.Set(LastKey, value.ToString()); }
        }

        public static bool IsDue(DateTime? untilUtc, DateTime nowUtc)
        {
            return untilUtc.HasValue && nowUtc >= untilUtc.Value;
        }

        public static string DurationLabel(int minutes)
        {
            if (minutes <= 0) return Tr.T("직접 켤 때까지", "Until turned on");
            if (minutes % 60 == 0) return (minutes / 60) + Tr.T("시간", " h");
            if (minutes > 60) return (minutes / 60) + Tr.T("시간 ", " h ") + (minutes % 60) + Tr.T("분", " min");
            return minutes + Tr.T("분", " min");
        }

        // 다시 켜질 시각 표시: 오늘이면 "14:30", 아니면 "11-21 01:30"
        public static string WhenLabel(DateTime untilLocal, DateTime nowLocal)
        {
            return untilLocal.Date == nowLocal.Date
                ? untilLocal.ToString("HH:mm")
                : untilLocal.ToString("MM-dd HH:mm");
        }

        // 남은 시간 표시: "1시간 5분 남음", "3분 남음", "1분 안에"
        public static string RemainingLabel(DateTime untilUtc, DateTime nowUtc)
        {
            TimeSpan left = untilUtc - nowUtc;
            if (left.TotalMinutes < 1) return Tr.T("1분 안에", "within 1 min");
            int mins = (int)Math.Ceiling(left.TotalMinutes);
            if (mins < 60) return mins + Tr.T("분 남음", " min left");
            return (mins / 60) + Tr.T("시간", " h") + (mins % 60 > 0 ? " " + (mins % 60) + Tr.T("분", " min") : "") + Tr.T(" 남음", " left");
        }
    }
}
