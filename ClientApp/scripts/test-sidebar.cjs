const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),ts=require('../node_modules/typescript'),{execFileSync}=require('node:child_process');
const root=path.resolve(__dirname,'../..');const cache={};function load(file){file=path.resolve(file);if(cache[file])return cache[file];if(file.endsWith('.json'))return JSON.parse(fs.readFileSync(file,'utf8'));const exports={};cache[file]=exports;const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS,esModuleInterop:true}}).outputText;const req=name=>{if(name==='lucide-react')return new Proxy({},{get:(_,k)=>k});let f=name.startsWith('@/')?root+'/ClientApp/src/'+name.slice(2):path.resolve(path.dirname(file),name);return load(f.endsWith('.json')?f:f+'.ts');};vm.runInNewContext(js,{exports,require:req,URLSearchParams});return exports;}
const navigationOnly=process.argv.includes('--navigation-only');
if(!navigationOnly)execFileSync(process.execPath,[path.join(__dirname,'sidebar-catalog.cjs'),'--check'],{stdio:'inherit'});
const model=load(root+'/ClientApp/src/features/sidebar/sidebarModel.ts');const nav=load(root+'/ClientApp/src/features/sidebar/officialNavigation.ts');const perms=load(root+'/ClientApp/src/features/permissions/permissionLogic.ts');const acessoBml=load(root+'/ClientApp/src/features/production/biBml011016/acessoBiBml011016.ts');
const editor=load(root+'/ClientApp/src/features/sidebar/sidebarEditorModel.ts');
const navigation=load(root+'/ClientApp/src/features/sidebar/sidebarNavigationState.ts');
let count=0;function test(name,fn){if(navigationOnly&&!name.startsWith('navigation:'))return;fn();console.log('PASS '+name);count++;}const clone=()=>JSON.parse(JSON.stringify(model.DEFAULT_ITEMS));
function leaves(nodes){return nodes.flatMap(n=>n.children?leaves(n.children):[n]);}
test('default catalog and immutable route/key contract',()=>{const total=model.CATALOG.length;assert(total>121);assert.equal(model.validateItems(clone()).length,total);assert.equal(new Set(model.DEFAULT_ITEMS.map(n=>n.id)).size,total);for(const n of model.CATALOG)if(!n.group){assert(n.to);assert(n.permissionKeys.length);}});
test('editor filters one principal group and keeps only descendants',()=>{const items=clone();const root=editor.topLevelGroups(items).find(n=>n.label.includes('Produ'));assert(root);const scope=editor.descendantIds(items,root.id);const rows=editor.filteredTreeRows(items,root.id,'','all',new Set());assert(rows.length>1);assert(rows.every(r=>scope.has(r.item.id)));assert(!rows.some(r=>r.item.parentId===null&&r.item.id!==root.id));});
test('editor search finds Curva S and reveals ancestors when collapsed',()=>{const items=clone();const root=editor.topLevelGroups(items).find(n=>n.label.includes('Produ'));const rows=editor.filteredTreeRows(items,root.id,'curva s','all',new Set([root.id]));assert(rows.some(r=>r.match&&r.item.label.toLocaleLowerCase('pt-BR').includes('curva s')));assert.equal(rows[0].item.id,root.id);});
test('editor visibility filter reflects hidden draft state',()=>{const items=clone();const page=items.find(n=>n.id.startsWith('page:'));page.hidden=true;const counts=editor.visibilityCounts(items,null);assert.equal(counts.hidden,items.filter(n=>n.hidden).length);const rows=editor.filteredTreeRows(items,null,'','hidden',new Set());assert(rows.some(r=>r.item.id===page.id&&r.match));});
test('navigation: query identifies only the selected planner tab',()=>{
 const route='/planejamento/painel-do-planejador?aba=documentos-gestao';
 assert.equal(navigation.rotaAtiva(route,{pathname:'/planejamento/painel-do-planejador',search:'?aba=documentos-gestao'}),true);
 assert.equal(navigation.rotaAtiva(route,{pathname:'/planejamento/painel-do-planejador',search:'?aba=planejamento'}),false);
 assert.equal(navigation.rotaAtiva(route,{pathname:'/planejamento/painel-do-planejador',search:''}),false);
});
test('navigation: permission refresh retains mounted navigation tree',()=>{
 const current=[{id:'new',label:'New'}],previous=[{id:'stable',label:'Stable'}];
 assert.equal(navigation.navigationDuringPermissionLoad(current,true,previous),previous);
 assert.equal(navigation.navigationDuringPermissionLoad(current,false,previous),current);
 assert.equal(navigation.navigationDuringPermissionLoad(current,true,[]),current);
});
test('navigation: sidebar scroll restoration is exact and idempotent',()=>{
 const container={scrollTop:0};
 for(let click=0;click<12;click++){navigation.restoreSidebarScroll(container,731);assert.equal(container.scrollTop,731);}
 navigation.restoreSidebarScroll(null,10);
});
test('navigation: active ancestors open without closing user-expanded groups',()=>{
 assert.equal(navigation.expandedAfterLocationChange(false,true,true),true);
 assert.equal(navigation.expandedAfterLocationChange(true,false,true),true);
 assert.equal(navigation.expandedAfterLocationChange(false,true,false),false);
});
for(const [name,mutate] of [ ['unknown IDs',a=>a[0].id='unknown'],['unknown custom IDs',a=>a[0].id='custom:00000000-0000-0000-0000-000000000000'],['duplicate IDs',a=>a[1].id=a[0].id],['permission injection',a=>a[0].permissionKey='admin'],['arbitrary route',a=>a[0].to='/evil'],['invalid icon',a=>a[0].icon='script'],['prototype icon',a=>a[0].icon='constructor'],['cycle',a=>a[0].parentId=a[0].id],['page as parent',a=>a[0].parentId=a.find(n=>n.id.startsWith('page:')).id],['duplicate positions',a=>a.find(n=>n.parentId===null&&n.id!==a[0].id).order=a[0].order],['missing items',a=>a.pop()] ])test('reject '+name,()=>{const a=clone();mutate(a);assert.throws(()=>model.validateItems(a));});
test('default fallback preserves dynamic conditions and badges',()=>{const actual=model.applyConfiguration(nav.NAVEGACAO,null);assert.equal(leaves(actual).map(n=>n.to).join('|'),leaves(nav.NAVEGACAO).map(n=>n.to).join('|'));assert.equal(leaves(actual).length,leaves(nav.NAVEGACAO).length);const page=leaves(actual).find(n=>n.to==='/modelo-3d');assert.equal(page.badgeAbaixo.texto,'Em desenvolvimento');assert(!leaves(actual).some(n=>n.to==='/qualidade/comparativo-hh-custos'));});
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
test('revision 4 with 121 items and 56 hidden is augmented only in memory',()=>{
 const old=clone().slice(0,121).map((n,i)=>({...n,hidden:i<56}));
 const hiddenIds=new Set(old.filter(n=>n.hidden).map(n=>n.id));
 const tree=model.applyConfiguration(nav.NAVEGACAO,{revision:4,items:old});
 const rendered=new Set();(function collect(ns){for(const n of ns){rendered.add(n.id);if(n.children)collect(n.children);}})(tree);
 for(const id of hiddenIds)assert(!rendered.has(id),'item oculto reapareceu: '+id);
 assert.equal(old.length,121);assert.equal(old.filter(n=>n.hidden).length,56);
 assert(tree.some(n=>n.label==='AUDITORIA'),'novos itens devem ser adicionados apenas na projeção em memória');
});
test('BI BML011016 menu exists only for exact master admin',()=>{
 const base=nav.identify(nav.NAVEGACAO),routes=x=>leaves(x).filter(n=>String(n.to||'').startsWith('/bi-bml011016/'));
 assert.equal(routes(nav.comAuditoriaMasterAdmin(base,true)).length,9);
 assert.equal(routes(nav.comAuditoriaMasterAdmin(base,false)).length,0);
 assert.equal(routes(nav.comAuditoriaMasterAdmin(base,undefined)).length,0);
});
test('BI BML011016 stays visible AFTER permission filtering only for Master Admin',()=>{
 const base=model.applyConfiguration(nav.NAVEGACAO,{revision:1,items:clone()});
 const routes=x=>leaves(x).filter(n=>String(n.to||'').startsWith('/bi-bml011016/'));
 const masterTree=nav.comAuditoriaMasterAdmin(base,true);
 const allMaster=perms.filtrarPorPermissao(masterTree,perms.criarCan({
   enforced:true,schemaInstalled:true,isMasterAdmin:true,email:'',projectIdAcc:null,permissions:[],anyProject:[]
 },'any'));
 assert.equal(routes(allMaster).length,9,'Master Admin deve ver as 9 páginas');
 assert.equal(allMaster.filter(n=>n.label==='BI BML011.016').length,1,'BI deve ser grupo próprio');
 assert.equal(allMaster.filter(n=>n.label==='AUDITORIA').length,1,'AUDITORIA antiga deve ser preservada');
 const nonMaster=perms.filtrarPorPermissao(nav.comAuditoriaMasterAdmin(base,false),perms.criarCan({
   enforced:true,schemaInstalled:true,isMasterAdmin:false,email:'',projectIdAcc:null,permissions:['permissions.manage'],anyProject:['permissions.manage']
 },'any'));
 assert.equal(routes(nonMaster).length,0,'admin comum nunca deve ver o BI');
});
test('every static runtime menu node is configurable',()=>{
 const catalogIds=new Set(model.CATALOG.map(n=>n.id));
 function verify(nodes){for(const n of nav.identify(nodes)){assert(catalogIds.has(n.id),'ausente no catalogo: '+n.id);if(n.children)verify(n.children);}}
 verify(nav.NAVEGACAO);
});
test('hidden Master Admin roots cannot be recreated after configuration',()=>{
 const items=clone();
 items.find(n=>n.id==='group:AUDITORIA').hidden=true;
 items.find(n=>n.id==='group:BI BML011.016').hidden=true;
 const configured=model.applyConfiguration(nav.contextualNavigation(false,true,true),{revision:4,items});
 const runtime=nav.comAuditoriaMasterAdmin(configured,true,[{id:'fase-1',name:'Fase 1'}]);
 assert(!runtime.some(n=>n.label==='AUDITORIA'));
 assert(!runtime.some(n=>n.label==='BI BML011.016'));
});
test('Plant 3D phase templates govern multiple dynamic phases without duplicates',()=>{
 const items=clone();
 items.find(n=>n.id==='template:plant3d-fase:qualitativa').hidden=true;
 const configured=model.applyConfiguration(nav.contextualNavigation(false,true,true),{revision:4,items});
 const runtime=nav.comAuditoriaMasterAdmin(configured,true,[{id:'f1',name:'Fase 1'},{id:'f2',name:'Fase 2'},{id:'f1',name:'duplicada'}]);
 const phaseLeaves=leaves(runtime).filter(n=>String(n.to||'').includes('/fases/'));
 assert.equal(phaseLeaves.length,4);
 assert.equal(new Set(phaseLeaves.map(n=>n.to)).size,4);
 assert(phaseLeaves.every(n=>!n.to.endsWith('/qualitativa')));
});
test('hidden Plant 3D phase template parent suppresses all generated phases',()=>{
 const items=clone();items.find(n=>n.id==='template:plant3d-fases').hidden=true;
 const configured=model.applyConfiguration(nav.contextualNavigation(false,true,true),{revision:4,items});
 const runtime=nav.comAuditoriaMasterAdmin(configured,true,[{id:'f1',name:'Fase 1'}]);
 assert(!leaves(runtime).some(n=>String(n.to||'').includes('/fases/f1/')));
});
test('BI BML011016 direct route guard fails closed',()=>{
 assert.equal(acessoBml.podeAcessarBiBml011016({isMasterAdmin:true},false,null),true);
 assert.equal(acessoBml.podeAcessarBiBml011016({isMasterAdmin:false},false,null),false);
 assert.equal(acessoBml.podeAcessarBiBml011016(null,false,null),false);
 assert.equal(acessoBml.podeAcessarBiBml011016({isMasterAdmin:true},false,'permission error'),false);
});console.log(count+' sidebar tests passed');
