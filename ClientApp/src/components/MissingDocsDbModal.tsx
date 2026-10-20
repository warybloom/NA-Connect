import { 
  Dialog, 
  DialogContent, 
  DialogHeader, 
  DialogTitle, 
  DialogDescription, 
  DialogFooter 
} from "./ui/dialog"; // ✅ Caminho corrigido para relativo
import { Button } from "./ui/button";
import { ScrollArea } from "./ui/scroll-area";
import { FileWarning } from "lucide-react";
import { cn } from "../lib/utils";

/**
 * Interface para a listagem de inconsistências entre a LDP e o Banco de Dados.
 * Utilizada após a execução do método UpdateLdpFromDatabase.
 */
interface MissingDocsDbModalProps {
  isOpen: boolean;
  onClose: () => void;
  documents: string[];
}

export function MissingDocsDbModal({ isOpen, onClose, documents }: MissingDocsDbModalProps) {
  return (
    <Dialog 
      open={isOpen} 
      // ✅ CORREÇÃO: Tipagem explícita 'boolean' para evitar o erro 'Unexpected any'
      onOpenChange={(open: boolean) => !open && onClose()}
    >
      {/* Aplicada a classe glass-card para manter o padrão visual do Dashboard e Files */}
      <DialogContent className="sm:max-w-[600px] flex flex-col max-h-[85vh] glass-card border-border/50">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-xl font-bold">
            <FileWarning className="w-5 h-5 text-destructive" />
            Inconsistência de Dados (Banco)
          </DialogTitle>
          <DialogDescription className="text-muted-foreground">
            Os seguintes documentos listados na LDP do projeto não possuem registro correspondente no banco de dados corporativo:
          </DialogDescription>
        </DialogHeader>

        

        <ScrollArea className="h-auto max-h-[50vh] w-full rounded-md border border-border/50 bg-background/40 p-4 my-4 flex-grow custom-scrollbar">
          {documents && documents.length > 0 ? (
            <ul className="space-y-2">
              {/* Mapeamento seguro das strings recebidas do DataController */}
              {documents.map((doc, i) => (
                <li 
                  key={`${doc}-${i}`} 
                  className="text-sm text-muted-foreground bg-muted/20 px-3 py-2 rounded border-l-2 border-primary/50 transition-colors hover:bg-muted/30"
                >
                  <span className="font-mono text-[11px] opacity-70 mr-2">DOC:</span>
                  {doc}
                </li>
              ))}
            </ul>
          ) : (
            <div className="flex flex-col items-center justify-center py-10 opacity-50">
              <p className="text-sm text-muted-foreground font-medium">Nenhuma inconsistência encontrada.</p>
            </div>
          )}
        </ScrollArea>

        <DialogFooter className="gap-2">
          <p className="mr-auto text-[10px] text-muted-foreground/50 uppercase tracking-widest hidden sm:block">
            Forges Sync Connect • Data Integrity
          </p>
          <Button 
            onClick={onClose}
            className="font-bold min-w-[120px] shadow-lg shadow-primary/10"
            title="Fechar relatório de inconsistências"
          >
            ENTENDIDO
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}