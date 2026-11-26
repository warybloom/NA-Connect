// 🎯 Adicione isso por volta da linha 45, logo após o 'interface TemplateGroup'
export interface Project {
  projetoId: number;
  nome_Projeto: string;
  project_ID_ACC: string;
  is_Ativo: boolean;

  // Opcionais (checkpoint Gestão de Projetos, 2026-09-18): NormaID/PacoteOCRID eram conceitos
  // ForgeSync sem tabela/vínculo real no N&A; nenhuma célula desta tela os lê mais. Mantidos
  // opcionais (não removidos do tipo) só porque `templates`/pacote de templates em outras abas usam
  // um campo de mesmo nome em objetos diferentes — não vale a pena desambiguar nesta tarefa.
  NormaID?: number | null;
  PacoteOCRID?: number | null;

  // Campos reais de dbo.Projetos (não o CRUD universal de Normas do ForgeSync — ver
  // AUDITORIA_FORGESYNC_NA/15-*.md). tipoLdp/namingStandard já são consumidos pelo Worker.
  tipoLdp?: number;
  namingStandard?: string | null;
  projectLanguage?: string | null;
  fileIdLdpAcc?: string | null;
}

export interface Norma {
  normaId: number;
  nome_Norma: string;
  descricao: string;
  mappings: Mapping[];
}

export interface Mapping {
  sigla: string;
  pasta_Destino: string;
  tipo_Documento: string;
}
