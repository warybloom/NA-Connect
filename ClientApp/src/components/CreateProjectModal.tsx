import { useState, useEffect, useMemo } from "react";
import toast from 'react-hot-toast';
import { useNavigate } from "react-router-dom";

import {
  Check,
  ChevronsUpDown,
  Rocket,
  UploadCloud,
  FileSpreadsheet,
  X
} from "lucide-react";

import { Button } from "./ui/button";
import { Input } from "./ui/input";
import { Label } from "./ui/label";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "./ui/command";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from "./ui/dialog";
import { Popover, PopoverContent, PopoverTrigger } from "./ui/popover";
import { cn } from "../lib/utils";

// ✅ Interfaces simplificadas para bater com o seu arquivo original funcional
interface ProjectTemplate {
  id: string;
  name: string;
}

interface CreateProjectModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProjectCreated: () => void; // ✅ Removida a tipagem de retorno para bater com o funcional
  hubId?: string;
}

export const CreateProjectModal = ({ isOpen, onClose, onProjectCreated, hubId }: CreateProjectModalProps) => {
  const navigate = useNavigate();
  const [templates, setTemplates] = useState<ProjectTemplate[]>([]);
  const [templateId, setTemplateId] = useState('');
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(true);
  const [searchTerm, setSearchTerm] = useState('');
  const [openPopover, setOpenPopover] = useState(false);

  /**
   * 1. Busca de Templates
   * hubId é OBRIGATÓRIO no backend real do N&A (Areas/DataManagement/Controllers/DataController.cs:434 —
   * [FromQuery] string hubId, retorna 400 BadRequest se ausente/vazio). Mantido na query ao contrário do
   * comentário original do ForgeSync (que presumia o contrário para outro backend).
   *
   */
  useEffect(() => {
    if (isOpen) {
      const fetchTemplates = async () => {
        setIsLoadingTemplates(true);
        try {
          const response = await fetch(`/DataManagement/Data/GetProjectTemplates?hubId=${hubId}`, {
            credentials: 'include'
          });

          if (response.status === 401) { navigate('/login'); return; }

          const data = await response.json();
          setTemplates(data || []);
        } catch (err) {
          toast.error("Falha ao carregar os templates do servidor.");
        } finally {
          setIsLoadingTemplates(false);
        }
      };
      fetchTemplates();
    }
  }, [isOpen, hubId, navigate]);

  /**
   * 2. Envio do Formulário (Lógica Principal)
   * Mantém exatamente as chaves do FormData do seu arquivo funcional
   *
   */
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!hubId) { toast.error("ID da Conta não encontrado."); return; }
    if (!selectedFile) { toast.error("Selecione a planilha do projeto."); return; }
    if (!templateId) { toast.error("Selecione um template."); return; }

    setIsSubmitting(true);
    const toastId = toast.loading("Provisionando projeto no ACC...");

    // ✅ FORMDATA IDENTICO AO FUNCIONAL
    const submissionData = new FormData();
    submissionData.append('hubId', hubId);
    submissionData.append('templateId', templateId);
    submissionData.append('memberSheet', selectedFile);

    try {
      const response = await fetch('/DataManagement/Data/CreateProjectFromTemplate', {
        method: 'POST',
        body: submissionData,
        credentials: 'include'
      });

      if (response.status === 401) { navigate('/login'); return; }

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.details || errorData.message || 'Erro ao criar o projeto.');
      }

      toast.success("Projeto criado e membros importados!", { id: toastId });
      onProjectCreated(); // ✅ Chamada sem argumentos como no original
      handleClose();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Erro inesperado.";
      toast.error(`Falha: ${msg}`, { id: toastId });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleClose = () => {
    setTemplateId('');
    setSelectedFile(null);
    onClose();
  };

  const filteredTemplates = useMemo(() => {
    return templates.filter(t => t.name.toLowerCase().includes(searchTerm.toLowerCase()));
  }, [templates, searchTerm]);

  return (
    <Dialog open={isOpen} onOpenChange={(open: boolean) => !open && handleClose()}>
      <DialogContent className="sm:max-w-[500px] glass-card p-0 overflow-hidden border-primary/20 shadow-2xl">

        <div className="bg-primary/10 px-6 py-6 border-b border-primary/20 flex items-center gap-4">
          <div className="w-12 h-12 rounded-xl bg-primary flex items-center justify-center shadow-lg shadow-primary/20">
            <Rocket className="w-6 h-6 text-white" />
          </div>
          <div>
            <DialogTitle className="text-xl font-black italic uppercase tracking-tighter">Provisionar Projeto</DialogTitle>
            <DialogDescription className="text-primary/70 font-bold uppercase text-[10px] tracking-widest">Forges Sync Connect • Automação</DialogDescription>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          <div className="space-y-5">

            <div className="space-y-2">
              <Label className="text-[10px] font-black uppercase tracking-widest text-muted-foreground ml-1">1. Template de Estrutura</Label>
              <Popover open={openPopover} onOpenChange={setOpenPopover}>
                <PopoverTrigger asChild>
                  <Button variant="outline" className="w-full justify-between bg-background/50 border-border/50 h-12 font-medium" disabled={isLoadingTemplates || isSubmitting}>
                    <div className="flex items-center gap-2 truncate text-xs font-bold uppercase tracking-tight">
                      {templateId ? (
                        <>
                          <Check className="w-4 h-4 text-primary" />
                          {templates.find(t => t.id === templateId)?.name}
                        </>
                      ) : isLoadingTemplates ? "A carregar..." : "Selecione um projeto modelo..."}
                    </div>
                    <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="w-[450px] p-0 glass-card border-border/50 shadow-2xl" align="start">
                  <Command className="bg-transparent">
                    <CommandInput placeholder="Filtrar modelos de projeto..." onValueChange={setSearchTerm} className="font-bold text-xs uppercase" />
                    <CommandList className="max-h-[300px]">
                      <CommandEmpty className="py-4 text-xs font-bold text-muted-foreground uppercase text-center tracking-widest">Nenhum modelo encontrado.</CommandEmpty>
                      <CommandGroup>
                        {filteredTemplates.map((t) => (
                          <CommandItem key={t.id} onSelect={() => { setTemplateId(t.id); setOpenPopover(false); }} className="py-3 cursor-pointer font-bold text-[10px] uppercase tracking-wider hover:bg-primary/10 transition-colors">
                            <Check className={cn("mr-2 h-4 w-4", templateId === t.id ? "opacity-100" : "opacity-0")} />
                            {t.name}
                          </CommandItem>
                        ))}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
            </div>

            <div className="space-y-2">
              <Label className="text-[10px] font-black uppercase tracking-widest text-muted-foreground ml-1">2. Planilha de Membros (LDP)</Label>
              <div className={cn("group relative border-2 border-dashed rounded-xl p-8 transition-all flex flex-col items-center justify-center gap-3 cursor-pointer", selectedFile ? "border-primary/50 bg-primary/5" : "border-border/60 hover:border-primary/40 hover:bg-muted/30")}>
                <Input type="file" onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSelectedFile(e.target.files?.[0] || null)} className="absolute inset-0 opacity-0 cursor-pointer z-10" accept=".xlsx,.xls,.csv" disabled={isSubmitting} />

                {selectedFile ? (
                  <>
                    <div className="w-12 h-12 rounded-full bg-primary/20 flex items-center justify-center animate-bounce"><FileSpreadsheet className="w-6 h-6 text-primary" /></div>
                    <div className="text-center">
                      <p className="text-xs font-black uppercase tracking-tighter text-foreground">{selectedFile.name}</p>
                      <p className="text-[9px] text-muted-foreground font-bold uppercase tracking-widest mt-1">Pronto para provisionar</p>
                    </div>
                  </>
                ) : (
                  <>
                    <UploadCloud className="w-10 h-10 text-muted-foreground group-hover:text-primary transition-colors" />
                    <div className="text-center">
                      <p className="text-[10px] font-black uppercase tracking-widest">Clique para anexar a LDP</p>
                      <p className="text-[9px] text-muted-foreground mt-1 uppercase font-bold opacity-60">Arraste seu arquivo .xlsx aqui</p>
                    </div>
                  </>
                )}
              </div>
            </div>
          </div>

          <DialogFooter className="gap-2 sm:gap-0 mt-4 border-t border-border/40 pt-6">
            <Button type="button" variant="ghost" onClick={handleClose} disabled={isSubmitting} className="font-black text-[10px] tracking-widest uppercase">CANCELAR</Button>
            <Button type="submit" disabled={isSubmitting || !templateId || !selectedFile} className="px-8 font-black text-[10px] tracking-[0.2em] uppercase shadow-lg shadow-primary/20 min-w-[160px]">
              {isSubmitting ? "PROCESSANDO..." : "CRIAR PROJETO"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
};
