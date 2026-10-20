import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const ts = require('typescript');
const compile = name => ts.transpileModule(readFileSync(new URL('../src/features/planning/costs/' + name, import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 },
}).outputText;
function load(code, dependencies) {
  const exports = {};
  vm.runInNewContext(code, { exports, require: name => dependencies[name], Intl, Date });
  return exports;
}
const api = load(compile('costsApi.ts'), {});
const model = load(compile('professionalPeriodForm.ts'), { './costsApi': api });
const period = (patch = {}) => ({ id: 1, professionalId: 10, start: '2026-01-01', end: null,
  monthlyHours: 160, remuneration: 0, manualRate: 95.5, monthlyCost: 0, hourlyRate: 95.5,
  manualRateUsed: true, active: true, items: [], ...patch });
const hourlyForm = () => ({ ...model.newForm(160, null), costMode: 'HORA', manualRate: 'R$ 95,50' });
const payload = form => { const result = model.montarPayload(form); assert.equal(result.ok, true, result.erro); return result.value; };

test('valor/hora dispensa remuneração, HH digitada e composição', () => {
  const result = payload({ ...hourlyForm(), remuneration: '', baseHours: '' });
  assert.equal(result.manualRate, 95.5); assert.equal(result.remuneration, 0);
  assert.ok(result.monthlyHours > 0); assert.equal(result.items.length, 0);
  assert.equal(result.copyPreviousComposition, false);
});
test('modo por hora ignora itens mensais antigos e não copia composição', () => {
  const result = payload({ ...hourlyForm(), remuneration: '8500', copyPrevious: true,
    items: [{ category: 'ENCARGOS', description: 'Encargo', calculationType: 'FIXO', amount: 'inválido', percentage: '' }] });
  assert.equal(result.remuneration, 0); assert.equal(result.items.length, 0); assert.equal(result.copyPreviousComposition, false);
});
test('valor/hora vazio, negativo ou inválido impede salvar', () => {
  for (const manualRate of ['', '-1', 'abc', 'Infinity']) assert.equal(model.montarPayload({ ...hourlyForm(), manualRate }).ok, false);
  assert.equal(payload({ ...hourlyForm(), manualRate: '0' }).manualRate, 0);
});
test('edição e nova vigência preservam modo e taxa por hora', () => {
  for (const form of [model.periodToForm(period()), model.newForm(160, period())]) {
    assert.equal(form.costMode, 'HORA'); assert.equal(payload(form).manualRate, 95.5);
    assert.equal(payload(form).copyPreviousComposition, false);
  }
});
test('composição com taxa manual continua composição', () => {
  const form = model.periodToForm(period({ remuneration: 8500, monthlyCost: 8500 }));
  assert.equal(form.costMode, 'COMPOSICAO'); assert.equal(payload(form).remuneration, 8500);
  assert.equal(payload(form).manualRate, 95.5);
});
test('composição mensal mantém validação e cópia de itens', () => {
  const form = { ...model.newForm(160, null), remuneration: '8.500,00' };
  assert.equal(payload(form).remuneration, 8500); assert.equal(payload(form).manualRate, null);
  assert.equal(payload(form).copyPreviousComposition, true);
  assert.equal(model.montarPayload({ ...form, baseHours: '' }).ok, false);
  assert.equal(model.montarPayload({ ...form, remuneration: '' }).ok, false);
});
test('modo por hora mantém validação de datas', () => {
  assert.equal(model.montarPayload({ ...hourlyForm(), start: '' }).ok, false);
  assert.equal(model.montarPayload({ ...hourlyForm(), start: '2026-10-08', end: '2026-10-07' }).ok, false);
});

async function harness(current) {
  const states = []; let cursor = 0; let loaded = false; const requests = [];
  const element = (type, props) => ({ type, props: props ?? {} });
  const hooks = {
    useState(initial) { const i = cursor++; if (!(i in states)) states[i] = initial; return [states[i], value => { states[i] = typeof value === 'function' ? value(states[i]) : value; }]; },
    useCallback: callback => callback,
    useEffect(effect) { if (!loaded) { loaded = true; effect(); } },
  };
  const custosApi = {
    vigencias: async () => current ? [current] : [],
    previaVigencia: async () => ({ confirmationRequired: false, isHistoricalBackfill: false }),
    criarVigencia: async (_, __, body) => { requests.push(body); },
    atualizarVigencia: async (_, __, ___, body) => { requests.push(body); },
  };
  const placeholders = new Proxy({}, { get: (_, key) => key });
  const component = load(compile('CustosVigenciasProfissional.tsx'), {
    react: hooks, 'react/jsx-runtime': { jsx: element, jsxs: element, Fragment: 'fragment' },
    'react-hot-toast': { default: { success() {}, error() {} } },
    'lucide-react': placeholders, '@/components/ui/alert-dialog': placeholders, '@/components/ui/button': placeholders,
    '@/lib/utils': { cn: (...args) => args.filter(Boolean).join(' ') },
    './costsApi': { ...api, custosApi }, './professionalPeriodForm': model,
    './CustosShared': { ConfirmarDesativacao: 'ConfirmarDesativacao', mensagemDe: String },
  });
  const render = () => { cursor = 0; return component.default({ projectId: 'fixture', professionalId: 10, hours: 160, onSaved() {} }); };
  function nodes(node) { if (!node || typeof node !== 'object') return []; if (Array.isArray(node)) return node.flatMap(nodes); return [node, ...nodes(node.props?.children)]; }
  const find = predicate => nodes(render()).find(predicate);
  render(); await new Promise(resolve => setImmediate(resolve));
  return { requests, find, nodes: () => nodes(render()) };
}
test('formulário oculta custos mensais e envia somente taxa ao criar', async () => {
  const h = await harness(period({ remuneration: 8500, manualRate: null, monthlyCost: 8500 }));
  h.find(n => n.type === 'Button' && n.props.children?.includes('Nova vigência')).props.onClick();
  h.find(n => n.type === 'select' && n.props.value === 'COMPOSICAO').props.onChange({ target: { value: 'HORA' } });
  const inputs = h.nodes().filter(n => n.type === 'input');
  assert.equal(inputs.length, 3); // início, fim e valor/hora
  inputs.find(n => n.props.inputMode === 'decimal').props.onChange({ target: { value: '95,50' } });
  await h.find(n => n.type === 'form').props.onSubmit({ preventDefault() {} });
  assert.equal(h.requests.length, 1); assert.equal(h.requests[0].manualRate, 95.5);
  assert.equal(h.requests[0].remuneration, 0); assert.equal(h.requests[0].copyPreviousComposition, false);
});
test('editar vigência por hora preserva taxa e dispensa composição', async () => {
  const h = await harness(period());
  h.find(n => n.props['aria-label']?.startsWith('Editar vigência')).props.onClick();
  assert.equal(h.find(n => n.type === 'select').props.value, 'HORA');
  await h.find(n => n.type === 'form').props.onSubmit({ preventDefault() {} });
  assert.equal(h.requests.length, 1); assert.equal(h.requests[0].manualRate, 95.5);
  assert.equal(h.requests[0].remuneration, 0); assert.equal(h.requests[0].items.length, 0);
});
