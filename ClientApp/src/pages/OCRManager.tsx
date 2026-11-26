// Administração dos templates de auditoria documental e suas regiões calibradas.
import React, { useState, useEffect } from 'react';
import { OCRCalibrator } from './OCRCalibrator';
import FormatMappingPanel from '@/features/document-quality/FormatMappingPanel';
import { usePermissions } from '@/features/permissions/PermissionProvider';
import toast from 'react-hot-toast';
// 🚀 CORREÇÃO AQUI: Adicionado o ChevronDown que estava faltando!
import { Scan, Plus, Maximize, AlertCircle, FileBox, Ruler, Loader2, X, Settings, ChevronDown } from 'lucide-react';

export const OCRManager = () => {
    const { can } = usePermissions();
    const [pacotes, setPacotes] = useState<any[]>([]);
    const [selectedPacote, setSelectedPacote] = useState<number | null>(null);
    const [selectedFolha, setSelectedFolha] = useState<string>("A1");
    const [step, setStep] = useState<'select' | 'calibrate'>('select');
    const [loading, setLoading] = useState(true);
    
    // Estados para o Novo Pacote
    const [isNewPackageModalOpen, setIsNewPackageModalOpen] = useState(false);
    const [newPackageName, setNewPackageName] = useState("");
    const [isRenamePackageModalOpen, setIsRenamePackageModalOpen] = useState(false);
    const [renamePackageName, setRenamePackageName] = useState("");

    // Busca os pacotes cadastrados no SQL
    const carregarPacotes = () => {
        setLoading(true);
        fetch('/api/admin/ocr/pacotes')
            .then(async res => {
                if (!res.ok) throw new Error(await responseMessage(res));
                return res.json();
            })
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

    const responseMessage = async (response: Response) => {
        const body = await response.json().catch(() => null);
        return body?.message || `HTTP ${response.status}`;
    };

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
                throw new Error(await responseMessage(response));
            }
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "Erro ao salvar pacote no banco.", { id: tid });
        }
    };

    const handleRenamePackage = async () => {
        if (!selectedPacote || !renamePackageName.trim()) {
            toast.error("Digite um nome para o template.");
            return;
        }
        const tid = toast.loading("Renomeando template...");
        try {
            const response = await fetch(`/api/admin/ocr/pacotes/${selectedPacote}`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Nome_Pacote: renamePackageName.toUpperCase() })
            });
            if (!response.ok) throw new Error(await responseMessage(response));
            toast.success("Template renomeado.", { id: tid });
            setIsRenamePackageModalOpen(false);
            carregarPacotes();
        } catch (error) {
            toast.error(error instanceof Error ? error.message : "Erro ao renomear template.", { id: tid });
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
        <div className="min-h-screen bg-slate-950 p-6 md:p-8 animate-in fade-in duration-700 font-sans">
            
            {/* CABEÇALHO COMPACTO INTEGRADO */}
            <div className="flex flex-col md:flex-row items-start md:items-center justify-between mb-8 pb-6 border-b border-border/60 gap-4">
                <div className="flex items-center gap-4">
                    <div className="p-3 bg-gradient-to-br from-primary to-blue-600 rounded-2xl shadow-lg shadow-primary/20">
                        <Scan className="text-white w-6 h-6" />
                    </div>
                    <div>
                        <h1 className="text-2xl md:text-3xl font-black text-foreground uppercase italic tracking-tight leading-none">
                            Auditoria de Qualidade <span className="text-transparent bg-clip-text bg-gradient-to-r from-white to-slate-400">de Documentos</span>
                        </h1>
                        <p className="text-slate-400 text-[10px] font-bold uppercase tracking-[0.2em] mt-2">
                            Leitura e validação automática de arquivos PDF
                        </p>
                    </div>
                </div>
            </div>

            {/* ÁREA DE CONFIGURAÇÃO (GRID DE CARDS ANALÍTICOS COMPACTOS) */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
                
                {/* CARD 1. SELEÇÃO DO PACOTE */}
                <div className="bg-slate-900 border border-border/60 p-6 rounded-2xl space-y-5 shadow-2xl relative overflow-hidden group hover:border-primary/20 transition-all">
                    <div className="absolute top-0 right-0 p-4 opacity-5 group-hover:opacity-10 transition-opacity"><FileBox size={80} /></div>
                    <div className="flex items-center justify-between gap-3 relative z-10">
                        <h4 className="text-[11px] font-black text-primary uppercase tracking-[0.15em] flex items-center gap-2">
                            <Settings size={16} /> 1. Pacote de Normas (Empresa)
                        </h4>
                        <button 
                            onClick={() => setIsNewPackageModalOpen(true)}
                            className="flex items-center gap-1.5 text-[9px] font-black uppercase tracking-wider bg-primary/10 text-primary px-3 py-1.5 rounded-lg border border-primary/20 hover:bg-primary hover:text-white transition-all shadow-lg shadow-primary/5 active:scale-95"
                        >
                            <Plus size={12} /> Novo
                        </button>
                        <button
                            disabled={!selectedPacote}
                            onClick={() => { const item = pacotes.find(p => p.id === selectedPacote); setRenamePackageName(item?.nome || ""); setIsRenamePackageModalOpen(true); }}
                            className="flex items-center gap-1.5 text-[9px] font-black uppercase tracking-wider bg-foreground/5 text-slate-200 px-3 py-1.5 rounded-lg border border-border hover:bg-foreground/10 disabled:opacity-40 transition-all"
                        >
                            Renomear
                        </button>
                    </div>
                    
                    <div className="relative group/select z-10">
                        <select 
                            id="pacote-select"
                            title="Selecione qual pacote de normas de carimbo você deseja configurar"
                            className="w-full bg-slate-950/50 border-2 border-border/60 rounded-xl p-4 text-xs text-foreground font-bold outline-none focus:border-primary/50 appearance-none transition-all group-hover/select:bg-slate-900/80 cursor-pointer disabled:opacity-50"
                            onChange={(e) => setSelectedPacote(Number(e.target.value))}
                            value={selectedPacote || ""}
                            disabled={loading}
                        >
                            <option value="" disabled className="text-slate-500">
                                {loading ? "Carregando pacotes..." : "Selecione um pacote de visão..."}
                            </option>
                            {pacotes.map(p => (
                                <option key={p.id} value={p.id} className="text-foreground font-bold">
                                    🏢 {p.nome}
                                </option>
                            ))}
                        </select>
                        {loading && <Loader2 className="absolute right-4 top-1/2 -translate-y-1/2 text-primary animate-spin" size={16} />}
                        <ChevronDown className="absolute right-4 top-1/2 -translate-y-1/2 text-slate-500 pointer-events-none group-hover/select:text-primary transition-colors" size={18} />
                    </div>
                </div>

                {/* CARD 2. SELEÇÃO DA FOLHA */}
                <div className="bg-slate-900 border border-border/60 p-6 rounded-2xl space-y-5 shadow-2xl relative overflow-hidden group hover:border-primary/20 transition-all">
                    <div className="absolute top-0 right-0 p-4 opacity-5 group-hover:opacity-10 transition-opacity"><Ruler size={80} /></div>
                    <h4 className="text-[11px] font-black text-primary uppercase tracking-[0.15em] flex items-center gap-2 relative z-10">
                        <Settings size={16} /> 2. Tamanho da Folha Base
                    </h4>
                    <div className="relative group/select z-10">
                        <select 
                            id="folha-select"
                            title="Selecione o padrão e tamanho da prancha"
                            className="w-full bg-slate-950/50 border-2 border-border/60 rounded-xl p-4 text-xs text-foreground font-bold outline-none focus:border-primary/50 appearance-none transition-all group-hover/select:bg-slate-900/80 cursor-pointer"
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
                        <ChevronDown className="absolute right-4 top-1/2 -translate-y-1/2 text-slate-500 pointer-events-none group-hover/select:text-primary transition-colors" size={18} />
                    </div>
                    {/* Cada formato (perfil) tem suas próprias coordenadas; os CAMPOS auditados são do template (comuns a todos). */}
                    <p className="relative z-10 text-[10px] leading-relaxed text-slate-400">
                        Cada perfil de folha tem suas coordenadas; os campos auditados são do template. Qual perfil cada valor do
                        <strong className="text-slate-200"> Format da LDP</strong> usa é definido no mapeamento abaixo.
                    </p>
                </div>

                {/* CARD 3. AÇÃO (COMPACTO) */}
                <div className="bg-slate-900 border border-border/60 p-6 rounded-2xl flex flex-col justify-between gap-5 shadow-2xl relative overflow-hidden group hover:border-emerald-500/20 transition-all xl:col-span-1 md:col-span-2 xl:row-span-1">
                    <div className="p-4 bg-amber-500/10 border border-amber-500/20 rounded-xl flex items-start gap-3 relative z-10">
                        <AlertCircle className="text-amber-500 shrink-0 mt-0.5" size={18} />
                        <div>
                            <h4 className="text-[10px] font-black uppercase text-amber-500 tracking-widest mb-1 leading-none">Atenção ao Padrão</h4>
                            <p className="text-[10px] text-amber-200/70 leading-relaxed font-medium">
                                Certifique-se de que o PDF corresponde <strong>exatamente</strong> ao tamanho da folha base selecionado acima.
                            </p>
                        </div>
                    </div>
                    <button
                        disabled={!selectedPacote || loading}
                        onClick={() => setStep('calibrate')}
                        className="group w-full h-14 flex items-center justify-center gap-3 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 disabled:from-slate-800 disabled:to-slate-800 disabled:text-slate-500 text-white font-black uppercase text-xs rounded-xl transition-all shadow-2xl shadow-emerald-600/10 active:scale-[0.98] relative z-10"
                    >
                        <Maximize size={18} className={!selectedPacote ? "opacity-50" : "group-hover:scale-110 transition-transform"} />
                        Abrir Calibrador
                    </button>
                </div>
            </div>

            {/* Mapeamento de formatos da LDP do template selecionado (nenhum "D = ANSI_D" global). */}
            {selectedPacote && <div className="mt-6"><FormatMappingPanel pacoteId={selectedPacote} podeEditar={can("settings.projects.edit")} /></div>}

            {/* ========================================================================= */}
            {/* MODAL PARA NOVO PACOTE (COMPACTA E INTEGRADA AO ESTILO) */}
            {/* ========================================================================= */}
            {isNewPackageModalOpen && (
                <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/80 backdrop-blur-sm p-4 animate-in fade-in duration-200">
                    <div className="bg-slate-900 border border-border rounded-2xl p-6 w-full max-w-sm shadow-[0_0_50px_rgba(0,0,0,0.5)] animate-in zoom-in-95 duration-200 space-y-6">
                        
                        <div className="flex items-center justify-between">
                            <div>
                                <h3 className="text-lg font-black uppercase italic tracking-tighter text-foreground leading-none">
                                    Novo <span className="text-primary">Pacote</span>
                                </h3>
                                <p className="text-[9px] text-slate-400 mt-1 font-bold uppercase tracking-widest">
                                    Defina o nome do padrão de carimbo
                                </p>
                            </div>
                            <button onClick={() => setIsNewPackageModalOpen(false)} title="Fechar" aria-label="Fechar" className="p-1.5 bg-foreground/5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-all">
                                <X size={16} />
                            </button>
                        </div>

                        <input 
                            autoFocus
                            type="text"
                            placeholder="EX: PETROBRAS REV-05"
                            value={newPackageName}
                            onChange={(e) => setNewPackageName(e.target.value)}
                            onKeyDown={(e) => e.key === 'Enter' && handleSaveNewPackage()}
                            className="w-full p-4 bg-slate-950/50 border border-border rounded-xl outline-none focus:border-primary/50 font-bold uppercase text-xs text-foreground placeholder:text-slate-600 shadow-inner transition-all"
                        />

                        <div className="flex gap-3 pt-4 border-t border-border/60">
                            <button 
                                onClick={() => setIsNewPackageModalOpen(false)}
                                className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest text-slate-400 hover:text-foreground hover:bg-foreground/5 rounded-lg transition-all"
                            >
                                Cancelar
                            </button>
                            <button 
                                onClick={handleSaveNewPackage}
                                className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest bg-gradient-to-r from-primary to-blue-600 text-white rounded-lg shadow-lg shadow-primary/20 hover:opacity-90 active:scale-95 transition-all"
                            >
                                Gravar
                            </button>
                        </div>
                    </div>
                </div>
            )}
            {isRenamePackageModalOpen && (
                <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-950/80 backdrop-blur-sm p-4 animate-in fade-in duration-200">
                    <div className="bg-slate-900 border border-border rounded-2xl p-6 w-full max-w-sm shadow-[0_0_50px_rgba(0,0,0,0.5)] space-y-6">
                        <div className="flex items-center justify-between">
                            <div><h3 className="text-lg font-black uppercase italic tracking-tighter text-foreground leading-none">Renomear <span className="text-primary">Template</span></h3><p className="text-[9px] text-slate-400 mt-1 font-bold uppercase tracking-widest">Atualize o nome do padrão de carimbo</p></div>
                            <button onClick={() => setIsRenamePackageModalOpen(false)} title="Fechar" aria-label="Fechar" className="p-1.5 bg-foreground/5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-all"><X size={16} /></button>
                        </div>
                        <input autoFocus type="text" value={renamePackageName} onChange={(e) => setRenamePackageName(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && handleRenamePackage()} className="w-full p-4 bg-slate-950/50 border border-border rounded-xl outline-none focus:border-primary/50 font-bold uppercase text-xs text-foreground" />
                        <div className="flex gap-3 pt-4 border-t border-border/60"><button onClick={() => setIsRenamePackageModalOpen(false)} className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest text-slate-400 hover:text-foreground hover:bg-foreground/5 rounded-lg transition-all">Cancelar</button><button onClick={handleRenamePackage} className="flex-1 py-3 text-[9px] font-black uppercase tracking-widest bg-gradient-to-r from-primary to-blue-600 text-white rounded-lg">Gravar</button></div>
                    </div>
                </div>
            )}
        </div>
    );
};
