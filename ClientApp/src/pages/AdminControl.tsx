// Localização: src/pages/MasterAdmin/index.tsx
import React, { useState, useMemo } from 'react';
import { 
  Users, Clock, Database, Lock, Unlock, Plus, Search, 
  Calendar, ShieldAlert, Settings2, Activity, Terminal, 
  ShieldCheck, Bot, Building2, Zap, Gift, AlertTriangle, KeyRound, Server
} from 'lucide-react';
import { cn } from "@/lib/utils"; 
import toast from 'react-hot-toast'; 
import ModalRegisterCompany from './MasterAdmin/ModalRegisterCompany'; 

// ============================================================================
// 🏆 DEFINIÇÕES DE TIPOS & MOCKS (Estrutura SaaS)
// ============================================================================

type PlanLevel = 'TRIAL_FREE' | 'BASIC_AUTO' | 'PRO_SYNC' | 'ENTERPRISE';

interface CompanyLicense {
  id: number;
  tenantName: string;
  contractedPlan: PlanLevel;
  maxProjects: number;
  usedProjects: number;
  maxSimultaneousRobots: number;
  activeRobots: number;
  licenseType: 'SUBSCRIPTION' | 'TRIAL';
  startDate: string;
  endDate: string;
  status: 'ACTIVE' | 'EXPIRED' | 'SUSPENDED' | 'PENDING_SETUP';
}

const planConfig: Record<PlanLevel, { name: string, color: string, robots: number }> = {
  TRIAL_FREE: { name: 'Free Trial (30d)', color: 'text-amber-600', robots: 1 },
  BASIC_AUTO: { name: 'N&A Basic Auto', color: 'text-blue-600', robots: 2 },
  PRO_SYNC: { name: 'N&A Pro Sync', color: 'text-emerald-600', robots: 5 },
  ENTERPRISE: { name: 'N&A Enterprise', color: 'text-purple-600', robots: 10 },
};

// ============================================================================
// 🖥️ COMPONENTES AUXILIARES DE UI (Clean / Light Mode)
// ============================================================================

const StatusBadge = ({ status }: { status: CompanyLicense['status'] }) => {
  const styles: Record<CompanyLicense['status'], string> = {
    ACTIVE: "bg-emerald-50 text-emerald-700 border-emerald-200",
    EXPIRED: "bg-rose-50 text-rose-700 border-rose-200",
    SUSPENDED: "bg-amber-50 text-amber-700 border-amber-200",
    PENDING_SETUP: "bg-slate-100 text-slate-600 border-slate-200",
  };
  const labels: Record<CompanyLicense['status'], string> = {
    ACTIVE: "OPERACIONAL", EXPIRED: "EXPIRADO", SUSPENDED: "SUSPENSO", PENDING_SETUP: "AGUARDANDO"
  };

  return (
    <span className={cn("inline-flex items-center gap-1.5 px-3 py-1 rounded-full border text-[10px] font-black uppercase tracking-tighter", styles[status])}>
      <div className={cn("w-1.5 h-1.5 rounded-full", status === 'ACTIVE' ? "bg-emerald-500 animate-pulse" : "bg-current")} />
      {labels[status]}
    </span>
  );
};

const CapacityGauge = ({ used, max, label, icon: Icon, color }: { used: number, max: number, label: string, icon: any, color: string }) => {
  const percentage = Math.min((used / max) * 100, 100);
  const isOver = used >= max;
  return (
    <div className="flex flex-col gap-1.5 group">
      <div className="flex justify-between items-end text-[10px] font-bold uppercase tracking-widest">
        <span className="text-slate-500 flex items-center gap-1"><Icon size={12} className={color}/> {label}</span>
        <span className={cn("font-mono font-black", isOver ? "text-rose-600" : "text-slate-700")}>{used} / {max}</span>
      </div>
      <div className="w-full h-2.5 bg-slate-100 rounded-full border border-slate-200 overflow-hidden relative shadow-inner">
        <div className={cn("h-full rounded-full transition-all duration-700 ease-out", isOver ? "bg-rose-500" : `bg-${color.split('-')[1]}-500`)} style={{ width: `${percentage}%` }} />
      </div>
    </div>
  );
};

// ============================================================================
// COMPONENTE PRINCIPAL: Master Admin Terminal
// ============================================================================

export default function AdminControl() {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [passwordInput, setPasswordInput] = useState("");
  const MASTER_KEY = "NA_SYNC_2026"; 

  const [isRegisterModalOpen, setIsRegisterModalOpen] = useState(false);
  const [busca, setBusca] = useState("");
  
  const [companies, setCompanies] = useState<CompanyLicense[]>([
    { id: 1, tenantName: "N&A Consultoria Eng.", contractedPlan: "PRO_SYNC", maxProjects: 50, usedProjects: 12, maxSimultaneousRobots: 5, activeRobots: 2, licenseType: 'SUBSCRIPTION', startDate: "2024-01-01", endDate: "2024-12-31", status: "ACTIVE" },
    { id: 2, tenantName: "TechBuild Trial Ltd.", contractedPlan: "TRIAL_FREE", maxProjects: 2, usedProjects: 2, maxSimultaneousRobots: 1, activeRobots: 1, licenseType: 'TRIAL', startDate: "2024-03-01", endDate: "2024-03-31", status: "EXPIRED" },
    { id: 3, tenantName: "PetroNave Operações", contractedPlan: "ENTERPRISE", maxProjects: 500, usedProjects: 234, maxSimultaneousRobots: 20, activeRobots: 8, licenseType: 'SUBSCRIPTION', startDate: "2024-02-15", endDate: "2025-02-14", status: "ACTIVE" },
  ]);

  const metrics = useMemo(() => {
    return {
      totalCompanies: companies.length,
      activeRobots: companies.reduce((acc, c) => acc + c.activeRobots, 0),
      expiredLicenses: companies.filter(c => c.status === 'EXPIRED').length,
      totalProjects: companies.reduce((acc, c) => acc + c.usedProjects, 0),
    }
  }, [companies]);

  const companiesFiltered = companies.filter(c => c.tenantName.toLowerCase().includes(busca.toLowerCase()));

  const handleAuth = (e: React.FormEvent) => {
    e.preventDefault();
    if (passwordInput === MASTER_KEY) setIsAuthenticated(true);
    else { alert("⚠️ ACESSO NEGADO!"); setPasswordInput(""); }
  };

  const handleCreateTrial = () => {
    const tid = toast.loading("Provisionando ambiente de teste...");
    setTimeout(() => {
        toast.dismiss(tid);
        toast.success("Trial de 30 dias provisionado!");
    }, 1500);
  }

  // --- TELA DE BLOQUEIO MESTRE (Light & Clean) ---
  if (!isAuthenticated) {
    return (
      <div className="fixed inset-0 z-[9999] bg-slate-50 flex items-center justify-center font-sans">
        <div className="absolute inset-0 bg-[linear-gradient(to_right,#cbd5e140_1px,transparent_1px),linear-gradient(to_bottom,#cbd5e140_1px,transparent_1px)] bg-[size:32px_32px]"></div>
        
        <div className="relative bg-white border border-slate-200 p-12 rounded-3xl shadow-xl w-full max-w-lg text-center space-y-8 animate-enter">
          <div className="bg-blue-50 w-24 h-24 rounded-2xl flex items-center justify-center mx-auto border border-blue-100 shadow-sm">
            <KeyRound className="text-blue-600 w-12 h-12" />
          </div>
          
          <div>
            <h2 className="text-slate-900 text-3xl font-black uppercase tracking-tight">Connect <span className="text-blue-600">Core</span></h2>
            <p className="text-slate-500 text-xs font-bold uppercase tracking-widest mt-2 border border-slate-100 bg-slate-50 inline-block px-4 py-1.5 rounded-full">Terminal Mestre Nível Overlord</p>
          </div>
          
          <form onSubmit={handleAuth} className="space-y-5">
            <input 
              aria-label="Chave Mestra" title="Chave Mestra" type="password" 
              value={passwordInput} onChange={(e) => setPasswordInput(e.target.value)}
              placeholder="INSIRA A CHAVE MESTRA"
              className="w-full bg-slate-50 border border-slate-200 rounded-xl p-4 text-slate-900 font-mono text-lg text-center outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-500/20 transition-all placeholder:text-slate-400"
              autoFocus required
            />
            <button aria-label="Desbloquear Terminal" title="Autenticar" type="submit" className="w-full bg-blue-600 hover:bg-blue-700 text-white font-bold uppercase tracking-widest py-4 rounded-xl transition-all shadow-md hover:shadow-lg active:scale-95 text-sm">
              Autenticar e Desbloquear
            </button>
          </form>
          
          <div className="pt-2 flex items-center justify-center gap-2 text-[10px] text-emerald-600 uppercase tracking-widest font-bold">
            <ShieldCheck size={16} /> Criptografia Quântica Ativa
          </div>
        </div>
      </div>
    );
  }

  // ============================================================================
  // COMPONENTE PRINCIPAL: Master Admin Terminal (Clean Mode)
  // ============================================================================
  return (
    <div className="min-h-screen bg-slate-50 text-slate-800 p-6 md:p-10 font-sans animate-enter">
      
      <ModalRegisterCompany 
        isOpen={isRegisterModalOpen}
        onClose={() => setIsRegisterModalOpen(false)}
        onSuccess={() => console.log("Cadastro efetuado!")}
      />

      <header className="flex flex-col md:flex-row justify-between items-start md:items-end mb-8 gap-6 border-b border-slate-200 pb-8">
        <div>
          <div className="flex items-center gap-2 text-blue-600 mb-2">
            <Terminal size={18} />
            <span className="text-[10px] font-bold uppercase tracking-[0.2em]">Forge Sync Connect Overlord</span>
          </div>
          <h1 className="text-4xl font-black text-slate-900 tracking-tight uppercase">
            Master <span className="text-blue-600">Admin</span> Terminal
          </h1>
          <p className="text-slate-500 mt-2 text-sm font-medium">Provisionamento de Tenancy, Matriz de Capacidade Operacional e Gestão de Licenciamento Global.</p>
        </div>
        
        <div className="flex flex-col sm:flex-row gap-3">
            <button aria-label="Criar Trial" title="Gerar Teste Grátis" onClick={handleCreateTrial} className="bg-white hover:bg-amber-50 text-amber-600 px-6 py-3 rounded-xl font-bold text-[11px] uppercase tracking-widest flex items-center gap-2 transition-all border border-slate-200 shadow-sm">
                <Gift size={16} strokeWidth={2.5} /> Gerar Free Trial (30d)
            </button>
            <button aria-label="Cadastrar Empresa" title="Cadastrar Novo Cliente" onClick={() => setIsRegisterModalOpen(true)} className="bg-blue-600 hover:bg-blue-700 text-white px-7 py-3 rounded-xl font-bold text-[11px] uppercase tracking-widest flex items-center gap-2 transition-all shadow-md hover:shadow-lg">
                <Plus size={16} strokeWidth={3} /> Cadastrar Empresa (SaaS)
            </button>
        </div>
      </header>

      {/* CARDS DE TELEMETRIA GLOBAL */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
        {[
          { label: "Empresas Licenciadas", value: metrics.totalCompanies, icon: Building2, color: "text-blue-600", bg: "bg-blue-50" },
          { label: "Robôs em Execução", value: metrics.activeRobots, icon: Bot, color: "text-emerald-600", bg: "bg-emerald-50" },
          { label: "Volume de Projetos", value: metrics.totalProjects, icon: Database, color: "text-purple-600", bg: "bg-purple-50" },
          { label: "Contas Expiradas", value: metrics.expiredLicenses, icon: AlertTriangle, color: "text-rose-600", bg: "bg-rose-50" },
        ].map(m => (
            <div key={m.label} className="rounded-2xl border border-slate-200 bg-white shadow-sm p-6 flex flex-col justify-center relative overflow-hidden group hover:shadow-md transition-all">
              <span className="text-[10px] font-bold text-slate-500 uppercase flex items-center gap-2 mb-3 relative z-10">
                <div className={cn("p-2 rounded-lg", m.bg)}>
                    <m.icon size={16} className={m.color} />
                </div>
                {m.label}
              </span>
              <div className="flex items-end justify-between relative z-10">
                <span className="text-4xl font-black text-slate-900 tracking-tight">{m.value}</span>
                <span className="text-[9px] font-bold bg-slate-100 px-2 py-1 rounded text-slate-500 uppercase tracking-wider">Global</span>
              </div>
            </div>
        ))}
      </div>

      {/* TABELA DE GESTÃO DE TENANCY E LICENÇAS */}
      <div className="rounded-2xl border border-slate-200 bg-white shadow-sm flex flex-col overflow-hidden">
        {/* Toolbar da Tabela */}
        <div className="p-6 border-b border-slate-100 flex flex-col md:flex-row justify-between items-start md:items-center gap-4 bg-slate-50/50">
          <h2 className="font-bold text-slate-800 flex items-center gap-2 text-base">
            <Server className="w-5 h-5 text-blue-600"/> Central de Tenancy & Capacidade
          </h2>
          
          <div className="relative w-full md:w-96 group">
            <Search className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400 group-focus-within:text-blue-600 transition-colors" size={16} />
            <input 
              aria-label="Buscar Empresa" title="Pesquisar" type="text" placeholder="PESQUISAR EMPRESA..." 
              value={busca} onChange={(e) => setBusca(e.target.value)}
              className="w-full h-11 bg-white border border-slate-200 rounded-xl pl-10 pr-4 text-xs font-medium text-slate-800 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 transition-all placeholder:text-slate-400"
            />
          </div>
        </div>

        {/* Corpo da Tabela */}
        <div className="overflow-x-auto custom-scrollbar">
          <table className="w-full text-left whitespace-nowrap">
            <thead className="bg-slate-50 text-slate-500 border-b border-slate-100">
              <tr className="text-[10px] font-bold uppercase tracking-widest">
                <th className="px-6 py-4">Identificação da Empresa</th>
                <th className="px-6 py-4">Nível do Plano</th>
                <th className="px-6 py-4 w-72">Matriz de Capacidade</th>
                <th className="px-6 py-4 text-center">Status</th>
                <th className="px-6 py-4 text-right">Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {companiesFiltered.map((c) => {
                const plan = planConfig[c.contractedPlan];
                return (
                  <tr key={c.id} className="hover:bg-slate-50/80 transition-colors group">
                    <td className="px-6 py-5">
                      <div className="flex items-center gap-4">
                        <div className="p-2.5 bg-slate-50 rounded-xl border border-slate-200 text-slate-500 group-hover:border-blue-200 group-hover:text-blue-600 transition-colors">
                            <Building2 size={18}/>
                        </div>
                        <div>
                            <span className="text-sm font-black text-slate-900 uppercase tracking-tight">{c.tenantName}</span>
                            <span className="block text-[10px] text-slate-500 font-mono mt-0.5">TENANT_ID: {c.id.toString().padStart(4, '0')} | Exp: {new Date(c.endDate).toLocaleDateString('pt-BR')}</span>
                        </div>
                      </div>
                    </td>
                    
                    <td className="px-6 py-5">
                      <span className={cn("text-[11px] font-black uppercase tracking-widest flex items-center gap-1.5", plan.color)}>
                        <Zap size={14} className="fill-current"/> {plan.name}
                      </span>
                    </td>
                    
                    <td className="px-6 py-5 space-y-3">
                      <CapacityGauge used={c.usedProjects} max={c.maxProjects} label="Projetos BIM360" icon={Database} color="text-blue-600" />
                      <CapacityGauge used={c.activeRobots} max={c.maxSimultaneousRobots} label="Instâncias de Robôs" icon={Bot} color="text-emerald-600" />
                    </td>
                    
                    <td className="px-6 py-5 text-center">
                      <StatusBadge status={c.status} />
                    </td>
                    
                    <td className="px-6 py-5 text-right">
                      <div className="flex justify-end gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                        <button aria-label="Configurar" title="Configurar Licença" className="p-2.5 bg-white hover:bg-blue-50 rounded-lg text-slate-400 hover:text-blue-600 transition-colors border border-slate-200">
                          <Settings2 size={16} />
                        </button>
                        <button aria-label={c.status === 'ACTIVE' ? "Suspender Acesso" : "Desbloquear"} title="Alterar Acesso" className="p-2.5 bg-white hover:bg-rose-50 rounded-lg text-slate-400 hover:text-rose-600 transition-colors border border-slate-200">
                          {c.status === 'ACTIVE' ? <Unlock size={16} /> : <Lock size={16} />}
                        </button>
                      </div>
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