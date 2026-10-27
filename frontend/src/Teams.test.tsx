import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import Teams from "./pages/Teams";
import TeamAvailability from "./pages/TeamAvailability";
import { api } from "./api";
import { av } from "./availability";
import { he } from "./he";
import type { Team } from "./teams";
vi.mock("./api", () => ({ api: vi.fn() }));
const mock = vi.mocked(api);
let teams: Team[];
let denied: boolean;
let submitted: boolean;
beforeEach(() => {
  denied = false; submitted = false;
  teams = ["צוות א׳", "צוות ב׳", "צוות ג׳", "צוות ד׳"].map((name, i) => ({ id: `t${i}`, name, version: 4, leaderUserId: i === 0 ? "lead" : null, deputyUserId: i === 0 ? "deputy" : null, members: i === 0 ? [
    { id: "lead", firstName: "ראש", lastName: "צוות", isActive: true, isEmployee: true },
    { id: "deputy", firstName: "סגן", lastName: "ראש", isActive: true, isEmployee: true },
    { id: "employee", firstName: "דנה", lastName: "כהן", isActive: true, isEmployee: true },
  ] : [] }));
  mock.mockReset();
  mock.mockImplementation(async (path, method = "GET") => {
    if (denied) throw new Error("FORBIDDEN");
    if (path.startsWith("/users?")) return { items: [{ id: "employee", firstName: "דנה", lastName: "כהן" }], totalCount: 1 };
    if (path.includes("/audit")) return [{ id: "a", actorUserId: "lead", targetUserId: "employee", teamId: "t0", schedulePeriodId: "p", year: 2026, month: 10, occurredAt: "2026-10-27T10:00:00Z", initialSubmission: true, previousValues: "[]", newValues: '[{"Date":"2026-10-01","ShiftType":"DAY","State":"BLOCKED"}]' }];
    if (path.endsWith("/conflicts")) return [{ assignmentId: "assignment", userId: "employee", date: "2026-10-01", code: "HARD_BLOCK_CONFLICT" }];
    if (path === "/admin/teams") return teams;
    if (path === "/team-availability/members") return [teams[0]];
    if (path.endsWith("/periods")) return [{ id: "p", year: 2026, month: 10 }];
    if (path.startsWith("/team-availability/employees/")) {
      if (method !== "GET") submitted = true;
      return { userId: "employee", year: 2026, month: 10, version: submitted ? 1 : 0, submitted, lastSubmittedAt: null, selections: [], weeks: [], window: { status: "CLOSED", administrativeState: "CLOSED", opensAt: null, closesAt: null } };
    }
    return teams;
  });
});
it("renders four teams, Hebrew RTL, audit values and draft conflicts", async () => {
  const { container } = render(<Teams />);
  await screen.findByRole("heading", { name: "היסטוריית שינויי זמינות" });
  expect(screen.getAllByRole("button", { name: "שמירת הצוות" })).toHaveLength(4);
  expect(screen.getAllByLabelText("שם הצוות")).toHaveLength(4);
  expect(container.querySelector('section[dir="rtl"]')).toBeInTheDocument();
  expect(screen.getByText(/שיבוץ בזמן חסימה/)).toBeInTheDocument();
  expect(screen.getByText(/אחרי: 2026-10-01 יום: חסום/)).toBeInTheDocument();
});
it("saves renamed team with explicit leadership selection and expected version", async () => {
  const user = userEvent.setup(); render(<Teams />);
  const inputs = await screen.findAllByLabelText("שם הצוות");
  await user.clear(inputs[0]); await user.type(inputs[0], "צוות חדש");
  await user.selectOptions(screen.getAllByLabelText("ראש צוות")[0], "employee");
  await user.selectOptions(screen.getAllByLabelText("סגן ראש צוות")[0], "deputy");
  await user.click(screen.getAllByRole("button", { name: "שמירת הצוות" })[0]);
  expect(mock).toHaveBeenCalledWith("/admin/teams/t0", "PUT", { name: "צוות חדש", leaderUserId: "employee", deputyUserId: "deputy", expectedVersion: 4 });
});
it("moves employees and supports removing membership", async () => {
  const user = userEvent.setup(); render(<Teams />);
  await screen.findAllByLabelText("שם הצוות");
  await user.selectOptions(screen.getByLabelText("עובד"), "employee");
  await user.selectOptions(screen.getByLabelText("צוות יעד"), "t1");
  await user.click(screen.getByRole("button", { name: "שמירת שיוך" }));
  await waitFor(() => expect(mock).toHaveBeenCalledWith("/admin/teams/members/employee", "PUT", { teamId: "t1" }));
  await user.selectOptions(screen.getByLabelText("צוות יעד"), "");
  await user.click(screen.getByRole("button", { name: "שמירת שיוך" }));
  await waitFor(() => expect(mock).toHaveBeenCalledWith("/admin/teams/members/employee", "PUT", { teamId: null }));
});
it("only displays the authorized team and excludes its leader from availability selection", async () => {
  render(<TeamAvailability />);
  const selector = await screen.findByLabelText("בחירת עובד");
  expect(within(selector).getByRole("option", { name: "דנה כהן" })).toBeInTheDocument();
  expect(within(selector).queryByRole("option", { name: "ראש צוות" })).not.toBeInTheDocument();
  expect(within(selector).getAllByRole("group")).toHaveLength(1);
});
it("allows editing closed window but writes only after explicit submission", async () => {
  const user = userEvent.setup(); render(<TeamAvailability />);
  await user.selectOptions(await screen.findByLabelText("בחירת עובד"), "employee");
  const shift = await screen.findByLabelText(`01/10/2026 ${av.shifts.DAY}`);
  expect(shift).toBeEnabled();
  await user.selectOptions(shift, "BLOCKED");
  expect(mock.mock.calls.filter(([, method]) => method === "POST")).toHaveLength(0);
  await user.click(screen.getByRole("button", { name: av.submit }));
  await waitFor(() => expect(mock).toHaveBeenCalledWith("/team-availability/employees/employee/2026/10", "POST", { expectedVersion: 0, selections: [{ date: "2026-10-01", shiftType: "DAY", state: "BLOCKED" }] }));
  expect(await screen.findByText(av.success)).toBeInTheDocument();
});
it("shows unauthorized state without rendering other team data", async () => {
  denied = true; render(<TeamAvailability />);
  expect(await screen.findByRole("alert")).toHaveTextContent(he.errors.FORBIDDEN);
  expect(screen.queryByLabelText("בחירת עובד")).not.toBeInTheDocument();
});
it("retains weekly block validation for delegated closed window edits", async () => {
  const user = userEvent.setup(); render(<TeamAvailability />);
  await user.selectOptions(await screen.findByLabelText("בחירת עובד"), "employee");
  for (const day of [4, 5, 6, 7]) await user.selectOptions(await screen.findByLabelText(`0${day}/10/2026 ${av.shifts.DAY}`), "BLOCKED");
  expect(screen.getByRole("button", { name: av.submit })).toBeDisabled();
  expect(screen.getByRole("alert")).toHaveTextContent(av.overflow);
});
it("shows service validation errors on team management", async () => {
  const user = userEvent.setup(); render(<Teams />); await screen.findAllByLabelText("שם הצוות");
  mock.mockRejectedValueOnce(new Error("INVALID_TEAM_LEADERSHIP"));
  await user.click(screen.getAllByRole("button", { name: "שמירת הצוות" })[0]);
  expect(await screen.findByText(he.errors.INVALID_TEAM_LEADERSHIP)).toBeInTheDocument();
});
