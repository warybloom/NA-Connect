import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import {
  applyRoutingOperation, buildOperationBody, canApply, checkPreview, classifyWarning, draftFingerprint, filterOrigins,
  getRoutingControl, groupByOrigin, normalizeRoutingControl, operationEffectText, operationsFor, previewRoutingOperation,
  RoutingControlError, summarizeOrigins, validateDraft, EMPTY_ROUTING_FILTERS, MIN_REASON_LENGTH,
  type RoutingControlItem, type RoutingOperationRequest, type RoutingPreview,
} from "../routingControlApi";
import { RoutingOriginsTable } from "../RoutingControlSection";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};

const P = "b.projeto-teste";
const item = (over: Partial<RoutingControlItem>): RoutingControlItem => ({
  sourceDocumentCode: "ORIG-001", sourceTitle: "Origem", discipline: "MODELAGEM 3D", sourceHours: 10, sourceCost: 1000,
  professionals: ["Pessoa A"], method: "DE_PARA", active: true, decisionId: "1", destinationDocumentCode: "DEST-001",
  destinations: [{ documentCode: "DEST-001", title: "Destino", hours: 10, cost: 1000 }],
  decisionBy: "resp@empresa", decisionAtUtc: "2026-10-29T10:00:00Z", reason: "vínculo validado", conflict: false, warning: null, version: "v1",
  ...over,
});

type FetchCall = { url: string; init?: RequestInit };
function mockFetch(responder: (call: FetchCall) => Response | Promise<Response>) {
  const calls: FetchCall[] = [];
  const original = globalThis.fetch;
  globalThis.fetch = (async (url: string, init?: RequestInit) => {
    const call = { url: String(url), init };
    calls.push(call);
    return responder(call);
  }) as typeof fetch;
  return { calls, restore: () => { globalThis.fetch = original; } };
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

// ------------------------------------------------------------------ CONTRATO HTTP ----

test("GET sem projeto não chama a API", async () => {
  const m = mockFetch(() => json({}));
  try {
    await assert.rejects(getRoutingControl(""), (e: unknown) => e instanceof RoutingControlError && e.kind === "invalid");
    await assert.rejects(getRoutingControl("   "));
    assert.equal(m.calls.length, 0);
  } finally { m.restore(); }
});

test("GET usa a URL exata do contrato e normaliza a resposta camelCase", async () => {
  const m = mockFetch(() => json({
    projectId: P, generatedAtUtc: "2026-10-29T12:00:00Z",
    items: [{ ...item({}), professionals: "Pessoa A; Pessoa B" }],
    history: [{ sourceDocumentCode: "ORIG-001", method: "DE_PARA", details: "A → B", by: "x", atUtc: "2026-10-28T00:00:00Z", reason: "r" }],
  }));
  try {
    const result = await getRoutingControl(P);
    assert.equal(m.calls[0].url, `/api/information-quality-audit/routing-control?projectId=${encodeURIComponent(P)}`);
    assert.equal(m.calls[0].init?.method, undefined, "GET");
    assert.deepEqual(result.items[0].professionals, ["Pessoa A", "Pessoa B"]);
    assert.equal(result.history.length, 1);
  } finally { m.restore(); }
});

test("API indisponível (404/503/rede) vira 'unavailable'; 403 'forbidden'; 409 'conflict'", async () => {
  for (const [status, kind] of [[404, "unavailable"], [503, "unavailable"], [403, "forbidden"], [409, "conflict"]] as const) {
    const m = mockFetch(() => json({ message: "x" }, status));
    try {
      await assert.rejects(getRoutingControl(P), (e: unknown) => e instanceof RoutingControlError && e.kind === kind);
    } finally { m.restore(); }
  }
  const m = mockFetch(() => { throw new TypeError("Failed to fetch"); });
  try {
    await assert.rejects(getRoutingControl(P), (e: unknown) => e instanceof RoutingControlError && e.kind === "unavailable");
  } finally { m.restore(); }
});

test("GET cancelado propaga AbortError (não vira erro de tela)", async () => {
  const m = mockFetch(({ init }) => new Promise<Response>((_, reject) => {
    init?.signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
  }));
  try {
    const controller = new AbortController();
    const pending = getRoutingControl(P, controller.signal);
    controller.abort();
    await assert.rejects(pending, (e: unknown) => e instanceof DOMException && e.name === "AbortError");
  } finally { m.restore(); }
});

const req = (over: Partial<RoutingOperationRequest> = {}): RoutingOperationRequest => ({
  projectId: P, sourceDocumentCode: "ORIG-001", operation: "EDIT_DE_PARA", destinationDocumentCode: " DEST-002 ",
  reason: "  confirmado com o responsável  ", version: "v1", ...over,
});

test("preview e apply: POST nas URLs do contrato, corpo exato com version e reason", async () => {
  const m = mockFetch(({ url }) => url.includes("/token?")
    ? json({ token: "csrf-token-simulacao" })
    : url.endsWith("/preview")
      ? json({ canConfirm: true, blockingReason: null, before: { sourceHours: 10, sourceCost: 1000, destinations: [] }, after: { sourceHours: 10, sourceCost: 1000, destinations: [] }, totalHoursDelta: 0, totalCostDelta: 0, version: "v2" })
      : json({ success: true, message: "ok", version: "v3" }));
  try {
    const preview = await previewRoutingOperation(req());
    await applyRoutingOperation({ ...req(), version: preview.version! });
    assert.equal(m.calls[0].url, "/api/information-quality-audit/routing-control/preview");
    assert.equal(m.calls[1].url, `/api/information-quality-audit/routing-control/token?projectId=${encodeURIComponent(P)}`);
    assert.equal(m.calls[2].url, "/api/information-quality-audit/routing-control/apply");
    assert.equal(new Headers(m.calls[2].init?.headers).get("X-Sidebar-CSRF"), "csrf-token-simulacao");
    assert.equal(m.calls[0].init?.method, "POST");
    assert.deepEqual(JSON.parse(String(m.calls[0].init?.body)), {
      projectId: P, sourceDocumentCode: "ORIG-001", operation: "EDIT_DE_PARA", reason: "confirmado com o responsável", version: "v1", destinationDocumentCode: "DEST-002",
    });
    assert.equal(JSON.parse(String(m.calls[2].init?.body)).version, "v2", "apply usa a versão devolvida pela simulação");
  } finally { m.restore(); }
});

test("RESET não envia destinationDocumentCode", () => {
  assert.equal("destinationDocumentCode" in buildOperationBody(req({ operation: "RESET_DE_PARA" })), false);
  assert.equal("destinationDocumentCode" in buildOperationBody(req({ operation: "RESET_REDISTRIBUICAO" })), false);
});

test("apply com conflito de versão (409) rejeita como 'conflict'", async () => {
  const m = mockFetch(({ url }) => url.includes("/token?")
    ? json({ token: "csrf-token" }) : json({ message: "Versão desatualizada." }, 409));
  try {
    await assert.rejects(applyRoutingOperation(req()), (e: unknown) => e instanceof RoutingControlError && e.kind === "conflict");
  } finally { m.restore(); }
});

// ------------------------------------------------------------------ REGRAS ----

test("3D0 (AT da origem) e PL1: De/Para específico preservado; redistribuição inativa não conflita", () => {
  const origins = groupByOrigin({
    items: [
      item({ sourceDocumentCode: "X-3D0-AT-001", sourceHours: 38, destinationDocumentCode: "X-3DM-3D-002", destinations: [{ documentCode: "X-3DM-3D-002", title: "Modelo Federado Geral", hours: 38, cost: 3800 }] }),
      item({ sourceDocumentCode: "X-3D0-AT-001", method: "REDISTRIBUICAO", active: false, sourceHours: 38, decisionId: "r1", destinationDocumentCode: null }),
      item({ sourceDocumentCode: "X-PL1-LD-001", sourceHours: 8.25, destinationDocumentCode: "X-3DM-3D-001" }),
    ],
    history: [],
  });
  const at = origins.find((o) => o.sourceDocumentCode === "X-3D0-AT-001")!;
  assert.equal(at.bothActive, false);
  assert.equal(at.sourceHours, 38, "origem contada uma vez, nunca somada entre métodos");
  assert.deepEqual([...at.activeMethods], ["DE_PARA"]);
  assert.deepEqual(operationsFor(at.items[1], true), [], "redistribuição inativa nunca é reativada pela tela");
  assert.equal(summarizeOrigins(origins).hours, 38 + 8.25);
});

test("De/Para + Redistribuição ativos na mesma origem → conflito explícito, sem somar HH", () => {
  const [origin] = groupByOrigin({
    items: [item({}), item({ method: "REDISTRIBUICAO", decisionId: "r", destinationDocumentCode: null })],
    history: [],
  });
  assert.equal(origin.bothActive, true);
  assert.equal(origin.alerts[0].kind, "AMBOS_ATIVOS");
  assert.equal(summarizeOrigins([origin]).hours, 10);
  assert.equal(filterOrigins([origin], { ...EMPTY_ROUTING_FILTERS, type: "CONFLITANTE" }).length, 1);
  assert.match(operationEffectText("RESET_REDISTRIBUICAO", origin), /De\/Para específico ativo permanece/);
  assert.match(operationEffectText("RESET_DE_PARA", origin), /Redistribuição por disciplina ativa permanece/);
});

test("SF0/AT: sem destino documental e sem operação nesta central", () => {
  const at = item({ sourceDocumentCode: "X-SF0-AT-001", method: "AT", discipline: "SEGURANÇA", destinations: [], destinationDocumentCode: null });
  assert.deepEqual(operationsFor(at, true), []);
  const [origin] = groupByOrigin({ items: [at], history: [] });
  assert.equal(origin.discipline, "SEGURANÇA");
  assert.equal(filterOrigins([origin], { ...EMPTY_ROUTING_FILTERS, type: "AT" }).length, 1);
  assert.equal(filterOrigins([origin], { ...EMPTY_ROUTING_FILTERS, type: "DE_PARA" }).length, 0);
});

test("somente leitura: nenhuma operação; sem version: nenhuma operação", () => {
  assert.deepEqual(operationsFor(item({}), false), []);
  assert.deepEqual(operationsFor(item({ version: null }), true), []);
  assert.deepEqual(operationsFor(item({}), true), ["EDIT_DE_PARA", "RESET_DE_PARA"]);
  assert.deepEqual(operationsFor(item({ method: "REDISTRIBUICAO" }), true), ["RESET_REDISTRIBUICAO"]);
});

test("troca de destino exige motivo, destino diferente, existente na LDP e diferente da origem", () => {
  const current = item({});
  const ldp = new Set(["DEST-001", "DEST-002"]);
  const draft = { operation: "EDIT_DE_PARA" as const, destinationDocumentCode: "DEST-002", reason: "x".repeat(MIN_REASON_LENGTH) };
  assert.equal(validateDraft(draft, current, ldp), null);
  assert.match(validateDraft({ ...draft, reason: "   curto  " }, current, ldp)!, /Justificativa/);
  assert.match(validateDraft({ ...draft, destinationDocumentCode: "" }, current, ldp)!, /destino/);
  assert.match(validateDraft({ ...draft, destinationDocumentCode: "dest-001" }, current, ldp)!, /destino atual/);
  assert.match(validateDraft({ ...draft, destinationDocumentCode: "ORIG-001" }, current, ldp)!, /própria origem/);
  assert.match(validateDraft({ ...draft, destinationDocumentCode: "NAO-EXISTE" }, current, ldp)!, /LDP/);
  assert.equal(validateDraft({ operation: "RESET_DE_PARA", destinationDocumentCode: "", reason: "motivo suficiente" }, current), null);
});

const preview = (over: Partial<RoutingPreview> = {}): RoutingPreview => ({
  canConfirm: true, blockingReason: null,
  before: { sourceHours: 10, sourceCost: 1000, destinations: [{ documentCode: "DEST-001", title: null, hours: 10, cost: 1000 }] },
  after: { sourceHours: 10, sourceCost: 1000, destinations: [] },
  totalHoursDelta: 0, totalCostDelta: 0, version: "v2", ...over,
});

test("reset sem perda de horas: resíduo volta para a origem; multiplicação/alteração da origem bloqueia", () => {
  const check = checkPreview(preview());
  assert.equal(check.beforeResidualHours, 0);
  assert.equal(check.afterResidualHours, 10, "HH ficam na origem, sem destino documental — nada some");
  assert.equal(check.localBlock, null);
  assert.ok(checkPreview(preview({ after: { sourceHours: 10, sourceCost: 1000, destinations: [{ documentCode: "A", title: null, hours: 12, cost: 1000 }] } })).localBlock);
  assert.ok(checkPreview(preview({ after: { sourceHours: 9, sourceCost: 1000, destinations: [] } })).localBlock);
});

test("confirmação: só com preview permitido, versão, reconhecimento e o mesmo pedido simulado (stale)", () => {
  const fp = draftFingerprint(req());
  assert.equal(canApply(preview(), fp, fp, true), true);
  assert.equal(canApply(preview(), fp, fp, false), false, "sem reconhecimento");
  assert.equal(canApply(preview({ canConfirm: false, blockingReason: "bloqueado" }), fp, fp, true), false, "bloqueio do servidor");
  assert.equal(canApply(preview({ version: null }), fp, fp, true), false, "sem token de versão");
  assert.equal(canApply(preview(), fp, draftFingerprint(req({ reason: "outro motivo qualquer" })), true), false, "motivo mudou após simular");
  assert.equal(canApply(preview(), fp, draftFingerprint(req({ version: "v9" })), true), false, "dados recarregados após simular");
  assert.equal(canApply(null, null, fp, true), false);
});

test("alertas do servidor: texto exibido como veio, tom por palavra-chave", () => {
  assert.equal(classifyWarning("Destino cancelado na LDP"), "DESTINO_CANCELADO");
  assert.equal(classifyWarning("Divergência de disciplina"), "DIVERGENCIA_DISCIPLINA");
  assert.equal(classifyWarning("HH alteradas desde a redistribuição"), "HH_ALTERADAS");
  assert.equal(classifyWarning("Origem sem LDP"), "SEM_LDP");
  assert.equal(classifyWarning("Documento de origem ainda ativo na LDP"), "ORIGEM_ATIVA_NA_LDP");
  const [origin] = groupByOrigin({ items: [item({ warning: "Destino cancelado; Divergência de disciplina" })], history: [] });
  assert.deepEqual(origin.alerts.map((a) => a.text), ["Destino cancelado", "Divergência de disciplina"]);
});

test("filtros por código (origem/destino), profissional, disciplina e status", () => {
  const origins = groupByOrigin({
    items: [
      item({}),
      item({ sourceDocumentCode: "ORIG-002", discipline: "TUBULAÇÃO", professionals: ["Pessoa B"], active: false, destinationDocumentCode: "DEST-XYZ", destinations: [] }),
    ],
    history: [{ sourceDocumentCode: "orig-002", method: "DE_PARA", details: null, by: null, atUtc: null, reason: null }],
  });
  assert.deepEqual(filterOrigins(origins, { ...EMPTY_ROUTING_FILTERS, search: "dest-xyz" }).map((o) => o.key), ["ORIG-002"]);
  assert.deepEqual(filterOrigins(origins, { ...EMPTY_ROUTING_FILTERS, professional: "Pessoa A" }).map((o) => o.key), ["ORIG-001"]);
  assert.deepEqual(filterOrigins(origins, { ...EMPTY_ROUTING_FILTERS, discipline: "TUBULAÇÃO" }).map((o) => o.key), ["ORIG-002"]);
  assert.deepEqual(filterOrigins(origins, { ...EMPTY_ROUTING_FILTERS, status: "INATIVO" }).map((o) => o.key), ["ORIG-002"]);
  assert.deepEqual(filterOrigins(origins, { ...EMPTY_ROUTING_FILTERS, type: "HISTORICO" }).map((o) => o.key), ["ORIG-002"]);
  assert.equal(origins.find((o) => o.key === "ORIG-002")!.history.length, 1, "histórico casa por código normalizado");
});

test("normalização: método desconhecido vira SEM_DIRECIONAMENTO, listas ausentes viram vazias", () => {
  const result = normalizeRoutingControl({ items: [{ sourceDocumentCode: "A", method: "OUTRO" }] }, P);
  assert.equal(result.projectId, P);
  assert.equal(result.items[0].method, "SEM_DIRECIONAMENTO");
  assert.deepEqual(result.items[0].destinations, []);
  assert.deepEqual(result.history, []);
});

// ------------------------------------------------------------------ RENDERIZAÇÃO / SEGURANÇA ----

test("tabela somente leitura não renderiza Editar/Resetar/Reavaliar; com edição renderiza", () => {
  const origins = groupByOrigin({ items: [item({}), item({ sourceDocumentCode: "ORIG-009", method: "REDISTRIBUICAO", decisionId: "r" })], history: [] });
  const readOnly = renderToStaticMarkup(React.createElement(RoutingOriginsTable, { origins, canEdit: false, onAction: () => {} }));
  assert.doesNotMatch(readOnly, /Editar|Resetar|Reavaliar/);
  assert.match(readOnly, /De\/Para específico/);
  assert.match(readOnly, /Redistribuição por disciplina/);
  const editable = renderToStaticMarkup(React.createElement(RoutingOriginsTable, { origins, canEdit: true, onAction: () => {} }));
  assert.match(editable, /Editar/);
  assert.match(editable, /Resetar De\/Para/);
  assert.match(editable, /Reavaliar/);
});

test("nenhuma mutação ao abrir a tela/trocar filtros: apply só no handler do modal; sem lote; sem API inventada", () => {
  const section = fonte("src/features/document-quality/RoutingControlSection.tsx");
  const dialog = fonte("src/features/document-quality/RoutingControlDialog.tsx");
  const page = fonte("src/features/document-quality/InformationQualityAuditPage.tsx");
  const api = fonte("src/features/document-quality/routingControlApi.ts");
  assert.doesNotMatch(section, /applyRoutingOperation|previewRoutingOperation/, "a seção nunca chama preview/apply");
  assert.doesNotMatch(page, /applyRoutingOperation|previewRoutingOperation/);
  assert.equal((dialog.match(/applyRoutingOperation\(/g) ?? []).length, 1, "um único ponto de gravação");
  assert.match(dialog, /const apply = async/);
  assert.equal((dialog.match(/previewRoutingOperation\(/g) ?? []).length, 1);
  assert.match(dialog, /const runPreview = async/);
  const urls = [...api.matchAll(/["`](\/api\/[^"`?$]+)/g)].map((m) => m[1]);
  assert.deepEqual(new Set(urls), new Set(["/api/information-quality-audit/routing-control", "/api/production-audit/timesheet-outside-ldp"]));
  assert.match(api, /\$\{ROUTING_BASE\}\/preview/);
  assert.match(api, /\$\{ROUTING_BASE\}\/apply/);
  assert.doesNotMatch(api, /batch/i, "sem reset em lote");
  assert.match(page, /can\("quality\.audit\.edit"\)/);
  assert.match(page, /role="tablist"/);
});
