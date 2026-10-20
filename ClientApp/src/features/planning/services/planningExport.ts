import * as XLSX from 'xlsx';
import { DEMO_DOCUMENTS, DEMO_PROJECTS } from '../shared/constants';
import { selectedDocuments } from '../shared/utils';
import type { PlanningWeek } from '../shared/types';
/** Output only. No workbook is read or used as operational persistence. */
export function exportDemoWeek(week: PlanningWeek) {
  const workbook = XLSX.utils.book_new();
  const metadata = [['PROGRAMAÇÃO SEMANAL — PREVIEW DEMO'], ['Dados fictícios. Inclui rascunhos e submissões; não representa programação oficial.'], ['Projeto', DEMO_PROJECTS.find(p => p.id === week.projectId)?.name ?? week.projectId], ['Início', week.start], ['Fim', week.end], ['Prazo de submissão (UTC)', week.deadline], ['Gerado em (UTC)', new Date().toISOString()]];
  XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(metadata), 'Contexto DEMO');
  const rows = selectedDocuments(week).map(row => { const doc = DEMO_DOCUMENTS.find(d => d.id === row.documentId)!; return { 'Referência DEMO': doc.id, Documento: doc.number, Título: doc.title, Disciplina: row.discipline, 'Data planejada': row.plannedDate, Responsável: row.responsible, 'HH previsto semana': row.weeklyHours, Observação: row.note, Estado: row.state }; });
  const sheet = XLSX.utils.json_to_sheet(rows, { header: ['Referência DEMO', 'Documento', 'Título', 'Disciplina', 'Data planejada', 'Responsável', 'HH previsto semana', 'Observação', 'Estado'] });
  sheet['!cols'] = [36, 16, 30, 20, 18, 25, 20, 40, 15].map(wch => ({ wch }));
  XLSX.utils.book_append_sheet(workbook, sheet, 'Documentos DEMO');
  XLSX.writeFile(workbook, `PREVIEW-DEMO-programacao-${week.start}.xlsx`);
}
