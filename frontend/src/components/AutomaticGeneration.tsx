import { useEffect, useState } from "react";
import { api } from "../api";
import { message } from "../he";
import type { Generation, ShiftTypeDefinition } from "../scheduling";

export default function AutomaticGeneration({ definitions, value, onChange, onSaved, disabled }: {
  definitions: ShiftTypeDefinition[]; value: Generation | null;
  onChange: (value: Generation | null) => void; onSaved: () => void; disabled: boolean;
}) {
  const [month, setMonth] = useState(() => new Intl.DateTimeFormat("sv-SE", { timeZone: "Asia/Jerusalem", year: "numeric", month: "2-digit" }).format(new Date()));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [shortagesOnly, setShortagesOnly] = useState(false);
  const pending = value?.state === "QUEUED" || value?.state === "RUNNING";
  useEffect(() => {
    if (!pending || !value) return;
    let alive = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const v = await api<Generation>(`/admin/scheduling/generation/${value!.id}`);
        if (alive) { onChange(v); setError(""); }
      } catch (e) { if (alive) setError(message(e)); }
      finally { if (alive) timer = setTimeout(() => void poll(), 2000); }
    }
    timer = setTimeout(() => void poll(), 1000);
    return () => { alive = false; clearTimeout(timer); };
  }, [value?.id, pending, onChange]);
  async function start() {
    setBusy(true); setError("");
    try {
      const [year, m] = month.split("-").map(Number);
      onChange(await api<Generation>("/admin/scheduling/generation", "POST", { year, month: m }));
    } catch (e) { setError(message(e)); } finally { setBusy(false); }
  }
  async function cancel() {
    if (!value) return;
    setBusy(true); setError("");
    try { await api(`/admin/scheduling/generation/${value.id}`, "DELETE"); onChange(null); }
    catch (e) { setError(message(e)); } finally { setBusy(false); }
  }
  async function save() {
    if (!value || !window.confirm("לשמור את הסידור כטיוטה שלא פורסמה?")) return;
    setBusy(true); setError("");
    try { onChange(await api<Generation>(`/admin/scheduling/generation/${value.id}/save`, "POST")); onSaved(); }
    catch (e) { setError(message(e)); } finally { setBusy(false); }
  }
  const rows = definitions.filter(d => d.includeInAutomaticScheduling && d.requiredEmployees > 0);
  const summary = value?.summary;
  return <section className="panel automatic-generation" aria-label="סידור אוטומטי">
    <h2>סידור אוטומטי חודשי</h2>
    <label className="field">חודש ליצירת סידור<input type="month" min="0002-01" max="9998-12" value={month} disabled={busy || pending || value?.state === "READY"} onChange={e => setMonth(e.target.value)} /></label>
    <details><summary>סוגי משמרות ודרישות איוש להרצה ({rows.length})</summary>
      <ul>{rows.map(d => <li key={d.id}>{d.name}: {d.requiredEmployees} עובדים</li>)}</ul>
    </details>
    <p className="help">ההרצה משתמשת בהגדרות השמורות. התוצאה היא טיוטה בלבד; בחירת אחראי משמרת ובדיקות לפרסום עדיין נדרשות.</p>
    <button disabled={busy || disabled || !month || pending || value?.state === "READY"} onClick={() => void start()}>צור סידור אוטומטי</button>
    {pending && <p role="status">{value?.state === "QUEUED" ? "ממתין להרצה…" : "מחשב סידור ואילוצים… ניתן להמשיך לנווט בין השבועות."}</p>}
    {value?.state === "READY" && <p role="status">תצוגה מקדימה — השיבוצים טרם נשמרו</p>}
    {value?.state === "SAVED" && <p role="status">הסידור נשמר כטיוטה שלא פורסמה</p>}
    {value?.state === "TIMEOUT" && <p role="alert">תם זמן החישוב ללא פתרון. לא נשמרו שיבוצים; ניתן לנסות שוב.</p>}
    {value?.state === "FAILED" && <p role="alert">ההרצה נכשלה. {message(new Error(value.errorCode ?? "INTERNAL_ERROR"))}</p>}
    {value?.state === "CANCELED" && <p role="status">ההרצה בוטלה ללא שמירה</p>}
    {value?.solverStatus === "FEASIBLE" && <p>נמצא פתרון חוקי במסגרת מגבלת הזמן. לא הוכח שכל יעדי האופטימיזציה מיטביים.</p>}
    {summary && <>
      <p aria-label="סיכום איוש">נדרשים: {summary.required} · שובצו: {summary.filled} · חסרים: {summary.missing} · תאים בחוסר: {summary.understaffedCells} · תאים מאוישים: {summary.fullyStaffedCells}</p>
      <p>{value?.coverageOptimal ? "הוכח מספר השיבוצים המרבי בהתאם לאילוצים ולשיבוצים המוגנים." : "ייתכן שניתן לצמצם חוסרים בהרצה נוספת."}</p>
      <label><input type="checkbox" checked={shortagesOnly} onChange={e => setShortagesOnly(e.target.checked)} />הצגת רשימת תאים בחוסר</label>
      {shortagesOnly && <div className="shortage-list">{value?.cells.filter(c => c.shortage > 0).map(c => <p key={`${c.definitionId}-${c.date}`}>
        {definitions.find(d => d.id === c.definitionId)?.name} · {c.date} · {c.shortage === 1 ? "חסר עובד 1" : `חסרים ${c.shortage} עובדים`}
      </p>)}</div>}
    </>}
    {value?.state === "READY" && <button className="primary" disabled={busy} onClick={() => void save()}>אישור ושמירת טיוטה</button>}
    {value && value.state !== "SAVED" && <button disabled={busy} onClick={() => void cancel()}>ביטול תצוגה מקדימה</button>}
    {error && <p role="alert">{error}</p>}
  </section>;
}
