// Novo Motor de LDP (Template/Mapeamento/Espelho/Controle) — schema `ldp` proposto em
// Migrations/ConfiguracaoLdp/*.sql. Rodada 2: persistência REAL via api/ldp-config/* passou a existir
// no backend, mas só funciona depois que essas migrations forem aplicadas manualmente (ver
// AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md). `id` é sempre um uuid
// client-side (identidade local, estável mesmo sem salvar); `backendId` é o Id numérico real
// devolvido pelo servidor depois de um "Salvar no banco" com sucesso — undefined = ainda não salvo.

export type TipoDadoCampo = "TEXTO" | "NUMERO" | "DECIMAL" | "DATA" | "PERCENTUAL" | "BOOLEANO";

export const TIPOS_DADO_CAMPO: { value: TipoDadoCampo; label: string }[] = [
  { value: "TEXTO", label: "Texto" },
  { value: "NUMERO", label: "Número" },
  { value: "DECIMAL", label: "Decimal" },
  { value: "DATA", label: "Data" },
  { value: "PERCENTUAL", label: "Percentual" },
  { value: "BOOLEANO", label: "Booleano" },
];

export type OrigemCampo = "MANUAL" | "CRONOGRAMA" | "TIMESHEET" | "CUSTO" | "WEEKLY" | "SISTEMA";

export const ORIGENS_CAMPO: { value: OrigemCampo; label: string; conectado: boolean }[] = [
  { value: "MANUAL", label: "Manual", conectado: true },
  { value: "CRONOGRAMA", label: "Cronograma", conectado: false },
  { value: "TIMESHEET", label: "Timesheet (HH Real)", conectado: false },
  { value: "CUSTO", label: "Custo", conectado: false },
  { value: "WEEKLY", label: "Programação Semanal (Weekly)", conectado: false },
  { value: "SISTEMA", label: "Sistema (outro domínio)", conectado: false },
];

export interface TemplateCampo {
  id: string;
  backendId?: number;
  nomeExibido: string;
  chaveCampo: string;
  tipoDado: TipoDadoCampo;
  obrigatorio: boolean;
  ordem: number;
  origem: OrigemCampo;
  ativo: boolean;
}

export interface TemplateAba {
  id: string;
  backendId?: number;
  nomeAba: string;
  ordem: number;
  ativo: boolean;
  campos: TemplateCampo[];
}

export type OrigemCriacaoTemplate = "MANUAL" | "CLONE" | "IMPORT_EXCEL";

export interface LdpTemplate {
  id: string;
  backendId?: number;
  nomeTemplate: string;
  descricao: string;
  origemCriacao: OrigemCriacaoTemplate;
  ativo: boolean;
  abas: TemplateAba[];
}

export interface CronogramaMapeamentoCampo {
  id: string;
  templateCampoId: string;
  colunaCronogramaOrigem: string;
  ordem: number;
}

export interface CronogramaMapeamento {
  id: string;
  backendId?: number;
  templateId: string;
  nomeMapeamento: string;
  ativo: boolean;
  campos: CronogramaMapeamentoCampo[];
}

// Nomes internos mantidos como "Modelo" por continuidade de código — representam
// ldp.ExcelEspelho/…Aba/…Campo no backend ("LDP Espelho", página /configuracao-ldp/espelho).
export interface ExcelModeloCampo {
  id: string;
  templateCampoId: string;
  ordem: number;
}

export interface ExcelModeloAba {
  id: string;
  templateAbaId: string;
  nomeAbaFisica: string;
  linhaCabecalho: number;
  /** Primeira coluna de dados (1 = coluna A) — pula colunas decorativas do XLSX base sem notação A1. */
  colunaInicial: number;
  ordem: number;
  campos: ExcelModeloCampo[];
}

export interface ExcelModelo {
  id: string;
  backendId?: number;
  templateId: string;
  nomeModelo: string;
  // Nome do arquivo original (rótulo). arquivoToken é o identificador real devolvido pelo upload
  // real ao servidor (api/ldp-config/espelho/upload-base) — presente só depois do upload ter sido
  // feito nesta sessão (não persistido em backendId antes disso).
  arquivoNomeOriginal: string | null;
  arquivoToken: string | null;
  abas: ExcelModeloAba[];
}

// ldp.ExcelControle/…Aba/…Campo no backend ("LDP Controle", página /configuracao-ldp/controle).
export interface ExcelControleCampo {
  id: string;
  templateCampoId: string;
  nomeExibidoOverride: string | null;
  ordem: number;
}

export interface ExcelControleAba {
  id: string;
  templateAbaId: string;
  nomeAbaExibido: string | null;
  ordem: number;
  campos: ExcelControleCampo[];
}

export interface ExcelControleConfig {
  id: string;
  backendId?: number;
  templateId: string;
  nomeConfiguracao: string;
  abas: ExcelControleAba[];
}

// ---------------------------------------------------------------------------
// Estrutura do Projeto (Projeto Único x Projeto por Fase) — ldp.ProjetoConfiguracao/ProjetoFase/
// ProjetoFaseRegra no backend. Conceito INDEPENDENTE de dbo.Projetos.Tipo_LDP (legado) — nunca usar
// Tipo_LDP=2 como sinônimo de "por fase". Chave real: Project_ID_ACC (projeto de verdade, vindo do
// mesmo registry usado em Configuração/Dashboard), não um id local do configurador.
// ---------------------------------------------------------------------------

export type ModoEstrutura = "PROJETO_UNICO" | "PROJETO_POR_FASE";

export const MODOS_ESTRUTURA: { value: ModoEstrutura; label: string }[] = [
  { value: "PROJETO_UNICO", label: "Projeto Único" },
  { value: "PROJETO_POR_FASE", label: "Projeto por Fase" },
];

export type ModoGeracaoFase = "ARQUIVO_POR_FASE" | "ABA_POR_FASE" | "CONSOLIDADO";

export const MODOS_GERACAO_FASE: { value: ModoGeracaoFase; label: string; descricao: string }[] = [
  { value: "ARQUIVO_POR_FASE", label: "Um arquivo por fase", descricao: 'Ex.: "PROJETO - FASE I.xlsx", "PROJETO - FASE II.xlsx"... cada arquivo só com os registros daquela fase.' },
  { value: "ABA_POR_FASE", label: "Um arquivo único com uma aba por fase", descricao: 'Ex.: "PROJETO - LDP.xlsx" com abas "FASE I", "FASE II"...' },
  { value: "CONSOLIDADO", label: "Um arquivo único consolidado", descricao: 'Todos os registros juntos, com o campo de sistema "Fase do Projeto" identificando cada linha.' },
];

export type OperadorRegra = "IGUAL_A" | "CONTEM" | "COMECA_COM" | "TERMINA_COM";

export const OPERADORES_REGRA: { value: OperadorRegra; label: string }[] = [
  { value: "IGUAL_A", label: "Igual a" },
  { value: "CONTEM", label: "Contém" },
  { value: "COMECA_COM", label: "Começa com" },
  { value: "TERMINA_COM", label: "Termina com" },
];

export type OperadorLogico = "ALL" | "ANY";

export interface ProjetoFaseRegra {
  id: string;
  backendId?: number;
  campoOrigem: string;
  operador: OperadorRegra;
  valorComparacao: string;
  ordem: number;
  ativa: boolean;
}

export interface ProjetoFase {
  id: string;
  backendId?: number;
  codigo: string;
  nome: string;
  ordem: number;
  ativa: boolean;
  /** Como combinar múltiplas regras desta fase — ALL = todas precisam bater, ANY = qualquer uma. */
  operadorLogico: OperadorLogico;
  regras: ProjetoFaseRegra[];
  // Preparado para override futuro (item "OVERRIDE POR FASE") — nulo = herda do projeto. Nenhuma UI
  // usa isto ainda; existe só para não exigir remodelagem de banco quando for implementado.
  templateIdOverride?: string | null;
  cronogramaMapeamentoIdOverride?: string | null;
  excelEspelhoIdOverride?: string | null;
  excelControleIdOverride?: string | null;
}

export interface ProjetoConfiguracao {
  /** Project_ID_ACC real (vindo do registry de projetos) — não um id local do configurador. */
  projectIdAcc: string;
  nomeProjetoExibicao: string;
  modoEstrutura: ModoEstrutura;
  modoGeracaoFase: ModoGeracaoFase | null;
  templateId: string | null;
  cronogramaMapeamentoId: string | null;
  excelEspelhoId: string | null;
  excelControleId: string | null;
  fases: ProjetoFase[];
  /** true depois de um "Salvar no banco" com sucesso desta configuração. */
  salvoNoBanco: boolean;
}

// Campos de sistema (Origem = SISTEMA) — derivados pelo motor, nunca duplicados em dados
// operacionais. "Fase do Projeto" só faz sentido quando Modo de Geração = CONSOLIDADO.
export const CAMPOS_SISTEMA: { chave: string; label: string }[] = [
  { chave: "PROJETO", label: "Projeto" },
  { chave: "PROJECT_ID_ACC", label: "Project_ID_ACC" },
  { chave: "PROJECT_PHASE", label: "Fase do Projeto" },
];

export interface ResultadoTesteRegras {
  porFase: { faseId: string; codigo: string; nome: string; quantidade: number }[];
  semFase: Record<string, string>[];
  conflitos: { linha: Record<string, string>; fases: { faseId: string; codigo: string }[] }[];
}
