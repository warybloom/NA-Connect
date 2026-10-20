import type { DisciplineSubmission, PlanningWeek } from './types';
export const canEdit = (state: DisciplineSubmission['state']) => state === 'RASCUNHO' || state === 'DEVOLVIDO';
export function deadlineStatus(submission: DisciplineSubmission, deadline: string, now = Date.now()) {
  if (submission.state !== 'RASCUNHO') return submission.state;
  return now > new Date(deadline).getTime() ? 'ATRASADO PARA SUBMISSÃO' : 'PENDENTE';
}
export const dateLabel = (value: string) => new Date(`${value}T12:00:00`).toLocaleDateString('pt-BR');
export const timeLabel = (value: string) => new Date(value).toLocaleString('pt-BR');
export function selectedDocuments(week: PlanningWeek) { return week.submissions.flatMap(s => s.documents.filter(d => d.selected).map(d => ({ ...d, discipline: s.discipline, state: s.state }))); }
export function validateSubmission(week: PlanningWeek, submission: DisciplineSubmission) {
  const rows = submission.documents.filter(d => d.selected);
  if (!rows.length) throw new Error('Selecione pelo menos um documento.');
  for (const row of rows) {
    if (row.plannedDate < week.start || row.plannedDate > week.end || !row.plannedDate) throw new Error('Informe uma data dentro da semana para cada documento.');
    if (!row.responsible.trim()) throw new Error('Informe o responsável de cada documento programado.');
    if (row.weeklyHours !== null && (!Number.isFinite(row.weeklyHours) || row.weeklyHours < 0 || row.weeklyHours >= 1e12 || Math.abs(row.weeklyHours * 1e6 - Math.round(row.weeklyHours * 1e6)) > 0.001)) throw new Error('HH semanal deve ser positivo ou zero, com até 12 inteiros e 6 decimais.');
    if (row.note.length > 2000) throw new Error('Observação deve ter no máximo 2000 caracteres.');
  }
}
