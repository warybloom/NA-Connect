// Carrega as fontes (somente leitura) das entidades usadas na página e calcula o DadosVisual de cada visual.
// Cache por projeto+entidade enquanto o módulo estiver aberto: trocar de página do BI não recarrega a mesma fonte.
import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError, getJson } from "@/lib/api";
import type { PaginaPbi, VisualPbi } from "../pbi/modelo";
import { colunasVazias, converterLinhas, ENTIDADES } from "./entidades";
import { CHAVE_SEMANA, chaveSegmentacao, dadosDoVisual, dataCorteSalva, ehSegmentacaoSemana, entidadeDoVisual, entidadesExigidas, opcoesSegmentacao,
  segmentacaoNaoReproduzida, semanasDSemana, type FonteCarregada, type Segmentacoes } from "./motor";
import type { DadosVisual } from "./tipos";

const cache = new Map<string, Promise<FonteCarregada>>();

type Resposta = { linhas: Record<string, unknown>[]; indisponivel?: string | null; versaoId?: number | null };

/**
 * 404 numa rota NOVA do BI isolado = backend ainda sem o controlador publicado → "indisponível" (usa o legado).
 * 502/503 NÃO caem no legado: a rota existe e a falha é mostrada (não mascarar erro da fonte isolada).
 */
async function lerFonte(url: string, permitir404: boolean): Promise<Resposta> {
  try { return await getJson<Resposta>(url); }
  catch (e) {
    if (permitir404 && e instanceof ApiError && e.status === 404)
      return { linhas: [], indisponivel: "endpoint do cronograma isolado do BI ainda não publicado no backend" };
    throw e;
  }
}

export function carregar(entidade: string, projectId: string): Promise<FonteCarregada> {
  const chave = `${projectId}|${entidade}`;
  const existente = cache.get(chave);
  if (existente) return existente;
  const def = ENTIDADES[entidade];
  const q = `?projectId=${encodeURIComponent(projectId)}`;
  const p = (async (): Promise<FonteCarregada> => {
    let versao = "";
    if (def.mesmaVersaoDe) {
      // Série e tarefas da MESMA versão: sem versão isolada nas tarefas (legado), a série não é misturada.
      const base = await carregar(def.mesmaVersaoDe, projectId);
      if (base.estado === "erro") return { estado: "erro", erro: base.erro };
      if (base.versaoId === undefined)
        return { estado: "ok", linhas: [], vazias: [], origem: def.endpoint,
          indisponivel: "série timephased só existe no cronograma isolado do BI (bi_chs001), ainda sem versão gravada — tarefas vindas do legado" };
      versao = `&versao=${base.versaoId}`;
    }
    let origem = def.endpoint;
    let r = await lerFonte(`${def.endpoint}${q}${versao}`, !!def.endpointAlternativo || !!def.mesmaVersaoDe);
    if (r.indisponivel && def.endpointAlternativo) {
      origem = def.endpointAlternativo;
      const motivo = r.indisponivel;
      r = await getJson<Resposta>(`${def.endpointAlternativo}${q}`);
      r = { ...r, indisponivel: r.indisponivel ?? undefined };
      console.info(`[BI CHS001] ${entidade}: fonte principal indisponível (${motivo}); usando ${origem}.`);
    }
    const linhas = converterLinhas(entidade, r.linhas ?? []);
    return { estado: "ok", linhas, vazias: colunasVazias(entidade, linhas), indisponivel: r.indisponivel ?? undefined,
      versaoId: origem === def.endpoint && typeof r.versaoId === "number" ? r.versaoId : undefined, origem };
  })().catch((e): FonteCarregada => {
    cache.delete(chave);
    const msg = e instanceof ApiError
      // 404 (rota inexistente) ou 502 (fallback HTML da SPA): backend ainda sem BiChs001Controller carregado.
      ? (e.status === 404 || e.status === 502 ? "Endpoint do BI indisponível — o backend precisa ser reiniciado para publicar /api/bi-chs001." : e.message)
      : "Não foi possível carregar os dados.";
    return { estado: "erro", erro: msg };
  });
  cache.set(chave, p);
  return p;
}

const SEM_DADOS: DadosVisual = { estado: "pendente", motivo: "" };
const ESTATICOS = new Set(["shape", "image", "textbox", "actionButton"]);


export function useDadosBi(pagina: PaginaPbi | undefined, projectId: string | undefined) {
  const [fontes, setFontes] = useState<Record<string, FonteCarregada>>({});
  const [segmentacoes, setSegmentacoes] = useState<Segmentacoes>({});

  const entidades = useMemo(() => {
    const s = new Set<string>();
    for (const v of pagina?.visuais ?? []) {
      if (ehSegmentacaoSemana(v) && !v.hidden) { s.add("WBX"); continue; }
      if (ESTATICOS.has(v.type) || v.hidden || segmentacaoNaoReproduzida(v)) continue;
      const e = entidadeDoVisual(v);
      if (e && ENTIDADES[e]) s.add(e);
      for (const x of entidadesExigidas(v)) s.add(x);
    }
    return [...s];
  }, [pagina]);

  useEffect(() => {
    setSegmentacoes({});
    if (!projectId || entidades.length === 0) { setFontes({}); return; }
    let vivo = true;
    setFontes(Object.fromEntries(entidades.map((e) => [e, { estado: "carregando" } as FonteCarregada])));
    for (const e of entidades) carregar(e, projectId).then((f) => { if (vivo) setFontes((atual) => ({ ...atual, [e]: f })); });
    return () => { vivo = false; };
  }, [entidades, projectId, pagina?.id]);

  const dadosDe = useCallback((v: VisualPbi): DadosVisual => {
    if (!pagina || ESTATICOS.has(v.type)) return SEM_DADOS;
    // dSemana: seleção do usuário → SELECTEDVALUE(dSemana[Data]); sem interação = data salva no PBIX (dataCorteSalva).
    const ctx = { pagina, fontes, segmentacoes, ...(CHAVE_SEMANA in segmentacoes ? { dataCorte: segmentacoes[CHAVE_SEMANA][0] as string ?? null } : {}) };
    if (ehSegmentacaoSemana(v)) {
      const semanas = semanasDSemana(fontes.WBX);
      if (semanas === "carregando") return { estado: "carregando" };
      if (semanas) {
        const atual = CHAVE_SEMANA in segmentacoes ? (segmentacoes[CHAVE_SEMANA][0] as string | undefined) : dataCorteSalva(pagina) ?? undefined;
        const rotulo = (d: string) => `${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}`;
        return {
          estado: "ok", tipo: "opcoes", selecionadas: atual ? [atual] : [],
          opcoes: semanas.map((d) => ({ chave: d, rotulo: rotulo(d) })),
          // Seleção única (SELECTEDVALUE): marcar outra semana substitui a anterior.
          alternar: (k) => setSegmentacoes((s) => ({ ...s, [CHAVE_SEMANA]: (s[CHAVE_SEMANA]?.[0] ?? atual) === k ? [] : [k] })),
          limpar: () => setSegmentacoes((s) => ({ ...s, [CHAVE_SEMANA]: [] })),
        };
      }
    }
    if (v.type === "slicer") {
      const naoReproduzida = segmentacaoNaoReproduzida(v);
      if (naoReproduzida) return { estado: "pendente", motivo: naoReproduzida };
      const ent = entidadeDoVisual(v);
      if (!ent || !ENTIDADES[ent]) return { estado: "pendente", motivo: `Segmentação sobre "${ent ?? "?"}" sem fonte mapeada.` };
      const f = fontes[ent];
      if (!f || f.estado === "carregando") return { estado: "carregando" };
      if (f.estado === "erro") return { estado: "erro", mensagem: f.erro ?? "" };
      const op = opcoesSegmentacao(v, ctx);
      if (!op) return { estado: "pendente", motivo: "Coluna da segmentação sem equivalente verificado." };
      const chave = chaveSegmentacao(op.entidade, op.coluna);
      const selecionadas = (segmentacoes[chave] ?? []).map((x) => String(x ?? ""));
      return {
        estado: "ok", tipo: "opcoes", selecionadas,
        opcoes: op.valores.map((x) => ({ chave: String(x ?? ""), rotulo: x === null || x === "" ? "(Blank)" : String(x) })),
        alternar: (k) => setSegmentacoes((s) => {
          const atual = (s[chave] ?? []).map((x) => String(x ?? ""));
          const prox = atual.includes(k) ? atual.filter((x) => x !== k) : [...atual, k];
          return { ...s, [chave]: prox.map((x) => (x === "" ? null : x)) };
        }),
        limpar: () => setSegmentacoes((s) => ({ ...s, [chave]: [] })),
      };
    }
    return dadosDoVisual(v, ctx);
  }, [pagina, fontes, segmentacoes]);

  return { dadosDe, fontes, entidades };
}
