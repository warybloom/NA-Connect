// BACKEND_PENDENTE: gestao de tenants/licencas (api/AdminMaster/*) nao existe no N&A. UI transplantada literalmente do ForgeSync; URLs absolutas convertidas para relativas. NUNCA usar o MASTER_KEY client-side como controle de acesso real. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
// Localização: src/pages/MasterAdmin/ModalRegisterCompany.tsx
import React, { useState } from 'react';
import { 
  X, Building2, Mail, User, ShieldCheck, 
  Database, Bot, ChevronRight, Settings2, 
  KeySquare, Box, BarChart3, Clock, Crown, Blocks
} from 'lucide-react';
import { cn } from "@/lib/utils";
import toast from 'react-hot-toast';

interface ModalRegisterCompanyProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

// 🧩 Definição dos Módulos N&A Connect
const MODULES_AVAILABLE = [
  { id: 'plant', name: 'Plant 3D', desc: 'Avanço 4D, Extração, Taags', icon: Box, color: 'text-emerald-600 dark:text-emerald-400', border: 'border-emerald-500/30', bg: 'bg-emerald-500/10' },
  { id: 'pmo', name: 'Relatórios PMO', desc: 'Gestão, Cadastros, Extração DB', icon: BarChart3, color: 'text-blue-600 dark:text-blue-400', border: 'border-blue-500/30', bg: 'bg-blue-500/10' },
  { id: 'timesheet', name: 'PMO + Timesheet', desc: 'Relatórios 1 + Plataforma Externa', icon: Clock, color: 'text-indigo-600 dark:text-indigo-400', border: 'border-indigo-500/30', bg: 'bg-indigo-500/10' },
  { id: 'acc_robo', name: 'ACC + Robôs', desc: 'Coordenação, Normas, Sincronização', icon: Bot, color: 'text-sky-600 dark:text-sky-400', border: 'border-sky-500/30', bg: 'bg-sky-500/10' },
  { id: 'premium', name: 'Premium Full', desc: 'Todos os módulos integrados', icon: Crown, color: 'text-purple-600 dark:text-purple-400', border: 'border-purple-500/30', bg: 'bg-purple-500/10' },
];

export default function ModalRegisterCompany({ isOpen, onClose, onSuccess }: ModalRegisterCompanyProps) {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    tenantName: '',
    cnpj: '',
    adminEmail: '', 
    apsClientId: '', 
    apsClientSecret: '', 
    maxProjects: 10,
    maxRobots: 2,
    activeModules: [] as string[],
  });

  const handleCnpjChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    let value = e.target.value.replace(/\D/g, "");
    if (value.length > 14) value = value.slice(0, 14);
    const maskedValue = value.replace(/^(\d{2})(\d)/, "$1.$2").replace(/^(\d{2})\.(\d{3})(\d)/, "$1.$2.$3").replace(/\.(\d{3})(\d)/, ".$1/$2").replace(/(\d{4})(\d)/, "$1-$2");
    setFormData({ ...formData, cnpj: maskedValue });
  };

  const toggleModule = (modId: string) => {
    setFormData(prev => {
      if (modId === 'premium') {
        const isPremium = prev.activeModules.includes('premium');
        return { ...prev, activeModules: isPremium ? [] : MODULES_AVAILABLE.map(m => m.id) };
      }
      const newModules = prev.activeModules.includes(modId) 
        ? prev.activeModules.filter(id => id !== modId && id !== 'premium') 
        : [...prev.activeModules, modId];
      return { ...prev, activeModules: newModules };
    });
  };

const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (formData.cnpj.length < 18) { toast.error("CNPJ incompleto."); return; }
    if (formData.activeModules.length === 0) { toast.error("Selecione pelo menos um módulo."); return; }

    setLoading(true);
    const tid = toast.loading("Provisionando infraestrutura e módulos...");
    
    // 🚀 ADICIONADO: Garantindo que o 'plan' vá para o C# para não quebrar o SQL
    const payloadParaOBackend = {
        ...formData,
        plan: "MODULAR" 
    };

    try {
      const res = await fetch("/api/AdminMaster/register-tenant", {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payloadParaOBackend)
      });

      if (res.ok) {
        toast.success("Empresa Licenciada com Sucesso!", { id: tid });
        onSuccess(); 
        onClose();
      } else {
        // 🚀 A MÁGICA AQUI: Lemos a mensagem exata de erro que o C# mandou!
        const erroDoServidor = await res.text(); 
        throw new Error(erroDoServidor || "Erro de validação na API.");
      }
    } catch (error: any) { 
      // Agora o Toast vai te mostrar EXATAMENTE o que o C# reclamou
      toast.error(error.message, { id: tid, duration: 5000 }); 
    } finally { 
      setLoading(false); 
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[99999] flex items-center justify-center p-4 bg-black/50 backdrop-blur-sm font-sans">
      <div className="relative w-full max-w-5xl bg-card border border-border rounded-3xl shadow-2xl overflow-hidden flex flex-col max-h-[95vh] animate-in zoom-in duration-300">
        
        <div className="flex items-center justify-between p-6 border-b border-border bg-muted/40">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 bg-blue-600 text-white rounded-2xl flex items-center justify-center shadow-md">
              <Blocks size={24} />
            </div>
            <div>
              <h2 className="text-2xl font-bold text-foreground tracking-tight">Provisionamento Modular</h2>
              <p className="text-xs text-muted-foreground font-bold uppercase tracking-widest mt-0.5">Nova Instância N&A Connect</p>
            </div>
          </div>
          {/* ✅ Correção: aria-label e title */}
          <button onClick={onClose} aria-label="Fechar Modal" title="Fechar Janela" className="p-2 text-muted-foreground hover:text-foreground transition-colors">
            <X size={24} />
          </button>
        </div>

        <div className="overflow-y-auto custom-scrollbar p-6 md:p-8">
          <form id="register-company-form" onSubmit={handleSubmit} className="grid grid-cols-1 lg:grid-cols-2 gap-x-10 gap-y-8">
            
            <div className="space-y-8">
              <div className="space-y-4">
                <h3 className="text-[10px] font-black uppercase tracking-[0.2em] text-muted-foreground flex items-center gap-2 border-b border-border pb-2">
                  <User size={14} /> Informações Corporativas
                </h3>
                <div className="space-y-3">
                  {/* ✅ Correção: id, title, placeholder, aria-label */}
                  <label htmlFor="tenantName" className="sr-only">Nome Fantasia</label>
                  <input id="tenantName" aria-label="Nome Fantasia" title="Nome Fantasia" required type="text" placeholder="Nome Fantasia / Razão" value={formData.tenantName} onChange={e => setFormData({...formData, tenantName: e.target.value})} className="w-full h-12 bg-muted/60 border border-border rounded-xl px-4 text-sm font-bold text-foreground focus:border-blue-500 outline-none transition-all" />
                  
                  <div className="grid grid-cols-2 gap-3">
                    <label htmlFor="cnpj" className="sr-only">CNPJ Oficial</label>
                    <input id="cnpj" aria-label="CNPJ Oficial" title="CNPJ Oficial" required type="text" placeholder="CNPJ Oficial" value={formData.cnpj} onChange={handleCnpjChange} className="w-full h-12 bg-muted/60 border border-border rounded-xl px-4 text-sm font-bold text-foreground focus:border-blue-500 outline-none transition-all font-mono" />
                    
                    <label htmlFor="adminEmail" className="sr-only">E-mail Administrativo</label>
                    <input id="adminEmail" aria-label="E-mail Administrativo" title="E-mail Administrativo" required type="email" placeholder="E-mail Admin (Root)" value={formData.adminEmail} onChange={e => setFormData({...formData, adminEmail: e.target.value})} className="w-full h-12 bg-muted/60 border border-border rounded-xl px-4 text-sm font-bold text-foreground focus:border-blue-500 outline-none transition-all" />
                  </div>
                </div>
              </div>

              <div className="space-y-4">
                <h3 className="text-[10px] font-black uppercase tracking-[0.2em] text-muted-foreground flex items-center gap-2 border-b border-border pb-2">
                  <KeySquare size={14} /> Credenciais Autodesk (BYOK)
                </h3>
                <div className="p-5 bg-muted/60 border border-border rounded-2xl space-y-3">
                  <p className="text-[10px] text-muted-foreground font-bold uppercase mb-2 leading-relaxed">
                    * Deixe em branco para usar o Client ID compartilhado (Trial). Preencha para garantir banda dedicada ao cliente.
                  </p>
                  {/* ✅ Correção: id, title, placeholder, aria-label */}
                  <label htmlFor="apsClientId" className="sr-only">APS Client ID</label>
                  <input id="apsClientId" aria-label="APS Client ID" title="APS Client ID" type="text" placeholder="APS Client ID" value={formData.apsClientId} onChange={e => setFormData({...formData, apsClientId: e.target.value})} className="w-full h-11 bg-card border border-border rounded-lg px-4 text-xs font-mono text-foreground focus:border-ring outline-none transition-all" />
                  
                  <label htmlFor="apsClientSecret" className="sr-only">APS Client Secret</label>
                  <input id="apsClientSecret" aria-label="APS Client Secret" title="APS Client Secret" type="password" placeholder="APS Client Secret" value={formData.apsClientSecret} onChange={e => setFormData({...formData, apsClientSecret: e.target.value})} className="w-full h-11 bg-card border border-border rounded-lg px-4 text-xs font-mono text-foreground focus:border-ring outline-none transition-all" />
                </div>
              </div>
            </div>

            <div className="space-y-8">
              <div className="space-y-4">
                <h3 className="text-[10px] font-black uppercase tracking-[0.2em] text-muted-foreground flex items-center gap-2 border-b border-border pb-2">
                  <Blocks size={14} /> Módulos Contratados
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  {MODULES_AVAILABLE.map(mod => {
                    const isSelected = formData.activeModules.includes(mod.id);
                    return (
                      <div 
                        key={mod.id} onClick={() => toggleModule(mod.id)}
                        className={cn(
                          "cursor-pointer p-4 rounded-xl border transition-all flex flex-col gap-1",
                          isSelected ? `border-${mod.color.split('-')[1]}-400 ${mod.bg} shadow-sm` : "bg-card border-border hover:border-foreground/20",
                          mod.id === 'premium' && "col-span-2 flex-row items-center gap-4"
                        )}
                      >
                        <mod.icon size={mod.id==='premium'?24:18} className={cn(isSelected ? mod.color : "text-muted-foreground")} />
                        <div>
                          <div className={cn("text-xs font-black uppercase", isSelected ? mod.color : "text-foreground/80")}>{mod.name}</div>
                          <div className="text-[9px] text-muted-foreground font-bold mt-0.5 leading-tight">{mod.desc}</div>
                        </div>
                      </div>
                    )
                  })}
                </div>
              </div>

              <div className={cn("p-5 rounded-2xl space-y-3 transition-all border", formData.activeModules.includes('acc_robo') || formData.activeModules.includes('premium') ? "bg-blue-500/5 border-blue-500/20" : "bg-muted/60 border-border opacity-50 grayscale pointer-events-none")}>
                <h4 className="text-[10px] font-black uppercase tracking-widest text-foreground/80 flex items-center gap-2">
                  <Settings2 size={12}/> Capacidade de Nuvem (Módulo ACC)
                </h4>
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-1">
                    <label htmlFor="maxProjects" className="text-[9px] font-bold text-muted-foreground uppercase flex items-center gap-1"><Database size={10}/> Projetos</label>
                    <input id="maxProjects" aria-label="Limite de Projetos" title="Limite de Projetos" type="number" min="1" placeholder="Max Projetos" value={formData.maxProjects} onChange={e => setFormData({...formData, maxProjects: Number(e.target.value)})} className="w-full h-10 bg-card border border-border rounded-lg px-3 text-sm font-black text-foreground outline-none" />
                  </div>
                  <div className="space-y-1">
                    <label htmlFor="maxRobots" className="text-[9px] font-bold text-muted-foreground uppercase flex items-center gap-1"><Bot size={10}/> Robôs Simutâneos</label>
                    <input id="maxRobots" aria-label="Limite de Robôs" title="Limite de Robôs" type="number" min="1" placeholder="Max Robôs" value={formData.maxRobots} onChange={e => setFormData({...formData, maxRobots: Number(e.target.value)})} className="w-full h-10 bg-card border border-border rounded-lg px-3 text-sm font-black text-foreground outline-none" />
                  </div>
                </div>
              </div>
            </div>

          </form>
        </div>

        <div className="p-6 border-t border-border bg-muted/60 flex justify-end gap-3">
          <button type="button" onClick={onClose} aria-label="Cancelar" title="Cancelar Operação" className="px-6 py-3 rounded-xl font-bold text-sm text-muted-foreground hover:bg-secondary transition-all">Cancelar</button>
          <button type="submit" form="register-company-form" disabled={loading} aria-label="Confirmar" title="Confirmar Módulos e Licenciar" className="bg-foreground hover:bg-foreground/90 text-background px-8 py-3 rounded-xl font-bold text-sm transition-all flex items-center gap-2 shadow-lg disabled:opacity-50">
            {loading ? 'Provisionando...' : 'Confirmar Módulos'} <ChevronRight size={18} />
          </button>
        </div>
      </div>
    </div>
  );
}