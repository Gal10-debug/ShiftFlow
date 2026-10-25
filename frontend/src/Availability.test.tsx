import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, expect, it, vi } from "vitest";
import AvailabilityPage from "./pages/Availability";
import AvailabilityAdmin from "./pages/AvailabilityAdmin";
import { api, resetCsrf } from "./api";
import {
  av,
  israelInstant,
  localInput,
  weekStart,
  type Availability,
  type Selection,
  type WindowSummary,
} from "./availability";
import { he } from "./he";
let data: Availability;
let summary: WindowSummary;
let writes: {
  method: string;
  body: { selections?: Selection[]; expectedVersion?: number };
}[];
let failure: string | null;
function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
beforeEach(() => {
  resetCsrf();
  failure = null;
  writes = [];
  data = {
    year: 2026,
    month: 10,
    userId: "employee",
    submitted: false,
    version: 0,
    lastSubmittedAt: null,
    selections: [],
    window: {
      status: "OPEN",
      administrativeState: "OPEN",
      opensAt: "2020-01-01T00:00:00Z",
      closesAt: "2099-01-01T00:00:00Z",
    },
    weeks: [
      "2026-09-27",
      "2026-10-04",
      "2026-10-11",
      "2026-10-18",
      "2026-10-25",
    ].map((startsOn) => ({ startsOn, otherMonthBlocks: 0 })),
  };
  summary = {
    year: 2026,
    month: 10,
    window: data.window,
    eligibleCount: 2,
    submittedCount: 1,
    employees: [
      {
        userId: "one",
        firstName: "דנה",
        lastName: "כהן",
        submitted: true,
        lastSubmittedAt: "2026-10-20T12:00:00Z",
      },
      {
        userId: "two",
        firstName: "יעל",
        lastName: "לוי",
        submitted: false,
        lastSubmittedAt: null,
      },
    ],
  };
  vi.spyOn(window, "confirm").mockReturnValue(false);
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, options: RequestInit = {}) => {
      const path = String(url);
      const method = options.method ?? "GET";
      if (path === "/api/auth/csrf") return json({ requestToken: "csrf" });
      if (method !== "GET") {
        const body = JSON.parse(options.body as string);
        writes.push({ method, body });
        if (failure)
          return json(
            { code: failure, weekStartsOn: "2026-09-27", limit: 3 },
            400,
          );
        if (path.startsWith("/api/admin/")) return json(summary);
        data = {
          ...data,
          submitted: true,
          lastSubmittedAt: "2026-10-20T12:00:00Z",
          version: data.version + 1,
          selections: body.selections.filter(
            (x: Selection) => x.state !== "AVAILABLE",
          ),
        };
        return json(data);
      }
      if (path === "/api/availability/periods")
        return json([
          {
            id: "period",
            year: 2026,
            month: 10,
            window: data.window,
            submitted: data.submitted,
            lastSubmittedAt: data.lastSubmittedAt,
          },
        ]);
      if (path.includes("/employees/")) return json(data);
      if (path.startsWith("/api/admin/")) return json(summary);
      return json(data);
    }),
  );
});
function page() {
  return render(
    <MemoryRouter>
      <AvailabilityPage />
    </MemoryRouter>,
  );
}
async function day(date = "01/10/2026", shift = "DAY") {
  return await screen.findByRole("combobox", {
    name: `${date} ${av.shifts[shift as "DAY" | "NIGHT"]}`,
  });
}

it("renders Hebrew RTL, Sunday-first calendar and first submission action", async () => {
  const view = page();
  expect(await screen.findByRole("button", { name: av.submit })).toBeEnabled();
  expect(view.container.querySelector("section")).toHaveAttribute("dir", "rtl");
  expect(screen.getAllByText("ראשון").length).toBeGreaterThan(0);
  expect(await day()).toHaveValue("AVAILABLE");
  expect(screen.getAllByText(av.notSubmitted).length).toBeGreaterThan(0);
});
it("keeps calendar edits local and only marks submitted after explicit POST", async () => {
  const user = userEvent.setup();
  page();
  await user.selectOptions(await day(), "BLOCKED");
  expect(writes).toHaveLength(0);
  expect(data.submitted).toBe(false);
  expect(screen.getAllByText(av.unsaved).length).toBeGreaterThan(0);
  await user.click(screen.getByRole("button", { name: av.submit }));
  expect(await screen.findByRole("button", { name: av.update })).toBeEnabled();
  expect(writes[0]).toMatchObject({
    method: "POST",
    body: {
      expectedVersion: 0,
      selections: [{ date: "2026-10-01", shiftType: "DAY", state: "BLOCKED" }],
    },
  });
  expect(screen.getByText(av.success)).toBeVisible();
  expect(screen.queryByText(av.unsaved)).not.toBeInTheDocument();
});
it("uses PUT and saved version for updates and removes stale blocks", async () => {
  data = {
    ...data,
    submitted: true,
    version: 4,
    lastSubmittedAt: "2026-10-20T12:00:00Z",
    selections: [{ date: "2026-10-01", shiftType: "DAY", state: "BLOCKED" }],
  };
  const user = userEvent.setup();
  page();
  await user.selectOptions(await day(), "AVAILABLE");
  expect(data.selections).toHaveLength(1);
  await user.click(screen.getByRole("button", { name: av.update }));
  expect(await screen.findByText(av.updated)).toBeVisible();
  expect(writes[0]).toMatchObject({
    method: "PUT",
    body: { expectedVersion: 4 },
  });
  expect(data.selections).toHaveLength(0);
});
it("persists an all-Available submission without special preferences", async () => {
  const user = userEvent.setup();
  page();
  await user.click(await screen.findByRole("button", { name: av.submit }));
  expect(await screen.findByText(av.success)).toBeVisible();
  expect(writes[0].body.selections).toEqual([]);
  expect(data.submitted).toBe(true);
});
it("shows cross-month block count and disables submission after fourth block", async () => {
  data.weeks[0].otherMonthBlocks = 2;
  const user = userEvent.setup();
  page();
  await user.selectOptions(await day(), "BLOCKED");
  expect(screen.getByRole("button", { name: av.submit })).toBeEnabled();
  await user.selectOptions(await day("01/10/2026", "NIGHT"), "BLOCKED");
  expect(screen.getByRole("alert")).toHaveTextContent(av.overflow);
  expect(screen.getByRole("button", { name: av.submit })).toBeDisabled();
  expect(screen.getByText(/4 מתוך 3 חסימות/)).toBeVisible();
  expect(writes).toHaveLength(0);
});
it("shows existing submissions read-only when closed", async () => {
  data.window = {
    ...data.window,
    status: "CLOSED",
    administrativeState: "CLOSED",
  };
  data.submitted = true;
  data.selections = [
    { date: "2026-10-01", shiftType: "DAY", state: "PREFERRED" },
  ];
  page();
  expect(await day()).toBeDisabled();
  expect(await day()).toHaveValue("PREFERRED");
  expect(screen.getByRole("button", { name: av.update })).toBeDisabled();
  expect(screen.getByText(av.readOnly)).toBeVisible();
});
it("keeps never-submitted status after closure", async () => {
  data.window.status = "CLOSED";
  page();
  expect(await day()).toBeDisabled();
  expect(screen.getAllByText(av.notSubmitted).length).toBeGreaterThan(0);
  expect(screen.getByRole("button", { name: av.submit })).toBeDisabled();
});
it("shows Hebrew server validation and affected week without claiming success", async () => {
  failure = "WEEKLY_BLOCK_LIMIT";
  const user = userEvent.setup();
  page();
  await user.selectOptions(await day(), "BLOCKED");
  await user.click(screen.getByRole("button", { name: av.submit }));
  expect(await screen.findByRole("alert")).toHaveTextContent("27/09/2026");
  expect(screen.getByRole("alert")).toHaveTextContent(
    he.errors.WEEKLY_BLOCK_LIMIT,
  );
  expect(screen.queryByText(av.success)).not.toBeInTheDocument();
  expect(data.submitted).toBe(false);
});
it("maps closed-window failure and preserves unsaved draft", async () => {
  failure = "WINDOW_CLOSED";
  const user = userEvent.setup();
  page();
  await user.selectOptions(await day(), "PREFERRED");
  await user.click(screen.getByRole("button", { name: av.submit }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    he.errors.WINDOW_CLOSED,
  );
  expect(await day()).toHaveValue("PREFERRED");
});
it("warns before discarding and refresh reloads only the saved version", async () => {
  const user = userEvent.setup();
  const view = page();
  await user.selectOptions(await day(), "BLOCKED");
  await user.click(screen.getByRole("button", { name: av.reset }));
  expect(window.confirm).toHaveBeenCalledWith(av.discard);
  expect(await day()).toHaveValue("BLOCKED");
  view.unmount();
  page();
  expect(await day()).toHaveValue("AVAILABLE");
  expect(data.submitted).toBe(false);
});
it("shows administrator counts and submitted versus missing status with read-only detail", async () => {
  const user = userEvent.setup();
  render(<AvailabilityAdmin />);
  expect(await screen.findByText("1 הגשות מתוך 2 עובדים פעילים")).toBeVisible();
  const table = screen.getByRole("table");
  expect(within(table).getByText("הוגש")).toBeVisible();
  expect(within(table).getByText("לא הוגש")).toBeVisible();
  await user.click(
    screen.getByRole("button", { name: `${av.view} — דנה כהן` }),
  );
  expect(await day()).toBeDisabled();
});
it("rejects invalid administrator date ranges locally", async () => {
  const user = userEvent.setup();
  render(<AvailabilityAdmin />);
  await screen.findByRole("table");
  await user.clear(screen.getByLabelText(av.opens));
  await user.type(screen.getByLabelText(av.opens), "2026-10-20T12:00");
  await user.clear(screen.getByLabelText(av.closes));
  await user.type(screen.getByLabelText(av.closes), "2026-10-19T12:00");
  await user.click(screen.getByRole("button", { name: av.configure }));
  expect(await screen.findByRole("alert")).toHaveTextContent(av.invalidTime);
  expect(writes).toHaveLength(0);
});
it("converts Israel times independently of host timezone and rejects ambiguous DST input", () => {
  expect(israelInstant("2026-10-24T19:00")).toBe("2026-10-24T16:00:00.000Z");
  expect(israelInstant("2026-10-25T07:00")).toBe("2026-10-25T05:00:00.000Z");
  expect(() => israelInstant("2026-10-25T01:30")).toThrow(
    "INVALID_WINDOW_TIME",
  );
  expect(() => israelInstant("2026-03-27T02:30")).toThrow(
    "INVALID_WINDOW_TIME",
  );
  expect(localInput("2026-10-24T16:00:00Z")).toBe("2026-10-24T19:00");
  expect(weekStart("2026-10-01")).toBe("2026-09-27");
  expect(weekStart("2027-01-01")).toBe("2026-12-27");
});
it("retains API error metadata for weekly-limit feedback", async () => {
  failure = "WEEKLY_BLOCK_LIMIT";
  await expect(api("/availability/2026/10", "POST", {})).rejects.toMatchObject({
    message: "WEEKLY_BLOCK_LIMIT",
    weekStartsOn: "2026-09-27",
    limit: 3,
  });
});

it("allows viewing a legacy submission even when no window has been configured", async () => {
  summary.window = {
    status: "NOT_CONFIGURED",
    administrativeState: "CLOSED",
    opensAt: null,
    closesAt: null,
  };
  render(<AvailabilityAdmin />);
  expect(
    await screen.findByRole("button", { name: `${av.view} — דנה כהן` }),
  ).toBeEnabled();
  expect(
    screen.getByRole("button", { name: `${av.view} — יעל לוי` }),
  ).toBeDisabled();
});
