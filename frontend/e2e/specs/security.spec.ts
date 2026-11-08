import { createServer, type Server } from "node:http";
import { test, expect, login, logout, api, raw, state, manual, rowId, userId, periodPath, board, publish, absence, decideAbsence } from "../fixtures";

test("Security: a deputy cannot decide their own absence and a different team approver can", async ({ page }) => {
  await login(page, 3);
  const own = await absence(page, "בקשה אישית של סגן");
  await page.goto("/team-absences");
  const card = page.getByRole("article").filter({ hasText: "בקשה אישית של סגן" });
  await expect(card.getByText("בקשה אישית מחייבת החלטה של מאשר אחר.", { exact: true })).toBeVisible();
  await expect(card.getByRole("button", { name: "בדיקה והחלטה", exact: true })).toHaveCount(0);
  const before = await state(page);
  const response = await raw(page, `/absences/${own.id}/decision`, "POST", { expectedVersion: own.version, decision: "APPROVED" });
  expect(response.status()).toBe(403); expect((await response.json()).code).toBe("SELF_ABSENCE_DECISION");
  expect(await state(page)).toEqual(before);
  await logout(page); await login(page, 2);
  await decideAbsence(page, "בקשה אישית של סגן", true, true);
  expect((await state(page)).absences.find(a => a.id === own.id)?.status).toBe("APPROVED");
});

test("Security: a same-site foreign origin cannot read CSRF tokens or forge a cookie-authenticated logout", async ({ page }) => {
  await login(page, 1); const before = await state(page);
  const victim = process.env.SHIFTFLOW_E2E_BACKEND_URL!;
  const attacker: Server = createServer((_request, response) => {
    response.setHeader("Content-Type", "text/html; charset=utf-8");
    response.end(`<!doctype html><html lang="he" dir="rtl"><title>בדיקת מקור חיצוני</title>
      <form method="post" action="${victim}/api/auth/logout"><button>שליחת טופס חיצוני</button></form>
      <button id="read">בדיקת קריאה חוצת מקור</button><output aria-label="תוצאת הקריאה"></output>
      <script>document.getElementById('read').onclick=async()=>{
        try { const r=await fetch('${victim}/api/auth/csrf',{credentials:'include'}); await r.text();
          document.querySelector('output').textContent='קריאה התאפשרה'; }
        catch { document.querySelector('output').textContent='הקריאה נחסמה'; }
      };</script></html>`);
  });
  await new Promise<void>((resolve, reject) => { attacker.once("error", reject); attacker.listen(0, "127.0.0.1", resolve); });
  try {
    const address = attacker.address(); if (!address || typeof address === "string") throw new Error("No isolated attacker origin");
    await page.goto(`http://127.0.0.1:${address.port}`);
    await page.getByRole("button", { name: "בדיקת קריאה חוצת מקור", exact: true }).click();
    await expect(page.getByLabel("תוצאת הקריאה", { exact: true })).toHaveText("הקריאה נחסמה");
    const denied = page.waitForResponse(r => r.url() === `${victim}/api/auth/logout` && r.request().method() === "POST");
    await page.getByRole("button", { name: "שליחת טופס חיצוני", exact: true }).click();
    const response = await denied;
    expect(response.status()).toBe(400); expect((await response.json()).code).toBe("CSRF_INVALID");
    // Ports differ, but the IP/site is identical: SameSite alone cannot explain
    // this rejection. The real antiforgery middleware rejected a valid session.
    expect((await raw(page, "/auth/me")).status()).toBe(200);
    expect(await state(page)).toEqual(before);
  } finally { await new Promise<void>((resolve, reject) => attacker.close(error => error ? reject(error) : resolve())); }
});

test("Security: stored markup stays literal Hebrew text in drafts and employee publications", async ({ page }) => {
  const payload = '<img src="/__security_probe_missing" onerror="document.documentElement.dataset.securityProbe=\'executed\'">טקסט עברי';
  await login(page, 1);
  expect((await manual(page, "ASSIGN", { definitionId: rowId(26), date: "2031-01-05", userId: userId(4), note: payload })).status()).toBe(200);
  await board(page);
  await expect(page.getByText(payload, { exact: true })).toBeVisible();
  await expect(page.locator('img[src="/__security_probe_missing"]')).toHaveCount(0);
  await expect(page.locator("html")).not.toHaveAttribute("data-security-probe", "executed");
  await publish(page);
  await logout(page); await login(page, 4); await page.goto("/my-schedule?period=2031-01");
  await expect(page.getByText(payload, { exact: true })).toBeVisible();
  await expect(page.locator('img[src="/__security_probe_missing"]')).toHaveCount(0);
  await expect(page.locator("html")).not.toHaveAttribute("data-security-probe", "executed");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  const published = await api<{ snapshot: { cells: { employees: { note: string | null }[] }[] } }>(page, "/my-schedule/2031/1");
  expect(published.snapshot.cells.flatMap(c => c.employees).some(e => e.note === payload)).toBe(true);
  expect((await raw(page, periodPath)).status()).toBe(403);
});
