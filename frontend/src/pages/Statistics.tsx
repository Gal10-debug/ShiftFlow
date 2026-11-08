import { useEffect, useState } from "react";
import { api } from "../api";
import { message } from "../he";
import { monthLabel } from "../availability";
import { hours } from "../personalSchedule";
import type { AdminAnalytics, AnalyticsMonth, AnalyticsTotals, PersonalAnalytics } from "../analytics";
import { Notice } from "../components/forms";

type TeamOption = { id: string; name: string };
const number = (n: number) => n.toLocaleString("he-IL", { maximumFractionDigits: 2 });
function Summary({ totals: t }: { totals: AnalyticsTotals }) {
  return <section className="panel" aria-label="סיכום סטטיסטיקות"><h2>סיכום התקופה</h2>
    <dl className="planned-hours-summary">
      <div><dt>סה״כ משמרות</dt><dd>{t.totalAssignedShifts}</dd></div>
      <div><dt>שעות מתוכננות ידועות</dt><dd>{hours(t.knownPlannedMinutes)}</dd></div>
      <div><dt>משמרות יום</dt><dd>{t.dayShiftCount}</dd></div>
      <div><dt>משמרות לילה</dt><dd>{t.nightShiftCount}</dd></div>
      <div><dt>שיבוצי סוף שבוע</dt><dd>{t.weekendShiftCount}</dd></div>
      <div><dt>שיבוצים בתפקידים מיוחדים</dt><dd>{t.specialDutyAssignments}</dd></div>
    </dl>
    {!t.isComplete && <p className="notice" role="status">זהו סכום חלקי של שעות ידועות בלבד. משך העבודה אינו ידוע עבור {t.unknownDurationAssignments} שיבוצים.</p>}
    <figure className="analytics-bars"><figcaption>התפלגות יום ולילה</figcaption>
      <label>יום <meter min={0} max={Math.max(1, t.dayShiftCount + t.nightShiftCount)} value={t.dayShiftCount} /> {t.dayShiftCount}</label>
      <label>לילה <meter min={0} max={Math.max(1, t.dayShiftCount + t.nightShiftCount)} value={t.nightShiftCount} /> {t.nightShiftCount}</label>
    </figure>
    <p>סוף שבוע: שישי ושבת לפי תאריך השיבוץ. שיבוצי כוננות וזמינות אינם נכללים ביום/לילה; לא הומצאו עבורם שעות עבודה.</p>
  </section>;
}
function Trends({ months }: { months: AnalyticsMonth[] }) {
  return <div className="table-scroll"><table><caption>מגמה חודשית וגרסאות פרסום</caption>
    <thead><tr><th scope="col">חודש</th><th scope="col">גרסה</th><th scope="col">משמרות</th><th scope="col">שעות מתוכננות ידועות</th><th scope="col">ללא משך ידוע</th></tr></thead>
    <tbody>{months.map(m => <tr key={m.month}><th scope="row">{monthLabel(m.year, m.month)}</th><td>{m.publication ? `גרסה ${m.publication.versionNumber} · ${m.publication.publicationType === "FINAL" ? "סופי" : "ראשוני"}` : "אין פרסום זמין"}</td>
      <td>{m.publication ? <><meter aria-label={`משמרות ${monthLabel(m.year, m.month)}`} min={0} max={Math.max(1, ...months.map(x => x.totals.totalAssignedShifts))} value={m.totals.totalAssignedShifts} /> {m.totals.totalAssignedShifts}</> : "—"}</td>
      <td>{m.publication ? <><meter aria-label={`שעות מתוכננות ${monthLabel(m.year, m.month)}`} min={0} max={Math.max(1, ...months.map(x => x.totals.knownPlannedMinutes))} value={m.totals.knownPlannedMinutes} /> {hours(m.totals.knownPlannedMinutes)}{!m.totals.isComplete && " · חלקי"}</> : "—"}</td>
      <td>{m.publication ? m.totals.unknownDurationAssignments : "—"}</td></tr>)}</tbody>
  </table></div>;
}
function Breakdown({ totals }: { totals: AnalyticsTotals }) {
  return <div className="table-scroll"><table><caption>פירוט לפי סוג משמרת</caption><thead><tr><th scope="col">סוג משמרת</th><th scope="col">שיבוצים</th><th scope="col">שעות מתוכננות ידועות</th><th scope="col">ללא משך ידוע</th></tr></thead>
    <tbody>{totals.byShiftType.map(t => <tr key={t.key}><th scope="row">{t.name}</th><td>{t.assignedShifts}</td><td>{hours(t.knownPlannedMinutes)}</td><td>{t.unknownDurationAssignments}</td></tr>)}</tbody></table></div>;
}
function Workforce({ data: d }: { data: AdminAnalytics }) {
  return <>
    <section className="panel" aria-label="התפלגות עומס"><h2>התפלגות עומס השיבוץ</h2><dl className="planned-hours-summary">
      <div><dt>עובדים משובצים</dt><dd>{d.distribution.scheduledEmployees}</dd></div>
      <div><dt>עובדים בהשוואה</dt><dd>{d.distribution.includedEmployees}</dd></div>
      <div><dt>מינימום משמרות</dt><dd>{d.distribution.minimumAssignedShifts}</dd></div>
      <div><dt>מקסימום משמרות</dt><dd>{d.distribution.maximumAssignedShifts}</dd></div>
      <div><dt>ממוצע לכל עובד בהשוואה</dt><dd>{number(d.distribution.averageAssignedShifts)}</dd></div>
      <div><dt>ממוצע לעובד משובץ</dt><dd>{number(d.distribution.averageShiftsPerScheduledEmployee)}</dd></div>
    </dl><p>מדדים תיאוריים בלבד. זמינות, הכשרה וזכאות לשיבוץ משפיעות על החלוקה. ההשוואה כוללת עובדים זכאים כיום גם ללא שיבוץ, ועובדים המופיעים בפרסום.</p>
    <ul>{d.distribution.shiftCounts.map(c => <li key={c.assignedShifts}>{c.assignedShifts} משמרות: {c.employees} עובדים</li>)}</ul></section>
    <section className="panel" aria-label="כיסוי תקנים"><h2>כיסוי תקנים</h2>{d.coverage ? <>
      <dl className="planned-hours-summary"><div><dt>תקנים נדרשים</dt><dd>{d.coverage.requiredPositions}</dd></div><div><dt>תקנים מאוישים</dt><dd>{d.coverage.filledPositions}</dd></div><div><dt>תקנים חסרים</dt><dd>{d.coverage.unfilledPositions}</dd></div><div><dt>אחוז כיסוי</dt><dd>{d.coverage.coveragePercentage === null ? "אין תקנים נדרשים" : `${number(d.coverage.coveragePercentage)}%`}</dd></div></dl>
      <div className="table-scroll"><table><caption>חוסרים לפי סוג משמרת</caption><thead><tr><th scope="col">סוג משמרת</th><th scope="col">נדרש</th><th scope="col">מאויש</th><th scope="col">חסר</th></tr></thead><tbody>{d.coverage.byShiftType.filter(t => t.requiredPositions > 0).map(t => <tr key={t.key}><th scope="row">{t.name}</th><td>{t.requiredPositions}</td><td>{t.filledPositions}</td><td>{t.unfilledPositions}</td></tr>)}</tbody></table></div>
      <p>הכיסוי מתייחס לתקנים ישימים שאינם בשורות ידניות, מתוך פרסומים זמינים בלבד. שיבוצים מעבר לתקן אינם מגדילים את הכיסוי.</p>
    </> : <p>כיסוי תקנים מוצג בתצוגת הארגון בלבד, כשקיים פרסום זמין. אין שיוך תקנים לצוותים במודל הנוכחי.</p>}</section>
    <p>שיוך לצוות מבוסס על החברות הנוכחית; הוא אינו משחזר חברות היסטורית.</p>
    <div className="table-scroll"><table><caption>סיכום עומס לפי צוות</caption><thead><tr><th scope="col">צוות</th><th scope="col">עובדים משובצים</th><th scope="col">משמרות</th><th scope="col">שעות מתוכננות ידועות</th><th scope="col">ללא משך ידוע</th></tr></thead><tbody>{d.teams.map(t => <tr key={t.teamId ?? "none"}><th scope="row">{t.name}</th><td>{t.scheduledEmployees}</td><td>{t.totals.totalAssignedShifts}</td><td>{hours(t.totals.knownPlannedMinutes)}</td><td>{t.totals.unknownDurationAssignments}</td></tr>)}</tbody></table></div>
    <div className="table-scroll"><table><caption>השוואת עומס עובדים</caption><thead><tr><th scope="col">עובד</th><th scope="col">משמרות</th><th scope="col">יום</th><th scope="col">לילה</th><th scope="col">סוף שבוע</th><th scope="col">שעות מתוכננות ידועות</th><th scope="col">ללא משך ידוע</th></tr></thead><tbody>{d.employees.map(e => <tr key={e.employeeId}><th scope="row">{e.name}</th><td>{e.totals.totalAssignedShifts}</td><td>{e.totals.dayShiftCount}</td><td>{e.totals.nightShiftCount}</td><td>{e.totals.weekendShiftCount}</td><td>{hours(e.totals.knownPlannedMinutes)}</td><td>{e.totals.unknownDurationAssignments}</td></tr>)}</tbody></table></div>
  </>;
}
export default function Statistics({ admin = false }: { admin?: boolean }) {
  const initial = new URLSearchParams(window.location.search).get("period");
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Jerusalem", year: "numeric", month: "2-digit" }).formatToParts(new Date());
  const [year, setYear] = useState(initial?.slice(0, 4) ?? today.find(p => p.type === "year")!.value);
  const [month, setMonth] = useState(Number(initial?.slice(5, 7) ?? today.find(p => p.type === "month")!.value));
  const [annual, setAnnual] = useState(false);
  const [team, setTeam] = useState("");
  const [employee, setEmployee] = useState("");
  const [version, setVersion] = useState("");
  const [teams, setTeams] = useState<TeamOption[]>([]);
  const [teamError, setTeamError] = useState("");
  const [employeeOptions, setEmployeeOptions] = useState<AdminAnalytics["employees"]>([]);
  const [result, setResult] = useState<{ path: string; data: PersonalAnalytics | AdminAnalytics } | null>(null);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const valid = /^\d{1,4}$/.test(year) && Number(year) >= 2 && Number(year) <= 9998 && month >= 1 && month <= 12;
  const params = new URLSearchParams();
  if (team) params.set("teamId", team);
  if (employee) params.set("employeeId", employee);
  if (version && !annual) params.set("version", version);
  const path = `/${admin ? "admin/statistics" : "my-statistics"}/${Number(year)}${annual ? "" : `/${month}`}${params.size ? `?${params}` : ""}`;
  useEffect(() => {
    if (!admin) return;
    let alive = true; setTeamError("");
    api<TeamOption[]>("/admin/teams").then(t => { if (alive) setTeams(t); }).catch(e => { if (alive) setTeamError(message(e)); });
    return () => { alive = false; };
  }, [admin, retry]);
  useEffect(() => {
    setResult(null); setError("");
    if (!valid) return;
    let alive = true, sequence = 0;
    const load = async () => {
      const ticket = ++sequence;
      try {
        const data = await api<PersonalAnalytics | AdminAnalytics>(path);
        if (alive && ticket === sequence) {
          setResult({ path, data }); setError("");
          if ("employees" in data && !employee) setEmployeeOptions(data.employees);
        }
      } catch (e) { if (alive && ticket === sequence) { setResult(null); setError(message(e)); } }
    };
    void load();
    const refresh = () => { sequence++; setResult(null); if (!document.hidden) void load(); };
    const timer = window.setInterval(() => { if (!document.hidden) void load(); }, 5000);
    window.addEventListener("focus", refresh); window.addEventListener("offline", refresh); document.addEventListener("visibilitychange", refresh);
    return () => { alive = false; window.clearInterval(timer); window.removeEventListener("focus", refresh); window.removeEventListener("offline", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [path, valid, retry, employee]);
  const data = result?.path === path ? result.data : null;
  const published = data?.months.filter(m => m.publication).length ?? 0;
  return <section dir="rtl"><h1>{admin ? "סטטיסטיקות שיבוץ" : "סטטיסטיקות אישיות"}</h1>
    <div className="analytics-controls panel">
      <label className="field">תקופת דיווח<select value={annual ? "year" : "month"} onChange={e => { setAnnual(e.target.value === "year"); setVersion(""); }}><option value="month">חודשי</option><option value="year">שנתי</option></select></label>
      <label className="field">שנה<input type="number" min={2} max={9998} value={year} onChange={e => { setYear(e.target.value); setVersion(""); }} /></label>
      {!annual && <label className="field">חודש<select value={month} onChange={e => { setMonth(Number(e.target.value)); setVersion(""); }}>{Array.from({ length: 12 }, (_, i) => <option key={i + 1} value={i + 1}>{monthLabel(2026, i + 1).replace("2026", "").trim()}</option>)}</select></label>}
      {!annual && <label className="field">גרסה היסטורית (רשות)<input type="number" min={1} step={1} value={version} placeholder="הגרסה האחרונה" onChange={e => setVersion(e.target.value)} /></label>}
      {admin && <><label className="field">צוות<select value={team} onChange={e => { setTeam(e.target.value); setEmployee(""); setEmployeeOptions([]); }}><option value="">כל הארגון</option>{teams.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
        <label className="field">עובד<select value={employee} onChange={e => setEmployee(e.target.value)}><option value="">כל העובדים</option>{employeeOptions.map(e => <option key={e.employeeId} value={e.employeeId}>{e.name}</option>)}</select></label></>}
    </div>
    <Notice error={error || teamError} />{(error || teamError) && <button onClick={() => setRetry(n => n + 1)}>ניסיון נוסף</button>}
    {!valid && <p role="alert">יש לבחור שנה בין 2 ל־9998 וחודש תקין.</p>}
    {valid && !data && !error && <p role="status">טוען סטטיסטיקות…</p>}
    {data && <>
      <p>{version && !annual ? "מוצגת גרסה היסטורית שנבחרה." : "מוצגת הגרסה המפורסמת האחרונה הזמינה בכל חודש."} חודשי פרסום זמינים: {published} מתוך {data.months.length}. חודשים ללא פרסום אינם נתוני אפס מאומתים.</p>
      {!published ? <p>אין פרסומים זמינים לתקופה שנבחרה.</p> : <><Summary totals={data.totals} />
        {data.totals.totalAssignedShifts === 0 && <p>אין שיבוצים בפרסומים הזמינים עבור הבחירה הנוכחית.</p>}
        {"comparison" in data && data.comparison && <section className="panel" aria-label="השוואה לחודש הקודם"><h2>השוואה לחודש הקודם</h2>
          {data.comparison.shiftCountChange === null ? <p>אין פרסום זמין להשוואה בחודש הקודם.</p> : <><p>שינוי במספר המשמרות: {number(data.comparison.shiftCountChange)} · שינוי בשעות המתוכננות הידועות: {hours(data.comparison.knownPlannedMinutesChange!)}</p>
            {!data.comparison.bothDurationsComplete && <p>ההשוואה מתייחסת לשעות ידועות בלבד; לפחות אחת התקופות כוללת משך לא ידוע.</p>}</>}
        </section>}
        <Breakdown totals={data.totals} />
      </>}
      <Trends months={data.months} />
      {"employees" in data && <Workforce data={data} />}
      <p>אלו שעות מתוכננות בלבד. כל המשמרת מיוחסת לתאריך השיבוץ לפי שעון ישראל, גם בחציית חצות. שיבוצי שישי ושבת בכוננות נספרים בנפרד. הנתונים אינם דיווח נוכחות או שכר.</p>
    </>}
  </section>;
}
