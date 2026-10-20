// Run from ClientApp: node --test src/features/planning/tests/workflow.test.cjs
// Compile only these isolated TS modules in memory; no build output is written.
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const { test } = require('node:test');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
require.extensions['.ts'] = (module, filename) => {
  if (!filename.startsWith(root + path.sep)) throw new Error('Only planning modules may be loaded');
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } });
  module._compile(output.outputText, filename);
};
const store = require('../shared/demoStore.ts');
const { deadlineStatus } = require('../shared/utils.ts');
test('draft, submit, return, resubmit, approval preserve history and lock documents', () => {
  const initial = store.getPlanningDemoSnapshot().weeks[0];
  const id = initial.id; const discipline = 'PROCESSO';
  const doc = initial.submissions[0].documents[0].documentId;
  assert.throws(() => store.demoAction(id, discipline, 'submit'), /Selecione/);
  store.patchDemoDocument(id, discipline, doc, { selected: true, responsible: 'DEMO Job Leader', weeklyHours: 20, plannedDate: initial.start });
  store.demoAction(id, discipline, 'save');
  store.demoAction(id, discipline, 'submit');
  assert.throws(() => store.patchDemoDocument(id, discipline, doc, { weeklyHours: 25 }), /bloqueada/);
  assert.throws(() => store.demoAction(id, discipline, 'return'), /comentário/);
  store.demoAction(id, discipline, 'return', 'Ajustar a distribuição semanal');
  store.patchDemoDocument(id, discipline, doc, { weeklyHours: 16 });
  store.demoAction(id, discipline, 'submit');
  store.demoAction(id, discipline, 'approve');
  assert.throws(() => store.patchDemoDocument(id, discipline, doc, { selected: false }), /bloqueada/);
  assert.throws(() => store.demoAction(id, discipline, 'submit'), /bloqueada/);
  const result = store.getPlanningDemoSnapshot();
  const sub = result.weeks[0].submissions[0];
  assert.equal(sub.state, 'APROVADO'); assert.equal(sub.documents[0].weeklyHours, 16);
  assert.equal(sub.history.length, 5); assert.equal(sub.returnReason, 'Ajustar a distribuição semanal');
  assert.equal(sub.history[1].documentSnapshot[0].weeklyHours, 20);
  assert.equal(sub.history[4].documentSnapshot[0].weeklyHours, 16);
  assert.equal(sub.history[1].actor, 'JOB_LEADER_DEMO');
  assert.equal(sub.history[4].actor, 'PLANEJADOR_DEMO');
  assert.equal(result.events.at(-1).name, 'PROGRAMACAO_APROVADA');
});
test('deadline uses a strict cutoff and does not override returned or approved states', () => {
  const deadline = '2026-09-18T18:00:00Z'; const time = Date.parse(deadline);
  for (const state of ['ENVIADO', 'DEVOLVIDO', 'APROVADO']) assert.equal(deadlineStatus({ state }, deadline, time + 1), state);
  assert.equal(deadlineStatus({ state: 'RASCUNHO' }, deadline, time), 'PENDENTE');
  assert.equal(deadlineStatus({ state: 'RASCUNHO' }, deadline, time + 1), 'ATRASADO PARA SUBMISSÃO');
});
test('submission rejects missing responsible, out-of-period date and invalid HH', () => {
  const week = store.getPlanningDemoSnapshot().weeks[0]; const discipline = 'TUBULAÇÃO'; const doc = week.submissions[1].documents[0].documentId;
  store.patchDemoDocument(week.id, discipline, doc, { selected: true });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /responsável/);
  store.patchDemoDocument(week.id, discipline, doc, { responsible: 'DEMO', plannedDate: '2026-10-01' });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /dentro da semana/);
  store.patchDemoDocument(week.id, discipline, doc, { plannedDate: week.start, weeklyHours: -1 });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /HH semanal/);
});
test('opening rejects overlaps and creates only expected disciplines', () => {
  assert.throws(() => store.openDemoWeek('demo-expansion', '2026-10-01', '2026-10-08', '2026-09-30T12:00:00Z', ['PROCESSO']), /1 a 7/);
  assert.throws(() => store.openDemoWeek('demo-expansion', '2026-10-01', '2026-10-05', '2026-10-07T12:00:00Z', ['PROCESSO']), /posterior/);
  assert.throws(() => store.openDemoWeek('demo-industrial', '2026-09-22', '2026-09-26', '2026-09-20T12:00:00Z', ['PROCESSO']), /Já existe/);
  const id = store.openDemoWeek('demo-expansion', '2026-09-21', '2026-09-25', '2026-09-18T12:00:00Z', ['MECÂNICA']);
  const week = store.getPlanningDemoSnapshot().weeks.find(w => w.id === id);
  assert.equal(week.submissions.length, 1); assert.equal(week.submissions[0].discipline, 'MECÂNICA');
  assert.ok(week.submissions[0].documents.every(d => d.weeklyHours === null));
});
test('submission rejects excess decimal precision, HH magnitude and long observations', () => {
  const week = store.getPlanningDemoSnapshot().weeks[0]; const discipline = 'ELÉTRICA'; const doc = week.submissions[2].documents[0].documentId;
  store.patchDemoDocument(week.id, discipline, doc, { selected: true, responsible: 'DEMO', weeklyHours: 0.1234567 });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /6 decimais/);
  store.patchDemoDocument(week.id, discipline, doc, { weeklyHours: 1e13 });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /HH semanal/);
  store.patchDemoDocument(week.id, discipline, doc, { weeklyHours: 12, note: 'a'.repeat(2001) });
  assert.throws(() => store.demoAction(week.id, discipline, 'submit'), /2000 caracteres/);
});
