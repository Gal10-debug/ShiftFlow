import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, expect, it, vi } from "vitest";
import SchedulingBoard from "./pages/SchedulingBoard";
import App from "./App";
import { resetCsrf, type User } from "./api";
import {
  scheduling as s,
  timeRanges,
  type ShiftTimeRange,
  displayBoardDate,
  moveWeek,
  type ShiftTypeDefinition,
  type WeeklyBoard,
} from "./scheduling";
import { weekStart } from "./availability";
import { he } from "./he";

let definitions: ShiftTypeDefinition[];
let writes: {
  path: string;
  body: { shiftTypes: (ShiftTypeDefinition & { expectedVersion: number })[] };
  headers: Record<string, string>;
}[];
let reads: string[];
let failure: string | null;
let empty: boolean;
let current: User;
function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
function board(anchor: string): WeeklyBoard {
  const start = weekStart(anchor);
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(`${start}T12:00:00Z`);
    d.setUTCDate(d.getUTCDate() + i);
    const date = d.toISOString().slice(0, 10);
    return {
      date,
      year: d.getUTCFullYear(),
      month: d.getUTCMonth() + 1,
      schedulePeriodId: null,
    };
  });
  return {
    weekStartsOn: start,
    weekEndsOn: days[6].date,
    timeZone: "Asia/Jerusalem",
    workspaceState: "UNPUBLISHED",
    days,
    rows: empty
      ? []
      : definitions.map((definition) => ({
          definition,
          cells: days.map((day) => ({
            date: day.date,
            shiftId: null,
            startsAt: `${day.date}T04:00:00Z`,
            endsAt: `${day.date}T16:00:00Z`,
            state:
              definition.requiredEmployees === 0
                ? "NOT_REQUIRED"
                : "UNASSIGNED",
          })),
        })),
  };
}
beforeEach(() => {
  resetCsrf();
  writes = [];
  reads = [];
  failure = null;
  empty = false;
  definitions = Array.from({ length: 51 }, (_, i) => ({
    id: `type-${i + 1}`,
    slot: i + 1,
    name: `משמרת ${i + 1}`,
    displayOrder: i + 1,
    period: "DAY",
    requiredEmployees: 0,
    includeInAutomaticScheduling: false,
    version: 1,
    updatedAt: "2026-10-26T00:00:00Z",
  }));
  current = {
    id: "admin",
    firstName: "ישראל",
    lastName: "ישראלי",
    username: "admin",
    roles: ["ADMIN"],
    isActive: true,
    mustChangePassword: false,
    createdAt: "2026-10-01T00:00:00Z",
  };
  vi.spyOn(window, "confirm").mockReturnValue(false);
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, options: RequestInit = {}) => {
      const path = String(url);
      const method = options.method ?? "GET";
      if (path === "/api/auth/me") return json(current);
      if (path === "/api/auth/csrf") return json({ requestToken: "csrf" });
      if (method === "PUT") {
        const body = JSON.parse(options.body as string);
        writes.push({
          path,
          body,
          headers: options.headers as Record<string, string>,
        });
        if (failure) return json({ code: failure }, 409);
        definitions = body.shiftTypes
          .map((row: ShiftTypeDefinition & { expectedVersion: number }) => ({
            ...row,
            version: row.expectedVersion + 1,
          }))
          .sort(
            (a: ShiftTypeDefinition, b: ShiftTypeDefinition) =>
              a.displayOrder - b.displayOrder,
          );
        return json(definitions);
      }
      reads.push(path);
      const urlValue = new URL(path, "http://localhost");
      return json(board(urlValue.searchParams.get("date") ?? "2026-10-25"));
    }),
  );
});
function page() {
  return render(<SchedulingBoard />);
}
async function grid() {
  return within(await screen.findByRole("region", { name: s.grid }));
}
async function edit() {
  const user = userEvent.setup();
  await screen.findByRole("region", { name: s.grid });
  await user.click(screen.getByRole("button", { name: s.configure }));
  return user;
}

it("renders exactly 51 rows and seven Sunday–Saturday Hebrew date headers in RTL", async () => {
  const view = page();
  const table = await grid();
  expect(table.getAllByRole("row")).toHaveLength(52);
  expect(table.getAllByRole("rowheader")).toHaveLength(51);
  expect(table.getAllByRole("columnheader")).toHaveLength(8);
  const headers = table.getAllByRole("columnheader").slice(1);
  s.weekdays.forEach((day, i) => expect(headers[i]).toHaveTextContent(day));
  expect(headers[0]).toHaveTextContent("25/10/2026");
  expect(headers[6]).toHaveTextContent("31/10/2026");
  expect(table.getAllByRole("rowheader")[50]).toHaveTextContent("משמרת 51");
  expect(view.container.querySelector("section")).toHaveAttribute("dir", "rtl");
  expect(screen.getByRole("region", { name: s.grid })).toHaveAttribute(
    "tabindex",
    "0",
  );
});
it("distinguishes the unpublished workspace and zero-staffing cells without fabricated assignments", async () => {
  definitions[0].requiredEmployees = 2;
  page();
  const table = await grid();
  expect(screen.getByText(s.unpublished)).toBeVisible();
  expect(table.getAllByText(s.unassigned)).toHaveLength(7);
  expect(table.getAllByText(s.noStaffing)).toHaveLength(350);
  expect(screen.getByText(s.preparation)).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /יצירת סידור|שיבוץ אוטומטי/ }),
  ).not.toBeInTheDocument();
});
it("navigates previous, next and current weeks and keeps complete date columns", async () => {
  const user = userEvent.setup();
  page();
  await grid();
  await user.click(screen.getByRole("button", { name: s.previous }));
  await waitFor(() => expect(reads.at(-1)).toContain("date=2026-10-18"));
  expect((await grid()).getAllByRole("columnheader")[1]).toHaveTextContent(
    "18/10/2026",
  );
  await user.click(screen.getByRole("button", { name: s.next }));
  await waitFor(() => expect(reads.at(-1)).toContain("date=2026-10-25"));
  await grid();
  await user.click(screen.getByRole("button", { name: s.current }));
  await waitFor(() => expect(reads.at(-1)).toBe("/api/admin/scheduling/board"));
});
it("loads a week spanning two years when a date is selected", async () => {
  page();
  await grid();
  fireEvent.change(screen.getByLabelText(s.date), {
    target: { value: "2027-01-01" },
  });
  await waitFor(() => expect(reads.at(-1)).toContain("date=2027-01-01"));
  expect((await grid()).getAllByRole("columnheader")[1]).toHaveTextContent(
    "27/12/2026",
  );
  expect((await grid()).getAllByRole("columnheader")[7]).toHaveTextContent(
    "02/01/2027",
  );
});
it("keeps edits local and explicitly saves name, day/night, count and automatic inclusion", async () => {
  page();
  const user = await edit();
  const name = screen.getByLabelText(`${s.name} 1`);
  await user.clear(name);
  await user.type(name, "צוות לילה");
  await user.selectOptions(screen.getByLabelText(`${s.period} 1`), "NIGHT");
  const count = screen.getByLabelText(`${s.staffing} 1`);
  await user.clear(count);
  await user.type(count, "7");
  await user.click(screen.getByLabelText(`${s.automatic} 1`));
  expect(writes).toHaveLength(0);
  expect(screen.getByText(s.unsaved)).toBeVisible();
  await user.click(screen.getByRole("button", { name: s.save }));
  expect(await screen.findByText(s.saved)).toBeVisible();
  expect(writes[0].body.shiftTypes).toHaveLength(51);
  expect(writes[0].body.shiftTypes[0]).toMatchObject({
    name: "צוות לילה",
    period: "NIGHT",
    requiredEmployees: 7,
    includeInAutomaticScheduling: true,
    expectedVersion: 1,
  });
  expect(writes[0].headers["X-CSRF-TOKEN"]).toBe("csrf");
  expect((await grid()).getAllByRole("rowheader")[0]).toHaveTextContent(
    "צוות לילה",
  );
});
it("persists intentional zero staffing even when the inclusion checkbox is selected", async () => {
  page();
  const user = await edit();
  await user.click(screen.getByLabelText(`${s.automatic} 1`));
  await user.click(screen.getByRole("button", { name: s.save }));
  await screen.findByText(s.saved);
  expect(writes[0].body.shiftTypes[0]).toMatchObject({
    requiredEmployees: 0,
    includeInAutomaticScheduling: true,
  });
  expect((await grid()).getAllByRole("rowheader")[0]).toHaveTextContent(
    s.excluded,
  );
});
it("validates counts and duplicate display orders before making a request", async () => {
  page();
  const user = await edit();
  const count = screen.getByLabelText(`${s.staffing} 1`);
  await user.clear(count);
  await user.type(count, "-1");
  await user.click(screen.getByRole("button", { name: s.save }));
  expect(screen.getByRole("alert")).toHaveTextContent(s.invalid);
  expect(writes).toHaveLength(0);
  await user.clear(count);
  await user.type(count, "1");
  const order = screen.getByLabelText(`${s.order} 1`);
  await user.clear(order);
  await user.type(order, "2");
  await user.click(screen.getByRole("button", { name: s.save }));
  expect(screen.getByRole("alert")).toHaveTextContent(s.invalid);
  expect(writes).toHaveLength(0);
});
it("saves a complete order permutation and renders the new order", async () => {
  page();
  const user = await edit();
  for (const [slot, order] of [
    [1, "2"],
    [2, "1"],
  ] as const) {
    const input = screen.getByLabelText(`${s.order} ${slot}`);
    await user.clear(input);
    await user.type(input, order);
  }
  await user.click(screen.getByRole("button", { name: s.save }));
  await screen.findByText(s.saved);
  expect((await grid()).getAllByRole("rowheader")[0]).toHaveTextContent(
    "משמרת 2",
  );
});
it("warns before discarding unsaved configuration on week navigation", async () => {
  page();
  const user = await edit();
  await user.type(screen.getByLabelText(`${s.name} 1`), " שינוי");
  const count = reads.length;
  await user.click(screen.getByRole("button", { name: s.next }));
  expect(window.confirm).toHaveBeenCalledWith(s.discard);
  expect(reads).toHaveLength(count);
  expect(writes).toHaveLength(0);
});
it("shows server conflict errors without presenting unsaved changes as saved", async () => {
  failure = "CONFLICT";
  page();
  const user = await edit();
  await user.type(screen.getByLabelText(`${s.name} 1`), " שינוי");
  await user.click(screen.getByRole("button", { name: s.save }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    he.errors.CONFLICT,
  );
  expect(screen.queryByText(s.saved)).not.toBeInTheDocument();
  expect(screen.getByText(s.unsaved)).toBeVisible();
});
it("shows a useful empty state instead of a published or fabricated roster", async () => {
  empty = true;
  page();
  expect(await screen.findByText(s.empty)).toBeVisible();
  expect(
    screen.queryByRole("region", { name: s.grid }),
  ).not.toBeInTheDocument();
});
it("hides scheduling navigation and rejects employee/coordinator routes", async () => {
  for (const role of ["EMPLOYEE", "COORDINATOR"] as const) {
    current.roles = [role];
    const view = render(
      <MemoryRouter initialEntries={["/scheduling"]}>
        <App />
      </MemoryRouter>,
    );
    expect(
      await screen.findByRole("heading", { name: he.unauthorized }),
    ).toBeVisible();
    expect(
      screen.queryByRole("link", { name: s.title }),
    ).not.toBeInTheDocument();
    view.unmount();
  }
});
it("calendar navigation uses date arithmetic across months independently of DST", () => {
  expect(moveWeek("2026-12-27", 1)).toBe("2027-01-03");
  expect(moveWeek("2026-10-25", -1)).toBe("2026-10-18");
  expect(displayBoardDate("2027-01-02")).toBe("02/01/2027");
});

it.each(["H06_09", "H07_13", "H07_15", "H15_23", "H23_07"] as ShiftTimeRange[])(
  "offers and persists the %s range in the shift-type table",
  async (range) => {
    page();
    const user = await edit();
    const selector = screen.getByLabelText(`${s.period} 1`);
    expect(within(selector).getAllByRole("option")).toHaveLength(7);
    await user.selectOptions(selector, range);
    expect(selector).toHaveValue(range);
    await user.click(screen.getByRole("button", { name: s.save }));
    await screen.findByText(s.saved);
    expect(writes[0].body.shiftTypes[0]).toMatchObject({
      timeRange: range,
      period: range === "H23_07" ? "NIGHT" : "DAY",
    });
    expect((await grid()).getAllByRole("rowheader")[0]).toHaveTextContent(
      timeRanges[range],
    );
  },
);

it("renders saved employee names, snapshot demand and singular/plural shortages in RTL cells", async () => {
  const saved = board("2026-10-25");
  saved.rows[0].cells[0] = { ...saved.rows[0].cells[0], state: "SAVED_DRAFT", requiredEmployees: 2, shortage: 1,
    employees: [{ userId: "e1", name: "ישראל ישראלי", protected: false, isShiftLead: false }] };
  saved.rows[0].cells[1] = { ...saved.rows[0].cells[1], state: "SAVED_DRAFT", requiredEmployees: 3, shortage: 2,
    employees: [{ userId: "e2", name: "שרה כהן", protected: true, isShiftLead: true }] };
  vi.stubGlobal("fetch", vi.fn(async () => json(saved)));
  const { container } = render(<SchedulingBoard />);
  expect(await screen.findByText("ישראל ישראלי")).toBeInTheDocument();
  expect(screen.getByText("חסר עובד 1")).toBeInTheDocument();
  expect(screen.getByText("חסרים 2 עובדים")).toBeInTheDocument();
  expect(screen.getByText(/שרה כהן/)).toHaveTextContent("אחראי משמרת");
  expect(screen.getByText("נדרשים: 2 · שובצו: 1")).toBeInTheDocument();
  expect(container.querySelector("section")).toHaveAttribute("dir", "rtl");
});

it("overlays unsaved preview names and clears them on cancel without saving", async () => {
  definitions[0].requiredEmployees = 2;
  definitions[0].includeInAutomaticScheduling = true;
  const saved = board("2026-10-25");
  const proposed = { id: "preview", year: 2026, month: 10, state: "READY", solverStatus: "OPTIMAL", provenObjectives: 4,
    coverageOptimal: true, publicationReady: false, errorCode: null, expiresAt: "2099-01-01",
    cells: [{ definitionId: definitions[0].id, date: "2026-10-25", startsAt: "", endsAt: "", requiredEmployees: 2,
      employees: [{ userId: "employee", name: "עובד בתצוגה", protected: false, isShiftLead: false }], shortage: 1 }],
    summary: { required: 2, filled: 1, missing: 1, understaffedCells: 1, fullyStaffedCells: 0 } };
  const requests: string[] = [];
  vi.stubGlobal("fetch", vi.fn(async (path: string, options?: RequestInit) => {
    requests.push(`${options?.method ?? "GET"} ${path}`);
    if (path.endsWith("/csrf")) return json({ requestToken: "token" });
    if (options?.method === "DELETE") return new Response(null, { status: 204 });
    if (path.endsWith("/generation")) return json(proposed, 202);
    return json(saved);
  }));
  render(<SchedulingBoard />);
  await screen.findByRole("table");
  await userEvent.click(screen.getByRole("button", { name: s.configure }));
  expect(screen.getByRole("button", { name: "צור סידור אוטומטי" })).toBeEnabled();
  await userEvent.click(screen.getByRole("button", { name: "צור סידור אוטומטי" }));
  expect(await screen.findByText("עובד בתצוגה")).toBeInTheDocument();
  expect(screen.getByText("חסר עובד 1")).toBeInTheDocument();
  expect(screen.getByText("תצוגה מקדימה")).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "ביטול תצוגה מקדימה" }));
  await waitFor(() => expect(screen.queryByText("עובד בתצוגה")).not.toBeInTheDocument());
  expect(requests.some(r => r.includes("/save"))).toBe(false);
});
