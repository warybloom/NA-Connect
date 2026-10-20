import {build} from 'esbuild';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {spawn} from 'node:child_process';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const root=process.cwd(),out=path.resolve(root,'../_build_verify/item3-ui');
mkdirSync(out,{recursive:true});
await build({stdin:{contents:`import React from 'react';import {createRoot} from 'react-dom/client';
import {GerenciaEngenhariaItem3} from './src/features/production/analytics/item3/GerenciaEngenhariaItem3';
const p=new URLSearchParams(location.search);document.documentElement.className=p.get('theme');
const titulo='Memória de cálculo e especificação técnica do sistema de engenharia — título longo para verificação integral de quebra de linhas e legibilidade '.repeat(4);
const itens=Array.from({length:12},(_,i)=>({key:String(i),data:'2026-10-19',dia:'SEG 19/10',disciplina:'Engenharia de Processos',codigoDocumento:'CHS001-000-PR0-DE-001',tituloDocumento:titulo,elaborador:'Maria da Silva',verificador:'José Pereira',aprovador:'Ana Souza',situacao:'Em elaboração',hhPrevisto:1234.56}));
createRoot(document.getElementById('root')).render(<div className="gv-shell" style={{display:'block',height:'auto',minHeight:0,width:'auto',marginLeft:p.get('sidebar')+'px',padding:'12px'}}><GerenciaEngenhariaItem3 itens={itens} grupos={[{key:'p',nome:'Engenharia de Processos',entregaveis:12,hh:14814.72},{key:'c',nome:'Civil',entregaveis:2,hh:48}]} agruparPorDia={true} onAgruparPorDia={()=>{}} descricaoPeriodo="19/10 a 24/10"/></div>);`,resolveDir:root,loader:'tsx'},bundle:true,format:'iife',outfile:path.join(out,'visual.js'),logLevel:'error'});
writeFileSync(path.join(out,'visual.html'),`<!doctype html><meta charset="utf-8"><style>body{margin:0}*{box-sizing:border-box}.ge3-layout{height:calc(100vh - 32px)}@media(max-width:900px){.ge3-layout{height:auto}}</style><link rel="stylesheet" href="${pathToFileURL(path.resolve('src/features/production/analytics/gestaoVistaJamile.css'))}"><link rel="stylesheet" href="visual.css"><div id="root"></div><script src="visual.js"></script>`);
const profile=path.join(out,'edge-'+Date.now()),child=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--disable-gpu','--no-first-run','--allow-file-access-from-files','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{windowsHide:true,stdio:'ignore'});
const wait=ms=>new Promise(r=>setTimeout(r,ms));let ws;
try{
for(let i=0;i<100&&!existsSync(path.join(profile,'DevToolsActivePort'));i++)await wait(100);
const port=readFileSync(path.join(profile,'DevToolsActivePort'),'utf8').split('\n')[0];
const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();ws=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);
await new Promise(r=>ws.onopen=r);let id=0;const pending=new Map();ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)(m.result);pending.delete(m.id)}};
const call=(method,params={})=>new Promise(r=>{pending.set(++id,r);ws.send(JSON.stringify({id,method,params}))});await call('Page.enable');const results=[];
for(const [width,height] of [[1920,1080],[2560,1440],[3840,2160],[800,900],[480,900]])for(const sidebar of [0,256,360])for(const theme of ['light','dark']){
await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
const url=new URL(pathToFileURL(path.join(out,'visual.html')));url.search=new URLSearchParams({sidebar,theme});await call('Page.navigate',{url:url.href});await wait(200);
const m=await call('Runtime.evaluate',{returnByValue:true,expression:`(()=>{const week=document.querySelector('.ge3-week'),grid=document.querySelector('.ge3-discipline-grid'),panels=[...grid.children],title=document.querySelector('.ge3-title-cell span'),r=panels.map(x=>x.getBoundingClientRect());return {loaded:!!title,columns:getComputedStyle(grid).gridTemplateColumns.split(' ').length,below:r.every(x=>x.top>=week.getBoundingClientRect().bottom),titleComplete:title.scrollHeight<=title.clientHeight+1&&title.scrollWidth<=title.clientWidth+1,bodyOverflow:document.documentElement.scrollWidth>innerWidth+1}})()`});
const result={width,height,sidebar,theme,...m.result.value};results.push(result);if(!result.loaded||!result.below||!result.titleComplete||result.bodyOverflow||result.columns!==(width>900?2:1))throw Error(JSON.stringify(result));
if(sidebar===256){const shot=await call('Page.captureScreenshot',{format:'png'});writeFileSync(path.join(out,`${width}-${theme}.png`),Buffer.from(shot.data,'base64'));}
}
writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(`${results.length} casos aprovados: título completo, ordem dos blocos, colunas e ausência de overflow da página.`);await call('Browser.close');
}finally{ws?.close();child.kill();}
