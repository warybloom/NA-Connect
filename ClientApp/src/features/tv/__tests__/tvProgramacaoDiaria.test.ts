// Programação Diária na TV (19/10/2026): tela própria (gestao-a-vista-programacao-diaria), UM projeto e UMA data por item,
// repetível com projetos/datas diferentes; a 02. PROGRAMAÇÃO semanal continua intacta; contrato com o backend.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import { encontrarFolha } from "../../production/productionRoutes";
import { ConteudoTv, TELAS_TV, TELAS_TV_DATA_OBRIGATORIA, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO, TELAS_TV_SELECIONAVEIS, TV_PROGRAMACAO_DIARIA, getTelaTv } from "../tvScreens";
import { dataReferenciaValida, formatarDataReferencia, slideComProjetos, validarApresentacoes, type TvPresentation } from "../tvLogic";
import { itensParaAdicionar, telasEmLote } from "../../settings/TelasTvConfigPage";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};
const P = (n: number, nome: string) => ({ hubId: "", projectId: `b.${String(n).repeat(8)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(12)}`, projectName: nome });
const CHS = P(2, "CHS001 - ETHANOL PLANT"), BML = P(3, "BML011.016 - COOLING TOWER");
const SEMANAL = "gestao-a-vista-programacao";
const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
const validar = (lista: TvPresentation[]) => validarApresentacoes(lista, permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_DATA_OBRIGATORIA);
const tv = (slides: TvPresentation["slides"]): TvPresentation => ({ id: "tv-eng", name: "TV ENGENHARIA", secondsPerScreen: 30, slides });

test("Catálogo: nova tela independente, um projeto, data obrigatória, mesmas keys da 02; semanal intacta", () => {
  const diaria = getTelaTv(TV_PROGRAMACAO_DIARIA)!;
  const semanal = getTelaTv(SEMANAL)!;
  assert.equal(TV_PROGRAMACAO_DIARIA, "gestao-a-vista-programacao-diaria");
  assert.ok(diaria.tvSelectable);
  assert.equal(diaria.multiProjeto, false);
  assert.equal(diaria.exigeDataReferencia, true);
  assert.equal(diaria.categoria, "Gestão à Vista");
  assert.deepEqual([...diaria.chaves], [...encontrarFolha("gestao-a-vista/programacao")!.permissionKeys]);
  assert.equal(diaria.containsCostData, false);
  assert.ok(TELAS_TV_PROJETO_UNICO.includes(TV_PROGRAMACAO_DIARIA));
  assert.deepEqual([...TELAS_TV_DATA_OBRIGATORIA], [TV_PROGRAMACAO_DIARIA]);
  // Semanal: mesma definição de antes (multiprojeto, sem data, rota do Controle de Produção).
  assert.equal(semanal.multiProjeto, true);
  assert.ok(!semanal.exigeDataReferencia);
  assert.equal(semanal.slug, "gestao-a-vista/programacao");
  assert.equal(TELAS_TV.filter((t) => t.id === TV_PROGRAMACAO_DIARIA).length, 1);
});

test("Contrato com o backend: TvTelas.cs registra a tela com as mesmas keys, Unico e DataReferenciaObrigatoria só nela", () => {
  const cs = fonte("../Services/Tv/TvTelas.cs");
  const linha = /T\("gestao-a-vista-programacao-diaria",\s*(Multi|Unico)((?:,\s*"[^"]+")+)\)\s*with\s*\{\s*DataReferenciaObrigatoria\s*=\s*true\s*\}/.exec(cs);
  assert.ok(linha, "tela ausente no backend");
  assert.equal(linha[1], "Unico");
  assert.deepEqual(Array.from(linha[2].matchAll(/"([^"]+)"/g), (m) => m[1]), [...getTelaTv(TV_PROGRAMACAO_DIARIA)!.chaves]);
  assert.equal((cs.match(/DataReferenciaObrigatoria\s*=\s*true/g) ?? []).length, 1);
  const regras = fonte("../Services/Tv/TvConfiguration.cs");
  assert.match(regras, /DateOnly\.TryParseExact\(v, "yyyy-MM-dd", CultureInfo\.InvariantCulture/);
  assert.match(regras, /string\? DataReferencia = null\);/);
});

test("Data de referência: só AAAA-MM-DD real (mesma regra do DateOnly.TryParseExact)", () => {
  for (const ok of ["2026-10-15", "2024-02-29", "2026-12-31"]) assert.ok(dataReferenciaValida(ok), ok);
  for (const ruim of ["", null, undefined, "2026-02-30", "2025-02-29", "2026-13-01", "2026-00-10", "15/10/2026", "2026-1-5", "2026-10-15T00:00:00", " 2026-10-15"])
    assert.ok(!dataReferenciaValida(ruim as string), String(ruim));
  assert.equal(formatarDataReferencia("2026-10-15"), "15/10/2026");
});

test("Validação: data obrigatória e válida na diária; um projeto por item; demais telas e JSON antigo não mudam", () => {
  assert.match(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS])])])!, /data de referência válida/);
  assert.match(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "2026-02-30")])])!, /data de referência válida/);
  assert.equal(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "2026-10-15")])]), null);
  // Nunca mescla projetos.
  assert.match(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS, BML], "2026-10-15")])])!, /um projeto por vez/);
  // JSON antigo (sem o campo) das demais telas continua válido e idêntico.
  const antigo = { screenId: SEMANAL, hubId: "", projectId: CHS.projectId, projectName: CHS.projectName };
  assert.equal(validar([tv([antigo])]), null);
  assert.deepEqual(Object.keys(slideComProjetos(SEMANAL, [CHS])).sort(), ["hubId", "projectId", "projectName", "projetos", "screenId"]);
});

test("Editor: várias instâncias, cada uma com projeto/data próprios; sem data nada entra; repetição idêntica ignorada", () => {
  const diaria = getTelaTv(TV_PROGRAMACAO_DIARIA)!;
  assert.deepEqual(itensParaAdicionar(diaria, [CHS], [], "consolidado", null), [], "sem data");
  assert.deepEqual(itensParaAdicionar(diaria, [CHS], [], "consolidado", "2026-02-30"), [], "data inválida");
  const dois = itensParaAdicionar(diaria, [CHS, BML], [], "consolidado", "2026-10-15");
  assert.deepEqual(dois.map((s) => [s.projectId, s.dataReferencia, s.projetos ?? null]), [[CHS.projectId, "2026-10-15", null], [BML.projectId, "2026-10-15", null]]);
  // Mesmo projeto em outra data = outro slide; mesmo projeto + mesma data = repetido (ignorado).
  const outraData = itensParaAdicionar(diaria, [CHS], dois, "consolidado", "2026-10-16");
  assert.equal(outraData.length, 1);
  assert.deepEqual(itensParaAdicionar(diaria, [CHS], [...dois, ...outraData], "consolidado", "2026-10-15"), []);
  const sequencia = [...dois, ...outraData, ...itensParaAdicionar(diaria, [BML], [...dois, ...outraData], "consolidado", "2026-10-20")];
  assert.equal(sequencia.length, 4);
  assert.equal(validar([tv(sequencia)]), null);
  // Limite atual de 50 itens por TV também vale para a diária.
  const cinquenta = Array.from({ length: 51 }, (_, i) => slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], `2026-10-${String((i % 28) + 1).padStart(2, "0")}`));
  assert.match(validar([tv(cinquenta)])!, /máximo de 50 telas/);
  // Semanal continua sem data (o parâmetro é ignorado para telas sem data).
  const semanal = itensParaAdicionar(getTelaTv(SEMANAL)!, [CHS], [], "consolidado", "2026-10-15");
  assert.equal(semanal[0].dataReferencia, undefined);
  // "Adicionar todas" nunca inclui a diária (exige data por item).
  assert.ok(!telasEmLote(TELAS_TV_SELECIONAVEIS).some((t) => t.id === TV_PROGRAMACAO_DIARIA));
});

const eff = (keys: string[], extra: Partial<EffectivePermissions> = {}): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: keys, anyProject: keys, ...extra });
const renderDiaria = (dataReferencia: string | null, selection = [{ id: CHS.projectId, hubId: "", name: CHS.projectName }]) => {
  const efetivas = eff([], { isMasterAdmin: true });
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  return renderToStaticMarkup(createElement(MemoryRouter, null,
    createElement(PermissionContext.Provider, { value: valor },
      createElement(ConteudoTv, { screenId: TV_PROGRAMACAO_DIARIA, selection, dataReferencia }))));
};

test("Player: dia ausente/inválido = aviso (nunca hoje); dia válido = shell da Programação Diária com projeto e dia no cabeçalho", () => {
  for (const ruim of [null, "", "2026-02-30", "hoje"]) {
    const html = renderDiaria(ruim);
    assert.match(html, /Programação Diária sem data de referência/, String(ruim));
    assert.match(html, /hoje nunca é usada/);
  }
  const ok = renderDiaria("2026-10-15");
  assert.match(ok, /data-testid="gestao-a-vista-programacao-diaria"/);
  assert.match(ok, /PROGRAMAÇÃO DIÁRIA/);
  assert.match(ok, /data-testid="gv-dia-referencia">QUI 15\/10\/2026</);
  assert.match(ok, /CHS001 - ETHANOL PLANT/);
  // Dois projetos salvos por engano: só o principal é exibido (defesa; o backend recusa salvar).
  const dois = renderDiaria("2026-10-15", [{ id: CHS.projectId, hubId: "", name: CHS.projectName }, { id: BML.projectId, hubId: "", name: BML.projectName }]);
  assert.ok(!/BML011/.test(dois));
});

test("Player e página: data do slide repassada, troca de slide remonta, mesmo endpoint semanal filtrado no dia", () => {
  const player = fonte("src/features/tv/TvPlayerPage.tsx");
  assert.match(player, /dataReferencia=\{def\?\.exigeDataReferencia \? slide\.dataReferencia \?\? null : null\}/);
  assert.match(player, /join\("\+"\)\}-\$\{slide\.dataReferencia \?\? ""\}`\}/);
  const pagina = fonte("src/features/production/analytics/GestaoVistaJamilePage.tsx");
  assert.match(pagina, /useProgramacao\(selection,week\.start,week\.end,screen==="programacao"\|\|screen==="visao-geral"\|\|diaFixo!==null\)/);
  assert.match(pagina, /<Programming reports=\{fr\} semana=\{week\} dia=\{diaFixo\} cat=\{cat\} dados=\{prog\.dados\} erro=\{null\}/);
  assert.match(pagina, /\.\.\.\(diaria\?prog\.falhas:\[\]\)/);
  // A 02 semanal continua com navegação de semana e filtro opcional de dia.
  assert.match(pagina, /\{screen==="programacao"&&<div className="gv-date gv-date-semana">/);
  assert.match(pagina, /<option value="">SEMANA COMPLETA<\/option>/);
});
