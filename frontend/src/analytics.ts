import type { HoursBreakdown } from "./personalSchedule";
export interface AnalyticsTotals {
  totalAssignedShifts: number; knownPlannedMinutes: number; unknownDurationAssignments: number;
  isComplete: boolean; totalPlannedMinutes: number | null; dayShiftCount: number; nightShiftCount: number;
  weekendShiftCount: number; specialDutyAssignments: number; byShiftType: HoursBreakdown[];
}
export interface Coverage {
  requiredPositions: number; filledPositions: number; unfilledPositions: number; coveragePercentage: number | null;
  byShiftType: { key: string; name: string; requiredPositions: number; filledPositions: number; unfilledPositions: number }[];
}
export interface AnalyticsMonth {
  year: number; month: number; publication: { id: string; versionNumber: number; publicationType: string } | null;
  totals: AnalyticsTotals; coverage: Coverage | null;
}
export interface PersonalAnalytics {
  year: number; month: number | null; totals: AnalyticsTotals; months: AnalyticsMonth[];
  comparison: { previousMonth: AnalyticsMonth; shiftCountChange: number | null; knownPlannedMinutesChange: number | null; bothDurationsComplete: boolean | null } | null;
}
export interface EmployeeWorkload { employeeId: string; name: string; teamId: string | null; totals: AnalyticsTotals }
export interface AdminAnalytics extends Omit<PersonalAnalytics, "comparison"> {
  teamId: string | null; employeeId: string | null; coverage: Coverage | null;
  distribution: { scheduledEmployees: number; includedEmployees: number; minimumAssignedShifts: number;
    maximumAssignedShifts: number; averageAssignedShifts: number; averageShiftsPerScheduledEmployee: number;
    shiftCounts: { assignedShifts: number; employees: number }[] };
  employees: EmployeeWorkload[];
  teams: { teamId: string | null; name: string; scheduledEmployees: number; totals: AnalyticsTotals }[];
}
