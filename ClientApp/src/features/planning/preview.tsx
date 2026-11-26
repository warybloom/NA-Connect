import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { WeeklyPlanningPage, PlannerDashboardPage } from './index';
function Preview() {
  const [module, setModule] = useState<'weekly' | 'planner'>('weekly');
  return <><nav aria-label="Módulos de demonstração" className="planning-workspace" style={{ minHeight: 0, padding: '12px 30px', display: 'flex', gap: 12 }}><button className={module === 'weekly' ? 'planning-primary' : ''} aria-pressed={module === 'weekly'} onClick={() => setModule('weekly')}>Programação semanal</button><button className={module === 'planner' ? 'planning-primary' : ''} aria-pressed={module === 'planner'} onClick={() => setModule('planner')}>Painel do planejador</button></nav>{module === 'weekly' ? <WeeklyPlanningPage/> : <PlannerDashboardPage/>}</>;
}
const root = document.getElementById('planning-preview');
if (root) createRoot(root).render(<Preview/>);
