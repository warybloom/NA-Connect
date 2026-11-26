import {build} from 'esbuild';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {spawn} from 'node:child_process';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const root=process.cwd(),out=path.resolve(root,'../_build_verify/hh-planejado');
mkdirSync(out,{recursive:true});
const source=readFileSync('src/features/production/analytics/GestaoVistaJamilePage.tsx','utf8');
const tile=source.slice(source.indexOf('function Tile('),source.indexOf('function Section('));
await build({stdin:{contents:`import React from 'react';import {createRoot} from 'react-dom/client';
const FASES=[30,60,90];const fmt=n=>new Intl.NumberFormat('pt-BR',{maximumFractionDigits:2}).format(n);
${tile}
const p=new URLSearchParams(location.search),large=p.has('large');
document.documentElement.className=p.get('theme');document.body.style.marginLeft=p.get('sidebar')+'px';
createRoot(document.getElementById('root')).render(<div className="gv-shell"><aside className="gv-sidebar"/><main className="gv-main"><div className="gv-content"><div className="gv-kpis gv-four gv-indicator-kpis">
<Tile title="HH PLANEJADO" className="gv-planned-hh" value={large?'16,84 Mil':'12,34'} gates={large?{30:123456789.12,60:98765432.98,90:1234567.89}:{30:1.23,60:4.56,90:6.55}}/>
{['HH REALIZADO','%HH CONSUMIDO','%CUSTO CONSUMIDO'].map(title=><Tile key={title} title={title} value="12,34"/>)}</div></div></main></div>);`,resolveDir:root,loader:'tsx'},bundle:true,format:'iife',outfile:path.join(out,'visual.js'),logLevel:'error'});
writeFileSync(path.join(out,'visual.html'),`<!doctype html><meta charset="utf-8"><style>body{margin:0}*{box-sizing:border-box}</style><link rel="stylesheet" href="${pathToFileURL(path.resolve('src/features/production/analytics/gestaoVistaJamile.css'))}"><div id="root"></div><script src="visual.js"></script>`);
const profile=path.join(out,'edge-'+Date.now()),child=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--disable-gpu','--no-first-run','--allow-file-access-from-files','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{windowsHide:true,stdio:'ignore'});
const wait=ms=>new Promise(r=>setTimeout(r,ms));let ws;
try{
for(let i=0;i<100&&!existsSync(path.join(profile,'DevToolsActivePort'));i++)await wait(100);
const port=readFileSync(path.join(profile,'DevToolsActivePort'),'utf8').split('\n')[0];
const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();ws=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);
await new Promise(r=>ws.onopen=r);let id=0;const pending=new Map();ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)(m.result);pending.delete(m.id)}};
const call=(method,params={})=>new Promise(r=>{pending.set(++id,r);ws.send(JSON.stringify({id,method,params}))});await call('Page.enable');const results=[];
for(const [width,height] of [[1920,1080],[2560,1440],[3840,2160],[900,900],[600,900]])for(const sidebar of [64,256,360])for(const theme of ['light','dark'])for(const large of [false,true]){
await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
const url=new URL(pathToFileURL(path.join(out,'visual.html')));url.search=new URLSearchParams({sidebar,theme,...(large?{large:1}:{})});await call('Page.navigate',{url:url.href});await wait(160);
const m=await call('Runtime.evaluate',{returnByValue:true,expression:`(()=>{const card=document.querySelector('.gv-planned-hh');if(!card)return {failed:true};const c=card.getBoundingClientRect(),values=[...card.querySelectorAll('.gv-gates b')],rects=values.map(v=>v.getBoundingClientRect());return {values:values.map(v=>v.textContent),failed:rects.some((r,i)=>r.left<c.left||r.right>c.right+1||values[i].scrollWidth>values[i].clientWidth+1)||rects.some((r,i)=>rects.some((s,j)=>i!==j&&r.left<s.right&&r.right>s.left&&r.top<s.bottom&&r.bottom>s.top)),rows:new Set(rects.map(r=>r.top)).size}})()`});
const layout=await call('Runtime.evaluate',{returnByValue:true,expression:`(()=>{const grid=document.querySelector('.gv-indicator-kpis'),cards=[...grid.children],r=cards.map(c=>c.getBoundingClientRect());return {cardRows:new Set(r.map(x=>x.top)).size,columns:getComputedStyle(grid).gridTemplateColumns.split(' ').length}})()`});
results.push({width,height,sidebar,theme,large,...m.result.value,...layout.result.value});if(m.result.value.failed||(width>=1920&&layout.result.value.cardRows!==1)||![1,2,4].includes(layout.result.value.columns))throw Error(JSON.stringify(results.at(-1)));
if(sidebar===360&&large){const shot=await call('Page.captureScreenshot',{format:'png'});writeFileSync(path.join(out,`${width}-${theme}.png`),Buffer.from(shot.data,'base64'));}
}
writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));console.log(`${results.length} layout cases passed (actual Tile, small/large values, themes and sidebars).`);await call('Browser.close');
}finally{ws?.close();child.kill();}
