import { useEffect, useState } from "react";
import { api, type User, type UserPage } from "../api";
import { he, message } from "../he";
import { instant, monthLabel, av } from "../availability";
import { Notice } from "../components/forms";
import type { Team, AuditEvent, RosterConflict } from "../teams";
function TeamEditor({ team, save, assign, busy, users }: { team: Team; save: (team: Team) => void; assign: (userId: string) => Promise<void>; busy: boolean; users: User[] }) {
  const administrators = users.filter(u => u.roles.includes("ADMIN")).map(u => u.id);
  const [member, setMember] = useState("");
  const available = users.filter(u => !administrators.includes(u.id) && !team.members.some(m => m.id === u.id));
  const [draft, setDraft] = useState(team);
  useEffect(() => setDraft(team), [team]);
  const leadership = (label: string, field: "leaderUserId" | "deputyUserId") => <label className="field">{label}<select value={draft[field] ?? ""} onChange={e => setDraft({ ...draft, [field]: e.target.value || null })}>
    <option value="">ללא מינוי</option>{draft[field] && administrators.includes(draft[field]!) && <option value={draft[field]!} disabled>מינוי מנהל היסטורי — נדרש חשבון נפרד</option>}{team.members.filter(m => !administrators.includes(m.id) && m.isActive && (field !== "deputyUserId" || m.isEmployee)).map(m => <option key={m.id} value={m.id}>{m.firstName} {m.lastName}</option>)}
  </select></label>;
  return <section className="panel" aria-label={team.name}><form onSubmit={e => { e.preventDefault(); save(draft); }}><fieldset disabled={busy}>
    <legend>{team.name}</legend><label className="field">שם הצוות<input required maxLength={100} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
    {leadership("ראש צוות", "leaderUserId")}{leadership("סגן ראש צוות", "deputyUserId")}
    <p>מנהל המערכת וראש הצוות הם חשבונות נפרדים. מנהל מערכת אינו משויך לצוות; סוג העובד ״מנהל״ אינו מעניק הרשאת מנהל מערכת. ראש הצוות אינו משתתף בסידור האוטומטי. שיבוץ הסגן כפוף לסוג העובד; כשירות לאחמ״ש נפרדת מהמינוי.</p>
    <ul>{team.members.map(m => <li key={m.id}>{m.firstName} {m.lastName}{m.id === team.leaderUserId ? " — ראש צוות" : m.id === team.deputyUserId ? " — סגן ראש צוות" : ""}</li>)}</ul>
    <button type="submit" className="primary">שמירת הצוות</button></fieldset></form>
    <form onSubmit={async e => { e.preventDefault(); await assign(member); }}>
      <label className="field">הוספת עובד לצוות<select required disabled={busy} value={member} onChange={e => setMember(e.target.value)}>
        <option value="">בחרו עובד קיים</option>{available.map(u => <option key={u.id} value={u.id}>{u.firstName} {u.lastName}{u.isActive === false ? " — לא פעיל" : ""}</option>)}
      </select></label>
      <button className="primary" disabled={busy || !available.some(u => u.id === member)}>שמירת שיוך לצוות</button>
    </form></section>;
}
function auditValues(raw: string) {
  const values = JSON.parse(raw) as { Date: string; ShiftType: "DAY" | "NIGHT"; State: "AVAILABLE" | "PREFERRED" | "BLOCKED" }[];
  return values.length ? values.map(v => `${v.Date} ${v.ShiftType === "DAY" ? "יום" : "לילה"}: ${av.states[v.State]}`).join("; ") : "ללא העדפות או חסימות";
}
export default function Teams() {
  const [teams, setTeams] = useState<Team[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [audit, setAudit] = useState<AuditEvent[]>([]);
  const [page, setPage] = useState(1);
  const [conflicts, setConflicts] = useState<RosterConflict[]>([]);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [member, setMember] = useState("");
  const [destination, setDestination] = useState("");
  async function load() {
    const [t, a, c] = await Promise.all([api<Team[]>("/admin/teams"), api<AuditEvent[]>(`/admin/teams/audit?page=${page}`), api<RosterConflict[]>("/admin/teams/conflicts")]);
    setTeams(t); setAudit(a); setConflicts(c);
  }
  useEffect(() => {
    let alive = true;
    let directoryRequest = 0;
    async function init() {
      const request = ++directoryRequest;
      const all: User[] = []; let p = 1; let result: UserPage;
      do { result = await api<UserPage>(`/users?page=${p++}&pageSize=100`); all.push(...result.items); } while (all.length < result.totalCount);
      if (alive && request === directoryRequest) setUsers(all);
    }
    Promise.all([load(), init()]).catch(e => { if (alive) setError(message(e)); }).finally(() => { if (alive) setLoading(false); });
    const refreshDirectory = () => {
      if (!document.hidden) void init().catch(e => { if (alive) setError(message(e)); });
    };
    window.addEventListener("focus", refreshDirectory);
    document.addEventListener("visibilitychange", refreshDirectory);
    return () => { alive = false; window.removeEventListener("focus", refreshDirectory); document.removeEventListener("visibilitychange", refreshDirectory); };
  }, [page]);
  async function mutate(action: () => Promise<unknown>) {
    setBusy(true); setError(""); setSuccess("");
    try { await action(); await load(); setSuccess("השינויים נשמרו."); }
    catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  const name = (id: string) => { const u = users.find(u => u.id === id); return u ? `${u.firstName} ${u.lastName}` : id; };
  return <section dir="rtl"><h1>ניהול צוותים</h1><Notice error={error} success={success} />
    {loading && <p role="status">{he.loading}</p>}
    {conflicts.length > 0 && <div className="notice error" role="alert"><h2>סתירות בסידור הדורשות טיפול</h2><ul>{conflicts.map(c => <li key={`${c.assignmentId}-${c.code}`}>{c.date} · {name(c.userId)} · {c.code === "TEAM_LEADER_ASSIGNED" ? "ראש צוות משובץ" : "שיבוץ בזמן חסימה"} · מזהה שיבוץ: <bdi>{c.assignmentId}</bdi></li>)}</ul></div>}
    {teams.map(t => <TeamEditor key={t.id} team={t} users={users} assign={userId => mutate(() => api(`/admin/teams/members/${userId}`, "PUT", { teamId: t.id }))} busy={busy} save={draft => mutate(() => api(`/admin/teams/${t.id}`, "PUT", { name: draft.name, leaderUserId: draft.leaderUserId, deputyUserId: draft.deputyUserId, expectedVersion: draft.version }))} />)}
    <form className="panel" onSubmit={e => { e.preventDefault(); mutate(() => api(`/admin/teams/members/${member}`, "PUT", { teamId: destination || null })); }}><h2>שיוך והעברת עובדים</h2>
      <p>העברה או הסרה של ראש צוות או סגן מבטלת את המינוי הקודם. חשבונות בעלי הרשאת מנהל מערכת אינם ניתנים לשיוך לצוות.</p>
      <label className="field">עובד<select required disabled={busy} value={member} onChange={e => { setMember(e.target.value); if (users.find(u => u.id === e.target.value)?.roles.includes("ADMIN")) setDestination(""); }}><option value="">בחרו עובד</option>{users.filter(u => !u.roles.includes("ADMIN") || teams.some(t => t.members.some(m => m.id === u.id))).map(u => <option key={u.id} value={u.id}>{name(u.id)} · {teams.find(t => t.members.some(m => m.id === u.id))?.name ?? "ללא צוות"}</option>)}</select></label>
      <label className="field">צוות יעד<select disabled={busy} value={destination} onChange={e => setDestination(e.target.value)}><option value="">ללא צוות — הסרת שיוך</option>{teams.map(t => <option key={t.id} value={t.id} disabled={users.find(u => u.id === member)?.roles.includes("ADMIN")}>{t.name}</option>)}</select></label>
      <button className="primary" disabled={busy || !member}>שמירת שיוך</button></form>
    <div className="panel"><h2>היסטוריית שינויי זמינות</h2>{!audit.length && <p>אין אירועים להצגה.</p>}
      {audit.map(a => <article key={a.id}><h3>{instant(a.occurredAt)} · {a.initialSubmission ? "הגשה ראשונה" : "עדכון"}</h3><p>בוצע על ידי {name(a.actorUserId)} עבור {name(a.targetUserId)} · {teams.find(t => t.id === a.teamId)?.name}</p><p>תקופת סידור: {monthLabel(a.year, a.month)}</p><p>לפני: {auditValues(a.previousValues)}</p><p>אחרי: {auditValues(a.newValues)}</p></article>)}
      <button disabled={page === 1 || busy} onClick={() => setPage(p => p - 1)}>הקודם</button><span> עמוד {page} </span><button disabled={audit.length < 50 || busy} onClick={() => setPage(p => p + 1)}>הבא</button>
    </div>
  </section>;
}
