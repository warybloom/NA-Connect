import { useState } from "react";
import { FileWarning, PauseCircle, ShieldAlert, PenLine } from "lucide-react";
import FormDesvio from "./FormDesvio";
import FormHold from "./FormHold";
import FormNC from "./FormNC";

export default function TabLancamentos() {
  const [abaAtiva, setAbaAtiva] = useState("desvios");

  return (
    <div className="w-full h-full flex flex-col gap-6 animate-enter">
      
      {/* CABEÇALHO DO MÓDULO */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-slate-700 pb-4">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-emerald-500/10 border border-emerald-500/30 text-emerald-500 rounded-lg shadow-[0_0_15px_rgba(16,185,129,0.15)]">
            <PenLine className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-xl font-black text-slate-100 uppercase tracking-tight">Cadastro de Lançamentos</h1>
            <p className="text-xs text-slate-400 font-medium">Inclusão manual de Desvios, Holds e Não Conformidades</p>
          </div>
        </div>
      </div>

      {/* NAVEGAÇÃO INTERNA */}
      <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
        <button
          onClick={() => setAbaAtiva("desvios")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "desvios" ? "bg-amber-500 text-white shadow-[0_0_15px_rgba(245,158,11,0.4)]" : "text-slate-400 hover:text-foreground hover:bg-slate-800 border border-transparent hover:border-slate-600"
          }`}
        >
          <FileWarning className="w-4 h-4" /> Desvios de Escopo (Pleitos)
        </button>

        <button
          onClick={() => setAbaAtiva("holds")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "holds" ? "bg-sky-500 text-white shadow-[0_0_15px_rgba(14,165,233,0.4)]" : "text-slate-400 hover:text-foreground hover:bg-slate-800 border border-transparent hover:border-slate-600"
          }`}
        >
          <PauseCircle className="w-4 h-4" /> Controle de Holds
        </button>

        <button
          onClick={() => setAbaAtiva("ncs")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "ncs" ? "bg-emerald-500 text-white shadow-[0_0_15px_rgba(16,185,129,0.4)]" : "text-slate-400 hover:text-foreground hover:bg-slate-800 border border-transparent hover:border-slate-600"
          }`}
        >
          <ShieldAlert className="w-4 h-4" /> Não Conformidades (NC)
        </button>
      </div>

      {/* ÁREA DO FORMULÁRIO (CHAMA OS COMPONENTES REAIS) */}
      <div className="flex-1 bg-slate-900 rounded-xl border border-slate-700 shadow-xl p-6 relative">
        {abaAtiva === "desvios" && <FormDesvio />}
        {abaAtiva === "holds" && <FormHold />}
        {abaAtiva === "ncs" && <FormNC />}
      </div>

    </div>
  );
}