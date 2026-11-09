import { test, expect, login, logout, api, raw, userId, absence } from "../fixtures";
import type { Availability } from "../../src/availability";

test("Fixture controls require their run secret and refuse unknown profiles without changing records", async ({ page, request }) => {
  const backend = process.env.SHIFTFLOW_E2E_BACKEND_URL;
  expect((await request.get(`${backend}/__e2e/state`)).status()).toBe(404);
  expect((await request.post(`${backend}/__e2e/reset?profile=empty`)).status()).toBe(404);
  expect((await request.post(`${backend}/__e2e/reset?profile=demo`, {
    headers: { "X-E2E-Control": process.env.SHIFTFLOW_E2E_TOKEN! },
  })).status()).toBe(400);
  await login(page, 4);
  expect((await api<{ id: string }>(page, "/auth/me")).id).toBe(userId(4));
});

test("Authentication rejects bad credentials, persists a session, and revokes it on logout", async ({ page }) => {
  await page.goto("/scheduling");
  await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel("שם משתמש", { exact: true }).fill("e2e-4");
  await page.getByLabel("סיסמה", { exact: true }).fill("invalid-password");
  await page.getByRole("button", { name: "התחברות", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("שם המשתמש או הסיסמה שגויים");
  await login(page, 4);
  await expect(page).toHaveURL(/\/employee$/);
  await page.reload();
  await expect(page.getByRole("heading", { name: "אילוצי השיבוץ שלי", exact: true })).toBeVisible();
  await logout(page);
  expect((await raw(page, "/auth/me")).status()).toBe(401);
  await page.goto("/my-schedule");
  await expect(page).toHaveURL(/\/login$/);
});

for (const [n, admin, delegated] of [[1, true, false], [2, false, true], [3, false, true], [5, false, false], [6, false, false]] as const) {
  test(`Role boundaries for ${["", "administrator", "leader", "deputy", "", "employee", "qualified employee"][n]}`, async ({ page }) => {
    await login(page, n);
    const nav = page.getByRole("navigation");
    await expect(nav.getByRole("link", { name: "ניהול משתמשים", exact: true })).toHaveCount(admin ? 1 : 0);
    await expect(nav.getByRole("link", { name: "זמינות הצוות", exact: true })).toHaveCount(delegated ? 1 : 0);
    expect((await raw(page, "/access/admin")).status()).toBe(admin ? 204 : 403);
    if (!admin) {
      await page.goto("/scheduling");
      await expect(page.getByRole("heading", { name: "אין לך הרשאה לעמוד הזה", exact: true })).toBeVisible();
    } else expect((await raw(page, "/availability/periods")).status()).toBe(403);
  });
}

test("Initial password change gates application access and persists the new password", async ({ page }) => {
  await login(page, 9);
  await expect(page).toHaveURL(/\/change-password$/);
  expect((await raw(page, "/availability/periods")).status()).toBe(403);
  await page.getByLabel("סיסמה נוכחית", { exact: true }).fill(process.env.SHIFTFLOW_E2E_PASSWORD!);
  await page.getByLabel("סיסמה חדשה", { exact: true }).fill(process.env.SHIFTFLOW_E2E_NEW_PASSWORD!);
  await page.getByLabel("אימות סיסמה חדשה", { exact: true }).fill(process.env.SHIFTFLOW_E2E_NEW_PASSWORD!);
  await page.getByRole("button", { name: "החלפת סיסמה", exact: true }).click();
  await expect(page).toHaveURL(/\/employee$/);
  await logout(page);
  await login(page, 9, process.env.SHIFTFLOW_E2E_NEW_PASSWORD!);
  await expect(page).toHaveURL(/\/employee$/);
});

test("Delegated constraints survive window closure, worker review and deputy correction while absences remain self-service", async ({ page }) => {
  await login(page, 1);
  await page.goto("/availability-admin");
  await page.getByLabel("חודש", { exact: true }).fill("2031-01");
  await page.getByRole("button", { name: "פתיחת הגשה", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("נפתח");
  await logout(page); await login(page, 2);
  await page.getByRole("combobox", { name: "בחירת עובד", exact: true }).selectOption(userId(4));
  for (const day of [5, 6, 7]) await page.getByRole("combobox", { name: `0${day}/01/2031 יום 07:00–19:00`, exact: true }).selectOption("BLOCKED");
  await page.getByRole("button", { name: "הגש זמינות", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "הזמינות הוגשה בהצלחה" })).toBeVisible();
  const path = `/team-availability/employees/${userId(4)}/2031/1`;
  const before = await api<Availability>(page, path);
  await page.getByRole("combobox", { name: "08/01/2031 יום 07:00–19:00", exact: true }).selectOption("BLOCKED");
  await expect(page.getByRole("alert")).toContainText("חריגה ממגבלת 3 החסימות");
  await expect(page.getByRole("button", { name: "עדכן זמינות", exact: true })).toBeDisabled();
  const invalid = await raw(page, path, "PUT", { expectedVersion: before.version,
    selections: [5, 6, 7, 8].map(day => ({ date: `2031-01-0${day}`, shiftType: "DAY", state: "BLOCKED" })) });
  expect(invalid.status()).toBe(400);
  expect((await invalid.json()).code).toBe("WEEKLY_BLOCK_LIMIT");
  expect((await api<Availability>(page, path)).selections).toEqual(before.selections);
  await page.reload(); await logout(page); await login(page, 4);
  await expect(page.getByRole("heading", { name: "אילוצי השיבוץ שלי", exact: true })).toBeVisible();
  await expect(page.getByText("את האילוצים מזינים ראש הצוות או סגן ראש הצוות. אם מצאת טעות, יש לפנות אליהם לצורך תיקון.", { exact: true })).toBeVisible();
  const shift = page.getByRole("combobox", { name: "05/01/2031 יום 07:00–19:00", exact: true });
  await expect(shift).toHaveValue("BLOCKED"); await expect(shift).toBeDisabled();
  await expect(page.getByRole("button", { name: /הגש זמינות|עדכן זמינות|ביטול שינויים/ })).toHaveCount(0);
  for (const method of ["POST", "PUT", "DELETE"]) {
    expect((await raw(page, "/availability/2031/1", method, { expectedVersion: before.version, selections: [] })).status()).toBe(method === "DELETE" ? 404 : 403);
  }
  expect((await api<Availability>(page, "/availability/2031/1")).selections).toEqual(before.selections);
  await logout(page); await login(page, 1);
  await page.goto("/availability-admin"); await page.getByLabel("חודש", { exact: true }).fill("2031-01");
  await page.getByRole("button", { name: "סגירת הגשה", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("נסגר");
  await logout(page); await login(page, 3);
  await page.getByRole("combobox", { name: "בחירת עובד", exact: true }).selectOption(userId(4));
  await page.getByRole("combobox", { name: "05/01/2031 יום 07:00–19:00", exact: true }).selectOption("PREFERRED");
  await page.getByRole("button", { name: "עדכן זמינות", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "הזמינות עודכנה בהצלחה" })).toBeVisible();
  await logout(page); await login(page, 4);
  await expect(shift).toHaveValue("PREFERRED"); await expect(shift).toBeDisabled();
  await absence(page, "בקשת היעדרות עצמאית לאחר עדכון אילוצים");
  const requests = await api<{ status: string }[]>(page, "/absences/mine");
  expect(requests).toHaveLength(1); expect(requests[0].status).toBe("PENDING");
});

for (const actor of [2, 3]) {
  test(`Closed-window delegation permits actor ${actor} only within their own team`, async ({ page }) => {
    await login(page, actor);
    await page.getByRole("combobox", { name: "בחירת עובד", exact: true }).selectOption(userId(4));
    await page.getByRole("combobox", { name: "05/01/2031 יום 07:00–19:00", exact: true }).selectOption("PREFERRED");
    await page.getByRole("button", { name: "הגש זמינות", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "הזמינות הוגשה בהצלחה" })).toBeVisible();
    await page.reload();
    await page.getByRole("combobox", { name: "בחירת עובד", exact: true }).selectOption(userId(4));
    await expect(page.getByRole("combobox", { name: "05/01/2031 יום 07:00–19:00", exact: true })).toHaveValue("PREFERRED");
    await expect(page.getByRole("combobox", { name: "בחירת עובד", exact: true }).getByRole("option", { name: "עובד אחר", exact: true })).toHaveCount(0);
    const path = `/team-availability/employees/${userId(8)}/2031/1`;
    expect((await raw(page, path)).status()).toBe(403);
    expect((await raw(page, path, "POST", { expectedVersion: 0, selections: [] })).status()).toBe(403);
    expect((await raw(page, "/access/admin")).status()).toBe(403);
  });
}
