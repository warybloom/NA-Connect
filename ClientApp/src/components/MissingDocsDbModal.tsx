// Caminho: src/components/MissingDocsDbModal.tsx (VERSÃO SHADCN/UI)

import React from 'react';
// Importa os componentes necessários do Shadcn/UI
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription, // Usaremos para a descrição
  DialogFooter
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { ScrollArea } from "@/components/ui/scroll-area"; // Para a lista rolável
import { FileWarning } from 'lucide-react'; // Ícone para destaque (opcional)

// --- Interface para as props (inalterada) ---
interface MissingDocsDbModalProps {
  isOpen: boolean;
  onClose: () => void;
  documents: string[];
}

export function MissingDocsDbModal({ isOpen, onClose, documents }: MissingDocsDbModalProps) {
  // Não renderiza nada se não estiver aberto (inalterado)
  // O controle `open` e `onOpenChange` do Dialog lida com isso internamente também
  // if (!isOpen) {
  //   return null; 
  // }

  return (
    // Usa o Dialog do Shadcn para controlar a abertura/fechamento
    <Dialog open={isOpen} onOpenChange={(open: boolean) => !open && onClose()}>
      {/* sm:max-w-[600px] define uma largura máxima similar aos outros modais */}
      <DialogContent className="sm:max-w-[600px] bg-card border-border flex flex-col max-h-[85vh]"> {/* Adiciona flexbox e max-height */}
        <DialogHeader>
          <DialogTitle className="flex items-center"> {/* Alinha ícone e título */}
            <FileWarning className="w-5 h-5 mr-2 text-destructive" /> {/* Ícone de aviso */}
            Documentos da LDP Não Encontrados no Banco
          </DialogTitle>
          <DialogDescription>
            Os seguintes documentos listados na LDP não tiveram correspondência no banco de dados para atualização do HH Realizado:
          </DialogDescription>
        </DialogHeader>

        {/* Área rolável para a lista de documentos */}
        {/* Adiciona my-4 para espaçamento vertical e flex-grow para ocupar espaço */}
        <ScrollArea className="h-auto max-h-[50vh] w-full rounded-md border p-4 my-4 flex-grow bg-muted/30"> {/* Ajusta altura e fundo */}
           {documents && documents.length > 0 ? (
             <ul className="list-disc pl-5 space-y-1"> {/* Adiciona espaço entre itens */}
               {documents.map((doc, index) => (
                 <li key={index} className="text-sm text-muted-foreground"> {/* Estilo do texto */}
                  {doc}
                 </li>
               ))}
             </ul>
           ) : (
             <p className="text-sm text-muted-foreground text-center">Nenhum documento não encontrado reportado.</p> // Fallback
           )}
        </ScrollArea>

        {/* Rodapé padrão com o botão de fechar */}
        <DialogFooter className="mt-auto"> {/* Garante que o rodapé fique no fundo */}
          <Button onClick={onClose}>Fechar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}