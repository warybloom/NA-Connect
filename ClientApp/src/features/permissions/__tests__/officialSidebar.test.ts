import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { DEFAULT_ITEMS, CATALOG, validateItems, moveItem, applyConfiguration } from '../../sidebar/sidebarModel';
import { SIDEBAR_ICONS } from '../../sidebar/icons';
import { NAVEGACAO, CONDITIONAL_NODES, identify } from '../../sidebar/officialNavigation';
import { filtrarPorPermissao } from '../permissionLogic';
const clone = () => DEFAULT_ITEMS.map(n => ({ ...n }));
test('backend catalog and icon allowlist match the frontend registry', () => {
  const generated = readFileSync(path.resolve(process.cwd(), '../Services/Sidebar/SidebarCatalog.Generated.cs'), 'utf8');
  const json = generated.match(/CatalogJson\s*=\s*"""([\s\S]*?)"""/)?.[1];
  assert.ok(json); assert.deepEqual(JSON.parse(json), CATALOG);
  const rules = readFileSync(path.resolve(process.cwd(), '../Services/Sidebar/SidebarRules.cs'), 'utf8');
  const iconBlock = rules.match(/\.Concat\(new\[\]\s*\{([^}]+)\}/)?.[1];
  assert.ok(iconBlock);
  const backend = [...iconBlock.matchAll(/"([^"]+)"/g)].map(m => m[1]).sort();
  assert.deepEqual(backend, Object.keys(SIDEBAR_ICONS).sort());
});
test('official sidebar rejects duplicate, unknown and technical fields', () => {
  assert.doesNotThrow(() => validateItems(clone()));
  const duplicated = clone(); duplicated[1] = { ...duplicated[0] };
  assert.throws(() => validateItems(duplicated));
  const unknown = clone(); unknown[0].id = 'page:/arbitrary';
  assert.throws(() => validateItems(unknown));
  assert.throws(() => validateItems(clone().map((n,i) => i === 0 ? { ...n, permissionKey:'permissions.manage' } : n)));
  assert.throws(() => validateItems(clone().map((n,i) => i === 0 ? { ...n, to:'https://attacker.test' } : n)));
});
test('official sidebar rejects cycles, page parents and duplicate order', () => {
  const cyclic = clone(); cyclic[0].parentId = cyclic[0].id;
  assert.throws(() => validateItems(cyclic));
  const pageParent = clone(); pageParent[0].parentId = pageParent.find(n => n.id.startsWith('page:'))!.id;
  assert.throws(() => validateItems(pageParent));
  const order = clone(); order[2].order = order[1].order;
  assert.throws(() => validateItems(order));
});
test('moving retains stable identity and renumbered siblings', () => {
  const original = clone(); const firstPage = original.find(n => n.id.startsWith('page:'))!;
  const target = original.find(n => n.parentId === null && n.id !== firstPage.parentId)!;
  const next = moveItem(original, firstPage.id, target.id);
  assert.equal(next.find(n => n.id === firstPage.id)!.parentId, target.id);
  assert.deepEqual(original, clone());
  assert.doesNotThrow(() => validateItems(next));
});
test('configuration cannot resurrect conditional nodes and permissions apply after config', () => {
  const items = clone();
  const result = applyConfiguration(NAVEGACAO, { revision:1, items });
  const flatten = (nodes: typeof result): string[] => nodes.flatMap(n => [n.to ?? '', ...flatten(n.children ?? [])]);
  assert.ok(!flatten(result).includes(CONDITIONAL_NODES[0].to!));
  assert.deepEqual(filtrarPorPermissao(result, () => false), []);
  const restricted = filtrarPorPermissao(result, key => key === 'permissions.manage');
  assert.ok(flatten(restricted).includes('/configuracoes/sidebar'));
  assert.ok(!flatten(restricted).includes('/cde/documentos'));
});
test('invalid runtime config falls back to current default with badges', () => {
  const expected = identify(NAVEGACAO);
  assert.deepEqual(applyConfiguration(NAVEGACAO, { revision:-1, items:clone() }), expected);
  assert.deepEqual(applyConfiguration(NAVEGACAO, null), expected);
});
