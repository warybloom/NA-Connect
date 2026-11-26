// BACKEND_PENDENTE: auditoria/mapeamento Plant3D (api/plant/*) nao existe no N&A. UI transplantada literalmente do ForgeSync. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter
} from "./ui/dialog"; 
import { Button } from "./ui/button"; 
import { ScrollArea } from "./ui/scroll-area"; 
import { FileWarning, CheckCircle2 } from "lucide-react";
import { cn } from "../lib/utils"; // ✅ Importado para manipulação de classes

/**
 * Interface rigorosa para o resultado da auditoria.
 * Alinhada com os campos de 'filesNotFound' do DataController.cs.
 */
export interface AuditResult {
  message: string;
  count: number;
  missingFiles: string[];
}

interface AuditResultModalProps {
  isOpen: boolean;
  onClose: () => void;
  result: AuditResult | null;
}

export function AuditResultModal({ isOpen, onClose, result }: AuditResultModalProps) {
  // Se não houver resultado, o modal não renderiza nada para evitar erros de 'null'
  if (!result) return null;

  const hasIssues = result.count > 0;

  return (
    <Dialog 
      open={isOpen} 
      // ✅ Tipagem explícita 'boolean' para evitar o erro 'Unexpected any'
      onOpenChange={(open: boolean) => !open && onClose()}
    >
      {/* Utilizando a classe glass-card definida no seu index.css para consistência visual */}
      <DialogContent className="sm:max-w-md glass-card border-border/50">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-xl font-bold">
            {hasIssues ? (
              <>
                <FileWarning className="w-5 h-5 text-destructive" /> 
                Arquivos Pendentes
              </>
            ) : (
              <>
                <CheckCircle2 className="w-5 h-5 text-green-500" /> 
                Auditoria Concluída
              </>
            )}
          </DialogTitle>
          <DialogDescription className="text-muted-foreground">
            {result.message}
          </DialogDescription>
        </DialogHeader>

        {/* Diagrama de Processamento de Auditoria:
            O sistema compara os itens da LDP com os arquivos reais no ACC.
        */}
        

        {hasIssues && (
          <div className="space-y-3">
            <p className="text-xs font-bold text-muted-foreground uppercase tracking-wider">
              Lista de Inconsistências ({result.count})
            </p>
            <ScrollArea className="h-60 rounded-md border border-border/50 bg-background/50 p-3">
              <ul className="space-y-2">
                {/* ✅ Tipagem implícita segura para o map (string f, number i) */}
                {result.missingFiles.map((fileName, index) => (
                  <li 
                    key={`${fileName}-${index}`} 
                    className="text-sm text-muted-foreground bg-muted/20 px-3 py-2 rounded-sm border-l-2 border-destructive/50"
                  >
                    {fileName}
                  </li>
                ))}
              </ul>
            </ScrollArea>
          </div>
        )}

        <DialogFooter className="mt-4">
          <Button 
            onClick={onClose}
            className={cn(
              "w-full font-bold",
              !hasIssues && "bg-green-600 hover:bg-green-700"
            )}
          >
            {hasIssues ? "ENTENDIDO" : "FECHAR"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}