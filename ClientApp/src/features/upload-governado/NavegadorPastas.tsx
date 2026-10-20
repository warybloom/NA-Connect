// Navegação de pastas do ACC a partir de "Project Files" (usada para escolher pastas-raiz protegidas e o destino do envio).
import { useEffect, useState } from "react";
import { ChevronRight, Folder, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { listarPastas } from "./uploadGovernadoApi";

export default function NavegadorPastas({ hubId, projectId, titulo, acao, onEscolher, onCancelar }: {
  hubId: string; projectId: string; titulo: string; acao: string;
  onEscolher: (folderId: string, caminho: string) => void; onCancelar: () => void;
}) {
  const [trilha, setTrilha] = useState<{ id: string; nome: string }[]>([]);
  const [pastas, setPastas] = useState<{ id: string; nome: string }[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const atual = trilha[trilha.length - 1];

  useEffect(() => {
    const abort = new AbortController();
    setCarregando(true); setErro(null);
    listarPastas(hubId, projectId, atual?.id, abort.signal)
      .then((r) => { setPastas(r.pastas); if (!atual) setTrilha([{ id: r.folderId, nome: "Project Files" }]); })
      .catch((e) => { if (!abort.signal.aborted) setErro(e instanceof Error ? e.message : "Falha ao listar pastas."); })
      .finally(() => { if (!abort.signal.aborted) setCarregando(false); });
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [atual?.id]);

  const caminho = trilha.map((t) => t.nome).join("/");
  return (
    <Dialog open onOpenChange={(v) => { if (!v) onCancelar(); }}>
      <DialogContent className="max-w-xl">
        <DialogHeader><DialogTitle>{titulo}</DialogTitle></DialogHeader>
        <div className="flex flex-wrap items-center gap-1 text-xs">
          {trilha.map((t, i) => (
            <span key={t.id} className="flex items-center gap-1">
              {i > 0 && <ChevronRight className="h-3 w-3 text-muted-foreground" />}
              <button type="button" className="hover:underline" onClick={() => setTrilha((tr) => tr.slice(0, i + 1))}>{t.nome}</button>
            </span>
          ))}
        </div>
        <div className="max-h-80 min-h-[160px] overflow-y-auto rounded-md border border-border">
          {carregando ? <p className="flex items-center gap-2 p-4 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando…</p>
            : erro ? <p className="p-4 text-sm text-destructive">{erro}</p>
            : pastas.length === 0 ? <p className="p-4 text-sm text-muted-foreground">Sem subpastas.</p>
            : pastas.map((p) => (
              <button key={p.id} type="button" onClick={() => setTrilha((tr) => [...tr, p])}
                className="flex w-full items-center gap-2 border-b border-border/50 px-3 py-2 text-left text-sm hover:bg-accent">
                <Folder className="h-4 w-4 text-primary" />{p.nome}
              </button>
            ))}
        </div>
        <div className="flex items-center justify-between gap-2">
          <span className="truncate text-xs text-muted-foreground" title={caminho}>{caminho}</span>
          <span className="flex gap-2">
            <Button variant="ghost" size="sm" onClick={onCancelar}>Cancelar</Button>
            <Button size="sm" disabled={!atual || trilha.length < 2} title={trilha.length < 2 ? "Escolha uma pasta dentro de Project Files" : undefined}
              onClick={() => atual && onEscolher(atual.id, caminho)}>{acao}</Button>
          </span>
        </div>
      </DialogContent>
    </Dialog>
  );
}
