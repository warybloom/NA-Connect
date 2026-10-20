import { useState } from "react";
import { Cpu, Loader2, AlertTriangle } from "lucide-react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useRobotExecution } from "@/features/robot/RobotExecutionProvider";
import { corpoExecucao, interpretarRespostaInicio } from "@/features/robot/robotExecution";

/** Projeto N&A ativo que o usuário acessa (GET /api/me/projects): id = Project_ID_ACC. */
export interface RobotRunProject { id: string; name: string }

/**
 * Botão "INICIAR ROBÔ" (Home): solicita ao backend uma execução MANUAL do mesmo pipeline do Worker (POST /api/worker/run) enviando SÓ
 * Project_ID_ACC — o backend resolve o hub no cadastro e valida UserProjects + robot.run por projeto. O acompanhamento é o monitor global
 * (RobotExecutionProvider): este botão não faz polling. 409 = já existe execução → mostra a ativa no monitor, sem criar outro.
 */
export function RobotRunButton({ projects }: { projects: RobotRunProject[] }) {
  const { status, aceitar, setExpandido } = useRobotExecution();
  const [starting, setStarting] = useState(false);
  const [open, setOpen] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [erro, setErro] = useState<string>();
  const selected = selectedIds.filter(id => projects.some(p => p.id === id));
  const rodando = status?.state === 'RUNNING';

  const iniciar = async (todos = false) => {
    if (!todos && selected.length === 0) { setErro('Selecione ao menos um projeto.'); return; }
    setErro(undefined); setStarting(true);
    try {
      const r = await fetch('/api/worker/run', { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(corpoExecucao(todos ? 'todos' : selected)) });
      const resposta = interpretarRespostaInicio(r.status, await r.json().catch(() => ({})));
      if (resposta.tipo === 'iniciado') { aceitar(resposta.status); setOpen(false); setExpandido(true); toast.success('Robô iniciado.'); return; }
      if (resposta.tipo === 'ocupado') { aceitar(resposta.status); setOpen(false); setExpandido(true); toast(resposta.mensagem + ' Acompanhando a execução ativa.'); return; }
      setErro(resposta.mensagem);
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Falha de conexão com o backend.');
    } finally { setStarting(false); }
  };

  const rotulo = starting ? 'INICIANDO...' : rodando ? 'ROBÔ EM EXECUÇÃO' : 'INICIAR ROBÔ';
  return (
    <>
      <Button variant="outline" disabled={starting}
        onClick={() => { if (rodando) { setExpandido(true); return; } setOpen(true); setErro(undefined); }}
        data-testid="btn-iniciar-robo" data-state={starting ? 'STARTING' : status?.state ?? 'IDLE'}
        className="border-primary/50 text-primary hover:bg-primary/10 gap-2 font-black text-[10px] tracking-widest uppercase">
        {starting || rodando ? <Loader2 className="w-4 h-4 animate-spin" /> : <Cpu className="w-4 h-4" />} {rotulo}
      </Button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-lg" data-testid="dialogo-robo">
          <DialogHeader>
            <DialogTitle className="uppercase tracking-widest text-sm">Iniciar robô — execução manual</DialogTitle>
            <DialogDescription>Dispara o mesmo pipeline do robô automático (todas as etapas) nos projetos escolhidos. Só um ciclo roda por vez.</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <fieldset disabled={starting || rodando} className="space-y-2">
              <legend className="text-xs font-bold uppercase text-muted-foreground">Projetos N&A ativos com acesso</legend>
              <div className="max-h-60 overflow-y-auto rounded-md border border-border p-3 space-y-2">
                {projects.length === 0 && <p className="text-sm text-muted-foreground">Nenhum projeto ativo disponível para você.</p>}
                {projects.map(p => <label key={p.id} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input type="checkbox" checked={selected.includes(p.id)}
                    onChange={e => setSelectedIds(ids => e.target.checked ? [...new Set([...ids, p.id])] : ids.filter(id => id !== p.id))} />
                  <span>{p.name}</span>
                </label>)}
              </div>
              <div className="flex items-center justify-between">
                <p className="text-sm" aria-live="polite">{selected.length} projeto(s) selecionado(s)</p>
                <Button variant="outline" onClick={() => setSelectedIds([])} disabled={starting || rodando || selected.length === 0}>Limpar seleção</Button>
              </div>
            </fieldset>
            {erro && <p role="alert" data-testid="robo-erro" className="text-sm text-rose-400 flex items-center gap-2"><AlertTriangle className="w-4 h-4" /> {erro}</p>}
            <div className="flex flex-col gap-2">
              <Button onClick={() => iniciar(false)} disabled={starting || rodando || selected.length === 0} data-testid="confirmar-robo" className="font-bold text-xs uppercase">
                EXECUTAR SELECIONADOS
              </Button>
              <Button variant="outline" onClick={() => iniciar(true)} disabled={starting || rodando || projects.length === 0} data-testid="executar-todos-permitidos" className="font-bold text-xs uppercase">
                EXECUTAR TODOS PERMITIDOS
              </Button>
              <p className="text-xs text-muted-foreground">Todos permitidos = projetos ativos em que você tem acesso e permissão de executar o robô (validado no servidor).</p>
              <Button variant="outline" onClick={() => setOpen(false)}>Fechar</Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
