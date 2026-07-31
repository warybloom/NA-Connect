import React, { useState, useEffect } from 'react';
import { Clock, Plus, Trash2, Save, AlertCircle, LayoutGrid, Search } from 'lucide-react';
import { toast } from 'react-hot-toast';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from '@/components/alternar-modo';


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
      const res = await fetch(`/api/admin/agendamento/${projetoSelecionado.projetoId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ 
          HorariosSync: scheduleString,
          NomeProjeto: projetoSelecionado.nome_Projeto 
        })
      });

      if (res.ok) {
        toast.success("Agenda salva com sucesso!");
        carregarProjetos(); 
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
        <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col gap-8 animate-enter m-4">
          <div className="flex items-center gap-4 mb-2">
            <SidebarTrigger className="-ml-6"/>
            <ModeToggle/>
                            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
                            </a>
          </div>
    <div className="flex h-full w-full bg-background text-foreground overflow-hidden rounded-xl border border-border shadow-sm animate-enter">
      {/* Sidebar Lateral de Projetos */}
      <nav className="w-80 border-r border-border bg-muted/20 flex flex-col" aria-label="Lista de Projetos">
        <div className="p-4 border-b border-border bg-card/50">
          <h2 className="text-lg font-bold flex items-center gap-2 text-foreground">
            <LayoutGrid size={20} className="text-sky-500" />
            Projetos Ativos
          </h2>
          <div className="relative mt-3">
            <label htmlFor="busca-projeto" className="sr-only">Filtrar projetos pelo nome</label>
            <Search className="absolute left-3 top-2.5 text-muted-foreground" size={16} aria-hidden="true" />
            <input 
              id="busca-projeto"
              type="text" 
              placeholder="Buscar projeto..."
              title="Digite o nome do projeto para filtrar a lista"
              className="w-full bg-background border border-border rounded-md py-2 pl-10 pr-4 text-sm focus:ring-1 focus:ring-sky-500 text-foreground outline-none transition-all shadow-sm"
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
                ? 'bg-sky-500/10 border border-sky-500/50 text-foreground shadow-sm' 
                : 'hover:bg-muted border border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              <p className="font-bold truncate">{p.nome_Projeto}</p>
              <p className="text-[10px] uppercase tracking-widest opacity-70 mt-0.5 font-bold">
                {p.HorariosSync ? `Agendado: ${p.HorariosSync}` : 'Sem agendamento'}
              </p>
            </button>
          ))}
          {projetosFiltrados.length === 0 && (
            <p className="text-xs text-center text-muted-foreground py-4">Nenhum projeto encontrado.</p>
          )}
        </div>
      </nav>

      {/* Área Principal de Configuração */}
      <main className="flex-1 overflow-y-auto p-6 md:p-8 custom-scrollbar bg-card/30" aria-labelledby="main-title">
        {projetoSelecionado ? (
          <div className="max-w-3xl mx-auto space-y-8 animate-enter">
            <div>
              <h1 id="main-title" className="text-2xl md:text-3xl font-black text-foreground mb-2 uppercase tracking-tight">
                Agendamento de Sincronização
              </h1>
              <p className="text-sky-600 dark:text-sky-400 uppercase tracking-widest text-xs font-bold bg-sky-500/10 inline-block px-2 py-1 rounded border border-sky-500/20">
                PROJETO: {projetoSelecionado.nome_Projeto}
              </p>
            </div>

            <div className="bg-card border border-border rounded-2xl p-6 shadow-xl">
              <div className="flex flex-col sm:flex-row items-end gap-4 mb-8 bg-muted/30 p-5 rounded-xl border border-border shadow-inner">
                <div className="space-y-2 flex flex-col flex-1 w-full">
                  <label htmlFor="novo-horario-input" className="text-xs font-bold text-muted-foreground uppercase ml-1">
                    Definir Novo Horário
                  </label>
                  <input 
                    id="novo-horario-input"
                    type="time" 
                    title="Escolha um horário para o robô rodar"
                    className="bg-background border border-border p-3 rounded-lg text-xl md:text-2xl font-mono text-foreground outline-none focus:ring-2 focus:ring-sky-500 transition-all shadow-sm [color-scheme:light] dark:[color-scheme:dark] w-full"
                    value={novoHorario}
                    onChange={(e) => setNovoHorario(e.target.value)}
                  />
                </div>
                <button 
                  onClick={adicionarHorario}
                  title="Adicionar horário selecionado à lista"
                  aria-label="Adicionar horário"
                  className="w-full sm:w-auto h-[3.25rem] px-8 bg-sky-600 hover:bg-sky-500 text-white font-bold rounded-lg transition-all flex items-center justify-center gap-2 active:scale-95 shadow-md shadow-sky-600/20"
                >
                  <Plus size={24} aria-hidden="true" /> Adicionar à Grade
                </button>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {horariosLocais.map(h => (
                  <div key={h} className="bg-background border border-border p-4 rounded-xl flex items-center justify-between group hover:border-sky-500/50 hover:shadow-md transition-all">
                    <div className="flex items-center gap-3">
                      <Clock size={20} className="text-sky-500" aria-hidden="true" />
                      <span className="text-2xl font-mono font-bold text-foreground">{h}</span>
                    </div>
                    <button 
                      onClick={() => removerHorario(h)}
                      title={`Remover horário ${h}`}
                      aria-label={`Excluir horário ${h}`}
                      className="text-muted-foreground hover:text-rose-500 transition-colors p-2 rounded-full hover:bg-rose-500/10"
                    >
                      <Trash2 size={20} aria-hidden="true" />
                    </button>
                  </div>
                ))}
              </div>

              {horariosLocais.length === 0 && (
                <div className="text-center py-12 border-2 border-dashed border-border rounded-xl bg-muted/10">
                  <AlertCircle className="mx-auto text-muted-foreground mb-2 opacity-50" size={40} aria-hidden="true" />
                  <p className="text-muted-foreground font-medium text-sm">Nenhum horário agendado. O robô não iniciará sozinho.</p>
                </div>
              )}

              <button 
                onClick={salvarAgenda}
                disabled={loading}
                title="Salvar todas as alterações de horário no banco de dados"
                aria-label={loading ? "Processando..." : "Confirmar agenda no banco"}
                className={`w-full mt-8 p-4 md:p-5 rounded-xl font-black tracking-wider text-sm md:text-lg transition-all flex items-center justify-center gap-3 shadow-lg ${
                  loading 
                  ? 'bg-muted border border-border text-muted-foreground cursor-not-allowed' 
                  : 'bg-emerald-600 hover:bg-emerald-500 text-white active:scale-[0.98] shadow-emerald-600/20'
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
              <p className="text-xs text-muted-foreground leading-relaxed">
                💡 <strong className="text-foreground">Dica:</strong> Para garantir que o processamento não sobrecarregue o servidor, 
                evite agendar muitos projetos exatamente no mesmo minuto. O sistema gerencia uma fila 
                automática de execução.
              </p>
            </div>
          </div>
        ) : (
          <div className="h-full flex flex-col items-center justify-center text-muted-foreground opacity-70">
            <Clock size={80} strokeWidth={1} className="mb-4 opacity-30" aria-hidden="true" />
            <p className="text-lg md:text-xl font-medium text-center px-4">Selecione um projeto na lista lateral<br/> para configurar o cronograma.</p>
          </div>
        )}
      </main>
    </div>
    	</div>
      </SidebarInset>
    </SidebarProvider>
  );
};

export default AgendamentoRobo;