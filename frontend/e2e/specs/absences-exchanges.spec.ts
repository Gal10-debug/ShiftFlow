import { test, expect, login, logout, api, raw, absence, decideAbsence, state, board, review, rowId, userId, manual, periodPath, publications, correction, saveConfiguration } from "../fixtures";
import type { Absence } from "../../src/absences";
import type { Candidate } from "../../src/publication";
import type { Exchange } from "../../src/exchanges";

test("One-time, recurring, partial-day and overnight requests persist independently; pending requests can be withdrawn", async ({ page }) => {
  await login(page, 4);
  await absence(page, "בקשה חד פעמית");
  await absence(page, "בקשה שבועית", "2031-01-05", "weekly");
  await absence(page, "בקשה חלקית", "2031-01-06", "once", ["09:00", "11:00"]);
  await absence(page, "בקשה חוצת חצות", "2031-01-07", "once", ["22:00", "06:00"]);
  await page.reload();
  const requests = await api<Absence[]>(page, "/absences/mine");
  expect(requests).toHaveLength(4);
  expect(requests.every(r => r.status === "PENDING")).toBe(true);
  expect(requests.find(r => r.pattern.explanation === "בקשה שבועית")?.pattern.weekdays).toEqual([0]);
  expect(requests.find(r => r.pattern.explanation === "בקשה חלקית")?.pattern.startTime).toBe("09:00:00");
  expect(requests.find(r => r.pattern.explanation === "בקשה חוצת חצות")?.pattern.endTime).toBe("06:00:00");
  const card = page.getByRole("article").filter({ hasText: "בקשה חד פעמית" });
  await card.getByRole("button", { name: "ביטול בקשה ממתינה", exact: true }).click();
  await expect(card).toContainText("בוטלה או הוחלפה");
});

test("Team approval, modification, and cancellation preserve the approved restriction until each decision", async ({ page }) => {
  await login(page, 4); await absence(page, "מחזור אישור שינוי וביטול");
  await logout(page); await login(page, 2); await decideAbsence(page, "מחזור אישור שינוי וביטול", true, true);
  await logout(page); await login(page, 4); await page.goto("/absences");
  await page.getByRole("article").filter({ hasText: "מחזור אישור שינוי וביטול" }).getByRole("button", { name: "בקשת שינוי", exact: true }).click();
  const form = page.getByRole("form", { name: "בקשת שינוי היעדרות", exact: true });
  await form.getByLabel("תאריך סיום", { exact: true }).fill("2031-01-07");
  await form.getByRole("textbox", { name: "הסבר (רשות)", exact: true }).fill("שינוי ממתין לבדיקה");
  await form.getByRole("button", { name: "שליחת בקשת שינוי", exact: true }).click();
  await expect(page.getByRole("article").filter({ hasText: "שינוי ממתין לבדיקה" })).toContainText("ממתינה לאישור");
  let requests = await api<Absence[]>(page, "/absences/mine");
  expect(requests.find(r => r.kind === "CREATE")?.status).toBe("APPROVED");
  await logout(page); await login(page, 3); await decideAbsence(page, "שינוי ממתין לבדיקה", true, true);
  await logout(page); await login(page, 4); await page.goto("/absences");
  const amended = page.getByRole("article").filter({ hasText: "שינוי ממתין לבדיקה" });
  await amended.getByRole("button", { name: "בקשת ביטול היעדרות", exact: true }).click();
  await expect(page.getByRole("article").filter({ hasText: "בקשת ביטול ·" })).toContainText("ממתינה לאישור");
  requests = await api<Absence[]>(page, "/absences/mine");
  expect(requests.find(r => r.kind === "MODIFY")?.status).toBe("APPROVED");
  await logout(page); await login(page, 2);
  await page.goto("/team-absences");
  await page.getByRole("article").filter({ hasText: "בקשת ביטול ·" }).getByRole("button", { name: "בדיקה והחלטה", exact: true }).click();
  await page.getByRole("region", { name: "בדיקת בקשת היעדרות", exact: true }).getByRole("button", { name: "אישור הבקשה", exact: true }).click();
  await expect(page.getByRole("region", { name: "בדיקת בקשת היעדרות", exact: true })).not.toBeVisible();
  const final = await state(page);
  expect(final.absences.filter(a => a.status === "APPROVED")).toHaveLength(1);
  expect(final.absences.filter(a => a.status === "CANCELLED")).toHaveLength(2);
});

test("Rejection creates no hard block, approved absence does, and other-team or employee decisions are forbidden", async ({ page }) => {
  await login(page, 4);
  const rejected = await absence(page, "בקשה לדחייה", "2031-01-06");
  const approved = await absence(page, "בקשה לאישור", "2031-01-05");
  expect((await raw(page, `/absences/${approved.id}/decision`, "POST", { expectedVersion: 1, decision: "APPROVED" })).status()).toBe(403);
  await logout(page); await login(page, 7);
  expect((await raw(page, `/absences/${approved.id}/conflicts`)).status()).toBe(403);
  expect((await raw(page, `/absences/${approved.id}/decision`, "POST", { expectedVersion: 1, decision: "APPROVED" })).status()).toBe(403);
  await page.goto("/team-absences"); await expect(page.getByText("בקשה לאישור", { exact: true })).toHaveCount(0);
  await logout(page); await login(page, 1);
  await decideAbsence(page, "בקשה לדחייה", false); await decideAbsence(page, "בקשה לאישור");
  const candidates = (date: string) => api<Candidate[]>(page, `${periodPath}/candidates?definitionId=${rowId(26)}&date=${date}`);
  expect((await candidates("2031-01-05")).find(c => c.userId === userId(4))?.reasons).toContain("APPROVED_ABSENCE");
  expect((await candidates("2031-01-06")).find(c => c.userId === userId(4))?.eligible).toBe(true);
  expect((await manual(page, "ASSIGN", { definitionId: rowId(26), date: "2031-01-05", userId: userId(4) })).status()).toBe(409);
  const statuses = (await state(page)).absences;
  expect(statuses.find(a => a.id === rejected.id)?.status).toBe("REJECTED");
});

test("Approving an absence overlapping draft work requires explicit removal confirmation", async ({ page }) => {
  await login(page, 1);
  expect((await manual(page, "ASSIGN", { definitionId: rowId(26), date: "2031-01-05", userId: userId(4) })).status()).toBe(200);
  await logout(page); await login(page, 4); await absence(page, "נדרש אישור הסרת טיוטה");
  await logout(page); await login(page, 1); await page.goto("/absence-management");
  await page.getByRole("article").filter({ hasText: "נדרש אישור הסרת טיוטה" }).getByRole("button", { name: "בדיקה והחלטה", exact: true }).click();
  const panel = page.getByRole("region", { name: "בדיקת בקשת היעדרות", exact: true });
  const approve = panel.getByRole("button", { name: "אישור הבקשה", exact: true });
  await expect(approve).toBeDisabled();
  expect((await state(page)).assignments).toHaveLength(1);
  await panel.getByRole("checkbox", { name: "אני מאשר הסרת השיבוצים המוצגים מהטיוטות, כולל בני זוג כוננות", exact: true }).check();
  await approve.click(); await expect(panel).not.toBeVisible();
  const after = await state(page); expect(after.assignments).toHaveLength(0); expect(after.publications).toHaveLength(0);
  expect(after.absences[0].status).toBe("APPROVED");
});

test.describe("Published absence conflicts", () => {
  test.use({ profile: "published" });
  test("Pending requests warn without blocking; approval records a conflict and never rewrites a publication", async ({ page }) => {
    await login(page, 4); await absence(page, "חפיפה לפרסום קיים");
    const original = (await state(page)).publications;
    await logout(page); await login(page, 1); await board(page);
    await expect(page.getByRole("complementary", { name: "בקשות היעדרות ממתינות", exact: true })).toBeVisible();
    expect((await review(page)).conflicts).toHaveLength(0);
    await decideAbsence(page, "חפיפה לפרסום קיים");
    expect((await state(page)).publications).toEqual(original);
    expect((await state(page)).assignments).toHaveLength(1);
    expect((await review(page)).conflicts.map(c => c.code)).toContain("APPROVED_ABSENCE");
    await logout(page); await login(page, 4); await page.goto("/my-schedule?period=2031-01");
    await expect(page.getByText("★ השיבוץ שלי", { exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toContainText("שעות עבודה מתוכננות12");
    await page.goto("/absences");
    await expect(page.getByRole("complementary", { name: "התנגשויות שטרם תוקנו", exact: true })).toBeVisible();
  });
});

async function requestExchange(page: import("@playwright/test").Page) {
  await page.goto("/shift-exchanges");
  const assignments = await api<{ assignmentId: string; publicationId: string }[]>(page, "/shift-exchanges/2031/1/assignments");
  await page.getByRole("combobox", { name: "המשמרת שלי", exact: true }).selectOption(assignments[0].assignmentId);
  await page.getByRole("combobox", { name: "עובד מחליף כשיר", exact: true }).selectOption(userId(6));
  await page.getByRole("button", { name: "שליחת בקשת החלפה", exact: true }).click();
  await expect(page.getByText("ממתינה להסכמת המחליף", { exact: true })).toBeVisible();
  return (await api<Exchange[]>(page, "/shift-exchanges"))[0];
}

test.describe("Exchange and personal schedule", () => {
  test.use({ profile: "qualified" });
  test("Exchange requires recipient consent, administrator approval, and explicit republication", async ({ page }) => {
    await login(page, 4); const request = await requestExchange(page);
    const original = (await state(page)).publications[0];
    await logout(page); await login(page, 6); await page.goto("/shift-exchanges");
    await page.getByRole("button", { name: "הסכמה להחלפה", exact: true }).click();
    await expect(page.getByText("ממתינה לאישור מנהל", { exact: true })).toBeVisible();
    await logout(page); await login(page, 1); await page.goto("/exchange-management");
    await page.getByRole("button", { name: "אישור ושמירת תיקון בטיוטה", exact: true }).click();
    await expect(page.getByRole("button", { name: "בדיקה ופרסום התיקון", exact: true })).toBeVisible();
    expect((await state(page)).publications).toEqual([original]);
    // The draft changed but the employee still reads the previous publication.
    await logout(page); await login(page, 4); await page.goto("/my-schedule?period=2031-01");
    await expect(page.getByText("★ השיבוץ שלי", { exact: true })).toBeVisible();
    await logout(page); await login(page, 1); await page.goto("/exchange-management");
    await page.getByRole("button", { name: "בדיקה ופרסום התיקון", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "בדיקה ופרסום תיקון החלפה", exact: true });
    const reviewButton = dialog.getByRole("button", { name: "אישור בדיקה מחודשת לחודש זה", exact: true });
    if (await reviewButton.isVisible()) await reviewButton.click();
    await dialog.getByRole("checkbox", { name: "אני מאשר/ת במפורש את חוסרי האיוש בחודשים המוצגים", exact: true }).check();
    await dialog.getByRole("checkbox", { name: "אני מאשר/ת פרסום עם הדיווחים הפתוחים המוצגים", exact: true }).check();
    await dialog.getByRole("button", { name: "אישור ופרסום לכל החודשים המוצגים", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    expect((await api<Exchange[]>(page, "/admin/shift-exchanges")).find(e => e.id === request.id)?.status).toBe("APPLIED");
    expect((await state(page)).publications[0]).toEqual(original);
    await logout(page); await login(page, 6); await page.goto("/my-schedule?period=2031-01");
    await expect(page.getByText("★ השיבוץ שלי", { exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toContainText("שעות עבודה מתוכננות12");
    await logout(page); await login(page, 4); await page.goto("/my-schedule?period=2031-01");
    await expect(page.getByText("★ השיבוץ שלי", { exact: true })).toHaveCount(0);
    await expect(page.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toContainText("שעות עבודה מתוכננות0");
    await page.getByRole("combobox", { name: "גרסת פרסום", exact: true }).selectOption("1");
    await expect(page.getByRole("region", { name: "סיכום חודשי מתוכנן" })).toContainText("שעות עבודה מתוכננות12");
  });

  for (const administrator of [false, true]) {
    test(`${administrator ? "Administrator" : "Recipient"} rejection leaves assignments and publication unchanged`, async ({ page }) => {
      await login(page, 4); await requestExchange(page); const before = await state(page);
      await logout(page); await login(page, 6); await page.goto("/shift-exchanges");
      await page.getByRole("button", { name: administrator ? "הסכמה להחלפה" : "דחיית ההחלפה", exact: true }).click();
      if (administrator) {
        await expect(page.getByText("ממתינה לאישור מנהל", { exact: true })).toBeVisible();
        await logout(page); await login(page, 1); await page.goto("/exchange-management");
        await page.getByLabel("סיבה או הערה", { exact: true }).fill("דחייה מבוקרת");
        await page.getByRole("button", { name: "דחיית הבקשה", exact: true }).click();
      }
      await expect(page.getByText("נדחתה", { exact: true })).toBeVisible();
      const after = await state(page); expect(after.assignments).toEqual(before.assignments); expect(after.publications).toEqual(before.publications);
    });
  }

  test("Unauthorized, unavailable, unqualified, and conflicting replacements are rejected by UI and backend", async ({ page }) => {
    await login(page, 6); await absence(page, "מחליף בחופשה");
    await logout(page); await login(page, 1); await decideAbsence(page, "מחליף בחופשה");
    const pub = (await publications(page))[0];
    // A qualified replacement with existing work is also ineligible.
    await board(page); await correction(page);
    await page.getByRole("button", { name: "הגדרות סוגי משמרות", exact: true }).click();
    await page.getByLabel("עובדים נדרשים 27", { exact: true }).fill("1");
    await page.getByRole("combobox", { name: "טווח שעות 27", exact: true }).selectOption("DAY");
    await saveConfiguration(page);
    expect((await manual(page, "ASSIGN", { definitionId: rowId(27), date: "2031-01-05", userId: userId(8) })).status()).toBe(200);
    const assignmentId = pub.snapshot!.cells.flatMap(c => c.employees)[0].assignmentId;
    await logout(page); await login(page, 4); await page.goto("/shift-exchanges");
    const candidatesLoaded = page.waitForResponse(r => r.url().includes("/shift-exchanges/2031/1/candidates?") && r.request().method() === "GET");
    await page.getByRole("combobox", { name: "המשמרת שלי", exact: true }).selectOption(assignmentId);
    const candidatesResponse = await candidatesLoaded;
    expect(candidatesResponse.status()).toBe(200); await candidatesResponse.finished();
    for (const [n, reason] of [[5, "QUALIFICATION_REQUIRED"], [6, "APPROVED_ABSENCE"], [8, "WORK_OVERLAP_OR_CONSECUTIVE"]] as const) {
      await expect(page.getByRole("combobox", { name: "עובד מחליף כשיר", exact: true }).locator(`option[value='${userId(n)}']`)).toHaveCount(0);
      const response = await raw(page, "/shift-exchanges/2031/1", "POST", { publicationId: pub.id, assignmentId, replacementUserId: userId(n) });
      expect(response.status()).toBe(409); expect((await response.json()).code).toBe(reason);
    }
    await logout(page); await login(page, 5);
    expect((await raw(page, "/shift-exchanges/2031/1", "POST", { publicationId: pub.id, assignmentId, replacementUserId: userId(4) })).status()).toBe(403);
    expect((await raw(page, "/admin/shift-exchanges")).status()).toBe(403);
  });

  test("Personal reporting is ownership scoped, and unpublished/protected APIs cannot be read or edited by employees", async ({ page }) => {
    await login(page, 4); await page.goto("/my-schedule?period=2031-01");
    const pub = await api<{ id: string; snapshot: { cells: { employees: { assignmentId: string }[] }[] } }>(page, "/my-schedule/2031/1");
    await page.getByRole("button", { name: "דיווח על בעיה", exact: true }).click();
    await page.getByLabel("תיאור הבעיה", { exact: true }).fill("בעיה אישית לבדיקת המנהל");
    await page.getByRole("button", { name: "שליחת דיווח", exact: true }).click();
    await expect(page.getByText("בעיה אישית לבדיקת המנהל", { exact: true })).toBeVisible();
    await logout(page); await login(page, 5); await page.goto("/my-schedule?period=2031-01");
    await expect(page.getByRole("button", { name: "דיווח על בעיה", exact: true })).toHaveCount(0);
    expect(await api<unknown[]>(page, "/my-schedule/2031/1/issues")).toHaveLength(0);
    expect((await raw(page, "/my-schedule/2031/1/issues", "POST", { publicationId: pub.id,
      assignmentId: pub.snapshot.cells.flatMap(c => c.employees)[0].assignmentId, explanation: "ניסיון לא מורשה" })).status()).toBe(403);
    expect((await raw(page, periodPath)).status()).toBe(403);
    expect((await raw(page, "/users")).status()).toBe(403);
    expect((await raw(page, `/my-schedule/${userId(4)}/2031/1`)).status()).toBe(404);
    expect((await raw(page, `${periodPath}/assignments`, "POST", { expectedToken: "0".repeat(64), operation: "REMOVE", assignmentId: pub.snapshot.cells.flatMap(c => c.employees)[0].assignmentId })).status()).toBe(403);
  });
});
