import { useEffect, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { instant } from "../availability";
import { lifecycle, issueStates, compareVersions, type Candidate, type Issue, type Publication, type Review } from "../publication";
import { Notice } from "./forms";
export interface SelectedCell {
  definitionId: string;
  date: string;
}
export default function ScheduleReviewPanel({
  year,
  month,
  reload,
  selected,
  onChanged
}: {
  year: number;
  month: number;
  reload: number;
  selected: SelectedCell | null;
  onChanged: () => void;
}) {
  const base = `/admin/scheduling/periods/${year}/${month}`;
  const [review, setReview] = useState<Review | null>(null);
  const [history, setHistory] = useState<Publication[]>([]);
  const [issues, setIssues] = useState<Issue[]>([]);
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [oldAssignment, setOldAssignment] = useState("");
  const [userId, setUserId] = useState("");
  const [operation, setOperation] = useState("ASSIGN");
  const [destination, setDestination] = useState("");
  const [targetDate, setTargetDate] = useState("");
  const [shortages, setShortages] = useState(false);
  const [openIssues, setOpenIssues] = useState(false);
  const [notes, setNotes] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const [compareA, setCompareA] = useState("");
  const [compareB, setCompareB] = useState("");
  const [responses, setResponses] = useState<Record<string, string>>({});
  async function load() {
    const [r, h, i] = await Promise.all([api<Review>(base), api<Publication[]>(`${base}/publications`), api<Issue[]>(`${base}/issues`)]);
    setReview(r);
    setHistory(h);
    setIssues(i);
  }
  useEffect(() => {
    let alive = true;
    setReview(null);
    setError("");
    setSuccess("");
    setShortages(false);
    setOpenIssues(false);
    Promise.all([api<Review>(base), api<Publication[]>(`${base}/publications`), api<Issue[]>(`${base}/issues`)]).then(([r, h, i]) => {
      if (alive) {
        setReview(r);
        setHistory(h);
        setIssues(i);
      }
    }).catch(e => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, [base, reload]);
  useEffect(() => {
    setOldAssignment("");
    setUserId("");
    setOperation("ASSIGN");
    setDestination(selected?.definitionId ?? "");
    setTargetDate(selected?.date ?? "");
  }, [selected?.definitionId, selected?.date]);
  const cell = review?.draft.cells.find(c => c.definitionId === selected?.definitionId && c.date === selected?.date);
  useEffect(() => {
    setCandidates([]);
    setUserId("");
    if (!destination || !targetDate) return;
    let alive = true;
    const query = new URLSearchParams({
      definitionId: destination,
      date: targetDate
    });
    if (oldAssignment && operation !== "ASSIGN") query.set("replacing", oldAssignment);
    api<Candidate[]>(`${base}/candidates?${query}`).then(c => {
      if (alive) setCandidates(c);
    }).catch(e => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, [base, destination, targetDate, oldAssignment, operation, reload]);
  async function action(path: string, body: unknown, method = "POST") {
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      await api(`${base}/${path}`, method, body);
      await load();
      setSuccess("הפעולה נשמרה בהצלחה.");
      setShortages(false);
      setOpenIssues(false);
      onChanged();
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  async function publish(type: "PRELIMINARY" | "FINAL") {
    if (!review || !window.confirm(type === "FINAL" ? "לפרסם סידור עבודה סופי? לאחר הפרסום לא ניתן לערוך אותו." : "לפרסם סידור עבודה ראשוני לעובדים?")) return;
    await action(`publications/${type}`, {
      expectedToken: review.token,
      acknowledgeShortages: shortages,
      acknowledgeOpenIssues: openIssues,
      notes
    });
  }
  if (!review?.draft) return <div className="panel"><h2>בדיקה ופרסום הסידור</h2><Notice error={error} /></div>;
  const editable = review.state === "DRAFT" || review.state === "UNDER_CORRECTION";
  const definitions = [...new Map(review.draft.cells.map(c => [c.definitionId, c])).values()];
  const assignments = [...review.draft.cells.flatMap(c => c.employees.map(e => ({
    ...e,
    label: `${c.date} · ${c.name} · ${e.name}`
  }))), ...review.draft.legacyAssignments.map(a => ({
    ...a,
    label: `${a.date} · ${a.name} · שיבוץ קודם`
  }))];
  const leftVersion = history.find(h => h.id === compareA)?.snapshot;
  const rightVersion = history.find(h => h.id === compareB)?.snapshot;
  const legacyChanged = JSON.stringify(leftVersion?.legacyAssignments) !== JSON.stringify(rightVersion?.legacyAssignments);
  function issueContext(issue: Issue) {
    const pub = history.find(h => h.id === issue.publicationId);
    const cell = pub?.snapshot?.cells.find(c => c.employees.some(e => e.assignmentId === issue.assignmentId));
    const employee = cell?.employees.find(e => e.assignmentId === issue.assignmentId);
    const legacy = pub?.snapshot?.legacyAssignments.find(a => a.assignmentId === issue.assignmentId);
    return `גרסה ${pub?.versionNumber ?? "—"} · ${employee?.name ?? legacy?.name ?? issue.reporterUserId} · ${cell?.date ?? legacy?.date ?? ""} · ${cell?.name ?? "שיבוץ קודם"}`;
  }
  const differences = compareVersions(history.find(h => h.id === compareA)?.snapshot ?? null, history.find(h => h.id === compareB)?.snapshot ?? null);
  return <section className="panel" dir="rtl" aria-label="בדיקה ופרסום הסידור"><h2>בדיקה ופרסום הסידור</h2><Notice error={error} success={success} />
    <p><strong>{lifecycle[review.state]}</strong> · חוסרים: {review.shortage} · דיווחים פתוחים: {review.openIssues}</p>
    <p>{review.publicationReady ? "הטיוטה תקינה לפרסום בכפוף לאישור חוסרים ודיווחים." : "נדרשת בדיקת הטיוטה לפני פרסום."}</p>
    {review.needsReview && <p className="notice">הזמינות או נתוני השיבוץ דורשים בדיקה מחודשת. יש לפתור סתירות ולאשר בדיקה לפני הפרסום.</p>}
    {!!review.conflicts.length && <ul className="notice error" aria-label="סתירות בסידור">{review.conflicts.map((c, i) => <li key={i}>{c.date} · {message(new Error(c.code))}</li>)}</ul>}
    <fieldset disabled={busy}>
      {editable && <button disabled={!!review.conflicts.length} onClick={() => action("review", {
        expectedToken: review.token
      })}>אישור בדיקת זמינות ואילוצים</button>}
      {review.state === "PRELIMINARY_PUBLISHED" && <button onClick={() => action("correction", {
        expectedToken: review.token
      })}>כניסה למצב תיקונים</button>}
      <label className="check"><input type="checkbox" checked={shortages} onChange={e => setShortages(e.target.checked)} />אני מאשר פרסום עם {review.shortage} מקומות לא מאוישים</label>
      <label className="check"><input type="checkbox" checked={openIssues} onChange={e => setOpenIssues(e.target.checked)} />אני מאשר פרסום סופי עם {review.openIssues} דיווחים שטרם טופלו</label>
      <label className="field">הערות לפרסום<textarea maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
      {editable && <button disabled={!review.publicationReady || review.shortage > 0 && !shortages} onClick={() => publish("PRELIMINARY")}>{review.state === "UNDER_CORRECTION" ? "פרסום ראשוני מעודכן" : "פרסום ראשוני"}</button>}
      {(review.state === "PRELIMINARY_PUBLISHED" || review.state === "UNDER_CORRECTION") && <button disabled={!review.publicationReady || review.shortage > 0 && !shortages || review.openIssues > 0 && !openIssues} onClick={() => publish("FINAL")}>פרסום סופי</button>}
    </fieldset>
    {editable && <form onSubmit={e => {
      e.preventDefault();
      action("assignments", {
        expectedToken: review.token,
        operation,
        assignmentId: oldAssignment || null,
        userId: userId || null,
        definitionId: destination || null,
        date: targetDate || null
      });
    }}>
      <h3>תיקון ידני</h3><p>אפשר לבחור תא בלוח או לבחור יעד כאן. כל שינוי נבדק ונשמר במפורש.</p><fieldset disabled={busy}>
      <label className="field">פעולה<select value={operation} onChange={e => setOperation(e.target.value)}><option value="ASSIGN">הוספת עובד</option><option value="REMOVE">הסרת שיבוץ</option><option value="REPLACE">החלפת עובד</option><option value="MOVE">העברת שיבוץ</option></select></label>
      {operation !== "ASSIGN" && <label className="field">שיבוץ קיים<select required value={oldAssignment} onChange={e => {
            const id = e.target.value;
            setOldAssignment(id);
            if (operation === "REPLACE") {
              const origin = review.draft.cells.find(c => c.employees.some(a => a.assignmentId === id));
              if (origin) {
                setDestination(origin.definitionId);
                setTargetDate(origin.date);
              }
            }
          }}><option value="">בחרו שיבוץ</option>{assignments.map(a => <option key={a.assignmentId} value={a.assignmentId}>{a.label}</option>)}</select></label>}
      {(operation === "ASSIGN" || operation === "MOVE") && <><label className="field">סוג משמרת<select required value={destination} onChange={e => setDestination(e.target.value)}><option value="">בחרו סוג</option>{definitions.map(c => <option key={c.definitionId} value={c.definitionId}>{c.name}</option>)}</select></label><label className="field">תאריך שיבוץ<input required type="date" min={`${year}-${String(month).padStart(2, "0")}-01`} max={`${year}-${String(month).padStart(2, "0")}-${new Date(Date.UTC(year, month, 0)).getUTCDate()}`} value={targetDate} onChange={e => setTargetDate(e.target.value)} /></label></>}
      {(operation === "ASSIGN" || operation === "REPLACE") && <label className="field">עובד כשיר<select required value={userId} onChange={e => setUserId(e.target.value)}><option value="">בחרו עובד</option>{candidates.filter(c => c.eligible).map(c => <option key={c.userId} value={c.userId}>{c.name}</option>)}</select></label>}
      {!!candidates.length && <details><summary>כשירות ומניעות עובדים</summary><ul>{candidates.map(c => <li key={c.userId}>{c.name}: {c.eligible ? "כשיר" : c.reasons.map(r => message(new Error(r))).join("; ")}</li>)}</ul></details>}
      {cell && <p>תא נבחר: {cell.name} · {cell.date}</p>}<button className="primary">שמירת תיקון ידני</button></fieldset></form>}
    <h3>דיווחי עובדים</h3>{!issues.length && <p>אין דיווחים.</p>}{issues.map(i => <article key={i.id}><h4>{issueContext(i)}</h4><p>{instant(i.createdAt)} · {issueStates[i.status]} · {i.explanation}</p><label className="field">תגובת מנהל<textarea maxLength={2000} value={responses[i.id] ?? i.response ?? ""} onChange={e => setResponses({
          ...responses,
          [i.id]: e.target.value
        })} /></label><label className="field">עדכון מצב דיווח<select aria-label={`מצב דיווח ${i.id}`} disabled={busy} value={i.status} onChange={e => action(`issues/${i.id}`, {
          expectedVersion: i.version,
          status: e.target.value,
          response: responses[i.id] ?? i.response
        }, "PUT")}>{Object.entries(issueStates).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>{i.response && <p>{i.response}</p>}</article>)}
    <h3>היסטוריית גרסאות והשוואה</h3>{history.map(h => <details key={h.id}><summary>גרסה {h.versionNumber} · {h.publicationType === "FINAL" ? "סופי" : h.publicationType === "PRELIMINARY" ? "ראשוני" : "היסטורי"} · {instant(h.publishedAt)}</summary><p>{h.notes}</p><p>{h.acknowledgedShortages ? "חוסרי האיוש אושרו במפורש. " : ""}{h.acknowledgedOpenIssues ? `אושר פרסום עם ${h.openIssueCount} דיווחים פתוחים.` : ""}</p><p>מזהה המנהל המפרסם: <bdi>{h.publishedByUserId}</bdi></p><ul>{h.snapshot?.cells.filter(c => c.employees.length > 0).map(c => <li key={c.definitionId + c.date}>{c.date} · {c.name} · {instant(c.startsAt)}–{instant(c.endsAt)} · {c.requiresShiftLeadQualification ? "כשירות אחמ״ש נדרשת · " : ""}נדרשים {c.requiredEmployees} · {c.employees.map(e => e.name).join(", ")}</li>)}</ul><ul>{h.snapshot?.legacyAssignments.map(a => <li key={a.assignmentId}>{a.date} · {a.name} · {instant(a.startsAt)}–{instant(a.endsAt)} · שיבוץ קודם</li>)}</ul></details>)}
    {history.length > 1 && <><label className="field">גרסה להשוואה א׳<select value={compareA} onChange={e => setCompareA(e.target.value)}><option value="">בחרו גרסה</option>{history.map(h => <option key={h.id} value={h.id}>{h.versionNumber}</option>)}</select></label><label className="field">גרסה להשוואה ב׳<select value={compareB} onChange={e => setCompareB(e.target.value)}><option value="">בחרו גרסה</option>{history.map(h => <option key={h.id} value={h.id}>{h.versionNumber}</option>)}</select></label>{compareA && compareB && <div aria-label="הבדלים בין גרסאות"><p>{differences.length} תאים השתנו</p>{legacyChanged && <div><p>שיבוצים קודמים ללא תא השתנו:</p><p>לפני: {leftVersion?.legacyAssignments.map(a => `${a.date} ${a.name} ${instant(a.startsAt)}`).join("; ") || "ללא"}</p><p>אחרי: {rightVersion?.legacyAssignments.map(a => `${a.date} ${a.name} ${instant(a.startsAt)}`).join("; ") || "ללא"}</p></div>}<ul>{differences.map(d => <li key={d.key}>{d.after?.date ?? d.before?.date}: {d.before?.name} ← {d.after?.name} · לפני: {d.before?.employees.map(e => e.name).join(", ") || "ללא שיבוץ"} · אחרי: {d.after?.employees.map(e => e.name).join(", ") || "ללא שיבוץ"} · נדרשים: {d.before?.requiredEmployees} ← {d.after?.requiredEmployees} · זמנים: {instant(d.before?.startsAt ?? null)}–{instant(d.before?.endsAt ?? null)} ← {instant(d.after?.startsAt ?? null)}–{instant(d.after?.endsAt ?? null)} · כשירות אחמ״ש: {d.before?.requiresShiftLeadQualification ? "נדרשת" : "לא נדרשת"} ← {d.after?.requiresShiftLeadQualification ? "נדרשת" : "לא נדרשת"}</li>)}</ul></div>}</>}
    <p className="muted">החלפות משמרות אינן זמינות בשלב זה.</p>
  </section>;
}
