import { useState } from "react";
import { FileWarning, PauseCircle, ShieldAlert, PenLine } from "lucide-react";
import FormDesvio from "./FormDesvio";
import FormHold from "./FormHold";
import FormNC from "./FormNC";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from "@/components/alternar-modo";
export default function TabLancamentos() {
  const [abaAtiva, setAbaAtiva] = useState("desvios");

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
    <div className=" flex flex-col gap-6 animate-enter m-6">
      
      {/* CABEÇALHO DO MÓDULO */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-sky-500/10 border border-sky-500/30 text-sky-500 rounded-lg ">
            <PenLine className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-xl font-black text-foreground uppercase tracking-tight">Cadastro de Lançamentos</h1>
            <p className="text-xs text-muted-foreground font-medium">Inclusão manual de Desvios, Holds e Não Conformidades</p>
          </div>
        </div>
      </div>

      {/* NAVEGAÇÃO INTERNA */}
      <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
        <button
          onClick={() => setAbaAtiva("desvios")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "desvios" ? "bg-primary text-white" : "text-muted-foreground hover:text-foreground hover:bg-muted border border-transparent hover:border-border"
          }`}
        >
          <FileWarning className="w-4 h-4" /> Desvios de Escopo (Pleitos)
        </button>

        <button
          onClick={() => setAbaAtiva("holds")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "holds" ? "bg-primary text-white " : "text-muted-foreground hover:text-foreground hover:bg-muted border border-transparent hover:border-border"
          }`}
        >
          <PauseCircle className="w-4 h-4" /> Controle de Holds
        </button>

        <button
          onClick={() => setAbaAtiva("ncs")}
          className={`flex items-center gap-2 whitespace-nowrap px-4 py-2 text-sm font-medium rounded-lg transition-all ${
            abaAtiva === "ncs" ? "bg-primary text-white" : "text-muted-foreground hover:text-foreground hover:bg-muted border border-transparent hover:border-border"
          }`}
        >
          <ShieldAlert className="w-4 h-4" /> Não Conformidades (NC)
        </button>
      </div>

      {/* ÁREA DO FORMULÁRIO */}
      <div className="flex-1 bg-card rounded-xl border border-border shadow-xl p-6 relative">
        {abaAtiva === "desvios" && <FormDesvio />}
        {abaAtiva === "holds" && <FormHold />}
        {abaAtiva === "ncs" && <FormNC />}
      </div>

    </div>
    	</div>
      </SidebarInset>
    </SidebarProvider>
  );
}