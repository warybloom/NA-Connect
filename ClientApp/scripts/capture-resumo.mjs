import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.resolve(root,'../_build_verify/resumo');
const edge='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const profile=path.join(output,'edge-cdp-'+Date.now());mkdirSync(profile,{recursive:true});
const process=spawn(edge,['--headless=new','--disable-gpu','--no-first-run','--disable-extensions','--allow-file-access-from-files','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{windowsHide:true,stdio:'ignore'});
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
let ws;
try{
 const portFile=path.join(profile,'DevToolsActivePort');
 for(let i=0;i<100&&!existsSync(portFile);i++)await wait(100);
 const port=readFileSync(portFile,'utf8').split('\n')[0];
 const targets=await (await fetch('http://127.0.0.1:'+port+'/json/list')).json();
 ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
 await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject});
 let next=0;const pending=new Map();
 ws.onmessage=event=>{const m=JSON.parse(event.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result)}};
 const call=(method,params={})=>new Promise((resolve,reject)=>{const id=++next;pending.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params}));});
 await call('Page.enable');
 const reports=JSON.parse(readFileSync(path.join(output,'reports.json'),'utf8'));
 const cases=[{name:'tv-fullhd-tipo2',type:2,width:1920,height:1080,mode:'tv'},{name:'tv-4k-tipo2',type:2,width:3840,height:2160,mode:'tv'},
  {name:'tv-fullhd-bml011016',type:1,width:1920,height:1080,mode:'tv'},{name:'tv-fullhd-shell-tipo2',type:2,width:1920,height:1080,mode:'tv',shell:true},
  {name:'resumo-normal-tipo2',type:2,width:1920,height:1080,mode:'normal'}];
 const evidence=[];
 for(const c of cases){
  const r=reports.find(r=>r.tipoLdp===c.type);if(!r)continue;
  await call('Emulation.setDeviceMetricsOverride',{width:c.width,height:c.height,deviceScaleFactor:1,mobile:false});
  const url=new URL(pathToFileURL(path.join(output,'visual.html')));url.searchParams.set('mode',c.mode);url.searchParams.set('project',r.projectId);
  if(c.shell)url.searchParams.set('shell','1');
  await call('Page.navigate',{url:url.href});
  for(let i=0;i<80;i++){
   await wait(100);
   const ready=await call('Runtime.evaluate',{expression:"document.querySelectorAll('[data-testid=\"resumo-disciplina\"]').length",returnByValue:true});
   if(ready.result.value>0)break;
  }
  await wait(500);
  const result=await call('Runtime.evaluate',{expression:`JSON.stringify({pageHeight:document.documentElement.scrollHeight,viewport:innerHeight,
   clippedCards:[...document.querySelectorAll('.resumo-discipline-card')].filter(c=>c.scrollHeight>c.clientHeight+1).length,
   clippedIndicators:[...document.querySelectorAll('.tv-tile>strong,.tv-tile .resumo-phase-strip')].filter(c=>c.scrollHeight>c.clientHeight+1).length,
   stageHeight:document.querySelector('.tv-stage')?.clientHeight,stageScroll:document.querySelector('.tv-stage')?.scrollHeight,
   moneyVisible:document.querySelector('.tv-stage')?.innerText.includes('R$')??false})`,returnByValue:true});
  const metrics=JSON.parse(result.result.value);evidence.push({name:c.name,...metrics});console.log(c.name+': '+JSON.stringify(metrics));
  const screenshot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width:c.width,height:c.mode==='normal'?metrics.pageHeight:c.height,scale:1}});
  writeFileSync(path.join(output,c.name+'.png'),Buffer.from(screenshot.data,'base64'));
  if(c.mode==='tv'&&(metrics.pageHeight>metrics.viewport||metrics.clippedCards||metrics.clippedIndicators||metrics.stageScroll>metrics.stageHeight+1||metrics.moneyVisible))throw Error('Layout validation failed: '+c.name);
 }
 writeFileSync(path.join(output,'visual-evidence.json'),JSON.stringify(evidence,null,2));
 await call('Browser.close');
}finally{ws?.close();process.kill();}
