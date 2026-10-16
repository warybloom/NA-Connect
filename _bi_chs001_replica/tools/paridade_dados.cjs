// Paridade de DADOS das páginas prioritárias do BI CHS001 (somente leitura; nada é gravado no banco).
// Roda o MESMO motor do frontend (ClientApp/src/features/production/biChs001/dados/motor.ts) sobre duas bases:
//   A) linhas do PBIX (cache do modelo lido com pbixray)      → o que o Power BI original calcula;
//   B) linhas atuais do banco N&A (mesma SQL de BiChs001Controller) → o que a tela N&A exibe.
// Uso: node _bi_chs001_replica/tools/paridade_dados.cjs <pbix_ldp4.json> <pbix_prog.json> <db_ldp.json> <db_prog.json> [saida.json]
//        [pbix_wbx.json db_cronograma.json]
// PBIX: _bi_chs001_replica/fixtures_pbix/*.json (já com os nomes do BI). Banco: linhas BRUTAS dos endpoints (/ldp, /programacao,
// /cronograma) — convertidas aqui pelo mesmo converterLinhas do frontend.
const fs = require("node:fs"), path = require("node:path"), vm = require("node:vm");
const root = path.resolve(__dirname, "../..");
const ts = require(path.join(root, "ClientApp/node_modules/typescript"));
const cache = {};
function load(file) {
  file = path.resolve(file);
  if (cache[file]) return cache[file];
  if (file.endsWith(".json")) return (cache[file] = JSON.parse(fs.readFileSync(file, "utf8")));
  const exports = {};
  cache[file] = exports;
  const js = ts.transpileModule(fs.readFileSync(file, "utf8"), { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS, esModuleInterop: true } }).outputText;
  const req = (name) => {
    const f = name.startsWith("@/") ? path.join(root, "ClientApp/src", name.slice(2)) : path.resolve(path.dirname(file), name);
    return load(f.endsWith(".json") ? f : fs.existsSync(f + ".ts") ? f + ".ts" : f + ".tsx");
  };
  vm.runInNewContext(js, { exports, require: req, console, JSON, Intl, Number, String, Object, Array, Set, Map, Math, Date });
  return exports;
}
const base = path.join(root, "ClientApp/src/features/production/biChs001");
const motor = load(path.join(base, "dados/motor.ts"));
const ent = load(path.join(base, "dados/entidades.ts"));
const [pbixLdp, pbixProg, dbLdp, dbProg, saida, pbixWbx, dbCrono] = process.argv.slice(2);
const ler = (f) => JSON.parse(fs.readFileSync(f, "utf8"));
const fontesPbix = { "LDP (4)": { estado: "ok", linhas: ler(pbixLdp) }, "Programação Semanal": { estado: "ok", linhas: ler(pbixProg) } };
const fontesDb = {
  "LDP (4)": { estado: "ok", linhas: ent.converterLinhas("LDP (4)", ler(dbLdp)) },
  "Programação Semanal": { estado: "ok", linhas: ent.converterLinhas("Programação Semanal", ler(dbProg)) },
};
if (pbixWbx && dbCrono) {
  fontesPbix.WBX = { estado: "ok", linhas: ler(pbixWbx) };
  fontesDb.WBX = { estado: "ok", linhas: ent.converterLinhas("WBX", ler(dbCrono)) };
}
// PBIX: ITEM e textos já vêm com os tipos do modelo; datas da programação são texto dd/MM/yyyy no PBIX.
const PAGINAS = ["p03", "p04", "p05", "p08", "p09", "p10", "p12", "p13", "p14", "p17"];
const resumo = (d) => {
  if (d.estado !== "ok") return { estado: d.estado, motivo: d.motivo ?? d.mensagem };
  if (d.tipo === "valores") return d.pendentes ? { ...d.valores, pendentes: Object.keys(d.pendentes) } : d.valores;
  if (d.tipo === "pontos") return Object.fromEntries(d.pontos.map((p) => [p.categoria, Object.values(p.valores)[0]]));
  if (d.tipo === "linhas") return { linhas: d.linhas.length, primeira: d.linhas[0] ?? null };
  return d.tipo;
};
const relatorio = [];
for (const id of PAGINAS) {
  const pagina = load(path.join(base, "generated", id + ".json"));
  for (const v of pagina.visuais) {
    if (!v.fields || v.hidden || v.type === "slicer") continue;
    const ctxA = { pagina, fontes: fontesPbix, segmentacoes: {} };
    const ctxB = { pagina, fontes: fontesDb, segmentacoes: {} };
    const a = resumo(motor.dadosDoVisual(v, ctxA)), b = resumo(motor.dadosDoVisual(v, ctxB));
    const igual = JSON.stringify(a) === JSON.stringify(b);
    relatorio.push({ pagina: `${pagina.ordem}. ${pagina.nome}`, visual: v.id.slice(0, 8), tipo: v.type, titulo: (v.c?.title?.[0]?.p?.text ?? "").trim(), pbix: a, banco: b, igual });
  }
}
if (saida) fs.writeFileSync(saida, JSON.stringify(relatorio, null, 1));
for (const r of relatorio) console.log(`${r.igual ? "IGUAL " : "DIFERE"} | ${r.pagina} | ${r.tipo} ${r.visual} ${r.titulo}\n   PBIX : ${JSON.stringify(r.pbix)}\n   BANCO: ${JSON.stringify(r.banco)}`);
