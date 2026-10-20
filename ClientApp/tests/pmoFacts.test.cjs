const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const ts = require('typescript');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname,'../src/components/pmo/pmoFacts.ts'),'utf8');
const code = ts.transpileModule(source, {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText;
const sandbox={exports:{},require:()=>({SEM_VALOR:'__PMO_NULL__'})};
vm.runInNewContext(code,sandbox);
const {sumFacts,consumption,balance,projectFacts}=sandbox.exports;
test('unknown, NaN and Infinity never become fabricated values',()=>{
  for(const v of [null,undefined,NaN,Infinity,-Infinity]){
    assert.equal(sumFacts([10,v]),null); assert.equal(consumption(v,100),null); assert.equal(balance(100,v),null);
  }
  assert.equal(sumFacts([]),null); assert.equal(consumption(10,0),null);
  assert.ok(Math.abs(consumption(6129.14,10000)-61.2914)<1e-10);
});
test('schedule-only discipline appears and no actual is changed into zero',()=>{
  const f=projectFacts([{planning:{plannedHhTotal:100,plannedHhByDiscipline:[{discipline:'Piping',plannedHh:100}],plannedCostByDiscipline:[]},
    actual:{total:{hh:null},byDiscipline:[]}}]);
  assert.equal(f.disciplines.length,1); assert.equal(f.disciplines[0].HH_Previsto,100);
  assert.equal(f.disciplines[0].HH_Realizado,null); assert.equal(f.cards.ConsumoHH,null);
});
test('multiple projects aggregate canonical facts without inspecting schedule leaves or LDP',()=>{
  const make=(hh)=>({planning:{plannedHhTotal:100,plannedHhByDiscipline:[],plannedCostByDiscipline:[]},actual:{total:{hh},byDiscipline:[]}});
  const f=projectFacts([make(20),make(30)]);
  assert.equal(f.cards.HH_Previsto,200);assert.equal(f.cards.HH_Realizado,50);assert.equal(f.cards.ConsumoHH,25);
});
