import { randomBytes, randomUUID } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdirSync, openSync, closeSync, writeFileSync, writeSync, rmSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const frontend = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const root = path.resolve(frontend, '..');
const run = randomUUID().replaceAll('-', '');
const db = `shiftflow_e2e_${run}`, container = `shiftflow-e2e-${run}`;
const privateDir = path.join(root, '.e2e-private', run), reportDir = path.join(frontend, 'e2e-report');
mkdirSync(privateDir, { recursive: true, mode: 0o700 });
const secret = () => randomBytes(24).toString('hex');
const token = secret(), password = `E2e!${secret()}`, dbPassword = secret();
const port = () => new Promise((resolve, reject) => {
  const s = createServer(); s.once('error', reject); s.listen(0, '127.0.0.1', () => {
    const result = s.address().port; s.close(() => resolve(result));
  });
});
const children = [], logs = [];
let containerId;
const env = { ...process.env, SHIFTFLOW_E2E_RUN: run, SHIFTFLOW_E2E_TOKEN: token,
  SHIFTFLOW_E2E_PASSWORD: password, SHIFTFLOW_E2E_NEW_PASSWORD: `Changed!${secret()}`,
  SHIFTFLOW_E2E_PRIVATE: privateDir, SHIFTFLOW_E2E_REPORT: reportDir,
  DOTNET_NOLOGO: 'true', DOTNET_CLI_TELEMETRY_OPTOUT: 'true' };
function command(exe, args, options = {}) {
  const r = spawnSync(exe, args, { cwd: root, env, encoding: 'utf8', ...options });
  if (r.status !== 0) throw new Error(`${exe} failed; see sanitized diagnostics.`);
  return r.stdout?.trim() ?? '';
}
function start(exe, args, name, cwd = root, progress = false) {
  const fd = openSync(path.join(privateDir, `${name}.log`), 'w', 0o600); logs.push(fd);
  const child = spawn(exe, args, { cwd, env, stdio: ['ignore', progress ? 'pipe' : fd, fd] });
  if (progress) {
    let pending = '', completed = 0;
    child.stdout.on('data', chunk => {
      writeSync(fd, chunk); pending += chunk.toString('utf8');
      const lines = pending.split('\n'); pending = lines.pop();
      for (const line of lines) {
        const status = /^\[E2E\] (passed|failed|timedOut|skipped|interrupted): /.exec(line);
        // Forward counts and a fixed status vocabulary, never titles or error text.
        if (status) console.log(`Browser progress: ${++completed} finished (${status[1]}).`);
      }
    });
  }
  children.push(child); return child;
}
async function ready(url, predicate, headers = {}) {
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    try { const response = await fetch(url, { headers, signal: AbortSignal.timeout(2000) }); if (response.ok && await predicate(response)) return; } catch { /* startup not ready */ }
    if (children.some(c => c.exitCode !== null || c.signalCode !== null)) throw new Error('An E2E service exited before readiness.');
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error('E2E service readiness timed out.');
}
const stop = () => { for (const child of children) if (child.exitCode === null) child.kill('SIGTERM'); };
process.once('SIGINT', () => { stop(); process.exitCode = 130; });
process.once('SIGTERM', () => { stop(); process.exitCode = 143; });
let exitCode = 1;
try {
  // A new container owns its own tmpfs; no externally supplied connection is accepted.
  const dockerEnv = { ...env, POSTGRES_DB: db, POSTGRES_USER: db, POSTGRES_PASSWORD: dbPassword };
  containerId = command('docker', ['run', '-d', '--name', container, '--label', `shiftflow.e2e.run=${run}`,
    '--memory=512m', '--cpus=2', '--tmpfs', '/var/lib/postgresql/data:rw', '-p', '127.0.0.1::5432',
    '-e', 'POSTGRES_DB', '-e', 'POSTGRES_USER', '-e', 'POSTGRES_PASSWORD', 'postgres:17-alpine'], { env: dockerEnv });
  const binding = command('docker', ['port', containerId, '5432/tcp']);
  if (!/^127\.0\.0\.1:\d+$/.test(binding)) throw new Error('Unexpected Docker binding.');
  const dbPort = Number(binding.split(':')[1]);
  if ([5432, 55432].includes(dbPort)) throw new Error('Refusing persistent database port.');
  const deadline = Date.now() + 60_000;
  while (spawnSync('docker', ['exec', containerId, 'pg_isready', '-U', db, '-d', db], { stdio: 'ignore' }).status !== 0) {
    if (Date.now() > deadline) throw new Error('Disposable PostgreSQL did not become ready.');
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  const backendPort = await port();
  let frontendPort = await port();
  while (frontendPort === backendPort) frontendPort = await port();
  Object.assign(env, { SHIFTFLOW_E2E_CONNECTION: `Host=127.0.0.1;Port=${dbPort};Database=${db};Username=${db};Password=${dbPassword}`,
    SHIFTFLOW_E2E_BACKEND_PORT: String(backendPort), SHIFTFLOW_E2E_BACKEND_URL: `http://127.0.0.1:${backendPort}`,
    SHIFTFLOW_E2E_FRONTEND_URL: `http://127.0.0.1:${frontendPort}` });
  command('dotnet', ['build', 'tests/ShiftManager.E2EHost', '-c', 'Release', '--warnaserror'], { stdio: 'inherit' });
  command('dotnet', ['tests/ShiftManager.E2EHost/bin/Release/net10.0/ShiftManager.E2EHost.dll', '--verify-guards'], { stdio: 'inherit' });
  start('dotnet', ['tests/ShiftManager.E2EHost/bin/Release/net10.0/ShiftManager.E2EHost.dll'], 'backend');
  await ready(`${env.SHIFTFLOW_E2E_BACKEND_URL}/__e2e/ready`, async r => { const j = await r.json(); return j.run === run && j.ready; }, { 'X-E2E-Control': token });
  start(process.execPath, ['node_modules/vite/bin/vite.js', '--config', 'e2e/vite.config.ts', '--host', '127.0.0.1', '--port', String(frontendPort), '--strictPort'], 'frontend', frontend);
  await ready(`${env.SHIFTFLOW_E2E_FRONTEND_URL}/__e2e/frontend-ready`, async r => (await r.json()).run === run);
  await ready(`${env.SHIFTFLOW_E2E_FRONTEND_URL}/api/__e2e/ready`, async r => (await r.json()).run === run, { 'X-E2E-Control': token });
  console.log('Disposable E2E environment ready; one Chromium worker.');
  if (process.argv.includes('--serve')) {
    // Local inspection only: credentials stay in the private file, never stdout.
    writeFileSync(path.join(privateDir, 'environment.json'), JSON.stringify(Object.fromEntries(Object.entries(env).filter(([key]) => key.startsWith('SHIFTFLOW_E2E_')))), { mode: 0o600 });
    console.log(`Inspection URL: ${env.SHIFTFLOW_E2E_FRONTEND_URL}`);
    console.log(`Private control file: ${path.relative(root, privateDir)}/environment.json`);
    await new Promise(resolve => { process.once('SIGINT', resolve); process.once('SIGTERM', resolve); });
    exitCode = 0;
  } else {
    const child = start(process.execPath, ['node_modules/@playwright/test/cli.js', 'test', ...process.argv.slice(2)], 'playwright', frontend, true);
    exitCode = await new Promise(resolve => child.once('close', code => resolve(code ?? 1)));
  }
} catch (error) { console.error(error.message); }
finally {
  stop();
  await Promise.all(children.map(child => child.exitCode === null && child.signalCode === null ? new Promise(resolve => {
    child.once('exit', resolve); setTimeout(() => { if (child.exitCode === null) child.kill('SIGKILL'); resolve(); }, 5000).unref();
  }) : Promise.resolve()));
  for (const fd of logs) closeSync(fd);
  // docker run can create a container and then fail before returning its ID.
  // The unique name is also safe to clean only after verifying the exact label.
  const cleanupTarget = containerId ?? container;
  const owner = spawnSync('docker', ['inspect', '--format', '{{index .Config.Labels "shiftflow.e2e.run"}}', cleanupTarget], { encoding: 'utf8' });
  if (owner.status === 0) {
    if (owner.stdout.trim() === run) {
      const removed = spawnSync('docker', ['rm', '-f', cleanupTarget], { stdio: 'ignore' });
      if (removed.status !== 0) { console.error('Owned E2E container cleanup failed.'); exitCode = 1; }
    } else { console.error('Container ownership could not be verified; refusing cleanup.'); exitCode = 1; }
  } else if (containerId) { console.error('Owned E2E container cleanup could not be verified.'); exitCode = 1; }
  // Raw artifacts are never copied to the report directory or printed.
  const sanitized = spawnSync('python3', ['e2e/sanitize.py', privateDir, reportDir], { cwd: frontend, env, stdio: 'inherit' });
  if (sanitized.status !== 0) { console.error('Artifact sanitization failed; no raw artifacts published.'); exitCode = 1; }
  if (!process.env.SHIFTFLOW_E2E_KEEP_PRIVATE) rmSync(privateDir, { recursive: true, force: true });
}
process.exitCode = exitCode;
