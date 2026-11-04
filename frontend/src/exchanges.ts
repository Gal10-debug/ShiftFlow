export interface ExchangeAssignment {
  assignmentId: string;
  publicationId: string;
  definitionId: string;
  date: string;
  startsAt: string;
  endsAt: string;
  isShiftLead: boolean;
  name: string;
}
export interface Exchange {
  id: string;
  requesterUserId: string;
  requesterName: string;
  replacementUserId: string;
  replacementName: string;
  status: string;
  version: number;
  reason: string | null;
  createdAt: string;
  assignments: ExchangeAssignment[];
}
export const exchangeStates: Record<string, string> = {
  AWAITING_REPLACEMENT: "ממתינה להסכמת המחליף",
  AWAITING_ADMIN: "ממתינה לאישור מנהל",
  APPROVED_PENDING_PUBLICATION: "אושרה — ממתינה לפרסום מחדש",
  APPLIED: "נכנסה לתוקף בפרסום מעודכן",
  REJECTED: "נדחתה",
  CANCELLED: "בוטלה",
  SUPERSEDED: "הסתיימה בעקבות שינוי בסידור"
};
