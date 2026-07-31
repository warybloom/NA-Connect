import React, { useState, useEffect } from 'react';
import { OCRCalibrator } from './OCRCalibrator';
import toast from 'react-hot-toast';
import { Scan, Plus, Maximize, AlertCircle, FileBox, Ruler, Loader2, X, Settings, ChevronDown } from 'lucide-react';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "../components/app-sidebar";
import { ModeToggle } from '@/components/alternar-modo';

export const OCRManager = () => {
    const [pacotes, setPacotes] = useState<any[]>([]);
    const [selectedPacote, setSelectedPacote] = useState<number | null>(null);
    const [selectedFolha, setSelectedFolha] = useState<string>("A1");
    const [step, setStep] = useState<'select' | 'calibrate'>('select');
    const [loading, setLoading] = useState(true);
    
    // Estados para o Novo Pacote
    const [isNewPackageModalOpen, setIsNewPackageModalOpen] = useState(false);
    const [newPackageName, setNewPackageName] = useState("");

    // Busca os pacotes cadastrados no SQL
    const carregarPacotes = () => {
        setLoading(true);
        fetch('/api/admin/ocr/pacotes')
            .then(res => res.json())
            .then(data => {
                setPacotes(data);
                setLoading(false);
            })
            .catch(err => {
                console.error("Erro ao carregar pacotes OCR:", err);
                setLoading(false);
            });
    };

    useEffect(() => {
        carregarPacotes();
    }, []);

    // Função de Salvamento do Novo Pacote
    const handleSaveNewPackage = async () => {
        if (!newPackageName.trim()) {
            toast.error("Digite um nome para o pacote.");
            return;
        }

        const tid = toast.loading("Registrando novo pacote...");
        try {
            const response = await fetch('/api/admin/ocr/pacotes', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Nome_Pacote: newPackageName.toUpperCase() })
            });

            if (response.ok) {
                toast.success("Pacote criado com sucesso!", { id: tid });
                setNewPackageName("");
                setIsNewPackageModalOpen(false);
                carregarPacotes(); 
            } else {
                throw new Error();
            }
        } catch (error) {
            toast.error("Erro ao salvar pacote no banco.", { id: tid });
        }
    };

    // Renderiza o Calibrador
    if (step === 'calibrate' && selectedPacote) {
        return (
            <div className="flex flex-col gap-2 animate-in fade-in duration-500">
                <OCRCalibrator 
                    pacoteId={selectedPacote} 
                    tipoFolha={selectedFolha} 
                    onBack={() => setStep('select')} 
                />
            </div>
        );
    }

    // =========================================================================
    // VISUAL MODERNIZADO E COMPACTO (Estilo Ofensores/Curva S)
    // =========================================================================
    return (
                <SidebarProvider>
            <AppSidebar />
            <SidebarInset>
                <div className="flex flex-col gap-8 animate-enter m-4">
                    <div className="flex items-center gap-9 mb-2">
                        <SidebarTrigger className="-m-5"/>
                        <ModeToggle/>
                        <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
                        </a>
                    </div>
        <div className="min-h-full w-full bg-background p-6 md:p-8 animate-in fade-in duration-700 font-sans rounded-xl shadow-sm border border-border">
            
            {/* CABEÇALHO COMPACTO INTEGRADO */}
            <div className="flex flex-col md:flex-row items-start md:items-center justify-between mb-8 pb-6 border-b border-border gap-4">
                <div className="flex items-center gap-4">
                    <div className="p-3 bg-gradient-to-br from-primary to-blue-600 rounded-2xl shadow-lg shadow-primary/20">
                        <Scan className="text-white w-6 h-6" />
                    </div>
                    <div>
                        <h1 className="text-2xl md:text-3xl font-black text-foreground uppercase italic tracking-tight leading-none">
                            Gestão de <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary to-blue-400">VISÃO (OCR)</span>
                        </h1>
                        <p className="text-muted-foreground text-[10px] font-bold uppercase tracking-[0.2em] mt-2">
                            Configure a inteligência de leitura de carimbos nos PDFs.
                        </p>
                    </div>
                </div>
            </div>

            {/* ÁREA DE CONFIGURAÇÃO (GRID DE CARDS ANALÍTICOS COMPACTOS) */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
                
                {/* CARD 1. SELEÇÃO DO PACOTE */}
                <div className="bg-card border border-border p-6 rounded-2xl space-y-5 shadow-sm relative overflow-hidden group hover:border-primary/50 transition-all">
                    <div className="absolute top-0 right-0 p-4 opacity-5 group-hover:opacity-10 transition-opacity text-foreground"><FileBox size={80} /></div>
                    <div className="flex items-center justify-between gap-3 relative z-10">
                        <h4 className="text-[11px] font-black text-primary uppercase tracking-[0.15em] flex items-center gap-2">
                            <Settings size={16} /> 1. Pacote de Normas (Empresa)
                        </h4>
                        <button 
                            onClick={() => setIsNewPackageModalOpen(true)}
                            title="Criar Novo Pacote"
                            aria-label="Criar Novo Pacote"
                            className="flex items-center gap-1.5 text-[9px] font-black uppercase tracking-wider bg-primary/10 text-primary px-3 py-1.5 rounded-lg border border-primary/20 hover:bg-primary hover:text-white transition-all shadow-sm active:scale-95"
                        >
                            <Plus size={12} /> Novo
                        </button>
                    </div>
                    
                    <div className="relative group/select z-10">
                        <select 
                            id="pacote-select"
                            title="Selecione qual pacote de normas de carimbo você deseja configurar"
                            aria-label="Selecione o pacote de normas"
                            className="w-full bg-background border border-border rounded-xl p-4 text-xs text-foreground font-bold outline-none focus:ring-2 focus:ring-primary focus:border-transparent appearance-none transition-all hover:bg-muted cursor-pointer disabled:opacity-50"
                            onChange={(e) => setSelectedPacote(Number(e.target.value))}
                            value={selectedPacote || ""}
                            disabled={loading}
                        >
                            <option value="" disabled className="text-muted-foreground">
                                {loading ? "Carregando pacotes..." : "Selecione um pacote de visão..."}
                            </option>
                            {pacotes.map(p => (
                                <option key={p.id} value={p.id} className="text-foreground font-bold">
                                    🏢 {p.nome}
                                </option>
                            ))}
                        </select>
                        {loading && <Loader2 className="absolute right-4 top-1/2 -translate-y-1/2 text-primary animate-spin" size={16} />}
                        <ChevronDown className="absolute right-4 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none group-hover/select:text-primary transition-colors" size={18} />
                    </div>
                </div>

                {/* CARD 2. SELEÇÃO DA FOLHA */}
                <div className="bg-card border border-border p-6 rounded-2xl space-y-5 shadow-sm relative overflow-hidden group hover:border-primary/50 transition-all">
                    <div className="absolute top-0 right-0 p-4 opacity-5 group-hover:opacity-10 transition-opacity text-foreground"><Ruler size={80} /></div>
                    <h4 className="text-[11px] font-black text-primary uppercase tracking-[0.15em] flex items-center gap-2 relative z-10">
                        <Settings size={16} /> 2. Tamanho da Folha Base
                    </h4>
                    <div className="relative group/select z-10">
                        <select 
                            id="folha-select"
                            title="Selecione o padrão e tamanho da prancha"
                            aria-label="Selecione o padrão e tamanho da prancha"
                            className="w-full bg-background border border-border rounded-xl p-4 text-xs text-foreground font-bold outline-none focus:ring-2 focus:ring-primary focus:border-transparent appearance-none transition-all hover:bg-muted cursor-pointer"
                            onChange={(e) => setSelectedFolha(e.target.value)}
                            value={selectedFolha}
                        >
                            <optgroup label="🌍 Padrão ISO (Brasil / Europa)" className="text-foreground font-bold">
                                <option value="A0">📄 A0 (841 x 1189 mm)</option>
                                <option value="A1">📄 A1 (594 x 841 mm)</option>
                                <option value="A2">📄 A2 (420 x 594 mm)</option>
                                <option value="A3">📄 A3 (297 x 420 mm)</option>
                                <option value="A4">📄 A4 (210 x 297 mm)</option>
                            </optgroup>
                            
                            <optgroup label="🇺🇸 Padrão ANSI (Engenharia EUA)" className="text-foreground font-bold">
                                <option value="ANSI_A">📄 ANSI A (8.5 x 11 in)</option>
                                <option value="ANSI_B">📄 ANSI B (11 x 17 in)</option>
                                <option value="ANSI_C">📄 ANSI C (17 x 22 in)</option>
                                <option value="ANSI_D">📄 ANSI D (22 x 34 in)</option>
                                <option value="ANSI_E">📄 ANSI E (34 x 44 in)</option>
                            </optgroup>

                            <optgroup label="🏛️ Padrão ARCH (Arquitetura EUA)" className="text-foreground font-bold">
                                <option value="ARCH_C">📄 ARCH C (18 x 24 in)</option>
                                <option value="ARCH_D">📄 ARCH D (24 x 36 in)</option>
                                <option value="ARCH_E">📄 ARCH E (36 x 48 in)</option>
                            </optgroup>
                        </select>
                        <ChevronDown className="absolute right-4 top-1/2 -translate-y-1/2 text-muted-foreground pointer-events-none group-hover/select:text-primary transition-colors" size={18} />
                    </div>
                </div>

                {/* CARD 3. AÇÃO (COMPACTO) */}
                <div className="bg-card border border-border p-6 rounded-2xl flex flex-col justify-between gap-5 shadow-sm relative overflow-hidden group hover:border-emerald-500/50 transition-all xl:col-span-1 md:col-span-2 xl:row-span-1">
                    <div className="p-4 bg-amber-500/10 border border-amber-500/20 rounded-xl flex items-start gap-3 relative z-10">
                        <AlertCircle className="text-amber-500 shrink-0 mt-0.5" size={18} />
                        <div>
                            <h4 className="text-[10px] font-black uppercase text-amber-600 dark:text-amber-500 tracking-widest mb-1 leading-none">Atenção ao Padrão</h4>
                            <p className="text-[10px] text-amber-700 dark:text-amber-200/70 leading-relaxed font-medium">
                                Certifique-se de que o PDF corresponde <strong>exatamente</strong> ao tamanho da folha base selecionado acima.
                            </p>
                        </div>
                    </div>
                    <button
                        disabled={!selectedPacote || loading}
                        onClick={() => setStep('calibrate')}
                        title="Abrir o Estúdio OCR com as opções selecionadas"
                        aria-label="Abrir o Estúdio OCR"
                        className="group w-full h-14 flex items-center justify-center gap-3 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 disabled:from-muted disabled:to-muted disabled:text-muted-foreground disabled:border disabled:border-border text-white font-black uppercase text-xs rounded-xl transition-all shadow-md active:scale-[0.98] relative z-10"
                    >
                        <Maximize size={18} className={!selectedPacote ? "opacity-50" : "group-hover:scale-110 transition-transform"} />
                        Abrir Calibrador
                    </button>
                </div>
            </div>

            {/* ========================================================================= */}
            {/* MODAL PARA NOVO PACOTE (COMPACTA E INTEGRADA AO ESTILO) */}
            {/* ========================================================================= */}
            {isNewPackageModalOpen && (
                <div className="fixed inset-0 z-[100] flex items-center justify-center bg-background/80 backdrop-blur-sm p-4 animate-in fade-in duration-200">
                    <div className="bg-card border border-border rounded-2xl p-6 w-full max-w-sm shadow-2xl animate-in zoom-in-95 duration-200 space-y-6">
                        
                        <div className="flex items-center justify-between">
                            <div>
                                <h3 className="text-lg font-black uppercase italic tracking-tighter text-foreground leading-none">
                                    Novo <span className="text-primary">Pacote</span>
                                </h3>
                                <p className="text-[9px] text-muted-foreground mt-1 font-bold uppercase tracking-widest">
                                    Defina o nome do padrão de carimbo
                                </p>
                            </div>
                            <button 
                                onClick={() => setIsNewPackageModalOpen(false)} 
                                title="Fechar modal" 
                                aria-label="Fechar modal" 
                                className="p-1.5 bg-muted text-muted-foreground hover:text-rose-500 hover:bg-rose-500/10 rounded-lg transition-all"
                            >
                                <X size={16} />
                            </button>
                        </div>

                        <input 
                            autoFocus
                            type="text"
                            title="Nome do Pacote"
                            aria-label="Nome do Pacote"
                            placeholder="EX: PETROBRAS REV-05"
                            value={newPackageName}
                            onChange={(e) => setNewPackageName(e.target.value)}
                            onKeyDown={(e) => e.key === 'Enter' && handleSaveNewPackage()}
                            className="w-full p-4 bg-background border border-border rounded-xl outline-none focus:ring-2 focus:ring-primary focus:border-transparent font-bold uppercase text-xs text-foreground placeholder:text-muted-foreground shadow-sm transition-all"
                        />

                        <div className="flex gap-3 pt-4 border-t border-border">
                            <button 
                                onClick={() => setIsNewPackageModalOpen(false)}
                                title="Cancelar operação"
                                aria-label="Cancelar operação"
                                className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest text-muted-foreground hover:text-foreground hover:bg-muted border border-transparent hover:border-border rounded-lg transition-all"
                            >
                                Cancelar
                            </button>
                            <button 
                                onClick={handleSaveNewPackage}
                                title="Gravar novo pacote no banco de dados"
                                aria-label="Gravar novo pacote"
                                className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest bg-gradient-to-r from-primary to-blue-600 text-white rounded-lg shadow-md hover:opacity-90 active:scale-95 transition-all"
                            >
                                Gravar
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
         </div>
            </SidebarInset>
        </SidebarProvider>
    );
};