export interface TeamMember { id: string; firstName: string; lastName: string; isActive: boolean; isEmployee?: boolean }
export interface Team { id: string; name: string; leaderUserId: string | null; deputyUserId: string | null; version: number; members: TeamMember[] }
export interface RosterConflict { assignmentId: string; userId: string; schedulePeriodId: string; date: string; code: string }
export interface AuditEvent { id: string; actorUserId: string; targetUserId: string; teamId: string; schedulePeriodId: string; year: number; month: number; occurredAt: string; initialSubmission: boolean; previousValues: string; newValues: string }
