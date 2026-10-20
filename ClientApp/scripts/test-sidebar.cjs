const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),ts=require('../node_modules/typescript'),{execFileSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..');const cache={};function load(file){file=path.resolve(file);if(cache[file])return cache[file];if(file.endsWith('.json'))return JSON.parse(fs.readFileSync(file,'utf8'));const exports={};cache[file]=exports;const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS,esModuleInterop:true}}).outputText;const req=name=>{if(name==='lucide-react')return new Proxy({},{get:(_,k)=>k});let f=name.startsWith('@/')?root+'/ClientApp/src/'+name.slice(2):path.resolve(path.dirname(file),name);return load(f.endsWith('.json')?f:f+'.ts');};vm.runInNewContext(js,{exports,require:req,URLSearchParams});return exports;}
execFileSync(process.execPath,[path.join(__dirname,'sidebar-catalog.cjs'),'--check'],{stdio:'inherit'});
const model=load(root+'/ClientApp/src/features/sidebar/sidebarModel.ts');const nav=load(root+'/ClientApp/src/features/sidebar/officialNavigation.ts');const perms=load(root+'/ClientApp/src/features/permissions/permissionLogic.ts');
let count=0;function test(name,fn){fn();console.log('PASS '+name);count++;}const clone=()=>JSON.parse(JSON.stringify(model.DEFAULT_ITEMS));
function leaves(nodes){return nodes.flatMap(n=>n.children?leaves(n.children):[n]);}
test('default catalog and immutable route/key contract',()=>{assert.equal(model.validateItems(clone()).length,117);assert.equal(new Set(model.DEFAULT_ITEMS.map(n=>n.id)).size,117);for(const n of model.CATALOG)if(!n.group){assert(n.to);assert(n.permissionKeys.length);}});
for(const [name,mutate] of [ ['unknown IDs',a=>a[0].id='unknown'],['unknown custom IDs',a=>a[0].id='custom:00000000-0000-0000-0000-000000000000'],['duplicate IDs',a=>a[1].id=a[0].id],['permission injection',a=>a[0].permissionKey='admin'],['arbitrary route',a=>a[0].to='/evil'],['invalid icon',a=>a[0].icon='script'],['prototype icon',a=>a[0].icon='constructor'],['cycle',a=>a[0].parentId=a[0].id],['page as parent',a=>a[0].parentId=a.find(n=>n.id.startsWith('page:')).id],['duplicate positions',a=>a.find(n=>n.parentId===null&&n.id!==a[0].id).order=a[0].order],['missing items',a=>a.pop()] ])test('reject '+name,()=>{const a=clone();mutate(a);assert.throws(()=>model.validateItems(a));});
test('default fallback preserves dynamic conditions and badges',()=>{const actual=model.applyConfiguration(nav.NAVEGACAO,null);function strip(ns){return ns.map(({id,children,...n})=>({...n,...(children?{children:strip(children)}:{})}));}assert.equal(JSON.stringify(strip(actual)),JSON.stringify(nav.NAVEGACAO));assert.equal(leaves(actual).length,leaves(nav.NAVEGACAO).length);const page=leaves(actual).find(n=>n.to==='/modelo-3d');assert.equal(page.badgeAbaixo.texto,'Em desenvolvimento');assert(!leaves(actual).some(n=>n.to==='/qualidade/comparativo-hh-custos'));});
test('reorder and reparent preserve route permission and badge',()=>{const source=clone();const page=source.find(n=>n.id==='page:/modelo-3d');const target=source.find(n=>n.id==='group:Configurações');const moved=model.moveItem(source,page.id,target.id);assert.equal(moved.find(n=>n.id===page.id).parentId,target.id);assert.equal(source.find(n=>n.id===page.id).parentId,'group:CDE');const runtime=leaves(model.applyConfiguration(nav.NAVEGACAO,{revision:1,items:moved})).find(n=>n.to==='/modelo-3d');assert.equal(runtime.permissionKey,'cde.model3d.view');assert.equal(runtime.badgeAbaixo.texto,'Em desenvolvimento');});
test('hidden ancestors hide descendants',()=>{const items=clone();items.find(n=>n.id==='group:CDE').hidden=true;assert(!leaves(model.applyConfiguration(nav.NAVEGACAO,{revision:1,items})).some(n=>n.to==='/modelo-3d'));});
test('permissions filter runs after configuration',()=>{const configured=model.applyConfiguration(nav.NAVEGACAO,{revision:1,items:clone()});const filtered=perms.filtrarPorPermissao(configured,key=>Array.isArray(key)?key.includes('cde.docs.view'):key==='cde.docs.view');assert.deepEqual(leaves(filtered).map(n=>n.to).join(','),'/cde/documentos');});
test('malformed runtime fallback never bypasses permission filter',()=>{const items=clone();items[0].id='bad';const fallback=model.applyConfiguration(nav.NAVEGACAO,{revision:1,items});assert.equal(perms.filtrarPorPermissao(fallback,()=>false).length,0);});
test('runtime does not resurrect conditional unavailable pages',()=>{assert(!leaves(model.applyConfiguration(nav.NAVEGACAO,{revision:1,items:clone()})).some(n=>n.to==='/qualidade/comparativo-hh-custos'));});
test('sidebar settings route requires permissions.manage',()=>assert.equal(perms.permissaoDaRota('/configuracoes/sidebar').join(','),'permissions.manage'));
test('conditional navigation preserves original environment matrix',()=>{for(const comparison of [false,true])for(const enforced of [false,true])for(const master of [false,true]){const tree=nav.contextualNavigation(comparison,enforced,master);const visible=leaves(tree);assert.equal(visible.some(n=>n.to==='/qualidade/comparativo-hh-custos'),comparison||enforced);assert.equal(tree[0].children.some(n=>n.label==='Custos do Projeto'),enforced||master);}});
test('published sidebar legacy 83 and 84 items preserves customizations and adds only missing pages',()=>{
 const current=clone();
 const tv='page:/controle-producao/resumo-tv-teste',gestao='page:/controle-producao/gestao-hh-custo';
 for(const absent of [[tv],[tv,gestao]]){
   const old=current.filter(n=>!absent.includes(n.id));
   const configured=model.applyConfiguration(nav.NAVEGACAO,{revision:8,items:old});
   const pages=leaves(configured);
   for(const id of absent){
     const route=id.slice(5);
     assert.equal(pages.filter(n=>n.to===route).length,1);
   }
   assert(pages.some(n=>n.to==='/controle-producao/resumo'));
 }
});
test('old sidebar receives Gestão à Vista hierarchy without losing hidden custom nodes',()=>{
 const fresh=clone();
 const old=fresh.filter(x=>!(x.id.includes('/controle-producao/gestao-a-vista/')||x.label==='GESTÃO À VISTA'));
 const resumo=old.find(x=>x.id==='page:/controle-producao/resumo');
 assert(resumo);resumo.hidden=true;
 const tree=model.applyConfiguration(nav.NAVEGACAO,{revision:15,items:old});
 const visible=leaves(tree);
 assert(!visible.some(x=>x.to==='/controle-producao/resumo'));
 for(const suffix of ['visao-geral','programacao','documentacao','indicadores','hh-disciplina','custo-disciplina']){
   assert(visible.some(x=>x.to==='/controle-producao/gestao-a-vista/'+suffix));
 }
});
console.log(count+' sidebar tests passed');
