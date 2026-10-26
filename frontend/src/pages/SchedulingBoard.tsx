import { useEffect, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { Notice } from "../components/forms";
import {
  scheduling as s,
  displayBoardDate,
  moveWeek,
  validDefinitions,
  type ShiftTypeDefinition,
  type WeeklyBoard,
} from "../scheduling";

export default function SchedulingBoard() {
  const [requestedDate, setRequestedDate] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [board, setBoard] = useState<WeeklyBoard | null>(null);
  const [baseline, setBaseline] = useState<ShiftTypeDefinition[]>([]);
  const [drafts, setDrafts] = useState<ShiftTypeDefinition[]>([]);
  const [editing, setEditing] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const dirty = JSON.stringify(drafts) !== JSON.stringify(baseline);
  const path = `/admin/scheduling/board${requestedDate ? `?date=${encodeURIComponent(requestedDate)}` : ""}`;
  function apply(value: WeeklyBoard) {
    setBoard(value);
    const definitions = value.rows.map((row) => row.definition);
    setBaseline(definitions);
    setDrafts(definitions);
  }
  useEffect(() => {
    let alive = true;
    setLoading(true);
    setError("");
    setSuccess("");
    setBoard(null);
    api<WeeklyBoard>(path)
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
  }, [path, reload]);
  useEffect(() => {
    if (!dirty) return;
    const unload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    const link = (event: MouseEvent) => {
      if (
        (event.target as Element).closest("a[href]") &&
        !window.confirm(s.discard)
      ) {
        event.preventDefault();
        event.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", unload);
    document.addEventListener("click", link, true);
    return () => {
      window.removeEventListener("beforeunload", unload);
      document.removeEventListener("click", link, true);
    };
  }, [dirty]);
  function navigate(date: string | null) {
    if (dirty && !window.confirm(s.discard)) return;
    setDrafts(baseline);
    setRequestedDate(date);
    setReload((n) => n + 1);
  }
  function change(id: string, patch: Partial<ShiftTypeDefinition>) {
    setDrafts((rows) =>
      rows.map((row) => (row.id === id ? { ...row, ...patch } : row)),
    );
    setSuccess("");
  }
  async function save() {
    setError("");
    setSuccess("");
    if (!validDefinitions(drafts)) {
      setError(s.invalid);
      return;
    }
    setBusy(true);
    try {
      const saved = await api<ShiftTypeDefinition[]>(
        "/admin/scheduling/shift-types",
        "PUT",
        {
          shiftTypes: drafts.map((row) => ({
            id: row.id,
            expectedVersion: row.version,
            name: row.name,
            displayOrder: row.displayOrder,
            period: row.period,
            requiredEmployees: row.requiredEmployees,
            includeInAutomaticScheduling: row.includeInAutomaticScheduling,
          })),
        },
      );
      setBaseline(saved);
      setDrafts(saved);
      setSuccess(s.saved);
      // Re-fetch canonical cell boundaries after a Day/Night change. A failed read must
      // not present stale board cells or discard the successful save's versions.
      setBoard(null);
      setLoading(true);
      try {
        apply(await api<WeeklyBoard>(path));
      } catch (e) {
        setError(message(e));
      } finally {
        setLoading(false);
      }
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <section dir="rtl" className="scheduling-page">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{s.unpublished}</span>
          <h1>{s.title}</h1>
          <p className="muted">{s.intro}</p>
        </div>
        <button
          disabled={busy || loading || !baseline.length}
          onClick={() => {
            if (editing && dirty) {
              if (!window.confirm(s.discard)) return;
              setDrafts(baseline);
            }
            setEditing((value) => !value);
          }}
        >
          {editing ? s.hide : s.configure}
        </button>
      </div>
      <p className="notice scheduling-preparation">{s.preparation}</p>
      <Notice error={error} success={success} />
      <div className="schedule-navigation">
        <button
          disabled={!board || busy || loading}
          onClick={() => navigate(moveWeek(board!.weekStartsOn, -1))}
        >
          {s.previous}
        </button>
        <button disabled={busy || loading} onClick={() => navigate(null)}>
          {s.current}
        </button>
        <button
          disabled={!board || busy || loading}
          onClick={() => navigate(moveWeek(board!.weekStartsOn, 1))}
        >
          {s.next}
        </button>
        <label className="field">
          {s.date}
          <input
            type="date"
            min="0002-01-08"
            max="9998-12-24"
            value={board?.weekStartsOn ?? requestedDate ?? ""}
            disabled={busy || loading}
            onChange={(e) => {
              if (e.target.value) navigate(e.target.value);
            }}
          />
        </label>
        <button
          disabled={busy || loading}
          onClick={() => navigate(requestedDate)}
        >
          {s.refresh}
        </button>
      </div>
      <p className="muted">{s.timezone}</p>
      {loading ? (
        <p role="status">{he.loading}</p>
      ) : (
        board &&
        (board.rows.length ? (
          <>
            <h2 className="schedule-range">
              <bdi>
                {displayBoardDate(board.weekStartsOn)} –{" "}
                {displayBoardDate(board.weekEndsOn)}
              </bdi>
            </h2>
            <p className="help" id="board-scroll-help">
              {s.scrolling}
            </p>
            <div
              className="schedule-scroll"
              tabIndex={0}
              role="region"
              aria-label={s.grid}
              aria-describedby="board-scroll-help"
            >
              <table className="schedule-grid">
                <caption className="sr-only">
                  {s.grid} — {s.unpublished}
                </caption>
                <thead>
                  <tr>
                    <th scope="col" className="schedule-row-label">
                      {s.rowHeader}
                    </th>
                    {board.days.map((day, i) => (
                      <th scope="col" key={day.date}>
                        <span className="schedule-weekday">
                          {s.weekdays[i]}
                        </span>
                        <time dateTime={day.date}>
                          {displayBoardDate(day.date)}
                        </time>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {board.rows.map((row) => (
                    <tr key={row.definition.id}>
                      <th scope="row" className="schedule-row-label">
                        <strong>{row.definition.name}</strong>
                        <small>{s.periods[row.definition.period]}</small>
                        <small>
                          {row.definition.requiredEmployees} {s.workers} ·{" "}
                          {row.definition.includeInAutomaticScheduling &&
                          row.definition.requiredEmployees > 0
                            ? s.selected
                            : s.excluded}
                        </small>
                      </th>
                      {row.cells.map((cell) => (
                        <td key={cell.date}>
                          <span
                            className={
                              cell.state === "NOT_REQUIRED"
                                ? "schedule-not-required"
                                : "schedule-unassigned"
                            }
                          >
                            {cell.state === "NOT_REQUIRED"
                              ? s.noStaffing
                              : s.unassigned}
                          </span>
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <p className="panel empty">{s.empty}</p>
        ))
      )}
      {editing && !!drafts.length && (
        <section className="panel shift-type-editor" aria-label={s.configure}>
          <h2>{s.configure}</h2>
          <p>{s.configureHelp}</p>
          <p className="muted">{s.untouched}</p>
          <form
            noValidate
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
          >
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>{s.name}</th>
                    <th>{s.order}</th>
                    <th>{s.period}</th>
                    <th>{s.staffing}</th>
                    <th>{s.automatic}</th>
                  </tr>
                </thead>
                <tbody>
                  {drafts.map((row) => (
                    <tr key={row.id}>
                      <td>
                        <input
                          aria-label={`${s.name} ${row.slot}`}
                          value={row.name}
                          maxLength={100}
                          required
                          disabled={busy || loading}
                          onChange={(e) =>
                            change(row.id, { name: e.target.value })
                          }
                        />
                      </td>
                      <td>
                        <input
                          aria-label={`${s.order} ${row.slot}`}
                          type="number"
                          min={1}
                          max={51}
                          step={1}
                          value={
                            Number.isFinite(row.displayOrder)
                              ? row.displayOrder
                              : ""
                          }
                          disabled={busy || loading}
                          onChange={(e) =>
                            change(row.id, {
                              displayOrder:
                                e.target.value === ""
                                  ? NaN
                                  : Number(e.target.value),
                            })
                          }
                        />
                      </td>
                      <td>
                        <select
                          aria-label={`${s.period} ${row.slot}`}
                          value={row.period}
                          disabled={busy || loading}
                          onChange={(e) =>
                            change(row.id, {
                              period: e.target
                                .value as ShiftTypeDefinition["period"],
                            })
                          }
                        >
                          <option value="DAY">{s.periods.DAY}</option>
                          <option value="NIGHT">{s.periods.NIGHT}</option>
                        </select>
                      </td>
                      <td>
                        <input
                          aria-label={`${s.staffing} ${row.slot}`}
                          type="number"
                          min={0}
                          max={1000}
                          step={1}
                          value={
                            Number.isFinite(row.requiredEmployees)
                              ? row.requiredEmployees
                              : ""
                          }
                          disabled={busy || loading}
                          onChange={(e) =>
                            change(row.id, {
                              requiredEmployees:
                                e.target.value === ""
                                  ? NaN
                                  : Number(e.target.value),
                            })
                          }
                        />
                        {row.requiredEmployees === 0 && (
                          <small>{s.noStaffing}</small>
                        )}
                      </td>
                      <td>
                        <input
                          aria-label={`${s.automatic} ${row.slot}`}
                          type="checkbox"
                          checked={row.includeInAutomaticScheduling}
                          disabled={busy || loading}
                          onChange={(e) =>
                            change(row.id, {
                              includeInAutomaticScheduling: e.target.checked,
                            })
                          }
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="form-actions">
              <button className="primary" disabled={busy || loading || !dirty}>
                {busy ? he.saving : s.save}
              </button>
              <button
                type="button"
                disabled={busy || !dirty}
                onClick={() => {
                  if (window.confirm(s.discard)) setDrafts(baseline);
                }}
              >
                {s.reset}
              </button>
              {dirty && (
                <span className="unsaved" role="status">
                  {s.unsaved}
                </span>
              )}
            </div>
          </form>
        </section>
      )}
    </section>
  );
}
