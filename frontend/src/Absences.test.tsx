import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, expect, it, vi } from "vitest";
import Absences from "./pages/Absences";
import { api } from "./api";
import type { Absence, AbsenceReport } from "./absences";
vi.mock("./api", () => ({ api: vi.fn() }));
let admin = false;
let actorId = "approver";
vi.mock("./auth", () => ({ useAuth: () => ({ user: { id: actorId, roles: admin ? ["ADMIN"] : ["EMPLOYEE"] } }) }));
const mocked = vi.mocked(api);
let requests: Absence[];
let report: AbsenceReport;
const request: Absence = {
  id: "request", userId: "employee", employeeName: "דנה כהן", teamId: "team", teamName: "צוות א", kind: "CREATE", status: "PENDING",
  pattern: { reason: "STUDIES", startDate: "2026-11-10", endDate: "2026-11-15", recurring: false, weekdays: [], fullDay: true, startTime: null, endTime: null, explanation: "לימודים" },
  replacesId: null, version: 1, createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z", decision: null, unresolvedConflicts: []
};
beforeEach(() => {
  admin = false; actorId = "approver"; mocked.mockReset(); requests = [structuredClone(request)]; report = { token: "conflicts", items: [] };
  mocked.mockImplementation(async (path, method = "GET") => {
    if (method !== "GET") return requests[0];
    if (path.endsWith("/conflicts")) return report;
    if (path === "/admin/teams" || path === "/team-availability/members") return [{ id: "team", name: "צוות א", members: [{ id: "employee", firstName: "דנה", lastName: "כהן" }] }];
    return requests;
  });
});
function page(manage = false) { return render(<MemoryRouter><Absences manage={manage} /></MemoryRouter>); }
it("requires another approver for personal proposals while preserving decisions on teammates' requests", async () => {
  actorId = "employee";
  requests.push({ ...structuredClone(request), id: "teammate-request", userId: "teammate", employeeName: "עמית לצוות" });
  page(true);
  expect(await screen.findByText("בקשה אישית מחייבת החלטה של מאשר אחר.")).toBeInTheDocument();
  expect(screen.getAllByRole("button", { name: "בדיקה והחלטה" })).toHaveLength(1);
  expect(within(screen.getByRole("article", { name: "בקשת היעדרות דנה כהן" })).queryByRole("button", { name: "בדיקה והחלטה" })).not.toBeInTheDocument();
});
it("submits inclusive one-time absence dates and displays pending as a warning only in Hebrew RTL", async () => {
  const { container } = page();
  expect(await screen.findByText("טרם אושרה — אינה חוסמת שיבוץ.")).toBeInTheDocument();
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  const form = screen.getByRole("form", { name: "בקשת היעדרות חדשה" });
  await userEvent.clear(within(form).getByLabelText("תאריך התחלה"));
  await userEvent.type(within(form).getByLabelText("תאריך התחלה"), "2026-11-10");
  await userEvent.clear(within(form).getByLabelText("תאריך סיום"));
  await userEvent.type(within(form).getByLabelText("תאריך סיום"), "2026-11-15");
  await userEvent.selectOptions(within(form).getByLabelText("סיבת ההיעדרות"), "RESERVE");
  await userEvent.click(within(form).getByRole("button", { name: "שליחת בקשה" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences", "POST", expect.objectContaining({ reason: "RESERVE", startDate: "2026-11-10", endDate: "2026-11-15", recurring: false, fullDay: true })));
});
it("submits indefinite weekly partial-day restrictions with multiple weekdays", async () => {
  page(); const form = screen.getByRole("form");
  await userEvent.selectOptions(within(form).getByLabelText("סוג היעדרות"), "weekly");
  await userEvent.clear(within(form).getByLabelText("תאריך סיום (רשות)"));
  await userEvent.click(within(form).getByLabelText("שני")); await userEvent.click(within(form).getByLabelText("רביעי"));
  await userEvent.click(within(form).getByLabelText("היעדרות ביום מלא"));
  await userEvent.type(within(form).getByLabelText("משעה"), "08:00"); await userEvent.type(within(form).getByLabelText("עד שעה"), "16:00");
  await userEvent.click(within(form).getByRole("button", { name: "שליחת בקשה" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences", "POST", expect.objectContaining({ recurring: true, weekdays: [1, 3], fullDay: false, startTime: "08:00:00", endTime: "16:00:00", endDate: null })));
});
it("requires a weekday for recurrence and never submits an invalid rule", async () => {
  page(); await userEvent.selectOptions(screen.getByLabelText("סוג היעדרות"), "weekly");
  await userEvent.click(screen.getByRole("button", { name: "שליחת בקשה" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("ימי השבוע");
  expect(mocked.mock.calls.some(([, method]) => method === "POST")).toBe(false);
});
it("approved requests can only request modification or cancellation; existing restriction stays active", async () => {
  requests[0].status = "APPROVED"; requests[0].version = 2; page();
  await userEvent.click(await screen.findByRole("button", { name: "בקשת שינוי" }));
  const form = screen.getByRole("form", { name: "בקשת שינוי היעדרות" });
  expect(within(form).getByText("ההיעדרות המאושרת נשארת בתוקף עד לאישור השינוי.")).toBeInTheDocument();
  await userEvent.selectOptions(within(form).getByLabelText("סיבת ההיעדרות"), "VACATION");
  await userEvent.click(within(form).getByRole("button", { name: "שליחת בקשת שינוי" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/request/changes", "POST", expect.objectContaining({ expectedVersion: 2, kind: "MODIFY", pattern: expect.objectContaining({ reason: "VACATION" }) })));
  await userEvent.click(screen.getByRole("button", { name: "בקשת ביטול היעדרות" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/request/changes", "POST", { expectedVersion: 2, kind: "CANCEL", pattern: null }));
  expect(screen.queryByRole("button", { name: "ביטול בקשה ממתינה" })).not.toBeInTheDocument();
});
it("pending requests can be cancelled and rejection reasons remain visible in history", async () => {
  requests.push({ ...structuredClone(request), id: "rejected", status: "REJECTED", decision: { actorUserId: "boss", actorName: "ראש הצוות", decision: "REJECTED", occurredAt: "2026-11-02T10:00:00Z", rejectionReason: "נא לתאם תאריכים" } });
  page(); expect(await screen.findByText(/נא לתאם תאריכים/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "ביטול בקשה ממתינה" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/request/cancel-pending", "POST", { expectedVersion: 1 }));
});
it("team approvers must review conflicts and explicitly confirm draft removals", async () => {
  report.items = [{ assignmentId: "assignment", schedulePeriodId: "period", year: 2026, month: 11, date: "2026-11-10", startsAt: "2026-11-10T05:00:00Z", endsAt: "2026-11-10T17:00:00Z", shiftName: "בוקר", state: "DRAFT", publicationId: null, publicationVersion: null, removable: true, removalBlock: null }];
  page(true); expect(await screen.findByRole("heading", { name: "אישור היעדרויות הצוות" })).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "בדיקה והחלטה" }));
  const review = await screen.findByRole("region", { name: "בדיקת בקשת היעדרות" });
  const approve = within(review).getByRole("button", { name: "אישור הבקשה" }); expect(approve).toBeDisabled();
  expect(within(review).queryByRole("link")).not.toBeInTheDocument();
  await userEvent.click(within(review).getByLabelText(/אני מאשר הסרת השיבוצים/));
  await userEvent.click(approve);
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/request/decision", "POST", { expectedVersion: 1, decision: "APPROVED", rejectionReason: null, conflictToken: "conflicts", confirmDraftRemovals: true }));
});
it("requires rejection reason and shows approver identity and timestamps", async () => {
  page(true); await userEvent.click(await screen.findByRole("button", { name: "בדיקה והחלטה" }));
  const reject = await screen.findByRole("button", { name: "דחיית הבקשה" }); expect(reject).toBeDisabled();
  await userEvent.type(screen.getByLabelText("סיבת דחייה"), "יש לתאם מועד אחר"); await userEvent.click(reject);
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/request/decision", "POST", expect.objectContaining({ decision: "REJECTED", rejectionReason: "יש לתאם מועד אחר" })));
});
it("administrators filter by team employee status and dates and can open published conflict corrections", async () => {
  admin = true;
  requests[0].status = "APPROVED";
  requests[0].unresolvedConflicts = [{ assignmentId: "a", schedulePeriodId: "p", year: 2026, month: 11, date: "2026-11-10", startsAt: "2026-11-10T05:00:00Z", endsAt: "2026-11-10T17:00:00Z", shiftName: "בוקר", state: "PUBLISHED_CONFLICT", publicationId: "pub", publicationVersion: 3, removable: false, removalBlock: null }];
  page(true);
  expect(await screen.findByRole("alert")).toHaveTextContent("טרם תוקנו");
  expect(screen.getByRole("link", { name: "פתיחת לוח הסידור לתיקון" })).toHaveAttribute("href", "/scheduling?period=2026-11");
  await userEvent.selectOptions(screen.getByLabelText("צוות"), "team"); await userEvent.selectOptions(screen.getByLabelText("עובד"), "employee");
  await userEvent.selectOptions(screen.getByLabelText("מצב הבקשה"), "APPROVED");
  await userEvent.type(screen.getByLabelText("מתאריך"), "2026-11-01"); await userEvent.type(screen.getByLabelText("עד תאריך"), "2026-11-30");
  await userEvent.click(screen.getByRole("button", { name: "החלת סינון" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/absences/manage?teamId=team&userId=employee&status=APPROVED&from=2026-11-01&through=2026-11-30"));
});
it("stale conflict reports require a fresh review before retrying approval", async () => {
  page(true); await userEvent.click(await screen.findByRole("button", { name: "בדיקה והחלטה" }));
  const approve = await screen.findByRole("button", { name: "אישור הבקשה" });
  mocked.mockRejectedValueOnce(new Error("STALE_ABSENCE_CONFLICTS")); await userEvent.click(approve);
  expect(await screen.findByRole("alert")).toHaveTextContent("בדיקת ההתנגשויות");
  expect(screen.queryByRole("button", { name: "אישור הבקשה" })).not.toBeInTheDocument();
});
