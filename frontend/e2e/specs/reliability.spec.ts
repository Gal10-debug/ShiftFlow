import { execFileSync } from "node:child_process";
import { test, expect, login, state } from "../fixtures";

test.use({ profile: "published" });
test("Owned PostgreSQL outage and interrupted read preserve publication history @reliability", async ({ page }) => {
  test.setTimeout(60_000);
  const run = process.env.SHIFTFLOW_E2E_RUN!;
  const container = process.env.SHIFTFLOW_E2E_CONTAINER!;
  if (!/^[a-f0-9]{32}$/.test(run) || container !== `shiftflow-e2e-${run}`)
    throw new Error("Refusing to fault an unowned container");
  function docker(...args: string[]) {
    return execFileSync("docker", args, { encoding: "utf8", timeout: 15_000, stdio: ["ignore", "pipe", "pipe"] }).trim();
  }
  function owns() {
    expect(docker("inspect", "--format", '{{index .Config.Labels "shiftflow.e2e.run"}}', container)).toBe(run);
  }
  await login(page, 4);
  const before = await state(page);
  owns();
  let paused = false;
  try {
    docker("pause", container); paused = true;
    const response = await page.request.get("/api/my-schedule/2031/1", { timeout: 8000 });
    expect(response.status()).toBe(500);
    expect((await response.json()).code).toBe("INTERNAL_ERROR");
    await expect(page.request.get("/api/my-schedule/2031/1", { timeout: 50 })).rejects.toThrow();
  } finally {
    if (paused) { owns(); docker("unpause", container); }
  }
  await expect.poll(async () => (await page.request.get("/api/my-schedule/2031/1")).status(), { timeout: 12_000 }).toBe(200);
  expect(await state(page)).toEqual(before);
});
