import { formatDate } from "./he";
export type Preference = "AVAILABLE" | "PREFERRED" | "BLOCKED";
export type ShiftType = "DAY" | "NIGHT";
export interface AvailabilityWindow {
  status: "NOT_CONFIGURED" | "CLOSED" | "SCHEDULED" | "EXPIRED" | "OPEN";
  administrativeState: "OPEN" | "CLOSED";
  opensAt: string | null;
  closesAt: string | null;
}
export interface Selection {
  date: string;
  shiftType: ShiftType;
  state: Preference;
}
export interface Period {
  id: string;
  year: number;
  month: number;
  window: AvailabilityWindow;
  submitted: boolean;
  lastSubmittedAt: string | null;
}
export interface Availability {
  year: number;
  month: number;
  userId: string;
  window: AvailabilityWindow;
  submitted: boolean;
  lastSubmittedAt: string | null;
  version: number;
  selections: Selection[];
  weeks: { startsOn: string; otherMonthBlocks: number }[];
}
export interface EmployeeSubmission {
  userId: string;
  firstName: string;
  lastName: string;
  submitted: boolean;
  lastSubmittedAt: string | null;
}
export interface WindowSummary {
  year: number;
  month: number;
  window: AvailabilityWindow;
  eligibleCount: number;
  submittedCount: number;
  employees: EmployeeSubmission[];
}
export const av = {
  title: "הזמינות שלי",
  adminTitle: "ניהול זמינות",
  month: "חודש",
  empty: "עדיין לא הוגדרו חודשים להגשת זמינות.",
  intro:
    "בוחרים זמינות, בודקים את השבועות ומגישים. הבחירות נשמרות רק בלחיצה על כפתור ההגשה.",
  limit:
    "עד 3 חסימות בשבוע, מיום ראשון עד שבת — גם כשהשבוע חוצה חודשים. העדפה אינה מבטיחה שיבוץ; חסימה היא אילוץ מחייב.",
  submit: "הגש זמינות",
  update: "עדכן זמינות",
  submitted: "הוגשה זמינות",
  notSubmitted: "לא הוגשה זמינות",
  unsaved: "יש שינויים שטרם הוגשו",
  success: "הזמינות הוגשה בהצלחה.",
  updated: "הזמינות עודכנה בהצלחה.",
  discard: "יש שינויים שלא הוגשו. לצאת ולוותר על השינויים?",
  reset: "ביטול שינויים",
  last: "הגשה אחרונה",
  opens: "פתיחת החלון",
  closes: "סגירת החלון",
  timezone: "כל המועדים מוצגים לפי שעון ישראל.",
  readOnly: "חלון ההגשה סגור. הזמינות מוצגת לקריאה בלבד.",
  week: "שבוע שמתחיל ב־",
  counter: "מתוך 3 חסימות",
  other: "כולל חסימות מהחודש הסמוך",
  overflow: "חריגה ממגבלת 3 החסימות בשבוע. יש להסיר חסימות לפני ההגשה.",
  states: { AVAILABLE: "זמין", PREFERRED: "מועדף", BLOCKED: "חסום" },
  shifts: { DAY: "יום 07:00–19:00", NIGHT: "לילה 19:00–07:00 למחרת" },
  statuses: {
    NOT_CONFIGURED: "לא הוגדר חלון",
    CLOSED: "סגור",
    SCHEDULED: "ממתין לפתיחה",
    EXPIRED: "מועד ההגשה הסתיים",
    OPEN: "פתוח להגשה",
  },
  configure: "שמירת מועדי החלון",
  open: "פתיחת הגשה",
  close: "סגירת הגשה",
  configured: "מועדי החלון נשמרו.",
  opened: "חלון ההגשה נפתח.",
  closed: "חלון ההגשה נסגר.",
  employee: "עובד",
  status: "מצב הגשה",
  view: "צפייה בזמינות",
  noEmployees: "אין עובדים פעילים בעלי תפקיד עובד.",
  submittedCount: "הגשות מתוך",
  eligible: "עובדים פעילים",
  adminHelp:
    "שמירת מועדים אינה פותחת חלון סגור. פתיחה מחדש שומרת את ההגשות הקיימות ומאפשרת עדכון עד מועד הסגירה.",
  invalidTime:
    "יש להזין מועדים תקינים לפי שעון ישראל, עם סגירה לאחר הפתיחה. בשעת מעבר שעון יש לבחור שעה חד־משמעית.",
  refresh: "רענון נתונים",
  back: "חזרה לרשימת העובדים",
  noSubmission: "העובד טרם הגיש זמינות לחודש זה.",
};
export function dateKey(date: Date) {
  return date.toISOString().slice(0, 10);
}
export function weekStart(date: string) {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() - d.getUTCDay());
  return dateKey(d);
}
export function daysInMonth(year: number, month: number) {
  return Array.from(
    { length: new Date(Date.UTC(year, month, 0)).getUTCDate() },
    (_, i) =>
      `${year}-${String(month).padStart(2, "0")}-${String(i + 1).padStart(2, "0")}`,
  );
}
export function monthLabel(year: number, month: number) {
  return new Intl.DateTimeFormat("he-IL", {
    month: "long",
    year: "numeric",
    timeZone: "UTC",
  }).format(new Date(Date.UTC(year, month - 1, 1)));
}
export function instant(value: string | null) {
  return value
    ? `${formatDate(value)} ${new Intl.DateTimeFormat("he-IL", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "Asia/Jerusalem" }).format(new Date(value))}`
    : "—";
}
export function localInput(value: string | null) {
  if (!value) return "";
  const p = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Jerusalem",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).formatToParts(new Date(value));
  const get = (type: string) => p.find((x) => x.type === type)!.value;
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}`;
}
export function israelInstant(local: string) {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(local))
    throw new Error("INVALID_WINDOW_TIME");
  const nominal = Date.parse(`${local}Z`);
  const matches = [2, 3]
    .map((offset) => new Date(nominal - offset * 3600000).toISOString())
    .filter((value) => localInput(value) === local);
  if (matches.length !== 1) throw new Error("INVALID_WINDOW_TIME");
  return matches[0];
}
export function isWindowOpen(window: AvailabilityWindow, now = Date.now()) {
  return (
    window.status === "OPEN" &&
    !!window.opensAt &&
    now >= Date.parse(window.opensAt) &&
    (!window.closesAt || now < Date.parse(window.closesAt))
  );
}
export function selectionKey(date: string, type: ShiftType) {
  return `${date}/${type}`;
}
export function selectionsMap(rows: Selection[]) {
  return Object.fromEntries(
    rows.map((x) => [selectionKey(x.date, x.shiftType), x.state]),
  );
}
export function weekCounts(
  data: Availability,
  choices: Record<string, Preference>,
) {
  const counts = Object.fromEntries(
    data.weeks.map((w) => [w.startsOn, w.otherMonthBlocks]),
  );
  for (const [key, state] of Object.entries(choices))
    if (state === "BLOCKED") {
      const w = weekStart(key.split("/")[0]);
      counts[w] = (counts[w] ?? 0) + 1;
    }
  return counts;
}
