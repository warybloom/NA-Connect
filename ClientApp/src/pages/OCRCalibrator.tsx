import { useEffect, useRef, useState } from "react";
import { Document, Page, pdfjs } from "react-pdf";
import toast from "react-hot-toast";
import { ChevronLeft, ChevronRight, Hand, Maximize, ZoomIn, ZoomOut } from "lucide-react";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/build/pdf.worker.min.mjs", import.meta.url).toString();

interface Props { pacoteId: number; tipoFolha: string; onBack: () => void }
interface Zone { x: number; y: number; width: number; height: number; page: number; referenceWidth: number; referenceHeight: number }
interface ApiRegion { campoDestino: string; x: number; y: number; width: number; height: number; pagina: number; page?: number; referenceWidth: number; referenceHeight: number }
interface PdfPageLike { getViewport: (options: { scale: number }) => { width: number; height: number } }

const BASE_WIDTH = 850;
/** Coluna REAL da LDP (catálogo dinâmico — GET /api/admin/ocr/campos, metadados de dbo.Ldp_Unificada). */
interface CampoCatalogo { campo: string; rotulo: string; tipo: string; ordem: number; recomendado: boolean; disponivel: boolean }

export const OCRCalibrator = ({ pacoteId, tipoFolha, onBack }: Props) => {
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [numPages, setNumPages] = useState(0);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState<{ width: number; height: number } | null>(null);
  // CAMPOS pertencem ao TEMPLATE (comuns a todos os formatos — config.PacoteOcrCampo); ZONAS são as coordenadas DESTE formato.
  const [catalogo, setCatalogo] = useState<CampoCatalogo[]>([]);
  const [campos, setCampos] = useState<string[]>([]);
  const [migracaoPendente, setMigracaoPendente] = useState(false);
  const [busca, setBusca] = useState("");
  const [buscando, setBuscando] = useState(false);
  const [activeField, setActiveField] = useState<string>("");
  const [zones, setZones] = useState<Record<string, Zone>>({});
  const [currentRect, setCurrentRect] = useState<Zone | null>(null);
  const [isDrawing, setIsDrawing] = useState(false);
  const [loadingRegions, setLoadingRegions] = useState(true);
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const panPosition = useRef({ x: 0, y: 0, left: 0, top: 0 });

  useEffect(() => {
    const abort = new AbortController();
    setLoadingRegions(true);
    const json = async <T,>(url: string) => {
      const response = await fetch(url, { signal: abort.signal });
      if (!response.ok) throw new Error(`Falha HTTP ${response.status}`);
      return response.json() as Promise<T>;
    };
    Promise.all([
      json<ApiRegion[]>(`/api/admin/ocr/coordenadas?pacoteId=${pacoteId}&tipoFolha=${encodeURIComponent(tipoFolha)}`),
      json<{ migracaoPendente: boolean; campos: string[] }>(`/api/admin/ocr/pacotes/${pacoteId}/campos`),
    ])
      .then(([regions, template]) => {
        // Coordenadas DESTE formato (inclusive de campos hoje inativos: ficam guardadas, nada é apagado).
        setZones(Object.fromEntries(regions.map((region) => [region.campoDestino, {
          x: Number(region.x), y: Number(region.y), width: Number(region.width), height: Number(region.height),
          page: Number(region.pagina ?? region.page ?? 1), referenceWidth: Number(region.referenceWidth), referenceHeight: Number(region.referenceHeight),
        }])));
        setMigracaoPendente(template.migracaoPendente);
        setCampos(template.campos);
        setActiveField(template.campos[0] ?? "");
      })
      .catch((error) => { if (!abort.signal.aborted) toast.error(error instanceof Error ? error.message : "Falha ao carregar regiões."); })
      .finally(() => { if (!abort.signal.aborted) setLoadingRegions(false); });
    return () => abort.abort();
  }, [pacoteId, tipoFolha]);

  useEffect(() => {
    const abort = new AbortController();
    fetch("/api/admin/ocr/campos", { signal: abort.signal })
      .then(async (response) => { if (!response.ok) throw new Error(`Falha HTTP ${response.status}`); return response.json() as Promise<CampoCatalogo[]>; })
      .then(setCatalogo)
      .catch((error) => { if (!abort.signal.aborted) toast.error(error instanceof Error ? error.message : "Falha ao carregar o catálogo de campos."); });
    return () => abort.abort();
  }, []);

  useEffect(() => () => { if (fileUrl) URL.revokeObjectURL(fileUrl); }, [fileUrl]);

  const rotulo = (campo: string) => catalogo.find((c) => c.campo.toLowerCase() === campo.toLowerCase())?.rotulo ?? campo;
  const adicionarCampo = (campo: string) => {
    if (!campo || campos.some((c) => c.toLowerCase() === campo.toLowerCase())) return;
    setCampos((c) => [...c, campo]); setActiveField(campo); setBusca(""); setBuscando(false);
  };
  // Remover = o TEMPLATE deixa de auditar o campo (em todos os formatos). A coordenada deste formato só é descartada no modo antigo
  // (migration 07 pendente), em que o campo ainda é derivado das regiões.
  const removerCampo = (campo: string) => {
    setCampos((c) => c.filter((x) => x !== campo));
    if (migracaoPendente) setZones((z) => { const n = { ...z }; delete n[campo]; return n; });
    if (activeField === campo) setActiveField("");
  };
  const termo = busca.trim().toLowerCase();
  const opcoes = catalogo
    .filter((c) => c.disponivel && !campos.some((x) => x.toLowerCase() === c.campo.toLowerCase()))
    .filter((c) => !termo || c.campo.toLowerCase().includes(termo) || c.rotulo.toLowerCase().includes(termo))
    .sort((a, b) => Number(b.recomendado) - Number(a.recomendado) || a.ordem - b.ordem);

  useEffect(() => {
    const container = scrollRef.current;
    if (!container) return;
    const wheel = (event: WheelEvent) => { event.preventDefault(); setZoom((previous) => Math.min(4, Math.max(.5, previous + (event.deltaY < 0 ? .15 : -.15)))); };
    container.addEventListener("wheel", wheel, { passive: false });
    return () => container.removeEventListener("wheel", wheel);
  }, [fileUrl]);

  const upload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".pdf")) { toast.error("Selecione um arquivo PDF."); event.target.value = ""; return; }
    const header = new Uint8Array(await file.slice(0, 1024).arrayBuffer());
    const signature = new TextDecoder("latin1").decode(header);
    if (!signature.includes("%PDF-")) { toast.error("O arquivo selecionado não possui uma estrutura PDF válida."); event.target.value = ""; return; }
    if (fileUrl) URL.revokeObjectURL(fileUrl);
    setFileUrl(URL.createObjectURL(file)); setPageNumber(1); setPageSize(null); setZoom(1);
  };

  const pdfLoadError = (cause: unknown) => {
    const error = cause instanceof Error ? cause : new Error(String(cause));
    const detail = `${error.name} ${error.message}`.toLowerCase();
    const message = detail.includes("password")
      ? "Este PDF é protegido por senha e não pode ser aberto para calibração."
      : detail.includes("invalidpdf") || detail.includes("invalid pdf") || detail.includes("corrupt") || detail.includes("xref")
        ? "O arquivo PDF está corrompido ou possui estrutura inválida."
        : "Não foi possível renderizar este PDF. Verifique se o arquivo está íntegro.";
    if (fileUrl) URL.revokeObjectURL(fileUrl);
    setFileUrl(null); setPageSize(null); setNumPages(0);
    toast.error(message);
  };

  const pageLoaded = (page: PdfPageLike) => { const viewport = page.getViewport({ scale: 1 }); setPageSize({ width: viewport.width, height: viewport.height }); };
  const referencePoint = (event: React.MouseEvent) => {
    if (!containerRef.current || !pageSize) return null;
    const bounds = containerRef.current.getBoundingClientRect();
    const factor = pageSize.width / BASE_WIDTH;
    return {
      x: Math.max(0, Math.min(pageSize.width, ((event.clientX - bounds.left) / zoom) * factor)),
      y: Math.max(0, Math.min(pageSize.height, ((event.clientY - bounds.top) / zoom) * factor)),
    };
  };
  const drawStart = (event: React.MouseEvent) => {
    if (event.button !== 0 || !fileUrl || !pageSize) return;
    const point = referencePoint(event); if (!point) return;
    setIsDrawing(true); setCurrentRect({ ...point, width: 0, height: 0, page: pageNumber, referenceWidth: pageSize.width, referenceHeight: pageSize.height });
  };
  const drawMove = (event: React.MouseEvent) => {
    if (!isDrawing || !currentRect) return;
    const point = referencePoint(event); if (point) setCurrentRect({ ...currentRect, width: point.x - currentRect.x, height: point.y - currentRect.y });
  };
  const drawEnd = () => {
    if (!isDrawing || !currentRect) return;
    setIsDrawing(false);
    const normalized: Zone = { ...currentRect, x: currentRect.width < 0 ? currentRect.x + currentRect.width : currentRect.x,
      y: currentRect.height < 0 ? currentRect.y + currentRect.height : currentRect.y, width: Math.abs(currentRect.width), height: Math.abs(currentRect.height) };
    if (activeField && normalized.width > 2 && normalized.height > 2) setZones((current) => ({ ...current, [activeField]: normalized }));
    setCurrentRect(null);
  };
  const panStart = (event: React.MouseEvent) => {
    if (event.button !== 1 && event.button !== 2) return;
    event.preventDefault(); setIsPanning(true);
    if (scrollRef.current) panPosition.current = { x: event.clientX, y: event.clientY, left: scrollRef.current.scrollLeft, top: scrollRef.current.scrollTop };
  };
  const panMove = (event: React.MouseEvent) => {
    if (!isPanning || !scrollRef.current) return;
    scrollRef.current.scrollLeft = panPosition.current.left - (event.clientX - panPosition.current.x);
    scrollRef.current.scrollTop = panPosition.current.top - (event.clientY - panPosition.current.y);
  };
  const save = async () => {
    if (campos.length === 0) { toast.error("Adicione ao menos um campo para auditar."); return; }
    const semRegiao = campos.filter((campo) => !zones[campo]);
    const toastId = toast.loading("Salvando regiões…");
    try {
      // 1) campos do TEMPLATE (todos os formatos); 2) coordenadas DESTE formato (todas as zonas, inclusive de campos inativos).
      if (!migracaoPendente) {
        const r = await fetch(`/api/admin/ocr/pacotes/${pacoteId}/campos`, {
          method: "PUT", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ campos }),
        });
        const b = await r.json().catch(() => null);
        if (!r.ok) throw new Error(b?.message ?? `Falha HTTP ${r.status}`);
      }
      const payload = Object.entries(zones).filter(([field]) => !migracaoPendente || campos.includes(field)).map(([field, zone]) => ({ campoDestino: field, x: Number(zone.x.toFixed(4)), y: Number(zone.y.toFixed(4)),
        width: Number(zone.width.toFixed(4)), height: Number(zone.height.toFixed(4)), pagina: zone.page,
        referenceWidth: Number(zone.referenceWidth.toFixed(4)), referenceHeight: Number(zone.referenceHeight.toFixed(4)) }));
      const response = await fetch(`/api/admin/ocr/salvar-coordenadas?pacoteId=${pacoteId}&tipoFolha=${encodeURIComponent(tipoFolha)}`, {
        method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify(payload),
      });
      const body = await response.json().catch(() => null);
      if (!response.ok) throw new Error(body?.message ?? `Falha HTTP ${response.status}`);
      toast.success(semRegiao.length > 0
        ? `Salvo. Sem coordenada neste formato (${tipoFolha}): ${semRegiao.map(rotulo).join(", ")} — aparecerão como NÃO CALIBRADO.`
        : "Campos e coordenadas salvos.", { id: toastId, duration: semRegiao.length > 0 ? 7000 : 3000 });
      onBack();
    } catch (error) { toast.error(error instanceof Error ? error.message : "Falha ao salvar.", { id: toastId }); }
  };
  const displayStyle = (zone: Zone) => { const factor = BASE_WIDTH / zone.referenceWidth; return {
    left: zone.x * factor * zoom, top: zone.y * factor * zoom, width: zone.width * factor * zoom, height: zone.height * factor * zoom,
  }; };

  return <div className="relative flex h-[calc(100vh-160px)] overflow-hidden rounded-3xl border border-border bg-muted shadow-2xl">
    <aside className="z-20 flex w-80 flex-col gap-5 border-r border-border bg-card p-6 shadow-xl">
      <div className="border-b pb-4"><h3 className="text-xl font-black uppercase italic text-foreground">Calibrar template</h3><p className="mt-1 text-[10px] font-bold uppercase tracking-widest text-primary">{tipoFolha} · Template {pacoteId}</p></div>
      <label className="space-y-2 text-[10px] font-black uppercase text-muted-foreground">1. PDF de exemplo
        <input type="file" accept="application/pdf" onChange={upload} className="block w-full text-[10px] file:mr-2 file:rounded-full file:border-0 file:bg-primary/10 file:px-3 file:py-2 file:text-primary" />
      </label>
      <div className="min-h-0 flex-1 space-y-2 overflow-y-auto"><p className="text-[10px] font-black uppercase text-muted-foreground">2. Campos auditados → região</p>
        {migracaoPendente && <p className="rounded-xl bg-amber-500/10 p-2 text-[10px] font-bold text-amber-700 dark:text-amber-400">Migration 07 pendente: campos ainda derivados das regiões deste template.</p>}
        {/* Catálogo DINÂMICO: todas as colunas reais da LDP, com pesquisa por rótulo ou nome da coluna. */}
        <div className="relative">
          <input value={busca} onChange={(e) => { setBusca(e.target.value); setBuscando(true); }} onFocus={() => setBuscando(true)}
            onBlur={() => window.setTimeout(() => setBuscando(false), 150)} disabled={loadingRegions || catalogo.length === 0}
            placeholder="+ Adicionar campo da LDP (pesquisar)…" aria-label="Pesquisar campo da LDP"
            className="w-full rounded-xl border-2 border-dashed border-border bg-card p-2 text-xs font-bold text-foreground/80 outline-none focus:border-primary" />
          {buscando && <ul role="listbox" className="absolute z-30 mt-1 max-h-64 w-full overflow-y-auto rounded-xl border border-border bg-card p-1 shadow-xl">
            {opcoes.length === 0 && <li className="p-2 text-[10px] text-muted-foreground">Nenhuma coluna encontrada.</li>}
            {opcoes.map((c) => <li key={c.campo} role="option" aria-selected={false}>
              <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => adicionarCampo(c.campo)}
                className="flex w-full items-center justify-between gap-2 rounded-lg px-2 py-1.5 text-left hover:bg-primary/5">
                <span className="min-w-0"><span className="block truncate text-[11px] font-bold text-foreground/80">{c.rotulo}</span>
                  <span className="block truncate font-mono text-[9px] text-muted-foreground">{c.campo} · {c.tipo}</span></span>
                {c.recomendado && <span className="flex-shrink-0 rounded bg-emerald-500/10 px-1.5 py-0.5 text-[8px] font-black text-emerald-700 dark:text-emerald-400">RECOMENDADO</span>}
              </button>
            </li>)}
          </ul>}
        </div>
        {!loadingRegions && campos.length === 0 && <p className="rounded-xl bg-muted/60 p-3 text-[10px] text-muted-foreground">Adicione os campos que este template vai auditar e desenhe a região de cada um no PDF.</p>}
        {campos.map((field) => <div key={field} className={`flex w-full items-center gap-1 rounded-xl border-2 text-xs font-bold ${activeField === field ? "border-primary bg-primary/5" : "border-border text-muted-foreground"}`}>
          <button type="button" className="flex min-w-0 flex-1 items-center justify-between p-3 text-left" onClick={() => { setActiveField(field); if (zones[field]) setPageNumber(zones[field].page); }}>
            <span className="min-w-0">{rotulo(field)}<small className="mt-0.5 block truncate font-mono text-[8px] font-normal text-muted-foreground">{field}</small></span>
            {zones[field] ? <span className="text-[9px] text-emerald-600 dark:text-emerald-400">PÁG. {zones[field].page} ✓</span> : <span className="text-[9px] text-amber-600 dark:text-amber-400">não calibrado em {tipoFolha}</span>}
          </button>
          <button type="button" title="Deixar de auditar este campo (em todos os formatos do template)" aria-label={`Remover ${rotulo(field)}`} className="px-2 text-muted-foreground hover:text-red-600" onClick={() => removerCampo(field)}>×</button>
        </div>)}
      </div>
      <div className="rounded-xl border border-blue-500/20 bg-blue-500/10 p-3 text-[9px] font-bold uppercase leading-relaxed text-blue-700 dark:text-blue-400"><Hand className="mr-2 inline h-4 w-4" />Botão direito: mover · Roda: zoom.</div>
      <div className="space-y-2"><button className="w-full rounded-lg bg-primary px-4 py-3 text-xs font-black uppercase text-primary-foreground disabled:opacity-50" disabled={loadingRegions} onClick={() => void save()}>Salvar configuração</button><button className="w-full py-2 text-xs text-muted-foreground" onClick={onBack}>Cancelar</button></div>
    </aside>
    <div ref={scrollRef} className={`relative flex flex-1 justify-center overflow-auto bg-secondary p-12 ${isPanning ? "cursor-grabbing" : ""}`}
      onContextMenu={(event) => event.preventDefault()} onMouseDown={panStart} onMouseMove={panMove} onMouseUp={() => setIsPanning(false)} onMouseLeave={() => setIsPanning(false)}>
      {fileUrl && <div className="fixed right-12 top-24 z-30 flex items-center gap-2 rounded-full border bg-card/90 p-2 shadow-xl">
        <button title="Reduzir zoom" onClick={() => setZoom((value) => Math.max(.5, value - .25))}><ZoomOut className="h-5 w-5" /></button><span className="w-12 text-center text-xs font-black">{Math.round(zoom * 100)}%</span>
        <button title="Aumentar zoom" onClick={() => setZoom((value) => Math.min(4, value + .25))}><ZoomIn className="h-5 w-5" /></button><button title="Zoom 100%" onClick={() => setZoom(1)}><Maximize className="h-4 w-4 text-primary" /></button>
      </div>}
      {fileUrl ? <div className="space-y-3">
        <div className="flex items-center justify-center gap-3 text-xs font-bold text-foreground/80"><button disabled={pageNumber <= 1} onClick={() => setPageNumber((page) => page - 1)}><ChevronLeft className="h-4 w-4" /></button>Página {pageNumber} de {numPages}<button disabled={pageNumber >= numPages} onClick={() => setPageNumber((page) => page + 1)}><ChevronRight className="h-4 w-4" /></button></div>
        <div ref={containerRef} className="relative m-auto h-fit w-fit cursor-crosshair bg-white shadow-[0_30px_60px_rgba(0,0,0,.3)]" onMouseDown={drawStart} onMouseMove={drawMove} onMouseUp={drawEnd}>
          <Document file={fileUrl} onLoadSuccess={({ numPages: count }) => setNumPages(count)} onLoadError={pdfLoadError} onSourceError={pdfLoadError}>
            <Page key={pageNumber} pageNumber={pageNumber} onLoadSuccess={pageLoaded} renderTextLayer={false} renderAnnotationLayer={false} width={BASE_WIDTH * zoom} />
          </Document>
          {Object.entries(zones).filter(([field, zone]) => zone.page === pageNumber && campos.includes(field)).map(([field, zone]) => <div key={field} className="pointer-events-none absolute border-[3px] border-emerald-500 bg-emerald-500/20" style={displayStyle(zone)}><span className="absolute -top-6 left-0 whitespace-nowrap rounded bg-emerald-600 px-2 py-0.5 text-[9px] font-black uppercase text-white">{rotulo(field)}</span></div>)}
          {currentRect && currentRect.page === pageNumber && <div className="pointer-events-none absolute border-[3px] border-primary bg-primary/20" style={displayStyle(currentRect)} />}
        </div>
      </div> : <div className="flex h-[600px] w-full max-w-2xl items-center justify-center rounded-[40px] border-4 border-dashed border-border text-[10px] font-black uppercase tracking-[.3em] text-muted-foreground">Aguardando PDF de exemplo</div>}
    </div>
  </div>;
};
