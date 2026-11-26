// Regression: BI BML011.016 menu must survive the ACTUAL sidebar permission filtering.
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const ts = require('../node_modules/typescript');
const root = path.resolve(__dirname, '../..'); const cache = {};
function load(file) {
  file = path.resolve(file);
  if (cache[file]) return cache[file];
  const exports = {}; cache[file] = exports;
  const js = ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS,esModuleInterop:true}
  }).outputText;
  const req = name => {
    if (name === 'lucide-react') return new Proxy({}, {get:(_,k)=>k});
    const source = name.startsWith('@/') ? root+'/ClientApp/src/'+name.slice(2) : path.resolve(path.dirname(file),name);
    return load(source.endsWith('.json') ? source : source+'.ts');
  };
  vm.runInNewContext(js,{exports,require:req,URLSearchParams}); return exports;
}
const nav=load(root+'/ClientApp/src/features/sidebar/officialNavigation.ts');
const perms=load(root+'/ClientApp/src/features/permissions/permissionLogic.ts');
const leaves=nodes=>nodes.flatMap(n=>n.children?leaves(n.children):[n]);
const routes=nodes=>leaves(nodes).filter(n=>String(n.to||'').startsWith('/bi-bml011016/'));
const base=nav.identify(nav.NAVEGACAO);
const masterEff={enforced:true,schemaInstalled:true,isMasterAdmin:true,email:'master@example.test',projectIdAcc:null,permissions:[],anyProject:[]};
const master=perms.filtrarPorPermissao(nav.comAuditoriaMasterAdmin(base,true),perms.criarCan(masterEff,'any'));
assert.equal(routes(master).length,9,'Master Admin must see all 9 pages');
assert.equal(master.filter(n=>n.label==='BI BML011.016').length,1,'BI should be independent root group');
assert.equal(master.filter(n=>n.label==='AUDITORIA').length,1,'Previously existing AUDITORIA must remain');
const adminEff={...masterEff,isMasterAdmin:false,permissions:['permissions.manage'],anyProject:['permissions.manage']};
const admin=perms.filtrarPorPermissao(nav.comAuditoriaMasterAdmin(base,false),perms.criarCan(adminEff,'any'));
assert.equal(routes(admin).length,0,'Other admins must not see BI');
assert.equal(routes(nav.comAuditoriaMasterAdmin(base,undefined)).length,0,'Not-yet-loaded identity must not see BI');
console.log('PASS: master sees 9 pages after filtering; normal admin 0; AUDITORIA preserved; separate root item.');
