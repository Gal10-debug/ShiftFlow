import { test, expect, login, logout, api, raw } from "../fixtures";
import type { AdminAnalytics, PersonalAnalytics } from "../../src/analytics";

test("Unpublished analytics distinguish missing publication from zero work", async ({ page }) => {
  await login(page, 4); await page.goto("/my-statistics?period=2031-01");
  await expect(page.getByText("אין פרסומים זמינים לתקופה שנבחרה.", { exact: true })).toBeVisible();
  expect((await raw(page, "/admin/statistics/2031/1")).status()).toBe(403);
  expect((await raw(page, "/my-statistics/2031/1?employeeId=71000000-0000-0000-0000-000000000006")).status()).toBe(400);
});

test.describe("Published statistics", () => {
  test.use({ profile: "qualified" });
  test("Employee navigates monthly and yearly statistics in mobile RTL", async ({ page }) => {
    await login(page, 4); await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/my-statistics?period=2031-01");
    const summary = page.getByRole("region", { name: "סיכום סטטיסטיקות", exact: true });
    await expect(summary).toContainText("שעות מתוכננות ידועות12");
    await expect(summary).toContainText("סה״כ משמרות1");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await expect(page.locator('section[dir="rtl"]').first()).toBeVisible();
    await page.getByRole("combobox", { name: "תקופת דיווח", exact: true }).selectOption("year");
    await expect(summary).toContainText("שעות מתוכננות ידועות12");
    await expect(page.getByRole("table", { name: "מגמה חודשית וגרסאות פרסום" }).locator("tbody tr")).toHaveCount(12);
    const annual = await api<PersonalAnalytics>(page, "/my-statistics/2031");
    expect(annual.totals.totalAssignedShifts).toBe(annual.months.reduce((sum, m) => sum + m.totals.totalAssignedShifts, 0));
    await page.getByRole("combobox", { name: "תקופת דיווח", exact: true }).selectOption("month");
    await page.getByRole("combobox", { name: "חודש", exact: true }).selectOption("2");
    await expect(page.getByText("אין פרסומים זמינים לתקופה שנבחרה.", { exact: true })).toBeVisible();
    await page.goto("/statistics"); await expect(page.getByRole("heading", { name: "אין לך הרשאה לעמוד הזה" })).toBeVisible();
  });
  test("Administrator sees organization coverage, team filters and yearly workload", async ({ page }) => {
    await login(page, 1); await page.goto("/statistics?period=2031-01");
    const data = await api<AdminAnalytics>(page, "/admin/statistics/2031/1");
    await expect(page.getByRole("region", { name: "כיסוי תקנים", exact: true })).toContainText(String(data.coverage!.unfilledPositions));
    const teams = await api<{ id: string; name: string }[]>(page, "/admin/teams");
    await page.getByRole("combobox", { name: "צוות", exact: true }).selectOption(teams[1].id);
    await expect(page.getByText("אין שיבוצים בפרסומים הזמינים עבור הבחירה הנוכחית.", { exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "כיסוי תקנים", exact: true })).toContainText("אין שיוך תקנים לצוותים");
    await page.getByRole("combobox", { name: "צוות", exact: true }).selectOption(teams[0].id);
    await expect(page.getByRole("region", { name: "סיכום סטטיסטיקות", exact: true })).toContainText("סה״כ משמרות1");
    await page.getByRole("combobox", { name: "תקופת דיווח", exact: true }).selectOption("year");
    await expect(page.getByRole("table", { name: "מגמה חודשית וגרסאות פרסום" }).locator("tbody tr")).toHaveCount(12);
    await expect(page.getByText(/מדדים תיאוריים בלבד/)).toBeVisible();
    await logout(page); await login(page, 2);
    expect((await raw(page, `/admin/statistics/2031/1?teamId=${teams[0].id}`)).status()).toBe(403);
    await logout(page); await login(page, 3);
    expect((await raw(page, `/admin/statistics/2031/1?teamId=${teams[0].id}`)).status()).toBe(403);
  });
});
