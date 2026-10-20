// Isolated browser regression: real auth/TV components; simulated API, no real passwords.
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { spawn } from "node:child_process";
import { createServer } from "node:http";
import path from "node:path";
const root = process.cwd(), out = path.resolve(root, "../_build_verify/usuario-na/routing-ui");
mkdirSync(out, { recursive: true });
await build({ stdin: { contents: `
import React from "react";
import {createRoot} from "react-dom/client";
import {MemoryRouter, Routes, Route} from "react-router-dom";
import ProtectedRoute from "./src/components/ProtectedRoute";
import {TvPortalEntry} from "./src/features/tv/portal/TvPortalEntry";
const requested=new URLSearchParams(location.search).get("case");
if(requested) {sessionStorage.setItem("case",requested);sessionStorage.setItem("calls","[]");}
const scenario=sessionStorage.getItem("case")||"tv";
window.fetch=async(url,options={})=>{
 const calls=JSON.parse(sessionStorage.getItem("calls")||"[]");
 calls.push({url:String(url),method:options.method||"GET"});sessionStorage.setItem("calls",JSON.stringify(calls));
 let status=200,body={};
 if(String(url).endsWith("CheckStatus"))body={isAuthenticated:scenario!=="unauthenticated"};
 else if(String(url).endsWith("/session")){
   status=scenario==="unavailable"?404:scenario==="expired"?401:200;
   body={restricted:!["admin","mixed","unavailable"].includes(scenario),canConfigure:scenario==="admin"||scenario==="mixed"};
 }else if(String(url).endsWith("/presentations"))body=scenario==="empty"?[]:[{id:"allowed",name:"TV autorizada",supported:true}];
 else if(String(url).includes("/presentations/")){status=403;}
 return new Response(JSON.stringify(body),{status,headers:{"content-type":"application/json"}});
};
createRoot(document.getElementById("root")).render(
 location.pathname === "/login" ? <p id="login">Login</p> : <TvPortalEntry><MemoryRouter><Routes><Route element={<ProtectedRoute/>}>
  <Route path="/" element={<aside id="admin-mounted">Dashboard administrativo</aside>}/>
 </Route><Route path="/login" element={<p id="login">Login</p>}/></Routes></MemoryRouter></TvPortalEntry>);
`, resolveDir: root, loader: "tsx" }, bundle: true, format: "iife",
  outfile: path.join(out, "app.js"), alias: { "@": path.join(root, "src") },
  plugins: [{ name: "fixture-slides", setup(b) {
    b.onResolve({ filter: /^\.\/tvScreens$/ }, () => ({ path: "slide", namespace: "fixture" }));
    b.onLoad({ filter: /.*/, namespace: "fixture" }, () => ({
      contents: 'import React from "react";export const getTelaTv=id=>({label:id});export const ConteudoTv=()=>React.createElement("div",null,"Slide fixture");',
      resolveDir: root,
    }));
  } }], logLevel: "error",
});
const server = createServer((req, res) => {
  res.setHeader("Content-Type", req.url === "/app.js" ? "text/javascript" : "text/html; charset=utf-8");
  res.end(req.url === "/app.js" ? readFileSync(path.join(out, "app.js")) :
    '<!doctype html><meta charset="utf-8"><div id="root"></div><script src="/app.js"></script>');
});
await new Promise(r => server.listen(0, "127.0.0.1", r));
const profile = path.join(out, "edge-" + Date.now());
const child = spawn("C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  ["--headless=new", "--disable-gpu", "--no-first-run", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank"],
  { windowsHide: true, stdio: "ignore" });
const wait = ms => new Promise(r => setTimeout(r, ms));
let ws;
try {
  for (let i = 0; i < 100 && !existsSync(path.join(profile, "DevToolsActivePort")); i++) await wait(100);
  const port = readFileSync(path.join(profile, "DevToolsActivePort"), "utf8").split("\n")[0];
  const tabs = await (await fetch("http://127.0.0.1:" + port + "/json/list")).json();
  ws = new WebSocket(tabs.find(t => t.type === "page").webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id) { pending.get(m.id)(m.result); pending.delete(m.id); } };
  const call = (method, params = {}) => new Promise(r => { pending.set(++id, r); ws.send(JSON.stringify({ id, method, params })); });
  const evaluate = async expression => {
    const r = await call("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true, userGesture: true });
    if (r.exceptionDetails) throw Error(JSON.stringify(r.exceptionDetails));
    return r.result.value;
  };
  const check = async (expression, label) => { if (!await evaluate(expression)) throw Error(label); console.log("OK " + label); };
  const navigate = async scenario => {
    await call("Page.navigate", { url: "http://127.0.0.1:" + server.address().port + "/?case=" + scenario });
    await wait(900);
  };
  await call("Page.enable");
  for (const scenario of ["admin", "mixed", "unavailable"]) {
    await navigate(scenario);
    await check('!!document.querySelector("#admin-mounted")&&location.pathname!=="/tv/iniciar"', scenario + " preserva dashboard sem bloqueio global");
  }
  await navigate("tv");
  await check('location.pathname==="/tv/iniciar"&&!document.querySelector("aside")&&!!document.querySelector("select")', "TV vai de / para entrada isolada sem montar admin");
  await check('JSON.parse(sessionStorage.calls).every(c=>c.method==="GET"&&!c.url.includes("/api/permissions")&&!c.url.includes("/api/robot"))', "TV nao monta providers administrativos");
  await navigate("empty");
  await check('document.body.textContent.includes("Nenhuma apresentação autorizada")&&document.body.textContent.includes("administrador")&&!document.querySelector("aside")', "sem atribuicao exibe instrucao sem falha de login");
  await navigate("forbidden");
  await evaluate('[...document.querySelectorAll("button")].find(b=>b.textContent.includes("Iniciar")).click()');
  await wait(450);
  await check('document.body.textContent.includes("vínculo ativo a todos os projetos")&&!document.querySelector("aside")', "403 da apresentacao explica atribuicao/projetos sem contornar seguranca");
  await navigate("expired");
  await check('!!document.querySelector("#login")&&!document.querySelector("aside")', "sessao vencida retorna ao login");
  await navigate("unauthenticated");
  await check('!!document.querySelector("#login")&&!JSON.parse(sessionStorage.calls).some(c=>c.url.endsWith("/session"))', "anonimo nao consulta sessao TV");
  writeFileSync(path.join(out, "results.json"), JSON.stringify({ passed: true, simulatedApi: true }));
  await call("Browser.close");
} finally { ws?.close(); child.kill(); server.close(); }
