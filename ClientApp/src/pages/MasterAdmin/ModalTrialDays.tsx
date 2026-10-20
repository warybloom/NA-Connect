// BACKEND_PENDENTE: gestao de tenants/licencas (api/AdminMaster/*) nao existe no N&A. UI transplantada literalmente do ForgeSync; URLs absolutas convertidas para relativas. NUNCA usar o MASTER_KEY client-side como controle de acesso real. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
// Localização: src/pages/MasterAdmin/ModalTrialDays.tsx
import React, { useState } from 'react';
import { X, Gift, Check, AlertTriangle, Building2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { cn } from "@/lib/utils"; 

interface ModalTrialDaysProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
  companies: any[]; // Recebe a lista de empresas do painel principal
}

export default function ModalTrialDays({ isOpen, onClose, onSuccess, companies }: ModalTrialDaysProps) {
  const [dias, setDias] = useState(30);
  const [tenantId, setTenantId] = useState("");
  const [loading, setLoading] = useState(false);

  const handleGerar = async () => {
    if (!tenantId) {
      toast.error("Selecione uma empresa na lista.");
      return;
    }

    setLoading(true);
    const tid = toast.loading(`Aplicando licença de ${dias} dias...`);
    
    try {
      const res = await fetch("/api/AdminMaster/generate-trial", {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenantId: Number(tenantId), dias }) 
      });

      if (res.ok) {
        toast.success("Licença atualizada com sucesso!", { id: tid });
        onSuccess(); 
        onClose();
      } else {
        const errorText = await res.text();
        throw new Error(errorText || "Erro ao atualizar licença no servidor.");
      }
    } catch (err: any) { 
      toast.error(err.message, { id: tid }); 
    } finally {
      setLoading(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[99999] bg-black/40 backdrop-blur-sm flex items-center justify-center p-4 font-sans">
      <div className="bg-card w-full max-w-md rounded-3xl shadow-2xl p-8 animate-in zoom-in duration-300 border border-border">
        
        {/* Header Clean */}
        <div className="flex items-center justify-between mb-8">
           <div className="flex items-center gap-4">
              <div className="w-12 h-12 bg-amber-500/10 text-amber-500 rounded-2xl flex items-center justify-center border border-amber-500/20 shadow-sm">
                <Gift size={24} />
              </div>
              <div>
                <h3 className="text-xl font-bold text-foreground tracking-tight">Renovar Licença</h3>
                <p className="text-[10px] font-bold text-muted-foreground uppercase tracking-widest mt-0.5">Aplicar período de teste</p>
              </div>
           </div>
           <button 
             onClick={onClose} 
             aria-label="Fechar Modal" 
             title="Fechar" 
             className="p-2 text-muted-foreground hover:text-foreground bg-muted/60 hover:bg-muted rounded-full transition-colors"
           >
              <X size={20} />
           </button>
        </div>
        
        <div className="space-y-6">
           
           {/* Dropdown de Empresas */}
           <div className="space-y-2">
              <label htmlFor="tenantSelect" className="text-[10px] font-bold text-muted-foreground uppercase ml-1">Selecione a Empresa</label>
              <div className="relative group">
                <Building2 className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground group-focus-within:text-amber-500 transition-colors" />
                <select 
                  id="tenantSelect"
                  aria-label="Selecionar Empresa"
                  title="Selecione a empresa que receberá o Trial"
                  className="w-full h-12 bg-muted/60 border border-border rounded-xl pl-12 pr-4 text-sm font-bold text-foreground focus:border-amber-500 focus:bg-card transition-all appearance-none cursor-pointer outline-none shadow-sm"
                  value={tenantId}
                  onChange={e => setTenantId(e.target.value)}
                >
                  <option value="" disabled>-- Escolha uma empresa --</option>
                  {companies && companies.map(c => (
                    <option key={c.id} value={c.id}>{c.tenantName}</option>
                  ))}
                </select>
              </div>
           </div>

           {/* Seleção de Dias e Vencimento */}
           <div className="space-y-3">
              <label htmlFor="trialDaysInput" className="text-[10px] font-bold text-muted-foreground uppercase ml-1">Duração do Período (Dias)</label>
              <div className="flex flex-wrap gap-2">
                 {[15, 30, 60].map(val => (
                    <button 
                      key={val} 
                      type="button" 
                      aria-label={`Aplicar ${val} dias`}
                      title={`Adicionar ${val} dias`}
                      onClick={() => setDias(val)} 
                      className={cn(
                        "flex-1 py-3 rounded-xl text-xs font-black transition-all border uppercase italic min-w-[60px]", 
                        dias === val ? "bg-amber-500 text-white border-amber-500 shadow-md" : "bg-card text-muted-foreground border-border hover:border-amber-500/30"
                      )}
                    >
                       {val}d
                    </button>
                 ))}
                 
                 {/* Botão de Teste: Vencido */}
                 <button 
                    type="button" 
                    aria-label="Simular licença vencida"
                    title="Definir dias negativos para forçar expiração"
                    onClick={() => setDias(-1)} 
                    className={cn(
                      "flex-1 py-3 rounded-xl text-[10px] font-black transition-all border uppercase italic flex items-center justify-center gap-1 min-w-[80px]", 
                      dias === -1 ? "bg-rose-500 text-white border-rose-500 shadow-md" : "bg-card text-rose-500 border-rose-500/30 hover:bg-rose-500/10"
                    )}
                  >
                     <AlertTriangle size={12}/> Vencido
                 </button>
                 
                 <input 
                   id="trialDaysInput" 
                   aria-label="Quantidade personalizada de dias"
                   title="Quantidade de dias" 
                   type="number" 
                   className="w-full p-3 bg-muted/60 border border-border rounded-xl text-sm font-black text-center text-foreground outline-none focus:bg-card focus:border-amber-500 mt-2 shadow-inner" 
                   value={dias} 
                   onChange={e => setDias(Number(e.target.value))} 
                 />
              </div>
           </div>

           {/* Rodapé e Ações */}
           <div className="flex items-center justify-end gap-3 pt-6 border-t border-border">
              <button 
                type="button"
                onClick={onClose} 
                aria-label="Cancelar"
                className="px-6 py-3 font-bold text-sm text-muted-foreground hover:text-foreground hover:bg-muted rounded-xl transition-all"
              >
                Cancelar
              </button>
              <button 
                type="button"
                onClick={handleGerar} 
                disabled={loading} 
                aria-label="Aplicar Licença"
                className="px-8 py-3 bg-foreground text-background font-bold text-sm rounded-xl hover:bg-foreground/90 transition-all flex items-center gap-2 shadow-lg shadow-black/5 active:scale-95 disabled:opacity-50"
              >
                 <Check size={18} strokeWidth={3}/> {loading ? "Aplicando..." : "Aplicar Licença"}
              </button>
           </div>
        </div>
      </div>
    </div>
  );
}