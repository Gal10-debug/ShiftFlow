import { useEffect, useState } from "react";
import { api } from "../api";
import RosterGrid from "../components/RosterGrid";
import { useAuth } from "../auth";
import { message } from "../he";
import PlannedHours from "../components/PlannedHours";
import { categories, hours, shiftDate, shiftTime } from "../personalSchedule";
import { monthLabel, instant, weekStart } from "../availability";
import { issueStates, type Publication, type Issue } from "../publication";
import { Notice } from "../components/forms";
export default function MySchedule() {
  const { user } = useAuth();
  const [periods, setPeriods] = useState<{
    year: number;
    month: number;
  }[]>([]);
  const [selected, setSelected] = useState("");
  const [version, setVersion] = useState("");
  const [versions, setVersions] = useState<number[]>([]);
  const [latestVersion, setLatestVersion] = useState(0);
  const [publication, setPublication] = useState<Publication | null>(null);
  const [issues, setIssues] = useState<Issue[]>([]);
  const [week, setWeek] = useState("");
  const [reporting, setReporting] = useState("");
  const [explanation, setExplanation] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const [periodsLoading, setPeriodsLoading] = useState(true);
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let alive = true;
    setPeriodsLoading(true);
    setError("");
    api<{
      year: number;
      month: number;
    }[]>("/my-schedule/periods").then(p => {
      if (alive) {
        setPeriods(p);
        const period = new URLSearchParams(window.location.search).get("period");
        const requested = p.find(x => `${x.year}-${String(x.month).padStart(2, "0")}` === period);
        if (requested) setSelected(`${requested.year}/${requested.month}`);
        else if (p.length) setSelected(`${p[0].year}/${p[0].month}`);
      }
    }).catch(e => {
      if (alive) setError(message(e));
    }).finally(() => { if (alive) setPeriodsLoading(false); });
    return () => {
      alive = false;
    };
  }, [retry]);
  useEffect(() => {
    if (!selected) return;
    let alive = true;
    setPublication(null);
    setError("");
    setSuccess("");
    setWeek("");
    setReporting("");
    setLoading(true);
    setVersions([]);
    let lastPublicationId = "";
    let sequence = 0;
    const load = () => { const ticket = ++sequence; return Promise.all([api<Publication>(`/my-schedule/${selected}${version ? `?version=${version}` : ""}`), api<Issue[]>(`/my-schedule/${selected}/issues`)]).then(([p, i]) => {
      if (alive && ticket === sequence) {
        setPublication(p);
        setError("");
        if (lastPublicationId !== p.id) {
          lastPublicationId = p.id;
          void api<number[]>(`/my-schedule/${selected}/versions`).then(v => { if (alive && ticket === sequence) setVersions(Array.isArray(v) ? v : []); }).catch(() => { if (alive && ticket === sequence) setVersions([]); });
        }
        if (!version) setLatestVersion(p.versionNumber);
        setIssues(i);
      }
    }).catch(e => {
      if (alive && ticket === sequence) { lastPublicationId = ""; setPublication(null); setIssues([]); setVersions([]); setReporting(""); setError(message(e)); }
    }).finally(() => { if (alive && ticket === sequence) setLoading(false); }); };
    void load();
    const refresh = () => { sequence++; setPublication(null); setIssues([]); setReporting(""); if (!document.hidden) void load(); };
    const timer = window.setInterval(() => { if (!document.hidden) void load(); }, 5000);
    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", refresh);
    window.addEventListener("offline", refresh);
    return () => {
      alive = false; window.clearInterval(timer);
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", refresh);
      window.removeEventListener("offline", refresh);
    };
  }, [selected, version, retry]);
  const snapshot = publication?.snapshot;
  const personal = publication?.personalSchedule;
  const assignments = personal ? [] : [...(snapshot?.cells.flatMap(c => c.employees.map(e => ({
    assignmentId: e.assignmentId,
    userId: e.userId,
    name: c.name,
    date: c.date,
    startsAt: c.startsAt,
    endsAt: c.endsAt,
    isShiftLead: e.isShiftLead
  }))) ?? []), ...(snapshot?.legacyAssignments.map(a => ({
    ...a,
    name: a.period === "DAY" ? "יום" : "לילה"
  })) ?? [])].sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const personalById = new Map(personal?.assignments.map(a => [a.assignmentId, a]));
  const ownAssignments = personal ? personal.assignments.filter(a => !week || weekStart(a.date) === week)
    : assignments.filter(a => a.userId === user?.id && (!week || weekStart(a.date) === week));
  const selectedIndex = periods.findIndex(p => `${p.year}/${p.month}` === selected);
  function chooseMonth(index: number) { const p = periods[index]; if (p) { setVersion(""); setSelected(`${p.year}/${p.month}`); } }
  async function report() {
    if (!publication) return;
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      await api(`/my-schedule/${selected}/issues`, "POST", {
        publicationId: publication.id,
        assignmentId: reporting,
        explanation
      });
      setIssues(await api<Issue[]>(`/my-schedule/${selected}/issues`));
      setReporting("");
      setExplanation("");
      setSuccess("הדיווח נשלח לבדיקת המנהל.");
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  return <section dir="rtl"><h1>סידור העבודה שלי</h1><Notice error={error} success={success} />
    {error && <button onClick={() => setRetry(n => n + 1)}>ניסיון נוסף</button>}
    {(periodsLoading || loading) && <p role="status">טוען את סידור העבודה…</p>}
    <nav className="schedule-navigation" aria-label="ניווט חודשי"><button disabled={selectedIndex < 0 || selectedIndex >= periods.length - 1 || busy} onClick={() => chooseMonth(selectedIndex + 1)}>החודש הקודם שפורסם</button>
    <label className="field">חודש סידור<select disabled={busy} value={selected} onChange={e => { setVersion(""); setSelected(e.target.value); }}>{periods.map(p => <option key={`${p.year}/${p.month}`} value={`${p.year}/${p.month}`}>{monthLabel(p.year, p.month)}</option>)}</select></label>
    <button disabled={selectedIndex <= 0 || busy} onClick={() => chooseMonth(selectedIndex - 1)}>החודש הבא שפורסם</button></nav>
    {!periodsLoading && !error && !periods.length && <p>עדיין לא פורסם עבורך סידור עבודה.</p>}
    {publication && <><label className="field">גרסת פרסום<select value={version} onChange={e => setVersion(e.target.value)}><option value="">הגרסה האחרונה</option>{versions.map(v => <option key={v} value={String(v)}>גרסה {v}</option>)}</select></label><h2>{publication.publicationType === "FINAL" ? "סידור עבודה סופי" : "סידור עבודה ראשוני"}</h2><p>גרסה {publication.versionNumber} · פורסם {instant(publication.publishedAt)}</p><p>{publication.notes}</p><label className="field">תצוגה שבועית<select value={week} onChange={e => setWeek(e.target.value)}><option value="">כל החודש</option>{[...new Set([...(snapshot?.cells.map(c => weekStart(c.date)) ?? []), ...assignments.map(a => weekStart(a.date))])].sort().map(w => <option key={w} value={w}>שבוע {w}</option>)}</select></label>
    {version && Number(version) < latestVersion && <p className="notice">מוצגת גרסה היסטורית שנבחרה. השיבוצים והשעות מתייחסים לגרסה זו בלבד.</p>}
    {personal && <PlannedHours schedule={personal} />}
    {!ownAssignments.length && <p>{week ? "אין לך משמרות בשבוע שנבחר." : "אין לך משמרות בחודש זה בגרסת הפרסום שנבחרה."}</p>}
    <div className="table-scroll"><table className="personal-shifts"><caption>המשמרות שלי — דיווח על בעיות</caption><thead><tr><th>תאריך</th><th>סוג משמרת</th><th>התחלה</th><th>סיום</th><th>משך מתוכנן</th><th>דיווח</th></tr></thead><tbody>{ownAssignments.map(a => { const planned = personalById.get(a.assignmentId); return <tr key={a.assignmentId} className="own-assignment"><td><time dateTime={a.date}>{shiftDate(a.date)}</time></td><td>{a.name}{a.isShiftLead ? " · אחראי משמרת" : ""}<small>{planned?.category === "ON_CALL" || planned?.category === "AVAILABILITY" ? categories[planned.category] : planned?.period === "NIGHT" ? "לילה" : planned?.period === "DAY" ? "יום" : ""}</small></td><td>{planned?.startsAt ? <time dateTime={planned.startsAt}>{shiftTime(planned.startsAt)}</time> : "לא זמין"}</td><td>{planned?.endsAt ? <><time dateTime={planned.endsAt}>{shiftTime(planned.endsAt)}</time>{planned.overnight && <small>ביום הבא · חוצה חצות</small>}</> : "לא זמין"}</td><td>{planned?.plannedMinutes != null ? `${hours(planned.plannedMinutes)} שעות` : "משך העבודה אינו ידוע"}</td><td>{publication.publicationType === "PRELIMINARY" && (!version || Number(version) === latestVersion) && !issues.some(i => i.publicationId === publication.id && i.assignmentId === a.assignmentId) && <button disabled={busy} onClick={() => {
                  setReporting(a.assignmentId);
                  setExplanation("");
                }}>דיווח על בעיה</button>}</td></tr>; })}</tbody></table></div>
    {snapshot && <RosterGrid snapshot={snapshot} currentUserId={user?.id} week={week} />}
    {reporting && <form className="panel" onSubmit={e => {
        e.preventDefault();
        report();
      }}><label className="field">תיאור הבעיה<textarea required maxLength={2000} value={explanation} onChange={e => setExplanation(e.target.value)} /></label><p>הדיווח מועבר לבדיקה ואינו משנה את השיבוץ. העדפה או זמינות אינן מבטיחות שיבוץ מסוים.</p><button disabled={busy || !explanation.trim()}>שליחת דיווח</button><button type="button" disabled={busy} onClick={() => setReporting("")}>ביטול</button></form>}
    <h2>הדיווחים שלי</h2>{issues.map(i => <article className="panel" key={i.id}><p>{i.explanation}</p><strong>{issueStates[i.status]}</strong><p>{i.response}</p><small>{instant(i.createdAt)}</small></article>)}<a href="/shift-exchanges">בקשות החלפת משמרות</a></>}
  </section>;
}
