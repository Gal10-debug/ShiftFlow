import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { useAuth } from "../auth";
import { message } from "../he";
import RosterGrid from "./RosterGrid";
import { type Review } from "../publication";
import { instant, monthLabel } from "../availability";
import { type Exchange, exchangeStates } from "../exchanges";
import { Notice } from "./forms";

export default function ExchangeRequests({ admin = false, reload = 0, onChanged }: { admin?: boolean; reload?: number; onChanged?: () => void }) {
  const { user } = useAuth();
  const [requests, setRequests] = useState<Exchange[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const publishDialog = useRef<HTMLDialogElement>(null);
  const [publishing, setPublishing] = useState<Exchange | null>(null);
  const [reviews, setReviews] = useState<Review[]>([]);
  const [ackShortages, setAckShortages] = useState(false);
  const [ackIssues, setAckIssues] = useState(false);
  const [notes, setNotes] = useState("");
  async function openPublication(r: Exchange) {
    setBusy(true); setError("");
    try {
      setReviews(await api<Review[]>(`/admin/shift-exchanges/${r.id}/publication-review`));
      setPublishing(r); setAckShortages(false); setAckIssues(false); setNotes(""); publishDialog.current?.showModal();
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  async function acknowledge(review: Review) {
    if (!publishing) return;
    setBusy(true); setError("");
    try {
      await api(`/admin/scheduling/periods/${review.draft.year}/${review.draft.month}/review`, "POST", { expectedToken: review.token });
      setReviews(await api<Review[]>(`/admin/shift-exchanges/${publishing.id}/publication-review`));
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  async function publish() {
    if (!publishing) return;
    setBusy(true); setError("");
    try {
      await api(`/admin/shift-exchanges/${publishing.id}/publish`, "POST", {
        periods: reviews.map(r => ({ year: r.draft.year, month: r.draft.month, expectedToken: r.token })),
        acknowledgeShortages: ackShortages, acknowledgeOpenIssues: ackIssues, notes: notes || null
      });
      publishDialog.current?.close(); setPublishing(null); setRequests(await api<Exchange[]>(base)); onChanged?.();
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  const base = admin ? "/admin/shift-exchanges" : "/shift-exchanges";
  useEffect(() => {
    let alive = true; let sequence = 0;
    const load = () => { const ticket = ++sequence; return api<Exchange[]>(base).then(r => { if (alive && ticket === sequence) setRequests(r); }).catch(e => { if (alive && ticket === sequence) { setRequests([]); setError(message(e)); } }); };
    void load();
    const timer = window.setInterval(() => { if (!document.hidden) void load(); }, 5000);
    const refresh = () => { sequence++; if (!admin) setRequests([]); if (!document.hidden) void load(); };
    window.addEventListener("focus", refresh); window.addEventListener("offline", refresh); document.addEventListener("visibilitychange", refresh);
    return () => { alive = false; window.clearInterval(timer); window.removeEventListener("focus", refresh); window.removeEventListener("offline", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [base, reload, admin]);
  async function decide(r: Exchange, decision: string) {
    setBusy(true); setError("");
    try {
      await api(`${base}/${r.id}/decision`, "POST", { expectedVersion: r.version, decision, reason: reasons[r.id] || null });
      setRequests(await api<Exchange[]>(base)); onChanged?.();
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  return <section dir="rtl"><h2>{admin ? "אישור החלפות משמרות" : "בקשות החלפת המשמרות שלי"}</h2><Notice error={error} />
    {admin && <p>האישור שומר תיקון בטיוטה. יש לבדוק ולפרסם מחדש כדי שההחלפה תיכנס לתוקף. זוג כוננות בחודשים סמוכים מתפרסם בשני החודשים יחד דרך פעולת הפרסום להלן.</p>}
    {!requests.length && <p>אין בקשות החלפה.</p>}
    {requests.map(r => <article className="panel" key={r.id}><p>{r.requesterName} ← {r.replacementName}</p>
      {r.assignments.map(a => <p key={a.assignmentId}>{a.name} · {a.date} · {instant(a.startsAt)}{a.isShiftLead ? " · אחראי משמרת" : ""}</p>)}
      {!r.assignments.length && <p>פרטי השיבוץ אינם זמינים לצפייה.</p>}
      <strong>{exchangeStates[r.status]}</strong><p>{r.reason}</p>
      {admin && r.status === "APPROVED_PENDING_PUBLICATION" && <button disabled={busy} onClick={() => openPublication(r)}>בדיקה ופרסום התיקון</button>}
      {(r.status === "AWAITING_REPLACEMENT" || r.status === "AWAITING_ADMIN") && <fieldset disabled={busy}>
        <label className="field">סיבה או הערה<textarea maxLength={2000} value={reasons[r.id] ?? ""} onChange={e => setReasons({ ...reasons, [r.id]: e.target.value })} /></label>
        {admin ? <>{r.status === "AWAITING_ADMIN" && <button onClick={() => decide(r, "APPROVE")}>אישור ושמירת תיקון בטיוטה</button>}<button onClick={() => decide(r, "REJECT")}>דחיית הבקשה</button></> : <>
          {r.requesterUserId === user?.id && <button onClick={() => decide(r, "CANCEL")}>ביטול בקשה</button>}
          {r.replacementUserId === user?.id && r.status === "AWAITING_REPLACEMENT" && <><button onClick={() => decide(r, "ACCEPT")}>הסכמה להחלפה</button><button onClick={() => decide(r, "REJECT")}>דחיית ההחלפה</button></>}
        </>}
      </fieldset>}
    </article>)}
    <dialog ref={publishDialog} aria-label="בדיקה ופרסום תיקון החלפה" onCancel={() => setPublishing(null)}>{publishing && <form onSubmit={e => { e.preventDefault(); void publish(); }}>
      <h2>בדיקה ופרסום תיקון החלפה</h2><Notice error={error} />
      <p>יפורסמו גרסאות חדשות לכל החודשים הבאים יחד. פרסום חדש יהיה גלוי לעובדים גם אם הגרסה הקודמת הוסתרה.</p>
      {reviews.map(r => <section key={`${r.draft.year}/${r.draft.month}`}><h3>{monthLabel(r.draft.year, r.draft.month)}</h3>
        <p>חוסרי איוש: {r.shortage} · דיווחים פתוחים: {r.openIssues} · סתירות: {r.conflicts.length}</p>
        <RosterGrid snapshot={r.draft} />
        {r.needsReview && <button type="button" disabled={busy || !!r.conflicts.length} onClick={() => acknowledge(r)}>אישור בדיקה מחודשת לחודש זה</button>}
      </section>)}
      <fieldset disabled={busy}><label><input type="checkbox" checked={ackShortages} onChange={e => setAckShortages(e.target.checked)} />אני מאשר/ת במפורש את חוסרי האיוש בחודשים המוצגים</label>
        <label><input type="checkbox" checked={ackIssues} onChange={e => setAckIssues(e.target.checked)} />אני מאשר/ת פרסום עם הדיווחים הפתוחים המוצגים</label>
        <label className="field">הערת פרסום<textarea maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
        <button disabled={reviews.some(r => r.needsReview || !!r.conflicts.length || r.shortage > 0 && !ackShortages || r.openIssues > 0 && !ackIssues)}>אישור ופרסום לכל החודשים המוצגים</button>
        <button type="button" onClick={() => { publishDialog.current?.close(); setPublishing(null); }}>ביטול</button>
      </fieldset>
    </form>}</dialog>
  </section>;
}
