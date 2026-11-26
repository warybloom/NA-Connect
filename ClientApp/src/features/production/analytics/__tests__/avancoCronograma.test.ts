import { test } from "node:test";
import assert from "node:assert/strict";
import { consolidarAvancoCronograma } from "../resumoLogic";
import type { FactsReport } from "@/components/pmo/pmoFacts";

const projeto = (projectId: string, num: number, den: number, medido = true): FactsReport => ({
  projectId,
  avanco: {
    // Medição por STATUS deve permanecer intacta: método diferente do cronograma.
    fisico: 96.34, fisicoNumerador: 75.48, fisicoDenominador: 78.35,
  },
  avancoCronograma: medido ? {
    fisico: Math.round((num / den) * 10000) / 100,
    fisicoNumerador: num, fisicoDenominador: den,
    tarefasConsideradas: 20, tarefasSemMedicao: 0,
    fonte: "Cronograma_Projetos", regraFisico: "Work × physical",
  } : undefined,
} as FactsReport);

test("Cronograma nao altera o percentual por status documental", () => {
  const r = projeto("BML", 98.3172, 100);
  assert.equal(consolidarAvancoCronograma([r]).fisico, 98.32);
  assert.equal(r.avanco?.fisico, 96.34);
});

test("Multiprojeto pondera por Work, nunca usa media de percentuais", () => {
  const a = projeto("A", 25, 30);
  const b = projeto("B", 65, 70);
  const v = consolidarAvancoCronograma([a,b]);
  assert.equal(v.fisico, 90);
  assert.equal(v.tarefas, 40);
});

test("Nao usa status como fallback nem exibe progresso parcial de multiplos projetos", () => {
  const a = projeto("A", 40, 50);
  const b = projeto("B", 0, 0, false);
  assert.equal(consolidarAvancoCronograma([a,b]).fisico, null);
  assert.equal(consolidarAvancoCronograma([]).fisico, null);
});
