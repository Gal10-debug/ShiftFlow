import { useEffect, useState } from "react";
import { api } from "../api";
import { message } from "../he";
import { instant, monthLabel } from "../availability";
import { type ExchangeAssignment } from "../exchanges";
import { type Candidate } from "../publication";
import ExchangeRequests from "../components/ExchangeRequests";
import { Notice } from "../components/forms";

export default function ShiftExchanges({ admin = false }: { admin?: boolean }) {
  const [periods, setPeriods] = useState<{year: number; month: number}[]>([]);
  const [period, setPeriod] = useState("");
  const [assignments, setAssignments] = useState<ExchangeAssignment[]>([]);
  const [assignment, setAssignment] = useState("");
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [replacement, setReplacement] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    if (admin) return;
    let alive = true;
    api<{year: number; month: number}[]>("/my-schedule/periods").then(r => { if (alive) { setPeriods(r); if (r.length) setPeriod(`${r[0].year}/${r[0].month}`); } }).catch(e => { if (alive) setError(message(e)); });
    return () => { alive = false; };
  }, [admin]);
  useEffect(() => {
    if (!period || admin) return;
    let alive = true;
    setAssignments([]); setAssignment(""); setCandidates([]); setReplacement("");
    let sequence = 0;
    const load = () => { const ticket = ++sequence; return api<ExchangeAssignment[]>(`/shift-exchanges/${period}/assignments`).then(r => { if (alive && ticket === sequence) setAssignments(previous => JSON.stringify(previous) === JSON.stringify(r) ? previous : r); }).catch(e => { if (alive && ticket === sequence) { setAssignments([]); setAssignment(""); setCandidates([]); setReplacement(""); setError(message(e)); } }); };
    void load();
    const timer = window.setInterval(() => { if (!document.hidden) void load(); }, 5000);
    const refresh = () => { sequence++; setAssignments([]); setAssignment(""); setCandidates([]); setReplacement(""); if (!document.hidden) void load(); };
    window.addEventListener("focus", refresh); window.addEventListener("offline", refresh); document.addEventListener("visibilitychange", refresh);
    return () => { alive = false; window.clearInterval(timer); window.removeEventListener("focus", refresh); window.removeEventListener("offline", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [period, admin, reload]);
  useEffect(() => {
    const selected = assignments.find(a => a.assignmentId === assignment);
    setCandidates([]); setReplacement("");
    if (!selected) return;
    let alive = true;
    api<Candidate[]>(`/shift-exchanges/${period}/candidates?publicationId=${selected.publicationId}&assignmentId=${selected.assignmentId}`).then(r => { if (alive) setCandidates(r); }).catch(e => { if (alive) setError(message(e)); });
    return () => { alive = false; };
  }, [period, assignment, assignments]);
  async function create() {
    const selected = assignments.find(a => a.assignmentId === assignment);
    if (!selected) return;
    setBusy(true); setError(""); setSuccess("");
    try {
      await api(`/shift-exchanges/${period}`, "POST", { publicationId: selected.publicationId, assignmentId: selected.assignmentId, replacementUserId: replacement });
      setReload(r => r + 1); setSuccess("הבקשה נשלחה למחליף. לאחר הסכמתו נדרש אישור מנהל ופרסום מחדש.");
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  return <section dir="rtl"><h1>{admin ? "ניהול החלפות משמרות" : "החלפות משמרות"}</h1><Notice error={error} success={success} />
    {!admin && <form className="panel" onSubmit={e => { e.preventDefault(); void create(); }}><h2>בקשת החלפה חדשה</h2><fieldset disabled={busy}>
      <label className="field">חודש<select value={period} onChange={e => setPeriod(e.target.value)}>{periods.map(p => <option key={`${p.year}/${p.month}`} value={`${p.year}/${p.month}`}>{monthLabel(p.year, p.month)}</option>)}</select></label>
      <label className="field">המשמרת שלי<select required value={assignment} onChange={e => setAssignment(e.target.value)}><option value="">בחרו שיבוץ</option>{assignments.map(a => <option key={a.assignmentId} value={a.assignmentId}>{a.name} · {a.date} · {instant(a.startsAt)}</option>)}</select></label>
      {!assignments.length && <p>אין שיבוצים עתידיים הזמינים להחלפה בחודש זה.</p>}
      <label className="field">עובד מחליף כשיר<select required value={replacement} onChange={e => setReplacement(e.target.value)}><option value="">בחרו מחליף</option>{candidates.map(c => <option key={c.userId} value={c.userId}>{c.name}</option>)}</select></label>
      <p>מוצגים מחליפים העומדים באילוצים כעת. הכשירות תיבדק שוב בהסכמה ובאישור המנהל. כוננות מחליפים בשישי ובשבת יחד.</p>
      <button disabled={!replacement || !assignments.some(a => a.assignmentId === assignment)}>שליחת בקשת החלפה</button>
    </fieldset></form>}
    <ExchangeRequests admin={admin} reload={reload} onChanged={() => setReload(r => r + 1)} />
  </section>;
}
