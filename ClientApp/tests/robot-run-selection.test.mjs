import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const ts = require('typescript');
const source = readFileSync(new URL('../src/components/RobotRunButton.tsx', import.meta.url), 'utf8');
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 } }).outputText;

// Exercita os handlers reais do componente com hooks e fetch locais; nenhuma API é chamada.
function harness() {
  const states = []; let cursor = 0; const requests = [];
  const element = (type, props) => ({ type, props: props ?? {} });
  const hooks = {
    useState(initial) { const i = cursor++; if (!(i in states)) states[i] = initial; return [states[i], value => { states[i] = typeof value === 'function' ? value(states[i]) : value; }]; },
    useCallback: callback => callback, useEffect() {}, useRef: value => ({ current: value }),
  };
  const exports = {};
  vm.runInNewContext(code, { exports,
    require(name) {
      if (name === 'react') return hooks;
      if (name === 'react/jsx-runtime') return { jsx: element, jsxs: element, Fragment: 'fragment' };
      if (name === 'react-hot-toast') return { default: { success() {}, error() {} } };
      return new Proxy({}, { get: (_, key) => key });
    },
    fetch: async (url, options) => { requests.push({ url, body: JSON.parse(options.body) }); return { ok: true, status: 202, json: async () => ({ state: 'RUNNING', message: 'fixture' }) }; },
  });
  const render = () => { cursor = 0; return exports.RobotRunButton({ hubId: 'hub-fixture', projects: [{ id: 'one', name: 'One' }, { id: 'two', name: 'Two' }] }); };
  function nodes(node) { if (!node || typeof node !== 'object') return []; if (Array.isArray(node)) return node.flatMap(nodes); return [node, ...nodes(node.props?.children)]; }
  const find = predicate => nodes(render()).find(predicate);
  return { requests, find, inputs: () => nodes(render()).filter(n => n.type === 'input'), button: id => find(n => n.props['data-testid'] === id) };
}

test('seleção múltipla envia somente os ids selecionados', async () => {
  const h = harness(); assert.equal(h.button('confirmar-robo').props.disabled, true);
  h.inputs()[0].props.onChange({ target: { checked: true } });
  h.inputs()[1].props.onChange({ target: { checked: true } });
  assert.equal(h.button('confirmar-robo').props.disabled, false);
  await h.button('confirmar-robo').props.onClick();
  assert.deepEqual(h.requests, [{ url: '/api/worker/run', body: { hubId: 'hub-fixture', projectIds: ['one', 'two'] } }]);
});
test('um projeto continua selecionável e limpar seleção desabilita execução', async () => {
  const h = harness(); h.inputs()[0].props.onChange({ target: { checked: true } });
  h.find(n => n.props.children === 'Limpar seleção').props.onClick();
  assert.equal(h.button('confirmar-robo').props.disabled, true);
  h.inputs()[1].props.onChange({ target: { checked: true } });
  await h.button('confirmar-robo').props.onClick();
  assert.deepEqual(h.requests[0].body.projectIds, ['two']);
});
test('todos os ativos não envia a lista do frontend', async () => {
  const h = harness(); await h.button('executar-todos-ativos').props.onClick();
  assert.deepEqual(h.requests[0].body, { allProjects: true });
});
