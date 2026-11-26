// CDE de UM projeto: segundo ponto de entrada para componentes que já existem em Relatórios.
//   Lista de Documentos -> CdeDocumentList (visão da LDP ESPELHO Tipo 1/Tipo 2, GET /api/reports/cde/document-list)
//   Curva S             -> ViewCurvaS (mesmo componente/endpoint /api/reports/curva-s)
//   Docs                -> FilesPage existente (rootFolderId via /DataManagement/Data/GetProjectRootFolder)
// Nenhuma API, consulta ou cópia de implementação nova.
import { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ArrowLeft, ClipboardList, ExternalLink, Files, Loader2, TrendingUp, type LucideIcon } from "lucide-react";

import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import CdeDocumentList from "./CdeDocumentList";
import ViewCurvaS from "@/components/pmo/ViewCurvaS";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { AcessoNegado } from "@/features/permissions/PermissionRouteGuard";

type AbaCde = "lista-documentos" | "docs" | "curva-s";

// permissionKey: aba visível só com a permissão NO PROJETO (as APIs validam a mesma key no backend).
const ABAS: { id: AbaCde; label: string; icon: LucideIcon; permissionKey: string }[] = [
  { id: "lista-documentos", label: "Lista de Documentos", icon: ClipboardList, permissionKey: "cde.document-list.view" },
  { id: "docs", label: "Docs", icon: Files, permissionKey: "cde.docs.view" },
  { id: "curva-s", label: "Curva S", icon: TrendingUp, permissionKey: "cde.curva-s.view" },
];

const abaValida = (v: string | null): v is AbaCde => ABAS.some((a) => a.id === v);

interface AccProject {
  id: string;
  attributes: { name: string };
}

/** Docs: resolve a pasta raiz pelo mesmo endpoint do Dashboard e abre o FilesPage existente. */
function AbaDocs({ hubId, projectId, projectName }: { hubId: string; projectId: string; projectName: string }) {
  const navigate = useNavigate();
  const [rootFolderId, setRootFolderId] = useState<string | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    setRootFolderId(null);
    setErro(null);
    fetch(`/DataManagement/Data/GetProjectRootFolder?hubId=${encodeURIComponent(hubId)}&projectId=${encodeURIComponent(projectId)}`, {
      credentials: "include",
      signal: abort.signal,
    })
      .then(async (res) => {
        if (res.status === 401) { navigate("/login"); return; }
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json();
        if (!data?.rootFolderId) throw new Error("pasta raiz não retornada");
        setRootFolderId(data.rootFolderId);
      })
      .catch((e: unknown) => {
        if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "erro desconhecido");
      });
    return () => abort.abort();
  }, [hubId, projectId, navigate]);

  return (
    <div className="rounded-xl border border-border bg-card/60 p-8 backdrop-blur-sm">
      <h3 className="text-base font-bold">Documentos do projeto no ACC</h3>
      <p className="mt-2 text-sm text-muted-foreground">
        Abre o navegador de pastas e arquivos do projeto (mesma navegação usada hoje a partir do Início).
      </p>
      <div className="mt-6">
        {erro ? (
          <p className="text-sm text-rose-400">Não foi possível localizar a pasta raiz do projeto ({erro}).</p>
        ) : (
          <Button
            disabled={!rootFolderId}
            onClick={() =>
              navigate(`/projects/${hubId}/${projectId}/${rootFolderId}`, { state: { projectName, hubId } })
            }
            className="font-black gap-2 text-[10px] tracking-widest uppercase"
          >
            {rootFolderId ? <ExternalLink className="w-4 h-4" /> : <Loader2 className="w-4 h-4 animate-spin" />}
            {rootFolderId ? "Abrir Docs" : "Localizando diretório raiz..."}
          </Button>
        )}
      </div>
    </div>
  );
}

export default function CdeProjetoPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { hubId = "", projectId = "" } = useParams<{ hubId: string; projectId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  const perm = useProjectPermissions(projectId || null);
  const abasVisiveis = ABAS.filter((a) => perm.can(a.permissionKey));
  const abaDaUrl = searchParams.get("aba");
  // Aba pedida na URL sem permissão continua "pedida" e cai em Acesso negado (sem montar o componente).
  const aba: AbaCde = abaValida(abaDaUrl) ? abaDaUrl : (abasVisiveis[0]?.id ?? "lista-documentos");
  const abaPermitida = abasVisiveis.some((a) => a.id === aba);

  // Nome vem do card (state); em acesso direto/recarregamento, busca na mesma fonte da lista de projetos.
  const nomeDoState = (location.state as { projectName?: string } | null)?.projectName;
  const [projectName, setProjectName] = useState<string | undefined>(nomeDoState);

  useEffect(() => {
    if (nomeDoState) { setProjectName(nomeDoState); return; }
    const abort = new AbortController();
    fetch(`/DataManagement/Data/Projetos?hubId=${encodeURIComponent(hubId)}`, { credentials: "include", signal: abort.signal })
      .then(async (res) => {
        if (res.status === 401) { navigate("/login"); return; }
        if (!res.ok) return;
        const data: { data?: AccProject[] } = await res.json();
        const nome = data.data?.find((p) => p.id === projectId)?.attributes.name;
        if (nome && !abort.signal.aborted) setProjectName(nome);
      })
      .catch(() => { /* sem nome: a página segue com o id do projeto */ });
    return () => abort.abort();
  }, [hubId, projectId, nomeDoState, navigate]);

  const nomeExibido = projectName ?? projectId;

  // Seleção de UM projeto, no formato que ViewCurvaS já espera.
  const selection = useMemo<PmoSelecionado[]>(
    () => [{ id: projectId, hubId, name: nomeExibido }],
    [projectId, hubId, nomeExibido]
  );

  const trocarAba = (nova: AbaCde) => {
    const proximos = new URLSearchParams(searchParams);
    proximos.set("aba", nova);
    // replace: mantém o state (nome do projeto) e não enche o histórico com trocas de aba.
    setSearchParams(proximos, { replace: true, state: location.state });
  };

  return (
    <div className="flex flex-col h-full animate-enter">
      <div className="mb-6 space-y-2">
        <button
          type="button"
          onClick={() => navigate("/cde")}
          className="flex items-center gap-1.5 text-[10px] font-black uppercase tracking-widest text-muted-foreground hover:text-primary transition-colors"
        >
          <ArrowLeft className="w-3.5 h-3.5" /> CDE
        </button>
        <h1 className="text-2xl font-black text-foreground tracking-tight uppercase">{nomeExibido}</h1>
      </div>

      <div className="mb-6 flex gap-1 overflow-x-auto rounded-xl border border-border bg-card/60 p-1 backdrop-blur-sm custom-scrollbar">
        {abasVisiveis.map((a) => {
          const Icon = a.icon;
          const ativa = a.id === aba;
          return (
            <button
              key={a.id}
              type="button"
              onClick={() => trocarAba(a.id)}
              className={cn(
                "flex items-center gap-2 whitespace-nowrap rounded-lg px-4 py-2.5 text-sm font-medium transition-colors duration-200",
                ativa ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"
              )}
            >
              <Icon className="h-4 w-4" />
              {a.label}
            </button>
          );
        })}
      </div>

      <div className="flex-1 min-h-0">
        {perm.loading && <p className="text-sm text-muted-foreground">Carregando permissões do projeto…</p>}
        {!perm.loading && !abaPermitida && <AcessoNegado detalhe="Você não tem permissão para esta área do CDE neste projeto." />}
        {!perm.loading && abaPermitida && aba === "lista-documentos" && <CdeDocumentList key={`${hubId}|${projectId}`} hubId={hubId} projectId={projectId} projectName={nomeExibido} />}
        {!perm.loading && abaPermitida && aba === "docs" && <AbaDocs hubId={hubId} projectId={projectId} projectName={nomeExibido} />}
        {!perm.loading && abaPermitida && aba === "curva-s" && <ViewCurvaS selection={selection} />}
      </div>
    </div>
  );
}
