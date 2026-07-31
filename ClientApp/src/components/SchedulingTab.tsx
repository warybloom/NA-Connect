import React, { useState, useEffect } from 'react';
import { Clock, Plus, Trash2, Save, AlertCircle } from 'lucide-react';
import { toast } from 'react-hot-toast';

interface SchedulingTabProps {
  projectId: number;
  initialSchedule: string; // Recebe o que está no banco (ex: "07:00,13:00")
  onRefresh: () => void;   // Função para recarregar os dados após salvar
}

export const SchedulingTab: React.FC<SchedulingTabProps> = ({ 
  projectId, 
  initialSchedule, 
  onRefresh 
}) => {
  const [times, setTimes] = useState<string[]>([]);
  const [newTime, setNewTime] = useState("08:00");
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (initialSchedule) {
      const sortedTimes = initialSchedule.split(',').filter(t => t !== "").sort();
      setTimes(sortedTimes);
    } else {
      setTimes([]);
    }
  }, [initialSchedule, projectId]);

  const addTime = () => {
    if (times.includes(newTime)) {
      toast.error("Este horário já está na lista!");
      return;
    }
    if (times.length >= 10) {
      toast.error("Limite máximo de 10 horários por projeto.");
      return;
    }
    const updated = [...times, newTime].sort();
    setTimes(updated);
  };

  const removeTime = (timeToRemove: string) => {
    setTimes(times.filter(t => t !== timeToRemove));
  };

  const handleSave = async () => {
    setIsSaving(true);
    const scheduleString = times.join(',');

    try {
      const res = await fetch(`/api/admin/projeto/${projectId}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ 
          HorariosSync: scheduleString 
        })
      });

      if (res.ok) {
        toast.success("Agenda sincronizada com o banco!");
        onRefresh();
      } else {
        const errData = await res.json();
        throw new Error(errData.message || "Erro ao salvar");
      }
    } catch (error) {
      console.error("Erro no agendamento:", error);
      toast.error("Falha ao salvar horários no servidor.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <section 
      className="flex flex-col gap-6 p-6 bg-card rounded-xl border border-border shadow-sm animate-enter"
      aria-labelledby={`sched-title-${projectId}`}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-blue-500/10 border border-blue-500/20 rounded-lg shadow-sm">
            <Clock className="text-blue-600 dark:text-blue-400" size={24} aria-hidden="true" />
          </div>
          <div>
            <h3 id={`sched-title-${projectId}`} className="text-lg font-black text-foreground uppercase tracking-tight">
              Cronograma do Robô
            </h3>
            <p className="text-sm text-muted-foreground font-medium">Defina os horários em que o Forge Sync deve rodar.</p>
          </div>
        </div>
      </div>

      <hr className="border-border" />

      {/* Seção de Entrada */}
      <div className="flex flex-col sm:flex-row items-end gap-4 bg-muted/30 border border-border p-5 rounded-lg shadow-inner">
        <div className="flex flex-col gap-2 flex-1 w-full">
          <label 
            htmlFor={`new-time-${projectId}`} 
            className="text-xs font-bold uppercase tracking-wider text-muted-foreground"
          >
            Novo Horário
          </label>
          <input 
            id={`new-time-${projectId}`}
            type="time" 
            value={newTime}
            onChange={(e) => setNewTime(e.target.value)}
            title="Selecione um horário para adicionar ao cronograma"
            placeholder="00:00"
            className="bg-background border border-border rounded-md p-2.5 text-foreground font-medium text-lg focus:ring-2 focus:ring-blue-500 outline-none transition-colors w-full shadow-sm [color-scheme:light] dark:[color-scheme:dark]"
          />
        </div>
        <button 
          onClick={addTime}
          title="Adicionar horário selecionado"
          aria-label="Adicionar novo horário ao cronograma"
          className="flex w-full sm:w-auto items-center justify-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-8 py-3 rounded-md font-bold transition-all active:scale-95 shadow-md"
        >
          <Plus size={20} aria-hidden="true" /> 
          <span>Adicionar</span>
        </button>
      </div>

      {/* Grid de Horários */}
      <div 
        className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3"
        role="list"
        aria-label="Lista de horários agendados"
      >
        {times.map(time => (
          <div 
            key={time} 
            role="listitem"
            className="group flex items-center justify-between bg-background border border-border p-3.5 rounded-lg hover:border-blue-500/50 hover:shadow-md transition-all"
          >
            <span className="text-xl font-mono font-bold text-blue-700 dark:text-blue-400">{time}</span>
            <button 
              onClick={() => removeTime(time)}
              title={`Remover horário ${time}`}
              aria-label={`Remover horário ${time}`}
              className="text-muted-foreground hover:text-rose-500 p-1.5 transition-colors rounded-md hover:bg-rose-500/10"
            >
              <Trash2 size={18} aria-hidden="true" />
            </button>
          </div>
        ))}
      </div>

      {times.length === 0 && (
        <div 
          className="flex items-center gap-3 p-4 bg-amber-500/10 border border-amber-500/20 rounded-lg shadow-sm"
          role="alert"
        >
          <AlertCircle className="text-amber-600 dark:text-amber-500 shrink-0" size={20} aria-hidden="true" />
          <p className="text-sm text-amber-800 dark:text-amber-200 font-medium">
            Nenhum horário definido. O robô entrará em modo "Apenas Manual".
          </p>
        </div>
      )}

      <button 
        onClick={handleSave}
        disabled={isSaving}
        title="Salvar alterações no servidor"
        aria-label={isSaving ? "Salvando alterações" : "Salvar agenda no projeto"}
        className={`flex items-center justify-center gap-3 p-4 rounded-xl font-bold text-sm uppercase tracking-widest transition-all shadow-md mt-2
          ${isSaving 
            ? 'bg-muted text-muted-foreground border border-border cursor-not-allowed' 
            : 'bg-emerald-600 hover:bg-emerald-500 text-white shadow-emerald-600/20 active:scale-[0.98]'
          }`}
      >
        {isSaving ? (
          "Processando..."
        ) : (
          <>
            <Save size={20} aria-hidden="true" /> 
            <span>Salvar Agenda no Projeto</span>
          </>
        )}
      </button>
    </section>
  );
};