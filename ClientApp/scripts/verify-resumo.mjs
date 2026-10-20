import {build} from 'esbuild';
import {readFileSync,writeFileSync,mkdirSync,readdirSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.resolve(root,'../_build_verify/resumo');mkdirSync(output,{recursive:true});
const entries=[
 'src/features/production/analytics/__tests__/resumo.test.ts',
 'src/features/production/analytics/__tests__/resumoPhases.test.ts',
 'src/features/production/analytics/__tests__/disciplinaApresentacao.test.ts',
 'src/features/production/dashboard/__tests__/ldpGates.test.ts',
];
await build({entryPoints:entries.map(p=>path.join(root,p)),entryNames:'[name]',outdir:path.join(output,'tests'),bundle:true,platform:'node',format:'esm',outExtension:{'.js':'.mjs'},alias:{'@':path.join(root,'src')},
  banner:{js:"import {createRequire as __createRequire} from 'node:module'; const require=__createRequire(import.meta.url);"},logLevel:'error'});
const files=entries.map(p=>path.join(output,'tests',path.basename(p,'.ts')+'.mjs'));
if(files.length!==entries.length||!files.length)throw new Error('No explicit test files');
const tests=spawnSync(process.execPath,['--test',...files],{cwd:root,encoding:'utf8'});
writeFileSync(path.join(output,'targeted-tests.log'),tests.stdout+tests.stderr);console.log(tests.stdout.slice(-600));
if(tests.status!==0)process.exit(tests.status??1);
if(!process.argv.includes('--visual'))process.exit(0);
// Real component validation against a read-only snapshot of the official local repositories.
const reports=JSON.parse(readFileSync(path.join(output,'reports.json'),'utf8'));
const dashboards=JSON.parse(readFileSync(path.join(output,'dashboards.json'),'utf8'));
await build({stdin:{contents:`
import React from 'react';import {createRoot} from 'react-dom/client';import {MemoryRouter} from 'react-router-dom';
import {PermissionContext} from './src/features/permissions/PermissionProvider';
import Normal from './src/features/production/analytics/ResumoProducaoPage';import TV from './src/features/production/analytics/ResumoTvTestePage';
import './src/index.css';
const reports=${JSON.stringify(reports)},dashboards=${JSON.stringify(dashboards)};
const params=new URLSearchParams(location.search),r=reports.find(r=>r.projectId===params.get('project'))??reports[0];
if(params.get('shell')==='1'){document.getElementById('root').style.paddingTop='166px';document.getElementById('root').style.marginLeft='256px';}
window.fetch=async(input)=>{const url=new URL(String(input),'http://localhost');const id=url.searchParams.get('projectId');
 const result=url.pathname==='/api/reports/gestao-vista'?reports.find(r=>r.projectId===id):dashboards[id];
 return new Response(JSON.stringify(result),{status:200,headers:{'content-type':'application/json'}});};
const selection=[{id:r.projectId,name:r.nomeProjeto,hubId:''}];
createRoot(document.getElementById('root')).render(<MemoryRouter><PermissionContext.Provider value={{can:()=>true,loading:false,error:null,effective:null,reload:()=>{}}}>
 {params.get('mode')==='normal'?<Normal selection={selection}/>:<TV selection={selection}/>}</PermissionContext.Provider></MemoryRouter>);
setInterval(()=>{
 const stage=document.querySelector('.tv-stage');const cells=[...document.querySelectorAll('.resumo-discipline-card')];
 const dimensions={pageHeight:document.documentElement.scrollHeight,viewport:innerHeight,stageHeight:stage?.clientHeight,stageScroll:stage?.scrollHeight,
 clippedCards:cells.filter(c=>c.scrollHeight>c.clientHeight+1).length,
 clippedIndicators:[...document.querySelectorAll('.tv-tile>strong,.tv-tile .resumo-phase-strip')].filter(c=>c.scrollHeight>c.clientHeight+1).length,
 moneyVisible:stage?.innerText.includes('R$')??false};
 document.body.dataset.validation=JSON.stringify(dimensions);document.title='Resumo local snapshot verified';
},250);
`,resolveDir:root,sourcefile:'resumo-visual.tsx',loader:'tsx'},bundle:true,platform:'browser',format:'iife',outfile:path.join(output,'visual.js'),alias:{'@':path.join(root,'src')},logLevel:'error'});
const assetDir=path.resolve(root,'../_build_verify/resumo-vite/assets');
const css=readdirSync(assetDir).filter(p=>p.endsWith('.css')).sort().at(-1);
writeFileSync(path.join(output,'visual.html'),`<!doctype html><html class="dark"><meta charset="utf-8"><link rel="stylesheet" href="../resumo-vite/assets/${css}"><link rel="stylesheet" href="visual.css"><style>html,body{margin:0;background:#0b1321;color:#eef2f8}#root{padding:12px;box-sizing:border-box}</style><div id="root"></div><script src="visual.js"></script></html>`);
console.log('Local visual harness: '+path.join(output,'visual.html'));
