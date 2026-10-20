import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { criarCan } from "../../permissions/permissionLogic";
import {
  MONITOR_POS_FIM_MS, PERMISSAO_EXECUTAR_ROBO, PERMISSOES_ACOMPANHAR_ROBO, POLLING_EM_EXECUCAO_MS, POLLING_OCIOSO_MS,
  corpoExecucao, criarPolling, deveExibirMonitor, erroRobo, etapaAtual, etapasConcluidas, interpretarRespostaInicio, intervaloPolling,
  percentualRobo, proximaEtapa, rotuloMinimizado, tempoDecorrido, type StatusRobo,
} from "../robotExecution";

/** Código sem comentários (os comentários citam endpoints/hubId de propósito). */
const codigo = (t: string) => t.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\{\/\*[\s\S]*?\*\/\}/g, "").replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};
const eff = (permissions: string[]) => ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "x@na.com", projectIdAcc: null, permissions, anyProject: permissions });
const P1 = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";
const P2 = "b.92c9d539-6092-4a08-a812-72801dc15fe7";

const RODANDO: StatusRobo = {
  state: "RUNNING", manual: true, projectId: P1, projectName: "CHS001", startedAt: "2026-10-06T11:30:00", finishedAt: null,
  message: "Execução manual de 3 projetos em andamento.", queuePosition: 1, queueTotal: 3, nextProject: "BML011.013",
  stages: [
    { id: "04", name: "GRDs", state: "COMPLETED" }, { id: "05", name: "WORKFLOW", state: "COMPLETED" },
    { id: "5B", name: "SNAPSHOT DAS ETAPAS ATUAIS DOS ACC REVIEWS", state: "COMPLETED" }, { id: "06", name: "CUSTO", state: "RUNNING" },
    { id: "07", name: "ATRIBUTOS ACC", state: "PENDING" },
  ],
  currentStageId: "06", stagePosition: 4, stageTotal: 5, overallCompletedStages: 3, overallTotalStages: 8, progressPercent: 37,
  recentEntries: [{ at: "2026-10-06T11:34:18", project: "CHS001", stage: "ETAPA 06 - CUSTO", status: "INICIADO" }],
};

test("1/2: INICIAR ROBÔ aparece na Home só com robot.run", () => {
  const home = codigo(fonte("src/pages/Dashboard.tsx"));
  assert.match(home, /const podeExecutarRobo = can\(PERMISSAO_EXECUTAR_ROBO\);/);
  assert.match(home, /\{podeExecutarRobo && <RobotRunButton projects=\{projetosRobo\} \/>\}/);
  assert.equal(PERMISSAO_EXECUTAR_ROBO, "robot.run");
  assert.equal(criarCan(eff(["robot.run"]), "any")(PERMISSAO_EXECUTAR_ROBO), true);
  assert.equal(criarCan(eff(["robot.view", "planning.coordination.view"]), "any")(PERMISSAO_EXECUTAR_ROBO), false);
  assert.match(codigo(fonte("src/components/RobotRunButton.tsx")), /INICIAR ROBÔ/);
});

test("3: seleção envia só Project_ID_ACC (sem hubId) e a Home continua N&A-first", () => {
  assert.deepEqual(corpoExecucao([P1, P2, P1]), { projectIds: [P1, P2] });
  assert.deepEqual(corpoExecucao("todos"), { allProjects: true });
  const botao = codigo(fonte("src/components/RobotRunButton.tsx"));
  assert.doesNotMatch(botao, /hubId/);
  assert.match(botao, /export function RobotRunButton\(\{ projects \}: \{ projects: RobotRunProject\[\] \}\)/);
  const home = codigo(fonte("src/pages/Dashboard.tsx"));
  assert.match(home, /\/api\/me\/projects/);
  assert.match(home, /projects\.filter\(p => p\.isAtivo\)\.map\(p => \(\{ id: p\.projectIdAcc, name: p\.nomeProjeto \}\)\)/);
  assert.doesNotMatch(home, /\/api\/(hubs|projects\/hub)|GetHubs|hubId: hub/i);
});

test("4/5: backend resolve o hub e valida UserProjects + robot.run por projeto (contrato do controller)", () => {
  const c = codigo(fonte("../Controllers/RobotRunController.cs"));
  assert.match(c, /access\.CheckProjectAccessAsync\(project\.ProjectId, ct\)/);
  assert.match(c, /permissions\.CheckAsync\(RunKeys, project\.ProjectId, ct\)/);
  assert.match(c, /HasHub\(project\)/);
  assert.doesNotMatch(c, /request\.HubId/);
  assert.doesNotMatch(c, /GetString\("AccessToken"\)/);
});

test("6/13: monitor global montado UMA vez no shell autenticado (acima de Home e Layout) — navegação não desmonta", () => {
  const app = codigo(fonte("src/App.tsx"));
  assert.match(app, /<PermissionProvider><RobotExecutionProvider><ProjetoAtualProvider><ProjectIdentityProvider><Outlet \/><\/ProjectIdentityProvider><\/ProjetoAtualProvider><RobotExecutionMonitor \/><\/RobotExecutionProvider><\/PermissionProvider>/);
  assert.equal(app.split("<RobotExecutionMonitor />").length - 1, 1);
  const shell = app.indexOf("<RobotExecutionMonitor />");
  assert.ok(shell < app.indexOf('path="/dashboard"') && shell < app.indexOf("<Layout />"), "o shell envolve a Home e o Layout");
  for (const f of ["src/components/Layout.tsx", "src/pages/Dashboard.tsx"]) assert.doesNotMatch(codigo(fonte(f)), /RobotExecutionMonitor|RobotExecutionProvider/);
  assert.equal(deveExibirMonitor(RODANDO, null, Date.now()), true);
  assert.equal(deveExibirMonitor({ ...RODANDO, state: "IDLE" }, null, Date.now()), false);
  assert.equal(rotuloMinimizado(RODANDO), "CHS001 • 37% • ETAPA 06 - CUSTO");
});

test("7/8: percentual = etapas reais do backend, limitado a 0–100; sem plano não há percentual", () => {
  assert.equal(percentualRobo(RODANDO), 37);
  assert.equal(percentualRobo({ ...RODANDO, progressPercent: 140 }), 100);
  assert.equal(percentualRobo({ ...RODANDO, progressPercent: -5 }), 0);
  assert.equal(percentualRobo({ ...RODANDO, progressPercent: null }), null);
  const logica = codigo(fonte("src/features/robot/robotExecution.ts"));
  assert.doesNotMatch(logica.slice(logica.indexOf("export function percentualRobo"), logica.indexOf("export const chaveExecucao")), /Date|agora|setInterval/);
});

test("9/10: projeto X/Y e etapa X/Y vêm do backend; atual, concluídas e próxima pelo plano", () => {
  const mon = codigo(fonte("src/features/robot/RobotExecutionMonitor.tsx"));
  assert.match(mon, /\{s\.queuePosition \?\? 0\} de \{s\.queueTotal\}/);
  assert.match(mon, /\{s\.stagePosition \?\? 0\} de \{s\.stageTotal\}/);
  assert.equal(etapaAtual(RODANDO)?.id, "06");
  assert.deepEqual(etapasConcluidas(RODANDO).map((e) => e.id), ["04", "05", "5B"]);
  assert.equal(proximaEtapa(RODANDO)?.id, "07");
  assert.equal(tempoDecorrido("2026-10-06T11:30:00", null, Date.parse("2026-10-06T11:34:32")), "00:04:32");
});

test("11: COMPLETED chega a 100% e mostra CONCLUÍDO; some quando fechado", () => {
  const fim: StatusRobo = { ...RODANDO, state: "COMPLETED", finishedAt: "2026-10-06T12:00:00", progressPercent: 100, message: "CONCLUÍDO COM SUCESSO" };
  assert.equal(percentualRobo(fim), 100);
  assert.equal(rotuloMinimizado(fim), "CHS001 • 100% • CONCLUÍDO");
  const agora = Date.parse("2026-10-06T12:05:00");
  assert.equal(deveExibirMonitor(fim, null, agora), true);
  assert.equal(deveExibirMonitor(fim, fim.startedAt, agora), false, "fechado pelo usuário");
  assert.equal(deveExibirMonitor(fim, null, Date.parse(fim.finishedAt!) + MONITOR_POS_FIM_MS + 1), false, "expira depois de um tempo");
  assert.equal(deveExibirMonitor({ ...RODANDO }, RODANDO.startedAt, agora), true, "em execução nunca some");
});

test("12: ERROR destaca projeto/etapa e mensagem", () => {
  const erro: StatusRobo = { ...RODANDO, state: "ERROR", finishedAt: "2026-10-06T12:00:00",
    stages: [{ id: "04", name: "GRDs", state: "COMPLETED" }, { id: "06", name: "CUSTO", state: "ERROR", message: "Schema Gestão à Vista indisponível." }],
    message: "CONCLUÍDO COM ERROS: 1 projeto(s)/falha(s)." };
  assert.deepEqual(erroRobo(erro), { projeto: "CHS001", etapa: "06 - CUSTO", mensagem: "Schema Gestão à Vista indisponível." });
  assert.deepEqual(erroRobo({ ...erro, stages: [], projects: [{ projectId: P2, name: "BML", state: "ERROR", message: "LDP não encontrada" }] }),
    { projeto: "BML", etapa: null, mensagem: "LDP não encontrada" });
  assert.equal(erroRobo(RODANDO), null);
  assert.match(rotuloMinimizado(erro), /ERRO$/);
  assert.match(codigo(fonte("src/features/robot/RobotExecutionMonitor.tsx")), /data-testid="robot-erro"/);
});

test("14: 409 adota a execução ATIVA (não cria outro monitor)", () => {
  const r = interpretarRespostaInicio(409, { message: "Robô já está em execução.", status: RODANDO });
  assert.equal(r.tipo, "ocupado");
  assert.equal(r.tipo === "ocupado" && r.status?.projectName, "CHS001");
  assert.equal(interpretarRespostaInicio(202, RODANDO).tipo, "iniciado");
  assert.equal(interpretarRespostaInicio(403, { message: "Projeto não autorizado." }).tipo, "erro");
  assert.equal(interpretarRespostaInicio(401, {}).tipo, "sessao");
  const botao = codigo(fonte("src/components/RobotRunButton.tsx"));
  assert.match(botao, /resposta\.tipo === 'ocupado'\) \{ aceitar\(resposta\.status\)/);
  assert.doesNotMatch(botao, /RobotExecutionMonitor/);
});

test("15: polling único — só o provider consulta /api/worker/status; nunca dois timers", async () => {
  const consultas = ["src/features/robot/RobotExecutionProvider.tsx", "src/components/RobotRunButton.tsx", "src/features/robot/RobotExecutionMonitor.tsx",
    "src/features/robot-configuration/RobotConfigurationPage.tsx", "src/pages/Dashboard.tsx", "src/components/Layout.tsx"]
    .filter((f) => codigo(fonte(f)).includes("/api/worker/status"));
  assert.deepEqual(consultas, ["src/features/robot/RobotExecutionProvider.tsx"]);
  assert.doesNotMatch(codigo(fonte("src/features/robot-configuration/RobotConfigurationPage.tsx")), /setInterval/);
  assert.equal(intervaloPolling(RODANDO), POLLING_EM_EXECUCAO_MS);
  assert.equal(intervaloPolling({ ...RODANDO, state: "COMPLETED" }), POLLING_OCIOSO_MS);
  assert.deepEqual([...PERMISSOES_ACOMPANHAR_ROBO], ["robot.view"]);

  // Agendador falso: conta timers vivos e consultas.
  const vivos = new Map<number, () => void>(); let seq = 0; let chamadas = 0; const intervalos: number[] = [];
  const t = { set: (fn: () => void, ms: number) => { intervalos.push(ms); vivos.set(++seq, fn); return seq; }, clear: (id: unknown) => { vivos.delete(id as number); } };
  const p = criarPolling(async () => { chamadas++; return RODANDO; }, t);
  p.iniciar(); p.iniciar(); p.iniciar();
  await new Promise((r) => setImmediate(r));
  assert.equal(chamadas, 1); assert.equal(vivos.size, 1); assert.deepEqual(intervalos, [POLLING_EM_EXECUCAO_MS]);
  p.agora(); p.agora();
  await new Promise((r) => setImmediate(r));
  assert.ok(vivos.size <= 1, "nunca mais de um timer agendado");
  p.parar();
  assert.equal(vivos.size, 0);
});
