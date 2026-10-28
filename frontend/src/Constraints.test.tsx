import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import OnCallPairEditor from "./components/OnCallPairEditor";
import { api } from "./api";
import { he } from "./he";
import type { Review } from "./publication";
vi.mock("./api", () => ({
  api: vi.fn()
}));
const mocked = vi.mocked(api);
const review: Review = {
  state: "DRAFT",
  revision: 1,
  token: "a".repeat(64),
  needsReview: true,
  publicationReady: false,
  shortage: 2,
  openIssues: 0,
  conflicts: [],
  draft: {
    year: 2027,
    month: 4,
    legacyAssignments: [],
    cells: [{
      semantic: "WEEKEND_ON_CALL",
      definitionId: "on-call",
      name: "שם ששונה",
      date: "2027-04-30",
      displayOrder: 26,
      period: "DAY",
      timeRange: "DAY",
      startsAt: "2027-04-30T04:00:00Z",
      endsAt: "2027-04-30T16:00:00Z",
      requiredEmployees: 1,
      requiresShiftLeadQualification: false,
      employees: []
    }]
  }
};
beforeEach(() => {
  mocked.mockReset();
  vi.spyOn(window, "confirm").mockReturnValue(true);
  mocked.mockImplementation(async (_, method = "GET") => method === "GET" ? [{
    userId: "employee",
    name: "דנה כהן",
    eligible: false,
    reasons: ["ON_CALL_PAIR_REQUIRED"]
  }] : review);
});
it("uses stable classification despite renaming and explicitly submits the adjacent-period decision", async () => {
  const user = userEvent.setup();
  const changed = vi.fn();
  const {
    container
  } = render(<OnCallPairEditor review={review} base="/period" onChanged={changed} />);
  await user.type(screen.getByLabelText("יום שישי של הזוג"), "2027-04-30");
  await waitFor(() => expect(screen.getByRole("option", {
    name: "דנה כהן"
  })).toBeInTheDocument());
  await user.selectOptions(screen.getByLabelText("עובד לזוג"), "employee");
  await user.click(screen.getByRole("checkbox"));
  await user.click(screen.getByRole("button", {
    name: "שמירת זוג כוננות"
  }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/period/on-call-pair", "POST", expect.objectContaining({
    friday: "2027-04-30",
    operation: "ASSIGN",
    userId: "employee",
    acknowledgeAdjacentPeriod: true,
    expectedToken: review.token
  })));
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  expect(changed).toHaveBeenCalled();
});
it("does not write when explicit confirmation is canceled", async () => {
  const user = userEvent.setup();
  render(<OnCallPairEditor review={review} base="/period" onChanged={vi.fn()} />);
  await user.type(screen.getByLabelText("יום שישי של הזוג"), "2027-04-30");
  await waitFor(() => expect(screen.getByRole("option", {
    name: "דנה כהן"
  })).toBeInTheDocument());
  await user.selectOptions(screen.getByLabelText("עובד לזוג"), "employee");
  vi.mocked(window.confirm).mockReturnValue(false);
  await user.click(screen.getByRole("button", {
    name: "שמירת זוג כוננות"
  }));
  expect(mocked.mock.calls.filter(([, m]) => m === "POST")).toHaveLength(0);
});
it("shows a clear Hebrew explanation for hard-constraint failures", async () => {
  const user = userEvent.setup();
  render(<OnCallPairEditor review={review} base="/period" onChanged={vi.fn()} />);
  await user.type(screen.getByLabelText("יום שישי של הזוג"), "2027-04-30");
  await waitFor(() => expect(screen.getByRole("option", {
    name: "דנה כהן"
  })).toBeInTheDocument());
  await user.selectOptions(screen.getByLabelText("עובד לזוג"), "employee");
  mocked.mockRejectedValueOnce(new Error("WEEKEND_ALLOCATION_LIMIT"));
  await user.click(screen.getByRole("button", {
    name: "שמירת זוג כוננות"
  }));
  expect(await screen.findByRole("alert")).toHaveTextContent(he.errors.WEEKEND_ALLOCATION_LIMIT);
});

it("loads candidates from Saturday's month for an explicit cross-month pair", async () => {
  const may = { ...review, draft: { ...review.draft, month: 5 } };
  render(<OnCallPairEditor review={may} base="/may" onChanged={vi.fn()} />);
  await userEvent.type(screen.getByLabelText("יום שישי של הזוג"), "2027-04-30");
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/may/candidates?definitionId=on-call&date=2027-05-01"));
});
