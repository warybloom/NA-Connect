import { useSyncExternalStore } from 'react';
import { DEMO_DOCUMENTS, DEMO_PROJECTS, DISCIPLINES } from './constants';
import { canEdit, validateSubmission } from './utils';
import type { PlanningSnapshot, PlanningWeek, ScheduledDocument, PlanningEventName } from './types';
let snapshot: PlanningSnapshot = { weeks: [], events: [] };
const listeners = new Set<() => void>();
function publish() { snapshot = { ...snapshot }; listeners.forEach(listener => listener()); }
function event(name: PlanningEventName, weekId: string, discipline?: string) { snapshot.events = [...snapshot.events, { name, weekId, discipline, at: new Date().toISOString() }]; }
function find(weekId: string) { const week = snapshot.weeks.find(w => w.id === weekId); if (!week) throw new Error('Semana não encontrada.'); return week; }
function update(weekId: string, change: (week: PlanningWeek) => void) { const week = structuredClone(find(weekId)); change(week); snapshot.weeks = snapshot.weeks.map(w => w.id === weekId ? week : w); publish(); }
export function openDemoWeek(projectId: string, start: string, end: string, deadline: string, disciplines: string[]) {
  if (!DEMO_PROJECTS.some(p => p.id === projectId) || !start || !end || !Number.isFinite(Date.parse(start)) || !Number.isFinite(Date.parse(end)) || end < start || !Number.isFinite(Date.parse(deadline)) || !disciplines.length || disciplines.some(d => !DISCIPLINES.includes(d))) throw new Error('Preencha projeto, período válido, prazo e disciplinas esperadas.');
  if ((Date.parse(end) - Date.parse(start)) / 86400000 > 6) throw new Error('A semana deve conter de 1 a 7 dias.');
  const deadlineDate = new Date(deadline);
  const localDeadlineDay = `${deadlineDate.getFullYear()}-${String(deadlineDate.getMonth() + 1).padStart(2, '0')}-${String(deadlineDate.getDate()).padStart(2, '0')}`;
  if (localDeadlineDay > end) throw new Error('O prazo não pode ser posterior ao fim da semana (horário local).');
  if (snapshot.weeks.some(w => w.projectId === projectId && w.start <= end && w.end >= start)) throw new Error('Já existe uma semana neste período para o projeto.');
  const id = `demo-week-${crypto.randomUUID()}`;
  const week: PlanningWeek = { id, projectId, start, end, deadline: new Date(deadline).toISOString(), submissions: [...new Set(disciplines)].map(discipline => ({ discipline, state: 'RASCUNHO', history: [], documents: DEMO_DOCUMENTS.filter(d => d.projectId === projectId && d.discipline === discipline).map(d => ({ documentId: d.id, selected: false, plannedDate: start, responsible: '', weeklyHours: null, note: '' })) })) };
  snapshot.weeks = [...snapshot.weeks, week]; event('PROGRAMACAO_ABERTA', id); publish(); return id;
}
export function patchDemoDocument(weekId: string, discipline: string, documentId: string, patch: Partial<Omit<ScheduledDocument, 'documentId'>>) {
  update(weekId, week => { const sub = week.submissions.find(s => s.discipline === discipline); if (!sub || !canEdit(sub.state)) throw new Error('Esta programação está bloqueada para edição.'); sub.documents = sub.documents.map(d => d.documentId === documentId ? { ...d, ...patch } : d); });
}
export function demoAction(weekId: string, discipline: string, action: 'save' | 'submit' | 'return' | 'approve' | 'comment', comment = '') {
  update(weekId, week => {
    const sub = week.submissions.find(s => s.discipline === discipline); if (!sub) throw new Error('Disciplina não encontrada.');
    if ((action === 'save' || action === 'submit') && !canEdit(sub.state)) throw new Error('A programação está bloqueada.');
    if ((action === 'approve' || action === 'return') && sub.state !== 'ENVIADO') throw new Error('Somente programações enviadas podem ser revisadas.');
    if ((action === 'return' || action === 'comment') && !comment.trim()) throw new Error('Informe um comentário.');
    if (comment.length > 2000) throw new Error('Comentário deve ter no máximo 2000 caracteres.');
    let name: PlanningEventName | undefined;
    if (action === 'submit') { validateSubmission(week, sub); sub.state = 'ENVIADO'; name = 'PROGRAMACAO_ENVIADA'; }
    if (action === 'return') { sub.state = 'DEVOLVIDO'; sub.returnReason = comment.trim(); name = 'PROGRAMACAO_DEVOLVIDA'; }
    if (action === 'approve') { sub.state = 'APROVADO'; name = 'PROGRAMACAO_APROVADA'; }
    sub.history.push({ at: new Date().toISOString(), action: ({ save: 'Rascunho salvo', submit: 'Enviado para revisão', return: 'Devolvido', approve: 'Aprovado', comment: 'Comentário do planejador' })[action], comment: comment.trim(), actor: action === 'save' || action === 'submit' ? 'JOB_LEADER_DEMO' : 'PLANEJADOR_DEMO', documentSnapshot: structuredClone(sub.documents.filter(d => d.selected)) });
    if (name) event(name, weekId, discipline);
  });
}
openDemoWeek(DEMO_PROJECTS[0].id, '2026-09-21', '2026-09-25', '2026-09-18T15:00:00-03:00', DISCIPLINES);
export const usePlanningDemo = () => useSyncExternalStore(listener => { listeners.add(listener); return () => { listeners.delete(listener); }; }, () => snapshot);
export const getPlanningDemoSnapshot = (): Readonly<PlanningSnapshot> => structuredClone(snapshot);
