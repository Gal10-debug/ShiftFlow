import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { beforeEach, expect, it, vi } from "vitest";
import AutomaticGeneration from "./components/AutomaticGeneration";
import type { Generation, ShiftTypeDefinition } from "./scheduling";
import { api } from "./api";
vi.mock("./api", () => ({ api: vi.fn() }));
const mocked = vi.mocked(api);
const definition: ShiftTypeDefinition = { id: "row", slot: 1, name: "בוקר", displayOrder: 1, period: "DAY", requiredEmployees: 3, includeInAutomaticScheduling: true, version: 1, updatedAt: "" };
const ready: Generation = { id: "run", year: 2026, month: 10, state: "READY", solverStatus: "FEASIBLE", provenObjectives: 0, coverageOptimal: false, publicationReady: false, errorCode: null, expiresAt: "2026-10-26", cells: [{ definitionId: "row", date: "2026-10-01", startsAt: "", endsAt: "", requiredEmployees: 3, employees: [{ userId: "e", name: "ישראל ישראלי", protected: false, isShiftLead: false }], shortage: 2 }], summary: { required: 3, filled: 1, missing: 2, understaffedCells: 1, fullyStaffedCells: 0 } };
function Harness({ initial = null }: { initial?: Generation | null }) {
  const [value, setValue] = useState(initial);
  return <AutomaticGeneration definitions={[definition]} value={value} onChange={setValue} onSaved={vi.fn()} disabled={false} />;
}
beforeEach(() => { mocked.mockReset(); vi.spyOn(window, "confirm").mockReturnValue(true); });
it("shows saved requirements and starts monthly generation", async () => {
  mocked.mockResolvedValue({ ...ready, state: "QUEUED", summary: null, cells: [] });
  render(<Harness />);
  await userEvent.click(screen.getByText(/סוגי משמרות ודרישות איוש/));
  expect(screen.getByText("בוקר: 3 עובדים")).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "צור סידור אוטומטי" }));
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/generation", "POST", expect.objectContaining({ year: expect.any(Number), month: expect.any(Number) }));
  expect(screen.getByRole("status")).toHaveTextContent("ממתין להרצה");
});
it("shows preview summary and shortages without claiming optimality", async () => {
  render(<Harness initial={ready} />);
  expect(screen.getByRole("status")).toHaveTextContent("טרם נשמרו");
  expect(screen.getByLabelText("סיכום איוש")).toHaveTextContent("נדרשים: 3 · שובצו: 1 · חסרים: 2");
  expect(screen.getByText(/ייתכן שניתן לצמצם/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole("checkbox"));
  expect(screen.getByText(/חסרים 2 עובדים/)).toBeInTheDocument();
});
it("cancel removes the preview using the cancellation endpoint", async () => {
  mocked.mockResolvedValue(undefined); render(<Harness initial={ready} />);
  await userEvent.click(screen.getByRole("button", { name: "ביטול תצוגה מקדימה" }));
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/generation/run", "DELETE");
  expect(screen.queryByText(/טרם נשמרו/)).not.toBeInTheDocument();
});
it("requires explicit confirmation before saving and marks saved draft", async () => {
  mocked.mockResolvedValue({ ...ready, state: "SAVED" }); render(<Harness initial={ready} />);
  vi.mocked(window.confirm).mockReturnValueOnce(false);
  await userEvent.click(screen.getByRole("button", { name: "אישור ושמירת טיוטה" }));
  expect(mocked).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole("button", { name: "אישור ושמירת טיוטה" }));
  expect(mocked).toHaveBeenCalledWith("/admin/scheduling/generation/run/save", "POST");
  expect(await screen.findByRole("status")).toHaveTextContent("נשמר כטיוטה שלא פורסמה");
});
it("shows stale save error while retaining preview", async () => {
  mocked.mockRejectedValue(new Error("STALE_PREVIEW")); render(<Harness initial={ready} />);
  await userEvent.click(screen.getByRole("button", { name: "אישור ושמירת טיוטה" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("הנתונים השתנו");
  expect(screen.getByRole("status")).toHaveTextContent("טרם נשמרו");
});
it("distinguishes timeout from technical failure", () => {
  const { rerender } = render(<Harness initial={{ ...ready, state: "TIMEOUT", summary: null, cells: [] }} />);
  expect(screen.getByRole("alert")).toHaveTextContent("תם זמן החישוב ללא פתרון");
  rerender(<AutomaticGeneration definitions={[]} value={{ ...ready, state: "FAILED", summary: null }} onChange={vi.fn()} onSaved={vi.fn()} disabled={false} />);
  expect(screen.getByRole("alert")).toHaveTextContent("ההרצה נכשלה");
});
it("polls asynchronously and displays completed preview", async () => {
  mocked.mockResolvedValue(ready); render(<Harness initial={{ ...ready, state: "RUNNING", summary: null }} />);
  expect(screen.getByRole("status")).toHaveTextContent("מחשב סידור");
  await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("טרם נשמרו"), { timeout: 2500 });
});

it("explains how to configure automatic scheduling when no rows are included", () => {
  render(<AutomaticGeneration definitions={[{ ...definition, includeInAutomaticScheduling: false }]} value={null} onChange={vi.fn()} onSaved={vi.fn()} disabled={false} />);
  expect(screen.getByRole("button", { name: "צור סידור אוטומטי" })).toBeDisabled();
  expect(screen.getByText(/לא הוגדרו משמרות לשיבוץ אוטומטי/)).toHaveTextContent("מספר עובדים גדול מאפס");
  expect(mocked).not.toHaveBeenCalled();
});
it("shows immediate feedback while the generation request is pending", async () => {
  let finish!: (value: unknown) => void;
  mocked.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  render(<Harness />);
  await userEvent.click(screen.getByRole("button", { name: "צור סידור אוטומטי" }));
  expect(screen.getByRole("status")).toHaveTextContent("שולח בקשה");
  expect(screen.getByRole("button", { name: /שולח בקשה/ })).toBeDisabled();
  finish({ ...ready, state: "QUEUED", cells: [], summary: null });
  await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("ממתין להרצה"));
});
it("shows the reason generation is blocked by unsaved settings", () => {
  render(<AutomaticGeneration definitions={[definition]} value={null} onChange={vi.fn()} onSaved={vi.fn()} disabled disabledReason="יש לשמור את השינויים לפני יצירת סידור" />);
  expect(screen.getByRole("button", { name: "צור סידור אוטומטי" })).toBeDisabled();
  expect(screen.getByText("יש לשמור את השינויים לפני יצירת סידור")).toBeInTheDocument();
});
