import { useEffect, useRef, useState } from "react";
import { Navigate, useLocation, useNavigate, useParams } from "react-router-dom";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { VisualBi } from "@/features/production/biChs001/pbi/VisualBi";
import { ler, type PaginaPbi, type VisualPbi } from "@/features/production/biChs001/pbi/modelo";
import type { Link } from "@/features/production/biChs001/pbi/VisuaisEstaticos";
import { recursoPbixBml } from "./api";
import { BI_BML011016_GRUPO, BI_BML011016_PAGINAS, paginaBiBmlPorId, paginaBiBmlPorSlug, rotaBiBml011016 } from "./biBml011016Paginas";
import { useDadosBml } from "./useDadosBml";
import "@/features/production/biChs001/biChs001.css";

// Layout privado: apenas a API autenticada entrega os JSONs do PBIX.
// Evita que o bundle Vite publique definicoes do relatorio para outros usuarios.
const recurso = (valor: unknown) => valor && typeof valor === "object" && "recurso" in valor ? recursoPbixBml(String((valor as { recurso: string }).recurso)) : undefined;

function ImagemProtegida({ v }: { v: VisualPbi }) {
  const fonte = ler<{ image?: { url?: unknown; scaling?: string; name?: string } }>(v.o, "image", "sourceFile")?.image;
  const src = recurso(fonte?.url) ?? recurso(ler(v.o, "general", "imageUrl"));
  if (!src) return null;
  return <div style={{ position: "absolute", left: v.x, top: v.y, width: v.w, height: v.h, zIndex: v.z, overflow: "hidden" }}>
    <img src={src} alt={fonte?.name ?? ""} draggable={false} style={{ width: "100%", height: "100%", objectFit: fonte?.scaling === "Fill" ? "cover" : fonte?.scaling === "Normal" ? "contain" : "fill" }} />
  </div>;
}

function Canvas({ pagina, children }: { pagina: PaginaPbi; children: React.ReactNode }) {
  const ref = useRef<HTMLDivElement>(null); const [escala, setEscala] = useState(0);
  useEffect(() => { const el = ref.current; if (!el) return; const medir = () => { const r = el.getBoundingClientRect(); setEscala(Math.max(.1, Math.min(r.width / pagina.largura, r.height / pagina.altura))); };
    medir(); const ro = new ResizeObserver(medir); ro.observe(el); return () => ro.disconnect(); }, [pagina.altura, pagina.largura]);
  const bg = ler<{ image?: { url?: unknown; scaling?: string } }>(pagina.objetos, "background", "image")?.image;
  return <div ref={ref} className="bi-chs001-area" style={{ background: ler(pagina.objetos, "outspace", "color", "#fff") }}>
    {escala > 0 && <div className="bi-chs001-escala" style={{ width: pagina.largura * escala, height: pagina.altura * escala }}>
      <div className="bi-chs001-pagina" style={{ width: pagina.largura, height: pagina.altura, transform: `scale(${escala})` }}>
        {recurso(bg?.url) && <img className="bi-chs001-fundo" src={recurso(bg?.url)} alt="" draggable={false} />}{children}
      </div>
    </div>}
  </div>;
}

export default function BiBml011016Page() {
  const { slug } = useParams(); const pagina = paginaBiBmlPorSlug(slug); const { loading, error, effective } = usePermissions();
  const [layout, setLayout] = useState<PaginaPbi>(); const [erroLayout, setErroLayout] = useState<string>();
  const autorizado = !loading && !error && effective?.isMasterAdmin === true;
  const dados = useDadosBml(layout?.visuais ?? [], autorizado); const navigate = useNavigate(); const location = useLocation();
  useEffect(() => {
    setLayout(undefined); setErroLayout(undefined);
    if (!pagina || !autorizado) return;
    const ctrl = new AbortController();
    fetch(`/api/bi-bml011016/layout/${pagina.ordem}`, { credentials: "include", cache: "no-store", signal: ctrl.signal })
      .then(async (r) => {
        if (!r.ok) throw new Error(`Layout original indisponível (HTTP ${r.status}).`);
        return r.json() as Promise<PaginaPbi>;
      }).then((resposta) => { if (!ctrl.signal.aborted) setLayout(resposta); })
      .catch((e: unknown) => { if (!ctrl.signal.aborted) setErroLayout(e instanceof Error ? e.message : "Falha ao carregar layout PBIR."); });
    return () => ctrl.abort();
  }, [pagina, autorizado]);
  if (loading) return null;
  if (error || effective?.isMasterAdmin !== true || dados.negado) return <Navigate to="/" replace />;
  if (!pagina) return <Navigate to={rotaBiBml011016(BI_BML011016_PAGINAS[0].slug)} replace />;
  if (erroLayout) return <ReportState title={BI_BML011016_GRUPO} error>{erroLayout}</ReportState>;
  if (!layout) return <ReportState title="Carregando página do BI…">Aguarde.</ReportState>;
  const ir = (link: Link) => { const alvo = link.tipo === "PageNavigation" ? paginaBiBmlPorId(link.destino) : undefined; if (alvo) navigate(rotaBiBml011016(alvo.slug) + location.search); };
  return <div className="flex h-full min-h-0 flex-col gap-2 animate-enter">
    <div className="flex flex-wrap items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
      <span>{BI_BML011016_GRUPO}</span><span>›</span><span className="text-foreground">{pagina.ordem}/9 · {pagina.nome}</span>
      <span className="ml-auto rounded border border-amber-300 bg-amber-50 px-2 py-1 text-[10px] font-medium normal-case tracking-normal text-amber-900">
        Fonte: PBIX original (snapshot histórico, não sincronizado com SQL)
      </span>
    </div>
    <Canvas pagina={layout}>{layout.visuais.map((v) => v.type === "image" ? <ImagemProtegida key={v.id} v={v} /> :
      <VisualBi key={v.id} v={v} dados={dados.dadosDe(v)} aoClicarLink={ir} linkDisponivel={(l) => !!paginaBiBmlPorId(l.destino)} />)}</Canvas>
  </div>;
}
