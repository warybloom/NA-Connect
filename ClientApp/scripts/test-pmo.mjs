// Testes de lógica pura de /relatorios (consolidação multi-projeto). Sem framework novo: esbuild (já presente via Vite) + node:test.
import { build } from 'esbuild';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dirs = [path.join(root, 'src/components/pmo/__tests__'), path.join(root, 'src/features/production/dashboard/__tests__'), path.join(root, 'src/features/permissions/__tests__'), path.join(root, 'src/features/tv/__tests__'), path.join(root, 'src/features/preferences/__tests__'), path.join(root, 'src/features/project/__tests__'), path.join(root, 'src/features/upload-governado/__tests__'), path.join(root, 'src/features/document-quality/__tests__'), path.join(root, 'src/features/production/analytics/__tests__'), path.join(root, 'src/features/auth/__tests__'), path.join(root, 'src/features/production/__tests__'), path.join(root, 'src/features/planning/costs/__tests__'), path.join(root, 'src/features/robot/__tests__'), path.join(root, 'src/features/theme/__tests__')];
const entries = dirs.flatMap(dir => readdirSync(dir).filter(f => f.endsWith('.test.ts')).map(f => path.join(dir, f)));
const out = mkdtempSync(path.join(tmpdir(), 'pmo-tests-'));
await build({ entryPoints: entries, outdir: out, entryNames: '[name]', bundle: true, platform: 'node', format: 'esm', outExtension: { '.js': '.mjs' },
  // .png/.css: o player da TV carrega o BI CHS001 (Gestão à Vista Tipo 2) com import() dinâmico — o bundle precisa aceitar
  // os assets dele (o módulo só é avaliado se o React.lazy disparar).
  alias: { '@': path.join(root, 'src') }, logLevel: 'error', loader: { '.tsx': 'tsx', '.png': 'file', '.css': 'empty' },
  // Dependências CJS (ex.: react-dom/server) fazem require() de módulos nativos do Node: o bundle ESM precisa de um require real.
  banner: { js: "import { createRequire as __createRequire } from 'node:module'; const require = __createRequire(import.meta.url);" } });
const files = readdirSync(out).filter(f => f.endsWith('.mjs')).map(f => path.join(out, f));
// Nunca chamar node --test sem arquivos (cairia na descoberta automática de outras pastas).
if (files.length !== entries.length) { console.error(`Esperados ${entries.length} bundles de teste, gerados ${files.length}.`); process.exit(1); }
const r = spawnSync(process.execPath, ['--test', ...files], { stdio: 'inherit' });
process.exit(r.status ?? 1);
