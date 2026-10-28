export interface PublishedEmployee {
  assignmentId: string;
  userId: string;
  name: string;
  isShiftLead: boolean;
}
export interface PublishedCell {
  semantic?: "ORDINARY" | "SIX_NINE" | "WEEKEND_ON_CALL";
  definitionId: string;
  date: string;
  name: string;
  displayOrder: number;
  period: string;
  timeRange: string;
  startsAt: string;
  endsAt: string;
  requiredEmployees: number;
  requiresShiftLeadQualification: boolean;
  employees: PublishedEmployee[];
}
export interface LegacyAssignment {
  assignmentId: string;
  userId: string;
  name: string;
  date: string;
  period: string;
  startsAt: string;
  endsAt: string;
  isShiftLead: boolean;
}
export interface Snapshot {
  year: number;
  month: number;
  cells: PublishedCell[];
  legacyAssignments: LegacyAssignment[];
}
export interface Publication {
  id: string;
  versionNumber: number;
  publicationType: string;
  publishedAt: string;
  publishedByUserId: string;
  notes: string | null;
  snapshot: Snapshot | null;
  acknowledgedShortages?: boolean;
  acknowledgedOpenIssues?: boolean;
  openIssueCount?: number;
}
export interface Review {
  state: string;
  revision: number;
  token: string;
  needsReview: boolean;
  publicationReady: boolean;
  shortage: number;
  openIssues: number;
  conflicts: {
    assignmentId: string | null;
    userId: string | null;
    date: string;
    code: string;
  }[];
  draft: Snapshot;
}
export interface Issue {
  id: string;
  publicationId: string;
  assignmentId: string;
  reporterUserId: string;
  explanation: string;
  status: string;
  response: string | null;
  version: number;
  createdAt: string;
}
export interface Candidate {
  userId: string;
  name: string;
  eligible: boolean;
  reasons: string[];
}
export const lifecycle: Record<string, string> = {
  DRAFT: "טיוטה",
  PRELIMINARY_PUBLISHED: "סידור עבודה ראשוני",
  UNDER_CORRECTION: "בהכנת תיקונים — העובדים רואים את הסידור הראשוני האחרון",
  FINAL_PUBLISHED: "סידור עבודה סופי",
  PUBLISHED: "פרסום היסטורי"
};
export const issueStates: Record<string, string> = {
  OPEN: "פתוח",
  IN_REVIEW: "בבדיקה",
  RESOLVED: "טופל",
  REJECTED: "נדחה"
};
export function compareVersions(a: Snapshot | null, b: Snapshot | null) {
  if (!a || !b) return [];
  const keys = (s: Snapshot) => s.cells.map(c => [c.definitionId + "/" + c.date, c] as const);
  const left = new Map(keys(a));
  const right = new Map(keys(b));
  return [...new Set([...left.keys(), ...right.keys()])].flatMap(key => {
    const before = left.get(key);
    const after = right.get(key);
    return JSON.stringify(before) === JSON.stringify(after) ? [] : [{
      key,
      before,
      after
    }];
  });
}
