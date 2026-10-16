// Roda o MOTOR do BI (mesmo código da tela) nas 10 páginas prioritárias com (A) linhas reais exportadas pelos endpoints
// (SELECT, SQL idêntico ao da API) e (B) o cache do PBIX (fixtures, só teste) e grava o comparativo visual a visual.
// Uso: node _bi_chs001_replica/tools/valores_reais.mjs <pasta com tarefas.json, diario.json, ldp.json, programacao.json> <saida.json>
import { mkdtempSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const raiz = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const clientApp = path.join(raiz, "ClientApp");
const require = createRequire(path.join(clientApp, "package.json"));
const { build } = require("esbuild");
const [pastaDados, saida] = process.argv.slice(2);
const dir = mkdtempSync(path.join(tmpdir(), "bi-valores-"));
const entrada = path.join(dir, "entrada.ts");
const modulo = path.join(clientApp, "src/features/production/biChs001").replace(/\\/g, "/");
writeFileSync(entrada, `
import { readFileSync } from "node:fs";
import { dadosDoVisual, dataCorteSalva } from "${modulo}/dados/motor";
import { converterLinhas, colunasVazias } from "${modulo}/dados/entidades";
const ler = (a: string) => JSON.parse(readFileSync(a, "utf8"));
const pg = (p: string) => ler("${modulo}/generated/" + p + ".json");
const fx = (a: string) => ler("${raiz.replace(/\\/g, "/")}/_bi_chs001_replica/fixtures_pbix/" + a);
const fonte = (ent: string, arq: string, extra = {}) => { const r = ler(process.argv[2] + "/" + arq); const linhas = converterLinhas(ent, r.linhas);
  return { estado: "ok", linhas, vazias: colunasVazias(ent, linhas), versaoId: r.versaoId, ...extra }; };
const REAL = { WBX: fonte("WBX", "tarefas.json"), AssignmentByDay: fonte("AssignmentByDay", "diario.json"),
  "LDP (4)": fonte("LDP (4)", "ldp.json"), "Programação Semanal": fonte("Programação Semanal", "programacao.json") };
const PBIX = { WBX: { estado: "ok", linhas: fx("wbx.json") }, AssignmentByDay: { estado: "ok", linhas: fx("abd.json") },
  "LDP (4)": { estado: "ok", linhas: fx("ldp4.json") }, "Programação Semanal": { estado: "ok", linhas: fx("programacao.json") } };
const resumo = (d: any) => {
  if (d.estado !== "ok") return { estado: d.estado, motivo: (d.motivo ?? d.mensagem ?? "").slice(0, 160) };
  if (d.tipo === "valores") return { valores: d.valores, pendentes: d.pendentes ? Object.keys(d.pendentes) : undefined };
  if (d.tipo === "pontos") { const ult = d.pontos.at(-1); return { pontos: d.pontos.length, categorias: d.pontos.map((p: any) => p.categoria),
    primeiro: d.pontos[0], ultimo: ult, total: d.pontos.reduce((s: number, p: any) => s + Object.values(p.valores).reduce((a: number, x: any) => a + (typeof x === "number" ? x : 0), 0), 0) }; }
  if (d.tipo === "linhas") return { linhas: d.linhas.length };
  if (d.tipo === "matriz") return { raizes: d.nos.map((n: any) => n.caminho[0]), filhosRaiz: d.nos[0]?.filhos.length, valoresRaiz: d.nos[0]?.valores };
  return { tipo: d.tipo };
};
const PAGINAS = ["p03", "p04", "p05", "p12", "p13", "p14", "p08", "p09", "p10", "p17"];
const out: any = {};
for (const p of PAGINAS) {
  const pagina = pg(p);
  out[p] = { nome: pagina.nome, dataCorte: dataCorteSalva(pagina), visuais: [] };
  for (const v of pagina.visuais) {
    if (["shape", "image", "textbox", "actionButton", "slicer"].includes(v.type) || v.hidden) continue;
    const real = resumo(dadosDoVisual(v, { pagina, fontes: REAL as any, segmentacoes: {} }));
    const pbix = resumo(dadosDoVisual(v, { pagina, fontes: PBIX as any, segmentacoes: {} }));
    out[p].visuais.push({ id: v.id.slice(0, 8), tipo: v.type, real, pbix, iguais: JSON.stringify(real) === JSON.stringify(pbix) });
  }
}
console.log(JSON.stringify(out));
`);
await build({ entryPoints: [entrada], outfile: path.join(dir, "entrada.mjs"), bundle: true, platform: "node", format: "esm",
  alias: { "@": path.join(clientApp, "src") }, absWorkingDir: clientApp, logLevel: "warning", loader: { ".tsx": "tsx", ".png": "file", ".css": "empty" },
  banner: { js: "import { createRequire as __cr } from 'node:module'; const require = __cr(import.meta.url);" } });
const { spawnSync } = await import("node:child_process");
const r = spawnSync(process.execPath, [path.join(dir, "entrada.mjs"), pastaDados], { encoding: "utf8", maxBuffer: 1 << 28 });
if (r.status !== 0) { console.error(r.stderr); process.exit(1); }
writeFileSync(saida, JSON.stringify(JSON.parse(r.stdout), null, 1));
const res = JSON.parse(r.stdout);
for (const [p, v] of Object.entries(res)) {
  const pend = v.visuais.filter((x) => x.real.estado && x.real.estado !== "ok").length;
  console.log(`${p} ${v.nome}: ${v.visuais.length} visuais de dados | iguais ao PBIX ${v.visuais.filter((x) => x.iguais).length} | sem valor (real) ${pend}`);
}
void pathToFileURL;
