// Coluna "Template Auditoria" da Configuração do Projeto: escolhe qual template (config.PacoteOcr) a Auditoria de Qualidade usa
// no projeto. Persiste em config.ProjetoConfiguracao.PacoteOcrId (só o template; a pasta de auditoria é preservada).
// Criar/calibrar templates continua em Configurações → Configuração de Templates → Auditoria Documental.
import { useCallback, useEffect, useState } from "react";
import toast from "react-hot-toast";
import { listPackages, saveProjectTemplate, listProjectTemplates, type OcrPackage } from "./documentQualityApi";

const SEM_TEMPLATE = "";

/** Templates disponíveis + template atual de cada projeto (carregados uma vez para a tabela inteira). */
export function useProjectAuditTemplates() {
  const [pacotes, setPacotes] = useState<OcrPackage[]>([]);
  const [porProjeto, setPorProjeto] = useState<Record<string, number>>({});
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    Promise.all([listPackages(abort.signal), listProjectTemplates(abort.signal)])
      .then(([ps, vinculos]) => {
        setPacotes(ps);
        setPorProjeto(Object.fromEntries(vinculos.map((v) => [v.projectIdAcc, v.packageId])));
      })
      .catch((e: unknown) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Falha ao carregar os templates de auditoria."); });
    return () => abort.abort();
  }, []);

  const alterar = useCallback(async (projectIdAcc: string, packageId: number | null) => {
    const anterior = porProjeto[projectIdAcc];
    setPorProjeto((m) => { const n = { ...m }; if (packageId) n[projectIdAcc] = packageId; else delete n[projectIdAcc]; return n; });
    try {
      await saveProjectTemplate(projectIdAcc, packageId);
      toast.success("Template de auditoria atualizado.");
    } catch (e) {
      setPorProjeto((m) => { const n = { ...m }; if (anterior) n[projectIdAcc] = anterior; else delete n[projectIdAcc]; return n; });
      toast.error(e instanceof Error ? e.message : "Não foi possível salvar o template de auditoria.");
    }
  }, [porProjeto]);

  return { pacotes, porProjeto, erro, alterar };
}

export function ProjectAuditTemplateSelect({ projectIdAcc, pacotes, valor, podeEditar, onChange }: {
  projectIdAcc: string | null | undefined; pacotes: readonly OcrPackage[]; valor: number | undefined; podeEditar: boolean;
  onChange: (packageId: number | null) => void;
}) {
  if (!projectIdAcc) return <span className="text-[10px] text-muted-foreground">—</span>;
  return (
    <select aria-label="Template de auditoria" disabled={!podeEditar} value={valor ? String(valor) : SEM_TEMPLATE}
      onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      className="h-9 w-full rounded-md border border-border/50 bg-card/30 px-2 text-[10px] font-black uppercase text-foreground disabled:opacity-60">
      <option value={SEM_TEMPLATE}>Sem template</option>
      {pacotes.map((p) => <option key={p.id} value={p.id}>{p.nome}</option>)}
      {/* Template vinculado que não está mais na lista (excluído): mostra o id para não esconder o vínculo. */}
      {valor && !pacotes.some((p) => p.id === valor) && <option value={valor}>Template #{valor}</option>}
    </select>
  );
}
