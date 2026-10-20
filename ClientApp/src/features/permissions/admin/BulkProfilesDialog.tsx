// "Aplicar perfis" / "Aplicar em vários projetos": configurar → PRÉVIA (obrigatória, nada gravado) → aplicar → resultado.
import { useEffect, useMemo, useState, type ReactNode } from "react";
import toast from "react-hot-toast";
import { AlertTriangle, ArrowLeft, Eye, Layers, Loader2, Search } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { ProjetoRegistrado } from "@/lib/projectsRegistry";
import { BulkPreviewView, BulkResultadoView } from "./BulkPreviewView";
import { permissionsApi, type BulkModo, type BulkPreview, type BulkResultado, type PermProfile } from "./permissionsAdminApi";

export function ModoSelector({ modo, onChange, avisoSubstituir }: { modo: BulkModo; onChange: (m: BulkModo) => void; avisoSubstituir: string }) {
  const opcao = (m: BulkModo, titulo: string, texto: string) => (
    <label className={cn("flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2.5",
      modo === m ? (m === "REPLACE" ? "border-amber-500 bg-amber-500/10" : "border-primary bg-primary/10") : "border-border hover:bg-muted/40")}>
      <input type="radio" className="mt-1 accent-primary" checked={modo === m} onChange={() => onChange(m)} />
      <span><span className="block text-sm font-bold">{titulo}</span><span className="block text-xs text-muted-foreground">{texto}</span></span>
    </label>
  );
  return (
    <div className="space-y-2">
      <div className="grid gap-2 sm:grid-cols-2">
        {opcao("ADD", "Adicionar", "Mantém a configuração atual e acrescenta a selecionada.")}
        {opcao("REPLACE", "Substituir", "Deixa exatamente como selecionado neste(s) projeto(s).")}
      </div>
      {modo === "REPLACE" && (
        <p className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-500">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />{avisoSubstituir}
        </p>
      )}
    </div>
  );
}

export function SeletorProjetos({ projetos, selecionados, onChange, desabilitados }: {
  projetos: ProjetoRegistrado[]; selecionados: Set<string>; onChange: (s: Set<string>) => void; desabilitados?: Set<string>;
}) {
  const [busca, setBusca] = useState("");
  const visiveis = projetos.filter((p) => !desabilitados?.has(p.projectIdAcc) && p.nomeProjeto.toLowerCase().includes(busca.trim().toLowerCase()));
  const alternar = (id: string, v: boolean) => { const n = new Set(selecionados); if (v) n.add(id); else n.delete(id); onChange(n); };
  return (
    <div className="rounded-lg border border-border">
      <div className="flex items-center gap-2 border-b border-border px-3 py-2">
        <Search className="h-3.5 w-3.5 text-muted-foreground" />
        <input className="h-7 flex-1 bg-transparent text-sm outline-none" placeholder="Buscar projeto…" value={busca} onChange={(e) => setBusca(e.target.value)} />
        <button type="button" className="text-xs text-primary hover:underline" onClick={() => onChange(new Set([...selecionados, ...visiveis.map((p) => p.projectIdAcc)]))}>Marcar visíveis</button>
        <button type="button" className="text-xs text-muted-foreground hover:underline" onClick={() => onChange(new Set())}>Limpar</button>
      </div>
      <div className="grid max-h-48 gap-1 overflow-y-auto p-2 sm:grid-cols-2 custom-scrollbar">
        {visiveis.map((p) => (
          <label key={p.projectIdAcc} className={cn("flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted/40", selecionados.has(p.projectIdAcc) && "bg-primary/10")}>
            <input type="checkbox" className="h-4 w-4 accent-primary" checked={selecionados.has(p.projectIdAcc)} onChange={(e) => alternar(p.projectIdAcc, e.target.checked)} />
            <span className="truncate">{p.nomeProjeto}</span>
          </label>
        ))}
      </div>
    </div>
  );
}

export function Secao({ titulo, children, direita }: { titulo: string; children: ReactNode; direita?: ReactNode }) {
  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h4 className="text-[11px] font-black uppercase tracking-widest text-muted-foreground">{titulo}</h4>{direita}
      </div>
      {children}
    </section>
  );
}

interface Props {
  open: boolean;
  onClose: () => void;
  /** Usuários selecionados (id → nome) — a seleção vem da lista do projeto atual. */
  usuarios: { userId: number; nome: string }[];
  projetos: ProjetoRegistrado[];
  projetoAtual: string;
  /** false = só o projeto atual ("Aplicar perfis"); true = escolher vários projetos. */
  multiProjeto: boolean;
  perfis: PermProfile[];
  onConcluido: () => void;
}

type Passo = "configurar" | "previa" | "resultado";

export default function BulkProfilesDialog({ open, onClose, usuarios, projetos, projetoAtual, multiProjeto, perfis, onConcluido }: Props) {
  const [passo, setPasso] = useState<Passo>("configurar");
  const [projSel, setProjSel] = useState<Set<string>>(new Set());
  const [perfisSel, setPerfisSel] = useState<Set<number>>(new Set());
  const [modo, setModo] = useState<BulkModo>("ADD");
  const [previa, setPrevia] = useState<BulkPreview | null>(null);
  const [resultado, setResultado] = useState<BulkResultado | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setPasso("configurar"); setProjSel(new Set(projetoAtual ? [projetoAtual] : [])); setPerfisSel(new Set()); setModo("ADD");
    setPrevia(null); setResultado(null); setErro(null); setOcupado(false);
  }, [open, projetoAtual]);

  // MasterAdmin nunca aparece: é global/protegido e o backend recusa em lote.
  const perfisDisponiveis = perfis.filter((p) => p.ativo && !p.isMasterAdmin);
  const nomeProjeto = (id: string) => projetos.find((p) => p.projectIdAcc === id)?.nomeProjeto ?? id;
  const req = useMemo(() => ({ userIds: usuarios.map((u) => u.userId), projectIdsAcc: [...projSel], profileIds: [...perfisSel], mode: modo }),
    [usuarios, projSel, perfisSel, modo]);
  const valido = req.userIds.length > 0 && req.projectIdsAcc.length > 0 && (modo === "REPLACE" || req.profileIds.length > 0);
  const alterado = perfisSel.size > 0 || modo !== "ADD" || projSel.size !== (projetoAtual ? 1 : 0);

  const verPrevia = async () => {
    setOcupado(true); setErro(null);
    try { setPrevia(await permissionsApi.lotePerfisPrevia(req)); setPasso("previa"); }
    catch (e) { setErro(e instanceof Error ? e.message : "Falha ao gerar a prévia."); }
    finally { setOcupado(false); }
  };

  const aplicar = async () => {
    setOcupado(true); setErro(null);
    try {
      const r = await permissionsApi.lotePerfisAplicar(req);
      setResultado(r); setPasso("resultado");
      if (r.sucesso) { toast.success(`${r.alterados} acesso(s) alterado(s).`); onConcluido(); }
      else toast.error("Operação não aplicada: nada foi gravado.");
    } catch (e) { setErro(e instanceof Error ? e.message : "Falha ao aplicar."); }
    finally { setOcupado(false); }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !ocupado) onClose(); }}>
      <DialogContent className="flex max-h-[90dvh] max-w-[860px] flex-col gap-0 overflow-hidden p-0"
        onInteractOutside={(e) => { if (ocupado || (passo === "configurar" && alterado) || passo === "previa") e.preventDefault(); }}>
        <DialogHeader className="border-b border-border px-6 py-4 text-left">
          <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
            <Layers className="h-5 w-5 text-primary" />
            {passo === "previa" ? "Prévia da operação em lote" : passo === "resultado" ? "Resultado" : multiProjeto ? "Aplicar acesso em vários projetos" : "Aplicar perfis"}
          </DialogTitle>
          <DialogDescription>
            {usuarios.length} usuário{usuarios.length === 1 ? "" : "s"} selecionado{usuarios.length === 1 ? "" : "s"}
            {!multiProjeto && <> · projeto <strong className="text-foreground">{nomeProjeto(projetoAtual)}</strong></>}
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5 custom-scrollbar">
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-400">{erro}</p>}

          {passo === "configurar" && (
            <>
              <Secao titulo="Usuários selecionados">
                <div className="flex max-h-20 flex-wrap gap-1 overflow-y-auto">
                  {usuarios.map((u) => <span key={u.userId} className="rounded-full bg-muted px-2 py-0.5 text-xs">{u.nome}</span>)}
                </div>
              </Secao>
              {multiProjeto && (
                <Secao titulo={`Projetos (${projSel.size})`}>
                  <SeletorProjetos projetos={projetos} selecionados={projSel} onChange={setProjSel} />
                  <p className="text-[11px] text-muted-foreground">
                    Só recebem acesso as combinações em que o usuário tem vínculo ACC ativo no projeto; as demais aparecem como ignoradas na prévia.
                  </p>
                </Secao>
              )}
              <Secao titulo="Perfis">
                <div className="grid gap-2 sm:grid-cols-2">
                  {perfisDisponiveis.map((p) => {
                    const marcado = perfisSel.has(p.profileId);
                    return (
                      <label key={p.profileId} className={cn("flex cursor-pointer items-center gap-2.5 rounded-md border px-3 py-2 text-sm",
                        marcado ? "border-primary bg-primary/10" : "border-border hover:bg-muted/40")}>
                        <input type="checkbox" className="h-4 w-4 accent-primary" checked={marcado}
                          onChange={(e) => setPerfisSel((s) => { const n = new Set(s); if (e.target.checked) n.add(p.profileId); else n.delete(p.profileId); return n; })} />
                        <span className="flex-1 truncate">{p.name}</span>
                        <span className="text-[10px] text-muted-foreground">{p.permissions.length}</span>
                      </label>
                    );
                  })}
                </div>
                <p className="text-[11px] text-muted-foreground">MasterAdmin não aparece: é global e não pode ser atribuído em lote. Exceções ALLOW/DENY não são alteradas.</p>
              </Secao>
              <Secao titulo="Modo">
                <ModoSelector modo={modo} onChange={setModo}
                  avisoSubstituir={perfisSel.size === 0
                    ? "Sem perfis marcados, SUBSTITUIR remove TODOS os perfis destes usuários nos projetos escolhidos."
                    : "SUBSTITUIR remove os perfis atuais destes usuários nos projetos escolhidos que não estiverem marcados."} />
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
