import type { Snapshot } from "../publication";
import { displayBoardDate, scheduling as s, timeRanges, type ShiftTimeRange } from "../scheduling";
import { weekStart } from "../availability";
import type { SelectedCell } from "./ScheduleReviewPanel";

// One stable palette by immutable definition identity, shared by drafts and frozen snapshots.
export function rowColor(id: string) {
  let hash = 0;
  for (const c of id) hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
  return `hsl(${hash * 137.508 % 360} 48% 93%)`;
}
export default function RosterGrid({ snapshot, currentUserId, week, onEdit }: {
  snapshot: Snapshot;
  currentUserId?: string;
  week?: string;
  onEdit?: (cell: SelectedCell) => void;
}) {
  const cells = snapshot.cells.filter(c => !week || weekStart(c.date) === week);
  const days = [...new Set(cells.map(c => c.date))].sort();
  const rows = [...new Map(cells.map(c => [c.definitionId, c])).values()].sort((a, b) => a.displayOrder - b.displayOrder);
  const indexed = new Map(cells.map(c => [c.definitionId + "/" + c.date, c]));
  return <><div className="schedule-scroll" tabIndex={0} role="region" aria-label={s.grid} dir="rtl">
    <table className="schedule-grid"><caption className="sr-only">{s.grid}</caption>
      <thead><tr><th scope="col" className="schedule-row-label">סוג משמרת</th>{days.map(date => <th key={date} scope="col"><span className="schedule-weekday">{s.weekdays[new Date(date + "T12:00:00Z").getUTCDay()]}</span><time dateTime={date}>{displayBoardDate(date)}</time></th>)}</tr></thead>
      <tbody>{rows.map(row => <tr key={row.definitionId} style={{ backgroundColor: rowColor(row.definitionId) }}>
        <th scope="row" className="schedule-row-label" style={{ backgroundColor: rowColor(row.definitionId) }}><strong>{row.name}</strong><small>{timeRanges[row.timeRange as ShiftTimeRange] ?? row.timeRange}</small>
          {row.includeInAutomaticScheduling !== undefined && <small>{row.configuredRequiredEmployees} עובדים · {row.includeInAutomaticScheduling && ((row.configuredRequiredEmployees ?? 0) > 0 || (row.weekendRequiredEmployees ?? 0) > 0) ? s.selected : s.excluded}</small>}
          {row.semantic === "SIX_NINE" && <small>עד פעם אחת בשבוע</small>}
          {row.semantic === "WEEKEND_ON_CALL" && <small>זוג שישי–שבת בלבד · הקצאה אחת</small>}
          {row.requiresShiftLeadQualification && <small>אחראי משמרת · מוסמכים בלבד</small>}</th>
        {days.map(date => {
          const cell = indexed.get(row.definitionId + "/" + date);
          if (!cell) return <td key={date} />;
          const enabled = cell.applicable ?? cell.state !== "NOT_APPLICABLE";
          const shortage = Math.max(0, cell.requiredEmployees - cell.employees.length);
          const select = (assignmentId?: string) => onEdit?.({ definitionId: cell.definitionId, date, assignmentId });
          return <td key={date} aria-disabled={!enabled} className={!enabled ? "schedule-disabled" : shortage ? "staffing-shortage" : undefined}>
            {cell.state === "PREVIEW" && <small>תצוגה מקדימה</small>}
            {cell.state === "SAVED_DRAFT" && <small>טיוטה שמורה</small>}
            {!enabled && <small>לא חל ביום זה</small>}
            {onEdit && enabled && <button className="cell-edit" aria-label={`עריכת ${cell.name} ${date}`} onClick={() => select()}>הוספת עובד / עריכה</button>}
            {cell.employees.map(employee => <div key={employee.assignmentId || employee.userId} className={`roster-employee${employee.userId === currentUserId ? " own-assignment" : ""}`}>
              {onEdit && enabled && employee.assignmentId ? <button className="employee-edit" onClick={() => select(employee.assignmentId)}>{employee.name}{employee.isShiftLead ? " · אחראי משמרת" : ""}</button> : <strong>{employee.name}{employee.isShiftLead ? " · אחראי משמרת" : ""}</strong>}
              {employee.userId === currentUserId && <small>★ השיבוץ שלי</small>}

              {employee.note && <p className="assignment-note">{employee.note}</p>}
            </div>)}
            {(enabled || cell.employees.length > 0) && <small>נדרשים: {cell.requiredEmployees} · שובצו: {cell.employees.length}</small>}
            {enabled && shortage > 0 && <strong>{shortage === 1 ? "חסר עובד 1" : `חסרים ${shortage} עובדים`}</strong>}
            {enabled && !cell.employees.length && <span className={shortage ? "schedule-unassigned" : "schedule-not-required"}>{shortage ? s.unassigned : s.noStaffing}</span>}
          </td>;
        })}</tr>)}</tbody>
    </table>
  </div>
    {snapshot.legacyAssignments.length > 0 && <section className="panel" dir="rtl" aria-label="שיבוצים קודמים ללא סוג משמרת">
      <h3>שיבוצים קודמים ללא סוג משמרת</h3>{snapshot.legacyAssignments.filter(a => !week || weekStart(a.date) === week).map(a =>
        <article key={a.assignmentId} className={`roster-employee${a.userId === currentUserId ? " own-assignment" : ""}`}>
          <strong>{a.name}</strong> · {a.date} · {a.period === "NIGHT" ? "לילה" : "יום"}
          {a.userId === currentUserId && <small>★ השיבוץ שלי</small>}{a.note && <p className="assignment-note">{a.note}</p>}
        </article>)}
    </section>}
  </>;
}
