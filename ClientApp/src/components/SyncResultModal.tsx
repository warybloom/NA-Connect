import { useState, useMemo } from "react";
import { 
  Dialog, 
  DialogContent, 
  DialogHeader, 
  DialogTitle, 
  DialogDescription, 
  DialogFooter 
} from "@/components/ui/dialog"; 
import { ScrollArea } from "@/components/ui/scroll-area";
import { Button } from "@/components/ui/button";
import { FileWarning, CheckCircle2 } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Interface recuperada do seu arquivo funcional.
 * Sincronizada com o retorno da Action 'SyncFromProjectLdp' no C#.
 */
export interface SyncResult {
  totalLdpFiles: number;
  filesUpdated: number;
  filesNotFound: string[];
  filesUpdatedSuccess: string[];
  allLdpFiles: string[];
  summaryMessage: string;
}

interface SyncResultModalProps {
  isOpen: boolean;
  onClose: () => void;
  result: SyncResult | null;
}

type FilterType = 'updated' | 'notFound' | 'all';

export function SyncResultModal({ isOpen, onClose, result }: SyncResultModalProps) {
  // ✅ LÓGICA PRESERVADA: Inicia mostrando as inconsistências
  const [activeFilter, setActiveFilter] = useState<FilterType>('notFound');

  /**
   * ✅ PROCESSAMENTO DE DADOS: Mantém exatamente a mesma regra de filtragem anterior
   */
  const displayedItems = useMemo(() => {
    if (!result) return [];
    switch (activeFilter) {
      case 'updated': 
        return (result.filesUpdatedSuccess || []).map(f => ({ name: f, status: 'updated' as const }));
      case 'notFound': 
        return (result.filesNotFound || []).map(f => ({ name: f, status: 'notFound' as const }));
      case 'all': 
        return (result.allLdpFiles || []).map(f => ({ 
          name: f, 
          status: (result.filesUpdatedSuccess || []).includes(f) ? ('updated' as const) : ('notFound' as const) 
        }));
      default:
        return [];
    }
  }, [activeFilter, result]);

  if (!result) return null;

  return (
    <Dialog 
      open={isOpen} 
      onOpenChange={(open: boolean) => !open && onClose()}
    >
      <DialogContent className="sm:max-w-2xl flex flex-col max-h-[90vh] glass-card border-border/50 shadow-2xl">
        <DialogHeader>
          <DialogTitle className="text-xl font-black italic uppercase tracking-tighter flex items-center gap-2">
            Relatório de Sincronização LDP
          </DialogTitle>
          <DialogDescription className="text-muted-foreground font-medium italic">
            {result.summaryMessage}
          </DialogDescription>
        </DialogHeader>
        
        <div className="my-4 flex-grow overflow-y-auto">
          <p className="text-[10px] font-black text-muted-foreground uppercase tracking-[0.2em] mb-3 px-1 opacity-70">
            Filtro Ativo: {activeFilter === 'all' ? 'Vista Geral' : activeFilter === 'updated' ? 'Sucesso de Gravação' : 'Documentos Ausentes'}
          </p>
          
          <ScrollArea className="h-80 w-full rounded-md border border-border/50 bg-background/40 p-2 custom-scrollbar">
            {displayedItems.length > 0 ? (
              displayedItems.map(({ name, status }, i) => (
                <div key={`${name}-${i}`} className="text-[11px] font-bold text-muted-foreground bg-muted/20 p-2.5 rounded mb-1 flex items-center justify-between border-l-2 border-transparent hover:border-primary/40 transition-all group">
                  <span className="truncate mr-4 font-mono">{name}</span>
                  
                  {status === 'updated' ? (
                    <span title="Sincronizado com o Banco de Dados">
                      <CheckCircle2 className="w-3.5 h-3.5 text-green-500 shrink-0" />
                    </span>
                  ) : (
                    <span title="Faltante no diretório do ACC ou LDP">
                      <FileWarning className="w-3.5 h-3.5 text-destructive shrink-0 animate-pulse" />
                    </span>
                  )}
                </div>
              ))
            ) : (
              <div className="flex flex-col items-center justify-center py-20 opacity-30 border-2 border-dashed border-border/40 rounded-lg">
                <p className="text-xs font-black uppercase tracking-widest">Lista Vazia</p>
              </div>
            )}
          </ScrollArea>
        </div>

        <DialogFooter className="flex-col sm:flex-row gap-4 border-t border-border/40 pt-6">
          <div className="flex flex-wrap items-center gap-2 flex-grow">
            {/* Botões de Filtro com Estética Forges Sync Connect */}
            <button 
              type="button"
              title="Mostrar todos"
              onClick={() => setActiveFilter('all')} 
              className={cn(
                "text-[9px] font-black uppercase tracking-widest px-3 py-1.5 rounded-md border transition-all", 
                activeFilter === 'all' ? "bg-primary/20 text-primary border-primary/50 shadow-lg shadow-primary/10" : "border-border text-muted-foreground hover:bg-accent"
              )}
            >
              TOTAL: {result.totalLdpFiles}
            </button>
            <button 
              type="button"
              title="Mostrar sincronizados"
              onClick={() => setActiveFilter('updated')} 
              className={cn(
                "text-[9px] font-black uppercase tracking-widest px-3 py-1.5 rounded-md border transition-all", 
                activeFilter === 'updated' ? "bg-green-500/20 text-green-400 border-green-500/50" : "border-border text-muted-foreground hover:bg-accent"
              )}
            >
              SUCESSO: {result.filesUpdated}
            </button>
            <button 
              type="button"
              title="Mostrar não encontrados"
              onClick={() => setActiveFilter('notFound')} 
              className={cn(
                "text-[9px] font-black uppercase tracking-widest px-3 py-1.5 rounded-md border transition-all", 
                activeFilter === 'notFound' ? "bg-destructive/20 text-red-400 border-destructive/50" : "border-border text-muted-foreground hover:bg-accent"
              )}
            >
              FALTANTES: {result.filesNotFound?.length || 0}
            </button>
          </div>
          <Button onClick={onClose} className="w-full sm:w-40 font-black text-[10px] tracking-[0.2em] shadow-lg shadow-primary/20 uppercase">
            CONCLUIR SINC
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export default SyncResultModal;