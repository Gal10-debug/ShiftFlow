import { defineConfig, mergeConfig } from "vite";
import base from "../vite.config";
const backend = process.env.SHIFTFLOW_E2E_BACKEND_URL;
const run = process.env.SHIFTFLOW_E2E_RUN;
if (!run || !/^[a-f0-9]{32}$/.test(run) || !backend || !/^http:\/\/127\.0\.0\.1:\d+$/.test(backend))
  throw new Error("E2E frontend requires an explicit disposable run and loopback backend.");
export default mergeConfig(base, defineConfig({
  server: { proxy: { "/api": backend } },
  plugins: [{ name: "owned-e2e-readiness", configureServer(server) {
    server.middlewares.use("/__e2e/frontend-ready", (_req, res) => {
      res.setHeader("Content-Type", "application/json"); res.end(JSON.stringify({ run }));
    });
  } }],
}));
