import { useEffect, useState } from 'react';
import { ApiError, getJson } from '@/lib/api';
import { limitedMap, type PmoProject } from './pmoData';
import type { ClassificacaoGestao, ResponsaveisReport, UniversoHhCusto } from './hhCustoControle';

export interface FalhaFonte { projectId: string; projectName: string; message: string }

/** Mensagem do 409 de CR/OS (não configurado/ambíguo) ou erro genérico — nunca vira "zero responsáveis". */
async function lerFalha(e: unknown) {
  if (e instanceof ApiError && e.status === 409) return 'CR/OS do projeto não configurado ou ambíguo: responsáveis indisponíveis.';
  return e instanceof Error ? e.message : 'Falha ao consultar.';
}

/**
 * Fontes complementares do Controle de HH / Custo, por projeto selecionado:
 *  • classificação Documentos de Gestão (só nos universos Engenharia/Gestão);
 *  • HH por responsável × documento × dia (painel RESPONSÁVEL e cross-filter).
 * Falha de um projeto é listada; nada é preenchido com zero.
 */
export function useHhCustoFontes(selection: PmoProject[], universo: UniversoHhCusto) {
  const [classificacoes, setClassificacoes] = useState<ClassificacaoGestao[]>([]);
  const [responsaveis, setResponsaveis] = useState<ResponsaveisReport[]>([]);
  const [falhasClassificacao, setFalhasClassificacao] = useState<FalhaFonte[]>([]);
  const [falhasResponsaveis, setFalhasResponsaveis] = useState<FalhaFonte[]>([]);
  const [carregandoClassificacao, setCarregandoClassificacao] = useState(universo !== 'original');
  const [carregandoResponsaveis, setCarregandoResponsaveis] = useState(true);
  const key = JSON.stringify(selection.map(p => [p.id, p.name]));

  useEffect(() => {
    const abort = new AbortController();
    const projetos: string[][] = JSON.parse(key);
    setResponsaveis([]); setFalhasResponsaveis([]); setCarregandoResponsaveis(true);
    const fr: FalhaFonte[] = [];
    limitedMap(projetos, async ([projectId, name]) => {
      try { return await getJson<ResponsaveisReport>(`/api/production/hh-custo/responsaveis?${new URLSearchParams({ projectId })}`, abort.signal); }
      catch (e) { if (abort.signal.aborted) throw e; fr.push({ projectId, projectName: name, message: await lerFalha(e) }); return null; }
    }).then(r => { if (!abort.signal.aborted) { setResponsaveis(r.filter((x): x is ResponsaveisReport => x !== null)); setFalhasResponsaveis(fr); } })
      .catch(() => undefined)
      .finally(() => { if (!abort.signal.aborted) setCarregandoResponsaveis(false); });

    if (universo !== 'original') {
      setClassificacoes([]); setFalhasClassificacao([]); setCarregandoClassificacao(true);
      const fc: FalhaFonte[] = [];
      limitedMap(projetos, async ([projectId, name]) => {
        try { return await getJson<ClassificacaoGestao>(`/api/planning/management-documents?${new URLSearchParams({ projectId })}`, abort.signal); }
        catch (e) { if (abort.signal.aborted) throw e; fc.push({ projectId, projectName: name, message: await lerFalha(e) }); return null; }
      }).then(r => { if (!abort.signal.aborted) { setClassificacoes(r.filter((x): x is ClassificacaoGestao => x !== null)); setFalhasClassificacao(fc); } })
        .catch(() => undefined)
        .finally(() => { if (!abort.signal.aborted) setCarregandoClassificacao(false); });
    } else setCarregandoClassificacao(false);
    return () => abort.abort();
  }, [key, universo]);

  return { classificacoes, responsaveis, falhasClassificacao, falhasResponsaveis, carregandoClassificacao, carregandoResponsaveis };
}
