import { useEffect, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { monthLabel, instant, weekStart } from "../availability";
import { issueStates, type Publication, type Issue } from "../publication";
import { Notice } from "../components/forms";
export default function MySchedule() {
  const [periods, setPeriods] = useState<{
    year: number;
    month: number;
  }[]>([]);
  const [selected, setSelected] = useState("");
  const [publication, setPublication] = useState<Publication | null>(null);
  const [issues, setIssues] = useState<Issue[]>([]);
  const [week, setWeek] = useState("");
  const [reporting, setReporting] = useState("");
  const [explanation, setExplanation] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let alive = true;
    api<{
      year: number;
      month: number;
    }[]>("/my-schedule/periods").then(p => {
      if (alive) {
        setPeriods(p);
        if (p.length) setSelected(`${p[0].year}/${p[0].month}`);
      }
    }).catch(e => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, []);
  useEffect(() => {
    if (!selected) return;
    let alive = true;
    setPublication(null);
    setError("");
    setSuccess("");
    setWeek("");
    setReporting("");
    Promise.all([api<Publication>(`/my-schedule/${selected}`), api<Issue[]>(`/my-schedule/${selected}/issues`)]).then(([p, i]) => {
      if (alive) {
        setPublication(p);
        setIssues(i);
      }
    }).catch(e => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, [selected]);
  const snapshot = publication?.snapshot;
  const assignments = [...(snapshot?.cells.flatMap(c => c.employees.map(e => ({
    assignmentId: e.assignmentId,
    name: c.name,
    date: c.date,
    startsAt: c.startsAt,
    endsAt: c.endsAt,
    isShiftLead: e.isShiftLead
  }))) ?? []), ...(snapshot?.legacyAssignments.map(a => ({
    ...a,
    name: a.period === "DAY" ? "יום" : "לילה"
  })) ?? [])].sort((a, b) => a.startsAt.localeCompare(b.startsAt));
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
    <label className="field">חודש סידור<select disabled={busy} value={selected} onChange={e => setSelected(e.target.value)}>{periods.map(p => <option key={`${p.year}/${p.month}`} value={`${p.year}/${p.month}`}>{monthLabel(p.year, p.month)}</option>)}</select></label>
    {!periods.length && <p>עדיין לא פורסם עבורך סידור עבודה.</p>}
    {publication && <><h2>{publication.publicationType === "FINAL" ? "סידור עבודה סופי" : "סידור עבודה ראשוני"}</h2><p>גרסה {publication.versionNumber} · פורסם {instant(publication.publishedAt)}</p><p>{publication.notes}</p><label className="field">תצוגה שבועית<select value={week} onChange={e => setWeek(e.target.value)}><option value="">כל החודש</option>{[...new Set(assignments.map(a => weekStart(a.date)))].map(w => <option key={w} value={w}>שבוע {w}</option>)}</select></label>
    <div className="table-scroll"><table><caption>המשמרות שלי בלבד</caption><thead><tr><th>תאריך</th><th>סוג משמרת</th><th>התחלה</th><th>סיום</th><th>דיווח</th></tr></thead><tbody>{assignments.filter(a => !week || weekStart(a.date) === week).map(a => <tr key={a.assignmentId} className="own-assignment"><td>{a.date}</td><td>{a.name}{a.isShiftLead ? " · אחראי משמרת" : ""}</td><td>{instant(a.startsAt)}</td><td>{instant(a.endsAt)}</td><td>{publication.publicationType === "PRELIMINARY" && !issues.some(i => i.publicationId === publication.id && i.assignmentId === a.assignmentId) && <button disabled={busy} onClick={() => {
                  setReporting(a.assignmentId);
                  setExplanation("");
                }}>דיווח על בעיה</button>}</td></tr>)}</tbody></table></div>
    {reporting && <form className="panel" onSubmit={e => {
        e.preventDefault();
        report();
      }}><label className="field">תיאור הבעיה<textarea required maxLength={2000} value={explanation} onChange={e => setExplanation(e.target.value)} /></label><p>הדיווח מועבר לבדיקה ואינו משנה את השיבוץ. העדפה או זמינות אינן מבטיחות שיבוץ מסוים.</p><button disabled={busy || !explanation.trim()}>שליחת דיווח</button><button type="button" disabled={busy} onClick={() => setReporting("")}>ביטול</button></form>}
    <h2>הדיווחים שלי</h2>{issues.map(i => <article className="panel" key={i.id}><p>{i.explanation}</p><strong>{issueStates[i.status]}</strong><p>{i.response}</p><small>{instant(i.createdAt)}</small></article>)}<p>החלפות משמרות אינן זמינות בשלב זה.</p></>}
  </section>;
}
