// Chamadas reais a api/ldp-config/* — schema ldp.* proposto (Migrations/ConfiguracaoLdp/*.sql), AINDA
// NÃO EXECUTADO. Toda função aqui pode rejeitar com um erro real do SQL Server ("Invalid object name
// 'ldp.Template'") até a migration ser aplicada manualmente — nunca resolve silenciosamente como se
// tivesse funcionado. Quem chama decide como mostrar o erro (nunca finge sucesso).
import type {
  LdpTemplate,
  CronogramaMapeamento,
  CronogramaMapeamentoCampo,
  ExcelModeloAba,
  ExcelControleAba,
  ProjetoConfiguracao,
} from "@/types/ldp-config";

async function jsonOuErro<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(body?.message || `Erro HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

// ---------------------------------------------------------------------------
// Template
// ---------------------------------------------------------------------------
export async function salvarTemplateNoBanco(t: LdpTemplate): Promise<number> {
  let backendId = t.backendId;
  if (!backendId) {
    const res = await fetch("/api/ldp-config/templates", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ nomeTemplate: t.nomeTemplate, descricao: t.descricao, origemCriacao: t.origemCriacao }),
    });
    const criado = await jsonOuErro<{ templateId: number }>(res);
    backendId = criado.templateId;
  }

  const arvore = {
    templateId: backendId,
    nomeTemplate: t.nomeTemplate,
    descricao: t.descricao,
    origemCriacao: t.origemCriacao,
    ativo: t.ativo,
    abas: t.abas.map((a) => ({
      templateAbaId: 0,
      nomeAba: a.nomeAba,
      ordem: a.ordem,
      ativo: a.ativo,
      campos: a.campos.map((c) => ({
        templateCampoId: 0,
        nomeExibido: c.nomeExibido,
        chaveCampo: c.chaveCampo,
        tipoDado: c.tipoDado,
        obrigatorio: c.obrigatorio,
        ordem: c.ordem,
        origem: c.origem,
        ativo: c.ativo,
      })),
    })),
  };

  const res = await fetch(`/api/ldp-config/templates/${backendId}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(arvore),
  });
  await jsonOuErro(res);
  return backendId;
}

// ---------------------------------------------------------------------------
// Mapeamento de Cronograma
// ---------------------------------------------------------------------------
export async function salvarMapeamentoNoBanco(templateBackendId: number, m: CronogramaMapeamento, chaveCampoParaBackendId: (templateCampoId: string) => number | undefined): Promise<number> {
  let backendId = m.backendId;
  if (!backendId) {
    const res = await fetch(`/api/ldp-config/templates/${templateBackendId}/mapeamentos`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ nomeMapeamento: m.nomeMapeamento }),
    });
    const criado = await jsonOuErro<{ cronogramaMapeamentoId: number }>(res);
    backendId = criado.cronogramaMapeamentoId;
  }

  const campos: (Omit<CronogramaMapeamentoCampo, "templateCampoId"> & { templateCampoId: number | null })[] = m.campos.map((c) => ({
    ...c,
    templateCampoId: c.templateCampoId ? chaveCampoParaBackendId(c.templateCampoId) ?? null : null,
  }));

  const res = await fetch(`/api/ldp-config/mapeamentos/${backendId}/campos`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(campos),
  });
  await jsonOuErro(res);
  return backendId;
}

// ---------------------------------------------------------------------------
// MPP
// ---------------------------------------------------------------------------
export interface MppPreviewResultado {
  camposDisponiveis: string[];
  linhas: Record<string, string>[];
  avisos: string[];
}

export async function importarMpp(arquivo: File): Promise<MppPreviewResultado> {
  const formData = new FormData();
  formData.append("arquivo", arquivo);
  const res = await fetch("/api/ldp-config/cronograma/importar-mpp", { method: "POST", body: formData });
  return jsonOuErro<MppPreviewResultado>(res);
}

// ---------------------------------------------------------------------------
// Excel Espelho
// ---------------------------------------------------------------------------
export async function uploadBaseEspelho(arquivo: File): Promise<{ arquivoToken: string; nomeOriginal: string }> {
  const formData = new FormData();
  formData.append("arquivo", arquivo);
  const res = await fetch("/api/ldp-config/espelho/upload-base", { method: "POST", body: formData });
  return jsonOuErro(res);
}

export interface GerarTesteEspelhoAba {
  nomeAbaFisica: string;
  linhaCabecalho: number;
  colunaInicial: number;
  camposOrdem: string[];
  linhasDados: Record<string, string>[];
}

/** Devolve o Blob do .xlsx gerado — quem chama decide como oferecer o download. */
export async function gerarTesteEspelho(arquivoToken: string, abas: GerarTesteEspelhoAba[]): Promise<Blob> {
  const res = await fetch("/api/ldp-config/espelho/gerar-teste", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ arquivoToken, abas }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(body?.message || `Erro HTTP ${res.status}`);
  }
  return res.blob();
}

export function dispararDownload(blob: Blob, nomeArquivo: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nomeArquivo;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

// ---------------------------------------------------------------------------
// Estrutura do Projeto (Projeto Único x Projeto por Fase) — rodada 3
// ---------------------------------------------------------------------------
export async function salvarProjetoConfiguracaoNoBanco(config: ProjetoConfiguracao, backendIdDoTemplate: (templateId: string | null) => number | null): Promise<void> {
  const body = {
    projectIdAcc: config.projectIdAcc,
    modoEstrutura: config.modoEstrutura,
    modoGeracaoFase: config.modoGeracaoFase,
    templateId: backendIdDoTemplate(config.templateId),
    cronogramaMapeamentoId: null as number | null,
    excelEspelhoId: null as number | null,
    excelControleId: null as number | null,
    fases: config.fases.map((f) => ({
      projetoFaseId: 0,
      codigo: f.codigo,
      nome: f.nome,
      ordem: f.ordem,
      ativa: f.ativa,
      operadorLogico: f.operadorLogico,
      templateIdOverride: null as number | null,
      cronogramaMapeamentoIdOverride: null as number | null,
      excelEspelhoIdOverride: null as number | null,
      excelControleIdOverride: null as number | null,
      regras: f.regras.map((r) => ({
        projetoFaseRegraId: 0,
        campoOrigem: r.campoOrigem,
        operador: r.operador,
        valorComparacao: r.valorComparacao,
        ordem: r.ordem,
        ativa: r.ativa,
      })),
    })),
  };

  const res = await fetch(`/api/ldp-config/projeto/${encodeURIComponent(config.projectIdAcc)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  await jsonOuErro(res);
}
