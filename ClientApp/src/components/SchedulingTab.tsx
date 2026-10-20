// BACKEND_PENDENTE: aba de agendamento (api/admin/projeto/:id) nao existe no N&A. UI transplantada literalmente. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
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
            className="flex flex-col gap-6 p-4 bg-slate-900/50 rounded-xl border border-border shadow-inner"
            aria-labelledby={`sched-title-${projectId}`}
        >
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-blue-500/20 rounded-lg">
                        <Clock className="text-blue-400" size={24} aria-hidden="true" />
                    </div>
                    <div>
                        <h3 id={`sched-title-${projectId}`} className="text-lg font-bold text-foreground">
                            Cronograma do Robô
                        </h3>
                        <p className="text-sm text-slate-400">Defina os horários em que o Forge Sync deve rodar.</p>
                    </div>
                </div>
            </div>

            <hr className="border-border/60" />

            {/* Seção de Entrada */}
            <div className="flex items-end gap-4 bg-foreground/5 p-4 rounded-lg">
                <div className="flex flex-col gap-2 flex-1">
                    <label 
                        htmlFor={`new-time-${projectId}`} 
                        className="text-xs font-medium uppercase tracking-wider text-slate-500"
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
                        className="bg-slate-950 border border-border rounded-md p-2 text-foreground text-lg focus:ring-2 focus:ring-blue-500 outline-none transition-all w-full"
                    />
                </div>
                <button 
                    onClick={addTime}
                    title="Adicionar horário selecionado"
                    aria-label="Adicionar novo horário ao cronograma"
                    className="flex items-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-6 py-2.5 rounded-md font-semibold transition-all active:scale-95 shadow-md"
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
                        className="group flex items-center justify-between bg-slate-800 border border-border/60 p-3 rounded-lg hover:border-blue-500/50 transition-all"
                    >
                        <span className="text-xl font-mono font-medium text-blue-100">{time}</span>
                        <button 
                            onClick={() => removeTime(time)}
                            title={`Remover horário ${time}`}
                            aria-label={`Remover horário ${time}`}
                            className="text-slate-500 hover:text-red-400 p-1 transition-colors rounded-md hover:bg-red-500/10"
                        >
                            <Trash2 size={18} aria-hidden="true" />
                        </button>
                    </div>
                ))}
            </div>

            {times.length === 0 && (
                <div 
                    className="flex items-center gap-3 p-4 bg-amber-500/10 border border-amber-500/20 rounded-lg"
                    role="alert"
                >
                    <AlertCircle className="text-amber-500" size={20} aria-hidden="true" />
                    <p className="text-sm text-amber-200">
                        Nenhum horário definido. O robô entrará em modo "Apenas Manual".
                    </p>
                </div>
            )}

            <button 
                onClick={handleSave}
                disabled={isSaving}
                title="Salvar alterações no servidor"
                aria-label={isSaving ? "Salvando alterações" : "Salvar agenda no projeto"}
                className={`flex items-center justify-center gap-3 p-4 rounded-xl font-bold text-lg transition-all
                    ${isSaving 
                        ? 'bg-slate-700 cursor-not-allowed opacity-70' 
                        : 'bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 shadow-lg shadow-emerald-900/20'
                    }`}
            >
                {isSaving ? (
                    "PROCESSANDO..."
                ) : (
                    <>
                        <Save size={22} aria-hidden="true" /> 
                        <span>SALVAR AGENDA NO PROJETO</span>
                    </>
                )}
            </button>
        </section>
    );
};