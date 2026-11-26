// Cliente de /standards, /standards/quality e /line-weights (CONTRATO 23/10/2026, Fases 1 e 2). Prefixo /api/plant-integration.
// Nada é inventado: resposta fora do contrato vira erro, nunca "sucesso". Gravar só com projeto e com a leitura anterior OK.
import { plantRequest, projetoPath } from "./plantApi";
import {
  LineWeight, LineWeights, PlantStandards, StandardsQuality,
  normalizarPadroes, normalizarPesos, normalizarQualidade, paraEnvioPadroes,
} from "./plantStandardsLogic";
import { newId } from "./plantRulesApi";

const JSON_HDR = { "Content-Type": "application/json" };

export async function getStandards(projectIdAcc: string, signal?: AbortSignal): Promise<PlantStandards> {
  const r = await plantRequest<unknown>(projetoPath(projectIdAcc) + "/standards", { signal });
  if (!r || typeof r !== "object") throw new Error("Resposta inesperada de /standards.");
  return normalizarPadroes(r, newId);
}

export async function putStandards(
  projectIdAcc: string, s: Pick<PlantStandards, "catalogs" | "tagLineColumn">,
): Promise<PlantStandards> {
  if (!projectIdAcc) throw new Error("Selecione um projeto antes de salvar.");
  const r = await plantRequest<unknown>(projetoPath(projectIdAcc) + "/standards", {
    method: "PUT", headers: JSON_HDR, body: JSON.stringify(paraEnvioPadroes(s)),
  });
  return r && typeof r === "object" ? normalizarPadroes(r, newId) : { ...s, updatedAtUtc: null };
}

export async function getStandardsQuality(projectIdAcc: string, signal?: AbortSignal): Promise<StandardsQuality> {
  const r = await plantRequest<unknown>(projetoPath(projectIdAcc) + "/standards/quality", { signal });
  return normalizarQualidade(r);
}

export async function getLineWeights(projectIdAcc: string, signal?: AbortSignal): Promise<LineWeights> {
  const r = await plantRequest<unknown>(projetoPath(projectIdAcc) + "/line-weights", { signal });
  if (!r || typeof r !== "object") throw new Error("Resposta inesperada de /line-weights.");
  return normalizarPesos(r);
}

export async function putLineWeights(projectIdAcc: string, weights: LineWeight[]): Promise<LineWeights> {
  if (!projectIdAcc) throw new Error("Selecione um projeto antes de salvar.");
  const body = { weights: weights.map(w => ({ key: w.key.trim(), label: w.label.trim(), percent: w.percent })) };
  const r = await plantRequest<unknown>(projetoPath(projectIdAcc) + "/line-weights", {
    method: "PUT", headers: JSON_HDR, body: JSON.stringify(body),
  });
  return r && typeof r === "object" ? normalizarPesos(r) : { weights: body.weights, updatedAtUtc: null };
}
