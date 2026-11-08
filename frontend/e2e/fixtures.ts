import { test as base, expect, type Page, type APIRequestContext } from "@playwright/test";
import type { Review, Publication } from "../src/publication";
import type { Absence } from "../src/absences";

export { expect };
export const month = "2031-01";
export const userId = (n: number) => `71000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
export const rowId = (n: number) => `40000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
export const periodPath = "/admin/scheduling/periods/2031/1";
export type Profile = "empty" | "published" | "qualified" | "shortage" | "pilot";
type State = { assignments: { id: string; userId: string }[]; publications: { id: string; versionNumber: number; hash: string }[];
  controls: { publicationId: string; hidden: boolean; deletedAt: string | null }[]; absences: { id: string; status: string }[] };

export async function control(request: APIRequestContext, path: string, method = "GET") {
  const response = await request.fetch(`${process.env.SHIFTFLOW_E2E_BACKEND_URL}/__e2e/${path}`, {
    method, headers: { "X-E2E-Control": process.env.SHIFTFLOW_E2E_TOKEN! },
  });
  expect(response.status(), "Disposable fixture control must succeed").toBe(200);
  return response.json();
}
export const state = (page: Page): Promise<State> => control(page.request, "state");
export async function api<T>(page: Page, path: string, method = "GET", data?: unknown): Promise<T> {
  const response = await raw(page, path, method, data);
  expect(response.status(), `${method} ${path}`).toBeGreaterThanOrEqual(200);
  expect(response.status(), `${method} ${path}`).toBeLessThan(300);
  return response.status() === 204 ? undefined as T : response.json();
}
export async function raw(page: Page, path: string, method = "GET", data?: unknown) {
  const headers: Record<string, string> = {};
  if (method !== "GET") {
    const csrf = await page.request.get("/api/auth/csrf");
    headers["X-CSRF-TOKEN"] = (await csrf.json()).requestToken;
  }
  return page.request.fetch(`/api${path}`, { method, data, headers });
}

export const test = base.extend<{ profile: Profile; isolated: void }>({
  profile: ["empty", { option: true }],
  isolated: [async ({ request, profile }, use) => {
    const proxy = await request.get("/api/__e2e/ready", { headers: { "X-E2E-Control": process.env.SHIFTFLOW_E2E_TOKEN! } });
    expect(proxy.status(), "Frontend must proxy only to this owned E2E host").toBe(200);
    expect((await proxy.json()).run).toBe(process.env.SHIFTFLOW_E2E_RUN);
    const result = await control(request, `reset?profile=${profile}`, "POST");
    expect(result.run).toBe(process.env.SHIFTFLOW_E2E_RUN);
    await use();
  }, { auto: true }],
  page: async ({ page }, use) => {
    await page.clock.setFixedTime(new Date("2030-12-01T10:00:00Z"));
    page.on("dialog", dialog => dialog.accept());
    await use(page);
  },
});

export async function login(page: Page, n: number, password = process.env.SHIFTFLOW_E2E_PASSWORD!) {
  const proof = await page.request.get("/api/__e2e/ready", { headers: { "X-E2E-Control": process.env.SHIFTFLOW_E2E_TOKEN! } });
  expect(proof.status(), "Refuse login unless the disposable proxy identity is proven").toBe(200);
  expect((await proof.json()).run).toBe(process.env.SHIFTFLOW_E2E_RUN);
  await page.goto("/login");
  await page.getByLabel("שם משתמש", { exact: true }).fill(`e2e-${n}`);
  await page.getByLabel("סיסמה", { exact: true }).fill(password);
  await page.getByRole("button", { name: "התחברות", exact: true }).click();
  await expect(page).not.toHaveURL(/\/login$/);
}
export async function logout(page: Page) {
  await page.getByRole("button", { name: "התנתקות", exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
}
export async function board(page: Page) {
  await page.goto(`/scheduling?period=${month}`);
  await page.getByLabel("בחירת תאריך בשבוע", { exact: true }).fill("2031-01-05");
  await expect(page.getByRole("region", { name: "בדיקה ופרסום הסידור", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "לוח סידור עבודה שבועי", exact: true }).locator('time[datetime="2031-01-05"]')).toBeVisible();
}
export async function edit(page: Page, date = "2031-01-05", name = "משמרת בדיקה") {
  await page.getByRole("button", { name: `עריכת ${name} ${date}`, exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "עריכת שיבוץ", exact: true });
  await expect(dialog).toBeVisible(); return dialog;
}
export async function boardAction(page: Page, name: string, suffix: string) {
  const changed = page.waitForResponse(r => r.url().endsWith(suffix) && r.request().method() === "POST");
  const refreshed = page.waitForResponse(r => r.url().includes("/api/admin/scheduling/board") && r.request().method() === "GET");
  await page.getByRole("button", { name, exact: true }).click();
  expect((await changed).status()).toBe(200);
  await (await refreshed).finished();
  await expect(page.getByRole("button", { name: "רענון הלוח", exact: true })).toBeEnabled();
}
export const correction = (page: Page) => boardAction(page, "כניסה למצב תיקונים", "/correction");
export async function saveConfiguration(page: Page) {
  const saved = page.waitForResponse(r => r.url().endsWith("/shift-types") && r.request().method() === "PUT");
  const refreshed = page.waitForResponse(r => r.url().includes("/api/admin/scheduling/board") && r.request().method() === "GET");
  await page.getByRole("button", { name: "שמירת הגדרות", exact: true }).click();
  expect((await saved).status()).toBe(200);
  await (await refreshed).finished();
  await expect(page.getByRole("button", { name: "רענון הלוח", exact: true })).toBeEnabled();
}
export const review = (page: Page) => api<Review>(page, periodPath);
export async function manual(page: Page, operation: string, fields: Record<string, unknown>) {
  const r = await review(page);
  return raw(page, `${periodPath}/assignments`, "POST", { expectedToken: r.token, operation,
    assignmentId: null, userId: null, definitionId: null, date: null, ...fields });
}
export async function saveEdit(page: Page) {
  const dialog = page.getByRole("dialog", { name: "עריכת שיבוץ", exact: true });
  const response = page.waitForResponse(r => r.url().endsWith("/assignments") && r.request().method() === "POST");
  await dialog.getByRole("button", { name: "שמירת תיקון ידני", exact: true }).click();
  expect((await response).status()).toBe(200);
  await dialog.getByRole("button", { name: "סגירה", exact: true }).click();
}
export async function publish(page: Page, final = false) {
  const panel = page.getByRole("region", { name: "בדיקה ופרסום הסידור", exact: true });
  await expect(panel.getByRole("checkbox", { name: /^אני מאשר פרסום סופי עם/ })).toBeEnabled();
  const acknowledge = panel.getByRole("button", { name: "אישור בדיקת זמינות ואילוצים", exact: true });
  if (await acknowledge.isVisible()) {
    await boardAction(page, "אישור בדיקת זמינות ואילוצים", "/review");
  }
  const shortages = panel.getByRole("checkbox", { name: /^אני מאשר פרסום עם/ });
  if (await shortages.isVisible()) await shortages.check();
  await panel.getByRole("checkbox", { name: /^אני מאשר פרסום סופי עם/ }).check();
  const response = page.waitForResponse(r => r.url().endsWith(`/publications/${final ? "FINAL" : "PRELIMINARY"}`) && r.request().method() === "POST");
  await panel.getByRole("button", { name: final ? "פרסום סופי" : "פרסום ראשוני", exact: true }).click();
  expect((await response).status()).toBe(200);
}
export const publications = (page: Page) => api<Publication[]>(page, `${periodPath}/publications`);
export async function absence(page: Page, explanation: string, date = "2031-01-05", kind = "once", partial?: [string, string]) {
  await page.goto("/absences");
  const form = page.getByRole("form", { name: "בקשת היעדרות חדשה", exact: true });
  await form.getByRole("combobox", { name: "סוג היעדרות", exact: true }).selectOption(kind);
  await form.getByLabel("תאריך התחלה", { exact: true }).fill(date);
  await form.getByLabel(kind === "weekly" ? "תאריך סיום (רשות)" : "תאריך סיום", { exact: true }).fill(kind === "weekly" ? "2031-02-01" : date);
  if (kind === "weekly") await form.getByRole("checkbox", { name: "ראשון", exact: true }).check();
  if (partial) {
    await form.getByRole("checkbox", { name: "היעדרות ביום מלא", exact: true }).uncheck();
    await form.getByLabel("משעה", { exact: true }).fill(partial[0]);
    await form.getByLabel("עד שעה", { exact: true }).fill(partial[1]);
  }
  await form.getByLabel("הסבר (רשות)", { exact: true }).fill(explanation);
  await form.getByRole("button", { name: "שליחת בקשה", exact: true }).click();
  await expect(page.getByRole("article").filter({ hasText: explanation })).toContainText("ממתינה לאישור");
  return (await api<Absence[]>(page, "/absences/mine")).find(a => a.pattern.explanation === explanation)!;
}
export async function decideAbsence(page: Page, explanation: string, approve = true, delegated = false) {
  await page.goto(delegated ? "/team-absences" : "/absence-management");
  await page.getByRole("article").filter({ hasText: explanation }).getByRole("button", { name: "בדיקה והחלטה", exact: true }).click();
  const panel = page.getByRole("region", { name: "בדיקת בקשת היעדרות", exact: true });
  if (!approve) await panel.getByLabel("סיבת דחייה", { exact: true }).fill("בדיקת דחייה");
  await panel.getByRole("button", { name: approve ? "אישור הבקשה" : "דחיית הבקשה", exact: true }).click();
  await expect(panel).not.toBeVisible();
}
