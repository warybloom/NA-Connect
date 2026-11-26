// BACKEND_PENDENTE: agendamento do robo (api/admin/agendamento/:id) nao existe no N&A. UI transplantada literalmente do ForgeSync. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
import React, { useState, useEffect } from 'react';
import { Clock, Plus, Trash2, Save, AlertCircle, LayoutGrid, Search } from 'lucide-react';
import { toast } from 'react-hot-toast';

// Interface baseada na estrutura do banco
interface ProjetoAgendamento {
    projetoId: number;
    nome_Projeto: string;
    HorariosSync: string; // Ex: "07:00,13:00"
    is_Ativo: boolean;
}

const AgendamentoRobo: React.FC = () => {
    const [projetos, setProjetos] = useState<ProjetoAgendamento[]>([]);
    const [projetoSelecionado, setProjetoSelecionado] = useState<ProjetoAgendamento | null>(null);
    const [horariosLocais, setHorariosLocais] = useState<string[]>([]);
    const [novoHorario, setNovoHorario] = useState("08:00");
    const [filtro, setFiltro] = useState("");
    const [loading, setLoading] = useState(false);

    // 1. Carregar lista de projetos ao abrir a página
    const carregarProjetos = async () => {
        try {
            const res = await fetch('/api/admin/projetos');
            if (!res.ok) throw new Error("Erro ao buscar projetos");
            const dados = await res.json();
            
            const formatados = dados.map((p: any) => ({ 
                ...p, 
                is_Ativo: Boolean(p.is_Ativo) 
            }));
            setProjetos(formatados);
        } catch (err) {
            toast.error("Erro ao carregar lista de projetos.");
        }
    };

    useEffect(() => { 
        carregarProjetos(); 
    }, []);

    // 2. Quando seleciona um projeto, prepara os horários
    const selecionarProjeto = (p: ProjetoAgendamento) => {
        setProjetoSelecionado(p);
        const lista = p.HorariosSync ? p.HorariosSync.split(',').filter(t => t !== "").sort() : [];
        setHorariosLocais(lista);
    };

    const adicionarHorario = () => {
        if (horariosLocais.includes(novoHorario)) {
            toast.error("Este horário já existe na lista.");
            return;
        }
        setHorariosLocais([...horariosLocais, novoHorario].sort());
    };

    const removerHorario = (h: string) => {
        setHorariosLocais(horariosLocais.filter(t => t !== h));
    };

    // 3. Salva no banco via PATCH
    const salvarAgenda = async () => {
        if (!projetoSelecionado) return;
        setLoading(true);
        const scheduleString = horariosLocais.join(',');

        try {
            // 🚀 ROTA CORRIGIDA: Agora bate exatamente com o ProjetoController.cs
            const res = await fetch(`/api/admin/agendamento/${projetoSelecionado.projetoId}`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ 
                    HorariosSync: scheduleString,
                    NomeProjeto: projetoSelecionado.nome_Projeto // Plano B para o C#
                })
            });

            if (res.ok) {
                toast.success("Agenda salva com sucesso!");
                carregarProjetos(); // Atualiza a lista lateral para refletir a mudança
            } else {
                throw new Error("Erro na resposta do servidor");
            }
        } catch (err) {
            toast.error("Erro ao salvar no servidor.");
        } finally {
            setLoading(false);
        }
    };

    const projetosFiltrados = projetos.filter(p => 
        p.nome_Projeto.toLowerCase().includes(filtro.toLowerCase())
    );

    return (
        <div className="flex h-screen bg-slate-950 text-slate-200 overflow-hidden animate-enter">
            {/* Sidebar Lateral de Projetos */}
            <nav className="w-80 border-r border-border/60 bg-slate-900/50 flex flex-col" aria-label="Lista de Projetos">
                <div className="p-4 border-b border-border/60">
                    <h2 className="text-lg font-bold flex items-center gap-2 text-foreground">
                        <LayoutGrid size={20} className="text-sky-400" />
                        Projetos Ativos
                    </h2>
                    <div className="relative mt-3">
                        <label htmlFor="busca-projeto" className="sr-only">Filtrar projetos pelo nome</label>
                        <Search className="absolute left-3 top-2.5 text-slate-500" size={16} aria-hidden="true" />
                        <input 
                            id="busca-projeto"
                            type="text" 
                            placeholder="Buscar projeto..."
                            title="Digite o nome do projeto para filtrar a lista"
                            className="w-full bg-slate-800 border border-slate-700 rounded-md py-2 pl-10 pr-4 text-sm focus:ring-1 focus:ring-sky-500 text-foreground outline-none transition-all"
                            onChange={(e) => setFiltro(e.target.value)}
                        />
                    </div>
                </div>
                
                <div className="flex-1 overflow-y-auto p-2 space-y-1 custom-scrollbar">
                    {projetosFiltrados.map(p => (
                        <button
                            key={p.projetoId}
                            onClick={() => selecionarProjeto(p)}
                            title={`Configurar horários para ${p.nome_Projeto}`}
                            className={`w-full text-left p-3 rounded-lg transition-all ${
                                projetoSelecionado?.projetoId === p.projetoId 
                                ? 'bg-sky-600/20 border border-sky-500/50 text-foreground shadow-lg' 
                                : 'hover:bg-foreground/5 border border-transparent text-slate-400'
                            }`}
                        >
                            <p className="font-bold truncate">{p.nome_Projeto}</p>
                            <p className="text-[10px] uppercase tracking-widest opacity-60 mt-0.5">
                                {p.HorariosSync ? `Agendado: ${p.HorariosSync}` : 'Sem agendamento'}
                            </p>
                        </button>
                    ))}
                </div>
            </nav>

            {/* Área Principal de Configuração */}
            <main className="flex-1 overflow-y-auto p-8 custom-scrollbar" aria-labelledby="main-title">
                {projetoSelecionado ? (
                    <div className="max-w-3xl mx-auto space-y-8 animate-enter">
                        <div>
                            <h1 id="main-title" className="text-3xl font-black text-foreground mb-2 uppercase tracking-tight">
                                Agendamento de Sincronização
                            </h1>
                            <p className="text-sky-400 uppercase tracking-widest text-xs font-bold bg-sky-400/10 inline-block px-2 py-1 rounded">
                                PROJETO: {projetoSelecionado.nome_Projeto}
                            </p>
                        </div>

                        {/* Auditoria 30/09/2026: o endpoint PATCH /api/admin/agendamento/{id} não existe no N&A e dbo.Projetos
                            não tem coluna de horários; o Worker roda na inicialização e por execução manual (Iniciar Robô).
                            Aviso explícito para não parecer que a agenda é aplicada. */}
                        <div role="note" className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm text-amber-200">
                            <strong className="block text-amber-300">Agendamento ainda não é aplicado pelo robô.</strong>
                            A gravação dos horários depende de um serviço de backend que ainda não existe no N&amp;A Connect —
                            salvar retorna erro e nenhum horário é executado. Hoje o robô roda na inicialização do sistema e pelo
                            botão “Iniciar Robô” da tela inicial.
                        </div>

                        <div className="bg-slate-900 border border-border rounded-2xl p-6 shadow-2xl">
                            <div className="flex items-end gap-4 mb-8 bg-foreground/5 p-4 rounded-xl border border-border/60">
                                <div className="space-y-2 flex flex-col">
                                    <label htmlFor="novo-horario-input" className="text-xs font-bold text-slate-500 uppercase ml-1">
                                        Definir Novo Horário
                                    </label>
                                    <input 
                                        id="novo-horario-input"
                                        type="time" 
                                        title="Escolha um horário para o robô rodar"
                                        className="bg-slate-950 border border-border p-3 rounded-lg text-2xl font-mono text-foreground outline-none focus:ring-2 focus:ring-sky-500 transition-all"
                                        value={novoHorario}
                                        onChange={(e) => setNovoHorario(e.target.value)}
                                    />
                                </div>
                                <button 
                                    onClick={adicionarHorario}
                                    title="Adicionar horário selecionado à lista"
                                    className="h-14 px-8 bg-sky-600 hover:bg-sky-500 text-white font-bold rounded-lg transition-all flex items-center gap-2 active:scale-95 shadow-lg shadow-sky-900/20"
                                >
                                    <Plus size={24} aria-hidden="true" /> Adicionar à Grade
                                </button>
                            </div>

                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                                {horariosLocais.map(h => (
                                    <div key={h} className="bg-slate-800 border border-border/60 p-4 rounded-xl flex items-center justify-between group hover:border-sky-500/30 transition-all shadow-md">
                                        <div className="flex items-center gap-3">
                                            <Clock size={20} className="text-sky-400" aria-hidden="true" />
                                            <span className="text-2xl font-mono font-bold text-foreground">{h}</span>
                                        </div>
                                        <button 
                                            onClick={() => removerHorario(h)}
                                            title={`Remover horário ${h}`}
                                            aria-label={`Excluir horário ${h}`}
                                            className="text-slate-500 hover:text-rose-400 transition-colors p-2 rounded-full hover:bg-rose-500/10"
                                        >
                                            <Trash2 size={20} aria-hidden="true" />
                                        </button>
                                    </div>
                                ))}
                            </div>

                            {horariosLocais.length === 0 && (
                                <div className="text-center py-12 border-2 border-dashed border-border/60 rounded-xl bg-foreground/[0.02]">
                                    <AlertCircle className="mx-auto text-slate-600 mb-2" size={40} aria-hidden="true" />
                                    <p className="text-slate-500 font-medium">Nenhum horário agendado. O robô não iniciará sozinho.</p>
                                </div>
                            )}

                            <button 
                                onClick={salvarAgenda}
                                disabled={loading}
                                title="Salvar todas as alterações de horário no banco de dados"
                                className={`w-full mt-8 p-5 rounded-xl font-black tracking-wider text-lg transition-all flex items-center justify-center gap-3 shadow-2xl ${
                                    loading 
                                    ? 'bg-slate-800 text-slate-500 cursor-not-allowed' 
                                    : 'bg-emerald-600 hover:bg-emerald-500 text-white active:scale-[0.98]'
                                }`}
                            >
                                {loading ? (
                                    "PROCESSANDO..."
                                ) : (
                                    <>
                                        <Save size={24} aria-hidden="true" /> 
                                        CONFIRMAR AGENDA NO BANCO
                                    </>
                                )}
                            </button>
                        </div>
                        
                        <div className="p-4 bg-sky-500/5 border border-sky-500/10 rounded-lg">
                            <p className="text-xs text-slate-500 leading-relaxed">
                                💡 <strong>Dica:</strong> Para garantir que o processamento não sobrecarregue o servidor, 
                                evite agendar muitos projetos exatamente no mesmo minuto. O sistema gerencia uma fila 
                                automática de execução.
                            </p>
                        </div>
                    </div>
                ) : (
                    <div className="h-full flex flex-col items-center justify-center text-slate-600">
                        <Clock size={80} strokeWidth={1} className="mb-4 opacity-10" aria-hidden="true" />
                        <p className="text-xl font-light">Selecione um projeto na lista lateral para configurar o cronograma.</p>
                    </div>
                )}
            </main>
        </div>
    );
};

export default AgendamentoRobo;