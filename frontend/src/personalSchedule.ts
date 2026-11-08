export interface PersonalAssignment {
  assignmentId: string;
  definitionId: string | null;
  date: string;
  name: string;
  period: string;
  category: string;
  startsAt: string | null;
  endsAt: string | null;
  overnight: boolean;
  plannedMinutes: number | null;
  durationUnavailableReason: string | null;
  isShiftLead: boolean;
}
export interface HoursBreakdown {
  key: string;
  name: string;
  assignedShifts: number;
  knownPlannedMinutes: number;
  unknownDurationAssignments: number;
  isComplete: boolean;
  totalPlannedMinutes: number | null;
}
export interface PersonalSchedule {
  assignments: PersonalAssignment[];
  summary: {
    year: number;
    month: number;
    totalAssignedShifts: number;
    knownPlannedMinutes: number;
    unknownDurationAssignments: number;
    isComplete: boolean;
    totalPlannedMinutes: number | null;
    dayShiftCount: number;
    nightShiftCount: number;
    onCallDutyCount: number;
    byShiftType: HoursBreakdown[];
    byCategory: HoursBreakdown[];
  };
  timeZone: string;
  attributionPolicy: string;
}

export const hours = (minutes: number) => new Intl.NumberFormat("he-IL", { maximumFractionDigits: 2 }).format(minutes / 60);
export const shiftDate = (date: string) => new Intl.DateTimeFormat("he-IL", {
  weekday: "long", day: "2-digit", month: "2-digit", year: "numeric", timeZone: "Asia/Jerusalem"
}).format(new Date(`${date}T12:00:00Z`));
export const shiftTime = (value: string) => new Intl.DateTimeFormat("he-IL", {
  hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "Asia/Jerusalem"
}).format(new Date(value));
export const categories: Record<string, string> = {
  ON_CALL: "כוננות סוף שבוע", AVAILABILITY: "זמינות ערב", SIX_NINE: "משמרת 6–9",
  EARLY_END: "סיום מוקדם", REHABILITATION: "שיקום בוקר", TIMED_WORK: "משמרת עבודה"
};
