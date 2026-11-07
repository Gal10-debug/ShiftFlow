import { test, expect, login, logout, api, raw, board, edit, saveEdit, manual, review, publish, state, rowId, userId, periodPath, correction, saveConfiguration } from "../fixtures";
import type { Candidate } from "../../src/publication";

async function generate(page: import("@playwright/test").Page) {
  await page.getByLabel("חודש ליצירת סידור", { exact: true }).fill("2031-01");
  await page.getByRole("button", { name: "צור סידור אוטומטי", exact: true }).click();
  await expect(page.getByText("תצוגה מקדימה — השיבוצים טרם נשמרו", { exact: true })).toBeVisible({ timeout: 70_000 });
  expect((await state(page)).assignments).toHaveLength(0);
  await page.getByRole("button", { name: "אישור ושמירת טיוטה", exact: true }).click();
  await expect(page.getByText("הסידור נשמר כטיוטה שלא פורסמה", { exact: true })).toBeVisible();
  expect((await state(page)).assignments.length).toBeGreaterThan(0);
}

test("Monthly generation is an explicit saved draft, followed by preliminary, correction, and immutable final publication", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page, 1); await board(page); await generate(page);
  const saved = await state(page);
  await page.reload();
  expect((await state(page)).assignments).toEqual(saved.assignments);
  await publish(page);
  const first = (await state(page)).publications[0];
  const initial = await api<{ snapshot: { cells: { employees: { userId: string }[] }[] } }[]>(page, `${periodPath}/publications`);
  const assignedUser = initial[0].snapshot.cells.flatMap(c => c.employees).find(e => e.userId !== userId(9))!.userId;
  const employeeNumber = Number(assignedUser.slice(-12));
  await logout(page); await login(page, employeeNumber);
  await page.goto("/my-schedule?period=2031-01");
  await expect(page.getByRole("heading", { name: "סידור עבודה ראשוני", exact: true })).toBeVisible();
  await expect(page.getByText("★ השיבוץ שלי", { exact: true }).first()).toBeVisible();
  await logout(page); await login(page, 1); await board(page);
  await correction(page);
  const r = await review(page);
  const cell = r.draft.cells.find(c => c.date >= "2031-01-05" && c.date <= "2031-01-11" && c.definitionId === rowId(26) && c.employees.length)!;
  const dialog = await edit(page, cell.date);
  await dialog.getByRole("combobox", { name: "פעולה", exact: true }).selectOption("NOTE");
  await dialog.getByRole("combobox", { name: "שיבוץ קיים", exact: true }).selectOption(cell.employees[0].assignmentId);
  await dialog.getByRole("textbox", { name: /^הערה לשיבוץ/ }).fill("תיקון מבוקר לפני פרסום סופי");
  await saveEdit(page); await publish(page, true);
  const final = await state(page);
  expect(final.publications).toHaveLength(2);
  expect(final.publications[0]).toEqual(first);
  expect(final.publications[1].hash).not.toBe(first.hash);
  await logout(page); await login(page, employeeNumber); await page.goto("/my-schedule?period=2031-01");
  await expect(page.getByRole("heading", { name: "סידור עבודה סופי", exact: true })).toBeVisible();
  await expect(page.getByText("תיקון מבוקר לפני פרסום סופי", { exact: true })).toBeVisible();
  await page.getByRole("combobox", { name: "גרסת פרסום", exact: true }).selectOption("1");
  await expect(page.getByRole("heading", { name: "סידור עבודה ראשוני", exact: true })).toBeVisible();
  await expect(page.getByText("תיקון מבוקר לפני פרסום סופי", { exact: true })).toHaveCount(0);
});

test.describe("Shortage fixture", () => {
  test.use({ profile: "shortage" });
  test("Priorities persist independently of display order and generation preserves shortages and hard limits", async ({ page }) => {
    test.setTimeout(150_000);
    await login(page, 1); await board(page);
    await page.getByRole("button", { name: "הגדרות סוגי משמרות", exact: true }).click();
    for (const [slot, priority] of [[26, "Critical"], [27, "High"], [28, "Normal"]] as const) {
      await page.getByRole("combobox", { name: `עדיפות שיבוץ ${slot}`, exact: true }).selectOption(priority);
      if (slot !== 26) {
        await page.getByLabel(`עובדים נדרשים ${slot}`, { exact: true }).fill("3");
        await page.getByRole("combobox", { name: `טווח שעות ${slot}`, exact: true }).selectOption("DAY");
        await page.getByLabel(`השתתפות בשיבוץ אוטומטי עתידי ${slot}`, { exact: true }).check();
        await page.getByLabel(`שורת אחראי משמרת ${slot}`, { exact: true }).check();
      }
    }
    await page.getByLabel("סדר תצוגה 26", { exact: true }).fill("27");
    await page.getByLabel("סדר תצוגה 27", { exact: true }).fill("26");
    await saveConfiguration(page);
    await expect(page.getByLabel("עדיפות שיבוץ 26", { exact: true })).toHaveValue("Critical");
    await page.reload();
    await page.getByRole("button", { name: "הגדרות סוגי משמרות", exact: true }).click();
    for (const [slot, priority] of [[26, "Critical"], [27, "High"], [28, "Normal"]] as const)
      await expect(page.getByLabel(`עדיפות שיבוץ ${slot}`, { exact: true })).toHaveValue(priority);
    await expect(page.getByLabel("סדר תצוגה 26", { exact: true })).toHaveValue("27");
    await page.getByRole("button", { name: "סגירת הגדרות", exact: true }).click();
    await generate(page);
    const r = await review(page);
    expect(r.conflicts).toHaveLength(0);
    const cells = r.draft.cells;
    for (const cell of cells) {
      expect(cell.employees.length).toBeLessThanOrEqual(cell.requiredEmployees);
      if (cell.requiredEmployees === 0) expect(cell.employees).toHaveLength(0);
      for (const e of cell.employees) expect([userId(4), userId(6), userId(8)]).toContain(e.userId);
    }
    expect(cells.filter(c => [rowId(27), rowId(28)].includes(c.definitionId)).flatMap(c => c.employees)).toHaveLength(0);
    expect(cells.filter(c => c.definitionId === rowId(26)).flatMap(c => c.employees).length).toBeGreaterThan(0);
    await expect(page.getByText(/חסרים \d+ עובדים/).first()).toBeVisible();
    await page.reload(); expect((await review(page)).draft).toEqual(r.draft);
  });
});

test("Manual add, replace, move, and remove persist and update staffing without exceeding capacity", async ({ page }) => {
  await login(page, 1); await board(page);
  let dialog = await edit(page);
  await dialog.getByRole("combobox", { name: "עובד כשיר", exact: true }).selectOption(userId(4));
  await saveEdit(page);
  expect((await state(page)).assignments).toHaveLength(1);
  dialog = await edit(page);
  await expect(dialog.getByRole("combobox", { name: "עובד כשיר", exact: true }).getByRole("option")).toHaveCount(1);
  await expect(dialog.getByRole("button", { name: "שמירת תיקון ידני", exact: true })).toBeDisabled();
  await dialog.getByRole("combobox", { name: "פעולה", exact: true }).selectOption("REPLACE");
  await dialog.getByRole("combobox", { name: "שיבוץ קיים", exact: true }).selectOption((await state(page)).assignments[0].id);
  await dialog.getByRole("combobox", { name: "עובד כשיר", exact: true }).selectOption(userId(6));
  await saveEdit(page);
  expect((await state(page)).assignments[0].userId).toBe(userId(6));
  dialog = await edit(page);
  await dialog.getByRole("combobox", { name: "פעולה", exact: true }).selectOption("MOVE");
  await dialog.getByRole("combobox", { name: "שיבוץ קיים", exact: true }).selectOption((await state(page)).assignments[0].id);
  await dialog.getByLabel("תאריך שיבוץ", { exact: true }).fill("2031-01-07");
  await saveEdit(page); await board(page);
  const moved = (await review(page)).draft.cells.find(c => c.date === "2031-01-07" && c.definitionId === rowId(26))!;
  expect(moved.employees.map(e => e.userId)).toEqual([userId(6)]);
  const cell = page.getByRole("cell").filter({ has: page.getByRole("button", { name: "עריכת משמרת בדיקה 2031-01-05", exact: true }) });
  await expect(cell).toContainText("חסר עובד 1");
  dialog = await edit(page, "2031-01-07");
  await dialog.getByRole("combobox", { name: "פעולה", exact: true }).selectOption("REMOVE");
  await dialog.getByRole("combobox", { name: "שיבוץ קיים", exact: true }).selectOption(moved.employees[0].assignmentId);
  await saveEdit(page); await page.reload(); expect((await state(page)).assignments).toHaveLength(0);
});

test("Manual UI and API reject overlaps, consecutive twelve-hour work, missing qualifications, and zero capacity", async ({ page }) => {
  await login(page, 1); await board(page);
  await page.getByRole("button", { name: "הגדרות סוגי משמרות", exact: true }).click();
  for (const slot of [27, 28, 30]) await page.getByLabel(`עובדים נדרשים ${slot}`, { exact: true }).fill("1");
  await page.getByRole("combobox", { name: "טווח שעות 27", exact: true }).selectOption("DAY");
  await page.getByRole("combobox", { name: "טווח שעות 28", exact: true }).selectOption("NIGHT");
  await page.getByLabel("שורת אחראי משמרת 30", { exact: true }).check();
  await saveConfiguration(page);
  expect((await manual(page, "ASSIGN", { definitionId: rowId(26), date: "2031-01-05", userId: userId(4) })).status()).toBe(200);
  for (const [slot, employee, reason] of [[27, 4, "WORK_OVERLAP_OR_CONSECUTIVE"], [28, 4, "WORK_OVERLAP_OR_CONSECUTIVE"], [30, 5, "QUALIFICATION_REQUIRED"], [29, 6, "STAFFING_EXCEEDED"]] as const) {
    const candidates = await api<Candidate[]>(page, `${periodPath}/candidates?definitionId=${rowId(slot)}&date=2031-01-05`);
    expect(candidates.find(c => c.userId === userId(employee))?.reasons).toContain(reason);
    const response = await manual(page, "ASSIGN", { definitionId: rowId(slot), date: "2031-01-05", userId: userId(employee) });
    expect(response.status()).toBe(409);
  }
  await board(page);
  const r = await review(page);
  for (const [slot, employee] of [[27, 4], [28, 4], [30, 5], [29, 6]]) {
    const name = r.draft.cells.find(c => c.definitionId === rowId(slot))!.name;
    const dialog = await edit(page, "2031-01-05", name);
    await expect(dialog.getByRole("combobox", { name: "עובד כשיר", exact: true }).locator(`option[value='${userId(employee)}']`)).toHaveCount(0);
    await dialog.getByRole("button", { name: "סגירה", exact: true }).click();
  }
  expect((await state(page)).assignments).toHaveLength(1);
  expect((await review(page)).conflicts).toHaveLength(0);
});

test.describe("Protected preliminary roster", () => {
  test.use({ profile: "published" });
  test("An administrator must enter correction before altering a protected published roster", async ({ page }) => {
    await login(page, 1); await board(page);
    const before = await state(page);
    await expect(page.getByRole("button", { name: "עריכת משמרת בדיקה 2031-01-05", exact: true })).toHaveCount(0);
    const response = await manual(page, "REMOVE", { assignmentId: before.assignments[0].id });
    expect(response.status()).toBe(409);
    expect((await response.json()).code).toBe("INVALID_SCHEDULE_TRANSITION");
    expect(await state(page)).toEqual(before);
  });
});

for (const [button, operation] of [["הסתרת הפרסום", "hide"], ["מחיקת הפרסום", "delete"]] as const) {
  test.describe(`Publication control ${operation}`, () => {
    test.use({ profile: "published" });
    test(`${operation} removes employee visibility while retaining immutable history`, async ({ page }) => {
      await login(page, 1); await board(page);
      const before = (await state(page)).publications;
      await page.getByText(/גרסה 1 · ראשוני/).click();
      await page.getByRole("button", { name: button, exact: true }).click();
      const dialog = page.getByRole("dialog", { name: "אישור שינוי פרסום", exact: true });
      await dialog.getByLabel("סיבת הפעולה", { exact: true }).fill("בדיקת נראות בלי מחיקת ההיסטוריה");
      await dialog.getByRole("button", { name: "אישור הפעולה", exact: true }).click();
      await expect(dialog).not.toBeVisible();
      const after = await state(page);
      expect(after.publications).toEqual(before);
      expect(after.controls).toHaveLength(1);
      await logout(page); await login(page, 4); await page.goto("/my-schedule?period=2031-01");
      expect((await raw(page, "/my-schedule/2031/1")).status()).toBe(404);
      await expect(page.getByRole("heading", { name: "סידור עבודה ראשוני", exact: true })).toHaveCount(0);
    });
  });
}
