
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  applyInitialModel,
  findingDocumentStatus,
  isCancelledStatus,
  isProgressSimulationPending,
  MODELO_INICIAL_AVANCO,
  parsePercent,
  progressBlockedReason,
  progressExcludedCodes,
  progressSettingsLink,
  PROGRESS_MISSING_RULE,
  rowsFromSettings,
  validateRows,
  type ProgressRow,
  type ProgressSettingItem,
} from "../physicalProgressApi";

const item = (documentStatus: string, patch: Partial<ProgressSettingItem> = {}): ProgressSettingItem => ({
  documentStatus, count: 3, isConfigured: false, percentual: null, ativo: true, updatedBy: null, updatedAtUtc: null, ...patch,
});

const rows = (...items: ProgressSettingItem[]) => rowsFromSettings({ projectId: "b.x", items, updatedBy: null, updatedAtUtc: null });

test("Modelo inicial nÃ£o sobrescreve linha salva nem valor digitado", () => {
  const base = rows(item("APPROVED", { isConfigured: true, percentual: 95 }), item("NOT STARTED"), item("Elaborando"));
  base[2] = { ...base[2], percentual: "35" };
  const { rows: next } = applyInitialModel(base);
  assert.equal(next[0].percentual, "95");
  assert.equal(next[0].origin, "salvo");
  assert.equal(next[1].percentual, "0");
  assert.equal(next[1].origin, "sugestao");
  assert.equal(next[2].percentual, "35");
});

test("Modelo inicial sÃ³ adiciona status sem equivalente na tela", () => {
  const { rows: next, filled, added } = applyInitialModel(rows(item("APPROVED"), item("nÃ£o iniciado")));
  assert.equal(filled, 2);
  assert.equal(added, MODELO_INICIAL_AVANCO.length - 2);
  assert.equal(next.filter((row) => row.documentStatus === "Aprovado").length, 0);
  assert.equal(next.filter((row) => row.documentStatus === "NÃ£o iniciado").length, 0);
  assert.ok(next.some((row) => row.documentStatus === "Coleta de dados" && row.percentual === "10" && row.origin === "modelo"));
});

test("Status observado sem percentual nÃ£o vai no PUT; salvo vazio bloqueia", () => {
  const base = rows(item("APPROVED", { isConfigured: true, percentual: 100 }), item("NOT STARTED"));
  let result = validateRows(base);
  assert.equal(result.rowErrors.size, 0);
  assert.deepEqual(result.items, [{ documentStatus: "APPROVED", percentual: 100, ativo: true }]);

  const cleared: ProgressRow[] = [{ ...base[0], percentual: "" }, base[1]];
  result = validateRows(cleared);
  assert.ok(result.rowErrors.has(0));
});

test("Percentual 0..100, vÃ­rgula decimal e duplicidade", () => {
  assert.equal(parsePercent("40,5"), 40.5);
  assert.equal(parsePercent(""), null);
  assert.ok(Number.isNaN(parsePercent("-1")));
  const base = rows(item("A"), item("B"), item("a "));
  const edited: ProgressRow[] = [{ ...base[0], percentual: "101" }, { ...base[1], percentual: "50" }, { ...base[2], percentual: "10" }];
  const result = validateRows(edited);
  assert.ok(result.rowErrors.get(0)?.includes("0 e 100"));
  assert.equal(result.rowErrors.get(2), "Document Status repetido.");
  assert.deepEqual(result.items, [{ documentStatus: "B", percentual: 50, ativo: true }]);
});

test("Simular avanÃ§o fÃ­sico: sÃ³ AVANCO_FISICO_AUSENTE_STATUS, sem cancelados/redistribuÃ­dos/validados", () => {
  const finding = (documentCode: string, status?: string) => ({
    rule: PROGRESS_MISSING_RULE,
    documentCode,
    details: status ? [{ label: "Document Status", value: status }] : [],
  });
  const excluded = progressExcludedCodes(
    [{ rule: "DOCUMENTO_RETIRADO_COM_HH", documentCode: "DOC-RET" }],
    ["DOC-RED"],
  );
  const validated = new Set(["DOC-OK"]);
  assert.equal(isProgressSimulationPending(finding("DOC-1", "Elaborando"), validated, excluded), true);
  assert.equal(isProgressSimulationPending(finding("DOC-1", "Cancelado"), validated, excluded), false);
  assert.equal(isProgressSimulationPending(finding("doc-ret"), validated, excluded), false);
  assert.equal(isProgressSimulationPending(finding("DOC-RED"), validated, excluded), false);
  assert.equal(isProgressSimulationPending(finding("DOC-OK"), validated, excluded), false);
  assert.equal(isProgressSimulationPending({ ...finding("DOC-2"), rule: "AVANCO_STATUS_SEM_CONFIGURACAO" }, validated, excluded), false);
  assert.equal(findingDocumentStatus(finding("DOC-1", " Aprovado ")), "Aprovado");
});

test("ConfirmaÃ§Ã£o exige percentual salvo e status nÃ£o cancelado", () => {
  const base = { projectId: "b.x", documentCode: "DOC-1", documentStatus: "Elaborando", originalPercent: 0, configuredPercent: 40, canConfirm: true, blockingReason: null };
  assert.equal(progressBlockedReason(base), null);
  assert.ok(progressBlockedReason({ ...base, configuredPercent: null }));
  assert.ok(progressBlockedReason({ ...base, documentStatus: "CANCELLED" }));
  assert.equal(progressBlockedReason({ ...base, canConfirm: false, blockingReason: "x" }), "x");
  assert.equal(isCancelledStatus("Retirada"), true);
});

test("Link de configuraÃ§Ã£o leva o status da ocorrÃªncia", () => {
  assert.equal(progressSettingsLink(null), "/qualidade/configuracao-avanco-fisico");
  assert.equal(progressSettingsLink("Em anÃ¡lise"), "/qualidade/configuracao-avanco-fisico?status=Em+an%C3%A1lise");
});

