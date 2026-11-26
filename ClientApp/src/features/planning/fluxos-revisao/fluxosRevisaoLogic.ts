// Painel de Planejamento → Fluxos de Revisão: validação, permissões e resolução local (pura). Contrato da API em
// features/production/analytics/doc03/reviewFlowsApi.ts (_agent_handoffs/DOC03_FRONTEND_CONTRACT.md).
import { normalizarEtapa } from "@/features/production/analytics/doc03/documentacaoFluxoLogic";
import type { DadosFluxo, DadosVinculo, ReviewFlowBinding } from "@/features/production/analytics/doc03/reviewFlowsApi";

export interface EtapaEditavel { id?: string; name: string; valores: string }
export const ACC_PROJECT_ID = /^b\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Etapas na ordem da lista (order = posição, 1..n); valores de Status_In_Flow separados por ";" ou quebra de linha. */
export function paraDadosFluxo(name: string, active: boolean, etapas: readonly EtapaEditavel[]): DadosFluxo {
  return { name: name.trim(), active, stages: etapas.map((e, i) => ({ ...(e.id ? { id: e.id } : {}), name: e.name.trim(), order: i + 1,
    statusInFlowValues: [...new Set(e.valores.split(/[;\n]/).map(v => v.trim()).filter(Boolean))] })) };
}

export function validarFluxo(d: DadosFluxo): string[] {
  const erros: string[] = [];
  if (!d.name) erros.push("Informe o nome do fluxo.");
  if (!d.stages.length) erros.push("O fluxo precisa de ao menos uma etapa.");
  if (d.stages.some(s => !s.name)) erros.push("Toda etapa precisa de nome.");
  const nomes = d.stages.map(s => normalizarEtapa(s.name)).filter(Boolean);
  if (new Set(nomes).size !== nomes.length) erros.push("Há etapas com o mesmo nome (acentos e maiúsculas não diferenciam).");
  // Um mesmo valor de Status_In_Flow em duas etapas tornaria a etapa atual ambígua.
  const valores = d.stages.flatMap(s => s.statusInFlowValues.map(normalizarEtapa));
  if (new Set(valores).size !== valores.length && new Set(nomes).size === nomes.length) erros.push("Um valor de Status_In_Flow aparece em mais de uma etapa.");
  return erros;
}

/** Corpo do vínculo conforme o escopo (campos do outro escopo vão nulos/vazios). */
export function paraDadosVinculo(d: DadosVinculo): DadosVinculo {
  return d.scope === "project"
    ? { flowId: d.flowId, scope: "project", projectId: d.projectId?.trim() || null, groupName: null, projectIds: [] }
    : { flowId: d.flowId, scope: "group", projectId: null, groupName: d.groupName?.trim() || null, projectIds: [...new Set(d.projectIds)] };
}

/**
 * Validação do vínculo contra os demais (idAtual = o próprio, na edição): um vínculo direto por projeto; nome de grupo único;
 * um projeto em no máximo um grupo (senão a resolução seria ambígua). Projeto com vínculo direto PODE estar num grupo (direto prevalece).
 */
export function validarVinculo(d: DadosVinculo, existentes: readonly ReviewFlowBinding[] = [], idAtual?: string,
  nomeProjeto: (id: string) => string = id => id): string[] {
  const erros: string[] = [];
  const outros = existentes.filter(b => b.id !== idAtual);
  if (!d.flowId) erros.push("Escolha o fluxo.");
  if (d.scope === "project") {
    if (!ACC_PROJECT_ID.test(d.projectId ?? "")) erros.push("Escolha o projeto.");
    else if (outros.some(b => b.scope === "project" && b.projectId === d.projectId)) erros.push(`${nomeProjeto(d.projectId!)} já tem vínculo direto; edite o vínculo existente.`);
  } else {
    const nome = normalizarEtapa(d.groupName);
    if (!nome) erros.push("Informe o nome do grupo.");
    else if (outros.some(b => b.scope === "group" && normalizarEtapa(b.groupName) === nome)) erros.push("Já existe um grupo com esse nome.");
    if (!d.projectIds.length) erros.push("Escolha ao menos um projeto para o grupo.");
    if (d.projectIds.some(id => !ACC_PROJECT_ID.test(id))) erros.push("Há Project ID do ACC inválido no grupo.");
    for (const id of d.projectIds) {
      const g = outros.find(b => b.scope === "group" && b.projectIds.includes(id));
      if (g) erros.push(`${nomeProjeto(id)} já pertence ao grupo "${g.groupName}".`);
    }
  }
  return erros;
}

export type SituacaoFluxo =
  | { tipo: "projeto"; binding: ReviewFlowBinding }
  | { tipo: "grupo"; binding: ReviewFlowBinding }
  | { tipo: "pendente" };
/** Mesma regra do backend: vínculo do projeto › vínculo de grupo › PENDENTE DE CONFIGURAÇÃO (nenhuma etapa inventada). */
export function situacaoDoProjeto(projectId: string, vinculos: readonly ReviewFlowBinding[]): SituacaoFluxo {
  const direto = vinculos.find(b => b.scope === "project" && b.projectId === projectId);
  if (direto) return { tipo: "projeto", binding: direto };
  const grupo = vinculos.find(b => b.scope === "group" && b.projectIds.includes(projectId));
  return grupo ? { tipo: "grupo", binding: grupo } : { tipo: "pendente" };
}

export function mover<T>(xs: readonly T[], i: number, delta: -1 | 1): T[] {
  const j = i + delta;
  if (j < 0 || j >= xs.length) return [...xs];
  const out = [...xs]; [out[i], out[j]] = [out[j], out[i]]; return out;
}
