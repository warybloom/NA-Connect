// Caminho: ClientApp/src/components/CreateProjectModal.tsx
// VERSÃO COM FIX PARA CLIQUE NA SCROLLBAR E LOGS ADICIONAIS

import React, { useEffect, useState, useMemo } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from "@/components/ui/dialog";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import toast from 'react-hot-toast';
import { Check, ChevronsUpDown } from "lucide-react";
import { cn } from "@/lib/utils";

interface ProjectTemplate {
    id: string;
    name: string;
}

interface CreateProjectModalProps {
    isOpen: boolean;
    onClose: () => void;
    onProjectCreated: () => void;
    hubId?: string;
}

export const CreateProjectModal = ({ isOpen, onClose, onProjectCreated, hubId }: CreateProjectModalProps) => {
    const [templates, setTemplates] = useState<ProjectTemplate[]>([]);
    const [templateId, setTemplateId] = useState<string>('');
    const [selectedFile, setSelectedFile] = useState<File | null>(null);

    const [isSubmitting, setIsSubmitting] = useState(false);
    const [isLoadingTemplates, setIsLoadingTemplates] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const [searchTerm, setSearchTerm] = useState('');
    const [openPopover, setOpenPopover] = useState(false);

    // Fetch templates
    useEffect(() => {
        // Log para verificar se o efeito é chamado quando isOpen muda
        console.log(`CreateProjectModal useEffect - isOpen: ${isOpen}`);
        if (isOpen) {
            const fetchTemplates = async () => {
                console.log("CreateProjectModal: Fetching templates...");
                setIsLoadingTemplates(true);
                setError(null);
                setSearchTerm('');
                setTemplateId('');
                try {
                    // Em CreateProjectModal.tsx, dentro do useEffect -> fetchTemplates

                    // Linha original:
                    // const response = await fetch('/DataManagement/Data/GetProjectTemplates');

                    // Altere para:
                    const response = await fetch(`/DataManagement/Data/GetProjectTemplates?hubId=${hubId}`); // <-- ADICIONADO hubId

                    console.log("CreateProjectModal: fetchTemplates response status:", response.status);
                    if (!response.ok) {
                        let errorMsg = 'Falha ao carregar os templates do servidor.';
                        try {
                            const errorData = await response.json();
                            errorMsg = errorData.message || errorMsg;
                        } catch (jsonErr) {
                             console.error("CreateProjectModal: Failed to parse error JSON", jsonErr);
                        }
                        throw new Error(errorMsg);
                    }
                    const data = await response.json();
                    console.log("CreateProjectModal: Templates received:", data);
                    setTemplates(data || []);
                } catch (err: any) {
                    console.error("CreateProjectModal: Error fetching templates:", err);
                    setError(err.message);
                    toast.error(err.message);
                } finally {
                    console.log("CreateProjectModal: Finished fetching templates.");
                    setIsLoadingTemplates(false);
                }
            };
            fetchTemplates();
        }
    }, [isOpen]);

    // Filter templates
    const filteredTemplates = useMemo(() => {
        if (!searchTerm) {
            return templates;
        }
        const lowerSearchTerm = searchTerm.toLowerCase();
        return templates.filter(template =>
            template.name.toLowerCase().includes(lowerSearchTerm)
        );
    }, [templates, searchTerm]);

    // handleFileChange
    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            setSelectedFile(e.target.files[0]);
        } else {
            setSelectedFile(null);
        }
    };

    // handleSubmit
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        console.log("CreateProjectModal: handleSubmit triggered");

        if (!hubId) {
            toast.error("Erro: O ID da Conta (Hub) não foi encontrado."); return;
        }
        if (!selectedFile) {
            toast.error("Por favor, selecione a planilha do projeto."); return;
        }
        if (!templateId) {
            toast.error("Por favor, selecione um template de projeto."); return;
        }

        setIsSubmitting(true); setError(null);
        const submissionData = new FormData();
        submissionData.append('hubId', hubId);
        submissionData.append('templateId', templateId);
        submissionData.append('memberSheet', selectedFile);
        console.log("CreateProjectModal: Submitting FormData...");

        try {
            const response = await fetch('/DataManagement/Data/CreateProjectFromTemplate', {
                method: 'POST', body: submissionData,
            });
            console.log("CreateProjectModal: Submit response status:", response.status);

            if (!response.ok) {
                 let errorMsg = 'Ocorreu um erro desconhecido ao criar o projeto.';
                 try {
                    const errorData = await response.json();
                    errorMsg = errorData.details || errorData.message || errorMsg;
                 } catch(jsonErr) {
                    console.error("CreateProjectModal: Failed to parse submit error JSON", jsonErr);
                 }
                throw new Error(errorMsg);
            }

            console.log("CreateProjectModal: Project creation successful");
            toast.success("Projeto criado e membros importados com sucesso!");
            onProjectCreated();
            onClose();
            setSelectedFile(null); setTemplateId(''); setSearchTerm('');

        } catch (err: any) {
            console.error("CreateProjectModal: Error during submission:", err);
            setError(err.message);
            toast.error(`Falha ao criar projeto: ${err.message}`);
        } finally {
            console.log("CreateProjectModal: Submission finished.");
            setIsSubmitting(false);
        }
    };

    // selectedTemplateName
    const selectedTemplateName = useMemo(() => {
        return templates.find(t => t.id === templateId)?.name;
    }, [templates, templateId]);

    // Log para depurar estado do Popover
    // console.log(`CreateProjectModal rendering - isOpen: ${isOpen}, openPopover: ${openPopover}, templateId: ${templateId}`);

    return (
        <Dialog open={isOpen} onOpenChange={(open: boolean) => !open && onClose()}>
            <DialogContent className="sm:max-w-md bg-card border-border">
                 <DialogHeader>
                    <DialogTitle>Criar Projeto via Planilha</DialogTitle>
                    <DialogDescription>
                        Selecione o template e a planilha para criar o projeto e adicionar os membros automaticamente.
                    </DialogDescription>
                </DialogHeader>
                <form onSubmit={handleSubmit}>
                    <div className="grid gap-6 py-4">
                        {/* --- Template Selector Combobox --- */}
                        <div className="grid grid-cols-4 items-center gap-4">
                            <Label htmlFor="template-trigger" className="text-right">Template</Label>
                            <Popover open={openPopover} onOpenChange={setOpenPopover}>
                                <PopoverTrigger asChild id="template-trigger" className="col-span-3">
                                    <Button
                                        variant="outline"
                                        role="combobox"
                                        aria-expanded={openPopover}
                                        className="w-full justify-between"
                                        disabled={isLoadingTemplates}
                                        onClick={() => {
                                            console.log("PopoverTrigger clicked");
                                            setOpenPopover((prev) => !prev); // Toggle popover state on click
                                        }}
                                    >
                                        {isLoadingTemplates ? "A carregar..." : (selectedTemplateName || "Selecione um template...")}
                                        <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                                    </Button>
                                </PopoverTrigger>
                                <PopoverContent
                                    className="w-[--radix-popover-trigger-width] max-h-[--radix-popover-content-available-height] p-0"
                                    onPointerDownOutside={(event) => {
                                        const targetElement = event.target as Element;
                                        // A verificação `closest` previne fechar ao clicar na scrollbar
                                        if (targetElement?.closest('[cmdk-list]')) {
                                            event.preventDefault();
                                        }
                                        // Se clicar fora do Popover E fora do Trigger, fecha
                                        // (Radix lida com fechar ao clicar no Trigger por padrão, mas esta lógica adiciona robustez)
                                        // else if (!targetElement?.closest('[role="combobox"]')) {
                                        //     setOpenPopover(false);
                                        // }
                                    }}
                                    // Adiciona onEscapeKeyDown para fechar com Esc
                                    onEscapeKeyDown={() => setOpenPopover(false)}
                                >
                                    <Command>
                                        <CommandInput
                                            placeholder="Pesquisar template..."
                                            value={searchTerm}
                                            onValueChange={setSearchTerm}
                                        />
                                        {/* Manter style inline para garantir scroll */}
                                        <CommandList style={{ height: '300px', maxHeight: '300px', overflowY: 'scroll' }}>
                                             <CommandEmpty>Nenhum template encontrado.</CommandEmpty>
                                            <CommandGroup>
                                                {filteredTemplates.map((template) => (
                                                    <CommandItem
                                                        key={template.id}
                                                        value={template.name}
                                                        onSelect={() => {
                                                            console.log(`Template selected: ${template.name} (ID: ${template.id})`);
                                                            setTemplateId(template.id);
                                                            setOpenPopover(false); // Fecha ao selecionar
                                                            setSearchTerm(''); // Limpa pesquisa
                                                        }}
                                                    >
                                                        <Check
                                                            className={cn(
                                                                "mr-2 h-4 w-4",
                                                                templateId === template.id ? "opacity-100" : "opacity-0"
                                                            )}
                                                        />
                                                        {template.name}
                                                    </CommandItem>
                                                ))}
                                            </CommandGroup>
                                        </CommandList>
                                    </Command>
                                </PopoverContent>
                            </Popover>
                        </div>
                        {/* --- End Combobox --- */}

                        <div className="grid grid-cols-4 items-center gap-4">
                            <Label htmlFor="memberSheet" className="text-right">Planilha</Label>
                            <Input id="memberSheet" type="file" onChange={handleFileChange} className="col-span-3" accept=".xlsx, .xls, .csv" required />
                        </div>
                    </div>
                     {error && <p className="text-sm text-destructive text-center mb-4">{error}</p>}
                    <DialogFooter>
                         <Button type="button" variant="ghost" onClick={onClose} disabled={isSubmitting}>Cancelar</Button>
                        <Button
                            type="submit"
                            disabled={isSubmitting || isLoadingTemplates || !templateId || !selectedFile}
                        >
                            {isSubmitting ? 'A Criar...' : 'Criar Projeto'}
                        </Button>
                    </DialogFooter>
                </form>
            </DialogContent>
        </Dialog>
    );
};