import { useState } from "react";
import { reasons, weekdays, type AbsencePattern } from "../absences";
import { Notice } from "./forms";

export default function AbsenceForm({ initial, busy, submit, cancel }: {
  initial?: AbsencePattern; busy: boolean; submit: (pattern: AbsencePattern) => void; cancel?: () => void;
}) {
  const today = new Intl.DateTimeFormat("sv-SE", { timeZone: "Asia/Jerusalem" }).format(new Date());
  const [p, setP] = useState<AbsencePattern>(initial ?? { reason: "STUDIES", startDate: today, endDate: today, recurring: false,
    weekdays: [], fullDay: true, startTime: null, endTime: null, explanation: "" });
  const [error, setError] = useState("");
  function patch(value: Partial<AbsencePattern>) { setP(old => ({ ...old, ...value })); setError(""); }
  return <form className="panel absence-form" aria-label={initial ? "בקשת שינוי היעדרות" : "בקשת היעדרות חדשה"} onSubmit={e => {
    e.preventDefault();
    if (p.endDate && p.endDate < p.startDate || p.recurring && !p.weekdays.length || !p.fullDay && (!p.startTime || !p.endTime || p.startTime === p.endTime)) {
      setError("יש לבדוק את טווח התאריכים, ימי השבוע והשעות."); return;
    }
    submit(p);
  }}><h2>{initial ? "בקשת שינוי היעדרות" : "בקשת היעדרות חדשה"}</h2>
    <Notice error={error} /><fieldset disabled={busy}>
      <label className="field">סוג היעדרות<select value={p.recurring ? "weekly" : "once"} onChange={e => { const recurring = e.target.value === "weekly"; patch({ recurring, weekdays: [], endDate: recurring ? null : p.endDate ?? p.startDate }); }}><option value="once">חד־פעמית</option><option value="weekly">חוזרת מדי שבוע</option></select></label>
      <label className="field">סיבת ההיעדרות<select value={p.reason} onChange={e => patch({ reason: e.target.value })}>{Object.entries(reasons).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <div className="absence-fields">
        <label className="field">תאריך התחלה<input required type="date" min="0002-01-01" max="9998-12-30" value={p.startDate} onChange={e => patch({ startDate: e.target.value })} /></label>
        <label className="field">{p.recurring ? "תאריך סיום (רשות)" : "תאריך סיום"}<input required={!p.recurring} type="date" min={p.startDate} max="9998-12-30" value={p.endDate ?? ""} onChange={e => patch({ endDate: e.target.value || null })} /></label>
      </div>
      <p className="help">תאריכי ההתחלה והסיום כלולים. השעות לפי שעון ישראל. חזרה שבועית ללא תאריך סיום תמשיך גם בחודשים הבאים.</p>
      {p.recurring && <fieldset><legend>ימי שבוע</legend><div className="absence-weekdays">{weekdays.map((day, index) => <label className="check" key={day}><input type="checkbox" checked={p.weekdays.includes(index)} onChange={e => patch({ weekdays: (e.target.checked ? [...p.weekdays, index] : p.weekdays.filter(d => d !== index)).sort() })} />{day}</label>)}</div></fieldset>}
      <label className="check"><input type="checkbox" checked={p.fullDay} onChange={e => patch({ fullDay: e.target.checked, startTime: null, endTime: null })} />היעדרות ביום מלא</label>
      {!p.fullDay && <><div className="absence-fields"><label className="field">משעה<input required type="time" value={p.startTime ?? ""} onChange={e => patch({ startTime: e.target.value || null })} /></label><label className="field">עד שעה<input required type="time" value={p.endTime ?? ""} onChange={e => patch({ endTime: e.target.value || null })} /></label></div><p className="help">שעת סיום מוקדמת משעת ההתחלה מציינת סיום ביום הבא. הטווח חל בכל אחד מהימים שנבחרו.</p></>}
      <label className="field">הסבר (רשות)<textarea maxLength={2000} value={p.explanation ?? ""} onChange={e => patch({ explanation: e.target.value || null })} /></label>
      {initial && <p className="notice">ההיעדרות המאושרת נשארת בתוקף עד לאישור השינוי.</p>}
      <div className="form-actions"><button className="primary" type="submit">{busy ? "שולח…" : initial ? "שליחת בקשת שינוי" : "שליחת בקשה"}</button>{cancel && <button type="button" onClick={cancel}>חזרה</button>}</div>
    </fieldset>
  </form>;
}
