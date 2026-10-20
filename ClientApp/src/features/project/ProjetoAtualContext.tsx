// PROJETO ATUAL — contexto ÚNICO de projeto do N&A Connect (30/09/2026).
// Evolução do mecanismo que já existia no Controle de Produção ("na.controleProducao.escopo" no sessionStorage):
// agora é global. A Home define o projeto ao entrar num card; as telas compatíveis abrem já com ele e, quando o
// usuário troca de projeto numa tela, o contexto é atualizado. Não há outro "projeto atual" concorrente.
// Persistência por aba do navegador (sessionStorage): conveniência de navegação — nunca regra de acesso (o backend
// valida o projeto em cada endpoint).
//
// MULTIPROJETO (30/09/2026): PrimaryProject (projetoAtual) + projetos de ANÁLISE (projetosAnalise). Telas
// OPERACIONAIS usam só o projetoAtual; telas ANALÍTICAS (Resumo, Análise Financeira...) usam projetosAnalise, que por
// padrão = [projetoAtual]. Trocar o projeto principal (Home ou seletor de uma tela) SEMPRE volta a análise para
// [novo principal] — uma seleção antiga nunca "vaza" para outro projeto. Regras puras em projetoAnalise.ts.
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { analiseAoTrocarPrincipal, normalizarAnalise } from "./projetoAnalise";

export interface ProjetoAtual {
  /** Hub ACC quando já conhecido; vazio significa projeto N&A selecionado sem conexão ACC resolvida. */
  hubId: string;
  /** Project_ID_ACC (b.xxx). */
  projectId: string;
  /** Nome exibido (opcional: telas que só conhecem o id podem completar depois). */
  nome?: string;
}

interface Estado { projetoAtual: ProjetoAtual | null; analise: ProjetoAtual[] }

const CHAVE = "na.projetoAtual";
const CHAVE_LEGADA = "na.controleProducao.escopo"; // { hub, projeto } — Controle de Produção (antes de 30/09/2026)

export function lerProjetoAtual(): ProjetoAtual | null {
  try {
    const atual = sessionStorage.getItem(CHAVE);
    if (atual) {
      const p = JSON.parse(atual) as ProjetoAtual;
      return p?.projectId ? { hubId: p.hubId ?? "", projectId: p.projectId, nome: p.nome } : null;
    }
    const legado = sessionStorage.getItem(CHAVE_LEGADA);
    if (legado) {
      const l = JSON.parse(legado) as { hub?: string; projeto?: string };
      return l?.hub && l?.projeto ? { hubId: l.hub, projectId: l.projeto } : null;
    }
  } catch { /* storage indisponível ou conteúdo inválido */ }
  return null;
}

const CHAVE_ANALISE = "na.projetosAnalise";

function lerAnalise(principal: ProjetoAtual | null): ProjetoAtual[] {
  try {
    const bruto = sessionStorage.getItem(CHAVE_ANALISE);
    return normalizarAnalise(principal, bruto ? (JSON.parse(bruto) as ProjetoAtual[]) : []);
  } catch { return normalizarAnalise(principal, []); }
}

function gravar(e: Estado) {
  try {
    if (e.projetoAtual) sessionStorage.setItem(CHAVE, JSON.stringify(e.projetoAtual)); else sessionStorage.removeItem(CHAVE);
    sessionStorage.setItem(CHAVE_ANALISE, JSON.stringify(e.analise));
    sessionStorage.removeItem(CHAVE_LEGADA);
  } catch { /* storage indisponível: o contexto segue em memória */ }
}

interface ProjetoAtualValue {
  projetoAtual: ProjetoAtual | null;
  /** Define (ou atualiza) o projeto atual. Mesmo projeto com nome novo só completa o nome. */
  definirProjeto: (p: ProjetoAtual) => void;
  limparProjeto: () => void;
  /** Projetos das telas ANALÍTICAS (padrão: [projetoAtual]). */
  projetosAnalise: ProjetoAtual[];
  definirProjetosAnalise: (lista: ProjetoAtual[]) => void;
}

const Ctx = createContext<ProjetoAtualValue | null>(null);

export function ProjetoAtualProvider({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<Estado>(() => {
    const p = lerProjetoAtual();
    return { projetoAtual: p, analise: lerAnalise(p) };
  });

  const definirProjeto = useCallback((p: ProjetoAtual) => {
    setEstado((e) => {
      const atual = e.projetoAtual;
      const hubId = p.hubId || (atual?.projectId === p.projectId ? atual.hubId : "");
      if (atual && atual.hubId === hubId && atual.projectId === p.projectId && (!p.nome || atual.nome === p.nome)) return e;
      const proximo = { hubId, projectId: p.projectId, nome: p.nome ?? (atual?.projectId === p.projectId ? atual.nome : undefined) };
      const novo = { projetoAtual: proximo, analise: analiseAoTrocarPrincipal(atual, proximo, e.analise) };
      gravar(novo);
      return novo;
    });
  }, []);

  const limparProjeto = useCallback(() => { const novo = { projetoAtual: null, analise: [] }; gravar(novo); setEstado(novo); }, []);

  const definirProjetosAnalise = useCallback((lista: ProjetoAtual[]) => {
    setEstado((e) => { const novo = { ...e, analise: normalizarAnalise(e.projetoAtual, lista) }; gravar(novo); return novo; });
  }, []);

  const value = useMemo(() => ({ projetoAtual: estado.projetoAtual, definirProjeto, limparProjeto, projetosAnalise: estado.analise, definirProjetosAnalise }),
    [estado, definirProjeto, limparProjeto, definirProjetosAnalise]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

/** Projeto atual. Fora do provider (ex.: testes/preview isolado) devolve um contexto inerte. */
export function useProjetoAtual(): ProjetoAtualValue {
  return useContext(Ctx) ?? { projetoAtual: null, definirProjeto: () => {}, limparProjeto: () => {}, projetosAnalise: [], definirProjetosAnalise: () => {} };
}
