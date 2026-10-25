import { readFileSync } from "node:fs";

// Consume Compose's resolved configuration through stdin; never print credentials in errors.
try {
  const config = JSON.parse(readFileSync(0, "utf8"));
  const postgres = config.services?.postgres;
  const env = postgres?.environment;
  const port = postgres?.ports?.find(
    (p) => Number(p.target) === 5432 && p.host_ip === "127.0.0.1",
  );
  const values = [env?.POSTGRES_DB, env?.POSTGRES_USER, env?.POSTGRES_PASSWORD];
  if (values.some((value) => typeof value !== "string" || !value.trim()))
    throw new Error("missing configuration");
  if (!port || !/^\d+$/.test(String(port.published)))
    throw new Error("missing loopback port");
  // Npgsql quoted values escape embedded double quotes by doubling them.
  const quote = (value) => `"${value.replaceAll('"', '""')}"`;
  process.stdout.write(
    `Host=127.0.0.1;Port=${port.published};Database=${quote(values[0])};Username=${quote(values[1])};Password=${quote(values[2])};Include Error Detail=false`,
  );
} catch {
  console.error(
    "Invalid local PostgreSQL configuration. Check .env POSTGRES_PASSWORD and Compose's loopback port; no credentials were printed.",
  );
  process.exitCode = 1;
}
