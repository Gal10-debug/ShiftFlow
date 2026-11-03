import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import ScheduleReviewPanel from "./components/ScheduleReviewPanel";
import RosterGrid, { rowColor } from "./components/RosterGrid";
import { api } from "./api";
import type { Review } from "./publication";
vi.mock("./api", () => ({ api: vi.fn() }));
const mocked = vi.mocked(api);
const definition = "40000000-0000-0000-0000-000000000016";
let review: Review;
beforeEach(() => {
  mocked.mockReset();
  review = { editingContractVersion: 1, state: "DRAFT", revision: 1, token: "a".repeat(64), needsReview: true, publicationReady: false,
    shortage: 2, openIssues: 0, conflicts: [], draft: { year: 2027, month: 5, legacyAssignments: [], cells: [{
      semantic: "WEEKEND_ON_CALL", definitionId: definition, name: "שם ששונה", date: "2027-05-01", displayOrder: 16,
      period: "DAY", timeRange: "DAY", startsAt: "2027-05-01T04:00:00Z", endsAt: "2027-05-01T16:00:00Z",
      requiredEmployees: 2, requiresShiftLeadQualification: false,
      employees: [{assignmentId: "assigned", userId: "me", name: "דנה כהן", isShiftLead: false, note: "להגיע מוקדם\n<script>bad()</script>"},
        {assignmentId: "other", userId: "other-user", name: "דנה כהן", isShiftLead: false}]
    }] } };
  mocked.mockImplementation(async (path, method = "GET") => {
    if (method !== "GET") return review;
    if (path.includes("candidates?")) return [{userId: "eligible",name: "עובד כשיר",eligible: true,reasons: [],isShiftLeadQualified: true,preferred: true},
      {userId: "blocked",name: "עובד חסום",eligible: false,reasons: ["HARD_BLOCK_CONFLICT"]}];
    if (path.endsWith("/publications") || path.endsWith("/issues")) return [];
    return review;
  });
});
function panel(assignmentId?: string) {
  return render(<ScheduleReviewPanel year={2027} month={5} reload={0} selected={{ definitionId: definition, date: "2027-05-01", assignmentId }} onChanged={vi.fn()} onClose={vi.fn()} />);
}
it("normal Saturday cell submits one ordinary command and explains automatic cross-month pairing", async () => {
  panel();
  const dialog = await screen.findByRole("dialog", {name: "עריכת שיבוץ"});
  expect(within(dialog).getByText(/יחול אוטומטית על שישי ושבת/)).toBeInTheDocument();
  expect(screen.queryByText("עריכת זוג כוננות סוף שבוע")).not.toBeInTheDocument();
  await userEvent.selectOptions(within(dialog).getByLabelText("עובד כשיר"), "eligible");
  await userEvent.click(within(dialog).getByRole("button",{name: "שמירת תיקון ידני"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith("/admin/scheduling/periods/2027/5/assignments", "POST", expect.objectContaining({operation: "ASSIGN", date: "2027-05-01", userId: "eligible", definitionId: definition})));
});
it("shows qualifications and preference, excludes invalid candidates and displays paired rejection in Hebrew", async () => {
  panel(); const dialog = await screen.findByRole("dialog");
  await screen.findByRole("option",{name: /עובד כשיר · מוסמך · העדפה/});
  expect(screen.queryByRole("option",{name: "עובד חסום"})).not.toBeInTheDocument();
  await userEvent.selectOptions(within(dialog).getByLabelText("עובד כשיר"), "eligible");
  mocked.mockRejectedValueOnce(new Error("WEEKEND_SHIFT_PROHIBITED"));
  await userEvent.click(within(dialog).getByRole("button",{name:"שמירת תיקון ידני"}));
  expect(await within(dialog).findByRole("alert")).toHaveTextContent("הפעולה כולה בוטלה");
});
it("assignment click selects that assignment and supports editing a public multiline note", async () => {
  panel("assigned"); const dialog = await screen.findByRole("dialog");
  expect(within(dialog).getByLabelText("שיבוץ קיים")).toHaveValue("assigned");
  await userEvent.selectOptions(within(dialog).getByLabelText("פעולה"),"NOTE");
  const note = within(dialog).getByLabelText(/הערה לשיבוץ/);
  expect(note).toHaveValue("להגיע מוקדם\n<script>bad()</script>");
  await userEvent.clear(note); await userEvent.type(note,"הערה חדשה");
  expect(within(dialog).getByText(/גלויה לכל העובדים/)).toBeInTheDocument();
  await userEvent.click(within(dialog).getByRole("button",{name:"שמירת תיקון ידני"}));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith(expect.any(String),"POST",expect.objectContaining({operation:"NOTE",assignmentId:"assigned",note:"הערה חדשה"})));
});
it("shared published board is read-only, separates employees and highlights by ID despite identical names", () => {
  const {container} = render(<RosterGrid snapshot={review.draft} currentUserId="me" />);
  expect(container.querySelectorAll(".roster-employee")).toHaveLength(2);
  expect(container.querySelectorAll(".own-assignment")).toHaveLength(1);
  expect(screen.getByText("★ השיבוץ שלי")).toBeInTheDocument();
  expect(screen.queryByRole("button")).not.toBeInTheDocument();
  expect(container.querySelector("script")).toBeNull();
  expect(container.querySelector(".assignment-note")).toHaveTextContent("<script>bad()</script>");
  expect(container.querySelector('[dir="rtl"]')).toBeInTheDocument();
  expect(rowColor(definition)).toBe(rowColor(definition));
  expect(container.querySelector("tbody tr")?.getAttribute("style")).toContain("background-color");
});
it("all 51 definitions disable only prohibited weekend cells without edit controls or artificial shortages", () => {
  const snapshot = {...review.draft,cells: Array.from({length:51},(_,i)=>({...review.draft.cells[0],definitionId:`40000000-0000-0000-0000-${String(i+1).padStart(12,"0")}`,name:`סוג ${i+1}`,semantic:i===15?"WEEKEND_ON_CALL" as const:"ORDINARY" as const,employees:[],requiredEmployees:0,applicable:[2,3,4,5,16].includes(i+1),displayOrder:i+1}))};
  const edit = vi.fn();const {container}=render(<RosterGrid snapshot={snapshot} onEdit={edit} />);
  expect(container.querySelectorAll('td[aria-disabled="true"]')).toHaveLength(46);
  expect(screen.getAllByRole("button")).toHaveLength(5);
  expect(screen.queryByText(/חסר עובד/)).not.toBeInTheDocument();
});
it("immutable published state has no manual editor", async () => {
  review.state="FINAL_PUBLISHED";panel("assigned");
  await screen.findByText("סידור עבודה סופי");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});
it("rejects a stale backend contract before submitting edits and explains restarting the server", async () => {
  delete review.editingContractVersion;
  panel("assigned");
  expect(await screen.findByRole("alert")).toHaveTextContent("שרת הסידור פועל בגרסה שאינה תואמת");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(mocked.mock.calls.filter(([,method])=>method === "POST")).toHaveLength(0);
});
it("shows safe field-specific Hebrew feedback for an actual note validation failure", async () => {
  panel("assigned"); const dialog = await screen.findByRole("dialog");
  await userEvent.selectOptions(within(dialog).getByLabelText("פעולה"),"NOTE");
  mocked.mockRejectedValueOnce(Object.assign(new Error("VALIDATION_ERROR"),{fieldErrors:{note:"INVALID_ASSIGNMENT_NOTE"}}));
  await userEvent.click(within(dialog).getByRole("button",{name:"שמירת תיקון ידני"}));
  expect(await within(dialog).findByRole("alert")).toHaveTextContent("הערת שיבוץ יכולה להכיל עד 1000 תווים");
});
it.each(["DRAFT","PRELIMINARY_PUBLISHED","UNDER_CORRECTION","FINAL_PUBLISHED"].flatMap(state=>["", "2027-04-25"].map(week=>[state,week])))
("keeps per-cell demand without aggregate shortages in state %s and week %s", async (state, week) => {
  review.state=state;review.shortage=12;
  const {container}=render(<><ScheduleReviewPanel year={2027} month={5} reload={0} selected={null} onChanged={vi.fn()} /><RosterGrid snapshot={review.draft} currentUserId="me" week={week} /></>);
  await screen.findByText(/דיווחים פתוחים:/);
  expect(screen.queryByLabelText("סיכום איוש")).not.toBeInTheDocument();
  expect(screen.queryByText(/חוסרים:|חסרים:|תאים בחוסר|הצגת רשימת תאים בחוסר/)).not.toBeInTheDocument();
  expect(container.querySelector(".shortage-list")).toBeNull();
  expect(screen.getByText("נדרשים: 2 · שובצו: 2")).toBeInTheDocument();
  const acknowledgment=screen.queryByRole("checkbox",{name:/אני מאשר פרסום עם 12/});
  if(state==="FINAL_PUBLISHED")expect(acknowledgment).not.toBeInTheDocument();
  else expect(acknowledgment).toBeInTheDocument();
});
