import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { Notice } from "../components/forms";
import AvailabilityCalendar from "../components/AvailabilityCalendar";
import {
  av,
  instant,
  israelInstant,
  localInput,
  monthLabel,
  selectionsMap,
  type Availability,
  type WindowSummary,
} from "../availability";

export default function AvailabilityAdmin() {
  const [month, setMonth] = useState(() =>
    localInput(new Date().toISOString()).slice(0, 7),
  );
  const [summary, setSummary] = useState<WindowSummary | null>(null);
  const [opens, setOpens] = useState("");
  const [closes, setCloses] = useState("");
  const [detail, setDetail] = useState<Availability | null>(null);
  const [employeeName, setEmployeeName] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const path = `/admin/availability/${Number(month.slice(0, 4))}/${Number(month.slice(5))}`;
  function apply(value: WindowSummary) {
    setSummary(value);
    setOpens(localInput(value.window.opensAt));
    setCloses(localInput(value.window.closesAt));
  }
  useEffect(() => {
    let alive = true;
    setLoading(true);
    setSummary(null);
    setDetail(null);
    setError("");
    setSuccess("");
    if (!/^\d{4}-\d{2}$/.test(month)) {
      setLoading(false);
      return;
    }
    api<WindowSummary>(path)
      .then((value) => {
        if (alive) apply(value);
      })
      .catch((e) => {
        if (alive) setError(message(e));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [path, month]);
  async function write(state?: boolean) {
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      let value: WindowSummary;
      if (state === undefined) {
        const opensAt = israelInstant(opens),
          closesAt = israelInstant(closes);
        if (closesAt <= opensAt) {
          setError(av.invalidTime);
          return;
        }
        value = await api<WindowSummary>(path, "PUT", { opensAt, closesAt });
      } else
        value = await api<WindowSummary>(`${path}/state`, "PUT", {
          isOpen: state,
        });
      apply(value);
      setSuccess(
        state === undefined ? av.configured : state ? av.opened : av.closed,
      );
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  async function refresh() {
    setBusy(true);
    setError("");
    try {
      apply(await api<WindowSummary>(path));
      setDetail(null);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  async function view(userId: string, name: string) {
    setBusy(true);
    setError("");
    setDetail(null);
    try {
      setDetail(await api<Availability>(`${path}/employees/${userId}`));
      setEmployeeName(name);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  function configure(e: FormEvent) {
    e.preventDefault();
    void write();
  }
  return (
    <section dir="rtl">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{he.workspace}</span>
          <h1>{av.adminTitle}</h1>
          <p className="muted">{av.adminHelp}</p>
        </div>
        <label className="field">
          {av.month}
          <input
            type="month"
            value={month}
            min="0002-01"
            max="9998-12"
            disabled={busy}
            onChange={(e) => setMonth(e.target.value)}
          />
        </label>
      </div>
      <Notice error={error} success={success} />
      {loading ? (
        <p role="status">{he.loading}</p>
      ) : (
        summary && (
          <>
            <div className="panel">
              <div className="page-heading">
                <h2>{monthLabel(summary.year, summary.month)}</h2>
                <strong>{av.statuses[summary.window.status]}</strong>
              </div>
              <form onSubmit={configure}>
                <div className="form-row">
                  <label className="field">
                    {av.opens}
                    <input
                      type="datetime-local"
                      value={opens}
                      required
                      disabled={busy}
                      onChange={(e) => setOpens(e.target.value)}
                    />
                  </label>
                  <label className="field">
                    {av.closes}
                    <input
                      type="datetime-local"
                      value={closes}
                      required
                      disabled={busy}
                      onChange={(e) => setCloses(e.target.value)}
                    />
                  </label>
                </div>
                <p className="help">{av.timezone}</p>
                <div className="form-actions">
                  <button className="primary" disabled={busy}>
                    {av.configure}
                  </button>
                  <button
                    type="button"
                    disabled={
                      busy || summary.window.status === "NOT_CONFIGURED"
                    }
                    onClick={() =>
                      write(summary.window.administrativeState !== "OPEN")
                    }
                  >
                    {summary.window.administrativeState === "OPEN"
                      ? av.close
                      : av.open}
                  </button>
                  <button type="button" onClick={refresh} disabled={busy}>
                    {av.refresh}
                  </button>
                </div>
              </form>
            </div>
            <div className="panel availability-roster">
              <h2>
                {summary.submittedCount} {av.submittedCount}{" "}
                {summary.eligibleCount} {av.eligible}
              </h2>
              {!summary.employees.length ? (
                <p>{av.noEmployees}</p>
              ) : (
                <div className="table-scroll">
                  <table>
                    <thead>
                      <tr>
                        <th>{av.employee}</th>
                        <th>{av.status}</th>
                        <th>{av.last}</th>
                        <th>{he.actions}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {summary.employees.map((e) => (
                        <tr key={e.userId}>
                          <td>
                            {e.firstName} {e.lastName}
                          </td>
                          <td>{e.submitted ? "הוגש" : "לא הוגש"}</td>
                          <td>
                            <bdi>{instant(e.lastSubmittedAt)}</bdi>
                          </td>
                          <td>
                            <button
                              disabled={
                                busy ||
                                (summary.window.status === "NOT_CONFIGURED" &&
                                  !e.submitted)
                              }
                              onClick={() =>
                                view(e.userId, `${e.firstName} ${e.lastName}`)
                              }
                              aria-label={`${av.view} — ${e.firstName} ${e.lastName}`}
                            >
                              {av.view}
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
            {detail && (
              <div className="availability-detail">
                <h2>
                  {av.view} — {employeeName}
                </h2>
                <p>
                  {detail.submitted
                    ? `${av.last}: ${instant(detail.lastSubmittedAt)}`
                    : av.noSubmission}
                </p>
                <AvailabilityCalendar
                  data={detail}
                  choices={selectionsMap(detail.selections)}
                  readOnly
                />
              </div>
            )}
          </>
        )
      )}
    </section>
  );
}
