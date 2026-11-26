// Programação Diária AUTOMÁTICA na TV (20/10/2026): botão "Gerar telas diárias automáticas" (selecionados × todos permitidos,
// sem duplicar, limite 50 sem parcial, 01 Visão Geral no início), dataReferencia "AUTO" validada, HOJE/ONTEM pela data civil da
// Bahia (virada da meia-noite, domingo/segunda), análise por disciplina (previsto hoje × pendente de ontem comprovado ×
// sem comprovação), contrato com o backend. Datas fixas e JSON antigo continuam como antes.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router-dom";
import { PermissionContext } from "../../permissions/PermissionProvider";
import { criarCan, type EffectivePermissions } from "../../permissions/permissionLogic";
import { ConteudoTv, TELAS_TV_DATA_OBRIGATORIA, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO, TELAS_TV_SELECIONAVEIS, TV_PROGRAMACAO_DIARIA, TV_VISAO_GERAL, getTelaTv } from "../tvScreens";
import {
  DATA_REFERENCIA_AUTO, ROTULO_DIARIA_AUTO, TV_LIMITES, dataCivilBahia, dataReferenciaAutomatica, dataReferenciaDoSlideValida, diaAnterior,
  gerarTelasDiariasAutomaticas, janelaDiariaAutomatica, slideComProjetos, validarApresentacoes, type TvPresentation, type TvSlide, type TvSlideProjeto,
} from "../tvLogic";
import { itensParaAdicionar } from "../../settings/TelasTvConfigPage";
import {
  analiseDiariaAutomatica, criarCatalogo, visaoGeralDaSemana, type DocumentoLdpEmissao, type ProgramacaoLinha, type ProgramacaoResposta,
} from "../../production/analytics/gestaoVistaJamileLogic";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), ".."), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};
const P = (n: number, nome: string): TvSlideProjeto => ({ hubId: "", projectId: `b.${String(n).repeat(8)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(4)}-${String(n).repeat(12)}`, projectName: nome });
const CHS = P(2, "CHS001 - ETHANOL PLANT"), BML = P(3, "BML011.016 - COOLING TOWER"), TIM = P(4, "TIM003 - TIMBO"), EMI = P(5, "EMI001 - PROJETO NEGADO");
const permitidas = TELAS_TV_SELECIONAVEIS.map((t) => t.id);
const validar = (lista: TvPresentation[]) => validarApresentacoes(lista, permitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_DATA_OBRIGATORIA);
const tv = (slides: TvSlide[], id = "tv-eng"): TvPresentation => ({ id, name: id.toUpperCase(), secondsPerScreen: 30, slides });
const auto = (p: TvSlideProjeto) => slideComProjetos(TV_PROGRAMACAO_DIARIA, [p], DATA_REFERENCIA_AUTO);
const gerar = (slides: TvSlide[], projetos: TvSlideProjeto[], o: Partial<Parameters<typeof gerarTelasDiariasAutomaticas>[0]> = {}) =>
  gerarTelasDiariasAutomaticas({ slides, projetos, telaDiaria: TV_PROGRAMACAO_DIARIA, telaVisaoGeral: TV_VISAO_GERAL,
    podeDiaria: () => true, podeVisaoGeral: () => true, visaoGeralDisponivel: true, ...o });

/* ------------------------------------------------------------------------------------------- AUTO e validação */
test("AUTO: valor exato aceito na diária; minúsculo/ausente/inválido recusados; data fixa e JSON antigo intactos", () => {
  assert.equal(DATA_REFERENCIA_AUTO, "AUTO");
  assert.ok(dataReferenciaAutomatica("AUTO") && dataReferenciaDoSlideValida("AUTO") && dataReferenciaDoSlideValida("2026-10-15"));
  for (const ruim of ["auto", " AUTO", "AUTO ", "", null, undefined, "hoje", "2026-02-30"]) assert.ok(!dataReferenciaDoSlideValida(ruim as string), String(ruim));
  assert.equal(validar([tv([auto(CHS)])]), null);
  assert.match(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "auto")])])!, /data de referência válida/);
  assert.match(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS])])])!, /data de referência válida/, "ausência continua erro");
  // Data fixa anterior continua válida, inclusive junto com a automática do mesmo projeto.
  assert.equal(validar([tv([slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "2026-10-15"), auto(CHS)])]), null);
  // JSON legado (sem dataReferencia) das telas comuns continua válido.
  assert.equal(validar([tv([{ screenId: "gestao-a-vista-programacao", hubId: "", projectId: CHS.projectId, projectName: CHS.projectName }])]), null);
});

test("AUTO idêntico (mesma tela + mesmo projeto) nunca duplica; projetos diferentes e outras apresentações ok", () => {
  assert.match(validar([tv([auto(CHS), auto(BML), auto(CHS)])])!, /automática repetida para CHS001/);
  assert.equal(validar([tv([auto(CHS), auto(BML)]), tv([auto(CHS)], "tv-sala")]), null, "outra apresentação pode ter a mesma");
  // Editor manual: item idêntico já existente nunca é adicionado de novo.
  assert.deepEqual(itensParaAdicionar(getTelaTv(TV_PROGRAMACAO_DIARIA)!, [CHS], [auto(CHS)], "consolidado", DATA_REFERENCIA_AUTO), []);
});

/* ------------------------------------------------------------------------------------------- botão */
test("Botão / Selecionados: um slide AUTO por projeto + 01 Visão Geral no início; só rascunho (nada é salvo aqui)", () => {
  const existentes = [slideComProjetos("ldp", [CHS])];
  const r = gerar(existentes, [CHS, BML]);
  assert.equal(r.bloqueio, null);
  assert.equal(r.visaoGeral, "inserida");
  assert.deepEqual(r.slides!.map((s) => [s.screenId, s.projectId, s.dataReferencia ?? null]), [
    [TV_VISAO_GERAL, CHS.projectId, null], ["ldp", CHS.projectId, null],
    [TV_PROGRAMACAO_DIARIA, CHS.projectId, "AUTO"], [TV_PROGRAMACAO_DIARIA, BML.projectId, "AUTO"]]);
  assert.deepEqual(r.slides![0].projetos!.map((p) => p.projectId), [CHS.projectId, BML.projectId], "Visão Geral consolida os projetos escolhidos");
  assert.ok(r.slides!.every((s) => !s.projetos || s.screenId === TV_VISAO_GERAL), "diária nunca mescla projetos");
  assert.match(r.mensagens.join(" "), /2 tela\(s\) diária\(s\).*CHS001, BML011\.016/);
  assert.match(r.mensagens.join(" "), /Salvar/);
  assert.deepEqual(existentes.length, 1, "lista recebida não é alterada");
  assert.equal(validar([tv(r.slides!)]), null);
});

test("Botão / Todos permitidos: só os projetos com permissão efetiva da diária (nunca a lista bruta); negados listados", () => {
  const pode = (id: string) => id !== EMI.projectId;
  const r = gerar([], [CHS, BML, TIM, EMI], { podeDiaria: pode, podeVisaoGeral: pode });
  assert.deepEqual(r.adicionados.map((p) => p.projectId), [CHS.projectId, BML.projectId, TIM.projectId]);
  assert.deepEqual(r.semPermissao.map((p) => p.projectId), [EMI.projectId]);
  assert.ok(!r.slides!.some((s) => projetosDe(s).includes(EMI.projectId)), "projeto negado nunca entra (nem na 01)");
  assert.match(r.mensagens.join(" "), /ignorado\(s\) por falta de permissão.*EMI001/);
  // Nenhum projeto com permissão: nada muda.
  const nenhum = gerar([], [EMI], { podeDiaria: pode });
  assert.equal(nenhum.slides, null);
  assert.match(nenhum.bloqueio!, /Sem permissão/);
  assert.equal(gerar([], []).slides, null);
});
const projetosDe = (s: TvSlide) => (s.projetos?.length ? s.projetos.map((p) => p.projectId) : [s.projectId]);

test("Duplicidade: segunda geração não duplica; Visão Geral existente não é duplicada nem movida; data fixa não conta como AUTO", () => {
  const primeira = gerar([], [CHS, BML]).slides!;
  const segunda = gerar(primeira, [CHS, BML, TIM]);
  assert.deepEqual(segunda.adicionados.map((p) => p.projectId), [TIM.projectId]);
  assert.deepEqual(segunda.jaExistentes.map((p) => p.projectId), [CHS.projectId, BML.projectId]);
  assert.equal(segunda.visaoGeral, "existente");
  assert.equal(segunda.slides!.filter((s) => s.screenId === TV_VISAO_GERAL).length, 1);
  assert.deepEqual(segunda.slides!.slice(0, primeira.length), primeira, "ordem já configurada preservada");
  const terceira = gerar(segunda.slides!, [CHS, BML, TIM]);
  assert.equal(terceira.slides, null);
  assert.match(terceira.mensagens[0], /Nada a adicionar/);
  // Visão Geral no MEIO da sequência: mantida onde está.
  const meio = [slideComProjetos("ldp", [CHS]), slideComProjetos(TV_VISAO_GERAL, [CHS])];
  assert.deepEqual(gerar(meio, [CHS]).slides!.map((s) => s.screenId), ["ldp", TV_VISAO_GERAL, TV_PROGRAMACAO_DIARIA]);
  // Slide de data fixa do mesmo projeto não impede o automático.
  const fixa = [slideComProjetos(TV_PROGRAMACAO_DIARIA, [CHS], "2026-10-15")];
  assert.deepEqual(gerar(fixa, [CHS], { visaoGeralDisponivel: false }).slides!.map((s) => s.dataReferencia), ["2026-10-15", "AUTO"]);
});

test("Visão Geral: sem permissão em algum projeto ou indisponível = não inserida, com aviso; diárias continuam", () => {
  const r = gerar([], [CHS, BML], { podeVisaoGeral: (id) => id === CHS.projectId });
  assert.equal(r.visaoGeral, "sem-permissao");
  assert.ok(!r.slides!.some((s) => s.screenId === TV_VISAO_GERAL));
  assert.match(r.mensagens.join(" "), /01 Visão Geral NÃO inserida: sem permissão em BML011\.016/);
  const ind = gerar([], [CHS], { visaoGeralDisponivel: false });
  assert.equal(ind.visaoGeral, "indisponivel");
  assert.match(ind.mensagens.join(" "), /NÃO inserida: tela indisponível/);
});

test("Limite de 50: contagem exata; acima do limite NADA é adicionado (nunca parcial silencioso)", () => {
  const projetos = Array.from({ length: 49 }, (_, i) => P(i % 9 + 1, `PRJ${i}`)).map((p, i) => ({ ...p, projectId: `b.${String(i).padStart(8, "0")}-0000-0000-0000-000000000000` }));
  const cabe = gerar([], projetos);                                       // 1 Visão Geral + 49 diárias = 50
  assert.equal(cabe.totalResultante, 50);
  assert.equal(cabe.slides!.length, TV_LIMITES.maxTelas);
  const estoura = gerar([slideComProjetos("ldp", [CHS])], projetos);     // 1 + 1 + 49 = 51
  assert.equal(estoura.slides, null);
  assert.equal(estoura.totalResultante, 51);
  assert.match(estoura.bloqueio!, /Limite de 50 telas por TV: seriam 51 \(1 atuais \+ 49 diária\(s\) \+ 01 Visão Geral\)\. Nada foi adicionado/);
});

/* ------------------------------------------------------------------------------------------- datas */
test("Data civil da Bahia: virada da meia-noite (UTC−3), domingo cruza a semana, segunda pega o domingo", () => {
  assert.equal(dataCivilBahia(new Date("2026-10-20T02:59:59Z")), "2026-10-19");   // 23:59:59 em Salvador
  assert.equal(dataCivilBahia(new Date("2026-10-20T03:00:00Z")), "2026-10-20");   // 00:00:00 em Salvador
  assert.equal(diaAnterior("2026-03-01"), "2026-02-28");
  assert.equal(diaAnterior("2026-01-01"), "2025-12-31");
  // Domingo 25/10: ontem é o sábado da semana anterior — uma consulta [24, 25], sem duplicar.
  assert.deepEqual(janelaDiariaAutomatica("2026-10-25"), { hoje: "2026-10-25", ontem: "2026-10-24", inicio: "2026-10-24", fim: "2026-10-25" });
  assert.deepEqual(janelaDiariaAutomatica("2026-10-26"), { hoje: "2026-10-26", ontem: "2026-10-25", inicio: "2026-10-25", fim: "2026-10-26" });
});

/* ------------------------------------------------------------------------------------------- análise */
let seq = 0;
const linha = (o: Partial<ProgramacaoLinha> & { documento: string | null; dataAtividade: string }): ProgramacaoLinha => ({ idRegistro: ++seq, tarefa: "ELABORAÇÃO",
  disciplinaWeekly: null, siglaDoc3: null, disciplinaNorma: "MECÂNICA", disciplinaCanonica: "MECHANICAL", fonteDisciplina: "DOC3", elaborador: "ANA", verificador: "BIA",
  aprovador: null, hhElaboracao: 4, hhVerificacao: 2, hhAprovacao: null, baseline: null, reprogramado: null, status: "EM ANDAMENTO", fase: null, ...o });
const resposta = (projectId: string, linhas: ProgramacaoLinha[]): ProgramacaoResposta => ({ projectId, inicio: "2026-10-19", fim: "2026-10-20", ultimaSincronizacao: null,
  registrosProjeto: linhas.length, registrosSemData: 0, datasComProgramacao: [], linhas });
const ldpDoc = (codigo: string, o: Partial<DocumentoLdpEmissao> = {}): DocumentoLdpEmissao => ({ codigo, emitido: false, cancelado: false, dataEmissao: null, ...o });
const PRJ = CHS.projectId;
const cat = criarCatalogo([], [{ id: PRJ, name: CHS.projectName! }]);

const dadosBase = () => [resposta(PRJ, [
  linha({ documento: "HOJE-1", dataAtividade: "2026-10-20T00:00:00" }),
  linha({ documento: "HOJE-1", tarefa: "VERIFICAÇÃO", dataAtividade: "2026-10-20" }),
  linha({ documento: "HOJE-2", disciplinaNorma: "CIVIL", disciplinaCanonica: "CIVIL", hhElaboracao: null, hhVerificacao: null, dataAtividade: "2026-10-20" }),
  linha({ documento: "PEND-1", dataAtividade: "2026-10-19" }),
  linha({ documento: "PEND-1", dataAtividade: "2026-10-19" }),                     // repetição da importação (mesma chave da 02)
  linha({ documento: "OK-ONTEM", dataAtividade: "2026-10-19" }),
  linha({ documento: "DEPOIS", dataAtividade: "2026-10-19" }),
  linha({ documento: "SEM-DATA", dataAtividade: "2026-10-19" }),
  linha({ documento: "FORA-LDP", disciplinaNorma: "CIVIL", disciplinaCanonica: "CIVIL", dataAtividade: "2026-10-19" }),
  linha({ documento: null, dataAtividade: "2026-10-19" }),
  linha({ documento: "ANTEONTEM", dataAtividade: "2026-10-18" }),                  // fora da janela: nunca vira histórico
  linha({ documento: "AMANHA", dataAtividade: "2026-10-21" }),
])];
const ldpBase = new Map([[PRJ, [ldpDoc("HOJE-1"), ldpDoc("PEND-1"), ldpDoc("OK-ONTEM", { emitido: true, dataEmissao: "2026-10-19T17:00:00" }),
  ldpDoc("DEPOIS", { emitido: true, dataEmissao: "2026-10-20" }), ldpDoc("SEM-DATA", { emitido: true })]]]);

test("Análise: previsto HOJE e pendente de ONTEM por disciplina; só não emissão comprovada é pendência; status desconhecido separado", () => {
  const a = analiseDiariaAutomatica({ dados: dadosBase(), cat, hoje: "2026-10-20", ldp: ldpBase });
  assert.equal(a.ontem, "2026-10-19");
  assert.equal(a.foraDaJanela, 2);
  assert.equal(a.repetidos, 1);
  assert.deepEqual(a.disciplinas.map((d) => d.nome), ["CIVIL", "MECÂNICA"]);
  const mec = a.disciplinas.find((d) => d.nome === "MECÂNICA")!, civ = a.disciplinas.find((d) => d.nome === "CIVIL")!;
  // Hoje: HOJE-1 com 2 atividades = 1 documento, HH somado; HH desconhecido fica null (nunca zero inventado).
  assert.equal(mec.documentosHoje, 1);
  assert.equal(mec.hoje.length, 2);
  assert.equal(mec.hhHoje, 12);
  assert.equal(civ.documentosHoje, 1);
  assert.equal(civ.hhHoje, null);
  // Ontem: PEND-1 (não emitido na LDP) é a única pendência; deduplicado; HH de uma atividade.
  assert.deepEqual(mec.pendentes.map((x) => x.codigo), ["PEND-1"]);
  assert.equal(mec.pendentes[0].atividades.length, 1);
  assert.equal(mec.hhPendente, 6);
  assert.equal(mec.emitidosOntem, 1, "OK-ONTEM emitido até ontem");
  assert.equal(mec.regularizados, 1, "DEPOIS emitido hoje: não é pendência");
  // Sem comprovação: emitido sem data (MEC), sem código (MEC), fora da LDP (CIVIL) — nunca chamados de pendentes.
  assert.deepEqual(mec.semComprovacao.map((x) => x.motivo).sort(), ["Atividade sem código de documento", "Emitido na LDP sem data de emissão (não comprova a emissão até ontem)"]);
  assert.deepEqual(civ.semComprovacao.map((x) => [x.codigo, x.motivo]), [["FORA-LDP", "Código não encontrado na LDP do projeto"]]);
  assert.deepEqual(civ.pendentes, []);
  assert.deepEqual(a.totais, { atividadesHoje: 3, documentosHoje: 2, hhHoje: 12, documentosOntem: 6, pendentes: 1, hhPendente: 6, semComprovacao: 3, emitidosOntem: 1, regularizados: 1 });
});

test("Análise: LDP indisponível, cancelado e código repetido com situações diferentes = sem comprovação (nunca pendente)", () => {
  const dados = [resposta(PRJ, [linha({ documento: "A", dataAtividade: "2026-10-19" }), linha({ documento: "B", dataAtividade: "2026-10-19" }), linha({ documento: "C", dataAtividade: "2026-10-19" })])];
  const sem = analiseDiariaAutomatica({ dados, cat, hoje: "2026-10-20", ldp: new Map() });
  assert.equal(sem.totais.pendentes, 0);
  assert.equal(sem.totais.semComprovacao, 3);
  const ldp = new Map([[PRJ, [ldpDoc("A", { cancelado: true }), ldpDoc("B"), ldpDoc("B", { emitido: true, dataEmissao: "2026-10-01" }), ldpDoc("C")]]]);
  const r = analiseDiariaAutomatica({ dados, cat, hoje: "2026-10-20", ldp });
  assert.deepEqual(r.disciplinas[0].pendentes.map((x) => x.codigo), ["C"]);
  assert.deepEqual(r.disciplinas[0].semComprovacao.map((x) => x.motivo).sort(), ["Código repetido na LDP com situações diferentes", "Documento cancelado na LDP"]);
});

test("Virada do dia: os mesmos dados recalculados para o dia seguinte mudam HOJE/ONTEM; dia vazio = vazio explícito", () => {
  const amanha = analiseDiariaAutomatica({ dados: dadosBase(), cat, hoje: "2026-10-21", ldp: ldpBase });
  assert.equal(amanha.ontem, "2026-10-20");
  assert.deepEqual(amanha.disciplinas.flatMap((d) => d.hoje.map((l) => l.documento)), ["AMANHA"]);
  // HOJE-1 (não emitido) programado em 20/10 vira pendência de ontem em 21/10; HOJE-2 está fora da LDP → sem comprovação.
  assert.deepEqual(amanha.disciplinas.flatMap((d) => d.pendentes.map((x) => x.codigo)), ["HOJE-1"]);
  assert.ok(!amanha.disciplinas.some((d) => d.pendentes.some((x) => x.codigo === "PEND-1")), "anteontem nunca é pendência (não é histórico)");
  const vazio = analiseDiariaAutomatica({ dados: [resposta(PRJ, [])], cat, hoje: "2026-10-25", ldp: ldpBase });
  assert.deepEqual(vazio.disciplinas, []);
  assert.equal(vazio.totais.documentosHoje, 0);
  assert.equal(vazio.totais.hhHoje, null);
});

test("01 Visão Geral inalterada pela refatoração da conciliação (emitido depois do sábado continua 'pendente')", () => {
  const SEMANA = { start: "2026-10-11", end: "2026-10-17" };
  const dados = [{ ...resposta(PRJ, [linha({ documento: "X", dataAtividade: "2026-10-13" }), linha({ documento: "Y", dataAtividade: "2026-10-13" })]), inicio: SEMANA.start, fim: SEMANA.end }];
  const ldp = new Map([[PRJ, [ldpDoc("X", { emitido: true, dataEmissao: "2026-10-20" }), ldpDoc("Y"), ldpDoc("Y", { emitido: true, dataEmissao: "2026-10-22" })]]]);
  const vg = visaoGeralDaSemana({ dados, projetos: [{ id: PRJ, name: "CHS" }], cat, semana: SEMANA, hoje: "2026-10-25", ldp });
  assert.deepEqual(vg.itens.map((x) => [x.codigo, x.situacao, x.motivo]), [["X", "pendente", null], ["Y", "pendente", null]]);
  assert.equal(vg.atrasados, 2);
});

/* ------------------------------------------------------------------------------------------- player / contrato */
const eff = (extra: Partial<EffectivePermissions>): EffectivePermissions =>
  ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "tv@na.com", projectIdAcc: null, permissions: [], anyProject: [], ...extra });
const renderDiaria = (dataReferencia: string | null) => {
  const efetivas = eff({ isMasterAdmin: true });
  const valor = { loading: false, error: null, effective: efetivas, can: criarCan(efetivas, "any"), reload: () => undefined };
  return renderToStaticMarkup(createElement(MemoryRouter, null, createElement(PermissionContext.Provider, { value: valor },
    createElement(ConteudoTv, { screenId: TV_PROGRAMACAO_DIARIA, selection: [{ id: CHS.projectId, hubId: "", name: CHS.projectName! }], dataReferencia }))));
};

test("Player: slide AUTO mostra projeto, HOJE e ONTEM vigentes (Bahia); data fixa continua igual; nunca o aviso de data ausente", () => {
  const hoje = dataCivilBahia();
  const html = renderDiaria("AUTO");
  assert.match(html, /data-testid="gestao-a-vista-programacao-diaria"/);
  assert.match(html, /AUTOMÁTICO · HOJE \+ PENDÊNCIAS DE ONTEM/);
  const br = (d: string) => d.split("-").reverse().join("/");
  assert.ok(html.includes(`HOJE `) && html.includes(br(hoje)) && html.includes(br(diaAnterior(hoje))), "datas vigentes no cabeçalho");
  assert.ok(!/sem data de referência/.test(html));
  assert.match(html, /CHS001 - ETHANOL PLANT/);
  assert.match(renderDiaria("2026-10-15"), /data-testid="gv-dia-referencia">QUI 15\/10\/2026</);
  assert.match(renderDiaria(null), /Programação Diária sem data de referência/);
});

test("Contratos: página/player/editor/backend usam AUTO com janela [ontem, hoje], remontagem diária e filtro efetivo de permissões", () => {
  const pagina = fonte("src/features/production/analytics/GestaoVistaJamilePage.tsx");
  assert.match(pagina, /<GestaoVistaJamileConteudo key=\{hoje\} \{\.\.\.props\} hojeAuto=\{hoje\}\/>/, "virada do dia remonta e rebusca tudo");
  assert.match(pagina, /const progAuto=useProgramacao\(selection,auto\?\.inicio\?\?week\.start,auto\?\.fim\?\?week\.end,auto!==null\);/);
  assert.match(pagina, /\.\.\.\(auto\?progAuto\.falhas:\[\]\)/, "falha bloqueia (sem fallback)");
  const hook = fonte("src/features/tv/useHojeBahia.ts");
  assert.match(hook, /setInterval/);
  assert.match(hook, /atual === agora \? atual : agora/, "estado só muda quando o dia muda (sem loop de render)");
  const player = fonte("src/features/tv/TvPlayerPage.tsx");
  assert.match(player, /Hoje \$\{formatarDataReferencia\(hoje\)\} \+ pendências de \$\{formatarDataReferencia\(diaAnterior\(hoje\)\)\}/);
  const editor = fonte("src/features/settings/TelasTvConfigPage.tsx");
  assert.match(editor, /opcoes\.filter\(\(p\) => podeTela\(p\.id, chavesTelaTv\(TV_PROGRAMACAO_DIARIA\)\)\)/, "todos = filtro efetivo, nunca lista bruta");
  assert.match(editor, /Gerar telas diárias automáticas/);
  assert.match(editor, /ROTULO_DIARIA_AUTO/);
  assert.ok(!/type="date"[^>]*value=\{s\.dataReferencia[^}]*AUTO/.test(editor));
  assert.equal(ROTULO_DIARIA_AUTO, "Hoje + pendências de ontem (automático)");
  const dia = fonte("../Services/Tv/TvDiaAutomatico.cs");
  assert.match(dia, /public const string Auto = "AUTO";/);
  assert.match(dia, /"America\/Bahia", "Bahia Standard Time"/);
  assert.match(dia, /\(hoje\.AddDays\(-1\), hoje\)/);
  const regras = fonte("../Services/Tv/TvConfiguration.cs");
  assert.match(regras, /DataReferenciaDoSlideValida\(t\.DataReferencia\)/);
  assert.match(fonte("../Services/Tv/Portal/TvPortalResources.cs"), /TvDiaAutomatico\.JanelaPermitida\(query\["inicio"\]/);
  assert.match(fonte("../Controllers/TvConfigurationController.cs"), /diariaAutomatica = true/);
});
