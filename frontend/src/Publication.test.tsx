import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import ScheduleReviewPanel from "./components/ScheduleReviewPanel";
import MySchedule from "./pages/MySchedule";
import { api } from "./api";
import { compareVersions, type Review, type Publication } from "./publication";
vi.mock("./api", () => ({
  api: vi.fn()
}));
const mocked = vi.mocked(api);
let review: Review;
let publication: Publication;
beforeEach(() => {
  mocked.mockReset();
  vi.spyOn(window, "confirm").mockReturnValue(true);
  const draft = {
    year: 2026,
    month: 10,
    cells: [{
      definitionId: "row",
      date: "2026-10-01",
      name: "בוקר",
      displayOrder: 1,
      period: "DAY",
      timeRange: "DAY",
      startsAt: "2026-10-01T04:00:00Z",
      endsAt: "2026-10-01T16:00:00Z",
      requiredEmployees: 1,
      requiresShiftLeadQualification: false,
      employees: [{
        assignmentId: "assignment",
        userId: "employee",
        name: "דנה כהן",
        isShiftLead: false
      }]
    }],
    legacyAssignments: []
  };
  review = {
    state: "DRAFT",
    revision: 1,
    token: "a".repeat(64),
    needsReview: false,
    publicationReady: true,
    shortage: 0,
    openIssues: 0,
    conflicts: [],
    draft
  };
  publication = {
    id: "pub",
    versionNumber: 1,
    publicationType: "PRELIMINARY",
    publishedAt: "2026-10-27T12:00:00Z",
    publishedByUserId: "admin",
    notes: null,
    snapshot: draft
  };
  mocked.mockImplementation(async (path, method = "GET") => {
    if (path === "/my-schedule/periods") return [{
      year: 2026,
      month: 10
    }];
    if (path.endsWith("/candidates") || path.includes("/candidates?")) return [{
      userId: "employee",
      name: "דנה כהן",
      eligible: true,
      reasons: []
    }, {
      userId: "leader",
      name: "ראש צוות",
      eligible: false,
      reasons: ["TEAM_LEADER_ASSIGNED"]
    }];
    if (path.endsWith("/publications")) return [publication];
    if (path.endsWith("/issues")) return method === "GET" ? [] : {};
    if (path.startsWith("/my-schedule/")) return publication;
    if (method !== "GET") return review;
    return review;
  });
});
const panel = () => render(<ScheduleReviewPanel year={2026} month={10} reload={0} selected={{
  definitionId: "row",
  date: "2026-10-01"
}} onChanged={vi.fn()} />);
it("reviews saved draft, staffing and history in Hebrew RTL", async () => {
  const {
    container
  } = panel();
  expect(await screen.findByText("טיוטה")).toBeInTheDocument();
  expect(screen.getByText(/הטיוטה תקינה לפרסום/)).toBeInTheDocument();
  expect(screen.getByText(/גרסה 1/)).toBeInTheDocument();
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
});
it("requires explicit publication confirmation and sends current token", async () => {
  const user = userEvent.setup();
  panel();
  const button = await screen.findByRole("button", {
    name: "פרסום ראשוני"
  });
  vi.mocked(window.confirm).mockReturnValueOnce(false);
  await user.click(button);
  expect(mocked.mock.calls.filter(([, method]) => method === "POST")).toHaveLength(0);
  await user.click(button);
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2026/10/publications/PRELIMINARY", "POST", expect.objectContaining({
    expectedToken: review.token
  })));
});
it("requires shortage acknowledgment before publication", async () => {
  review.shortage = 2;
  panel();
  const button = await screen.findByRole("button", {
    name: "פרסום ראשוני"
  });
  expect(button).toBeDisabled();
  await userEvent.click(screen.getByRole("checkbox", {
    name: /2 מקומות/
  }));
  expect(button).toBeEnabled();
});
it("manual assignment submits explicit target and explains excluded candidates", async () => {
  const user = userEvent.setup();
  panel();
  const selector = await screen.findByLabelText("עובד כשיר");
  await waitFor(() => expect(screen.getByRole("option", {
    name: "דנה כהן"
  })).toBeInTheDocument());
  await user.selectOptions(selector, "employee");
  await user.click(screen.getByRole("button", {
    name: "שמירת תיקון ידני"
  }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2026/10/assignments", "POST", expect.objectContaining({
    operation: "ASSIGN",
    userId: "employee",
    definitionId: "row",
    date: "2026-10-01",
    expectedToken: review.token
  })));
});
it("shows correction mode while preserving preliminary publication history", async () => {
  review.state = "PRELIMINARY_PUBLISHED";
  panel();
  await userEvent.click(await screen.findByRole("button", {
    name: "כניסה למצב תיקונים"
  }));
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2026/10/correction", "POST", {
    expectedToken: review.token
  });
  expect(screen.queryByRole("button", {
    name: "שמירת תיקון ידני"
  })).not.toBeInTheDocument();
});
it("final publication requires visible unresolved report acknowledgment", async () => {
  review.state = "UNDER_CORRECTION";
  review.openIssues = 1;
  panel();
  const final = await screen.findByRole("button", {
    name: "פרסום סופי"
  });
  expect(final).toBeDisabled();
  await userEvent.click(screen.getByRole("checkbox", {
    name: /1 דיווחים/
  }));
  await userEvent.click(final);
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2026/10/publications/FINAL", "POST", expect.objectContaining({
    acknowledgeOpenIssues: true
  }));
});
it("compares immutable version snapshots including staffing and historical names", () => {
  const a = publication.snapshot!;
  const b = {
    ...a,
    cells: a.cells.map(c => ({
      ...c,
      name: "שם חדש",
      requiredEmployees: 3
    }))
  };
  const diff = compareVersions(a, b);
  expect(diff).toHaveLength(1);
  expect(diff[0].before?.name).toBe("בוקר");
  expect(diff[0].after?.requiredEmployees).toBe(3);
});
it("employees see their preliminary schedule and explicitly submit an assignment issue", async () => {
  const user = userEvent.setup();
  const {
    container
  } = render(<MySchedule />);
  expect(await screen.findByRole("heading", {
    name: "סידור עבודה ראשוני"
  })).toBeInTheDocument();
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  await user.click(screen.getByRole("button", {
    name: "דיווח על בעיה"
  }));
  await user.type(screen.getByLabelText("תיאור הבעיה"), "יש לי בעיה במשמרת הזאת");
  expect(mocked.mock.calls.filter(([, m]) => m === "POST")).toHaveLength(0);
  await user.click(screen.getByRole("button", {
    name: "שליחת דיווח"
  }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/my-schedule/2026/10/issues", "POST", {
    publicationId: "pub",
    assignmentId: "assignment",
    explanation: "יש לי בעיה במשמרת הזאת"
  }));
});
it("employees see final label without issue action and can choose weekly view", async () => {
  publication.publicationType = "FINAL";
  render(<MySchedule />);
  await screen.findByRole("heading", {
    name: "סידור עבודה סופי"
  });
  expect(screen.queryByRole("button", {
    name: "דיווח על בעיה"
  })).not.toBeInTheDocument();
  await userEvent.selectOptions(screen.getByLabelText("תצוגה שבועית"), "2026-09-27");
  expect(screen.getByText("בוקר")).toBeInTheDocument();
});
it("denies administrative review when the server returns forbidden", async () => {
  mocked.mockRejectedValue(new Error("FORBIDDEN"));
  panel();
  expect(await screen.findByRole("alert")).toHaveTextContent("אין לך הרשאה");
  expect(screen.queryByRole("button", {
    name: "פרסום ראשוני"
  })).not.toBeInTheDocument();
});
it("shows stale draft warning and requires an explicit fresh review", async () => {
  review.needsReview = true;
  review.publicationReady = false;
  panel();
  expect(await screen.findByRole("button", {
    name: "פרסום ראשוני"
  })).toBeDisabled();
  await userEvent.click(screen.getByRole("button", {
    name: "אישור בדיקת זמינות ואילוצים"
  }));
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2026/10/review", "POST", {
    expectedToken: review.token
  });
});
