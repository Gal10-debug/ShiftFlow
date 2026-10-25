import {
  av,
  daysInMonth,
  monthLabel,
  selectionKey,
  weekCounts,
  weekStart,
  type Availability,
  type Preference,
} from "../availability";
export default function AvailabilityCalendar({
  data,
  choices,
  onChange,
  readOnly,
}: {
  data: Availability;
  choices: Record<string, Preference>;
  onChange?: (key: string, value: Preference) => void;
  readOnly: boolean;
}) {
  const days = daysInMonth(data.year, data.month);
  const counts = weekCounts(data, choices);
  const weeks = [...new Set(days.map(weekStart))];
  const weekdays = ["ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת"];
  return (
    <div
      className="availability-calendar"
      aria-label={`לוח זמינות ${monthLabel(data.year, data.month)}`}
    >
      <div className="calendar-weekdays" aria-hidden="true">
        {weekdays.map((day) => (
          <span key={day}>{day}</span>
        ))}
      </div>
      {weeks.map((week) => (
        <section
          className="calendar-week"
          key={week}
          aria-label={`${av.week}${week}`}
        >
          <div
            className={`week-caption ${counts[week] > 3 ? "over-limit" : ""}`}
          >
            <strong>
              {av.week}
              {week.split("-").reverse().join("/")}
            </strong>
            <span>
              {counts[week]} {av.counter}
              {data.weeks.find((w) => w.startsOn === week)?.otherMonthBlocks
                ? ` · ${av.other}`
                : ""}
            </span>
          </div>
          <div className="calendar-days">
            {Array.from({ length: 7 }, (_, i) => {
              const d = new Date(`${week}T12:00:00Z`);
              d.setUTCDate(d.getUTCDate() + i);
              const date = d.toISOString().slice(0, 10);
              if (!days.includes(date))
                return (
                  <div
                    className="calendar-placeholder"
                    key={date}
                    aria-hidden="true"
                  />
                );
              return (
                <article className="calendar-day" key={date}>
                  <div className="day-heading">
                    <strong>{Number(date.slice(-2))}</strong>
                    <span>{weekdays[i]}</span>
                  </div>
                  {(["DAY", "NIGHT"] as const).map((type) => {
                    const key = selectionKey(date, type);
                    const state = choices[key] ?? "AVAILABLE";
                    const label = `${date.split("-").reverse().join("/")} ${av.shifts[type]}`;
                    return (
                      <label
                        className={`shift-choice state-${state.toLowerCase()}`}
                        key={type}
                      >
                        <span>{av.shifts[type]}</span>
                        <select
                          aria-label={label}
                          value={state}
                          disabled={readOnly}
                          onChange={(e) =>
                            onChange?.(key, e.target.value as Preference)
                          }
                        >
                          {(["AVAILABLE", "PREFERRED", "BLOCKED"] as const).map(
                            (value) => (
                              <option key={value} value={value}>
                                {av.states[value]}
                              </option>
                            ),
                          )}
                        </select>
                      </label>
                    );
                  })}
                </article>
              );
            })}
          </div>
        </section>
      ))}
    </div>
  );
}
