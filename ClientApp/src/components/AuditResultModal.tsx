// Caminho: ClientApp/src/components/AuditResultModal.tsx
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { ScrollArea } from "@/components/ui/scroll-area";
import { FileWarning, CheckCircle2 } from "lucide-react";

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
  if (!result) return null;

  const hasIssues = result.count > 0;

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md bg-card border-border">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {hasIssues ? (
              <>
                <FileWarning className="w-5 h-5 text-yellow-500" />
                Auditoria: Arquivos Não Listados
              </>
            ) : (
              <>
                <CheckCircle2 className="w-5 h-5 text-green-500" />
                Auditoria: Tudo Certo
              </>
            )}
          </DialogTitle>
          <DialogDescription>
            {result.message}
          </DialogDescription>
        </DialogHeader>

        {hasIssues && (
          <div className="py-2">
            <p className="text-sm text-muted-foreground mb-2">
              Os seguintes arquivos existem no ACC, mas <strong>não foram encontrados</strong> na LDP:
            </p>
            <ScrollArea className="h-48 w-full rounded-md border p-2 bg-muted/30">
              <ul className="space-y-1">
                {result.missingFiles.map((file, index) => (
                  <li key={index} className="text-sm text-red-400 flex items-start gap-2">
                     <span className="mt-1.5 h-1.5 w-1.5 rounded-full bg-red-500 flex-shrink-0" />
                     <span className="break-all">{file}</span>
                  </li>
                ))}
              </ul>
            </ScrollArea>
          </div>
        )}

        <DialogFooter>
          <Button onClick={onClose}>Fechar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}