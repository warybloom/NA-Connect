export type SubmissionState = 'RASCUNHO' | 'ENVIADO' | 'DEVOLVIDO' | 'APROVADO';
export type FuturePlanningRole = 'DISCIPLINA' | 'JOB_LEADER' | 'PLANEJADOR';
export type PlanningEventName = 'PROGRAMACAO_ABERTA' | 'PRAZO_SE_APROXIMANDO' | 'PROGRAMACAO_ATRASADA' | 'PROGRAMACAO_ENVIADA' | 'PROGRAMACAO_DEVOLVIDA' | 'PROGRAMACAO_APROVADA';
export interface PlanningDocument { id: string; projectId: string; number: string; title: string; discipline: string }
export interface ScheduledDocument { documentId: string; selected: boolean; plannedDate: string; responsible: string; weeklyHours: number | null; note: string }
export interface PlanningHistory { at: string; action: string; comment: string; actor: 'JOB_LEADER_DEMO' | 'PLANEJADOR_DEMO'; documentSnapshot: ScheduledDocument[] }
export interface DisciplineSubmission { discipline: string; state: SubmissionState; documents: ScheduledDocument[]; history: PlanningHistory[]; returnReason?: string }
export interface PlanningWeek { id: string; projectId: string; start: string; end: string; deadline: string; submissions: DisciplineSubmission[] }
export interface PlanningSnapshot { weeks: PlanningWeek[]; events: { name: PlanningEventName; weekId: string; discipline?: string; at: string }[] }
export interface SourceDocument { sourceRecordId: number; documentNumber: string | null; title: string | null; discipline: string | null; plannedDate: string | null; documentTotalHours: number | null }
export interface SourceDocumentResponse { source: string; identityStatus: 'SNAPSHOT_REFERENCE_ONLY'; totalRows: number; page: number; pageSize: number; rows: SourceDocument[]; warning: string }
