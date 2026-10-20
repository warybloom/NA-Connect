// Modal de NOVO perfil (e de duplicar): nome obrigatório, descrição opcional, erro dentro do modal, loading no botão.
// ESC/X fecham; clicar fora só fecha sem alterações pendentes; Enter no nome salva quando válido.
import { useEffect, useState, type FormEvent } from "react";
import { Loader2, ShieldPlus } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

interface Props {
  open: boolean;
  modo: "criar" | "duplicar";
  /** Duplicar: nome sugerido e descrição do perfil de origem. */
  nomeInicial?: string;
  descricaoInicial?: string;
  onClose: () => void;
  /** Lança Error com a mensagem do backend em caso de falha (exibida no modal). */
  onConfirmar: (nome: string, descricao: string | null) => Promise<void>;
}

export default function PerfilDialog({ open, modo, nomeInicial = "", descricaoInicial = "", onClose, onConfirmar }: Props) {
  const [nome, setNome] = useState(nomeInicial);
  const [descricao, setDescricao] = useState(descricaoInicial);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    if (open) { setNome(nomeInicial); setDescricao(descricaoInicial); setErro(null); setSalvando(false); }
  }, [open, nomeInicial, descricaoInicial]);

  const alterado = nome !== nomeInicial || descricao !== descricaoInicial;
  const valido = nome.trim().length > 0 && nome.trim().length <= 100 && descricao.length <= 500;

  const enviar = async (e?: FormEvent) => {
    e?.preventDefault();
    if (!valido || salvando) return;
    setSalvando(true);
    setErro(null);
    try {
      await onConfirmar(nome.trim(), descricao.trim() || null);
    } catch (ex) {
      setErro(ex instanceof Error ? ex.message : "Não foi possível salvar o perfil.");
    } finally {
      setSalvando(false);
    }
  };

  const criar = modo === "criar";
  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !salvando) onClose(); }}>
      <DialogContent
        className="max-w-[560px] gap-0 p-0"
        onInteractOutside={(e) => { if (alterado || salvando) e.preventDefault(); }}
        onEscapeKeyDown={(e) => { if (salvando) e.preventDefault(); }}
      >
        <form onSubmit={enviar}>
          <DialogHeader className="border-b border-border px-6 py-5 text-left">
            <DialogTitle className="flex items-center gap-2 text-base font-black uppercase tracking-widest">
              <ShieldPlus className="h-5 w-5 text-primary" />
              {criar ? "Novo perfil" : "Duplicar perfil"}
            </DialogTitle>
            <DialogDescription>
              {criar ? "Configure um perfil de acesso ao N&A Connect." : "A cópia recebe a descrição e as mesmas permissões do perfil de origem."}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-5 px-6 py-6">
            <div className="space-y-2">
              <Label htmlFor="perfil-nome">Nome do perfil <span className="text-primary">*</span></Label>
              <Input id="perfil-nome" autoFocus value={nome} maxLength={100} placeholder="Ex.: Engenharia, Planejamento, Cliente…"
                onChange={(e) => setNome(e.target.value)} disabled={salvando} />
            </div>
            {criar && <div className="space-y-2">
              <Label htmlFor="perfil-descricao">Descrição <span className="text-xs font-normal text-muted-foreground">(opcional)</span></Label>
              <Textarea id="perfil-descricao" rows={3} value={descricao} maxLength={500} placeholder="Para quem é este perfil e o que ele permite."
                onChange={(e) => setDescricao(e.target.value)} disabled={salvando} className="resize-none" />
            </div>}
            {erro && (
              <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-400">{erro}</p>
            )}
          </div>

          <DialogFooter className="gap-2 border-t border-border px-6 py-4">
            <Button type="button" variant="outline" onClick={onClose} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={!valido || salvando} className="min-w-[140px] gap-2">
              {salvando && <Loader2 className="h-4 w-4 animate-spin" />}
              {criar ? "Criar perfil" : "Duplicar perfil"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
