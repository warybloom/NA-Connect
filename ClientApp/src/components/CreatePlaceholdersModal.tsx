// Caminho: ClientApp/src/components/CreatePlaceholdersModal.tsx
import { useState } from "react";
import { FilePlus2, Loader2, Database, CheckCircle2, AlertTriangle, XCircle } from "lucide-react";
import toast from 'react-hot-toast';
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Label } from "@/components/ui/label";

export interface PlaceholderResult {
    totalRowsInLdp: number;
    rowsProcessed: number;
    placeholdersCreated: number;
    filesSkippedExisting: number;
    filesSkippedFiltered: number;
    createdFiles: string[];
    skippedExistingFiles: string[];
    skippedFilteredInfo: string[];
    errors: string[];
    summaryMessage: string;
}

interface CreatePlaceholdersModalProps {
    isOpen: boolean;
    onClose: () => void;
    onPlaceholdersCreated: (result: PlaceholderResult) => void;
    projectId?: string;
    hubId?: string;
}

export const CreatePlaceholdersModal = ({ isOpen, onClose, onPlaceholdersCreated, projectId, hubId }: CreatePlaceholdersModalProps) => {
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [resultData, setResultData] = useState<PlaceholderResult | null>(null);
    // NORMA é obrigatória no backend (Areas/DataManagement/Controllers/DataController.cs:938 — [FromQuery] string norma,
    // sem default, valida via Enum.TryParse<NormaNomenclatura> e retorna 400 se ausente/inválida).
    const [norma, setNorma] = useState<string>('Cliente');

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!projectId || !hubId) {
            toast.error("IDs de Projeto ou Hub ausentes.");
            return;
        }

        setIsSubmitting(true);
        const lt = toast.loading("O motor autónomo está a processar a LDP...");

        try {
            const response = await fetch(
                `/DataManagement/Data/CreatePlaceholdersFromProjectLdp?hubId=${hubId}&projectId=${projectId}&norma=${norma}`,
                { method: 'POST', credentials: 'include' }
            );

            // Verificação manual para evitar o erro "Unexpected end of JSON input"
            const text = await response.text();
            const responseData = text ? JSON.parse(text) : {};

            if (!response.ok) {
                throw new Error(responseData.message || responseData.summaryMessage || 'Erro ao processar placeholders.');
            }

            toast.success("Processamento finalizado!", { id: lt });
            setResultData(responseData as PlaceholderResult); 

        } catch (error: any) {
            toast.error(error.message, { id: lt });
            console.error("Erro na Automação:", error);
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleFinish = () => {
        if (resultData) {
            onPlaceholdersCreated(resultData);
        }
        setResultData(null); 
        onClose(); 
    };

    if (!isOpen) return null;

    return (
        <Dialog open={isOpen} onOpenChange={isSubmitting ? undefined : (resultData ? handleFinish : onClose)}>
            <DialogContent className="sm:max-w-[550px] bg-card border-border shadow-2xl">
                
                {!resultData ? (
                    <>
                        <DialogHeader>
                            <DialogTitle>Motor de Placeholders</DialogTitle>
                            <DialogDescription>
                                O sistema identificará automaticamente o DNA do projeto e as pastas de destino configuradas.
                            </DialogDescription>
                        </DialogHeader>
                        
                        <form onSubmit={handleSubmit}>
                            <div className="py-6 flex flex-col items-center justify-center gap-4 text-center">
                                <Database className="w-12 h-12 text-primary opacity-20" />
                                <p className="text-sm text-muted-foreground px-4">
                                    O motor fará uma varredura paralela na LDP processada e criará ficheiros vazios nas respetivas pastas de disciplina.
                                </p>
                                <div className="w-full max-w-[220px] space-y-1.5 text-left">
                                    <Label htmlFor="norma-select">Norma de Nomenclatura</Label>
                                    <Select value={norma} onValueChange={setNorma}>
                                        <SelectTrigger id="norma-select">
                                            <SelectValue />
                                        </SelectTrigger>
                                        <SelectContent>
                                            <SelectItem value="Cliente">Cliente</SelectItem>
                                            <SelectItem value="NA">N&A</SelectItem>
                                        </SelectContent>
                                    </Select>
                                </div>
                            </div>

                            <DialogFooter>
                                <Button type="button" variant="ghost" onClick={onClose} disabled={isSubmitting}>Cancelar</Button>
                                <Button type="submit" disabled={isSubmitting} className="gap-2 min-w-[160px]">
                                    {isSubmitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <FilePlus2 className="w-4 h-4" />}
                                    {isSubmitting ? 'A Processar...' : 'Iniciar Automação'}
                                </Button>
                            </DialogFooter>
                        </form>
                    </>
                ) : (
                    <>
                        <DialogHeader>
                            <DialogTitle className="flex items-center gap-2">
                                <CheckCircle2 className="text-green-500 w-5 h-5" />
                                Relatório do Processamento
                            </DialogTitle>
                            <DialogDescription>
                                {resultData.summaryMessage}
                            </DialogDescription>
                        </DialogHeader>

                        <div className="py-4 space-y-4 max-h-[60vh] overflow-y-auto pr-2 custom-scrollbar">
                            <div className="grid grid-cols-3 gap-2 text-center mb-4">
                                <div className="bg-green-500/10 border border-green-500/20 rounded p-2">
                                    <p className="text-2xl font-bold text-green-500">{resultData.placeholdersCreated}</p>
                                    <p className="text-[10px] text-muted-foreground uppercase">Criados</p>
                                </div>
                                <div className="bg-orange-500/10 border border-orange-500/20 rounded p-2">
                                    <p className="text-2xl font-bold text-orange-500">{resultData.filesSkippedFiltered + resultData.filesSkippedExisting}</p>
                                    <p className="text-[10px] text-muted-foreground uppercase">Ignorados</p>
                                </div>
                                <div className="bg-red-500/10 border border-red-500/20 rounded p-2">
                                    <p className="text-2xl font-bold text-red-500">{resultData.errors?.length || 0}</p>
                                    <p className="text-[10px] text-muted-foreground uppercase">Erros</p>
                                </div>
                            </div>

                            {/* Lista de Erros (A Autodesk gritando as falhas) */}
                            {resultData.errors && resultData.errors.length > 0 && (
                                <div className="space-y-2">
                                    <h4 className="text-sm font-semibold text-red-500 flex items-center gap-2">
                                        <XCircle className="w-4 h-4" /> Falhas ({resultData.errors.length})
                                    </h4>
                                    <ul className="text-[11px] text-muted-foreground bg-red-500/5 p-3 rounded space-y-1">
                                        {resultData.errors.map((err, i) => (
                                            <li key={i} className="flex gap-2"><span className="text-red-400">•</span>{err}</li>
                                        ))}
                                    </ul>
                                </div>
                            )}

                            {/* Lista de Ignorados (Siglas sem regra no seu Banco) */}
                            {resultData.skippedFilteredInfo && resultData.skippedFilteredInfo.length > 0 && (
                                <div className="space-y-2">
                                    <h4 className="text-sm font-semibold text-orange-500 flex items-center gap-2">
                                        <AlertTriangle className="w-4 h-4" /> Ignorados por Regra/Pasta ({resultData.skippedFilteredInfo.length})
                                    </h4>
                                    <ul className="text-[11px] text-muted-foreground bg-orange-500/5 p-3 rounded space-y-1">
                                        {resultData.skippedFilteredInfo.map((doc, i) => (
                                            <li key={i} className="flex gap-2"><span className="text-orange-400">•</span>{doc}</li>
                                        ))}
                                    </ul>
                                </div>
                            )}

                            {/* Lista de Criados com Sucesso */}
                            {resultData.createdFiles && resultData.createdFiles.length > 0 && (
                                <div className="space-y-2">
                                    <h4 className="text-sm font-semibold text-green-500 flex items-center gap-2">
                                        <CheckCircle2 className="w-4 h-4" /> Criados com Sucesso ({resultData.createdFiles.length})
                                    </h4>
                                    <ul className="text-[11px] text-muted-foreground bg-green-500/5 p-3 rounded space-y-1">
                                        {resultData.createdFiles.slice(0, 20).map((file, i) => (
                                            <li key={i} className="flex gap-2"><span className="text-green-400">•</span>{file}</li>
                                        ))}
                                        {resultData.createdFiles.length > 20 && <li>...e mais {resultData.createdFiles.length - 20} ficheiros.</li>}
                                    </ul>
                                </div>
                            )}
                        </div>

                        <DialogFooter>
                            <Button onClick={handleFinish} className="w-full">Fechar Relatório</Button>
                        </DialogFooter>
                    </>
                )}
            </DialogContent>
        </Dialog>
    );
};