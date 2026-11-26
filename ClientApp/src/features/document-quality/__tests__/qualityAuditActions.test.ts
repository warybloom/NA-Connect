import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import {
  confirmedRedistributionCodes, redistributionActionFor, type InformationQualityFinding,
} from "../informationQualityApi";
import {
  MODELO_INICIAL_AVANCO, STATUS_ALIASES, applyInitialModel, canonicalStatusKey, parseProgressSettings, rowsFromSettings, validateRows,
} from "../physicalProgressApi";

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};
const P = "b.c3e20d9d-779e-423c-9381-abec38cf11cb";
const f = (rule: string, documentCode: string | null, decisionRequired = true): Pick<InformationQualityFinding, "rule" | "documentCode" | "decisionRequired"> =>
  ({ rule, documentCode, decisionRequired });

// ------------------------------------------------------------------ PROBLEMA 1 ----

test("TIMESHEET_SEM_LDP com decisionRequired e não confirmado → ação disponível (Simular), nunca '—'", () => {
  // Histórico real do BML011.013: só CI5-CS-001 está CONFIRMADO.
  const confirmed = confirmedRedistributionCodes(
    [f("TIMESHEET_SEM_LDP", "BML011.013-940-ME7-DS-002")],
    [{ status: "CONFIRMADO", sourceDocumentCode: "BML011.013-000-CI5-CS-001" }],
  );
  assert.equal(confirmed.has("BML011.013-940-ME7-DS-002"), false, "documento pendente nunca é marcado como confirmado");
  assert.equal(redistributionActionFor(f("TIMESHEET_SEM_LDP", "BML011.013-940-ME7-DS-002"), confirmed), "SIMULAR");
  assert.equal(redistributionActionFor(f("TIMESHEET_SEM_LDP", " bml011.013-940-me7-ds-002 "), confirmed), "SIMULAR");
  assert.equal(redistributionActionFor(f("DOCUMENTO_RETIRADO_COM_HH", "X-1"), confirmed), "SIMULAR");
});

test("Redistribuição: confirmado → selo; sem número do documento → aviso explícito; outras regras → nenhuma", () => {
  const confirmed = confirmedRedistributionCodes([], [{ status: "CONFIRMADO", sourceDocumentCode: " doc-1 " }, { status: "PENDENTE", sourceDocumentCode: "DOC-2" }]);
  assert.equal(redistributionActionFor(f("TIMESHEET_SEM_LDP", "DOC-1"), confirmed), "REDISTRIBUIDO");
  assert.equal(redistributionActionFor(f("TIMESHEET_SEM_LDP", "DOC-2"), confirmed), "SIMULAR", "PENDENTE continua pendente");
  assert.equal(redistributionActionFor(f("REDISTRIBUICAO_CONFIRMADA", "DOC-3", false), confirmed), "REDISTRIBUIDO");
  assert.equal(redistributionActionFor(f("TIMESHEET_SEM_LDP", "  "), confirmed), "SEM_CODIGO");
  assert.equal(redistributionActionFor(f("NA_DOCUMENT_DUPLICADO", "DOC-4"), confirmed), "NENHUMA");
});

test("A página usa redistributionActionFor (Simular não some por bloqueio futuro do preview)", () => {
  const page = fonte("src/features/document-quality/InformationQualityAuditPage.tsx");
  assert.match(page, /const kind = redistributionActionFor\(finding, confirmedCodes\);\s*if \(kind === "SIMULAR"\)/);
  assert.match(page, /Sem número do documento/);
});

// ------------------------------------------------------------------ PROBLEMA 2 ----

test("GET progress-settings devolve LISTA (documentCount): a tela passa a ler status observados e configurados", () => {
  const settings = parseProgressSettings(P, [
    { documentStatus: "EM ELABORAÇÃO", documentCount: 12, configId: 4, percentual: 40, ativo: true, isConfigured: true },
    { documentStatus: "CLIENT REVIEW", documentCount: 3, configId: null, percentual: null, ativo: false, isConfigured: false },
  ] as never);
  assert.equal(settings.items.length, 2);
  assert.deepEqual(settings.items.map((i) => [i.documentStatus, i.count, i.isConfigured, i.percentual]),
    [["EM ELABORAÇÃO", 12, true, 40], ["CLIENT REVIEW", 3, false, null]]);
  const rows = rowsFromSettings(settings);
  assert.deepEqual(rows.map((r) => r.origin), ["salvo", "observado"]);
  // Formato antigo { projectId, items } continua aceito; resposta de outro projeto é descartada.
  assert.equal(parseProgressSettings(P, { projectId: P, items: [{ documentStatus: "APPROVED", count: 1, percentual: 100 }] }).items.length, 1);
  assert.throws(() => parseProgressSettings(P, { projectId: "b.outro", items: [] }));
});

test("Chave canônica igual à do backend: aliases explícitos, sem fuzzy", () => {
  for (const s of ["Elaborando", "EM ELABORAÇÃO", "  em   elaboracao ", "IN PROGRESS", "Work_In-Progress"]) assert.equal(canonicalStatusKey(s), "ELABORANDO", s);
  assert.equal(canonicalStatusKey("APPROVED"), "APROVADO");
  assert.equal(canonicalStatusKey("ELABORAÇÃO"), "ELABORACAO", "parecido não é alias");
  assert.equal(canonicalStatusKey("CLIENT REVIEW"), "CLIENT REVIEW");
  // Modelo inicial sempre coberto pela tabela de aliases.
  for (const e of MODELO_INICIAL_AVANCO) {
    assert.ok(STATUS_ALIASES[canonicalStatusKey(e.documentStatus)], e.documentStatus);
    for (const a of e.equivalentes) assert.equal(canonicalStatusKey(a), canonicalStatusKey(e.documentStatus), a);
  }
});

test("Tabela de aliases do frontend é espelho exato da do backend (DocumentStatusCanonical.cs)", () => {
  const cs = fonte("../Services/DocumentQuality/DocumentStatusCanonical.cs");
  const backend = Object.fromEntries([...cs.matchAll(/\["([A-Z ]+)"\] = \[([^\]]*)\]/g)]
    .map((m) => [m[1], [...m[2].matchAll(/"([^"]+)"/g)].map((x) => x[1])]));
  assert.deepEqual(backend, STATUS_ALIASES);
});

test("Salvar: o mesmo significado (Elaborando × EM ELABORAÇÃO) não pode ter dois percentuais; observado recebe a sugestão do modelo", () => {
  const settings = parseProgressSettings(P, [{ documentStatus: "EM ELABORAÇÃO", documentCount: 12, isConfigured: false, percentual: null }] as never);
  const { rows, filled } = applyInitialModel(rowsFromSettings(settings));
  assert.equal(filled, 1);
  const observado = rows.find((r) => r.documentStatus === "EM ELABORAÇÃO")!;
  assert.equal(observado.percentual, "40");
  assert.equal(rows.some((r) => r.documentStatus === "Elaborando"), false, "modelo não duplica um status já observado");
  const dup = validateRows([...rows, { documentStatus: "IN PROGRESS", count: 0, saved: null, percentual: "50", ativo: true, origin: "manual" }]);
  assert.equal(dup.rowErrors.get(rows.length), "Document Status repetido.", "IN PROGRESS = mesmo significado de EM ELABORAÇÃO");
});
