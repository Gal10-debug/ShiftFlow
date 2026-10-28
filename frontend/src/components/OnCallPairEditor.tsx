import { useEffect, useState } from "react";
import { api } from "../api";
import { message } from "../he";
import type { Candidate, Review } from "../publication";
import { Notice } from "./forms";
export default function OnCallPairEditor({
  review,
  base,
  onChanged
}: {
  review: Review;
  base: string;
  onChanged: () => void;
}) {
  const definition = review.draft.cells.find(c => c.semantic === "WEEKEND_ON_CALL");
  const [operation, setOperation] = useState("ASSIGN");
  const [friday, setFriday] = useState("");
  const [user, setUser] = useState("");
  const [assignment, setAssignment] = useState("");
  const [adjacent, setAdjacent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const assigned = review.draft.cells.filter(c => c.semantic === "WEEKEND_ON_CALL").flatMap(c => c.employees.map(e => ({
    ...e,
    date: c.date
  })));
  useEffect(() => {
    setCandidates([]);
    if (!definition || !friday) return;
    let alive = true;
    // The Saturday view may manage a Friday belonging to the preceding month.
    const day = new Date(friday + "T12:00:00Z");
    if (day.getUTCFullYear() !== review.draft.year || day.getUTCMonth() + 1 !== review.draft.month) day.setUTCDate(day.getUTCDate() + 1);
    const query = new URLSearchParams({
      definitionId: definition.definitionId,
      date: day.toISOString().slice(0, 10)
    });
    if (assignment) query.set("replacing", assignment);
    api<Candidate[]>(`${base}/candidates?${query}`).then(c => {
      if (alive) setCandidates(c);
    }).catch(e => {
      if (alive) setError(message(e));
    });
    return () => {
      alive = false;
    };
  }, [base, friday, assignment, definition?.definitionId]);
  if (!definition) return null;
  async function save() {
    if (!window.confirm("להחיל את הפעולה על זוג שישי ושבת? שינוי בחודש סמוך יבוצע רק אם אישרת אותו במפורש.")) return;
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      await api(`${base}/on-call-pair`, "POST", {
        expectedToken: review.token,
        operation,
        friday,
        userId: user || null,
        assignmentId: assignment || null,
        acknowledgeAdjacentPeriod: adjacent
      });
      setSuccess("זוג הכוננות נשמר בהצלחה.");
      setAdjacent(false);
      onChanged();
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  return <form onSubmit={e => {
    e.preventDefault();
    save();
  }} dir="rtl"><h3>עריכת זוג כוננות סוף שבוע</h3><Notice error={error} success={success} />
    <p>שישי ושבת נשמרים יחד לאותו עובד. הזוג נחשב הקצאה אחת בחודש של יום שישי. אין שינוי אוטומטי בחודש סמוך.</p>
    <fieldset disabled={busy}><label className="field">פעולת זוג<select value={operation} onChange={e => {
          setOperation(e.target.value);
          setAssignment("");
        }}><option value="ASSIGN">הוספת זוג</option><option value="REMOVE">הסרת זוג</option><option value="REPLACE">החלפת עובד בזוג</option><option value="MOVE">העברת זוג לסוף שבוע אחר</option></select></label>
    {operation !== "ASSIGN" && <label className="field">שיבוץ מקור בזוג<select required value={assignment} onChange={e => {
          const id = e.target.value;
          setAssignment(id);
          const source = assigned.find(a => a.assignmentId === id);
          if (source) {
            const day = new Date(source.date + "T12:00:00Z");
            if (day.getUTCDay() === 6) day.setUTCDate(day.getUTCDate() - 1);
            setFriday(day.toISOString().slice(0, 10));
          }
        }}><option value="">בחרו שיבוץ</option>{assigned.map(a => <option key={a.assignmentId} value={a.assignmentId}>{a.date} · {a.name}</option>)}</select></label>}
    <label className="field">יום שישי של הזוג<input required type="date" value={friday} onChange={e => setFriday(e.target.value)} /></label>
    {(operation === "ASSIGN" || operation === "REPLACE") && <label className="field">עובד לזוג<select required value={user} onChange={e => setUser(e.target.value)}><option value="">בחרו עובד</option>{candidates.map(c => <option key={c.userId} value={c.userId}>{c.name}</option>)}</select></label>}
    <label className="check"><input type="checkbox" checked={adjacent} onChange={e => setAdjacent(e.target.checked)} />אני מאשר במפורש שינוי של שני ימי הזוג גם בחודש סמוך</label>
    <button className="primary">שמירת זוג כוננות</button></fieldset></form>;
}
