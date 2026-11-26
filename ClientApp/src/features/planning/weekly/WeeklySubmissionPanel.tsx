import { PlanningHistoryList } from '../shared/PlanningHistoryList';
import { useState } from 'react';
import { demoAction } from '../shared/demoStore';
import { canEdit } from '../shared/utils';
import { Badge } from '../shared/PlanningShell';
import type { DisciplineSubmission } from '../shared/types';
export function WeeklySubmissionPanel({ weekId, submission }: { weekId: string; submission: DisciplineSubmission }) {
  const [message, setMessage] = useState('');
  function act(action: 'save' | 'submit') { try { demoAction(weekId, submission.discipline, action); setMessage(action === 'save' ? 'Rascunho salvo nesta sessão de demonstração.' : 'Enviado. Aguarde a revisão do planejador.'); } catch (error) { setMessage((error as Error).message); } }
  return <section className="planning-card"><div className="planning-section-title"><h2>Submissão da disciplina</h2><Badge value={submission.state}/></div>{submission.returnReason && <p className="planning-return"><strong>Última devolução:</strong> {submission.returnReason}</p>}<p>{submission.state === 'APROVADO' ? 'Programação aprovada e congelada. Uma revisão futura deverá preservar este histórico.' : submission.state === 'ENVIADO' ? 'Edição bloqueada enquanto o planejador revisa a programação.' : 'Selecione documentos e complete data, responsável e HH semanal quando aplicável.'}</p><div className="planning-actions"><button disabled={!canEdit(submission.state)} onClick={() => act('save')}>Salvar rascunho</button><button className="planning-primary" disabled={!canEdit(submission.state)} onClick={() => act('submit')}>Enviar para planejador</button></div><p role="status">{message}</p><PlanningHistoryList history={submission.history}/></section>;
}
