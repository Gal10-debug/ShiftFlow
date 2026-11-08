export interface PublishedEmployee {
  assignmentId: string;
  userId: string;
  name: string;
  isShiftLead: boolean;
  note?: string | null;
}
export interface PublishedCell {
  assignmentMode?: "Automatic" | "ManualOnly";
  applicable?: boolean;
  state?: string;
  includeInAutomaticScheduling?: boolean;
  configuredRequiredEmployees?: number;
  weekendRequiredEmployees?: number;
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
  note?: string | null;
}
export interface Snapshot {
  year: number;
  month: number;
  cells: PublishedCell[];
  legacyAssignments: LegacyAssignment[];
}
export interface Publication {
  personalSchedule?: import("./personalSchedule").PersonalSchedule | null;
  hidden?: boolean;
  deleted?: boolean;
  controlVersion?: number;
  id: string;
  versionNumber: number;
  publicationType: string;
  publishedAt: string;
  publishedByUserId: string;
  publishedByName?: string | null;
  notes: string | null;
  snapshot: Snapshot | null;
  acknowledgedShortages?: boolean;
  acknowledgedOpenIssues?: boolean;
  openIssueCount?: number;
}
export interface Review {
  pendingAbsenceWarnings?: {assignmentId: string; userId: string; date: string; code: string}[];
  finalRevision?: boolean;
  editingContractVersion?: number;
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
  isShiftLeadQualified?: boolean;
  preferred?: boolean;
}
export const lifecycle: Record<string, string> = {
  DRAFT: "טיוטה",
  PRELIMINARY_PUBLISHED: "סידור עבודה ראשוני",
  UNDER_CORRECTION: "בהכנת תיקונים — העובדים רואים את הגרסה האחרונה שפורסמה",
  FINAL_PUBLISHED: "סידור עבודה סופי",
  PUBLISHED: "פרסום היסטורי"
};
export const issueStates: Record<string, string> = {
  OPEN: "פתוח",
  IN_REVIEW: "בבדיקה",
  RESOLVED: "טופל",
  REJECTED: "נדחה"
};
