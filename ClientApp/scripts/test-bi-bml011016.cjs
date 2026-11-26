const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),ts=require('../node_modules/typescript');
const root=path.resolve(__dirname,'../..');
function load(file){const exports={};const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText;vm.runInNewContext(js,{exports,require});return exports;}
const acesso=load(root+'/ClientApp/src/features/production/biBml011016/acessoBiBml011016.ts');
const paginas=load(root+'/ClientApp/src/features/production/biBml011016/biBml011016Paginas.ts');
const sidebar=fs.readFileSync(root+'/ClientApp/src/features/sidebar/officialNavigation.ts','utf8');
const app=fs.readFileSync(root+'/ClientApp/src/App.tsx','utf8');
assert.equal(paginas.BI_BML011016_PAGINAS.length,9);
assert.match(sidebar,/isMasterAdmin === true \? \[\.\.\.nodes, identify\(\[BI_BML011016_MASTER\]\)\[0\]/);
assert.match(app,/<Route element=\{<MasterAdminOnly \/>\}>[\s\S]*path="\/bi-bml011016\/:slug"/);
assert.equal(acesso.podeAcessarBiBml011016({isMasterAdmin:true},false,null),true);
assert.equal(acesso.podeAcessarBiBml011016({isMasterAdmin:false},false,null),false);
assert.equal(acesso.podeAcessarBiBml011016(null,false,null),false);
assert.equal(acesso.podeAcessarBiBml011016({isMasterAdmin:true},false,'erro'),false);
console.log('PASS BI BML011016: 9 páginas e rota master-only (master/comum/deslogado/erro)');