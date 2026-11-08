import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import { he, message } from "../he";
import { instant } from "../availability";
import { reasons, statuses, kinds, describe, scheduleLink, type Absence, type AbsencePattern, type AbsenceReport, type AbsenceConflict } from "../absences";
import type { Team } from "../teams";
import AbsenceForm from "../components/AbsenceForm";
import { Notice } from "../components/forms";

function ConflictList({ items, admin }: { items: AbsenceConflict[]; admin: boolean }) {
  return <ul className="absence-conflicts">{items.map(c => <li key={`${c.assignmentId}/${c.publicationId ?? "draft"}`}>
    <strong>{c.shiftName}</strong> · {c.date} · {instant(c.startsAt)} – {instant(c.endsAt)} · {c.removable ? "טיוטה — מיועד להסרה באישור" : `פורסם בגרסה ${c.publicationVersion} — נדרש תיקון ופרסום חדש`}
    {c.removalBlock && <p role="alert">{message(new Error(c.removalBlock))}</p>}
    {admin && <p><Link to={scheduleLink(c, true)}>פתיחת לוח הסידור לתיקון</Link></p>}
  </li>)}</ul>;
}
export default function Absences({ manage = false }: { manage?: boolean }) {
  const { user } = useAuth();
  const admin = user?.roles.includes("ADMIN") ?? false;
  const [items, setItems] = useState<Absence[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [knownEmployees, setKnownEmployees] = useState<Record<string, string>>({});
  const [query, setQuery] = useState("");
  const [filters, setFilters] = useState({ teamId: "", userId: "", status: "", from: "", through: "" });
  const [editing, setEditing] = useState<Absence | null>(null);
  const [formKey, setFormKey] = useState(0);
  const [reviewing, setReviewing] = useState<Absence | null>(null);
  const [report, setReport] = useState<AbsenceReport | null>(null);
  const [confirmRemovals, setConfirmRemovals] = useState(false);
  const [rejection, setRejection] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const gate = useRef(false);
  const path = manage ? `/absences/manage${query ? `?${query}` : ""}` : "/absences/mine";
  const title = manage ? admin ? "ניהול היעדרויות" : "אישור היעדרויות הצוות" : "היעדרויות";
  function accept(value: Absence[]) {
    setItems(value);
    setKnownEmployees(old => ({ ...old, ...Object.fromEntries(value.map(r => [r.userId, r.employeeName])) }));
  }
  useEffect(() => {
    let alive = true; setLoading(true); setError(""); setItems([]); setReviewing(null); setReport(null);
    api<Absence[]>(path).then(value => { if (alive) accept(value); }).catch(e => { if (alive) setError(message(e)); }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [path]);
  useEffect(() => {
    if (!manage) return;
    let alive = true;
    api<Team[]>(admin ? "/admin/teams" : "/team-availability/members").then(value => { if (alive) setTeams(value); }).catch(e => { if (alive) setError(message(e)); });
    return () => { alive = false; };
  }, [manage, admin]);
  async function action(url: string, body: unknown) {
    if (gate.current) return false;
    gate.current = true; setBusy(true); setError(""); setSuccess("");
    try {
      await api(url, "POST", body); accept(await api<Absence[]>(path)); setSuccess("הבקשה נשמרה בהצלחה."); return true;
    } catch (e) { setError(message(e)); return false; }
    finally { gate.current = false; setBusy(false); }
  }
  async function submit(pattern: AbsencePattern) {
    // JSON TimeOnly uses seconds; native time inputs emit hours and minutes.
    pattern = { ...pattern, startTime: pattern.startTime?.length === 5 ? pattern.startTime + ":00" : pattern.startTime,
      endTime: pattern.endTime?.length === 5 ? pattern.endTime + ":00" : pattern.endTime };
    if (await action(editing ? `/absences/${editing.id}/changes` : "/absences", editing ? { expectedVersion: editing.version, kind: "MODIFY", pattern } : pattern)) {
      setEditing(null); setFormKey(k => k + 1);
    }
  }
  async function inspect(request: Absence) {
    if (gate.current) return;
    gate.current = true; setBusy(true); setError(""); setReport(null); setReviewing(request); setConfirmRemovals(false); setRejection("");
    try { setReport(await api<AbsenceReport>(`/absences/${request.id}/conflicts`)); }
    catch (e) { setError(message(e)); }
    finally { gate.current = false; setBusy(false); }
  }
  async function decide(decision: string) {
    if (!reviewing || !report) return;
    if (await action(`/absences/${reviewing.id}/decision`, { expectedVersion: reviewing.version, decision,
      rejectionReason: decision === "REJECTED" ? rejection : null, conflictToken: report.token, confirmDraftRemovals: confirmRemovals })) {
      setReviewing(null); setReport(null);
    } else { setReport(null); }
  }
  const employees = { ...Object.fromEntries(teams.flatMap(t => t.members.map(m => [m.id, `${m.firstName} ${m.lastName}`]))), ...knownEmployees };
  return <section className="absences-page" dir="rtl">
    <div className="page-heading"><div><span className="eyebrow">בקשות ואישורים</span><h1>{title}</h1><p className="muted">{manage ? "אישור בקשות, בדיקת שיבוצים חופפים ומעקב אחר החלטות." : "בקשות חד־פעמיות וחוזרות. היעדרות מאושרת מחייבת את סידור העבודה."}</p></div></div>
    <Notice error={error} success={success} />
    <p className="notice">בקשה ממתינה אינה חוסמת שיבוץ. היעדרות מאושרת נשארת בתוקף עד שאושרו שינוי או ביטול.</p>
    {!manage && <AbsenceForm key={`${editing?.id ?? "new"}/${formKey}`} initial={editing?.pattern} busy={busy} submit={submit} cancel={editing ? () => setEditing(null) : undefined} />}
    {manage && <form className="panel absence-filters" aria-label="סינון בקשות היעדרות" onSubmit={e => {
      e.preventDefault(); setQuery(new URLSearchParams(Object.entries(filters).filter(([, v]) => v)).toString());
    }}><fieldset disabled={busy}><legend>סינון בקשות</legend><div className="absence-fields">
      <label className="field">צוות<select value={filters.teamId} onChange={e => setFilters({ ...filters, teamId: e.target.value })}><option value="">כל הצוותים המורשים</option>{teams.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <label className="field">עובד<select value={filters.userId} onChange={e => setFilters({ ...filters, userId: e.target.value })}><option value="">כל העובדים המורשים</option>{Object.entries(employees).map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
      <label className="field">מצב הבקשה<select value={filters.status} onChange={e => setFilters({ ...filters, status: e.target.value })}><option value="">כל המצבים</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label className="field">מתאריך<input type="date" value={filters.from} onChange={e => setFilters({ ...filters, from: e.target.value })} /></label>
      <label className="field">עד תאריך<input type="date" min={filters.from} value={filters.through} onChange={e => setFilters({ ...filters, through: e.target.value })} /></label>
    </div><button type="submit">החלת סינון</button></fieldset></form>}
    {reviewing && <section className="panel absence-review" aria-label="בדיקת בקשת היעדרות"><h2>בדיקת בקשה — {reviewing.employeeName}</h2><p>{kinds[reviewing.kind]} · {reasons[reviewing.pattern.reason]} · {describe(reviewing.pattern)}</p>
      {busy && !report && <p role="status">בודק התנגשויות…</p>}
      {report && <>{!report.items.length ? <p>לא נמצאו שיבוצים חופפים.</p> : <ConflictList items={report.items} admin={admin} />}
        {report.items.some(c => c.publicationId) && <p className="notice">אישור ההיעדרות ישאיר את הפרסום הקיים ללא שינוי. מנהל הסידור חייב לתקן ולפרסם גרסה חדשה; עד אז ההתנגשות נשארת פתוחה.</p>}
        {report.items.some(c => c.removable) && <label className="check"><input type="checkbox" disabled={busy} checked={confirmRemovals} onChange={e => setConfirmRemovals(e.target.checked)} />אני מאשר הסרת השיבוצים המוצגים מהטיוטות, כולל בני זוג כוננות</label>}
        <button className="primary" disabled={busy || report.items.some(c => c.removalBlock) || report.items.some(c => c.removable) && !confirmRemovals} onClick={() => void decide("APPROVED")}>אישור הבקשה</button>
        <label className="field">סיבת דחייה<textarea maxLength={2000} value={rejection} disabled={busy} onChange={e => setRejection(e.target.value)} /></label>
        <button disabled={busy || !rejection.trim()} onClick={() => void decide("REJECTED")}>דחיית הבקשה</button>
      </>}
      <div className="form-actions"><button disabled={busy} onClick={() => void inspect(reviewing)}>רענון בדיקת התנגשויות</button><button disabled={busy} onClick={() => { setReviewing(null); setReport(null); }}>סגירה</button></div>
    </section>}
    <h2>{manage ? "בקשות והיסטוריית החלטות" : "הבקשות שלי"}</h2>
    {loading ? <p role="status">{he.loading}</p> : !items.length && <p className="panel empty">לא נמצאו בקשות.</p>}
    <div className="absence-history">{items.map(r => <article className="panel absence-card" key={r.id} aria-label={`${kinds[r.kind]} ${r.employeeName}`}>
      <div className="absence-card-heading"><h3>{manage ? `${r.employeeName} · ` : ""}{kinds[r.kind]} · {reasons[r.pattern.reason]}</h3><span className={`badge absence-${r.status.toLowerCase()}`}>{statuses[r.status]}</span></div>
      {r.teamName && <p>{r.teamName}</p>}<p><bdi>{describe(r.pattern)}</bdi></p>{r.pattern.explanation && <p className="absence-explanation">{r.pattern.explanation}</p>}
      <p className="muted">הוגשה: {instant(r.createdAt)}</p>
      {r.status === "PENDING" && <p className="notice">טרם אושרה — אינה חוסמת שיבוץ.{r.replacesId ? " ההיעדרות הקודמת נשארת בתוקף." : ""}</p>}
      {r.kind === "CANCEL" && r.status === "APPROVED" && <p>ביטול ההיעדרות אושר.</p>}
      {r.decision && <p>החלטה: {statuses[r.decision.decision]} · {r.decision.actorName} · {instant(r.decision.occurredAt)}{r.decision.rejectionReason && <><br />סיבת הדחייה: {r.decision.rejectionReason}</>}</p>}
      {!!r.unresolvedConflicts.length && <aside aria-label="התנגשויות שטרם תוקנו"><p role="alert">ההיעדרות אושרה, אך קיימים שיבוצים שפורסמו וטרם תוקנו. נדרשים תיקון ופרסום גרסה חדשה.</p><ConflictList items={r.unresolvedConflicts} admin={admin} /></aside>}
      <div className="form-actions">
        {manage && r.status === "PENDING" && r.userId !== user?.id && <button disabled={busy} onClick={() => void inspect(r)}>בדיקה והחלטה</button>}
        {manage && r.status === "PENDING" && r.userId === user?.id && <p>בקשה אישית מחייבת החלטה של מאשר אחר.</p>}
        {!manage && r.status === "PENDING" && <button disabled={busy} onClick={() => void action(`/absences/${r.id}/cancel-pending`, { expectedVersion: r.version })}>ביטול בקשה ממתינה</button>}
        {!manage && r.status === "APPROVED" && r.kind !== "CANCEL" && (items.some(child => child.replacesId === r.id && child.status === "PENDING") ? <p>בקשת שינוי או ביטול ממתינה לאישור.</p> : <>
          <button disabled={busy} onClick={() => setEditing(r)}>בקשת שינוי</button>
          <button disabled={busy} onClick={() => void action(`/absences/${r.id}/changes`, { expectedVersion: r.version, kind: "CANCEL", pattern: null })}>בקשת ביטול היעדרות</button>
        </>)}
      </div>
    </article>)}</div>
  </section>;
}
