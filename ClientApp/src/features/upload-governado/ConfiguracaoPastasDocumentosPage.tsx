// Painel de Planejamento → Configuração de Pastas de Documentos (por PROJETO — o projeto atual).
// O planejador marca só as PASTAS-RAIZ protegidas pela LDP (ex.: "09 - DOCUMENTOS DE ENGENHARIA"). A regra vale para a raiz e
// TODAS as subpastas, em qualquer profundidade: dentro dessas árvores, só sobe documento que existe na LDP. Não há regra por
// disciplina. Só os IDs das raízes são salvos.
import { useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, FolderLock, Loader2, Plus, Save, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ReportState } from "@/components/reports/ReportComponents";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import NavegadorPastasCompartilhado from "./NavegadorPastas";
import { carregarConfiguracao, salvarConfiguracao, type PastaProtegida } from "./uploadGovernadoApi";

export default function ConfiguracaoPastasDocumentosPage() {
  const { projetoAtual } = useProjetoAtual();
  const { can } = usePermissions();
  const podeEditar = can("settings.projects.edit");
  const [raizes, setRaizes] = useState<PastaProtegida[]>([]);
  const [salvas, setSalvas] = useState("[]");
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [aviso, setAviso] = useState<{ ok: boolean; texto: string } | null>(null);
  const [navegando, setNavegando] = useState(false);
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    if (!projetoAtual) return;
    const abort = new AbortController();
    setCarregando(true); setErro(null);
    carregarConfiguracao(projetoAtual.hubId, projetoAtual.projectId, abort.signal)
      .then((r) => { setRaizes(r.configuracao.raizes); setSalvas(JSON.stringify(r.configuracao.raizes)); })
      .catch((e) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Falha ao carregar a configuração."); })
      .finally(() => { if (!abort.signal.aborted) setCarregando(false); });
    return () => abort.abort();
  }, [projetoAtual?.hubId, projetoAtual?.projectId]);

  if (!projetoAtual) return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;
  if (carregando) return <ReportState title="Carregando…">Lendo a configuração do projeto.</ReportState>;
  if (erro) return <ReportState title={erro} error>Verifique o acesso ao projeto.</ReportState>;

  const alterado = JSON.stringify(raizes) !== salvas;
  const salvar = async () => {
    setSalvando(true); setAviso(null);
    try {
      const r = await salvarConfiguracao(projetoAtual.hubId, projetoAtual.projectId, raizes);
      setRaizes(r.configuracao.raizes); setSalvas(JSON.stringify(r.configuracao.raizes)); setAviso({ ok: true, texto: "Configuração salva." });
    } catch (e) { setAviso({ ok: false, texto: e instanceof Error ? e.message : "Falha ao salvar." }); }
    finally { setSalvando(false); }
  };

  return (
    <div className="space-y-4 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <FolderLock className="h-6 w-6 text-primary" />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Configuração de Pastas de Documentos</h1>
          <p className="text-sm text-muted-foreground">
            {projetoAtual.nome ?? projetoAtual.projectId} — dentro das pastas protegidas (e de todas as suas subpastas) só sobe documento que existe na LDP.
          </p>
        </div>
        {podeEditar && (
          <>
            <Button size="sm" variant="outline" className="gap-2" onClick={() => setNavegando(true)}><Plus className="h-4 w-4" />Adicionar pasta raiz</Button>
            <Button size="sm" className="gap-2" disabled={!alterado || salvando} onClick={() => void salvar()}>
              {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}Salvar
            </Button>
          </>
        )}
      </section>
      {aviso && (
        <p className={`flex items-center gap-2 text-sm ${aviso.ok ? "text-emerald-600 dark:text-emerald-400" : "text-destructive"}`}>
          {aviso.ok ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}{aviso.texto}
        </p>
      )}
      <section className="rounded-xl border border-border bg-card/60">
        <h2 className="border-b border-border px-4 py-2.5 text-[10px] font-black uppercase tracking-widest text-muted-foreground">Pastas protegidas ({raizes.length})</h2>
        {raizes.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted-foreground">Nenhuma pasta protegida — o Envio de Documentos não aplica a regra da LDP neste projeto.</p>
        ) : (
          <ul>
            {raizes.map((r) => (
              <li key={r.folderId} className="flex items-center gap-3 border-b border-border/50 px-4 py-2.5 last:border-b-0">
                <FolderLock className="h-4 w-4 flex-shrink-0 text-primary" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-bold">{r.caminho.split("/").pop()}</span>
                  <span className="block truncate text-[11px] text-muted-foreground" title={r.caminho}>{r.caminho} · inclui todas as subpastas</span>
                </span>
                {podeEditar && (
                  <Button size="sm" variant="ghost" className="h-7 gap-1 px-2 text-xs text-destructive hover:text-destructive"
                    onClick={() => setRaizes((rs) => rs.filter((x) => x.folderId !== r.folderId))}><Trash2 className="h-3.5 w-3.5" />Remover</Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
      {navegando && (
        <NavegadorPastasCompartilhado hubId={projetoAtual.hubId} projectId={projetoAtual.projectId} titulo="Adicionar pasta protegida" acao="Proteger esta pasta"
          onCancelar={() => setNavegando(false)}
          onEscolher={(folderId, caminho) => {
            setRaizes((rs) => (rs.some((x) => x.folderId === folderId) ? rs : [...rs, { folderId, caminho }]));
            setNavegando(false);
          }} />
      )}
    </div>
  );
}

/**
 * COMPATIBILIDADE: o navegador de pastas era exportado desta página e é usado por outra tela
 * (features/document-quality/AuditProjectConfiguration.tsx). Mantém a mesma assinatura, delegando ao NavegadorPastas compartilhado.
 */
export function NavegadorPastas({ hubId, projectId, disciplina, onEscolher, onCancelar }:
  { hubId: string; projectId: string; disciplina: string; onEscolher: (folderId: string, caminho: string) => void; onCancelar: () => void }) {
  return <NavegadorPastasCompartilhado hubId={hubId} projectId={projectId} titulo={`Pasta para ${disciplina || "(sem disciplina)"}`} acao="Usar esta pasta"
    onEscolher={onEscolher} onCancelar={onCancelar} />;
}
