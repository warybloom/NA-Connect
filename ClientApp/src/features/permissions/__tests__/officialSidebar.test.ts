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
test('a réplica do Resumo é incorporada em layouts antigos sem desocultar outras telas', () => {
  const nova = 'page:/controle-producao/resumo-cronograma';
  const original = 'page:/controle-producao/resumo';
  const lista = 'page:/controle-producao/lista-documentos';
  const antigo = clone().filter(n=>n.id!==nova).map(n=>(
    n.parentId==='group:Controle de Produção' && n.order>1 ? {...n, order:n.order-1} : n
  ));
  const configOriginal = antigo.map(n=>n.id===lista ? {...n,hidden:true} : n);
  const view = applyConfiguration(NAVEGACAO, {revision:1,items:configOriginal});
  const children = view.find(n=>n.id==='group:Controle de Produção')?.children ?? [];
  const ids = children.map(n=>n.id);
  assert.equal(ids[ids.indexOf(original)+1],nova);
  assert.ok(!ids.includes(lista),'item oculto deve permanecer oculto');
  assert.deepEqual(configOriginal, antigo.map(n=>n.id===lista ? {...n,hidden:true} : n),'a configuração salva não pode ser alterada em memória');
});

test('invalid runtime config falls back to current default with badges', () => {
  const expected = identify(NAVEGACAO);
  assert.deepEqual(applyConfiguration(NAVEGACAO, { revision:-1, items:clone() }), expected);
  assert.deepEqual(applyConfiguration(NAVEGACAO, null), expected);
});

test('GET do sidebar ja filtrado nao ressuscita ocultos, grupos invisiveis ou folhas sem permissao', () => {
  const updated = clone().map(n => ({
    ...n,
    label: n.id === 'page:/controle-producao/resumo-cronograma' ? 'Cronograma Personalizado' : n.label,
    hidden: n.id === 'page:/controle-producao/boletim-medicao' || n.id === 'group:Configurações',
  }));
  const byId = new Map(updated.map(n => [n.id, n]));
  const allowed = (n: typeof updated[number]): boolean => {
    if (n.hidden || n.id === 'page:/cde/documentos') return false; // sem permissao
    let p=n.parentId;
    while(p !== null) {
      const parent=byId.get(p)!;
      if(parent.hidden) return false;
      p=parent.parentId;
    }
    return true;
  };
  const serverItems=updated.filter(allowed);
  assert.ok(serverItems.length < updated.length, 'backend retorna subconjunto');
  const result=applyConfiguration(NAVEGACAO,{revision:3,items:serverItems},{serverFiltered:true});
  const flatten=(nodes: typeof result): typeof result => nodes.flatMap(n=>[n,...flatten(n.children??[])]);
  const visible=flatten(result);
  const ids=new Set(visible.map(n=>n.id));
  assert.ok(!ids.has('page:/controle-producao/boletim-medicao'));
  assert.ok(!ids.has('group:Configurações'));
  assert.ok(!ids.has('page:/cde/documentos'));
  assert.ok(!visible.some(n=>n.id?.startsWith('page:/configuracoes/')));
  assert.equal(visible.find(n=>n.id==='page:/controle-producao/resumo-cronograma')?.label,'Cronograma Personalizado');
  assert.ok(visible.some(n=>n.id==='page:/controle-producao/lista-documentos'));
  // Config filtrada/malformada: falha fechada, nunca abre defaults.
  assert.deepEqual(applyConfiguration(NAVEGACAO,{revision:3,items:[{...serverItems[0],id:'desconhecido'}]},{serverFiltered:true}),[]);
});

test('GET filtrado da revisao zero tambem nao restaura folhas bloqueadas', () => {
  const full=clone();
  const visible=full.filter(n=>n.id!=='page:/cde/documentos');
  const built=applyConfiguration(NAVEGACAO,{revision:0,items:visible},{serverFiltered:true});
  const flatten=(ns:typeof built):string[]=>ns.flatMap(n=>n.children?flatten(n.children):[n.to??'']);
  assert.ok(!flatten(built).includes('/cde/documentos'));
});
