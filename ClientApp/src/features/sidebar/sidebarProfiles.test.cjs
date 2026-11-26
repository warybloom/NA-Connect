// Testes do Gerenciador de Sidebar por perfil (API mockada + lógica de escopo + UI via SSR).
// Executar: node src/features/sidebar/sidebarProfiles.test.cjs   (a partir de ClientApp/)
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),{createRequire}=require('node:module');
const app=path.resolve(__dirname,'../../..');const nodeRequire=createRequire(path.join(app,'package.json'));const ts=nodeRequire('typescript');
let fetchImpl=null;const fetchCalls=[];
const sandbox={URLSearchParams,AbortController,CustomEvent:class{constructor(t,o){this.type=t;this.detail=o?.detail;}},window:{dispatchEvent(){}},
  fetch:(...args)=>{fetchCalls.push(args);return fetchImpl(...args);},console};
const cache={};
function resolve(from,name){if(name.startsWith('@/'))name=path.join(app,'src',name.slice(2));else if(name.startsWith('.'))name=path.resolve(path.dirname(from),name);else return null;
  for(const c of [name,name+'.ts',name+'.tsx'])if(fs.existsSync(c)&&fs.statSync(c).isFile())return c;throw Error('Módulo não encontrado: '+name);}
function load(file){if(cache[file])return cache[file].exports;if(file.endsWith('.json'))return JSON.parse(fs.readFileSync(file,'utf8'));
  const module={exports:{}};cache[file]=module;
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{fileName:file,compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS,esModuleInterop:true,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  vm.runInNewContext(js,{...sandbox,module,exports:module.exports,require:name=>{const f=resolve(file,name);return f?load(f):nodeRequire(name);}});return module.exports;}
const dir=__dirname;
const api=load(path.join(dir,'sidebarApi.ts')),scope=load(path.join(dir,'sidebarProfileScope.ts')),model=load(path.join(dir,'sidebarModel.ts'));
const nav=load(path.join(dir,'officialNavigation.ts')),perms=load(path.join(app,'src/features/permissions/permissionLogic.ts'));
const {SidebarScopeBar}=load(path.join(dir,'SidebarScopeBar.tsx'));const React=nodeRequire('react'),{renderToStaticMarkup}=nodeRequire('react-dom/server');
const json=(status,body)=>Promise.resolve({ok:status>=200&&status<300,status,json:()=>body===undefined?Promise.reject(Error('sem corpo')):Promise.resolve(body)});
const clone=()=>model.DEFAULT_ITEMS.map(n=>({...n}));
const PROFILES=[[1,'MasterAdmin',true],[2,'Administrador'],[3,'Somente Leitura'],[4,'adm'],[6,'Controle de Produção Restrito'],[7,'TV'],[8,'Administrador de Telas TV'],[9,'PERFIL GESTÃO'],[10,'PERFIL GESTÃO cópia']]
  .map(([profileId,name,isMasterAdmin])=>({profileId,name,isMasterAdmin:!!isMasterAdmin,ativo:true,revision:0,configured:false}));
/** Revisão global 4 legada: 121 itens salvos, 56 ocultos. */
const legacyGlobal=()=>({revision:4,items:clone().slice(0,121).map((n,i)=>({...n,hidden:i<56}))});
const tests=[];const test=(name,fn)=>tests.push([name,fn]);

test('api: lista perfis reais via GET /api/sidebar/profiles',async()=>{
  fetchImpl=()=>json(200,{profiles:PROFILES});fetchCalls.length=0;
  const list=await api.listSidebarProfiles();
  assert.equal(fetchCalls[0][0],'/api/sidebar/profiles');assert.equal(fetchCalls[0][1].method,'GET');
  assert.deepEqual(list.map(p=>p.profileId),[1,2,3,4,6,7,8,9,10]);
  fetchImpl=()=>json(200,{items:[]});await assert.rejects(api.listSidebarProfiles(),/inválida/);
});
test('api: detalhe do perfil traz configuração e csrf; resposta de outro perfil é rejeitada',async()=>{
  const g=legacyGlobal();fetchImpl=()=>json(200,{configuration:g,profileId:9,profileName:'PERFIL GESTÃO',isMasterAdmin:false,configured:false,csrfToken:'tok-9'});fetchCalls.length=0;
  const d=await api.getSidebarProfile(9);assert.equal(fetchCalls[0][0],'/api/sidebar/profiles/9');assert.equal(d.csrfToken,'tok-9');
  await assert.rejects(api.getSidebarProfile(7),/inválida/);
  fetchCalls.length=0;await assert.rejects(api.getSidebarProfile(0),/Perfil inválido/);await assert.rejects(api.getSidebarProfile(1.5),/Perfil inválido/);assert.equal(fetchCalls.length,0);
});
test('api: publicar perfil usa PUT /profiles/{id}/publish, X-Sidebar-CSRF e corpo {revision,items}',async()=>{
  const items=clone();fetchImpl=(url,init)=>json(200,{revision:3,items:JSON.parse(init.body).items});fetchCalls.length=0;
  const r=await api.publishSidebarProfile(9,2,items,'tok-9');
  const [url,init]=fetchCalls[0];assert.equal(url,'/api/sidebar/profiles/9/publish');assert.equal(init.method,'PUT');
  assert.equal(init.headers['X-Sidebar-CSRF'],'tok-9');assert.equal(init.headers['Content-Type'],'application/json');
  assert.deepEqual(Object.keys(JSON.parse(init.body)),['revision','items']);assert.equal(JSON.parse(init.body).revision,2);assert.equal(r.revision,3);
});
test('api: 409 vira conflict; 404/503/rede/403 mantêm causas distintas',async()=>{
  fetchImpl=()=>json(409,{message:'O menu deste perfil mudou. Recarregue antes de publicar.'});
  await assert.rejects(api.publishSidebarProfile(9,2,clone(),'t'),e=>e.kind==='conflict'&&e.status===409&&/mudou/.test(e.message));
  fetchImpl=()=>json(409);await assert.rejects(api.publishSidebarProfile(9,2,clone(),'t'),e=>e.kind==='conflict'&&/Recarregue/.test(e.message));
  fetchImpl=()=>json(404);await assert.rejects(api.listSidebarProfiles(),e=>e.kind==='missing');
  fetchImpl=()=>json(503,{message:'Menu oficial indisponível. Verifique a aplicação manual do schema.'});await assert.rejects(api.listSidebarProfiles(),e=>e.kind==='database'&&/schema/.test(e.message));
  fetchImpl=()=>json(403);await assert.rejects(api.publishSidebarProfile(1,0,clone(),'t'),e=>e.kind==='forbidden');
  fetchImpl=()=>Promise.reject(new TypeError('Failed to fetch'));await assert.rejects(api.getSidebarProfile(9),e=>e.kind==='network');
});
test('api: publicação GLOBAL legada continua em PUT /api/sidebar/publish',async()=>{
  fetchImpl=()=>json(200,{revision:5,items:[]});fetchCalls.length=0;
  await api.sidebarRequest('/publish','PUT',{revision:4,items:[]},'tok-g');
  assert.equal(fetchCalls[0][0],'/api/sidebar/publish');assert.equal(fetchCalls[0][1].headers['X-Sidebar-CSRF'],'tok-g');
});
test('escopo: Master Admin sem layout próprio parte do catálogo completo SEM os 56 ocultos globais',()=>{
  const g=legacyGlobal();
  // Mesmo que um backend devolvesse a configuração global, o Master não herda as ocultações.
  for(const configuration of [{revision:0,items:clone()},g]){
    const base=scope.profileBaseConfiguration({configuration,profileId:1,profileName:'MasterAdmin',isMasterAdmin:true,configured:false,csrfToken:'t'});
    assert.equal(base.items.length,model.CATALOG.length);assert.equal(base.items.filter(n=>n.hidden).length,0);assert.equal(base.revision,0);
  }
  assert.equal(g.items.filter(n=>n.hidden).length,56,'global legado não pode ser alterado');
});
test('escopo: perfil comum sem layout herda a revisão global 4 (56 ocultos) completada em memória; CAS espera 0',()=>{
  const g=legacyGlobal();const before=JSON.stringify(g);
  const base=scope.profileBaseConfiguration({configuration:g,profileId:9,profileName:'PERFIL GESTÃO',isMasterAdmin:false,configured:false,csrfToken:'t'});
  assert.equal(base.revision,0,'primeira publicação do perfil espera revisão 0, não a revisão global 4');
  assert.equal(base.items.length,model.CATALOG.length);
  const hidden=new Set(g.items.filter(n=>n.hidden).map(n=>n.id));
  assert.equal(base.items.filter(n=>hidden.has(n.id)&&n.hidden).length,56,'as 56 escolhas anteriores são preservadas');
  for(const n of g.items){const m=base.items.find(x=>x.id===n.id);assert.equal(m.label,n.label);assert.equal(m.icon,n.icon);}
  assert.equal(JSON.stringify(g),before,'resposta global não é mutada');
  model.validateItems(base.items);
});
test('escopo: perfil configurado usa o próprio layout e a própria revisão',()=>{
  const items=clone();items.find(n=>n.id==='group:CDE').hidden=true;items[0].label='Renomeado';
  const base=scope.profileBaseConfiguration({configuration:{revision:7,items},profileId:7,profileName:'TV',isMasterAdmin:false,configured:true,csrfToken:'t'});
  assert.equal(base.revision,7);assert.equal(base.items.find(n=>n.id==='group:CDE').hidden,true);assert.equal(base.items[0].label,'Renomeado');
});
test('rascunhos: troca de escopo preserva rascunho sujo e nunca o aplica a outro perfil',()=>{
  const cfg={revision:0,items:clone()};const edited=clone();edited[0].hidden=!edited[0].hidden;
  let drafts=scope.stashDraft({},'profile:9',{config:cfg,items:edited,token:'t9',detail:null});
  assert.deepEqual(Object.keys(drafts),['profile:9']);
  let [taken,rest]=scope.takeDraft(drafts,'profile:10');assert.equal(taken,null,'perfil 10 não recebe rascunho do 9');assert.deepEqual(Object.keys(rest),['profile:9']);
  [taken,rest]=scope.takeDraft(drafts,'global');assert.equal(taken,null);
  [taken,rest]=scope.takeDraft(drafts,'profile:9');assert.equal(taken.items,edited);assert.equal(taken.token,'t9');assert.equal(Object.keys(rest).length,0);
  drafts=scope.stashDraft(drafts,'profile:9',{config:cfg,items:clone(),token:'t9',detail:null});assert.equal(Object.keys(drafts).length,0,'rascunho sem alteração não é guardado');
  assert.equal(scope.isDirty(cfg.items,[...clone()].reverse()),false,'ordem do array não é alteração');
  assert.equal(scope.scopeLabel('profile:9',PROFILES),'PERFIL GESTÃO');assert.equal(scope.scopeLabel('global',PROFILES),'Global');
});
test('segurança: perfil Master só é publicável por Master; preview de perfil comum não promete Master-only',()=>{
  assert.equal(scope.canPublishProfile({isMasterAdmin:true},false),false);assert.equal(scope.canPublishProfile({isMasterAdmin:true},true),true);
  assert.equal(scope.canPublishProfile({isMasterAdmin:false},false),true);
  assert.equal(scope.previewAsMasterAdmin({kind:'profile',profileId:9},{isMasterAdmin:false},true),false);
  assert.equal(scope.previewAsMasterAdmin({kind:'profile',profileId:1},{isMasterAdmin:true},false),false);
  assert.equal(scope.previewAsMasterAdmin({kind:'profile',profileId:1},{isMasterAdmin:true},true),true);
  const leaves=ns=>ns.flatMap(n=>n.children?leaves(n.children):[n]);
  const all=scope.fullyVisibleDefault();
  const tree=nav.contextualNavigation(false,true,scope.previewAsMasterAdmin({kind:'profile',profileId:9},{isMasterAdmin:false},true));
  const configured=model.applyConfiguration(tree,{revision:1,items:all});
  assert(!leaves(configured).some(n=>String(n.to||'').startsWith('/bi-bml011016/')),'layout visível não recria BI Master-only');
  const noPerms=perms.filtrarPorPermissao(configured,perms.criarCan({enforced:true,schemaInstalled:true,isMasterAdmin:false,email:'',projectIdAcc:null,permissions:[],anyProject:[]},'any'));
  assert.equal(leaves(noPerms).length,0,'visibilidade de perfil nunca concede permissão');
});
const render=props=>renderToStaticMarkup(React.createElement(SidebarScopeBar,{scope:{kind:'global'},profiles:PROFILES,profilesLoading:false,profilesError:null,detail:null,viewerIsMasterAdmin:true,pendingDrafts:[],onScopeChange(){},...props}));
test('ui: seletor de escopo GLOBAL/Por Perfil com perfis reais',()=>{
  const html=render({scope:{kind:'profile',profileId:9}});
  assert.match(html,/GLOBAL \(legado\)/);assert.match(html,/Por Perfil/);
  for(const p of PROFILES)assert(html.includes('>'+p.name),'perfil ausente: '+p.name);
  assert.match(html,/MasterAdmin · Master Admin/);assert.match(html,/sem layout próprio/);
  assert.match(html,/As permissões do usuário continuam sendo aplicadas/);
  assert.match(render({}),/aria-checked="true"[^>]*>.*GLOBAL/s);
});
test('ui: API por perfil indisponível desabilita "Por Perfil" sem bloquear o GLOBAL',()=>{
  const html=render({profiles:[],profilesError:new api.SidebarApiError('x','missing',404)});
  assert.match(html,/não possui a API \/api\/sidebar\/profiles/);
  assert.match(html,/<button[^>]*disabled=""[^>]*>.*?Por Perfil/s);
  assert.match(render({profiles:[],profilesError:new api.SidebarApiError('x','database',503)}),/schema de perfis/);
});
test('ui: perfil Master Admin informa que não herda ocultações e bloqueia publicação para não-Master',()=>{
  const detail={configuration:{revision:0,items:clone()},profileId:1,profileName:'MasterAdmin',isMasterAdmin:true,configured:false,csrfToken:'t'};
  const locked=render({scope:{kind:'profile',profileId:1},detail,viewerIsMasterAdmin:false});
  assert.match(locked,/Somente Master Admin pode publicar/);assert.match(locked,/não herda as ocultações globais/);
  assert.doesNotMatch(render({scope:{kind:'profile',profileId:1},detail,viewerIsMasterAdmin:true}),/Somente Master Admin pode publicar/);
  const common=render({scope:{kind:'profile',profileId:9},detail:{...detail,profileId:9,profileName:'PERFIL GESTÃO',isMasterAdmin:false,configuration:legacyGlobal()}});
  assert.match(common,/herda o menu global \(revisão 4\)/);
});
test('ui: aviso de rascunhos não publicados lista apenas outros escopos',()=>{
  const html=render({scope:{kind:'profile',profileId:7},pendingDrafts:[{key:'global',label:'Global'},{key:'profile:9',label:'PERFIL GESTÃO'},{key:'profile:7',label:'TV'}]});
  assert.match(html,/Rascunhos não publicados preservados: Global, PERFIL GESTÃO\./);assert.match(html,/não é aplicado a outro perfil/);
  assert.doesNotMatch(render({}),/Rascunhos não publicados/);
});
(async()=>{let passed=0;for(const [name,fn] of tests){try{await fn();console.log('PASS '+name);passed++;}catch(e){console.error('FAIL '+name);console.error(e);process.exitCode=1;}}
  console.log(passed+'/'+tests.length+' sidebar profile tests passed');})();
