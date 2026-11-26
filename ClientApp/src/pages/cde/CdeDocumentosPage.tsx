// CDE → Documentos: abre o navegador REAL de pastas (Project Files) do PROJETO ATUAL. Não é uma tela de documentos paralela:
// só resolve a pasta raiz e redireciona para o FilesPage existente.
import { useEffect, useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { buscarPastaRaiz, rotaPasta } from "./pastasDoProjeto";

export default function CdeDocumentosPage() {
  const { projetoAtual } = useProjetoAtual();
  const navigate = useNavigate();
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    if (!projetoAtual) return;
    const abort = new AbortController();
    setErro(null);
    buscarPastaRaiz(projetoAtual.hubId, projetoAtual.projectId, abort.signal)
      .then((raiz) => navigate(rotaPasta(projetoAtual.hubId, projetoAtual.projectId, raiz),
        { replace: true, state: { projectName: projetoAtual.nome, hubId: projetoAtual.hubId } }))
      .catch((e: unknown) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Falha ao abrir as pastas do projeto."); });
    return () => abort.abort();
  }, [projetoAtual, navigate]);

  if (!projetoAtual) return <Navigate to="/dashboard" replace />;
  if (erro) return <ReportState title="Não foi possível abrir as pastas do projeto">{erro}</ReportState>;
  return (
    <p className="flex items-center gap-2 p-8 text-sm text-muted-foreground">
      <Loader2 className="h-4 w-4 animate-spin" />Abrindo Project Files de {projetoAtual.nome ?? projetoAtual.projectId}…
    </p>
  );
}
