export interface AbsencePattern {
  reason: string; startDate: string; endDate: string | null; recurring: boolean;
  weekdays: number[]; fullDay: boolean; startTime: string | null; endTime: string | null; explanation: string | null;
}
export interface AbsenceConflict {
  assignmentId: string; schedulePeriodId: string; year: number; month: number; date: string;
  startsAt: string; endsAt: string; shiftName: string; state: string;
  publicationId: string | null; publicationVersion: number | null; removable: boolean; removalBlock: string | null;
}
export interface AbsenceReport { token: string; items: AbsenceConflict[] }
export interface Absence {
  id: string; userId: string; employeeName: string; teamId: string | null; teamName: string | null;
  kind: string; status: string; pattern: AbsencePattern; replacesId: string | null; version: number;
  createdAt: string; updatedAt: string;
  decision: {actorUserId: string; actorName: string; decision: string; occurredAt: string; rejectionReason: string | null} | null;
  unresolvedConflicts: AbsenceConflict[];
}
export const reasons: Record<string, string> = { STUDIES: "לימודים", RESERVE: "מילואים", VACATION: "חופשה", OTHER: "אחר" };
export const statuses: Record<string, string> = { PENDING: "ממתינה לאישור", APPROVED: "מאושרת", REJECTED: "נדחתה", CANCELLED: "בוטלה או הוחלפה" };
export const kinds: Record<string, string> = { CREATE: "בקשת היעדרות", MODIFY: "בקשת שינוי", CANCEL: "בקשת ביטול" };
export const weekdays = ["ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת"];
export function describe(p: AbsencePattern) {
  return `${p.startDate} – ${p.endDate ?? "ללא תאריך סיום"}${p.recurring ? ` · בכל ${p.weekdays.map(d => weekdays[d]).join(", ")}` : ""} · ${p.fullDay ? "יום מלא" : `${p.startTime?.slice(0, 5)}–${p.endTime?.slice(0, 5)}`}`;
}
export function scheduleLink(c: AbsenceConflict, admin: boolean) {
  return `${admin ? "/scheduling" : "/my-schedule"}?period=${c.year}-${String(c.month).padStart(2, "0")}`;
}
