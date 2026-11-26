// "Copiar acesso para outros projetos": configuração do usuário no projeto de ORIGEM → vários destinos.
// Perfis (padrão marcado) e exceções ALLOW/DENY (padrão desmarcado). Prévia obrigatória antes de gravar.
import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, ArrowLeft, Copy, Eye, Loader2 } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import type { ProjetoRegistrado } from "@/lib/projectsRegistry";
import { BulkPreviewView, BulkResultadoView } from "./BulkPreviewView";
import { ModoSelector, Secao, SeletorProjetos } from "./BulkProfilesDialog";
import { permissionsApi, type BulkModo, type BulkPreview, type BulkResultado } from "./permissionsAdminApi";

interface Props {
  open: boolean;
  onClose: () => void;
  userId: number;
  usuarioNome: string;
  origem: { projectIdAcc: string; nome: string };
  /** Configuração atual na origem (exibição). */
  perfisOrigem: string[];
  overridesOrigem: number;
  projetos: ProjetoRegistrado[];
  onConcluido: () => void;
}

type Passo = "configurar" | "previa" | "resultado";

export default function CopyAccessDialog({ open, onClose, userId, usuarioNome, origem, perfisOrigem, overridesOrigem, projetos, onConcluido }: Props) {
  const [passo, setPasso] = useState<Passo>("configurar");
  const [destinos, setDestinos] = useState<Set<string>>(new Set());
  const [copiarPerfis, setCopiarPerfis] = useState(true);
  const [copiarOverrides, setCopiarOverrides] = useState(false);
  const [modo, setModo] = useState<BulkModo>("ADD");
  const [previa, setPrevia] = useState<BulkPreview | null>(null);
  const [resultado, setResultado] = useState<BulkResultado | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setPasso("configurar"); setDestinos(new Set()); setCopiarPerfis(true); setCopiarOverrides(false); setModo("ADD");
    setPrevia(null); setResultado(null); setErro(null); setOcupado(false);
  }, [open]);

  const req = useMemo(() => ({ userId, sourceProjectIdAcc: origem.projectIdAcc, targetProjectIdsAcc: [...destinos],
    copyProfiles: copiarPerfis, copyOverrides: copiarOverrides, mode: modo }), [userId, origem.projectIdAcc, destinos, copiarPerfis, copiarOverrides, modo]);
  const valido = destinos.size > 0 && (copiarPerfis || copiarOverrides);

  const verPrevia = async () => {
    setOcupado(true); setErro(null);
    try { setPrevia(await permissionsApi.loteCopiaPrevia(req)); setPasso("previa"); }
    catch (e) { setErro(e instanceof Error ? e.message : "Falha ao gerar a prévia."); }
    finally { setOcupado(false); }
  };

  const aplicar = async () => {
    setOcupado(true); setErro(null);
    try {
      const r = await permissionsApi.loteCopiaAplicar(req);
      setResultado(r); setPasso("resultado");
      if (r.sucesso) { toast.success(`Acesso copiado para ${r.alterados} projeto(s).`); onConcluido(); }
      else toast.error("Cópia não aplicada: nada foi gravado.");
    } catch (e) { setErro(e instanceof Error ? e.message : "Falha ao aplicar."); }
    finally { setOcupado(false); }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !ocupado) onClose(); }}>
      <DialogContent className="flex max-h-[90dvh] max-w-[860px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (ocupado || passo !== "resultado") e.preventDefault(); }}>
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Copy className="h-5 w-5 text-primary" />
            {passo === "previa" ? "Prévia da cópia de acesso" : passo === "resultado" ? "Resultado" : "Copiar acesso para outros projetos"}
          </DialogTitle>
          <DialogDescription>
            <strong className="text-foreground">{usuarioNome}</strong> · origem <strong className="text-foreground">{origem.nome}</strong>
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5 custom-scrollbar">
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-400">{erro}</p>}

          {passo === "configurar" && (
            <>
              <Secao titulo="Configuração atual na origem">
                <div className="flex flex-wrap items-center gap-1 rounded-lg border border-border bg-background/40 px-3 py-2">
                  {perfisOrigem.length
                    ? perfisOrigem.map((p) => <span key={p} className="rounded-full bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary">{p}</span>)
                    : <span className="text-xs text-muted-foreground">Sem perfis neste projeto</span>}
                  <span className="ml-auto text-xs text-muted-foreground">{overridesOrigem} exceç{overridesOrigem === 1 ? "ão" : "ões"} individua{overridesOrigem === 1 ? "l" : "is"}</span>
                </div>
              </Secao>
              <Secao titulo={`Projetos de destino (${destinos.size})`}>
                <SeletorProjetos projetos={projetos} selecionados={destinos} onChange={setDestinos} desabilitados={new Set([origem.projectIdAcc])} />
                <p className="text-[11px] text-muted-foreground">
                  Só recebe a configuração o projeto em que {usuarioNome} tem vínculo ACC ativo. Nenhum acesso novo é criado; os demais aparecem como ignorados.
                </p>
              </Secao>
              <Secao titulo="Copiar">
                <div className="grid gap-2 sm:grid-cols-2">
                  <label className="flex cursor-pointer items-center gap-2.5 rounded-md border border-border px-3 py-2 text-sm hover:bg-muted/40">
                    <input type="checkbox" className="h-4 w-4 accent-primary" checked={copiarPerfis} onChange={(e) => setCopiarPerfis(e.target.checked)} />Perfis
                  </label>
                  <label className="flex cursor-pointer items-center gap-2.5 rounded-md border border-border px-3 py-2 text-sm hover:bg-muted/40">
                    <input type="checkbox" className="h-4 w-4 accent-primary" checked={copiarOverrides} onChange={(e) => setCopiarOverrides(e.target.checked)} />Exceções individuais ALLOW/DENY
                  </label>
                </div>
                {copiarOverrides && (
                  <p className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-500">
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                    As exceções ALLOW/DENY do projeto de origem serão replicadas nos destinos. Em ADICIONAR, uma exceção que o destino já tem para a mesma permissão é mantida.
                  </p>
                )}
              </Secao>
              <Secao titulo="Modo">
                <ModoSelector modo={modo} onChange={setModo}
                  avisoSubstituir={`SUBSTITUIR deixa os destinos iguais à origem: ${copiarPerfis ? "perfis" : ""}${copiarPerfis && copiarOverrides ? " e " : ""}${copiarOverrides ? "exceções" : ""} que não existem na origem serão removidos.`} />
              </Secao>
            </>
          )}

          {passo === "previa" && previa && <BulkPreviewView p={previa} />}
          {passo === "resultado" && resultado && <BulkResultadoView r={resultado} />}
        </div>

        <DialogFooter className="gap-2 border-t border-border px-6 py-3">
          {passo === "configurar" && (
            <>
              <Button variant="outline" onClick={onClose} disabled={ocupado}>Cancelar</Button>
              <Button onClick={verPrevia} disabled={!valido || ocupado} className="min-w-[150px] gap-2">
                {ocupado ? <Loader2 className="h-4 w-4 animate-spin" /> : <Eye className="h-4 w-4" />}Ver prévia
              </Button>
            </>
          )}
          {passo === "previa" && (
            <>
              <Button variant="outline" onClick={() => setPasso("configurar")} disabled={ocupado} className="gap-1.5"><ArrowLeft className="h-4 w-4" />Voltar</Button>
              <Button variant="outline" onClick={onClose} disabled={ocupado}>Cancelar</Button>
              <Button onClick={aplicar} disabled={ocupado || !previa || previa.alterar === 0} className="min-w-[170px] gap-2">
                {ocupado && <Loader2 className="h-4 w-4 animate-spin" />}
                {previa && previa.alterar === 0 ? "Nada a alterar" : "Aplicar alterações"}
              </Button>
            </>
          )}
          {passo === "resultado" && <Button onClick={onClose}>Fechar</Button>}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
