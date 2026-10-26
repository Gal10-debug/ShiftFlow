export type ShiftTimeRange =
  "DAY" | "NIGHT" | "H06_09" | "H07_13" | "H07_15" | "H15_23" | "H23_07";
export const timeRanges: Record<ShiftTimeRange, string> = {
  DAY: "יום · 07:00–19:00",
  NIGHT: "לילה · 19:00–07:00 למחרת",
  H06_09: "06:00–09:00",
  H07_13: "07:00–13:00",
  H07_15: "07:00–15:00",
  H15_23: "15:00–23:00",
  H23_07: "23:00–07:00 למחרת",
};
export function rangePeriod(range: ShiftTimeRange): "DAY" | "NIGHT" {
  return range === "NIGHT" || range === "H23_07" ? "NIGHT" : "DAY";
}
export interface ShiftTypeDefinition {
  id: string;
  slot: number;
  name: string;
  displayOrder: number;
  period: "DAY" | "NIGHT";
  timeRange?: ShiftTimeRange;
  requiredEmployees: number;
  includeInAutomaticScheduling: boolean;
  requiresShiftLeadQualification?: boolean;
  version: number;
  updatedAt: string;
}
export interface BoardCell {
  date: string;
  shiftId: string | null;
  startsAt: string;
  endsAt: string;
  state: "NOT_REQUIRED" | "UNASSIGNED" | "SAVED_DRAFT";
  employees?: ScheduledEmployee[];
  requiredEmployees?: number;
  shortage?: number;
  availabilityAnchors?: {
    date: string;
    period: "DAY" | "NIGHT";
    shiftId: string | null;
  }[];
}
export interface WeeklyBoard {
  weekStartsOn: string;
  weekEndsOn: string;
  timeZone: string;
  workspaceState: "UNPUBLISHED";
  days: {
    date: string;
    year: number;
    month: number;
    schedulePeriodId: string | null;
  }[];
  rows: { definition: ShiftTypeDefinition; cells: BoardCell[] }[];
}
export const scheduling = {
  title: "סידור עבודה",
  intro:
    "לוח שבועי והגדרות סוגי המשמרות. השורות מייצגות סוגי משמרת, לא מקומות לעובדים.",
  unpublished: "מרחב מנהלי — טרם פורסם",
  preparation:
    "ניתן ליצור סידור חודשי, לבדוק חוסרים ולשמור טיוטה. הסידור אינו מפורסם לעובדים.",
  current: "השבוע הנוכחי",
  previous: "השבוע הקודם",
  next: "השבוע הבא",
  date: "בחירת תאריך בשבוע",
  weekdays: ["א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳"],
  rowHeader: "סוג משמרת",
  unassigned: "לא שובץ",
  noStaffing: "ללא צורך באיוש",
  grid: "לוח סידור עבודה שבועי",
  scrolling:
    "ניתן לגלול אופקית ואנכית בלוח. במקלדת יש להתמקד בלוח ולהשתמש במקשי החצים.",
  configure: "הגדרות סוגי משמרות",
  hide: "סגירת הגדרות",
  name: "שם סוג המשמרת",
  order: "סדר תצוגה",
  period: "טווח שעות",
  staffing: "עובדים נדרשים",
  automatic: "השתתפות בשיבוץ אוטומטי עתידי",
  periods: { DAY: timeRanges.DAY, NIGHT: timeRanges.NIGHT },
  configureHelp:
    "אפשר לשנות את שמות 51 הסוגים ואת הסדר שלהם. מספר העובדים הוא בין 0 ל־1000; אפס פירושו שאין צורך באיוש. סדר התצוגה חייב להכיל כל מספר מ־1 עד 51 פעם אחת.",
  untouched:
    "סוגים שלא נבחרו לשיבוץ אוטומטי וסוגים עם אפס עובדים לא ישתתפו בהרצה עתידית. שמירת ההגדרות אינה משנה שיבוצים קיימים.",
  selected: "נבחר לשיבוץ עתידי",
  excluded: "מחוץ לשיבוץ עתידי",
  workers: "עובדים",
  save: "שמירת הגדרות",
  saved: "הגדרות סוגי המשמרות נשמרו בהצלחה.",
  unsaved: "יש שינויים שלא נשמרו",
  discard: "יש שינויים שלא נשמרו בהגדרות. להמשיך ולוותר עליהם?",
  reset: "ביטול שינויים",
  invalid:
    "יש להזין שם באורך 1–100 תווים, מספר עובדים שלם בין 0 ל־1000 וסדר ייחודי מ־1 עד 51 לכל שורה. ניתן לסמן שורת אחראי משמרת אחת בלבד.",
  refresh: "רענון הלוח",
  empty: "לא נמצאו סוגי משמרות. יש לוודא שמיגרציות מסד הנתונים הוחלו.",
  timezone:
    "התאריכים והשעות הם לפי שעון ישראל; השבוע מתחיל ביום ראשון ומסתיים בשבת.",
};
export function displayBoardDate(value: string) {
  return value.split("-").reverse().join("/");
}
export function moveWeek(value: string, offset: number) {
  const date = new Date(`${value}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + offset * 7);
  return date.toISOString().slice(0, 10);
}
export function validDefinitions(rows: ShiftTypeDefinition[]) {
  return (
    rows.length === 51 &&
    rows.filter(row => row.requiresShiftLeadQualification).length <= 1 &&
    new Set(rows.map((row) => row.id)).size === 51 &&
    new Set(rows.map((row) => row.displayOrder)).size === 51 &&
    rows.every(
      (row) =>
        row.name.trim().length > 0 &&
        row.name.length <= 100 &&
        Number.isInteger(row.displayOrder) &&
        row.displayOrder >= 1 &&
        row.displayOrder <= 51 &&
        Number.isInteger(row.requiredEmployees) &&
        row.requiredEmployees >= 0 &&
        row.requiredEmployees <= 1000 &&
        (row.period === "DAY" || row.period === "NIGHT") &&
        Object.hasOwn(timeRanges, row.timeRange ?? row.period) &&
        rangePeriod(row.timeRange ?? row.period) === row.period,
    )
  );
}

export interface ScheduledEmployee { userId: string; name: string; protected: boolean; isShiftLead: boolean }
export interface GeneratedCell { definitionId: string; date: string; startsAt: string; endsAt: string; requiredEmployees: number; employees: ScheduledEmployee[]; shortage: number }
export interface Generation {
  id: string; year: number; month: number; state: string; solverStatus: string | null;
  provenObjectives: number; coverageOptimal: boolean; publicationReady: boolean; errorCode: string | null; expiresAt: string;
  cells: GeneratedCell[];
  summary: { required: number; filled: number; missing: number; understaffedCells: number; fullyStaffedCells: number } | null;
}
