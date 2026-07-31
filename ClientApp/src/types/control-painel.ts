// 🎯 Adicione isso por volta da linha 45, logo após o 'interface TemplateGroup'
export interface Project {
  projetoId: number;
  nome_Projeto: string;
  project_ID_ACC: string;
  is_Ativo: boolean;
  
  // ✅ Alterado para o padrão unificado (sem underline)
  NormaID: number | null; 
  PacoteOCRID: number | null;
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
