import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { Notice } from "../components/forms";
import AvailabilityCalendar from "../components/AvailabilityCalendar";
import {
  av,
  instant,
  isWindowOpen,
  monthLabel,
  selectionsMap,
  weekCounts,
  type Availability as AvailabilityData,
  type Period,
  type Preference,
  type Selection,
} from "../availability";

export default function Availability({ targetId }: { targetId?: string }) {
  const base = targetId ? `/team-availability/employees/${targetId}` : "/availability";
  const [periods, setPeriods] = useState<Period[]>([]);
  const [selected, setSelected] = useState("");
  const [data, setData] = useState<AvailabilityData | null>(null);
  const [choices, setChoices] = useState<Record<string, Preference>>({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [now, setNow] = useState(Date.now());
  const saved = data ? selectionsMap(data.selections) : {};
  const normalized = (value: Record<string, Preference>) =>
    JSON.stringify(
      Object.entries(value)
        .filter(([, state]) => state !== "AVAILABLE")
        .sort(),
    );
  const dirty = !!data && normalized(saved) !== normalized(choices);
  const busyRef = useRef(false);
  const generationRef = useRef(0);
  const dirtyRef = useRef(dirty);
  dirtyRef.current = dirty;
  useEffect(() => {
    let alive = true;
    api<Period[]>(`${base}/periods`)
      .then((rows) => {
        if (alive) {
          setPeriods(rows);
          if (rows.length) setSelected(`${rows[0].year}/${rows[0].month}`);
        }
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
  }, [base]);
  useEffect(() => {
    if (!selected) return;
    let alive = true;
    setLoading(true);
    setData(null);
    setError("");
    setSuccess("");
    api<AvailabilityData>(`${base}/${selected}`)
      .then((value) => {
        if (alive) {
          setData(value);
          setChoices(selectionsMap(value.selections));
        }
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
  }, [selected, base]);
  useEffect(() => {
    if (!selected) return;
    let alive = true;
    const tick = window.setInterval(() => setNow(Date.now()), 1000);
    const poll = window.setInterval(() => {
      if (busyRef.current) return;
      const generation = generationRef.current;
      api<AvailabilityData>(`${base}/${selected}`)
        .then((value) => {
          if (!alive || busyRef.current || generation !== generationRef.current)
            return;
          if (dirtyRef.current)
            setData((old) =>
              old ? { ...old, window: value.window, weeks: value.weeks } : old,
            );
          else {
            setData(value);
            setChoices(selectionsMap(value.selections));
          }
        })
        .catch((e) => {
          if (alive) setError(message(e));
        });
    }, 15000);
    return () => {
      alive = false;
      window.clearInterval(poll);
      window.clearInterval(tick);
    };
  }, [selected, base]);
  useEffect(() => {
    if (!dirty) return;
    const unload = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    const link = (e: MouseEvent) => {
      if (
        (e.target as Element).closest("a[href]") &&
        !window.confirm(av.discard)
      ) {
        e.preventDefault();
        e.stopPropagation();
      }
    };
    window.addEventListener("beforeunload", unload);
    document.addEventListener("click", link, true);
    return () => {
      window.removeEventListener("beforeunload", unload);
      document.removeEventListener("click", link, true);
    };
  }, [dirty]);
  const open = !!data && (!!targetId || isWindowOpen(data.window, now));
  const overflow =
    data && Object.values(weekCounts(data, choices)).some((count) => count > 3);
  async function submit() {
    if (!data || busy) return;
    setBusy(true);
    busyRef.current = true;
    generationRef.current++;
    setError("");
    setSuccess("");
    const selections: Selection[] = Object.entries(choices).map(
      ([key, state]) => {
        const [date, shiftType] = key.split("/");
        return { date, shiftType: shiftType as Selection["shiftType"], state };
      },
    );
    try {
      const value = await api<AvailabilityData>(
        `${base}/${selected}`,
        data.submitted ? "PUT" : "POST",
        { expectedVersion: data.version, selections },
      );
      setData(value);
      setChoices(selectionsMap(value.selections));
      setSuccess(data.submitted ? av.updated : av.success);
    } catch (e) {
      setError(message(e));
      if (e instanceof Error && e.message === "WINDOW_CLOSED")
        setData((old) =>
          old ? { ...old, window: { ...old.window, status: "CLOSED" } } : old,
        );
    } finally {
      busyRef.current = false;
      setBusy(false);
      setNow(Date.now());
    }
  }
  return (
    <section dir="rtl">
      <div className="page-heading">
        <div>
          <span className="eyebrow">{he.workspace}</span>
          <h1>{targetId ? "עריכת זמינות עובד" : av.title}</h1>
          {targetId && <p>ניתן לערוך גם כשהחלון סגור. השינויים נשמרים רק בהגשה מפורשת ומתועדים בהיסטוריה.</p>}
          <p className="muted">{av.intro}</p>
        </div>
        {!!periods.length && (
          <label className="field">
            {av.month}
            <select
              value={selected}
              disabled={busy}
              onChange={(e) => {
                if (!dirty || window.confirm(av.discard))
                  setSelected(e.target.value);
              }}
            >
              {periods.map((p) => (
                <option key={p.id} value={`${p.year}/${p.month}`}>
                  {monthLabel(p.year, p.month)}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <Notice error={error} success={success} />
      {loading ? (
        <p role="status">{he.loading}</p>
      ) : !data ? (
        <p className="panel empty">{av.empty}</p>
      ) : (
        <>
          <div className="panel availability-summary">
            <div>
              <strong>{av.statuses[data.window.status]}</strong>
              <p>
                {av.opens}: <bdi>{instant(data.window.opensAt)}</bdi> ·{" "}
                {av.closes}: <bdi>{instant(data.window.closesAt)}</bdi>
              </p>
              <small>{av.timezone}</small>
            </div>
            <div>
              <strong>{data.submitted ? av.submitted : av.notSubmitted}</strong>
              <p>
                {av.last}: <bdi>{instant(data.lastSubmittedAt)}</bdi>
              </p>
              {dirty && (
                <span className="unsaved" role="status">
                  {av.unsaved}
                </span>
              )}
            </div>
          </div>
          <p className="availability-help">{av.limit}</p>
          {!open && <p className="notice">{av.readOnly}</p>}
          {overflow && (
            <p className="notice error" role="alert">
              {av.overflow}
            </p>
          )}
          <AvailabilityCalendar
            data={data}
            choices={choices}
            readOnly={!open || busy}
            onChange={(key, state) => {
              setChoices((old) => ({ ...old, [key]: state }));
              setSuccess("");
            }}
          />
          <div className="availability-actions">
            <button
              className="primary"
              disabled={!open || busy || !!overflow}
              onClick={submit}
            >
              {busy ? he.saving : data.submitted ? av.update : av.submit}
            </button>
            {dirty && (
              <button
                disabled={busy}
                onClick={() => {
                  if (window.confirm(av.discard)) {
                    setChoices(saved);
                    setSuccess("");
                  }
                }}
              >
                {av.reset}
              </button>
            )}
            <span className="muted">
              {dirty
                ? av.unsaved
                : data.submitted
                  ? av.submitted
                  : av.notSubmitted}
            </span>
          </div>
        </>
      )}
    </section>
  );
}
