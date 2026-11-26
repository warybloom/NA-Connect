import { useState } from 'react';
import { DEMO_PROJECTS } from '../shared/constants';
import { usePlanningDemo } from '../shared/demoStore';
import { PlanningShell, PlanningFilters, Badge, useNow } from '../shared/PlanningShell';
import { deadlineStatus, dateLabel, timeLabel } from '../shared/utils';
import { WeeklyDocumentGrid } from './WeeklyDocumentGrid';
import { WeeklySubmissionPanel } from './WeeklySubmissionPanel';
import { SourceDocumentsPanel } from './SourceDocumentsPanel';
export function WeeklyPlanningPage({ source }: { source?: { hubId: string; projectId: string; start: string; end: string } }) {
  const { weeks } = usePlanningDemo(); const [projectId, setProjectId] = useState(DEMO_PROJECTS[0].id); const [weekId, setWeekId] = useState(weeks[0]?.id ?? ''); const [discipline, setDiscipline] = useState('PROCESSO'); const now = useNow();
  const week = weeks.find(w => w.id === weekId && w.projectId === projectId); const submission = week?.submissions.find(s => s.discipline === discipline);
  return <PlanningShell title="Programação semanal" subtitle="Prepare a semana da sua disciplina e acompanhe a aprovação."><PlanningFilters weeks={weeks} projectId={projectId} weekId={weekId} onProject={id => { setProjectId(id); setWeekId(weeks.find(w => w.projectId === id)?.id ?? ''); }} onWeek={setWeekId}><label>Disciplina<select value={discipline} onChange={e => setDiscipline(e.target.value)}><option value="">Selecione</option>{week?.submissions.map(s => <option key={s.discipline}>{s.discipline}</option>)}</select></label></PlanningFilters>{week ? <><section className="planning-week-strip"><div><small>PERÍODO</small><strong>{dateLabel(week.start)} → {dateLabel(week.end)}</strong></div><div><small>PRAZO DE SUBMISSÃO · HORÁRIO LOCAL</small><strong>{timeLabel(week.deadline)}</strong></div><Badge value="SEMANA ABERTA"/>{submission && <Badge value={deadlineStatus(submission, week.deadline, now)}/>}</section>{submission ? <><section className="planning-card"><h2>Documentos da disciplina</h2><WeeklyDocumentGrid week={week} submission={submission}/></section><WeeklySubmissionPanel key={`${week.id}-${discipline}`} weekId={week.id} submission={submission}/></> : <p className="planning-empty">Selecione uma disciplina esperada nesta semana.</p>}</> : <p className="planning-empty">O planejador ainda não abriu uma semana para este projeto.</p>}{source && <SourceDocumentsPanel {...source}/>}</PlanningShell>;
}
export default WeeklyPlanningPage;
