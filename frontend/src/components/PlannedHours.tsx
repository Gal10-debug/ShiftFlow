import { hours, type PersonalSchedule } from "../personalSchedule";

export default function PlannedHours({ schedule }: { schedule: PersonalSchedule }) {
  const s = schedule.summary;
  return <section className="panel" aria-label="סיכום חודשי מתוכנן">
    <h2>העומס החודשי המתוכנן שלי</h2>
    <dl className="planned-hours-summary">
      <div><dt>סה״כ משמרות</dt><dd>{s.totalAssignedShifts}</dd></div>
      <div><dt>שעות עבודה מתוכננות</dt><dd>{s.isComplete && s.totalPlannedMinutes !== null ? hours(s.totalPlannedMinutes) : "הסכום המלא אינו זמין"}</dd></div>
      <div><dt>משמרות יום</dt><dd>{s.dayShiftCount}</dd></div>
      <div><dt>משמרות לילה</dt><dd>{s.nightShiftCount}</dd></div>
    </dl>
    {!s.isComplete && <p className="notice" role="status">שעות ידועות בלבד: {hours(s.knownPlannedMinutes)}. משך העבודה אינו ידוע עבור {s.unknownDurationAssignments} שיבוצים. זהו סכום חלקי.</p>}
    {s.onCallDutyCount > 0 && <p>כוננויות סוף שבוע המיוצגות בחודש: {s.onCallDutyCount}. ימי שישי ושבת מוצגים כשיבוצים נפרדים; לא נוספו עבורם שעות עבודה.</p>}
    <p>כל המועדים לפי שעון ישראל. משמרת החוצה חצות נספרת במלואה בחודש תאריך השיבוץ. אלו שעות מתוכננות בלבד.</p>
    {s.byShiftType.length > 0 && <details><summary>פירוט לפי סוג משמרת</summary><ul>{s.byShiftType.map(t => <li key={t.key}>
      {t.name}: {t.assignedShifts} שיבוצים · {hours(t.knownPlannedMinutes)} שעות {t.isComplete ? "מתוכננות" : `ידועות בלבד · ${t.unknownDurationAssignments} שיבוצים ללא משך ידוע`}
    </li>)}</ul></details>}
  </section>;
}
