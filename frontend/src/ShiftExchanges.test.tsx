import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { api } from "./api";
import ShiftExchanges from "./pages/ShiftExchanges";
import ExchangeRequests from "./components/ExchangeRequests";
import type { Exchange } from "./exchanges";
vi.mock("./api", () => ({ api: vi.fn() }));
vi.mock("./auth", () => ({ useAuth: () => ({ user: { id: "me" } }) }));
const mocked = vi.mocked(api);
let request: Exchange;
beforeEach(() => {
  mocked.mockReset();
  request = { id: "request", requesterUserId: "me", requesterName: "דנה", replacementUserId: "other", replacementName: "רון", status: "AWAITING_REPLACEMENT", version: 1, reason: null, createdAt: "2027-04-01T00:00:00Z", assignments: [] };
  mocked.mockImplementation(async (path, method = "GET") => {
    if (path === "/my-schedule/periods") return [{year: 2027, month: 4}];
    if (path.endsWith("/assignments")) return [{assignmentId: "assignment", publicationId: "publication", definitionId: "definition", date: "2027-04-30", startsAt: "2027-04-30T04:00:00Z", endsAt: "2027-04-30T16:00:00Z", isShiftLead: false, name: "שיקום בוקר"}];
    if (path.includes("/candidates?")) return [{userId: "other", name: "רון", eligible: true, reasons: []}];
    if (method === "POST") return request;
    return [request];
  });
});
it("creates an employee exchange from an eligible own assignment and candidate in Hebrew RTL", async () => {
  const {container} = render(<ShiftExchanges />);
  expect(container.querySelector('section[dir="rtl"]')).toBeInTheDocument();
  await screen.findByRole("option", {name: /שיקום בוקר/});
  await userEvent.selectOptions(screen.getByLabelText("המשמרת שלי"), "assignment");
  await screen.findByRole("option", {name: "רון"});
  await userEvent.selectOptions(screen.getByLabelText("עובד מחליף כשיר"), "other");
  await userEvent.click(screen.getByRole("button", {name: "שליחת בקשת החלפה"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/shift-exchanges/2027/4", "POST", { publicationId: "publication", assignmentId: "assignment", replacementUserId: "other" }));
  expect(await screen.findByText(/הבקשה נשלחה למחליף/)).toBeInTheDocument();
});
it("lets only the proposed replacement accept or reject", async () => {
  request.requesterUserId = "requester"; request.replacementUserId = "me";
  render(<ExchangeRequests />);
  await userEvent.click(await screen.findByRole("button", {name: "הסכמה להחלפה"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/shift-exchanges/request/decision", "POST", {expectedVersion: 1, decision: "ACCEPT", reason: null}));
  expect(screen.queryByRole("button", {name: "ביטול בקשה"})).not.toBeInTheDocument();
});
it("allows the requester to cancel while waiting and shows the retained status history", async () => {
  render(<ExchangeRequests />);
  await userEvent.click(await screen.findByRole("button", {name: "ביטול בקשה"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/shift-exchanges/request/decision", "POST", {expectedVersion: 1, decision: "CANCEL", reason: null}));
  expect(screen.queryByRole("button", {name: "הסכמה להחלפה"})).not.toBeInTheDocument();
});
it("administrator approval explicitly saves a correction draft", async () => {
  request.status = "AWAITING_ADMIN";
  render(<ExchangeRequests admin />);
  await userEvent.click(await screen.findByRole("button", {name: "אישור ושמירת תיקון בטיוטה"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/shift-exchanges/request/decision", "POST", {expectedVersion: 1, decision: "APPROVE", reason: null}));
  expect(screen.getByText(/יש לבדוק ולפרסם מחדש/)).toBeInTheDocument();
});
it("administrator cannot approve before replacement consent", async () => {
  render(<ExchangeRequests admin />);
  await screen.findByRole("button", {name: "דחיית הבקשה"});
  expect(screen.queryByRole("button", {name: "אישור ושמירת תיקון בטיוטה"})).not.toBeInTheDocument();
});
