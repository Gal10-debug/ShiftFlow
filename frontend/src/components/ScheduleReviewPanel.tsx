import { monthLabel } from "../availability";
import RosterGrid from "./RosterGrid";
import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { instant } from "../availability";
import { lifecycle, issueStates, type Candidate, type Issue, type Publication, type Review } from "../publication";
import { Notice } from "./forms";
export interface SelectedCell {
  definitionId: string;
  date: string;
  assignmentId?: string;
}
export default function ScheduleReviewPanel({
  year,
  month,
  reload,
  selected,
  onChanged,
  onClose,
  onEditableChange
}: {
  year: number;
  month: number;
  reload: number;
  selected: SelectedCell | null;
  onChanged: () => void;
  onClose?: () => void;
  onEditableChange?: (editable: boolean) => void;
}) {
  const base = `/admin/scheduling/periods/${year}/${month}`;
  const editor = useRef<HTMLDialogElement>(null);
  const [review, setReview] = useState<Review | null>(null);
  const [history, setHistory] = useState<Publication[]>([]);
  const [issues, setIssues] = useState<Issue[]>([]);
  const submitting = useRef(false);
  const [candidateLoading, setCandidateLoading] = useState(false);
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [oldAssignment, setOldAssignment] = useState("");
  const [userId, setUserId] = useState("");
  const [operation, setOperation] = useState("ASSIGN");
  const [destination, setDestination] = useState("");
  const [targetDate, setTargetDate] = useState("");
  const [shortages, setShortages] = useState(false);
  const [openIssues, setOpenIssues] = useState(false);
  const [notes, setNotes] = useState("");
  const [assignmentNote, setAssignmentNote] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const controlDialog = useRef<HTMLDialogElement>(null);
  const [controlTarget, setControlTarget] = useState<{ publication: Publication; operation: string } | null>(null);
  const [controlReason, setControlReason] = useState("");
  async function controlPublication() {
    if (!controlTarget || busy) return;
    setBusy(true); setError("");
    try {
      await api(`${base}/publications/${controlTarget.publication.id}/control`, "POST", {
        expectedVersion: controlTarget.publication.controlVersion ?? 0,
        operation: controlTarget.operation, reason: controlReason
      });
      controlDialog.current?.close(); setControlTarget(null); await load(); onChanged();
      setSuccess("בקרת הפרסום עודכנה ותועדה.");
    } catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  function openControl(publication: Publication, operation: string) {
    setControlReason(""); setControlTarget({ publication, operation }); controlDialog.current?.showModal();
  }
  const [responses, setResponses] = useState<Record<string, string>>({});
  useEffect(() => {
    onEditableChange?.(review?.editingContractVersion === 1 && (review?.state === "DRAFT" || review?.state === "UNDER_CORRECTION" || review?.state === "FINAL_PUBLISHED"));
  }, [review?.state, review?.editingContractVersion, onEditableChange]);
  useEffect(() => {
    if (operation === "NOTE") setAssignmentNote(review?.draft.cells.flatMap(c => c.employees).find(e => e.assignmentId === oldAssignment)?.note ?? "");
  }, [operation, oldAssignment]);
  async function load() {
    const [r, h, i] = await Promise.all([api<Review>(base), api<Publication[]>(`${base}/publications`), api<Issue[]>(`${base}/issues`)]);
    setReview(r);
    setHistory(h);
    setIssues(i);
  }
  useEffect(() => {
    let alive = true;
    if (review?.draft.year !== year || review?.draft.month !== month) setReview(null);
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
    setOldAssignment(selected?.assignmentId ?? "");
    setUserId("");
    setAssignmentNote("");
    setOperation(selected?.assignmentId ? "REPLACE" : "ASSIGN");
    setDestination(selected?.definitionId ?? "");
    setTargetDate(selected?.date ?? "");
  }, [selected?.definitionId, selected?.date, selected?.assignmentId]);
  const cell = review?.draft.cells.find(c => c.definitionId === selected?.definitionId && c.date === selected?.date);
  useEffect(() => {
    setCandidates([]);
    setCandidateLoading(false);
    setUserId("");
    if (!destination || !targetDate || operation === "NOTE" || operation === "REMOVE") return;
    let alive = true;
    setCandidateLoading(true);
    const query = new URLSearchParams({
      definitionId: destination,
      date: targetDate
    });
    if (oldAssignment && operation !== "ASSIGN") query.set("replacing", oldAssignment);
    api<Candidate[]>(`${base}/candidates?${query}`).then(c => {
      if (alive) setCandidates(c);
    }).catch(e => {
      if (alive) setError(message(e));
    }).finally(() => { if (alive) setCandidateLoading(false); });
    return () => {
      alive = false;
    };
  }, [base, destination, targetDate, oldAssignment, operation, reload]);
  async function action(path: string, body: unknown, method = "POST") {
    if (path === "assignments" && review?.editingContractVersion !== 1) { setError(he.errors.SCHEDULING_SERVER_OUTDATED); return; }
    if (submitting.current) return;
    submitting.current = true;
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      await api(`${base}/${path}`, method, body);
      await load();
      setSuccess("הפעולה נשמרה בהצלחה.");
      if (path === "assignments") { setOldAssignment(""); setOperation("ASSIGN"); setAssignmentNote(""); }
      setShortages(false);
      setOpenIssues(false);
      onChanged();
    } catch (e) {
      const fields = (e as { fieldErrors?: Record<string, string> }).fieldErrors;
      setError(fields ? [...new Set(Object.values(fields))].map(code => message(new Error(code))).join(" ") : message(e));
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }
  async function publish(type: "PRELIMINARY" | "FINAL") {
    if (!review || !window.confirm(type === "FINAL" ? "לפרסם סידור עבודה סופי? תיקונים נוספים יישמרו בטיוטה וידרשו פרסום גרסה סופית חדשה." : "לפרסם סידור עבודה ראשוני לעובדים?")) return;
    await action(`publications/${type}`, {
      expectedToken: review.token,
      acknowledgeShortages: shortages,
      acknowledgeOpenIssues: openIssues,
      notes
    });
  }
  useEffect(() => {
    const dialog = editor.current;
    if (!dialog) return;
    const previous = document.activeElement as HTMLElement | null;
    if (typeof dialog.showModal === "function") { if (!dialog.open) dialog.showModal(); }
    else dialog.setAttribute("open", "");
    return () => { if (dialog.open && typeof dialog.close === "function") dialog.close(); previous?.focus(); };
  }, [!!cell, selected?.definitionId, selected?.date, review?.state, review?.editingContractVersion]);
  if (!review?.draft) return <div className="panel"><h2>בדיקה ופרסום הסידור</h2><Notice error={error} /></div>;
  const editable = review.editingContractVersion === 1 && (review.state === "DRAFT" || review.state === "UNDER_CORRECTION");
  const canEditCell = review.editingContractVersion === 1 && (editable || review.state === "FINAL_PUBLISHED");
  const definitions = [...new Map(review.draft.cells.map(c => [c.definitionId, c])).values()];
  const assignments = [...review.draft.cells.flatMap(c => c.employees.map(e => ({
    ...e,
    label: `${c.date} · ${c.name} · ${e.name}`
  }))), ...review.draft.legacyAssignments.map(a => ({
    ...a,
    label: `${a.date} · ${a.name} · שיבוץ קודם`
  }))];
  function issueContext(issue: Issue) {
    const pub = history.find(h => h.id === issue.publicationId);
    const cell = pub?.snapshot?.cells.find(c => c.employees.some(e => e.assignmentId === issue.assignmentId));
    const employee = cell?.employees.find(e => e.assignmentId === issue.assignmentId);
    const legacy = pub?.snapshot?.legacyAssignments.find(a => a.assignmentId === issue.assignmentId);
    return `גרסה ${pub?.versionNumber ?? "—"} · ${employee?.name ?? legacy?.name ?? issue.reporterUserId} · ${cell?.date ?? legacy?.date ?? ""} · ${cell?.name ?? "שיבוץ קודם"}`;
  }
  return <section className="panel" dir="rtl" aria-label="בדיקה ופרסום הסידור"><h2>בדיקה ופרסום הסידור</h2><Notice error={cell && canEditCell ? "" : error} success={cell && canEditCell ? "" : success} />
    {review.editingContractVersion !== 1 && <p role="alert" className="notice error">{he.errors.SCHEDULING_SERVER_OUTDATED}</p>}
    <p><strong>{lifecycle[review.state]}</strong> · דיווחים פתוחים: {review.openIssues}</p>
    <p>{review.publicationReady ? "הטיוטה תקינה לפרסום בכפוף לאישור חוסרים ודיווחים." : "נדרשת בדיקת הטיוטה לפני פרסום."}</p>
    {!!review.pendingAbsenceWarnings?.length && <aside className="notice" aria-label="בקשות היעדרות ממתינות"><p>השיבוצים הבאים חופפים לבקשות היעדרות שטרם אושרו. זו אזהרה בלבד ואינה מונעת שיבוץ או פרסום.</p><ul>{review.pendingAbsenceWarnings.map(w => <li key={w.assignmentId}>{w.date} · {assignments.find(a => a.assignmentId === w.assignmentId)?.label}</li>)}</ul></aside>}
    {review.conflicts.some(c => c.code === "APPROVED_ABSENCE") && <p role="alert" className="notice error">קיימים שיבוצים החופפים להיעדרות מאושרת. יש להסיר או לתקן אותם בלוח ולאשר בדיקה לפני הפרסום. פרסום קודם נשאר ללא שינוי.</p>}
    {review.needsReview && <p className="notice">הזמינות או נתוני השיבוץ דורשים בדיקה מחודשת. יש לפתור סתירות ולאשר בדיקה לפני הפרסום.</p>}
    <fieldset disabled={busy}>
      {editable && <button disabled={!!review.conflicts.length} onClick={() => action("review", {
        expectedToken: review.token
      })}>אישור בדיקת זמינות ואילוצים</button>}
      {review.state === "PRELIMINARY_PUBLISHED" && <button onClick={() => action("correction", {
        expectedToken: review.token
      })}>כניסה למצב תיקונים</button>}
      {(editable || review.state === "PRELIMINARY_PUBLISHED") && review.shortage > 0 && <label className="check"><input type="checkbox" checked={shortages} onChange={e => setShortages(e.target.checked)} />אני מאשר פרסום עם {review.shortage} מקומות לא מאוישים</label>}
      <label className="check"><input type="checkbox" checked={openIssues} onChange={e => setOpenIssues(e.target.checked)} />אני מאשר פרסום סופי עם {review.openIssues} דיווחים שטרם טופלו</label>
      <label className="field">הערות לפרסום<textarea maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
      {editable && !review.finalRevision && <button disabled={!review.publicationReady || review.shortage > 0 && !shortages} onClick={() => publish("PRELIMINARY")}>{review.state === "UNDER_CORRECTION" ? "פרסום ראשוני מעודכן" : "פרסום ראשוני"}</button>}
      {(review.state === "PRELIMINARY_PUBLISHED" || review.state === "UNDER_CORRECTION") && <button disabled={!review.publicationReady || review.shortage > 0 && !shortages || review.openIssues > 0 && !openIssues} onClick={() => publish("FINAL")}>פרסום סופי</button>}
    </fieldset>
    {canEditCell && cell && <dialog ref={editor} className="cell-editor" aria-label="עריכת שיבוץ" onCancel={e => { e.preventDefault(); onClose?.(); }}><button type="button" onClick={onClose} disabled={busy}>סגירה</button><form onSubmit={e => {
      e.preventDefault();
      action("assignments", {
        expectedToken: review.token,
        operation,
        assignmentId: oldAssignment || null,
        userId: userId || null,
        definitionId: destination || null,
        date: targetDate || null,
        note: operation === "NOTE" && !assignmentNote ? null : assignmentNote || null
      });
    }}>
      <h3>תיקון ידני</h3><Notice error={error} success={success} /><p>{cell.name} · {cell.date} · נדרשים {cell.requiredEmployees} · שובצו {cell.employees.length}</p>
      {cell.semantic === "WEEKEND_ON_CALL" && <p className="notice">כל שינוי יחול אוטומטית על שישי ושבת יחד, גם בחודש סמוך. הזוג נספר בחודש של יום שישי.</p>}<fieldset disabled={busy}>
      <label className="field">פעולה<select value={operation} onChange={e => setOperation(e.target.value)}><option value="ASSIGN">הוספת עובד</option><option value="REMOVE">הסרת שיבוץ</option><option value="REPLACE">החלפת עובד</option><option value="MOVE">העברת שיבוץ</option><option value="NOTE">עריכת הערה</option></select></label>
      {operation !== "ASSIGN" && <label className="field">שיבוץ קיים<select required value={oldAssignment} onChange={e => {
            const id = e.target.value;
            setOldAssignment(id);
            if (operation === "REPLACE" || operation === "NOTE" || operation === "REMOVE") {
              const origin = review.draft.cells.find(c => c.employees.some(a => a.assignmentId === id));
              if (origin) {
                setDestination(origin.definitionId);
                setTargetDate(origin.date);
              }
            }
          }}><option value="">בחרו שיבוץ</option>{assignments.filter(a => cell.employees.some(e => e.assignmentId === a.assignmentId)).map(a => <option key={a.assignmentId} value={a.assignmentId}>{a.label}</option>)}</select></label>}
      {(operation === "ASSIGN" || operation === "MOVE") && <><label className="field">סוג משמרת<select required value={destination} onChange={e => setDestination(e.target.value)}><option value="">בחרו סוג</option>{definitions.map(c => <option key={c.definitionId} value={c.definitionId}>{c.name}</option>)}</select></label><label className="field">תאריך שיבוץ<input required type="date" min={`${year}-${String(month).padStart(2, "0")}-01`} max={`${year}-${String(month).padStart(2, "0")}-${new Date(Date.UTC(year, month, 0)).getUTCDate()}`} value={targetDate} onChange={e => setTargetDate(e.target.value)} /></label></>}
      {(operation === "ASSIGN" || operation === "REPLACE") && <label className="field">עובד כשיר<select required value={userId} onChange={e => setUserId(e.target.value)}><option value="">בחרו עובד</option>{candidates.filter(c => c.eligible).map(c => <option key={c.userId} value={c.userId}>{c.name}{c.isShiftLeadQualified ? " · מוסמך" : ""}{c.preferred ? " · העדפה" : ""}</option>)}</select></label>}
      {candidateLoading && <p role="status">בודק כשירות וזמינות…</p>}
      {!!candidates.length && <details><summary>כשירות ומניעות עובדים</summary><ul>{candidates.map(c => <li key={c.userId}>{c.name}: {c.eligible ? "כשיר" : c.reasons.map(r => message(new Error(r))).join("; ")}</li>)}</ul></details>}
      {(operation === "NOTE" || operation === "ASSIGN" || operation === "REPLACE") && <label className="field">הערה לשיבוץ<textarea maxLength={1000} value={assignmentNote} onChange={e => setAssignmentNote(e.target.value)} placeholder={operation === "NOTE" ? cell.employees.find(e => e.assignmentId === oldAssignment)?.note ?? "" : ""} /><small>ההערה גלויה לכל העובדים לאחר פרסום. בכוננות היא זהה בשני הימים. שדה ריק מסיר הערה.</small></label>}
      {cell && <p>תא נבחר: {cell.name} · {cell.date}</p>}<button className="primary" disabled={candidateLoading || ((operation === "ASSIGN" || operation === "REPLACE") && !candidates.some(c => c.userId === userId && c.eligible))}>{busy ? "שומר…" : "שמירת תיקון ידני"}</button></fieldset></form></dialog>}
    <h3>דיווחי עובדים</h3>{!issues.length && <p>אין דיווחים.</p>}{issues.map(i => <article key={i.id}><h4>{issueContext(i)}</h4><p>{instant(i.createdAt)} · {issueStates[i.status]} · {i.explanation}</p><label className="field">תגובת מנהל<textarea maxLength={2000} value={responses[i.id] ?? i.response ?? ""} onChange={e => setResponses({
          ...responses,
          [i.id]: e.target.value
        })} /></label><label className="field">עדכון מצב דיווח<select aria-label={`מצב דיווח ${i.id}`} disabled={busy} value={i.status} onChange={e => action(`issues/${i.id}`, {
          expectedVersion: i.version,
          status: e.target.value,
          response: responses[i.id] ?? i.response
        }, "PUT")}>{Object.entries(issueStates).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>{i.response && <p>{i.response}</p>}</article>)}
    {review.state === "FINAL_PUBLISHED" && <p className="notice">בחירת פעולת עריכה בלוח תפתח טיוטת תיקון. העובדים ימשיכו לראות את הגרסה הסופית עד לפרסום סופי מעודכן.</p>}
    <h3>היסטוריית גרסאות</h3>{history.map(h => <details key={h.id}><summary>גרסה {h.versionNumber} · {h.publicationType === "FINAL" ? "סופי" : h.publicationType === "PRELIMINARY" ? "ראשוני" : "היסטורי"} · {instant(h.publishedAt)}</summary><p>מצב נראות: <strong>{h.deleted ? "נמחק — נשמר בארכיון בלבד" : h.hidden ? "מוסתר מהעובדים" : "גלוי לעובדים בכפוף למצב הגרסה האחרונה"}</strong></p>
      {!h.deleted && <><button disabled={busy} onClick={() => openControl(h, h.hidden ? "RESTORE" : "HIDE")}>{h.hidden ? "החזרת נראות" : "הסתרת הפרסום"}</button><button disabled={busy} onClick={() => openControl(h, "DELETE")}>מחיקת הפרסום</button></>}
      <p>{h.notes}</p>{h.snapshot && <RosterGrid snapshot={h.snapshot} />}<p>{h.acknowledgedShortages ? "חוסרי האיוש אושרו במפורש. " : ""}{h.acknowledgedOpenIssues ? `אושר פרסום עם ${h.openIssueCount} דיווחים פתוחים.` : ""}</p><p>פורסם על ידי: <bdi>{h.publishedByName ?? h.publishedByUserId}</bdi></p></details>)}
    <dialog ref={controlDialog} aria-label="אישור שינוי פרסום" onCancel={() => setControlTarget(null)}>
      {controlTarget && <form onSubmit={e => { e.preventDefault(); void controlPublication(); }}><h3>{controlTarget.operation === "DELETE" ? "אישור מחיקת פרסום" : controlTarget.operation === "HIDE" ? "אישור הסתרת פרסום" : "אישור החזרת נראות"}</h3>
        <p>{monthLabel(year, month)} · גרסה {controlTarget.publication.versionNumber} · {controlTarget.publication.publicationType === "FINAL" ? "פרסום סופי" : "פרסום ראשוני"} · {instant(controlTarget.publication.publishedAt)}</p>
        <p>מזהה פרסום: <bdi>{controlTarget.publication.id}</bdi></p>
        <p>{controlTarget.operation === "DELETE" ? "הפרסום יוסר מגישת העובדים ויישמר בארכיון לצורכי תיעוד. לא ניתן לבטל מחיקה. אם זו הגרסה האחרונה, לא תוחזר גרסה קודמת באופן אוטומטי; ניתן לפרסם תיקון חדש." : controlTarget.operation === "HIDE" ? "הפרסום יוסתר מהעובדים. ניתן להחזיר את נראותו במפורש. שמירת טיוטה לא תחזיר נראות." : "הפרסום יחזור לנראות. גרסה קודמת אינה מחליפה את הגרסה האחרונה."}</p>
        <Notice error={error} /><label className="field">סיבת הפעולה<textarea required maxLength={2000} value={controlReason} onChange={e => setControlReason(e.target.value)} /></label>
        <button disabled={busy || !controlReason.trim()}>אישור הפעולה</button><button type="button" disabled={busy} onClick={() => { controlDialog.current?.close(); setControlTarget(null); }}>ביטול</button>
      </form>}
    </dialog>
    <a href="/exchange-management">אישור החלפות משמרות</a>
  </section>;
}
