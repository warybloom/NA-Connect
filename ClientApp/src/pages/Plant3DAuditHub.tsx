// Localização: src/pages/Plant3DAuditHub.tsx
import React, { useState, useEffect } from 'react';
import { 
    Database, 
    Settings2, 
    PlayCircle, 
    ClipboardCheck, 
    Activity,
    FileSearch,
    RefreshCw
} from 'lucide-react';

// ✅ IMPORTAÇÕES DE COMPONENTES UI (MANTIDAS)
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";
import toast from 'react-hot-toast';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { useNavigate } from "react-router-dom";
import { ModeToggle } from '@/components/alternar-modo';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/Select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";

// ✅ CORREÇÃO DE IMPORTS DOS SEUS COMPONENTES
// Se o erro persistir com '../', tente usar o alias '@/components/' 
import PlantMappingPanel from "@/components/PlantMappingPanel";
import SyncHistoryDashboard from "@/components/SyncHistoryDashboard";
import AuditResultsTable from "@/components/AuditResultsTable"; 


export default function Plant3DAuditHub({ projectId }: { projectId: string }) {
    const [activeTab, setActiveTab] = useState("monitoramento");
    const [isExecuting, setIsExecuting] = useState(false);
    const [auditResults, setAuditResults] = useState<any[]>([]);

    

    /**
     * ✅ LÓGICA: DISPARAR O MOTOR DE CRUZAMENTO (C#)
     */
    const handleExecuteAudit = async () => {
        setIsExecuting(true);
        const tid = toast.loading("🤖 Robô iniciando cruzamento P&ID vs 3D...");
        
        try {
            // Rota que aciona o PlantAuditOrchestrator no Backend
            const res = await fetch(`/api/plant/executar-auditoria/${projectId}`, { 
                method: 'POST' 
            });

            if (res.ok) {
                toast.success("Cruzamento concluído com sucesso!", { id: tid });
                fetchResults();
                setActiveTab("resultados");
            } else {
                const error = await res.json();
                throw new Error(error.message || "Falha na execução");
            }
        } catch (error: any) {
            toast.error(`Erro: ${error.message}`, { id: tid });
        } finally {
            setIsExecuting(false);
        }
    };

    /**
     * ✅ LÓGICA: BUSCAR RESULTADOS DA TABELA [Plant3D_Ativos]
     */
    const fetchResults = async () => {
        try {
            const res = await fetch(`/api/plant/resultados/${projectId}`);
            if (res.ok) {
                const data = await res.json();
                setAuditResults(data);
            }
        } catch (error) {
            console.error("Erro ao carregar resultados:", error);
        }
    };

    useEffect(() => {
        if (activeTab === "resultados") fetchResults();
    }, [activeTab, projectId]);

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
        <div className="flex flex-col gap-6 p-8 animate-enter">
            <div className="flex items-center justify-between">
                <div>
                    <div className="flex items-center gap-2 text-blue-500 mb-1">
                        <Database className="w-4 h-4" />
                        <span className="text-[10px] font-black uppercase tracking-[0.3em]">Auditoria de Engenharia</span>
                    </div>
                    <h1 className="text-3xl font-black  hover:text-black tracking-tighter uppercase italic">
                        N&A <span className="text-blue-500">Plant Audit Hub</span>
                    </h1>
                </div>

                <div className="flex gap-3">
                    <Button 
                        onClick={handleExecuteAudit}
                        disabled={isExecuting}
                        className=" text-white font-black text-[10px] uppercase tracking-widest gap-2 shadow-lg shadow-orange-900/20"
                    >
                        {isExecuting ? <RefreshCw className="w-4 h-4 animate-spin" /> : <PlayCircle className="w-4 h-4" />}
                        {isExecuting ? "Processando..." : "Executar Cruzamento"}
                    </Button>
                </div>
            </div>

            <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
                <TabsList className="bg-blue-900 border border-black/5 backdrop-blur-md p-1 h-auto gap-1 mb-8">
                    <TabsTrigger value="monitoramento" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-blue-500 px-6 py-2.5 gap-2  data-[state=active]:text-white">
                        <Activity className="w-4 h-4" /> Monitoramento (Live)
                    </TabsTrigger>
                    
                    <TabsTrigger value="mapeamento" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-blue-500 px-6 py-2.5 gap-2 text-slate-400 data-[state=active]:text-white">
                        <Settings2 className="w-4 h-4" /> Mapeamento de Banco (De/Para)
                    </TabsTrigger>
                    
                    <TabsTrigger value="resultados" className="font-black text-[10px] tracking-[0.2em] uppercase data-[state=active]:bg-blue-500 px-6 py-2.5 gap-2 text-slate-400 data-[state=active]:text-white">
                        <ClipboardCheck className="w-4 h-4" /> Tela de Resultados
                    </TabsTrigger>
                </TabsList>

                <TabsContent value="monitoramento" className="mt-0">
                    <SyncHistoryDashboard />
                </TabsContent>

                <TabsContent value="mapeamento" className="mt-0">
                    <PlantMappingPanel projectId={projectId} />
                </TabsContent>

                <TabsContent value="resultados" className="mt-0">
                    {auditResults.length > 0 ? (
                        <AuditResultsTable data={auditResults} />
                    ) : (
                        <div className="glass-card p-20 bg-slate-900/40 border border-white/5 rounded-[2rem] text-center flex flex-col items-center gap-6">
                            <div className="p-6 bg-white/5 rounded-full border border-white/10">
                                <FileSearch className="w-12 h-12 text-slate-500" />
                            </div>
                            <h3 className="text-xl font-black uppercase italic text-white tracking-tighter">Aguardando Processamento</h3>
                            <p className="text-[10px] text-slate-500 font-bold uppercase tracking-[0.2em] mt-2 max-w-md mx-auto leading-relaxed">
                                Configure o mapeamento e execute a auditoria para visualizar as divergências.
                            </p>
                        </div>
                    )}
                </TabsContent>
            </Tabs>
        </div>
         </div>
      </SidebarInset>
    </SidebarProvider>
    );
}