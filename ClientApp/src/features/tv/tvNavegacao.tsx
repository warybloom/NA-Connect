// Navegação EXPLÍCITA do player de TV para telas que têm menu próprio (Gestão à Vista — Jamile). Dentro do player, o menu
// lateral não navega para /controle-producao: só salta para um slide JÁ incluído na apresentação. Fora do player
// (contexto ausente) as telas usam as rotas normais do Controle de Produção.
// Sem dependência de tvScreens.tsx (evita ciclo: tvScreens → ProductionPageContent → GestaoVistaJamilePage → aqui).
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { limitedMap } from "@/components/pmo/pmoData";
import { buscarEfetivas, usePermissions } from "@/features/permissions/PermissionProvider";
import { criarCan } from "@/features/permissions/permissionLogic";

export interface DestinoTv { indice: number; projectIds: string[] }

export interface NavegacaoTv {
  /** Slide da apresentação que exibe a tela (preferindo os mesmos projetos do slide atual); null = não incluída. */
  destino: (screenId: string) => DestinoTv | null;
  /** Vai para o slide (zera o tempo de rotação; pausa/ordem/projetos continuam os da apresentação). */
  irPara: (indice: number) => void;
}

const Contexto = createContext<NavegacaoTv | null>(null);

export const NavegacaoTvProvider = ({ valor, children }: { valor: NavegacaoTv; children: ReactNode }) =>
  <Contexto.Provider value={valor}>{children}</Contexto.Provider>;

/** null = fora do player de TV. */
export const useNavegacaoTv = () => useContext(Contexto);

interface SlideNavegavel { screenId: string; projectIds: readonly string[] }

const mesmoConjunto = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((x) => b.includes(x));

/**
 * Slide de destino para uma tela: o com EXATAMENTE os mesmos projetos do slide atual; senão o do mesmo projeto principal;
 * senão o primeiro da sequência com essa tela. null quando a tela não está na apresentação.
 */
export function destinoNaApresentacao(slides: readonly SlideNavegavel[], atual: number, screenId: string): DestinoTv | null {
  const candidatos = slides.map((s, indice) => ({ s, indice })).filter((x) => x.s.screenId === screenId);
  if (candidatos.length === 0) return null;
  const projetosAtuais = slides[atual]?.projectIds ?? [];
  const escolhido = candidatos.find((x) => mesmoConjunto(x.s.projectIds, projetosAtuais))
    ?? candidatos.find((x) => x.s.projectIds[0] === projetosAtuais[0]) ?? candidatos[0];
  return { indice: escolhido.indice, projectIds: [...escolhido.s.projectIds] };
}

/**
 * Permissão da tela em TODOS os projetos (QUALQUER uma das keys em cada projeto; mesma resolução do backend em
 * /api/permissions/me?projectId=). Sem enforcement ou master: liberado. negados = projetos sem permissão (ou sem resposta).
 */
export function usePermissaoProjetos(projectIds: readonly string[], chaves: readonly string[]): { loading: boolean; negados: string[] } {
  const { effective, loading: carregandoGlobal } = usePermissions();
  const aplica = !!effective && effective.enforced && !effective.isMasterAdmin;
  const chave = `${projectIds.join("|")}#${chaves.join("|")}`;
  const [resposta, setResposta] = useState<{ chave: string; negados: string[] } | null>(null);
  useEffect(() => {
    setResposta(null);
    if (!aplica || projectIds.length === 0) return;
    const abort = new AbortController();
    const negados: string[] = [];
    limitedMap([...projectIds], async (id) => {
      try {
        const eff = await buscarEfetivas(id, abort.signal);
        const doMesmoUsuario = eff.email?.toLowerCase() === effective?.email?.toLowerCase() && eff.projectIdAcc?.toLowerCase() === id.toLowerCase();
        if (!doMesmoUsuario || !criarCan(eff, "project")(chaves)) negados.push(id);
      } catch { if (!abort.signal.aborted) negados.push(id); }                   // sem resposta = sem permissão
    }, 4).then(() => { if (!abort.signal.aborted) setResposta({ chave, negados }); }).catch(() => undefined);
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chave, aplica]);
  if (carregandoGlobal) return { loading: true, negados: [] };
  if (!effective) return { loading: false, negados: [...projectIds] };             // permissões não validadas: nada é liberado
  if (!aplica) return { loading: false, negados: [] };
  if (projectIds.length === 0) return { loading: false, negados: [] };
  return resposta && resposta.chave === chave ? { loading: false, negados: resposta.negados } : { loading: true, negados: [] };
}

/**
 * Item de menu dentro do player: habilitado só se a tela está na apresentação E o usuário tem a permissão da tela em
 * TODOS os projetos daquele slide (ex.: 04. INDICADORES exige a key gerencial). Caso contrário, visível e sem função.
 */
export function ItemMenuTv({ navegacao, screenId, chaves, ativo, className, classeIndisponivel, children }: {
  navegacao: NavegacaoTv; screenId: string; chaves: readonly string[]; ativo: boolean;
  className?: string; classeIndisponivel: string; children: ReactNode;
}) {
  const destino = navegacao.destino(screenId);
  const perm = usePermissaoProjetos(destino?.projectIds ?? [], chaves);
  const habilitado = !!destino && !perm.loading && perm.negados.length === 0;
  if (!habilitado || !destino) {
    const motivo = !destino ? "Tela não incluída nesta apresentação" : perm.loading ? "Verificando permissão…"
      : `Sem permissão para esta tela em ${perm.negados.length} projeto(s) do slide`;
    return <span className={[className, classeIndisponivel].filter(Boolean).join(" ")} aria-disabled="true" title={motivo}
      data-tv-nav={screenId} data-tv-nav-estado="indisponivel">{children}</span>;
  }
  return (
    <button type="button" className={className} aria-current={ativo ? "page" : undefined} data-tv-nav={screenId} data-tv-nav-estado="habilitado"
      title={ativo ? "Tela em exibição" : "Ir para esta tela da apresentação"} onClick={() => { if (!ativo) navegacao.irPara(destino.indice); }}>
      {children}
    </button>
  );
}
