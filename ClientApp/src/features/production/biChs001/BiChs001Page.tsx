// Rota /controle-producao/bi-chs001/<pagina>: reprodução do relatório Power BI CHS001-000-PM0-RT-002-01.
// Projeto: URL → projeto atual → seletor (mesma seleção interna das telas do Controle de Produção). O BI é da CHS001:
// outro projeto selecionado NÃO é exibido sob os títulos da CHS001 (aviso explícito).
// Permissão: keys da página (biChs001Paginas.ts) no projeto selecionado; cada endpoint de dados revalida no backend.
import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePageHeader } from "@/components/PageHeaderContext";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { AcessoNegado } from "@/features/permissions/PermissionRouteGuard";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import ProductionProjectSelector, { useProductionSelectionInterna } from "../ProductionProjectSelector";
import { BI_CHS001_GRUPO, BI_CHS001_PAGINAS, paginaBiPorId, rotaBiChs001, type BiChs001Pagina } from "./biChs001Paginas";
import { BiChs001Canvas } from "./BiChs001Canvas";
import { VisualBi } from "./pbi/VisualBi";
import type { Link } from "./pbi/VisuaisEstaticos";
import type { PaginaPbi } from "./pbi/modelo";
import { useDadosBi } from "./dados/useDadosBi";
import { avisosDeFonte, infoFonte } from "./dados/avisos";
import "./biChs001.css";

const LAYOUTS = import.meta.glob<{ default: PaginaPbi }>("./generated/p*.json");

/** O BI reproduz exclusivamente a CHS001 (títulos fixos "CHS001 - ETHANOL PLANT" no layout original). */
export const ehProjetoChs001 = (nome: string | undefined) => !!nome && /^CHS001\b/i.test(nome.trim());

function useLayout(pagina: BiChs001Pagina) {
  const [layout, setLayout] = useState<PaginaPbi>();
  const [erro, setErro] = useState<string>();
  useEffect(() => {
    let vivo = true;
    setLayout(undefined); setErro(undefined);
    const carregar = LAYOUTS[`./generated/${pagina.arquivo}`];
    if (!carregar) { setErro(`Layout ${pagina.arquivo} não encontrado.`); return; }
    carregar().then((m) => { if (vivo) setLayout(m.default); }).catch(() => { if (vivo) setErro("Falha ao carregar o layout da página."); });
    return () => { vivo = false; };
  }, [pagina.arquivo]);
  return { layout, erro };
}

function PaginaBi({ pagina, projectId }: { pagina: BiChs001Pagina; projectId: string }) {
  const { layout, erro } = useLayout(pagina);
  const { dadosDe, fontes } = useDadosBi(layout, projectId);
  const avisos = useMemo(() => (layout ? avisosDeFonte(layout, fontes) : []), [layout, fontes]);
  const informacoes = useMemo(() => infoFonte(fontes), [fontes]);
  const navigate = useNavigate();
  const location = useLocation();
  if (erro) return <ReportState title="BI CHS001" error>{erro}</ReportState>;
  if (!layout) return <ReportState title="Carregando página do BI…">Aguarde.</ReportState>;
  const linkDisponivel = (l: Link) => l.tipo === "PageNavigation" && !!paginaBiPorId(l.destino);
  const aoClicarLink = (l: Link) => {
    const alvo = paginaBiPorId(l.destino);
    if (alvo) navigate(rotaBiChs001(alvo.slug) + location.search);
  };
  return (
    <>
      {informacoes.map((a) => <div key={a} className="bi-info-fonte" role="note">{a}</div>)}
      {avisos.map((a) => <div key={a} className="bi-aviso-fonte" role="note">{a}</div>)}
      <BiChs001Canvas pagina={layout}>
        {layout.visuais.map((v) => (
          <VisualBi key={v.id} v={v} dados={dadosDe(v)} aoClicarLink={aoClicarLink} linkDisponivel={linkDisponivel} />
        ))}
      </BiChs001Canvas>
    </>
  );
}

export default function BiChs001Page({ pagina }: { pagina: BiChs001Pagina }) {
  const [params, setParams] = useSearchParams();
  const { projetoAtual, definirProjeto } = useProjetoAtual();
  const s = useProductionSelectionInterna({
    hubId: params.get("hub") ?? projetoAtual?.hubId ?? null,
    projectId: params.get("projeto") ?? (params.get("hub") ? null : projetoAtual?.projectId ?? null),
  });
  const projeto = s.selection[0];

  useEffect(() => {
    const proximos = new URLSearchParams(params);
    let mudou = false;
    for (const [k, v] of Object.entries({ hub: s.hubId, projeto: s.projectId })) {
      if (v && proximos.get(k) !== v) { proximos.set(k, v); mudou = true; }
      if (!v && proximos.has(k)) { proximos.delete(k); mudou = true; }
    }
    if (mudou) setParams(proximos, { replace: true });
    if (s.projectId) definirProjeto({ hubId: s.hubId, projectId: s.projectId, nome: projeto?.name });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [s.hubId, s.projectId, projeto?.name]);

  usePageHeader(projeto ? projeto.name : "BI CHS001 — selecione o projeto", <ProductionProjectSelector s={s} />,
    [projeto?.id, projeto?.name, s.projectId, s.projects, s.loadingProjects, s.hubId, pagina.slug]);

  const perm = useProjectPermissions(s.projectId || null);

  let conteudo;
  if (s.selection.length === 0) conteudo = <ReportState title="Nenhum projeto selecionado">Selecione o projeto CHS001 no topo da página.</ReportState>;
  else if (perm.loading) conteudo = <ReportState title="Carregando permissões do projeto…">Aguarde.</ReportState>;
  else if (!perm.can(pagina.permissionKeys)) conteudo = <AcessoNegado detalhe={`Você não tem permissão para "${pagina.nome}" neste projeto.`} />;
  else if (!ehProjetoChs001(projeto?.name))
    conteudo = (
      <ReportState title="BI exclusivo da CHS001">
        Esta seção reproduz o relatório Power BI da CHS001 - ETHANOL PLANT. Selecione o projeto CHS001 no topo da página
        (projeto atual: {projeto?.name ?? "—"}).
      </ReportState>
    );
  else conteudo = <PaginaBi key={`${pagina.slug}|${s.projectId}`} pagina={pagina} projectId={s.projectId} />;

  return (
    <div className="flex h-full flex-col gap-2 animate-enter">
      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
        <span>Controle de Produção</span><span>›</span><span>{BI_CHS001_GRUPO}</span><span>›</span>
        <span className="text-foreground">{pagina.ordem}/{BI_CHS001_PAGINAS.length} · {pagina.nome}</span>
      </div>
      {s.error && <p className="text-sm text-rose-400">{s.error}</p>}
      {conteudo}
    </div>
  );
}
