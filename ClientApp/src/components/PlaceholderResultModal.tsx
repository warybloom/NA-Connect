// Caminho: ClientApp/src/components/PlaceholderResultModal.tsx
// VERSÃO FINAL: CORREÇÃO DE EXPORTAÇÃO NOMEADA E ADAPTAÇÃO DE TIPOS

import { Fragment } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { Button } from "@/components/ui/button";
import { CheckCircle, XCircle, FileText, AlertTriangle } from "lucide-react";
// Importamos a interface correta definida no outro arquivo
import type { PlaceholderResult } from "@/components/CreatePlaceholdersModal";

interface PlaceholderResultModalProps {
  isOpen: boolean;
  onClose: () => void;
  result: PlaceholderResult | null;
}

// ✅ CORREÇÃO APLICADA: 'export function' para permitir Named Import ({ })
export function PlaceholderResultModal({ isOpen, onClose, result }: PlaceholderResultModalProps) {
  if (!result) return null;

  // Cálculos baseados na interface PlaceholderResult oficial
  const successCount = result.placeholdersCreated;
  const errorCount = result.errors.length;
  const existingCount = result.filesSkippedExisting;

  return (
    <Transition appear show={isOpen} as={Fragment}>
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/25 backdrop-blur-sm" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4 text-center">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-2xl transform overflow-hidden rounded-2xl bg-card p-6 text-left align-middle shadow-xl transition-all">
                <Dialog.Title as="h3" className="text-lg font-medium leading-6 text-foreground flex items-center gap-2">
                  <FileText className="h-5 w-5 text-blue-500" />
                  Resultado da Criação de Placeholders
                </Dialog.Title>
                
                <div className="mt-4 space-y-4">
                  {/* Mensagem de Resumo */}
                  <div className="bg-muted/60 p-4 rounded-lg text-sm text-foreground/80">
                    <p className="font-semibold">{result.summaryMessage}</p>
                    <p className="text-xs text-muted-foreground mt-1">
                        Total Processado: {result.rowsProcessed} | Total LDP: {result.totalRowsInLdp}
                    </p>
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    {/* Coluna de Sucessos */}
                    <div className="border rounded-lg p-3 bg-green-500/5 border-green-500/20">
                      <div className="flex items-center gap-2 font-semibold text-green-700 dark:text-green-400 mb-2">
                        <CheckCircle className="h-4 w-4" />
                        Sucessos ({successCount})
                      </div>
                      <ul className="max-h-40 overflow-y-auto text-xs space-y-1 scrollbar-thin scrollbar-thumb-green-200">
                        {result.createdFiles && result.createdFiles.length > 0 ? (
                            result.createdFiles.map((fileName, idx) => (
                                <li key={idx} className="text-green-800 dark:text-green-400 flex items-start gap-1">
                                    <span>•</span> {fileName}
                                </li>
                            ))
                        ) : (
                            <li className="text-muted-foreground italic">Nenhum arquivo criado nesta execução.</li>
                        )}
                      </ul>
                    </div>

                    {/* Coluna de Erros */}
                    <div className="border rounded-lg p-3 bg-red-500/5 border-red-500/20">
                      <div className="flex items-center gap-2 font-semibold text-red-700 dark:text-red-400 mb-2">
                        <XCircle className="h-4 w-4" />
                        Erros ({errorCount})
                      </div>
                      <ul className="max-h-40 overflow-y-auto text-xs space-y-1 scrollbar-thin scrollbar-thumb-red-200">
                        {result.errors && result.errors.length > 0 ? (
                            result.errors.map((errorMsg, idx) => (
                                <li key={idx} className="text-red-800 dark:text-red-400 break-words flex items-start gap-1">
                                  <span>•</span> {errorMsg}
                                </li>
                            ))
                        ) : (
                            <li className="text-muted-foreground italic">Nenhum erro encontrado.</li>
                        )}
                      </ul>
                    </div>
                  </div>
                  
                  {/* Área de Arquivos Ignorados (Já Existentes) */}
                  {existingCount > 0 && (
                     <div className="border rounded-lg p-3 bg-yellow-500/5 border-yellow-500/20">
                      <div className="flex items-center gap-2 font-semibold text-yellow-700 dark:text-yellow-400 mb-2">
                        <AlertTriangle className="h-4 w-4" />
                        Já Existiam ({existingCount})
                      </div>
                      <div className="text-xs text-yellow-800 dark:text-yellow-400 mb-2">
                        Arquivos ignorados pois já existiam na pasta de destino:
                      </div>
                      <ul className="max-h-24 overflow-y-auto text-xs space-y-1 text-yellow-900/80 dark:text-yellow-200/80">
                          {result.skippedExistingFiles && result.skippedExistingFiles.slice(0, 50).map((file, idx) => (
                              <li key={idx}>{file}</li>
                          ))}
                          {result.skippedExistingFiles && result.skippedExistingFiles.length > 50 && (
                              <li className="italic text-[10px] mt-1">...e mais {result.skippedExistingFiles.length - 50} arquivos.</li>
                          )}
                      </ul>
                    </div>
                  )}

                </div>

                <div className="mt-6 flex justify-end">
                  <Button
                    type="button"
                    onClick={onClose}
                    className="font-bold uppercase text-xs"
                  >
                    Fechar
                  </Button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}