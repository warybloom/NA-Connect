// Localização: src/pages/MasterAdmin/ModalEditCompany.tsx
import React, { useState, useEffect } from 'react';
import { X, Database, Bot, ChevronRight, Settings2, Blocks, Crown, BarChart3, Clock, Box } from 'lucide-react';
import { cn } from "@/lib/utils";
import toast from 'react-hot-toast';

interface ModalEditCompanyProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
  company: any; // Recebe os dados da empresa clicada
}

const MODULES_AVAILABLE = [
  { id: 'plant', name: 'Plant 3D', desc: 'Avanço 4D, Extração, Taags', icon: Box, color: 'text-emerald-600', border: 'border-emerald-200', bg: 'bg-emerald-50' },
  { id: 'pmo', name: 'Relatórios PMO', desc: 'Gestão, Cadastros, Extração DB', icon: BarChart3, color: 'text-blue-600', border: 'border-blue-200', bg: 'bg-blue-50' },
  { id: 'timesheet', name: 'PMO + Timesheet', desc: 'Relatórios 1 + Plataforma Externa', icon: Clock, color: 'text-indigo-600', border: 'border-indigo-200', bg: 'bg-indigo-50' },
  { id: 'acc_robo', name: 'ACC + Robôs', desc: 'Coordenação, Normas, Sincronização', icon: Bot, color: 'text-sky-600', border: 'border-sky-200', bg: 'bg-sky-50' },
  { id: 'premium', name: 'Premium Full', desc: 'Todos os módulos integrados', icon: Crown, color: 'text-purple-600', border: 'border-purple-200', bg: 'bg-purple-50' },
];

export default function ModalEditCompany({ isOpen, onClose, onSuccess, company }: ModalEditCompanyProps) {
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    maxProjects: 10,
    maxRobots: 2,
    activeModules: [] as string[],
  });

  // 🚀 Quando o modal abrir, carrega os dados atuais da empresa
  useEffect(() => {
    if (company) {
      setFormData({
        maxProjects: company.maxProjects || 10,
        maxRobots: company.maxSimultaneousRobots || 2,
        activeModules: company.activeModules || [],
      });
    }
  }, [company, isOpen]);

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
    if (formData.activeModules.length === 0) { toast.error("Selecione pelo menos um módulo."); return; }

    setLoading(true);
    const tid = toast.loading("Atualizando infraestrutura do cliente...");
    
    try {
      const res = await fetch("http://localhost:5277/api/AdminMaster/update-tenant", {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tenantId: company.id,
          maxProjects: formData.maxProjects,
          maxRobots: formData.maxRobots,
          activeModules: formData.activeModules
        })
      });

      if (res.ok) {
        toast.success("Licença atualizada com sucesso!", { id: tid });
        onSuccess(); 
        onClose();
      } else {
        const errorText = await res.text();
        throw new Error(errorText || "Erro ao atualizar no banco de dados.");
      }
    } catch (error: any) { 
      toast.error(error.message, { id: tid }); 
    } finally { 
      setLoading(false); 
    }
  };

  if (!isOpen || !company) return null;

  return (
    <div className="fixed inset-0 z-[99999] flex items-center justify-center p-4 bg-slate-900/50 backdrop-blur-sm font-sans">
      <div className="relative w-full max-w-2xl bg-white border border-slate-200 rounded-3xl shadow-2xl overflow-hidden flex flex-col animate-in zoom-in duration-300">
        
        <div className="flex items-center justify-between p-6 border-b border-slate-100 bg-slate-50/50">
          <div className="flex items-center gap-4">
            <div className="w-12 h-12 bg-blue-50 text-blue-600 rounded-2xl flex items-center justify-center border border-blue-100 shadow-sm">
              <Settings2 size={24} />
            </div>
            <div>
              <h2 className="text-xl font-bold text-slate-900 tracking-tight">Modificar Licença</h2>
              <p className="text-[10px] font-bold text-slate-500 uppercase tracking-widest mt-0.5">{company.tenantName}</p>
            </div>
          </div>
          <button onClick={onClose} aria-label="Fechar" className="p-2 text-slate-400 hover:text-slate-900 transition-colors"><X size={24} /></button>
        </div>

        <form id="edit-company-form" onSubmit={handleSubmit} className="p-6 md:p-8 space-y-8">
          
          <div className="space-y-4">
            <h3 className="text-[10px] font-black uppercase tracking-[0.2em] text-slate-400 flex items-center gap-2 border-b border-slate-100 pb-2">
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
                      isSelected ? `border-${mod.color.split('-')[1]}-400 ${mod.bg} shadow-sm` : "bg-white border-slate-200 hover:border-slate-300",
                      mod.id === 'premium' && "col-span-2 flex-row items-center gap-4"
                    )}
                  >
                    <mod.icon size={mod.id==='premium'?24:18} className={cn(isSelected ? mod.color : "text-slate-400")} />
                    <div>
                      <div className={cn("text-xs font-black uppercase", isSelected ? mod.color : "text-slate-600")}>{mod.name}</div>
                      <div className="text-[9px] text-slate-400 font-bold mt-0.5 leading-tight">{mod.desc}</div>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          <div className={cn("p-5 rounded-2xl space-y-3 transition-all border", formData.activeModules.includes('acc_robo') || formData.activeModules.includes('premium') ? "bg-blue-50/50 border-blue-100" : "bg-slate-50 border-slate-100 opacity-50 grayscale pointer-events-none")}>
            <h4 className="text-[10px] font-black uppercase tracking-widest text-slate-600 flex items-center gap-2">
              <Database size={12}/> Capacidade de Nuvem (Módulo ACC)
            </h4>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1">
                <label htmlFor="editProjects" className="text-[9px] font-bold text-slate-500 uppercase flex items-center gap-1">Projetos</label>
                <input id="editProjects" type="number" min="1" value={formData.maxProjects} onChange={e => setFormData({...formData, maxProjects: Number(e.target.value)})} className="w-full h-10 bg-white border border-slate-200 rounded-lg px-3 text-sm font-black text-slate-900 outline-none" />
              </div>
              <div className="space-y-1">
                <label htmlFor="editRobots" className="text-[9px] font-bold text-slate-500 uppercase flex items-center gap-1">Robôs Simutâneos</label>
                <input id="editRobots" type="number" min="1" value={formData.maxRobots} onChange={e => setFormData({...formData, maxRobots: Number(e.target.value)})} className="w-full h-10 bg-white border border-slate-200 rounded-lg px-3 text-sm font-black text-slate-900 outline-none" />
              </div>
            </div>
          </div>

        </form>

        <div className="p-6 border-t border-slate-100 bg-slate-50 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-6 py-3 rounded-xl font-bold text-sm text-slate-500 hover:bg-slate-200 transition-all">Cancelar</button>
          <button type="submit" form="edit-company-form" disabled={loading} className="bg-slate-900 hover:bg-slate-800 text-white px-8 py-3 rounded-xl font-bold text-sm transition-all flex items-center gap-2 shadow-lg disabled:opacity-50">
            {loading ? 'Salvando...' : 'Salvar Alterações'} <ChevronRight size={18} />
          </button>
        </div>
      </div>
    </div>
  );
}