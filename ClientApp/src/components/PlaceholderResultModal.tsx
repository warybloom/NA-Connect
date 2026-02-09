// Caminho: ClientApp/src/components/PlaceholderResultModal.tsx
// VERSÃO COMPLETA E ATUALIZADA (Incluindo console.log para depuração)

import React from 'react'; // Importar React é uma boa prática
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter
} from "@/components/ui/dialog"; // Componentes UI do Shadcn/ui
import { ScrollArea } from "@/components/ui/scroll-area";
import { Button } from "@/components/ui/button";
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "@/components/ui/accordion";

// Interface TypeScript que ESPELHA a classe C# PlaceholderResult ATUALIZADA
interface PlaceholderResult {
  totalRowsInLdp: number;       // Total de linhas lidas da LDP
  rowsProcessed: number;        // Quantas linhas foram TENTADAS (após filtro inicial, se houver)
  placeholdersCreated: number;  // Quantos foram criados com sucesso
  filesSkippedExisting: number; // Quantos foram pulados porque já existiam
  filesSkippedFiltered: number; // Quantos foram pulados por causa dos filtros de tipo/disciplina
  createdFiles: string[];       // Lista dos nomes dos arquivos criados
  skippedExistingFiles: string[];// Lista dos nomes dos arquivos pulados (existentes)
  skippedFilteredInfo: string[]; // Lista de informações sobre arquivos pulados (filtrados)
  errors: string[];             // Lista de mensagens de erro
  summaryMessage: string;       // Mensagem de resumo geral
}

// Props que o componente Modal espera receber do componente pai (Files.tsx)
interface PlaceholderResultModalProps {
  isOpen: boolean;                // Controla se o modal está visível
  onClose: () => void;            // Função para fechar o modal
  result: PlaceholderResult | null; // Os dados do resultado (ou null se ainda não houver)
}

// Componente Funcional React
export function PlaceholderResultModal({
  isOpen,
  onClose,
  result
}: PlaceholderResultModalProps) {

  // --- ADICIONADO PARA DEPURAÇÃO ---
  console.log(`PlaceholderResultModal attempting to render. isOpen: ${isOpen}, result exists: ${!!result}`);
  // ---------------------------------

  // Se não houver dados de resultado, não renderiza nada
  if (!result) {
     console.log("PlaceholderResultModal returning null because result is null/undefined"); // Log adicional
     return null;
  }

  // Desestruturação com VALORES PADRÃO para as listas, prevenindo o erro 'length' of undefined
  const {
    totalRowsInLdp = 0,
    // rowsProcessed = 0, // Descomente se quiser mostrar este valor
    placeholdersCreated = 0,
    filesSkippedExisting = 0,
    filesSkippedFiltered = 0, // Novo contador
    createdFiles = [], // Valor padrão: array vazio
    skippedExistingFiles = [], // Valor padrão: array vazio
    skippedFilteredInfo = [], // Novo - Valor padrão: array vazio
    errors = [], // Valor padrão: array vazio
    summaryMessage = "Operação concluída." // Mensagem padrão
  } = result;

  // Calcula a contagem total de ignorados para exibição (opcional)
  const totalSkipped = filesSkippedExisting + filesSkippedFiltered;

  return (
    <Dialog open={isOpen} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-2xl bg-card border-border"> {/* Estilos baseados no Shadcn/ui */}
        <DialogHeader>
          <DialogTitle>Resultado da Criação de Placeholders</DialogTitle>
          {/* Mostra a mensagem de resumo vinda do backend */}
          <DialogDescription>{summaryMessage}</DialogDescription>
        </DialogHeader>

        {/* Seção de Resumo com contadores */}
        <div className="grid grid-cols-2 gap-4 my-4">
          <div className="p-4 bg-secondary rounded-lg">
            <p className="text-sm text-muted-foreground">Total Linhas na LDP</p>
            <p className="text-2xl font-bold text-foreground">{totalRowsInLdp}</p>
          </div>
          <div className="p-4 bg-green-900/50 rounded-lg">
            <p className="text-sm text-green-400">Criados com Sucesso</p>
            <p className="text-2xl font-bold text-foreground">{placeholdersCreated}</p>
          </div>
          {/* Bloco para Ignorados (Total ou separados) - Escolha uma das opções abaixo */}

          {/* Opção 1: Mostrar Total de Ignorados */}
          {/* <div className="p-4 bg-yellow-900/50 rounded-lg">
            <p className="text-sm text-yellow-400">Total Ignorados</p>
            <p className="text-2xl font-bold text-foreground">{totalSkipped}</p>
          </div> */}

          {/* Opção 2: Mostrar Ignorados Separados */}
           <div className="p-4 bg-yellow-900/50 rounded-lg">
             <p className="text-sm text-yellow-400">Ignorados (Já Existiam)</p>
             <p className="text-2xl font-bold text-foreground">{filesSkippedExisting}</p>
           </div>
           <div className="p-4 bg-blue-900/50 rounded-lg"> {/* Cor diferente para filtrados */}
             <p className="text-sm text-blue-400">Ignorados (Filtrados)</p>
             <p className="text-2xl font-bold text-foreground">{filesSkippedFiltered}</p>
           </div>

          {/* Contagem de Erros (agora seguro devido ao valor padrão `errors = []`) */}
          <div className="p-4 bg-destructive/30 rounded-lg">
            <p className="text-sm text-red-400">Erros</p>
            <p className="text-2xl font-bold text-foreground">{errors.length}</p> {/* Seguro usar .length */}
          </div>
        </div>

        {/* Seção de Detalhes com Acordeão */}
        <Accordion type="single" collapsible className="w-full">

          {/* Detalhes dos Criados (seguro usar .length) */}
          {createdFiles.length > 0 && (
            <AccordionItem value="created">
              <AccordionTrigger>Detalhes dos {createdFiles.length} Arquivos Criados</AccordionTrigger>
              <AccordionContent>
                <ScrollArea className="h-40 rounded-md border p-2">
                  <ul className="list-disc pl-5 text-sm text-muted-foreground">
                    {createdFiles.map((file, index) => <li key={`created-${index}`}>{file}</li>)}
                  </ul>
                </ScrollArea>
              </AccordionContent>
            </AccordionItem>
          )}

          {/* Detalhes dos Ignorados (Já Existiam) (seguro usar .length) */}
          {skippedExistingFiles.length > 0 && (
            <AccordionItem value="skipped-existing">
              <AccordionTrigger>Detalhes dos {skippedExistingFiles.length} Ignorados (Já Existiam)</AccordionTrigger>
              <AccordionContent>
                <ScrollArea className="h-40 rounded-md border p-2">
                  <ul className="list-disc pl-5 text-sm text-muted-foreground">
                    {skippedExistingFiles.map((file, index) => <li key={`skipped-e-${index}`}>{file}</li>)}
                  </ul>
                </ScrollArea>
              </AccordionContent>
            </AccordionItem>
          )}

          {/* NOVO: Detalhes dos Ignorados (Filtrados) (seguro usar .length) */}
          {skippedFilteredInfo.length > 0 && (
            <AccordionItem value="skipped-filtered">
              <AccordionTrigger>Detalhes dos {skippedFilteredInfo.length} Ignorados (Filtrados por Tipo/Disciplina)</AccordionTrigger>
              <AccordionContent>
                <ScrollArea className="h-40 rounded-md border p-2">
                  <ul className="list-disc pl-5 text-sm text-muted-foreground">
                    {/* skippedFilteredInfo contém a string completa "Linha X: Ignorada (Tipo='Y', Disc='Z')" */}
                    {skippedFilteredInfo.map((info, index) => <li key={`skipped-f-${index}`}>{info}</li>)}
                  </ul>
                </ScrollArea>
              </AccordionContent>
            </AccordionItem>
          )}

          {/* Detalhes dos Erros (seguro usar .length) */}
          {errors.length > 0 && (
            <AccordionItem value="errors">
              <AccordionTrigger className="text-destructive">Detalhes dos {errors.length} Erros Encontrados</AccordionTrigger>
              <AccordionContent>
                <ScrollArea className="h-40 rounded-md border border-destructive/50 bg-destructive/10 p-2">
                  <ul className="list-disc pl-5 text-sm text-destructive">
                    {errors.map((error, index) => <li key={`error-${index}`}>{error}</li>)}
                  </ul>
                </ScrollArea>
              </AccordionContent>
            </AccordionItem>
          )}
        </Accordion> {/* Fim do Acordeão */}

        <DialogFooter>
          <Button onClick={onClose}>Fechar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}