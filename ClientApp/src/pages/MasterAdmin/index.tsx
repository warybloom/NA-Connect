// BACKEND_PENDENTE: gestao de tenants/licencas (api/AdminMaster/*) nao existe no N&A. UI transplantada literalmente do ForgeSync; URLs absolutas convertidas para relativas. NUNCA usar o MASTER_KEY client-side como controle de acesso real. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
// Localização: src/pages/MasterAdmin/index.tsx
import React, { useState, useMemo, useEffect } from 'react';
import { 
  Users, Clock, Database, Lock, Plus, Search, 
  Settings2, Terminal, Bot, Building2, Gift, Server, ShieldCheck, Blocks
} from 'lucide-react';
import { cn } from "@/lib/utils"; 
import toast from 'react-hot-toast';
import ModalRegisterCompany from './ModalRegisterCompany';
import ModalTrialDays from './ModalTrialDays';
import ModalEditCompany from './ModalEditCompany'; // 🔥 O Modal de Edição que criamos!

interface CompanyLicense {
  id: number;
  tenantName: string;
  contractedPlan: string;
  maxProjects: number;
  usedProjects: number;
  maxSimultaneousRobots: number;
  activeRobots: number;
  endDate: string;
  status: 'ACTIVE' | 'EXPIRED' | 'SUSPENDED';
  activeModules: string[]; // 🔥 Agora o React sabe ler os módulos
}

// Dicionário de cores para os Módulos ficarem bonitos na tabela
const moduleColors: Record<string, { name: string, bg: string, text: string }> = {
  'plant': { name: 'Plant 3D', bg: 'bg-emerald-500/10', text: 'text-emerald-600 dark:text-emerald-400' },
  'pmo': { name: 'PMO', bg: 'bg-blue-500/10', text: 'text-blue-600 dark:text-blue-400' },
  'timesheet': { name: 'Timesheet', bg: 'bg-indigo-500/10', text: 'text-indigo-600 dark:text-indigo-400' },
  'acc_robo': { name: 'ACC + Robôs', bg: 'bg-sky-500/10', text: 'text-sky-600 dark:text-sky-400' },
  'premium': { name: 'Premium', bg: 'bg-purple-500/10', text: 'text-purple-600 dark:text-purple-400' },
};

const StatusBadge = ({ status }: { status: CompanyLicense['status'] }) => {
  const styles = {
    ACTIVE: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20",
    EXPIRED: "bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-500/20",
    SUSPENDED: "bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20",
  };
  return (
    <span className={cn("px-3 py-1 rounded-full text-[10px] font-bold border uppercase italic", styles[status])}>
      {status === 'ACTIVE' ? "OPERACIONAL" : status}
    </span>
  );
};

// 🚀 FUNÇÃO PARA CALCULAR OS DIAS RESTANTES
const getDiasRestantes = (endDateStr: string) => {
  const end = new Date(endDateStr);
  const now = new Date();
  
  // Zera as horas para calcular apenas dias inteiros
  end.setHours(0,0,0,0);
  now.setHours(0,0,0,0);
  
  const diffTime = end.getTime() - now.getTime();
  const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
  
  if (diffDays > 0) return { texto: `${diffDays} dias restantes`, cor: "text-muted-foreground" };
  if (diffDays === 0) return { texto: `Vence hoje`, cor: "text-amber-500 font-bold" };
  return { texto: `Vencido há ${Math.abs(diffDays)} dias`, cor: "text-rose-500 font-bold" };
};

const CapacityGauge = ({ used, max, label, icon: Icon, colorClass }: any) => {
  const percentage = Math.min((used / max) * 100, 100);
  return (
    <div className="space-y-1">
      <div className="flex justify-between text-[9px] font-bold uppercase text-muted-foreground">
        <span className="flex items-center gap-1"><Icon size={10}/> {label}</span>
        <span>{used}/{max}</span>
      </div>
      <div className="w-full bg-muted h-1.5 rounded-full overflow-hidden border border-border/50">
        <div className={cn("h-full transition-all", colorClass)} style={{ width: `${percentage}%` }} />
      </div>
    </div>
  );
};

export default function MasterAdminIndex() {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [passwordInput, setPasswordInput] = useState("");
  
  const [isRegisterModalOpen, setIsRegisterModalOpen] = useState(false);
  const [isTrialModalOpen, setIsTrialModalOpen] = useState(false);
  const [editingCompany, setEditingCompany] = useState<CompanyLicense | null>(null); // 🔥 Estado do modal de edição
  
  const [companies, setCompanies] = useState<CompanyLicense[]>([]);
  const [busca, setBusca] = useState("");
  const [loading, setLoading] = useState(true);

  const API_URL = "/api/AdminMaster"; 

  const fetchTenants = async () => {
    setLoading(true);
    try {
      const res = await fetch(`${API_URL}/tenants`);
      if (res.ok) setCompanies(await res.json());
    } catch (error) {
      toast.error("Erro ao conectar com o banco de dados.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isAuthenticated) fetchTenants();
  }, [isAuthenticated]);

  const metrics = useMemo(() => ({
    totalCompanies: companies.length,
    activeRobots: companies.reduce((a, c) => a + (c.activeRobots || 0), 0),
    totalProjects: companies.reduce((a, c) => a + (c.usedProjects || 0), 0),
    alerts: companies.filter(c => c.status !== 'ACTIVE').length,
  }), [companies]);

  const handleAuth = (e: React.FormEvent) => {
    e.preventDefault();
    if (passwordInput === "NA_SYNC_2026") setIsAuthenticated(true);
    else { toast.error("Chave Mestra Inválida"); setPasswordInput(""); }
  };

  if (!isAuthenticated) {
    return (
      <div className="fixed inset-0 z-[9999] bg-muted/60 flex items-center justify-center font-sans">
        <div className="w-full max-w-md p-10 bg-card border border-border shadow-2xl rounded-3xl text-center space-y-6">
          <div className="w-20 h-20 bg-blue-500/10 text-blue-600 dark:text-blue-400 rounded-2xl flex items-center justify-center mx-auto border border-blue-500/20 shadow-inner">
            <Lock size={40} />
          </div>
          <div>
            <h2 className="text-2xl font-black text-foreground uppercase italic tracking-tighter">Terminal <span className="text-blue-600 dark:text-blue-400">Master</span></h2>
            <p className="text-muted-foreground text-xs font-bold uppercase tracking-widest mt-1">Acesso Restrito ao Desenvolvedor</p>
          </div>
          <form onSubmit={handleAuth} className="space-y-4">
            <label htmlFor="masterKey" className="sr-only">Chave Mestra</label>
            <input 
              id="masterKey" type="password" required autoFocus placeholder="CHAVE MESTRA"
              className="w-full p-4 bg-muted/60 border border-border rounded-xl text-center font-mono outline-none focus:ring-2 focus:ring-blue-500 transition-all shadow-inner"
              value={passwordInput} onChange={e => setPasswordInput(e.target.value)}
            />
            <button type="submit" className="w-full bg-foreground hover:bg-foreground/90 text-background font-black uppercase tracking-widest py-4 rounded-xl transition-all shadow-lg active:scale-95">
              Autenticar
            </button>
          </form>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background text-foreground p-8 font-sans animate-in fade-in duration-500">
      
      <ModalRegisterCompany isOpen={isRegisterModalOpen} onClose={() => setIsRegisterModalOpen(false)} onSuccess={fetchTenants} />
      <ModalTrialDays isOpen={isTrialModalOpen} onClose={() => setIsTrialModalOpen(false)} onSuccess={fetchTenants} companies={companies} />
      
      {/* 🔥 O Modal de Edição (Só abre se clicar na engrenagem) */}
      <ModalEditCompany isOpen={!!editingCompany} onClose={() => setEditingCompany(null)} onSuccess={fetchTenants} company={editingCompany} />

      <header className="flex flex-col md:flex-row justify-between items-end mb-10 gap-6 border-b border-border pb-8">
        <div className="space-y-1">
          <div className="flex items-center gap-2 text-blue-600 dark:text-blue-400 font-black text-[10px] uppercase tracking-[0.3em]">
            <ShieldCheck size={16} /> N&A Connect Overlord
          </div>
          <h1 className="text-4xl font-black text-foreground tracking-tighter uppercase italic">SaaS Command <span className="text-blue-600 dark:text-blue-400">Center</span></h1>
          <p className="text-muted-foreground text-sm font-bold uppercase tracking-widest">Provisionamento e Matriz de Capacidade Global</p>
        </div>
        
        <div className="flex gap-3">
            <button onClick={() => setIsTrialModalOpen(true)} className="bg-card hover:bg-amber-500/10 text-amber-600 dark:text-amber-400 px-6 py-3.5 rounded-xl font-black text-[11px] uppercase tracking-widest flex items-center gap-2 transition-all border border-border shadow-sm">
                <Gift size={18} strokeWidth={2.5} /> Gerar Free Trial (30d)
            </button>

            <button onClick={() => setIsRegisterModalOpen(true)} className="bg-blue-600 hover:bg-blue-700 text-white px-7 py-3.5 rounded-xl font-black text-[11px] uppercase tracking-widest flex items-center gap-2 transition-all shadow-md hover:shadow-lg">
                <Plus size={18} strokeWidth={3} /> Cadastrar Empresa (SaaS)
            </button>
        </div>
      </header>

      {/* KPI CARDS */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-6 mb-10">
        {[
          { label: "Empresas", value: metrics.totalCompanies, icon: Building2, color: "text-blue-600 dark:text-blue-400", bg: "bg-blue-500/10" },
          { label: "Robôs Ativos", value: metrics.activeRobots, icon: Bot, color: "text-emerald-600 dark:text-emerald-400", bg: "bg-emerald-500/10" },
          { label: "Projetos", value: metrics.totalProjects, icon: Database, color: "text-purple-600 dark:text-purple-400", bg: "bg-purple-500/10" },
          { label: "Alertas", value: metrics.alerts, icon: Clock, color: "text-rose-600 dark:text-rose-400", bg: "bg-rose-500/10" },
        ].map(kpi => (
          <div key={kpi.label} className="bg-card border border-border p-6 rounded-2xl shadow-sm hover:shadow-md transition-all group">
            <div className={cn("w-12 h-12 rounded-xl flex items-center justify-center mb-4 transition-transform group-hover:scale-110", kpi.bg, kpi.color)}>
              <kpi.icon size={24} />
            </div>
            <div className="text-4xl font-black text-foreground tracking-tighter italic">{kpi.value}</div>
            <div className="text-[10px] font-black text-muted-foreground uppercase tracking-[0.2em] mt-1">{kpi.label}</div>
          </div>
        ))}
      </div>

      {/* DATA TABLE */}
      <div className="bg-card border border-border rounded-3xl shadow-sm overflow-hidden">
        <div className="p-6 border-b border-border flex flex-col md:flex-row justify-between items-center gap-4 bg-muted/40">
          <h2 className="font-black text-foreground flex items-center gap-2 uppercase italic tracking-widest"><Server size={18} className="text-blue-600 dark:text-blue-400"/> Gestão de Tenancy</h2>
          <div className="relative w-full md:w-96">
            <Search className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" size={16} />
            <input 
               type="text" placeholder="BUSCAR EMPRESA NO ECOSSISTEMA..." 
               className="w-full pl-12 pr-4 py-3 bg-card border border-border rounded-xl text-xs font-bold uppercase outline-none focus:ring-2 focus:ring-blue-500/20 transition-all"
               value={busca} onChange={e => setBusca(e.target.value)}
            />
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left">
            <thead className="bg-muted/60 text-[10px] font-black text-muted-foreground uppercase tracking-widest">
              <tr>
                <th className="px-8 py-5 text-blue-600 dark:text-blue-400">Identificação do Cliente</th>
                <th className="px-8 py-5">Módulos Contratados</th>
                <th className="px-8 py-5 w-72">Matriz de Capacidade</th>
                <th className="px-8 py-5 text-center">Status</th>
                <th className="px-8 py-5 text-right">Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {companies.filter(c => c.tenantName.toLowerCase().includes(busca.toLowerCase())).map(c => {
                const tempoRestante = getDiasRestantes(c.endDate);

                return (
                  <tr key={c.id} className="hover:bg-muted/60 transition-colors">
                    <td className="px-8 py-5">
                      <div className="font-black text-foreground uppercase italic tracking-tighter">{c.tenantName}</div>
                      <div className="text-[10px] text-muted-foreground font-mono">TENANT_ID: {String(c.id).padStart(4,'0')}</div>
                    </td>
                    
                    {/* 🔥 COLUNA DE MÓDULOS */}
                    <td className="px-8 py-5">
                      <div className="flex flex-wrap gap-1.5 max-w-[250px]">
                        {c.activeModules && c.activeModules.length > 0 ? (
                          c.activeModules.map(modId => {
                             const m = moduleColors[modId] || { name: modId, bg: 'bg-muted', text: 'text-foreground/80' };
                             return (
                               <span key={modId} className={cn("px-2 py-0.5 rounded border text-[9px] font-black uppercase italic border-border", m.bg, m.text)}>
                                 {m.name}
                               </span>
                             );
                          })
                        ) : (
                           <span className="text-[10px] text-muted-foreground italic">Sem Módulos</span>
                        )}
                      </div>
                    </td>

                    <td className="px-8 py-5 space-y-3">
                       <CapacityGauge label="Projetos" used={c.usedProjects} max={c.maxProjects} icon={Database} colorClass="bg-blue-600" />
                       <CapacityGauge label="Robôs" used={c.activeRobots} max={c.maxSimultaneousRobots} icon={Bot} colorClass="bg-emerald-500" />
                    </td>
                    
                    {/* 🔥 COLUNA DE STATUS COM OS DIAS RESTANTES */}
                    <td className="px-8 py-5 text-center">
                      <div className="flex flex-col items-center gap-1.5">
                        <StatusBadge status={c.status} />
                        <span className={cn("text-[9px] uppercase", tempoRestante.cor)}>
                          {tempoRestante.texto}
                        </span>
                      </div>
                    </td>
                    
                    {/* 🔥 BOTÃO DE AÇÕES SEMPRE VISÍVEL */}
                    <td className="px-8 py-5 text-right">
                        <button 
                          aria-label="Gerenciar Licença" 
                          title="Gerenciar Módulos e Licença" 
                          onClick={() => setEditingCompany(c)} 
                          className="p-3 text-muted-foreground bg-card hover:text-blue-600 hover:bg-blue-500/10 rounded-xl border border-border hover:border-blue-500/30 transition-all shadow-sm"
                        >
                          <Settings2 size={18}/>
                        </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}