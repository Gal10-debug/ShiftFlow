import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import MySchedule from "./pages/MySchedule";
import { api } from "./api";
import type { Publication } from "./publication";
import { hours, shiftDate, shiftTime, type PersonalSchedule } from "./personalSchedule";

vi.mock("./auth", () => ({ useAuth: () => ({ user: { id: "employee" } }) }));
vi.mock("./api", () => ({ api: vi.fn() }));
const mocked = vi.mocked(api);
let publication: Publication;
let schedule: PersonalSchedule;
beforeEach(() => {
  mocked.mockReset();
  schedule = {
    timeZone: "Asia/Jerusalem", attributionPolicy: "ASSIGNMENT_DATE",
    assignments: [{ assignmentId: "a", definitionId: "d", date: "2026-10-31", name: "לילה",
      period: "NIGHT", category: "TIMED_WORK", startsAt: "2026-10-31T17:00:00Z", endsAt: "2026-11-01T05:00:00Z",
      overnight: true, plannedMinutes: 720, durationUnavailableReason: null, isShiftLead: false }],
    summary: { year: 2026, month: 10, totalAssignedShifts: 1, knownPlannedMinutes: 720,
      unknownDurationAssignments: 0, isComplete: true, totalPlannedMinutes: 720, dayShiftCount: 0, nightShiftCount: 1,
      onCallDutyCount: 0, byShiftType: [{ key: "d", name: "לילה", assignedShifts: 1,
        knownPlannedMinutes: 720, unknownDurationAssignments: 0, isComplete: true, totalPlannedMinutes: 720 }], byCategory: [] }
  };
  publication = { id: "p", versionNumber: 2, publicationType: "PRELIMINARY", publishedAt: "2026-10-01T10:00:00Z",
    publishedByUserId: "", notes: null, snapshot: { year: 2026, month: 10, cells: [], legacyAssignments: [] }, personalSchedule: schedule };
  mocked.mockImplementation(async path => {
    if (path.endsWith("/periods")) return [{ year: 2026, month: 10 }, { year: 2026, month: 9 }];
    if (path.endsWith("/versions")) return [2, 1];
    if (path.endsWith("/issues")) return [];
    return publication;
  });
});

it("renders authoritative monthly totals and chronological personal assignments in Hebrew RTL", async () => {
  const { container } = render(<MySchedule />);
  const summary = await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  expect(summary).toHaveTextContent("שעות עבודה מתוכננות12");
  expect(summary).toHaveTextContent("משמרות לילה1");
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  const table = screen.getByRole("table", { name: "המשמרות שלי — דיווח על בעיות" });
  expect(table).toHaveTextContent("19:00"); expect(table).toHaveTextContent("07:00");
  expect(table).toHaveTextContent("ביום הבא · חוצה חצות"); expect(table).toHaveTextContent("12 שעות");
});

it("formats Israel dates and fractional hours independently of the browser zone", () => {
  expect(shiftTime("2026-10-31T17:00:00Z")).toBe("19:00");
  expect(shiftDate("2026-10-31")).toContain("31.10.2026");
  expect(hours(90)).toBe("1.5");
});

it("labels mixed known and unknown durations as an incomplete subtotal", async () => {
  schedule.summary.isComplete = false; schedule.summary.totalPlannedMinutes = null;
  schedule.summary.unknownDurationAssignments = 1;
  render(<MySchedule />);
  const summary = await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  expect(summary).toHaveTextContent("הסכום המלא אינו זמין");
  expect(summary).toHaveTextContent("שעות ידועות בלבד: 12");
  expect(summary).toHaveTextContent("זהו סכום חלקי");
});

it("shows unavailable assignment duration without inventing work hours for on-call", async () => {
  schedule.assignments[0] = { ...schedule.assignments[0], category: "ON_CALL", plannedMinutes: null };
  schedule.summary.onCallDutyCount = 1;
  render(<MySchedule />);
  await screen.findByText("משך העבודה אינו ידוע");
  expect(screen.getByText(/ימי שישי ושבת מוצגים כשיבוצים נפרדים/)).toBeInTheDocument();
});

it("shows a clear zero-assignment empty state for a visible publication", async () => {
  schedule.assignments = []; schedule.summary.totalAssignedShifts = 0;
  schedule.summary.knownPlannedMinutes = 0; schedule.summary.totalPlannedMinutes = 0;
  render(<MySchedule />);
  expect(await screen.findByText("אין לך משמרות בחודש זה בגרסת הפרסום שנבחרה.")).toBeInTheDocument();
  expect(screen.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toHaveTextContent("שעות עבודה מתוכננות0");
});

it("distinguishes loading from the unpublished empty state", async () => {
  let resolve!: (value: unknown) => void;
  mocked.mockReturnValue(new Promise(r => { resolve = r; }));
  render(<MySchedule />);
  expect(screen.getByRole("status")).toHaveTextContent("טוען");
  expect(screen.queryByText("עדיין לא פורסם עבורך סידור עבודה.")).not.toBeInTheDocument();
  await act(async () => resolve([]));
  expect(await screen.findByText("עדיין לא פורסם עבורך סידור עבודה.")).toBeInTheDocument();
});

it("shows loading while a selected publication is pending", async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation((path, method, body) => path === "/my-schedule/2026/10" ? new Promise(() => {}) : original(path, method, body));
  render(<MySchedule />);
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-schedule/2026/10"));
  expect(screen.getByRole("status")).toHaveTextContent("טוען");
  expect(screen.queryByRole("region", { name: "סיכום חודשי מתוכנן" })).not.toBeInTheDocument();
});

it("shows request errors and retries explicitly", async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockRejectedValue(new Error("INTERNAL_ERROR"));
  render(<MySchedule />);
  expect(await screen.findByRole("alert")).toBeInTheDocument();
  mocked.mockImplementation(original);
  await userEvent.click(screen.getByRole("button", { name: "ניסיון נוסף" }));
  expect(await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" })).toBeInTheDocument();
});

it("navigates only published months with one schedule request per transition", async () => {
  render(<MySchedule />); await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  expect(screen.getByRole("button", { name: "החודש הבא שפורסם" })).toBeDisabled();
  await userEvent.click(screen.getByRole("button", { name: "החודש הקודם שפורסם" }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-schedule/2026/9"));
  expect(mocked.mock.calls.filter(([path]) => path === "/my-schedule/2026/9")).toHaveLength(1);
  expect(screen.getByRole("button", { name: "החודש הקודם שפורסם" })).toBeDisabled();
});

it("ties historical summary and assignments to the selected publication", async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, method, body) => path.includes("?version=1")
    ? { ...publication, versionNumber: 1, personalSchedule: { ...schedule, summary: { ...schedule.summary, totalPlannedMinutes: 180 } } }
    : original(path, method, body));
  render(<MySchedule />); await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  await userEvent.selectOptions(screen.getByLabelText("גרסת פרסום"), "1");
  await waitFor(() => expect(screen.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toHaveTextContent("שעות עבודה מתוכננות3"));
  expect(screen.getByText(/מוצגת גרסה היסטורית/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "דיווח על בעיה" })).not.toBeInTheDocument();
});

it("removes summaries as well as assignments when publication visibility is revoked", async () => {
  render(<MySchedule />); await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  mocked.mockRejectedValue(new Error("NOT_FOUND"));
  await act(async () => window.dispatchEvent(new Event("focus")));
  await screen.findByRole("alert");
  expect(screen.queryByRole("region", { name: "סיכום חודשי מתוכנן" })).not.toBeInTheDocument();
  expect(screen.queryByRole("table", { name: "המשמרות שלי — דיווח על בעיות" })).not.toBeInTheDocument();
});

it("retains per-type breakdown and employee exchange actions", async () => {
  render(<MySchedule />); await screen.findByRole("region", { name: "סיכום חודשי מתוכנן" });
  await userEvent.click(screen.getByText("פירוט לפי סוג משמרת"));
  expect(within(screen.getByRole("region", { name: "סיכום חודשי מתוכנן" })).getByRole("listitem")).toHaveTextContent("12 שעות מתוכננות");
  expect(screen.getByRole("link", { name: "בקשות החלפת משמרות" })).toHaveAttribute("href", "/shift-exchanges");
});
