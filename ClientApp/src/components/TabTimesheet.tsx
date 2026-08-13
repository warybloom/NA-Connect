import { useState, useEffect } from "react";
import { BarChart3, TrendingUp, AlertTriangle, FileSearch, CalendarClock, LayoutDashboard, Search, ChevronDown, Activity } from "lucide-react";
import ViewTimesheet from "./ViewTimesheet"; 
import ViewCurvaS from "./ViewCurvaS"; 
import ViewProducao from "./ViewProducao"; 
import ViewAvanco4D from "./ViewAvanco4D";
import ViewGestaoAVista from "./ViewGestaoAVista";
import ViewOfensores from "./ViewOfensores";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from "@/components/alternar-modo";

export default function TabTimesheet() {
  const [abaAtiva, setAbaAtiva] = useState("gestao");
  
  // O Estado global do Projeto (Por padrão ele abre em 'TODOS')
  const [projetoSelecionado, setProjetoSelecionado] = useState("TODOS");
  const [listaProjetos, setListaProjetos] = useState<any[]>([]);

  // Carrega a lista de projetos do banco de dados na inicialização
  useEffect(() => {
    const fetchProjetos = async () => {
      try {
        const res = await fetch("http://localhost:5277/api/TimesheetReport/projetos-lista");
        const json = await res.json();
        if (json.sucesso) {
          setListaProjetos(json.dados);
        }
      } catch (err) {
        console.error("Erro ao carregar projetos:", err);
      }
    };
    fetchProjetos();
  }, []);

  console.log("abaAtiva atual:", abaAtiva);

  return (

        <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col gap-1 animate-enter m-4">
          <div className="flex items-center gap-7">
            <SidebarTrigger className="-m-5"/>
            <ModeToggle/>
                            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
                            </a>
          </div>
          
    <div className="w-full h-full flex flex-col gap-4 animate-enter m-4">
      
      {/* BARRA GLOBAL SUPERIOR COM O FILTRO DE PROJETOS */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-1 border-b border-slate-700 pb-4">
        
        {/* Título do Módulo */}
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-blue-500/10 border border-blue-500/30 text-sky-500 rounded-lg">
            <Activity className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-xl font-black uppercase tracking-tight ">Painel Gerencial (PMO)</h1>
            <p className="text-xs text-slate-400 font-medium">Controle integrado de Escopo, Cronograma e Custos</p>
          </div>
        </div>

        {/* O ComboBox de Seleção Dinâmica (Select) CORRIGIDO */}
        <div className="flex items-center gap-2 bg-slate-800/80 border border-slate-600 rounded-lg p-1 pr-3 w-full md:w-auto shadow-sm">
          <div className="bg-slate-700 p-1.5 rounded-md text-slate-300">
            <Search className="w-4 h-4" />
          </div>
          <select
            value={projetoSelecionado}
            onChange={(e) => setProjetoSelecionado(e.target.value)}
            title="Selecionar Projeto"
            aria-label="Selecionar Projeto"
            className="bg-transparent text-sm font-semibold text-slate-100 outline-none cursor-pointer border-none focus:ring-0 w-full md:w-56 appearance-none"
          >
            <option value="TODOS" className="bg-slate-800 text-sky-500 font-bold">
              📊 Todos os Projetos (Portfólio)
            </option>
            {listaProjetos.map((p, idx) => (
              <option key={idx} value={p.Codigo} className="bg-slate-800 font-medium text-slate-300">
                {p.Codigo}
              </option>
            ))}
          </select>
          <ChevronDown className="w-4 h-4 text-slate-400 pointer-events-none" />
        </div>

      </div>

      {/* NAVEGAÇÃO INTERNA DO MÓDULO */}
      <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
        <button
            onClick={() => {
    console.log("cliquei em gestao");
    setAbaAtiva("gestao");
  }}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva ===  "gestao" ? "bg-primary text-white" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <LayoutDashboard className="w-4 h-4" /> Gestão à Vista
        </button>

        <button
          onClick={() => setAbaAtiva("timesheet")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "timesheet" ? "bg-primary text-primary-foreground" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <BarChart3 className="w-4 h-4" /> Financeiro / Horas
        </button>

        <button
          onClick={() => setAbaAtiva("producao")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "producao" ? "bg-primary text-primary-foreground" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <FileSearch className="w-4 h-4" /> Mapa de Calor (Status)
        </button>
        
        <button
          onClick={() => setAbaAtiva("curvas")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "curvas" ? "bg-primary text-primary-foreground" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <TrendingUp className="w-4 h-4" /> Curva S (EVM)
        </button>

        <button
          onClick={() => setAbaAtiva("avanco4d")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "avanco4d" ? "bg-primary text-white" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <CalendarClock className="w-4 h-4" /> Avanço 4D
        </button>

        <button
          onClick={() => setAbaAtiva("desvios")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "desvios" ? "bg-primary text-primary-foreground" : "text-slate-400 hover:text-white hover:bg-slate-800"
          }`}
        >
          <AlertTriangle className="w-4 h-4" /> Análise de Desvios
        </button>
      </div>

      {/* RENDERIZAÇÃO DINÂMICA DA TELA */}
      <div className="flex-1">
        {abaAtiva === "gestao" && <ViewGestaoAVista projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "timesheet" && <ViewTimesheet projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "producao" && <ViewProducao projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "curvas" && <ViewCurvaS projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "avanco4d" && <ViewAvanco4D projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "desvios" && <ViewOfensores projetoCodigo={projetoSelecionado} />}
        {abaAtiva === "desvios" && (
          <div className="text-slate-400 p-8 text-center border border-dashed border-slate-700 rounded-xl">
            Módulo Top 10 Ofensores em Construção...
          </div>
        )}
      </div>

    </div>
    	</div>
      </SidebarInset>
    </SidebarProvider>
  );
}