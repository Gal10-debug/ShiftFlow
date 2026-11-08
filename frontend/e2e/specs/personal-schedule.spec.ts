import { test, expect, login, logout, api, raw, rowId, userId } from "../fixtures";
import type { Publication, Review } from "../../src/publication";

test("An employee cannot retrieve unpublished personal hours", async ({ page }) => {
  await login(page, 4); await page.goto("/my-schedule");
  expect((await raw(page, "/my-schedule/2031/1")).status()).toBe(404);
  await expect(page.getByText("עדיין לא פורסם עבורך סידור עבודה.", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toHaveCount(0);
});

test.describe("Monthly personal planned hours", () => {
  test.use({ profile: "qualified" });
  test("Published months have consistent hours, mobile RTL navigation, and historical summaries", async ({ page }) => {
    await login(page, 1);
    const path = "/admin/scheduling/periods/2031/2";
    for (const date of ["2031-02-03", "2031-02-05"]) {
      const r = await api<Review>(page, path);
      await api(page, `${path}/assignments`, "POST", { expectedToken: r.token, operation: "ASSIGN",
        assignmentId: null, userId: userId(4), definitionId: rowId(26), date });
    }
    let r = await api<Review>(page, path);
    r = await api<Review>(page, `${path}/review`, "POST", { expectedToken: r.token });
    await api(page, `${path}/publications/PRELIMINARY`, "POST", {
      expectedToken: r.token, acknowledgeShortages: true, acknowledgeOpenIssues: true, notes: null
    });
    await logout(page); await login(page, 4);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/my-schedule?period=2031-02");
    const summary = page.getByRole("region", { name: "סיכום חודשי מתוכנן" });
    await expect(summary).toContainText("שעות עבודה מתוכננות24");
    const personal = await api<Publication>(page, "/my-schedule/2031/2");
    expect(personal.personalSchedule?.summary.totalAssignedShifts).toBe(2);
    expect(personal.personalSchedule?.assignments.map(a => a.date)).toEqual(["2031-02-03", "2031-02-05"]);
    await expect(page.locator('section[dir="rtl"]').first()).toBeVisible();
    const table = page.getByRole("table", { name: "המשמרות שלי — דיווח על בעיות" });
    await expect(table).toContainText("07:00"); await expect(table).toContainText("19:00");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole("button", { name: "החודש הקודם שפורסם", exact: true }).click();
    await expect(summary).toContainText("שעות עבודה מתוכננות12");
    await expect(page.getByRole("combobox", { name: "חודש סידור", exact: true })).toHaveValue("2031/1");
    await page.getByRole("button", { name: "החודש הבא שפורסם", exact: true }).click();
    await expect(summary).toContainText("שעות עבודה מתוכננות24");
  });
});
