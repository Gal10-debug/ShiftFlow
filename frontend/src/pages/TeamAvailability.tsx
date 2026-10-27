import { useEffect, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { av } from "../availability";
import type { Team } from "../teams";
import { Notice } from "../components/forms";
import Availability from "./Availability";
export default function TeamAvailability() {
  const [teams, setTeams] = useState<Team[]>([]);
  const [target, setTarget] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let alive = true;
    api<Team[]>("/team-availability/members").then(t => { if (alive) setTeams(t); })
      .catch(e => { if (alive) setError(message(e)); }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);
  return <section dir="rtl"><h1>זמינות הצוות</h1><Notice error={error} />
    {loading ? <p role="status">{he.loading}</p> : !error && <label className="field">בחירת עובד
      <select value={target} onChange={e => { if (!target || window.confirm(av.discard)) setTarget(e.target.value); }}>
        <option value="">בחרו עובד</option>
        {teams.map(t => <optgroup key={t.id} label={t.name}>{t.members.filter(m => m.isActive && m.isEmployee && m.id !== t.leaderUserId).map(m => <option key={m.id} value={m.id}>{m.firstName} {m.lastName}</option>)}</optgroup>)}
      </select></label>}
    {target && <Availability key={target} targetId={target} />}
  </section>;
}
