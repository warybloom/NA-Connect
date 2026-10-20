// Adapter único para cruzar a lista de projetos da ACC (retornada por
// /DataManagement/Data/{Hubs,Projetos}, já real, não mock) com o registro do N&A em dbo.Projetos
// (Is_Ativo/Tipo_LDP/Naming_Standard), exposto por AdminProjetosController (/api/admin/projetos).
// Único ponto de normalização do prefixo "b." — Dashboard/Projects/Files não devem reimplementar isso.
// Ver AUDITORIA_FORGESYNC_NA/15-FUNCIONALIZACAO-DASHBOARD-PROJETOS-CONFIGURACAO.md §3.

export interface ProjetoRegistrado {
  projetoId: number;
  nomeProjeto: string;
  projectIdAcc: string;
  isAtivo: boolean;
  tipoLdp: number;
  namingStandard: string | null;
  projectLanguage: string | null;
  fileIdLdpAcc: string | null;
}

export function normalizeAccId(id: string | null | undefined): string {
  if (!id) return '';
  return id.startsWith('b.') ? id : `b.${id}`;
}

let cache: Promise<Map<string, ProjetoRegistrado>> | null = null;

/**
 * Erro tipado para quem precisa DISTINGUIR "0 projetos reais" de "falha ao carregar" (ex.:
 * ControlPanel.tsx, que precisa mostrar um estado de erro real em vez de tabela vazia — ver
 * AUDITORIA_FORGESYNC_NA/15-*.md checkpoint 17). Dashboard/Projects podem seguir ignorando esta
 * distinção (fail-open: continuam mostrando a lista da ACC sem a decoração de Is_Ativo).
 */
export class ProjetosRegistradosError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ProjetosRegistradosError';
  }
}

/**
 * Busca o registro completo de dbo.Projetos uma única vez (cacheado em memória para a sessão da SPA) e
 * devolve um Map por Project_ID_ACC normalizado. REJEITA (ProjetosRegistradosError) se a chamada HTTP
 * falhar ou o corpo não puder ser interpretado como a lista esperada — nunca mais mascara erro como
 * "Map vazio" (checkpoint 15 fazia isso e escondia falhas reais atrás de "0 projetos"). Uma falha NÃO
 * fica em cache — a próxima chamada tenta de novo.
 */
export function getProjetosRegistrados(force = false): Promise<Map<string, ProjetoRegistrado>> {
  if (force) cache = null;
  if (!cache) {
    cache = fetch('/api/admin/projetos', { credentials: 'include' })
      .then(res => {
        console.log(`[PROJECTS_REGISTRY] status=${res.status}`);
        if (!res.ok) {
          throw new ProjetosRegistradosError(res.status, `GET /api/admin/projetos retornou HTTP ${res.status}`);
        }
        return res.json();
      })
      .then((rows: unknown) => {
        if (!Array.isArray(rows)) {
          throw new ProjetosRegistradosError(0, 'GET /api/admin/projetos não retornou uma lista (resposta inesperada — provável proxy/rota incorreta).');
        }
        console.log(`[PROJECTS_REGISTRY] rows=${rows.length}`);
        const map = new Map<string, ProjetoRegistrado>();
        for (const r of rows as any[]) {
          const key = normalizeAccId(r.project_ID_ACC ?? r.projectIdAcc);
          if (!key) continue;
          map.set(key, {
            projetoId: r.projetoId,
            nomeProjeto: r.nome_Projeto ?? r.nomeProjeto,
            projectIdAcc: key,
            isAtivo: Boolean(r.is_Ativo ?? r.isAtivo),
            tipoLdp: r.tipoLdp ?? 1,
            namingStandard: r.namingStandard ?? null,
            projectLanguage: r.projectLanguage ?? null,
            fileIdLdpAcc: r.fileIdLdpAcc ?? null,
          });
        }
        return map;
      })
      .catch(err => {
        console.error('[PROJECTS_REGISTRY] erro ao carregar registro N&A:', err);
        cache = null; // nunca cacheia falha
        throw err instanceof ProjetosRegistradosError ? err : new ProjetosRegistradosError(0, String(err));
      });
  }
  return cache;
}
