import { useEffect, useState } from 'react';
import { ArrowDown, ArrowUp, GripVertical, Save, Play, Cpu } from 'lucide-react';
import './robot-configuration.css';
import { usePermissions } from '@/features/permissions/PermissionProvider';
import { useRobotExecution } from '@/features/robot/RobotExecutionProvider';
import type { StatusRobo } from '@/features/robot/robotExecution';

interface Selection { projectId: string; order: number; enabled: boolean; stages: string[] }
interface Robot { id: string; name: string; enabled: boolean; projects: Selection[]; schedule: null }
interface Configuration { version: number; revision: number; savedAt: string | null; robots: Robot[] }
interface Project { projectId: string; name: string; hubId: string; active: boolean; tipoLdp: number }
interface Stage { id: string; name: string; order: number; available: boolean; dependencies: string; canRunAlone: boolean }
interface Response { configuration: Configuration; projects: Project[]; stages: Stage[]; storage: string }
interface Status { state: string; message: string; robotId?: string; projectName: string | null; currentStage?: string; queuePosition: number; queueTotal: number; nextProject?: string; startedAt: string | null; projects: { projectId: string; name: string; state: string; message?: string }[] }
async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(url, { credentials: 'include', ...options });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.message ?? `Servidor indisponível (HTTP ${response.status}).`);
  if (!data) throw new Error('Resposta inválida do servidor. Verifique o proxy/backend.');
  return data;
}
export default function RobotConfigurationPage() {
  const [loaded, setLoaded] = useState<Response>();
  const [robot, setRobot] = useState<Robot>();
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  // Status do robô: provider global (polling único na aplicação) — esta tela não consulta /api/worker/status por conta própria.
  const execucao = useRobotExecution();
  const status = execucao.status as Status | undefined;
  const statusError = execucao.erro ?? '';
  const [showInactive, setShowInactive] = useState(false);
  const [dragging, setDragging] = useState<string>();
  async function load() {
    setBusy(true); setError('');
    try {
      const data = await request<Response>('/api/robot-config');
      const current = data.configuration.robots.find(r => r.id === 'main');
      if (!current) throw new Error('Robô Principal não encontrado na configuração.');
      const projects = [...current.projects].sort((a, b) => a.order - b.order).map((p, i) => ({ ...p, order: i + 1 }));
      for (const project of data.projects) if (!projects.some(p => p.projectId === project.projectId))
        projects.push({ projectId: project.projectId, order: projects.length + 1, enabled: false, stages: [] });
      setLoaded(data); setRobot({ ...current, projects }); setDirty(false);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  useEffect(() => { void load(); }, []);
  const running = status?.state === 'RUNNING';
  const locked = busy || running;
  // robot.view abre a página; configurar e executar são permissões separadas (o backend valida as mesmas keys).
  const { can } = usePermissions();
  const podeConfigurar = can('robot.configure');
  const podeExecutar = can('robot.run');
  const edicaoBloqueada = locked || !podeConfigurar;
  function edit(next: Robot) { setRobot(next); setDirty(true); setNotice(''); }
  function update(projectId: string, patch: Partial<Selection>) { if (robot) edit({ ...robot, projects: robot.projects.map(p => p.projectId === projectId ? { ...p, ...patch } : p) }); }
  function move(projectId: string, position: number) {
    if (!robot || edicaoBloqueada || !Number.isFinite(position)) return;
    const projects = [...robot.projects]; const index = projects.findIndex(p => p.projectId === projectId);
    if (index < 0) return;
    const [item] = projects.splice(index, 1); projects.splice(Math.max(0, Math.min(projects.length, position - 1)), 0, item);
    edit({ ...robot, projects: projects.map((p, i) => ({ ...p, order: i + 1 })) });
  }
  async function save() {
    if (!robot || !loaded) return;
    setBusy(true); setError('');
    try {
      const configuration = await request<Configuration>(`/api/robot-config/${encodeURIComponent(robot.id)}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedRevision: loaded.configuration.revision,
          robot: { ...robot, projects: robot.projects.filter(p => p.enabled || p.stages.length > 0 || loaded.configuration.robots.find(r => r.id === robot.id)?.projects.some(saved => saved.projectId === p.projectId)) } }),
      });
      const savedRobot = configuration.robots.find(r => r.id === robot.id)!;
      setLoaded({ ...loaded, configuration }); setRobot({ ...savedRobot, projects: robot.projects.map(p => savedRobot.projects.find(saved => saved.projectId === p.projectId) ?? p) }); setDirty(false); setNotice('Configuração salva no servidor.');
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function run() {
    if (!robot || !loaded || dirty) return;
    setBusy(true); setError('');
    try {
      const value = await request<Status>('/api/worker/run-configured', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ robotId: robot.id, expectedRevision: loaded.configuration.revision }) });
      execucao.aceitar(value as unknown as StatusRobo); setNotice('Fila salva aceita pelo Worker atual. Acompanhe a execução abaixo.');
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <section className="robot-configuration">
    <header className="robot-heading"><div><p>AUTOMAÇÃO</p><h1>Configuração do Robô</h1><span>Projetos, ordem e etapas do motor atual.</span></div><Cpu size={34}/></header>
    {error && <div className="robot-error" role="alert">{error}</div>}
    {!loaded || !robot ? <div className="robot-card"><p>{busy ? 'Carregando cadastro e configuração reais…' : 'Configuração não disponível. Nenhum dado de demonstração é utilizado.'}</p><button onClick={load} disabled={busy}>Tentar novamente</button></div> : <>
      <div className="robot-card robot-toolbar"><div><h2>{robot.name}</h2><span>Última configuração salva: {loaded.configuration.savedAt ? new Date(loaded.configuration.savedAt).toLocaleString('pt-BR') : 'Ainda não salva'} · Revisão {loaded.configuration.revision}</span></div>
        <label><input type="checkbox" checked={robot.enabled} disabled={edicaoBloqueada} onChange={e => edit({ ...robot, enabled: e.target.checked })}/>{robot.enabled ? 'ATIVO' : 'INATIVO'}</label>
      </div>
      <div className="robot-toolbar"><div><h2>Fila de projetos</h2><p>Arraste ou altere a ordem pelo número/setas. As etapas seguem a sequência do Worker.</p></div><label><input type="checkbox" checked={showInactive} onChange={e => setShowInactive(e.target.checked)}/>Mostrar projetos inativos</label></div>
      {robot.projects.filter(p => showInactive || loaded.projects.find(r => r.projectId === p.projectId)?.active || p.enabled).map(item => {
        const project = loaded.projects.find(p => p.projectId === item.projectId); const active = project?.active === true;
        return <article key={item.projectId} className={`robot-card robot-project ${item.enabled ? 'selected' : ''}`}
          onDragOver={e => { if (dragging && !edicaoBloqueada) e.preventDefault(); }} onDrop={e => { e.preventDefault(); if (dragging) move(dragging, item.order); setDragging(undefined); }}>
          <div className="robot-project-head">
            <span draggable={!edicaoBloqueada} onDragStart={e => { setDragging(item.projectId); e.dataTransfer.setData('text/plain', item.projectId); e.dataTransfer.effectAllowed = 'move'; }} onDragEnd={() => setDragging(undefined)} title="Arrastar projeto" className="robot-drag"><GripVertical/></span>
            <label>Ordem<input type="number" min={1} max={robot.projects.length} value={item.order} disabled={edicaoBloqueada} aria-label={`Ordem de ${project?.name ?? item.projectId}`} onChange={e => move(item.projectId, Number(e.target.value))}/></label>
            <div className="robot-project-name"><strong>{project?.name ?? item.projectId}</strong><small>{active ? `Cadastro ativo · LDP Tipo ${project?.tipoLdp}` : 'Inativo/ausente no cadastro — execução bloqueada'}</small></div>
            <label><input type="checkbox" checked={item.enabled} disabled={edicaoBloqueada || (!active && !item.enabled)} onChange={e => update(item.projectId, { enabled: e.target.checked })}/>Ativo no robô</label><span>{item.stages.length} etapas</span>
            <button disabled={edicaoBloqueada || item.order === 1} onClick={() => move(item.projectId, item.order - 1)} aria-label={`Subir ${project?.name}`}><ArrowUp size={16}/></button><button disabled={edicaoBloqueada || item.order === robot.projects.length} onClick={() => move(item.projectId, item.order + 1)} aria-label={`Descer ${project?.name}`}><ArrowDown size={16}/></button>
          </div>
          <details><summary>Configurar etapas</summary><p>Selecione explicitamente as dependências indicadas. Etapas ausentes não são acrescentadas pelo sistema.</p><div className="robot-stages">{loaded.stages.map(stage => <label key={stage.id} className={!stage.available ? 'unavailable' : ''}>
            <input type="checkbox" checked={item.stages.includes(stage.id)} disabled={edicaoBloqueada || !active || !stage.available} onChange={e => update(item.projectId, { stages: e.target.checked ? [...item.stages, stage.id] : item.stages.filter(s => s !== stage.id) })}/>
            <span><strong>{stage.id === 'GESTAO_VISTA' ? '' : `Etapa ${stage.id} · `}{stage.name}</strong><small>{stage.dependencies}</small></span>
          </label>)}</div></details>
        </article>;
      })}
      {!robot.projects.length && <p>Nenhum projeto cadastrado disponível.</p>}
      <div className="robot-actions"><button disabled={edicaoBloqueada} onClick={save}><Save size={16}/>Salvar configuração</button><button className="robot-primary" disabled={locked || !podeExecutar || dirty || !robot.enabled || !robot.projects.some(p => p.enabled) || !!statusError} onClick={run}><Play size={16}/>Executar agora</button><span>{!podeConfigurar && !podeExecutar ? 'Somente visualização: sem permissão para configurar ou executar.' : !podeConfigurar ? 'Sem permissão para alterar a configuração.' : !podeExecutar ? 'Sem permissão para executar o robô.' : dirty ? 'Alterações não salvas. Salve antes de executar.' : 'Executa somente a configuração salva.'}</span></div>
      <p role="status">{notice}</p>
    </>}
    <section className="robot-card"><h2>Execução do Worker</h2>{statusError && <p role="alert" className="robot-error">Status indisponível: {statusError}</p>}{status ? <>
      <strong>{status.state === 'RUNNING' ? 'ROBÔ EM EXECUÇÃO' : status.message}</strong>
      <div className="robot-status"><span>Projeto atual: <b>{status.projectName ?? '—'}</b></span><span>Fila: {status.queuePosition ?? 0} / {status.queueTotal ?? 0}</span><span>Etapa: {status.currentStage ?? '—'}</span><span>Próximo: {status.nextProject ?? '—'}</span><span>Início: {status.startedAt ? new Date(status.startedAt).toLocaleString('pt-BR') : '—'}</span></div>
      <ul>{status.projects?.map(p => <li key={p.projectId}><b>{p.name}</b> · {({ PENDING: 'Aguardando', RUNNING: 'Em execução', COMPLETED: 'Concluído', ERROR: 'Erro' } as Record<string, string>)[p.state]}{p.message && <small> — {p.message}</small>}</li>)}</ul>
      <p>Concluídos: {status.projects?.filter(p => p.state === 'COMPLETED').length ?? 0} · Com erro: {status.projects?.filter(p => p.state === 'ERROR').length ?? 0}</p>
    </> : <p>Consultando status real…</p>}<small>Cancelamento manual indisponível: chamadas legadas não oferecem interrupção segura. Não há encerramento forçado de processos.</small></section>
  </section>;
}
