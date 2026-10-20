import { TvPortalAssignments } from "@/features/tv/portal/TvPortalAssignments";
// Configurações → Configuração de Telas para TV (Gestão à Vista) — ponta a ponta (30/09/2026):
// abrir → carregar (GET /api/tv-config) → montar apresentações (nome, tempo por tela, sequência de telas × projeto)
// → salvar (PUT com revisão; 409 = outra sessão salvou) → reabrir carrega o salvo → "Iniciar apresentação" abre o
// player em tela cheia (/gestao-a-vista?apresentacao=<id>). Telas candidatas = catálogo da TV derivado do Controle de
// Produção (tvScreens.tsx ← productionRoutes.ts), filtrado por: implementada, aceita pelo servidor (telasPermitidas),
// autorizada para o usuário (telasAutorizadas do servidor + permissão local com as MESMAS keys da rota).
// Persistência em App_Data/tv-configuration.json (sem tabela nova).
import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowDown, ArrowUp, CheckCircle2, Loader2, MonitorPlay, Play, Plus, RefreshCw, Save, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  NOME_PROJETO_GESTAO_VISTA_TIPO2, TELAS_TV, TELAS_TV_DATA_OBRIGATORIA, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_PROJETO_UNICO, chavesTelaTv, getTelaTv,
  telasPorCategoria, type TelaTv,
} from "@/features/tv/tvScreens";
import SeletorProjetosTv from "@/features/tv/SeletorProjetosTv";
import { useProjetosPermitidosTv } from "@/features/tv/useProjetosPermitidosTv";
import type { AccItem } from "@/lib/api";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { useProductionSelectionInterna } from "@/features/production/ProductionProjectSelector";
import { TV_PROJECTS_ENDPOINT } from "@/features/production/productionSelectionLogic";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { carregarConfiguracaoTv, salvarConfiguracaoTv, TvSaveError } from "@/features/tv/tvApi";
import {
  TV_LIMITES, TV_SEGUNDOS_PADRAO, dataReferenciaValida, formatarDataReferencia, gerarIdApresentacao, mesmasApresentacoes, mover, projetosDoSlide,
  resumoProjetosSlide, resumoTv, slideComProjetos, tvAtiva, validarApresentacoes, type TvSlide, type TvSlideProjeto,
  type TvConfiguration, type TvPresentation,
} from "@/features/tv/tvLogic";

type Aviso = { tipo: "ok" | "erro"; texto: string } | null;

export default function TelasTvConfigPage() {
  const navigate = useNavigate();
  const { projetoAtual } = useProjetoAtual();
  const [config, setConfig] = useState<TvConfiguration | null>(null);
  const [telasPermitidas, setTelasPermitidas] = useState<string[]>([]);
  const [telasAutorizadas, setTelasAutorizadas] = useState<string[] | null>(null);
  const [rascunho, setRascunho] = useState<TvPresentation[]>([]);
  const [selecionada, setSelecionada] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erroCarga, setErroCarga] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [aviso, setAviso] = useState<Aviso>(null);

  const carregar = useCallback(async (signal?: AbortSignal) => {
    setCarregando(true); setErroCarga(null);
    try {
      const r = await carregarConfiguracaoTv(signal);
      if (signal?.aborted) return;
      setConfig(r.configuration);
      setTelasPermitidas(Array.isArray(r.telasPermitidas) ? r.telasPermitidas : []);
      setTelasAutorizadas(Array.isArray(r.telasAutorizadas) ? r.telasAutorizadas : null);
      setRascunho(r.configuration.presentations);
      setSelecionada((atual) => (r.configuration.presentations.some((a) => a.id === atual) ? atual : r.configuration.presentations[0]?.id ?? null));
    } catch (e) {
      if (!signal?.aborted) setErroCarga(e instanceof Error ? e.message : "Não foi possível carregar a configuração.");
    } finally {
      if (!signal?.aborted) setCarregando(false);
    }
  }, []);

  useEffect(() => {
    const abort = new AbortController();
    void carregar(abort.signal);
    return () => abort.abort();
  }, [carregar]);

  const alterado = config != null && !mesmasApresentacoes(rascunho, config.presentations);

  // Fechar/recarregar a aba com alterações não salvas pede confirmação do navegador.
  useEffect(() => {
    if (!alterado) return;
    const h = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", h);
    return () => window.removeEventListener("beforeunload", h);
  }, [alterado]);

  const { can } = usePermissions();
  // Estado de CADA tela do catálogo. Lista vazia/erro do servidor NUNCA libera telas: sem telasPermitidas nada é
  // selecionável (o erro de carga já é tratado à parte), e a permissão local é sempre exigida.
  const opcoesTela = useMemo(() => estadosDasTelas(TELAS_TV, telasPermitidas, telasAutorizadas, can), [telasPermitidas, telasAutorizadas, can]);
  const candidatas = useMemo(() => opcoesTela.filter((o) => o.estado === "ok").map((o) => o.tela), [opcoesTela]);
  const naoAceitasPeloServidor = opcoesTela.filter((o) => o.estado === "servidor").length;
  const erroValidacao = useMemo(() => validarApresentacoes(rascunho, telasPermitidas, TELAS_TV_PROJETO_UNICO, TELAS_TV_PROJETO_EXCLUSIVO, TELAS_TV_DATA_OBRIGATORIA),
    [rascunho, telasPermitidas]);
  const atual = rascunho.find((a) => a.id === selecionada) ?? null;

  const alterar = (id: string, f: (a: TvPresentation) => TvPresentation) => {
    setAviso(null);
    setRascunho((lista) => lista.map((a) => (a.id === id ? f(a) : a)));
  };

  const novaApresentacao = () => {
    const nome = `TV ${rascunho.length + 1}`;
    const id = gerarIdApresentacao(nome, rascunho.map((a) => a.id));
    setRascunho((l) => [...l, { id, name: nome, secondsPerScreen: TV_SEGUNDOS_PADRAO, slides: [], ativa: true }]);
    setSelecionada(id);
    setAviso(null);
  };

  const excluirApresentacao = (id: string) => {
    const resto = rascunho.filter((a) => a.id !== id);
    setRascunho(resto);
    setSelecionada(resto[0]?.id ?? null);
    setAviso(null);
  };

  const salvar = async () => {
    if (!config || erroValidacao) return;
    setSalvando(true); setAviso(null);
    try {
      const salva = await salvarConfiguracaoTv(config.revision, rascunho.map((a) => ({ ...a, name: a.name.trim() })));
      setConfig(salva);
      setRascunho(salva.presentations);
      setAviso({ tipo: "ok", texto: "Configuração salva." });
    } catch (e) {
      setAviso({ tipo: "erro", texto: e instanceof TvSaveError || e instanceof Error ? e.message : "Não foi possível salvar." });
    } finally {
      setSalvando(false);
    }
  };

  const podeIniciar = !!atual && !alterado && tvAtiva(atual) && config?.presentations.some((a) => a.id === atual.id);
  const rotuloTela = (id: string) => getTelaTv(id)?.label ?? id;

  return (
    <div className="space-y-4 animate-enter">
      <TvPortalAssignments />
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary/10 text-primary"><MonitorPlay className="h-5 w-5" /></span>
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Configuração de Telas para TV</h1>
          <p className="text-sm text-muted-foreground">
            Gestão à Vista: monte a sequência de telas do Controle de Produção, escolha o projeto de cada tela e o tempo de exibição.
          </p>
          {config?.savedAt && (
            <p className="mt-1 text-[11px] text-muted-foreground">
              Última gravação: {new Date(config.savedAt).toLocaleString("pt-BR")}{config.savedBy ? ` · ${config.savedBy}` : ""} · revisão {config.revision}
            </p>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" className="gap-2" disabled={carregando || salvando} onClick={() => void carregar()}>
            <RefreshCw className="h-4 w-4" />Recarregar
          </Button>
          <Button size="sm" className="gap-2 font-bold" disabled={!alterado || !!erroValidacao || salvando} onClick={() => void salvar()}>
            {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}Salvar
          </Button>
          <Button size="sm" variant="secondary" className="gap-2 font-bold uppercase tracking-wide" disabled={!podeIniciar}
            title={alterado ? "Salve as alterações antes de iniciar" : atual && !tvAtiva(atual) ? "TV inativa — ative para iniciar" : undefined}
            onClick={() => atual && navigate(`/gestao-a-vista?apresentacao=${encodeURIComponent(atual.id)}`)}>
            <Play className="h-4 w-4" />Iniciar apresentação
          </Button>
        </div>
      </section>

      {aviso && (
        <p role="status" className={cn("flex items-center gap-2 rounded-lg border px-4 py-2 text-sm",
          aviso.tipo === "ok" ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400" : "border-destructive/40 bg-destructive/10 text-destructive")}>
          {aviso.tipo === "ok" ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}{aviso.texto}
        </p>
      )}
      {alterado && erroValidacao && (
        <p className="flex items-center gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-2 text-sm text-amber-600 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4" />{erroValidacao}
        </p>
      )}

      {!carregando && !erroCarga && config && telasPermitidas.length === 0 && (
        <p role="alert" className="flex items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-2 text-sm text-destructive">
          <AlertTriangle className="h-4 w-4" />O servidor não informou nenhuma tela permitida para a TV — nenhuma tela pode ser adicionada. Verifique a versão do backend.
        </p>
      )}
      {!carregando && !erroCarga && telasPermitidas.length > 0 && naoAceitasPeloServidor > 0 && (
        <p role="status" className="flex items-center gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-2 text-sm text-amber-600 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4" />
          {naoAceitasPeloServidor} tela(s) do Controle de Produção ainda não são aceitas pelo servidor em execução (backend anterior a esta versão).
          Elas aparecem como indisponíveis até o backend ser atualizado.
        </p>
      )}

      {carregando ? (
        <div className="flex min-h-[30vh] items-center justify-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando configuração…</div>
      ) : erroCarga ? (
        <div className="flex min-h-[30vh] flex-col items-center justify-center gap-3 text-center">
          <AlertTriangle className="h-8 w-8 text-destructive" />
          <p className="max-w-md text-sm text-muted-foreground">{erroCarga}</p>
          <Button variant="outline" size="sm" onClick={() => void carregar()}>Tentar novamente</Button>
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[340px_1fr]">
          <aside className="space-y-2 rounded-xl border border-border bg-card/60 p-3">
            <div className="flex items-center justify-between px-1">
              <h2 className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">TVs configuradas ({rascunho.length})</h2>
              <Button size="sm" variant="ghost" className="h-7 gap-1 px-2 text-xs" disabled={rascunho.length >= TV_LIMITES.maxApresentacoes} onClick={novaApresentacao}>
                <Plus className="h-3.5 w-3.5" />Nova
              </Button>
            </div>
            {rascunho.length === 0 && <p className="px-1 py-4 text-xs text-muted-foreground">Nenhuma TV configurada. Clique em “Nova” para criar a primeira.</p>}
            {/* Listagem: Nome da Tela / projeto / conteúdo / status / última atualização — id técnico só como informação secundária. */}
            {rascunho.map((a) => {
              const r = resumoTv(a, rotuloTela);
              return (
                <button key={a.id} type="button" onClick={() => setSelecionada(a.id)} title={`Identificador técnico: ${a.id}`}
                  className={cn("flex w-full flex-col items-start gap-0.5 rounded-lg border px-3 py-2 text-left transition-colors",
                    a.id === selecionada ? "border-primary/50 bg-primary/10" : "border-border/40 hover:bg-accent/50")}>
                  <span className="flex w-full items-center gap-2">
                    <span className="min-w-0 flex-1 truncate text-sm font-black uppercase">{a.name || "(sem nome)"}</span>
                    <span className={cn("rounded-full px-2 py-0.5 text-[9px] font-black tracking-wider",
                      r.status === "ATIVA" ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400" : "bg-muted text-muted-foreground")}>{r.status}</span>
                  </span>
                  <span className="w-full truncate text-[11px] font-bold text-primary">{r.projeto}</span>
                  <span className="w-full truncate text-[11px] text-muted-foreground">{r.conteudo}</span>
                  <span className="text-[10px] text-muted-foreground/80">
                    {a.slides.length} tela(s) · {a.secondsPerScreen}s{a.atualizadoEm ? ` · atualizada ${new Date(a.atualizadoEm).toLocaleString("pt-BR")}` : ""}
                  </span>
                </button>
              );
            })}
          </aside>

          {atual ? (
            <EditorApresentacao
              key={atual.id}
              apresentacao={atual}
              candidatas={candidatas}
              opcoesTela={opcoesTela}
              projetoInicial={projetoAtual}
              onChange={(f) => alterar(atual.id, f)}
              onExcluir={() => excluirApresentacao(atual.id)}
            />
          ) : (
            <section className="flex min-h-[30vh] items-center justify-center rounded-xl border border-dashed border-border text-sm text-muted-foreground">
              Selecione ou crie uma TV.
            </section>
          )}
        </div>
      )}
    </div>
  );
}

type EstadoTela = "ok" | "sem-permissao" | "servidor" | "em-desenvolvimento";
interface OpcaoTela { tela: TelaTv; estado: EstadoTela }

/** Estado de cada tela no seletor (exportado para teste): só "ok" pode ser adicionada. Ids repetidos aparecem uma vez. */
export function estadosDasTelas(telas: readonly TelaTv[], telasPermitidas: readonly string[], telasAutorizadas: readonly string[] | null,
  can: (keys: readonly string[]) => boolean): OpcaoTela[] {
  const vistos = new Set<string>();
  return telas.filter((t) => !vistos.has(t.id) && !!vistos.add(t.id)).map((tela): OpcaoTela => ({
    tela,
    estado: !tela.tvSelectable ? "em-desenvolvimento"
      : !telasPermitidas.includes(tela.id) ? "servidor"
      : !can(chavesTelaTv(tela.id)) || (telasAutorizadas !== null && !telasAutorizadas.includes(tela.id)) ? "sem-permissao"
      : "ok",
  }));
}

const SUFIXO_ESTADO: Record<EstadoTela, string> = {
  ok: "", "sem-permissao": " — sem permissão", servidor: " — indisponível (backend desatualizado)", "em-desenvolvimento": " — em desenvolvimento (indisponível)",
};

/**
 * Telas que "Adicionar todas" inclui: as sem dados financeiros, não exclusivas de um projeto e sem data por item (financeiras,
 * Gestão à Vista Tipo 2 e Programação Diária só entram adicionadas uma a uma).
 */
export const telasEmLote = <T extends Pick<TelaTv, "containsCostData" | "projetoExclusivo"> & Partial<Pick<TelaTv, "exigeDataReferencia">>>(telas: readonly T[]): T[] =>
  telas.filter((t) => !t.containsCostData && !t.projetoExclusivo && !t.exigeDataReferencia);

/** Projetos selecionados que a tela NÃO aceita (tela exclusiva de um projeto — Gestão à Vista Tipo 2 = CHS001). */
export const projetosForaDaTela = (tela: Pick<TelaTv, "projetoExclusivo">, projetos: readonly TvSlideProjeto[]): TvSlideProjeto[] =>
  tela.projetoExclusivo ? projetos.filter((p) => p.projectId !== tela.projetoExclusivo) : [];

/** Mensagem do bloqueio ao adicionar/trocar projeto de uma tela exclusiva. */
export const avisoProjetoExclusivo = (tela: Pick<TelaTv, "label">, fora: readonly TvSlideProjeto[]) =>
  `"${tela.label}" é exclusiva do projeto ${NOME_PROJETO_GESTAO_VISTA_TIPO2}: selecione somente a CHS001 `
  + `(não permitido: ${fora.map((p) => p.projectName ?? p.projectId).join(", ")}).`;

/** Mesmo item = mesma tela, mesmos projetos e mesma data (Programação Diária repetida com outra data/projeto é outro item). */
const mesmoItem = (a: TvSlide, b: TvSlide) =>
  a.screenId === b.screenId && projetosDoSlide(a).map((p) => p.projectId).join("+") === projetosDoSlide(b).map((p) => p.projectId).join("+")
  && (a.dataReferencia ?? null) === (b.dataReferencia ?? null);

/** "consolidado" = 1 item com todos os projetos; "por-projeto" = 1 item para cada projeto. */
export type ModoAdicao = "consolidado" | "por-projeto";

/** Telas em que o editor oferece os dois modos (Gestão à Vista); as demais telas multiprojeto sempre consolidam. */
export const ofereceModos = (tela: Pick<TelaTv, "multiProjeto" | "categoria">) => tela.multiProjeto && tela.categoria === "Gestão à Vista";

/**
 * Itens a adicionar para uma tela com os projetos selecionados. Tela multiprojeto: consolidado (padrão) = 1 item com
 * todos; por projeto = 1 item para cada. Tela de um projeto só: sempre 1 item POR projeto (nunca combina resultados).
 * Itens idênticos aos já existentes são ignorados. Tela com data (Programação Diária): exige dataReferencia válida — cada item
 * leva a data; sem data nada é adicionado (nunca "hoje" implícito).
 */
export function itensParaAdicionar(tela: Pick<TelaTv, "id" | "multiProjeto"> & Partial<Pick<TelaTv, "exigeDataReferencia">>, projetos: readonly TvSlideProjeto[],
  existentes: readonly TvSlide[], modo: ModoAdicao = "consolidado", dataReferencia: string | null = null): TvSlide[] {
  if (projetos.length === 0) return [];
  if (tela.exigeDataReferencia && !dataReferenciaValida(dataReferencia)) return [];
  const data = tela.exigeDataReferencia ? dataReferencia : null;
  const novos = tela.multiProjeto && modo === "consolidado" ? [slideComProjetos(tela.id, [...projetos], data)] : projetos.map((p) => slideComProjetos(tela.id, [p], data));
  const out: TvSlide[] = [];
  for (const n of novos) if (![...existentes, ...out].some((e) => mesmoItem(e, n))) out.push(n);
  return out;
}

interface EditorProps {
  apresentacao: TvPresentation;
  candidatas: readonly TelaTv[];
  opcoesTela: readonly OpcaoTela[];
  projetoInicial: { hubId: string; projectId: string } | null;
  onChange: (f: (a: TvPresentation) => TvPresentation) => void;
  onExcluir: () => void;
}

function EditorApresentacao({ apresentacao: a, candidatas, opcoesTela, projetoInicial, onChange, onExcluir }: EditorProps) {
  // Fluxo: PROJETOS → TELA → ADICIONAR. A lista de projetos vem da conta ACC do projeto atual (mesma fonte do seletor
  // do Controle de Produção), filtrada para ATIVOS + permitidos. A seleção fica mantida entre adições.
  // Projetos ELEGÍVEIS da TV (já sincronizados; fonte administrativa interna): sem Autodesk e sem depender dos
  // UserProjects de quem configura. hubId só é repassado se já existir.
  const sel = useProductionSelectionInterna({ hubId: projetoInicial?.hubId, projectId: projetoInicial?.projectId }, TV_PROJECTS_ENDPOINT);
  const chavesTodas = useMemo(() => [...new Set(candidatas.flatMap((t) => chavesTelaTv(t.id)))], [candidatas]);
  const { opcoes, verificando, podeTela } = useProjetosPermitidosTv(sel.projects, chavesTodas);
  const [selecionados, setSelecionados] = useState<string[]>([]);
  const [tela, setTela] = useState<string>(candidatas[0]?.id ?? "");
  // Lista de candidatas mudou (ex.: permissões carregadas depois) e a escolhida deixou de valer → primeira válida.
  useEffect(() => { if (!candidatas.some((t) => t.id === tela)) setTela(candidatas[0]?.id ?? ""); }, [candidatas, tela]);
  const telaDef = candidatas.find((t) => t.id === tela);
  const [aviso, setAviso] = useState<string | null>(null);
  // Programação Diária: data de referência do(s) novo(s) item(ns). Começa VAZIA — o usuário escolhe; nunca "hoje" implícito.
  const [dataNova, setDataNova] = useState("");
  const faltaData = !!telaDef?.exigeDataReferencia && !dataReferenciaValida(dataNova);
  // Projeto atual (Home) já vem marcado na primeira vez, se estiver entre os permitidos.
  const [iniciado, setIniciado] = useState(false);
  useEffect(() => {
    if (iniciado || verificando || opcoes.length === 0) return;
    setIniciado(true);
    if (projetoInicial && opcoes.some((p) => p.id === projetoInicial.projectId)) setSelecionados([projetoInicial.projectId]);
  }, [iniciado, verificando, opcoes, projetoInicial]);

  const projetosSelecionados: TvSlideProjeto[] = selecionados
    .map((id) => opcoes.find((p) => p.id === id)).filter((p): p is AccItem => !!p)
    .map((p) => ({ hubId: sel.hubId, projectId: p.id, projectName: p.attributes.name }));
  const semPermissao = (screenId: string) => projetosSelecionados.filter((p) => !podeTela(p.projectId, chavesTelaTv(screenId)));

  const adicionar = (modo: ModoAdicao = "consolidado") => {
    if (!telaDef || projetosSelecionados.length === 0) return;
    // Tela exclusiva de um projeto (Gestão à Vista Tipo 2 = CHS001): qualquer outro projeto bloqueia a adição inteira.
    const fora = projetosForaDaTela(telaDef, projetosSelecionados);
    if (fora.length > 0) { setAviso(avisoProjetoExclusivo(telaDef, fora)); return; }
    // Permissão conferida em TODOS os projetos selecionados (consolidado ou por projeto).
    const negados = semPermissao(telaDef.id);
    if (negados.length > 0) { setAviso(`Sem permissão para "${telaDef.label}" em: ${negados.map((p) => p.projectName ?? p.projectId).join(", ")}.`); return; }
    if (faltaData) { setAviso(`Informe a data de referência (AAAA-MM-DD) de "${telaDef.label}".`); return; }
    const novos = itensParaAdicionar(telaDef, projetosSelecionados, a.slides, modo, telaDef.exigeDataReferencia ? dataNova : null);
    const cabem = novos.slice(0, Math.max(0, TV_LIMITES.maxTelas - a.slides.length));
    setAviso(novos.length === 0 ? `"${telaDef.label}" já está na sequência com esses projetos${telaDef.exigeDataReferencia ? " e essa data" : ""}.`
      : cabem.length < novos.length ? `Limite de ${TV_LIMITES.maxTelas} telas: ${novos.length - cabem.length} item(ns) não adicionado(s).` : null);
    if (cabem.length > 0) onChange((x) => ({ ...x, slides: [...x.slides, ...cabem] }));
  };
  // Todas as telas COMUNS autorizadas com os projetos selecionados (só as que o usuário pode ver em todos eles). Telas com
  // dados financeiros (ex.: 04. INDICADORES) nunca entram em lote: só quando adicionadas explicitamente. Telas de um
  // projeto só recebem um item por projeto; itens já existentes não são duplicados.
  const emLote = telasEmLote(candidatas);
  const adicionarTodas = () => {
    if (projetosSelecionados.length === 0) return;
    const permitidas = emLote.filter((t) => semPermissao(t.id).length === 0);
    const exclusivas = candidatas.filter((t) => t.projetoExclusivo).length;
    const comData = candidatas.filter((t) => t.exigeDataReferencia && !t.projetoExclusivo).length;
    const financeiras = candidatas.length - emLote.length - exclusivas - comData;
    const novos: TvSlide[] = [];
    for (const t of permitidas) novos.push(...itensParaAdicionar(t, projetosSelecionados, [...a.slides, ...novos]));
    const cabem = novos.slice(0, Math.max(0, TV_LIMITES.maxTelas - a.slides.length));
    const avisos = [
      permitidas.length < emLote.length ? `${emLote.length - permitidas.length} tela(s) ignorada(s) por falta de permissão em algum projeto selecionado.` : "",
      financeiras > 0 ? `${financeiras} tela(s) com dados financeiros não incluída(s) — adicione-as individualmente.` : "",
      exclusivas > 0 ? `${exclusivas} tela(s) Gestão à Vista Tipo 2 (exclusivas da CHS001) não incluída(s) — adicione-as individualmente.` : "",
      comData > 0 ? `Programação Diária não incluída — adicione-a individualmente, com projeto e data.` : "",
      cabem.length < novos.length ? `Limite de ${TV_LIMITES.maxTelas} telas: ${novos.length - cabem.length} item(ns) não adicionado(s).` : "",
    ].filter(Boolean);
    setAviso(avisos.length ? avisos.join(" ") : null);
    onChange((x) => ({ ...x, slides: [...x.slides, ...cabem] }));
  };

  const campo = "h-9 rounded-lg border border-border bg-background px-3 text-sm text-foreground";
  const rotulo = "text-[10px] font-black uppercase tracking-widest text-muted-foreground";
  return (
    <section className="space-y-4 rounded-xl border border-border bg-card/60 p-5">
      <div className="flex flex-wrap items-end gap-4">
        <label className="flex min-w-[240px] flex-1 flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">
          Nome da Tela / TV
          <input className={campo} maxLength={TV_LIMITES.maxNome} value={a.name} placeholder="Ex.: TV SALA DE ENGENHARIA"
            onChange={(e) => onChange((x) => ({ ...x, name: e.target.value }))} />
          <span className="text-[10px] font-normal normal-case tracking-normal text-muted-foreground/80">Identificador técnico: {a.id}</span>
        </label>
        <label className="flex items-center gap-2 pb-6 text-xs font-bold uppercase tracking-wider text-muted-foreground">
          <input type="checkbox" className="h-4 w-4 accent-primary" checked={tvAtiva(a)} onChange={(e) => onChange((x) => ({ ...x, ativa: e.target.checked }))} />
          Ativa
        </label>
        <label className="flex w-40 flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">
          Segundos por tela
          <input type="number" className={campo} min={TV_LIMITES.minSegundos} max={TV_LIMITES.maxSegundos} value={a.secondsPerScreen}
            onChange={(e) => onChange((x) => ({ ...x, secondsPerScreen: Math.round(Number(e.target.value) || 0) }))} />
        </label>
        <Button variant="ghost" size="sm" className="gap-2 text-destructive hover:bg-destructive/10 hover:text-destructive" onClick={onExcluir}>
          <Trash2 className="h-4 w-4" />Excluir TV
        </Button>
      </div>

      <div className="space-y-3 rounded-lg border border-border bg-background/40 p-4">
        <h3 className={rotulo}>Adicionar tela</h3>
        <div className="space-y-1">
          <span className={rotulo}>Projetos</span>
          <SeletorProjetosTv opcoes={opcoes} selecionados={selecionados} onChange={setSelecionados} carregando={sel.loadingHubs || sel.loadingProjects || verificando} />
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1">
            <span className={rotulo}>Tela</span>
            {/* Por categoria (ordem do Controle de Produção). Indisponíveis aparecem desabilitadas, com o motivo. */}
            <select aria-label="Tela" className={cn(campo, "w-[22rem] max-w-full")} value={tela} onChange={(e) => setTela(e.target.value)}
              disabled={candidatas.length === 0}>
              {candidatas.length === 0 && <option value="">Nenhuma tela disponível para o seu perfil</option>}
              {telasPorCategoria(opcoesTela.map((o) => ({ ...o, categoria: o.tela.categoria }))).map((g) => (
                <optgroup key={g.categoria} label={g.categoria}>
                  {g.telas.map((o) => (
                    <option key={o.tela.id} value={o.tela.id} disabled={o.estado !== "ok"}>{o.tela.label}{SUFIXO_ESTADO[o.estado]}</option>
                  ))}
                </optgroup>
              ))}
            </select>
          </label>
          {telaDef?.exigeDataReferencia && (
            <label className="flex flex-col gap-1">
              <span className={rotulo}>Data de referência</span>
              <input type="date" aria-label="Data de referência" data-testid="tv-data-referencia-nova" required
                className={cn(campo, "w-44", faltaData && "border-destructive")} value={dataNova} onChange={(e) => setDataNova(e.target.value)} />
            </label>
          )}
          {telaDef && ofereceModos(telaDef) && projetosSelecionados.length > 1 ? (<>
            {/* Gestão à Vista com vários projetos: o usuário escolhe consolidar (preferencial) ou uma tela por projeto. */}
            <Button size="sm" className="gap-1" data-testid="tv-adicionar-consolidado" disabled={a.slides.length >= TV_LIMITES.maxTelas} onClick={() => adicionar("consolidado")}
              title="Consolidar em uma tela: um slide com todos os projetos selecionados">
              <Plus className="h-4 w-4" />Adicionar 1 tela consolidada ({projetosSelecionados.length} projetos)
            </Button>
            <Button size="sm" variant="secondary" className="gap-1" data-testid="tv-adicionar-por-projeto" disabled={a.slides.length >= TV_LIMITES.maxTelas} onClick={() => adicionar("por-projeto")}
              title="Adicionar uma tela por projeto">
              <Plus className="h-4 w-4" />Adicionar {projetosSelecionados.length} telas (1 por projeto)
            </Button>
          </>) : (
            <Button size="sm" className="gap-1" disabled={!telaDef || projetosSelecionados.length === 0 || faltaData || a.slides.length >= TV_LIMITES.maxTelas} onClick={() => adicionar()}
              title={faltaData ? "Informe a data de referência" : undefined}>
              <Plus className="h-4 w-4" />
              {telaDef && !telaDef.multiProjeto && projetosSelecionados.length > 1 ? `Adicionar ${projetosSelecionados.length} itens (1 por projeto)` : "Adicionar"}
            </Button>
          )}
          <Button size="sm" variant="outline" disabled={emLote.length === 0 || projetosSelecionados.length === 0 || a.slides.length >= TV_LIMITES.maxTelas} onClick={adicionarTodas}
            title="Telas com dados financeiros (custos, HH gerencial) não entram em lote — adicione-as individualmente">
            Adicionar todas as telas ({emLote.length})
          </Button>
        </div>
        {telaDef && (
          <p className="text-[11px] text-muted-foreground">
            {ofereceModos(telaDef)
              ? "Gestão à Vista: “consolidada” = um slide com todos os projetos selecionados (quantidades, HH e custos somados; percentuais calculados sobre os totais; disciplinas pela Norma de cada projeto); “1 por projeto” = um slide para cada projeto."
              : telaDef.multiProjeto
              ? "Esta tela consolida vários projetos: um item com todos os projetos selecionados."
              : "Esta tela exibe um projeto por vez: com vários projetos selecionados, entra um item para cada projeto (os resultados nunca são combinados)."}
            {telaDef.containsCostData && " Contém valores monetários/HH gerenciais: na TV, só aparece para usuários com a permissão de custo da tela."}
            {telaDef.projetoExclusivo && ` Gestão à Vista Tipo 2: reprodução do BI da CHS001 — só pode ser adicionada com o projeto ${NOME_PROJETO_GESTAO_VISTA_TIPO2}.`}
            {telaDef.exigeDataReferencia && " Programação Diária: documentos programados SOMENTE na data de referência (sem agregação da semana). Pode ser adicionada várias vezes, cada item com o seu projeto e a sua data."}
          </p>
        )}
        {aviso && <p className="text-xs text-amber-600 dark:text-amber-400">{aviso}</p>}
        {sel.error && <p className="text-xs text-destructive">{sel.error}</p>}
      </div>

      <div>
        <h3 className="mb-2 text-[10px] font-black uppercase tracking-widest text-muted-foreground">
          Sequência ({a.slides.length}/{TV_LIMITES.maxTelas}) · duração total {Math.round((a.slides.length * a.secondsPerScreen) / 60 * 10) / 10} min
        </h3>
        {a.slides.length === 0 ? (
          <p className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-muted-foreground">Nenhuma tela na sequência.</p>
        ) : (
          <ol className="space-y-1.5">
            {a.slides.map((s, i) => {
              const def = getTelaTv(s.screenId);
              const Icone = def?.icon ?? MonitorPlay;
              const escopo = resumoProjetosSlide(s);
              const foraDaTela = def ? projetosForaDaTela(def, projetosSelecionados) : [];
              return (
                <li key={`${i}-${s.screenId}-${projetosDoSlide(s).map((p) => p.projectId).join("+")}-${s.dataReferencia ?? ""}`}
                  className="flex items-center gap-3 rounded-lg border border-border bg-background/40 px-3 py-2">
                  <span className="w-6 text-right text-xs font-black text-muted-foreground">{i + 1}</span>
                  <Icone className="h-4 w-4 flex-shrink-0 text-primary" />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-bold uppercase">{def?.label ?? s.screenId}</span>
                    {projetosDoSlide(s).length > 1 ? (
                      // Item multiprojeto: identificado e com a lista de projetos visível.
                      <details className="text-[11px] text-muted-foreground" data-testid="tv-item-multiprojeto">
                        <summary className="cursor-pointer truncate" title={escopo.completo}>
                          <span className="mr-1 rounded bg-primary/15 px-1.5 py-0.5 text-[9px] font-black tracking-wider text-primary">MULTIPROJETO · {projetosDoSlide(s).length}</span>
                          {escopo.texto}
                        </summary>
                        <ul className="mt-1 max-h-40 list-disc overflow-auto pl-5">{projetosDoSlide(s).map((p) => <li key={p.projectId}>{p.projectName ?? p.projectId}</li>)}</ul>
                      </details>
                    ) : (
                      <span className="block truncate text-[11px] text-muted-foreground" title={escopo.completo}>
                        {escopo.texto}{def?.exigeDataReferencia && dataReferenciaValida(s.dataReferencia) ? ` · ${formatarDataReferencia(s.dataReferencia)}` : ""}
                      </span>
                    )}
                  </span>
                  {def?.exigeDataReferencia && (
                    // Data deste item (independente dos demais itens da mesma tela). Obrigatória: vazia/inválida bloqueia o salvar.
                    <input type="date" aria-label={`Data de referência do item ${i + 1}`} data-testid="tv-data-referencia-item" required
                      className={cn("h-7 rounded-md border bg-background px-2 text-xs", dataReferenciaValida(s.dataReferencia) ? "border-border" : "border-destructive")}
                      value={s.dataReferencia ?? ""}
                      onChange={(e) => onChange((x) => ({ ...x, slides: x.slides.map((y, j) => (j === i ? { ...y, dataReferencia: e.target.value || null } : y)) }))} />
                  )}
                  <Button variant="ghost" size="sm" className="h-7 px-2 text-xs"
                    title={def && foraDaTela.length > 0 ? avisoProjetoExclusivo(def, foraDaTela)
                      : def && !def.multiProjeto && projetosSelecionados.length > 1 ? "Esta tela exibe um projeto por vez — selecione apenas um projeto" : "Trocar os projetos desta tela pelos projetos selecionados acima"}
                    disabled={projetosSelecionados.length === 0 || foraDaTela.length > 0 || (!!def && !def.multiProjeto && projetosSelecionados.length > 1)}
                    onClick={() => onChange((x) => ({ ...x, slides: x.slides.map((y, j) => (j === i ? slideComProjetos(y.screenId, projetosSelecionados, y.dataReferencia) : y)) }))}>
                    Usar seleção
                  </Button>
                  <Button variant="ghost" size="icon" className="h-7 w-7" title="Subir" disabled={i === 0}
                    onClick={() => onChange((x) => ({ ...x, slides: mover(x.slides, i, i - 1) }))}><ArrowUp className="h-3.5 w-3.5" /></Button>
                  <Button variant="ghost" size="icon" className="h-7 w-7" title="Descer" disabled={i === a.slides.length - 1}
                    onClick={() => onChange((x) => ({ ...x, slides: mover(x.slides, i, i + 1) }))}><ArrowDown className="h-3.5 w-3.5" /></Button>
                  <Button variant="ghost" size="icon" className="h-7 w-7 text-destructive hover:text-destructive" title="Remover"
                    onClick={() => onChange((x) => ({ ...x, slides: x.slides.filter((_, j) => j !== i) }))}><Trash2 className="h-3.5 w-3.5" /></Button>
                </li>
              );
            })}
          </ol>
        )}
      </div>
    </section>
  );
}
