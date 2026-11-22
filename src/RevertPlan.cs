using System;
using System.Collections.Generic;

namespace DefenderTray
{
    // 외부 변경 되돌리기에서 "무엇을 되돌릴지"만 정하는 순수 함수 (실제 변경은 TrayContext.Watch.cs)
    static class RevertPlan
    {
        public static string Key(DefenderChange c)
        {
            return c.Kind + "|" + (c.Value ?? "").TrimEnd('\\');
        }

        // 항목마다 "첫 외부 변경 이전 상태"로 되돌릴 대상을 고름.
        // 추가 후 삭제처럼 여러 번 바뀌어도 첫 변경만 보면 원래 상태를 알 수 있음
        // (첫 변경이 추가였다면 원래는 없었음 → 삭제, 삭제였다면 원래 있었음 → 추가).
        // 그룹 정책 변경은 정책이 다시 적용되므로 되돌리지 않음.
        public static List<DefenderChange> Exclusions(IEnumerable<DefenderChange> external, bool revertAdd, bool revertRemove)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<DefenderChange>();
            foreach (DefenderChange c in external)
            {
                if (!c.IsExclusion || c.Policy || c.Kind == null) continue;
                if (!seen.Add(Key(c))) continue;
                if (c.Added ? revertAdd : revertRemove) result.Add(c);
            }
            return result;
        }

        // 실시간 보호에 대한 마지막 외부 변경이 "꺼짐"인지
        public static bool LastRtpWasOff(IEnumerable<DefenderChange> external)
        {
            DefenderChange last = null;
            foreach (DefenderChange c in external) if (c.IsRtpOn || c.IsRtpOff) last = c;
            return last != null && last.IsRtpOff;
        }
    }
}
