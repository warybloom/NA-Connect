// Caminho: ClientApp/src/components/CreatePlaceholdersModal.tsx
import { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/Select";
import { Button } from "@/components/ui/button";
// import { Input } from "@/components/ui/input"; // <--- REMOVIDO Input
import { Label } from "@/components/ui/label";
import toast from 'react-hot-toast';

// ... (interface PlaceholderResult permanece igual) ...
interface PlaceholderResult {
  totalRowsInLdp: number;
  rowsProcessed: number;        // Adicionado se necessário pelo backend
  placeholdersCreated: number;
  filesSkippedExisting: number; // Nome atualizado
  filesSkippedFiltered: number; // Nome atualizado
  createdFiles: string[];
  skippedExistingFiles: string[];// Nome atualizado
  skippedFilteredInfo: string[]; // Adicionado
  errors: string[];
  summaryMessage: string;
}

interface CreatePlaceholdersModalProps {
    isOpen: boolean;
    onClose: () => void;
    onPlaceholdersCreated: (result: PlaceholderResult) => void; // Agora usa a interface atualizada
    projectId?: string;
    hubId?: string;
}

export const CreatePlaceholdersModal = ({ isOpen, onClose, onPlaceholdersCreated, projectId, hubId }: CreatePlaceholdersModalProps) => {
    // REMOVIDO: const [selectedFile, setSelectedFile] = useState<File | null>(null);
    const [norma, setNorma] = useState<string>('Cliente'); // 'Cliente' como padrão
    const [isSubmitting, setIsSubmitting] = useState(false);

    // REMOVIDO: handleFileChange

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        // Verificações essenciais
        if (!hubId || !projectId) {
            toast.error("Erro: ID do Hub ou do Projeto não está disponível para iniciar a operação.");
            return;
        }
        // REMOVIDO: Verificação do selectedFile

        setIsSubmitting(true);
        const loadingToast = toast.loading("A processar LDP do projeto e criar placeholders...");

        // REMOVIDO: Criação do FormData

        try {
    const response = await fetch(`/DataManagement/Data/CreatePlaceholdersFromProjectLdp?hubId=${hubId}&projectId=${projectId}&norma=${norma}`, {
        method: 'POST',
    });

    // 1. Tenta ler a resposta como JSON independentemente do status
    let responseData: any;
    try {
        responseData = await response.json();
    } catch (jsonError) {
        // Se falhar ao parsear JSON (ex: resposta vazia ou texto simples), usa o texto
        const errorText = await response.text();
        throw new Error(`Erro ${response.status}: ${errorText || response.statusText}`);
    }

    // 2. Agora verifica se a resposta NÃO foi bem-sucedida (status não é 2xx)
        if (!response.ok) {
            // Procura pela mensagem de erro:
            // - Prioriza 'message' ou 'details' (vindo dos retornos de erro do C# como BadRequest, NotFound, etc.)
            // - Se não encontrar, usa 'summaryMessage' (caso o backend retorne o PlaceholderResult mesmo em erro 500)
            // - Se nada for encontrado, usa uma mensagem genérica.
            const errorMessage = responseData.message
                                || responseData.details
                                || responseData.summaryMessage
                                || 'Ocorreu um erro desconhecido ao processar a LDP.';
            throw new Error(errorMessage); // Lança o erro para ser pego pelo catch principal
        }

        // 3. Se a resposta foi OK (2xx), agora podemos seguramente tratar como PlaceholderResult
        const result: PlaceholderResult = responseData;

        toast.success("Processo concluído! A exibir relatório.", { id: loadingToast });
        onClose();
        console.log("Calling onPlaceholdersCreated with:", result);
        onPlaceholdersCreated(result);
        setNorma('Cliente');

        } catch (err: any) {
                // O erro lançado acima (ou de rede) terá uma propriedade .message
                toast.error(`Falha: ${err.message}`, { id: loadingToast });
        } finally {
        setIsSubmitting(false);
    }

    };

    return (
        <Dialog open={isOpen} onOpenChange={onClose}>
            <DialogContent className="sm:max-w-[480px] bg-card border-border">
                <DialogHeader>
                    <DialogTitle>Criar Ficheiros Placeholder</DialogTitle>
                    <DialogDescription>
                        {/* Texto ajustado */}
                        Selecione a norma de nomenclatura. O sistema usará a LDP encontrada no projeto para criar os ficheiros vazios nas pastas corretas.
                    </DialogDescription>
                </DialogHeader>
                <form onSubmit={handleSubmit}>
                    <div className="grid gap-6 py-4">
                        {/* REMOVIDO: Div do input de ficheiro */}
                        {/* <div className="grid grid-cols-4 items-center gap-4">
                            <Label htmlFor="ldpFile" className="text-right">LDP</Label>
                            <Input id="ldpFile" type="file" onChange={handleFileChange} className="col-span-3" accept=".xlsx, .xls" required />
                        </div> */}
                        <div className="grid grid-cols-4 items-center gap-4">
                            <Label htmlFor="norma" className="text-right">Norma do Nome</Label>
                            <Select onValueChange={setNorma} value={norma} required>
                                <SelectTrigger className="col-span-3">
                                    <SelectValue placeholder="Selecione a norma de nomenclatura" />
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value="Cliente">Norma do Cliente</SelectItem>
                                    <SelectItem value="NA">Norma N&A</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>
                    </div>
                    <DialogFooter>
                        <Button type="button" variant="ghost" onClick={onClose} disabled={isSubmitting}>Cancelar</Button>
                        {/* Ajustado disabled condition */}
                        <Button type="submit" disabled={isSubmitting}>
                            {isSubmitting ? 'A Criar...' : 'Criar Placeholders'}
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
};