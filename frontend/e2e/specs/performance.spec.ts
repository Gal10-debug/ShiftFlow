import { performance } from "node:perf_hooks";
import { setTimeout as pause } from "node:timers/promises";
import { test, expect, control, login, periodPath } from "../fixtures";
import type { APIRequestContext, APIResponse } from "@playwright/test";
import type { Review } from "../../src/publication";
import type { Generation } from "../../src/scheduling";

function bounded(name: string, fallback: number, min: number, max: number) {
  const value = Number(process.env[name] ?? fallback);
  if (!Number.isInteger(value) || value < min || value > max) throw new Error(`Invalid ${name}`);
  return value;
}
const duration = bounded("SHIFTFLOW_PERFORMANCE_SECONDS", 1, 1, 10);
const navigations = bounded("SHIFTFLOW_PERFORMANCE_NAVIGATIONS", 6, 3, 20);
const levels = (process.env.SHIFTFLOW_PERFORMANCE_CONCURRENCY ?? "1,5,10,20").split(",").map(Number);
if (!levels.length || levels.some(n => ![1, 5, 10, 20].includes(n)) || new Set(levels).size !== levels.length)
  throw new Error("Concurrency must be a unique subset of 1,5,10,20");

type Observation = { milliseconds: number; status: number; bytes: number };
function summarize(samples: Observation[], wallMs: number) {
  const sorted = samples.map(s => s.milliseconds).sort((a, b) => a - b);
  const quantile = (q: number) => sorted[Math.max(0, Math.ceil(sorted.length * q) - 1)] ?? null;
  return { samples: samples.length, p50Ms: quantile(0.5), p95Ms: samples.length >= 100 ? quantile(0.95) : null,
    maxMs: quantile(1), errors: samples.filter(s => s.status < 200 || s.status >= 300).length,
    meanResponseBytes: samples.reduce((total, sample) => total + sample.bytes, 0) / samples.length,
    statuses: Object.fromEntries([...new Set(samples.map(s => s.status))].map(code => [code, samples.filter(s => s.status === code).length])),
    throughputPerSecond: samples.length / (wallMs / 1000), wallMs };
}

test.use({ profile: "pilot", trace: "off" });
test("Pilot API concurrency, write workflows, query plans and monthly board rendering @performance", async ({ page, playwright }, info) => {
  test.setTimeout(300_000);
  const contexts: APIRequestContext[] = [];
  const results: object[] = [];
  const browser: object[] = [];
  let assignmentCount = 0;
  let queryReport: unknown = null;
  async function csrf(context: APIRequestContext) {
    const response = await context.get("/api/auth/csrf");
    expect(response.status()).toBe(200);
    const value = (await response.json()).requestToken as string; await response.dispose(); return value;
  }
  async function send(context: APIRequestContext, path: string, method = "GET", data?: unknown): Promise<APIResponse> {
    return context.fetch(`/api${path}`, { method, data,
      headers: method === "GET" ? {} : { "X-CSRF-TOKEN": await csrf(context) }, timeout: 15_000 });
  }
  async function checked<T>(context: APIRequestContext, path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await send(context, path, method, data); expect(response.ok(), `${method} workflow status`).toBe(true);
    const result = response.status() === 204 ? undefined : await response.json(); await response.dispose(); return result as T;
  }
  async function measure(name: string, concurrency: number, action: (worker: number) => Promise<APIResponse>, repeat = true) {
    const before = await control(page.request, "metrics");
    const samples: Observation[] = []; const started = performance.now();
    await Promise.all(Array.from({ length: concurrency }, async (_, worker) => {
      for (let i = 0; i < (repeat ? 500 : 1); i++) {
        const start = performance.now(); const response = await action(worker);
        // Include complete response transfer, not just receipt of headers.
        const body = await response.body();
        if (!response.ok()) {
          const error = await response.json().catch(() => ({ code: "NON_JSON_ERROR" }));
          expect(response.ok(), `${name}: HTTP ${response.status()}, code ${String(error.code)}`).toBe(true);
        }
        samples.push({ milliseconds: performance.now() - start, status: response.status(), bytes: body.byteLength }); await response.dispose();
        if (!repeat || performance.now() - started >= duration * 1000) break;
        await pause(25);
      }
    }));
    const summary = summarize(samples, performance.now() - started);
    const after = await control(page.request, "metrics");
    results.push({ operation: name, concurrency, execution: repeat ? "warm" : "one per isolated user; CSRF included", ...summary,
      backendCpuSeconds: after.cpuSeconds - before.cpuSeconds, workingSetBytes: after.workingSetBytes,
      readerCommandsPerRequest: (after.readerCommands - before.readerCommands) / samples.length });
    expect(summary.errors, name).toBe(0);
  }
  try {
    // All contexts target the freshly claimed host. No external base URL is accepted.
    await control(page.request, "performance", "DELETE");
    for (const actor of [1, ...Array.from({ length: 20 }, (_, i) => i + 10)]) {
      contexts.push(await playwright.request.newContext({ baseURL: process.env.SHIFTFLOW_E2E_BACKEND_URL!,
        extraHTTPHeaders: { "X-E2E-Actor": String(actor) } }));
    }
    const admin = contexts[0], employees = contexts.slice(1);
    await checked(admin, "/auth/login", "POST", { username: "e2e-1", password: process.env.SHIFTFLOW_E2E_PASSWORD! });
    for (const concurrency of levels) {
      await measure("login", concurrency, worker => send(employees[worker], "/auth/login", "POST", {
        username: `e2e-${worker + 10}`, password: process.env.SHIFTFLOW_E2E_PASSWORD! }), false);
    }
    // Ensure smaller custom concurrency selections still authenticate every fixture user.
    for (let i = 0; i < employees.length; i++) {
      const response = await send(employees[i], "/auth/me"); const authenticated = response.ok(); await response.dispose();
      if (!authenticated) await checked(employees[i], "/auth/login", "POST", { username: `e2e-${i + 10}`, password: process.env.SHIFTFLOW_E2E_PASSWORD! });
    }
    // Exactly one preview, at the unchanged production 30-second solver budget.
    const generationStarted = performance.now();
    let generation = await checked<Generation>(admin, "/admin/scheduling/generation", "POST", { year: 2031, month: 3 });
    const generationDeadline = generationStarted + 45_000;
    while (["QUEUED", "RUNNING"].includes(generation.state) && performance.now() < generationDeadline) {
      await pause(200);
      generation = await checked<Generation>(admin, `/admin/scheduling/generation/${generation.id}`);
    }
    expect(generation.state, "Monthly generation must produce a valid incumbent").toBe("READY");
    results.push({ operation: "monthly-generation", concurrency: 1, samples: 1,
      elapsedMs: performance.now() - generationStarted, solverStatus: generation.solverStatus,
      provenObjectives: generation.provenObjectives, summary: generation.summary, stages: generation.stages });
    await checked(admin, `/admin/scheduling/generation/${generation.id}`, "DELETE");
    const endpoints = [
      ["personal-schedule", "/my-schedule/2031/1"], ["availability-read", "/availability/2031/2"],
      ["absence-read", "/absences/mine"], ["monthly-board", "/admin/scheduling/board?date=2031-01-01&monthly=true"],
    ] as const;
    for (const [operation, path] of endpoints) {
      const client = operation === "monthly-board" ? admin : employees[0];
      await measure(`${operation}-first`, 1, () => send(client, path), false);
      for (const concurrency of levels) await measure(operation, concurrency, worker => send(operation === "monthly-board" ? admin : employees[worker], path));
    }
    const leader = await playwright.request.newContext({ baseURL: process.env.SHIFTFLOW_E2E_BACKEND_URL!, extraHTTPHeaders: { "X-E2E-Actor": "2" } });
    contexts.push(leader);
    await checked(leader, "/auth/login", "POST", { username: "e2e-2", password: process.env.SHIFTFLOW_E2E_PASSWORD! });
    await measure("team-scoped-members", 1, () => send(leader, "/team-availability/members"));
    await measure("publication-history", 1, () => send(admin, periodPath + "/publications"));
    for (const concurrency of levels) {
      const versions = await Promise.all(employees.slice(0, concurrency).map(c => checked<{ version: number }>(c, "/availability/2031/2")));
      await measure("availability-submit", concurrency, worker => send(employees[worker], "/availability/2031/2", versions[worker].version ? "PUT" : "POST",
        { expectedVersion: versions[worker].version, selections: [] }), false);
      const requests: { id: string; version: number }[] = [];
      await measure("absence-create", concurrency, async worker => {
        const day = `2031-02-${String(levels.indexOf(concurrency) + 1).padStart(2, "0")}`;
        const response = await send(employees[worker], "/absences", "POST", { reason: "VACATION", startDate: day, endDate: day,
          recurring: false, weekdays: [], fullDay: true, startTime: null, endTime: null, explanation: "בדיקת ביצועים" });
        requests[worker] = await response.json(); return response;
      }, false);
      const conflicts = await Promise.all(requests.map(r => checked<{ token: string }>(admin, `/absences/${r.id}/conflicts`)));
      await measure("absence-approve", concurrency, worker => send(admin, `/absences/${requests[worker].id}/decision`, "POST", {
        expectedVersion: requests[worker].version, decision: "APPROVED", conflictToken: conflicts[worker].token }), false);
    }
    const original = await control(page.request, "state");
    assignmentCount = original.assignments.length;
    const current = await checked<Review>(admin, periodPath);
    await measure("enter-correction", 1, () => send(admin, periodPath + "/correction", "POST", { expectedToken: current.token }), false);
    let review = await checked<Review>(admin, periodPath);
    const source = review.draft.cells.find(c => c.definitionId === "40000000-0000-0000-0000-000000000026" && c.date === "2031-01-05")!.employees[0];
    await measure("administrative-correction", 1, () => send(admin, periodPath + "/assignments", "POST", {
      expectedToken: review.token, operation: "REPLACE", assignmentId: source.assignmentId, userId: "71000000-0000-0000-0000-000000000010",
      definitionId: "40000000-0000-0000-0000-000000000026", date: "2031-01-05" }), false);
    review = await checked<Review>(admin, periodPath);
    review = await checked<Review>(admin, periodPath + "/review", "POST", { expectedToken: review.token });
    await measure("publication", 1, () => send(admin, periodPath + "/publications/PRELIMINARY", "POST", {
      expectedToken: review.token, acknowledgeShortages: true, acknowledgeOpenIssues: true, notes: null }), false);
    const after = await control(page.request, "state");
    expect(after.publications[0]).toEqual(original.publications[0]);
    expect(after.publications).toHaveLength(2);
    const exchangeSource = (await checked<{ assignmentId: string; publicationId: string; date: string }[]>(employees[0], "/shift-exchanges/2031/1/assignments"))
      .find(a => a.date === "2031-01-05")!;
    let exchange: { id: string; version: number };
    await measure("exchange-create", 1, async () => {
      const response = await send(employees[0], "/shift-exchanges/2031/1", "POST", {
        publicationId: exchangeSource.publicationId, assignmentId: exchangeSource.assignmentId,
        replacementUserId: "71000000-0000-0000-0000-000000000011" });
      exchange = await response.json(); return response;
    }, false);
    await measure("exchange-consent", 1, async () => {
      const response = await send(employees[1], `/shift-exchanges/${exchange.id}/decision`, "POST", { expectedVersion: exchange.version, decision: "ACCEPT" });
      exchange = await response.json(); return response;
    }, false);
    await measure("exchange-approve", 1, () => send(admin, `/admin/shift-exchanges/${exchange.id}/decision`, "POST", {
      expectedVersion: exchange.version, decision: "APPROVE" }), false);
    review = await checked<Review>(admin, periodPath);
    review = await checked<Review>(admin, periodPath + "/review", "POST", { expectedToken: review.token });
    await measure("exchange-publish", 1, () => send(admin, `/admin/shift-exchanges/${exchange.id}/publish`, "POST", {
      periods: [{ year: 2031, month: 1, expectedToken: review.token }], acknowledgeShortages: true, acknowledgeOpenIssues: true, notes: null }), false);
    for (const concurrency of levels) await measure("exchange-read", concurrency, worker => send(employees[worker], "/shift-exchanges"));
    expect((await control(page.request, "state")).publications.slice(0, 2)).toEqual(after.publications);
    queryReport = await control(page.request, "performance");

    // Browser measurements include fetch, React work and two animation frames.
    await login(page, 1);
    const requests: string[] = [];
    page.on("request", r => { if (r.url().includes("/api/")) requests.push(new URL(r.url()).pathname); });
    const cdp = await page.context().newCDPSession(page);
    await cdp.send("Performance.enable");
    for (let i = 0; i < navigations; i++) {
      const start = performance.now(); const count = requests.length;
      if (i === 0) {
        await page.goto("/scheduling?period=2031-01");
        await page.getByRole("combobox", { name: "תצוגת לוח", exact: true }).selectOption("month");
      } else {
        await page.getByLabel("חודש לבדיקה", { exact: true }).fill(`2031-${i % 2 ? "02" : "01"}`);
      }
      const grid = page.getByRole("region", { name: "לוח סידור עבודה שבועי", exact: true });
      await expect(grid.locator("tbody tr")).toHaveCount(51);
      await expect(grid.locator("thead time")).toHaveCount(i % 2 ? 28 : 31);
      await expect(page.getByRole("button", { name: "רענון הלוח", exact: true })).toBeEnabled();
      await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
      const metrics = (await cdp.send("Performance.getMetrics")).metrics;
      browser.push({ iteration: i, month: i % 2 ? 2 : 1, elapsedMs: performance.now() - start,
        apiRequests: requests.slice(count).reduce<Record<string, number>>((result, path) => ({ ...result, [path]: (result[path] ?? 0) + 1 }), {}),
        metrics: Object.fromEntries(metrics.filter(m => ["JSHeapUsedSize", "Nodes", "LayoutCount", "RecalcStyleCount", "TaskDuration", "ScriptDuration"].includes(m.name)).map(m => [m.name, m.value])) });
      expect(await page.locator(".scheduling-page").getAttribute("dir")).toBe("rtl");
    }
    await cdp.send("HeapProfiler.collectGarbage");
    browser.push({ afterExplicitGarbageCollection: true,
      dom: await cdp.send("Memory.getDOMCounters"),
      heapBytes: (await cdp.send("Performance.getMetrics")).metrics.find(m => m.name === "JSHeapUsedSize")?.value });
  } finally {
    await info.attach("performance-results", { body: JSON.stringify({ environment: `local Kestrel + disposable PostgreSQL; ${process.env.SHIFTFLOW_PERFORMANCE_BUILD} React build`,
      durationSeconds: duration, thinkTimeMs: 25, concurrencyLevels: levels, dataset: { users: 29, rows: 51, cellsPerMonth: 1581,
        assignments: assignmentCount, historicalApprovedAbsences: 100 },
      percentilePolicy: "p95 omitted below 100 samples; all local percentiles are exploratory", api: results, database: queryReport, browser }, null, 2), contentType: "application/json" });
    await Promise.all(contexts.map(context => context.dispose()));
  }
});
