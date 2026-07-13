// Caminho: ClientApp/src/components/SyncResultModal.tsx (VERSÃO COM FILTROS INTERATIVOS)
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
import { FileWarning, CheckCircle2, List } from "lucide-react";
import { cn } from "@/lib/utils";

// Interface atualizada para corresponder ao backend
interface SyncResult {
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

// MUDANÇA: Novo tipo para controlar o estado do filtro
type FilterType = 'updated' | 'notFound' | 'all';

export function SyncResultModal({ isOpen, onClose, result }: SyncResultModalProps) {
  // MUDANÇA: Estado para gerir o filtro ativo. Começa por mostrar os ficheiros não encontrados.
  const [activeFilter, setActiveFilter] = useState<FilterType>('notFound');

  // MUDANÇA: Hook useMemo para determinar qual lista mostrar com base no filtro
  const displayedItems = useMemo(() => {
    if (!result) return [];
    switch (activeFilter) {
      case 'updated':
        return result.filesUpdatedSuccess.map(file => ({ name: file, status: 'updated' as const }));
      case 'notFound':
        return result.filesNotFound.map(file => ({ name: file, status: 'notFound' as const }));
      case 'all':
        return result.allLdpFiles.map(file => ({
          name: file,
          status: result.filesUpdatedSuccess.includes(file) ? 'updated' : 'notFound',
        }));
      default:
        return [];
    }
  }, [activeFilter, result]);

  if (!result) return null;

  const filterButtonClasses = (filterType: FilterType) => cn(
    "cursor-pointer rounded-full border px-3 py-1 text-xs font-semibold transition-colors focus:outline-none",
    activeFilter === filterType 
      ? "ring-2 ring-offset-2 ring-offset-background" // Estilo do filtro ativo
      : "hover:bg-muted/50" // Estilo para hover
  );

  const getHeader = () => {
    switch (activeFilter) {
      case 'updated':
        return (
          <h3 className="font-semibold mb-2 flex items-center text-green-500">
            <CheckCircle2 className="w-5 h-5 mr-2" />
            Ficheiros Atualizados ({result.filesUpdatedSuccess.length})
          </h3>
        );
      case 'notFound':
        return (
          <h3 className="font-semibold mb-2 flex items-center text-destructive">
            <FileWarning className="w-5 h-5 mr-2" />
            Ficheiros Não Encontrados ({result.filesNotFound.length})
          </h3>
        );
      case 'all':
        return (
          <h3 className="font-semibold mb-2 flex items-center text-primary">
            <List className="w-5 h-5 mr-2" />
            Total de Ficheiros na LDP ({result.allLdpFiles.length})
          </h3>
        );
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-2xl flex flex-col max-h-[90vh]">
        <DialogHeader>
          <DialogTitle>Relatório de Sincronização da LDP</DialogTitle>
          <DialogDescription>{result.summaryMessage}</DialogDescription>
        </DialogHeader>
        
        {/* MUDANÇA: Área de conteúdo com cabeçalho e lista dinâmicos */}
        <div className="my-4 flex-grow overflow-y-auto">
          {getHeader()}
          <ScrollArea className="h-80 w-full rounded-md border p-2">
            {displayedItems.length > 0 ? (
              <div className="flex flex-col gap-2 p-2">
                {displayedItems.map(({ name, status }, index) => (
                  <div key={index} className="text-sm text-muted-foreground bg-muted/50 p-2 rounded flex items-center justify-between">
                    <span>{name}</span>
                    {activeFilter === 'all' && (
                      status === 'updated' 
                        ? <CheckCircle2 className="w-4 h-4 text-green-500" /> 
                        : <FileWarning className="w-4 h-4 text-destructive" />
                    )}
                  </div>
                ))}
              </div>
            ) : (
              <div className="flex items-center justify-center h-full text-muted-foreground">
                Nenhum item para exibir neste filtro.
              </div>
            )}
          </ScrollArea>
        </div>

        <DialogFooter>
          <div className="flex w-full justify-between items-center">
            {/* MUDANÇA: Os emblemas agora são botões de filtro */}
            <div className="flex items-center gap-2">
              <div onClick={() => setActiveFilter('all')} className={cn(filterButtonClasses('all'), "border-blue-500/50", activeFilter === 'all' && "bg-blue-900/50 text-blue-300 ring-blue-500")}>
                Total na LDP: {result.totalLdpFiles}
              </div>
              <div onClick={() => setActiveFilter('updated')} className={cn(filterButtonClasses('updated'), "border-green-700", activeFilter === 'updated' && "bg-green-900/50 text-green-300 ring-green-600")}>
                Atualizados: {result.filesUpdated}
              </div>
              <div onClick={() => setActiveFilter('notFound')} className={cn(filterButtonClasses('notFound'), "border-destructive", activeFilter === 'notFound' && "bg-destructive/30 text-red-300 ring-destructive")}>
                Não Encontrados: {result.filesNotFound.length}
              </div>
            </div>
             <Button onClick={onClose}>Fechar</Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}