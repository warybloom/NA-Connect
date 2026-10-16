// Testes isolados do módulo BI CHS001 (mesmo padrão de ClientApp/scripts/test-pmo.mjs: esbuild + node:test), sem tocar na
// suíte existente. Uso: node _bi_chs001_replica/tools/testar.mjs
import { spawnSync } from "node:child_process";
import { mkdtempSync, readdirSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const raiz = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const clientApp = path.join(raiz, "ClientApp");
const require = createRequire(path.join(clientApp, "package.json"));
const { build } = require("esbuild");
const dir = path.join(clientApp, "src/features/production/biChs001/__tests__");
const entries = readdirSync(dir).filter((f) => f.endsWith(".test.ts")).map((f) => path.join(dir, f));
const out = mkdtempSync(path.join(tmpdir(), "bi-chs001-tests-"));
await build({
  entryPoints: entries, outdir: out, entryNames: "[name]", bundle: true, platform: "node", format: "esm", outExtension: { ".js": ".mjs" },
  jsx: "automatic", alias: { "@": path.join(clientApp, "src") }, logLevel: "warning", loader: { ".tsx": "tsx", ".png": "file", ".css": "empty" },
  absWorkingDir: clientApp,
  banner: { js: "import { createRequire as __createRequire } from 'node:module'; const require = __createRequire(import.meta.url);" },
});
const arquivos = readdirSync(out).filter((f) => f.endsWith(".mjs")).map((f) => path.join(out, f));
if (arquivos.length !== entries.length) { console.error(`Esperados ${entries.length} bundles, gerados ${arquivos.length}.`); process.exit(1); }
const r = spawnSync(process.execPath, ["--test", ...arquivos],
  { stdio: "inherit", cwd: clientApp });
process.exit(r.status ?? 1);
