import type { PlanningDocument } from './types';
export const DEMO_PROJECTS = [{ id: 'demo-industrial', name: 'DEMO · Unidade industrial' }, { id: 'demo-expansion', name: 'DEMO · Expansão' }];
export const DISCIPLINES = ['PROCESSO', 'TUBULAÇÃO', 'ELÉTRICA', 'MECÂNICA', 'INSTRUMENTAÇÃO'];
export const DEMO_DOCUMENTS: PlanningDocument[] = DEMO_PROJECTS.flatMap(project => DISCIPLINES.flatMap((discipline, index) => [0, 1, 2].map(n => ({ id: `${project.id}-${index}-${n}`, projectId: project.id, number: `DEMO-${index + 1}0${n + 1}`, title: ['Memorial de cálculo', 'Desenho de implantação', 'Lista de materiais'][n], discipline }))));
export const FUTURE_MODULE_ACCESS = { weekly: ['DISCIPLINA', 'JOB_LEADER'], planner: ['PLANEJADOR'] } as const;
