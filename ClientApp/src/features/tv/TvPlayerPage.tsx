import { classificarFalhaTv, localTvInterval, mensagemFalhaTv, setTvPortalScope, TV_FALHAS_TOLERADAS } from "./portal/portalTransport";
// Gestão à Vista — player da TV em tela cheia (fora do Layout: sem sidebar/header). Lê a configuração salva em
// Configurações → Configuração de Telas para TV (GET /api/tv-config) e exibe as telas em rodízio, reutilizando o
// ProductionScreen (o mesmo dashboard do Controle de Produção). Cada tela busca seus dados no endpoint do
// dashboard, que valida acesso ao projeto e permissão — o player não contorna nenhuma regra.
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { AlertTriangle, ChevronLeft, ChevronRight, Loader2, Maximize, Minimize, Pause, Play, X } from "lucide-react";
import { ThemeLogo } from "@/features/theme/ThemeLogo";
import { ConteudoTv, TV_PROGRAMACAO_DIARIA, getTelaTv } from "./tvScreens";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { carregarConfiguracaoTv } from "./tvApi";
import {
  dataReferenciaAutomatica, diaAnterior, escolherApresentacao, formatarDataReferencia, layoutDoSlide, projetosDoSlide, proximoIndice, resumoProjetosSlide,
  rotuloLayoutDiario, tituloCentralTv, tvAtiva,
  type TituloCentralTv, type TvPresentation,
} from "./tvLogic";
import { useHojeBahia } from "./useHojeBahia";
import { sairDaTelaCheia, useTelaCheia, type DocumentoTelaCheia } from "./telaCheia";
import { NavegacaoTvProvider, destinoNaApresentacao, type NavegacaoTv } from "./tvNavegacao";

export default function TvPlayerPage({ portal }: { portal?: { presentationId: string; seconds: number; onExit: () => void } } = {}) {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { can } = usePermissions();
  const [apresentacoes, setApresentacoes] = useState<TvPresentation[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [indice, setIndice] = useState(0);
  const [pausado, setPausado] = useState(false);
  const [decorrido, setDecorrido] = useState(0);
  // Tela cheia do CONTAINER da apresentação (não do documento inteiro); estado lido do navegador (fullscreenchange).
  const palco = useRef<HTMLDivElement>(null);
  const { ativa: emTelaCheia, alternar: telaCheia, aviso: avisoTelaCheia } = useTelaCheia(palco);
  // Programação Diária automática: datas VIGENTES no cabeçalho (mudam sozinhas na virada do dia, sem F5).
  const hoje = useHojeBahia();

  useEffect(() => {
    const abort = new AbortController();
    (portal ? fetch(`/api/tv-portal/presentations/${encodeURIComponent(portal.presentationId)}`, { credentials: "same-origin", signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(r.status === 401 ? "Sessão expirada. Entre novamente." : r.status === 403 ? "Apresentação não autorizada para esta conta. Solicite ao administrador que confira a atribuição e o vínculo ativo a todos os projetos dos slides." : "Apresentação indisponível. Tente novamente ou consulte o administrador."); return { configuration: { presentations: [await r.json() as TvPresentation] } }; }) : carregarConfiguracaoTv(abort.signal))
      .then((r) => { if (!abort.signal.aborted) setApresentacoes(r.configuration.presentations); })
      .catch((e) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Não foi possível carregar a configuração da TV."); });
    return () => abort.abort();
  }, []);

  const apresentacao = useMemo(
    () => (apresentacoes ? escolherApresentacao(apresentacoes, portal?.presentationId ?? params.get("apresentacao")) : undefined),
    [apresentacoes, params, portal?.presentationId]
  );
  const total = apresentacao?.slides.length ?? 0;
  useEffect(() => {
    if (!portal) return;
    const abort = new AbortController();
    let falhas = 0;
    const timer = window.setInterval(() => {
      fetch(`/api/tv-portal/presentations/${encodeURIComponent(portal.presentationId)}`, { credentials: "same-origin", signal: abort.signal })
        .then(async r => {
          if (!r.ok) throw Object.assign(Error(mensagemFalhaTv(r.status)), { status: r.status });
          const p = await r.json() as TvPresentation;
          falhas = 0; setErro(null); setApresentacoes([p]);
        })
        .catch(e => {
          if (abort.signal.aborted) return;
          const status = typeof e?.status === "number" ? e.status as number : null;
          // Revogado/encerrado: sai da tela já. Transitório: segura o já autorizado e só desiste após várias tentativas;
          // o intervalo continua e a TV volta sozinha quando o servidor responder.
          if (classificarFalhaTv(status) === "transitorio" && ++falhas < TV_FALHAS_TOLERADAS) return;
          setErro(mensagemFalhaTv(status)); setApresentacoes([]);
        });
    }, 15000);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [portal?.presentationId]);
  const duracaoMs = (portal ? (localTvInterval(portal.seconds) ?? 30) : (apresentacao?.secondsPerScreen ?? 30)) * 1000;

  const irPara = useCallback((passo: 1 | -1) => { setIndice((i) => proximoIndice(i, total, passo)); setDecorrido(0); }, [total]);

  // Menus internos das telas (Gestão à Vista) navegam SÓ entre os slides desta apresentação: mesma ordem, projetos
  // salvos e tempo por tela (o salto zera o tempo do slide; pausado continua pausado). Nunca sai do player.
  const indiceAtual = Math.min(indice, Math.max(0, total - 1));
  useLayoutEffect(() => { setTvPortalScope(portal ? { presentation: portal.presentationId, slide: indiceAtual } : null); return () => setTvPortalScope(null); }, [portal?.presentationId, indiceAtual]);
  const navegacaoTv = useMemo<NavegacaoTv>(() => ({
    destino: (screenId) => destinoNaApresentacao((apresentacao?.slides ?? []).map((s) => ({ screenId: s.screenId, projectIds: projetosDoSlide(s).map((p) => p.projectId) })),
      indiceAtual, screenId),
    irPara: (i) => { if (i >= 0 && i < total) { setIndice(i); setDecorrido(0); } },
  }), [apresentacao, indiceAtual, total]);

  // Relógio do rodízio (tique de 250 ms para a barra de progresso).
  useEffect(() => {
    if (pausado || total === 0) return;
    const t = window.setInterval(() => setDecorrido((d) => d + 250), 250);
    return () => window.clearInterval(t);
  }, [pausado, total]);
  useEffect(() => { if (decorrido >= duracaoMs) irPara(1); }, [decorrido, duracaoMs, irPara]);

  const sair = useCallback(() => {
    void sairDaTelaCheia(document as unknown as DocumentoTelaCheia);
    if (portal) { portal.onExit(); return; }
    navigate(can("settings.tv.configure") ? "/configuracoes/telas-tv" : "/dashboard");
  }, [navigate, can, portal]);

  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (e.key === "ArrowRight") irPara(1);
      else if (e.key === "ArrowLeft") irPara(-1);
      else if (e.key === " ") { e.preventDefault(); setPausado((p) => !p); }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [irPara]);

  if (apresentacao && !tvAtiva(apresentacao)) {
    return (
      <Tela>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 text-center">
          <AlertTriangle className="h-10 w-10 text-amber-500" />
          <p className="text-xs font-black uppercase tracking-[0.3em] text-muted-foreground">{apresentacao.name}</p>
          <p className="max-w-md text-sm text-muted-foreground">Esta TV está INATIVA. Ative-a em Configurações → Configuração de Telas para TV.</p>
          <button type="button" onClick={sair} className="rounded-lg border border-border px-4 py-2 text-sm font-bold hover:bg-accent">Voltar</button>
        </div>
      </Tela>
    );
  }
  if (erro || (apresentacoes && !apresentacao) || (apresentacao && total === 0)) {
    return (
      <Tela>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 text-center">
          <AlertTriangle className="h-10 w-10 text-amber-500" />
          <p className="max-w-md text-sm text-muted-foreground">
            {erro ?? "Nenhuma apresentação configurada. Monte a sequência em Configurações → Configuração de Telas para TV."}
          </p>
          <button type="button" onClick={sair} className="rounded-lg border border-border px-4 py-2 text-sm font-bold hover:bg-accent">Voltar</button>
        </div>
      </Tela>
    );
  }
  if (!apresentacao) {
    return <Tela><div className="flex flex-1 items-center justify-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando apresentação…</div></Tela>;
  }

  const slide = apresentacao.slides[Math.min(indice, total - 1)];
  const def = getTelaTv(slide.screenId);
  // Qualquer slide com um unico projeto exibe seu nome; o slide de uma disciplina exibe projeto — disciplina.
  const titulo = tituloCentralTv(slide, def?.label, TV_PROGRAMACAO_DIARIA);
  const progresso = Math.min(100, (decorrido / duracaoMs) * 100);
  const botao = "flex h-8 w-8 items-center justify-center rounded-md text-muted-foreground hover:bg-accent hover:text-foreground";

  return (
    <Tela palco={palco}>
      {/* ESQUERDA: logo + nome da apresentação (secundário) · CENTRO EXATO (absoluto): nome da TELA em exibição (destaque) ·
          DIREITA: contador e controles. Só a posição dos textos foi trocada; dados/rotação inalterados.
          Projeto unico (titulo.destaque): grade 1fr | 2fr | 1fr — título no centro exato, em fluxo (quebra em até 2 linhas e
          a barra cresce junto, sem sobrepor esquerda/direita). Nas demais telas os wrappers são "contents" (layout original). */}
      <header className={titulo.destaque
        ? "relative grid grid-cols-[minmax(0,1fr)_minmax(0,2fr)_minmax(0,1fr)] items-center gap-4 border-b border-border bg-card/40 px-6 py-3"
        : "relative flex items-center gap-4 border-b border-border bg-card/40 px-6 py-3"} data-testid="tv-cabecalho">
        <div className={titulo.destaque ? "flex min-w-0 items-center gap-4" : "contents"}>
        <div className="flex h-8 flex-shrink-0 items-center"><ThemeLogo variant="sidebar-compact" /></div>
        <div className={titulo.destaque ? "min-w-0 flex-1" : "min-w-0 max-w-[28%] flex-1"}>
          <p className="truncate text-sm font-black tracking-tight" data-testid="tv-identificacao"
            title={`Configuração: ${apresentacao.name} · id ${apresentacao.id}`}>{apresentacao.name}</p>
          <p className={`${titulo.destaque ? "line-clamp-2" : "truncate"} text-[11px] font-medium text-muted-foreground`} title={resumoProjetosSlide(slide).completo} data-testid="tv-escopo-slide">
            {resumoProjetosSlide(slide).texto}
            {def?.exigeDataReferencia && dataReferenciaAutomatica(slide.dataReferencia)
              ? ` · Hoje ${formatarDataReferencia(hoje)} + pendências de ${formatarDataReferencia(diaAnterior(hoje))}`
                + (layoutDoSlide(slide) === "padrao" ? "" : ` · ${rotuloLayoutDiario(slide)}`)
              : def?.exigeDataReferencia && slide.dataReferencia ? ` · ${formatarDataReferencia(slide.dataReferencia)}` : ""}
          </p>
        </div>
        </div>
        <TituloTelaTv titulo={titulo} />
        <div className={titulo.destaque ? "flex min-w-0 items-center justify-end gap-4" : "contents"}>
        <div className="flex-1" />
        {avisoTelaCheia && <span role="status" className="text-[11px] font-medium text-amber-500">{avisoTelaCheia}</span>}
        <span className="text-xs font-bold tabular-nums text-muted-foreground">{Math.min(indice, total - 1) + 1} / {total}</span>
        <div className="flex items-center gap-1">
          <button type="button" className={botao} title="Anterior (←)" onClick={() => irPara(-1)}><ChevronLeft className="h-4 w-4" /></button>
          <button type="button" className={botao} title={pausado ? "Continuar (espaço)" : "Pausar (espaço)"} onClick={() => setPausado((p) => !p)}>
            {pausado ? <Play className="h-4 w-4" /> : <Pause className="h-4 w-4" />}
          </button>
          <button type="button" className={botao} title="Próxima (→)" onClick={() => irPara(1)}><ChevronRight className="h-4 w-4" /></button>
          <button type="button" className={botao} title={emTelaCheia ? "Sair da tela cheia" : "Tela cheia"}
            aria-label={emTelaCheia ? "Sair da tela cheia" : "Tela cheia"} aria-pressed={emTelaCheia} data-testid="tv-tela-cheia"
            onClick={() => void telaCheia()}>
            {emTelaCheia ? <Minimize className="h-4 w-4" /> : <Maximize className="h-4 w-4" />}
          </button>
          <button type="button" className={botao} title="Sair da apresentação" onClick={sair}><X className="h-4 w-4" /></button>
        </div>
        </div>
      </header>
      <div className="h-1 w-full bg-border/40">
        <div className="h-full bg-primary transition-[width] duration-200 ease-linear" style={{ width: `${progresso}%` }} />
      </div>
      <main className="flex-1 overflow-auto p-6">
        {/* Projetos DO ITEM (config salva, determinística — nunca o contexto do navegador). key com todos os projetos:
            trocar de slide remonta a tela e nenhuma seleção anterior vaza. */}
        <NavegacaoTvProvider valor={navegacaoTv}>
          <ConteudoTv
            key={`${indice}-${slide.screenId}-${projetosDoSlide(slide).map((p) => p.projectId).join("+")}-${slide.dataReferencia ?? ""}`}
            screenId={slide.screenId}
            dataReferencia={def?.exigeDataReferencia ? slide.dataReferencia ?? null : null}
            layoutDiario={def?.exigeDataReferencia ? slide.layoutDiario ?? null : null}
            disciplina={def?.exigeDataReferencia ? slide.disciplina ?? null : null}
            disciplinaNome={def?.exigeDataReferencia ? slide.disciplinaNome ?? null : null}
            selection={projetosDoSlide(slide).map((p) => ({ id: p.projectId, hubId: p.hubId, name: p.projectName ?? p.projectId }))}
          />
        </NavegacaoTvProvider>
      </main>
    </Tela>
  );
}

/**
 * Título central da barra. destaque (projeto unico): em fluxo na coluna central, peso 900, ≥ text-2xl e até 2 linhas
 * (texto longo desce um degrau; o completo fica no tooltip); "— DISCIPLINA" em inline-block para quebrar como um bloco só.
 * Multprojeto: exatamente o h1 original (absoluto, uma linha).
 */
export const TituloTelaTv = ({ titulo }: { titulo: TituloCentralTv }) => titulo.destaque ? (
  <h1 className={`line-clamp-2 min-w-0 break-words text-center font-black uppercase leading-tight tracking-wide text-foreground ${
    titulo.texto.length > 60 ? "text-2xl 2xl:text-3xl" : "text-2xl lg:text-3xl 2xl:text-4xl"}`}
    title={titulo.texto} data-testid="tv-tela-atual">
    {titulo.partes ? <>{titulo.partes.projeto} <span className="inline-block">— {titulo.partes.disciplina}</span></> : titulo.texto}
  </h1>
) : (
  <h1 className="pointer-events-none absolute left-1/2 top-1/2 max-w-[40%] -translate-x-1/2 -translate-y-1/2 truncate text-center text-xl font-bold uppercase tracking-wide text-foreground"
    data-testid="tv-tela-atual">
    {titulo.texto}
  </h1>
);

// Em tela cheia o container ocupa exatamente a viewport (100% × 100%), sem scroll externo; o conteúdo rola dentro de <main>.
const Tela = ({ children, palco }: { children: ReactNode; palco?: RefObject<HTMLDivElement> }) => (
  <div ref={palco} data-testid="tv-palco"
    className="flex min-h-screen w-full flex-col bg-background text-foreground [&:fullscreen]:h-full [&:fullscreen]:min-h-0 [&:fullscreen]:w-full [&:fullscreen]:overflow-hidden">
    {children}
  </div>
);
