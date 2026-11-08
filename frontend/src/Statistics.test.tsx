import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { api } from "./api";
import Statistics from "./pages/Statistics";
import type { AdminAnalytics, PersonalAnalytics } from "./analytics";
vi.mock("./api", () => ({ api: vi.fn() }));
const mocked = vi.mocked(api);
let personal: PersonalAnalytics;
let admin: AdminAnalytics;
beforeEach(() => {
  vi.resetAllMocks(); window.history.replaceState({}, "", "/my-statistics?period=2026-10");
  const totals = { totalAssignedShifts: 3, knownPlannedMinutes: 1440, unknownDurationAssignments: 1,
    totalPlannedMinutes: null, isComplete: false, dayShiftCount: 1, nightShiftCount: 1, weekendShiftCount: 1, specialDutyAssignments: 1,
    byShiftType: [{ key: "a", name: "משמרת בדיקה", assignedShifts: 3, knownPlannedMinutes: 1440, unknownDurationAssignments: 1, isComplete: false, totalPlannedMinutes: null }] };
  personal = { year: 2026, month: 10, totals, months: [{ year: 2026, month: 10, totals, publication: { id: "p", versionNumber: 2, publicationType: "FINAL" }, coverage: null }],
    comparison: { previousMonth: { year: 2026, month: 9, totals, publication: null, coverage: null }, shiftCountChange: null, knownPlannedMinutesChange: null, bothDurationsComplete: null } };
  admin = { ...personal, teamId: null, employeeId: null, coverage: { requiredPositions: 4, filledPositions: 3, unfilledPositions: 1, coveragePercentage: 75,
    byShiftType: [{ key: "a", name: "משמרת בדיקה", requiredPositions: 4, filledPositions: 3, unfilledPositions: 1 }] },
    distribution: { scheduledEmployees: 1, includedEmployees: 2, minimumAssignedShifts: 0, maximumAssignedShifts: 3, averageAssignedShifts: 1.5, averageShiftsPerScheduledEmployee: 3, shiftCounts: [{ assignedShifts: 3, employees: 1 }] },
    employees: [{ employeeId: "e", name: "עובד בדיקה", teamId: "t", totals }], teams: [{ teamId: "t", name: "צוות בדיקה", scheduledEmployees: 1, totals }] };
  mocked.mockImplementation(async path => path === "/admin/teams" ? [{ id: "t", name: "צוות בדיקה" }] : path.includes("/admin/") ? admin : personal);
});
it("renders Hebrew RTL totals, incomplete hours, charts and publication provenance", async () => {
  const { container } = render(<Statistics />);
  const summary = await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
  expect(summary).toHaveTextContent("סה״כ משמרות3"); expect(summary).toHaveTextContent("שעות מתוכננות ידועות24");
  expect(summary).toHaveTextContent("משך העבודה אינו ידוע עבור 1 שיבוצים");
  expect(within(summary).getAllByRole("meter")).toHaveLength(2);
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  expect(screen.getByRole("table", { name: "מגמה חודשית וגרסאות פרסום" })).toHaveTextContent("גרסה 2 · סופי");
  expect(screen.getByRole("table", { name: "פירוט לפי סוג משמרת" })).toHaveTextContent("משמרת בדיקה");
});
it("navigates month, year and annual view using backend results", async () => {
  render(<Statistics />); await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
  await userEvent.selectOptions(screen.getByLabelText("חודש", { exact: true }), "9");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-statistics/2026/9"));
  await userEvent.selectOptions(screen.getByLabelText("תקופת דיווח"), "year");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-statistics/2026"));
  expect(screen.queryByLabelText("חודש", { exact: true })).not.toBeInTheDocument();
  await userEvent.clear(screen.getByLabelText("שנה")); await userEvent.type(screen.getByLabelText("שנה"), "2025");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-statistics/2025"));
});
it("distinguishes missing publications from published zero assignments", async () => {
  personal.months[0].publication = null;
  const view = render(<Statistics />);
  expect(await screen.findByText("אין פרסומים זמינים לתקופה שנבחרה.")).toBeInTheDocument();
  expect(screen.queryByRole("region", { name: "סיכום סטטיסטיקות" })).not.toBeInTheDocument();
  view.unmount(); personal.months[0].publication = { id: "p", versionNumber: 1, publicationType: "FINAL" }; personal.totals.totalAssignedShifts = 0;
  render(<Statistics />);
  expect(await screen.findByText("אין שיבוצים בפרסומים הזמינים עבור הבחירה הנוכחית.")).toBeInTheDocument();
});
it("shows loading without claiming empty data", () => {
  mocked.mockReturnValue(new Promise(() => {})); render(<Statistics />);
  expect(screen.getByRole("status")).toHaveTextContent("טוען סטטיסטיקות");
  expect(screen.queryByText("אין פרסומים זמינים לתקופה שנבחרה.")).not.toBeInTheDocument();
});
it("retries failed requests", async () => {
  mocked.mockRejectedValue(new Error("INTERNAL_ERROR")); render(<Statistics />);
  await screen.findByRole("alert"); mocked.mockResolvedValue(personal);
  await userEvent.click(screen.getByRole("button", { name: "ניסיון נוסף" }));
  await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
});
it("filters team and employee reports and displays coverage and limitations", async () => {
  render(<Statistics admin />); await screen.findByRole("table", { name: "השוואת עומס עובדים" });
  expect(screen.getByRole("region", { name: "כיסוי תקנים" })).toHaveTextContent("75%");
  expect(screen.getByText(/מדדים תיאוריים בלבד/)).toBeInTheDocument();
  await userEvent.selectOptions(screen.getByLabelText("צוות", { exact: true }), "t");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/statistics/2026/10?teamId=t"));
  await userEvent.selectOptions(screen.getByLabelText("עובד", { exact: true }), "e");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/statistics/2026/10?teamId=t&employeeId=e"));
});
it("does not attribute staffing shortages to a team", async () => {
  admin.coverage = null; render(<Statistics admin />);
  expect(await screen.findByText(/אין שיוך תקנים לצוותים במודל הנוכחי/)).toBeInTheDocument();
});
it("explains incomplete-duration comparison subtotals", async () => {
  personal.comparison!.shiftCountChange = 1; personal.comparison!.knownPlannedMinutesChange = -60; personal.comparison!.bothDurationsComplete = false;
  render(<Statistics />);
  const comparison = await screen.findByRole("region", { name: "השוואה לחודש הקודם" });
  expect(comparison).toHaveTextContent("שינוי במספר המשמרות: 1");
  expect(comparison).toHaveTextContent("-1"); expect(comparison).toHaveTextContent("לפחות אחת התקופות כוללת משך לא ידוע");
});
it("removes displayed analytics when focus revalidation denies access", async () => {
  render(<Statistics />); await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
  mocked.mockRejectedValue(new Error("FORBIDDEN"));
  await act(async () => window.dispatchEvent(new Event("focus"))); await screen.findByRole("alert");
  expect(screen.queryByRole("region", { name: "סיכום סטטיסטיקות" })).not.toBeInTheDocument();
});
it("requests a specific historical publication", async () => {
  render(<Statistics />); await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
  await userEvent.type(screen.getByLabelText("גרסה היסטורית (רשות)"), "1");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-statistics/2026/10?version=1"));
  expect(await screen.findByText(/מוצגת גרסה היסטורית שנבחרה/)).toBeInTheDocument();
});
it("does not request invalid calendar periods", async () => {
  render(<Statistics />); await screen.findByRole("region", { name: "סיכום סטטיסטיקות" });
  await userEvent.clear(screen.getByLabelText("שנה"));
  expect(screen.getByRole("alert")).toHaveTextContent("יש לבחור שנה");
  expect(mocked.mock.calls.some(([p]) => p === "/my-statistics/0/10")).toBe(false);
});

it("preserves the supported early reporting year in Hebrew month labels", async () => {
  window.history.replaceState({}, "", "/my-statistics?period=0002-01");
  personal.months[0].year = 2; personal.months[0].month = 1;
  render(<Statistics />);
  const trend = await screen.findByRole("table", { name: "מגמה חודשית וגרסאות פרסום" });
  expect(trend).toHaveTextContent("ינואר 2"); expect(trend).not.toHaveTextContent("1902");
  expect(mocked).toHaveBeenCalledWith("/my-statistics/2/1");
});
