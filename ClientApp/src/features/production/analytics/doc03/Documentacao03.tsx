/* 03 DOCUMENTAÇÃO — um cartão (DOCUMENTOS EM FLUXO), setas das etapas do(s) fluxo(s) de revisão ativo(s), legenda e a tabela
   STATUS DOS DOCUMENTOS POR DISCIPLINA (DISCIPLINA | TOTAL | etapas). Somente leitura: as etapas vêm do cadastro do Painel de
   Planejamento → Fluxos de Revisão (/api/review-flows/effective). Sem a API: erro explícito e NENHUMA etapa (nada presumido).
   Projeto sem fluxo: PENDENTE DE CONFIGURAÇÃO, etapas N/A. Disciplina = catálogo normalizado da Gestão à Vista. Sem fases/gates. */
import {useEffect,useMemo,useState,type CSSProperties} from "react";
import {TriangleAlert} from "lucide-react";
import {enrichDocuments,type FactsReport} from "@/components/pmo/pmoFacts";
import type {CatalogoDisciplinas} from "../gestaoVistaJamileLogic";
import {ReviewFlowsError,obterFluxosEfetivos,type EffectiveProjectFlow} from "./reviewFlowsApi";
import {analisarDocumentacao,completarFluxos} from "./documentacaoFluxoLogic";
import "./documentacao03.css";

type EstadoFluxos={fluxos:EffectiveProjectFlow[];erro:string|null;carregando:boolean};
/** Fluxos efetivos dos projetos selecionados. Falha da API = nenhuma etapa + erro visível (nunca silencioso, nunca presumido). */
export function useFluxosEfetivos(projectIds:readonly string[]):EstadoFluxos&{retry:()=>void}{
 const chave=projectIds.join("|");
 const [estado,setEstado]=useState<EstadoFluxos>({fluxos:[],erro:null,carregando:true});
 const [tentativa,setTentativa]=useState(0);
 useEffect(()=>{
  const ids=chave?chave.split("|"):[];
  const ctrl=new AbortController();
  setEstado(s=>({...s,carregando:true}));
  obterFluxosEfetivos(ids,ctrl.signal)
   .then(r=>{if(!ctrl.signal.aborted)setEstado({fluxos:completarFluxos(ids,Array.isArray(r?.projects)?r.projects:[]),erro:null,carregando:false});})
   .catch(e=>{if(ctrl.signal.aborted)return;
    const msg=e instanceof ReviewFlowsError||e instanceof Error?e.message:"Indisponível";
    setEstado({fluxos:[],erro:msg,carregando:false});});
  return()=>ctrl.abort();
 },[chave,tentativa]);
 return {...estado,retry:()=>setTentativa(t=>t+1)};
}

const valor=(n:number|null)=>n===null?"N/A":String(n);
const ROTA_CADASTRO="Painel de Planejamento → Fluxos de Revisão";

export default function Documentacao03({reports,cat,projetos}:{reports:FactsReport[];cat:CatalogoDisciplinas;projetos:{id:string;name:string}[]}){
 const ids=useMemo(()=>projetos.map(p=>p.id),[projetos]);
 const fl=useFluxosEfetivos(ids);
 const a=useMemo(()=>analisarDocumentacao({docs:enrichDocuments(reports),fluxos:fl.fluxos,cat}),[reports,fl.fluxos,cat]);
 const nome=(id:string)=>projetos.find(p=>p.id===id)?.name??id;
 const multi=projetos.length>1;
 const fluxosUsados=[...new Set(fl.fluxos.filter(f=>f.flowName).map(f=>f.flowName!))];
 const carregando=fl.carregando;
 const semEtapas=a.colunas.length===0;
 return <div className="gv-doc03" data-testid="gv-doc03">
  {fl.erro&&<div className="gv-warning gv-doc03-aviso" role="alert" data-testid="gv-doc03-aviso-api">
   <TriangleAlert size={14}/> Fluxos de revisão indisponíveis: {fl.erro}. As etapas não são exibidas — nenhuma etapa é presumida.
   {" "}<button type="button" className="gv-link" onClick={fl.retry}>Tentar novamente</button>
  </div>}
  {!fl.erro&&!carregando&&a.projetosSemFluxo.length>0&&<p className="gv-warning" role="status" data-testid="gv-doc03-sem-fluxo">
   Fluxo de revisão PENDENTE DE CONFIGURAÇÃO: {a.projetosSemFluxo.map(nome).join(", ")} ({ROTA_CADASTRO}). Etapas N/A para esse(s) projeto(s).</p>}
  <div className="gv-doc03-topo">
   <div className="gv-tile gv-doc03-kpi" data-testid="gv-doc03-em-fluxo"
    title={`Documentos (projeto + código) não emitidos e não cancelados cuja etapa atual (Status_In_Flow) corresponde a uma etapa do fluxo do projeto. Emitidos: ${a.emitidos}; sem status: ${a.semStatus}; fora do fluxo: ${a.foraDoFluxo.reduce((s,x)=>s+x.quantidade,0)}.`}>
    <span className="gv-tile-title">DOCUMENTOS EM FLUXO</span><strong className="gv-tile-value">{carregando?"…":fl.erro?"—":semEtapas?"N/A":String(a.emFluxo)}</strong>
    <small className="gv-tile-nota"><span className="gv-tile-nota-neutra">soma das etapas configuradas</span></small>
   </div>
   <div className="gv-flow gv-doc03-flow" role="list" aria-label="Etapas do fluxo de revisão" style={{"--gv-doc03-etapas":Math.max(1,a.colunas.length)} as CSSProperties}>
    {a.colunas.length?a.colunas.map((c,i)=><div key={c.chave} role="listitem" className={`gv-chevron gv-chevron-${i%4}`} data-testid="gv-doc03-etapa">
     <b>{c.nome}</b><strong>{carregando?"…":valor(a.total.etapas[c.chave])}</strong></div>)
     :<div className="gv-empty" data-testid="gv-doc03-sem-etapas">{carregando?"Carregando fluxos de revisão…":fl.erro?"Etapas indisponíveis (erro ao ler os fluxos de revisão).":`Fluxo de revisão PENDENTE DE CONFIGURAÇÃO (${ROTA_CADASTRO}).`}</div>}
   </div>
  </div>
  <p className="gv-doc03-legenda" data-testid="gv-doc03-legenda">
   Os números contemplam os documentos classificados em etapas aplicáveis do fluxo de revisão por todas as disciplinas do{multi?"s":""} projeto{multi?"s":""} selecionado{multi?"s":""}
   {" "}(cada documento = projeto + código, contado uma vez; AT, cancelados e linhas vazias da LDP fora). A etapa atual vem do campo Status_In_Flow sincronizado com o Autodesk Forma/ACC.
   {" "}N/A = etapa não aplicável ao projeto; 0 = etapa aplicável sem documentos.
   {" "}Etapas configuradas em {ROTA_CADASTRO}.
   {fluxosUsados.length>0&&<> Fluxo{fluxosUsados.length>1?"s":""}: {fluxosUsados.join(" · ")}.</>}
  </p>
  <section className="gv-section gv-section-navy gv-document-table"><header>STATUS DOS DOCUMENTOS POR DISCIPLINA</header><div className="gv-section-body">
   <div className="gv-table-scroll">
    <table data-testid="gv-doc03-tabela"><thead><tr><th>DISCIPLINA</th><th>DOCUMENTOS EM FLUXO</th>{a.colunas.map(c=><th key={c.chave}>{c.nome}</th>)}</tr></thead>
    <tbody>{a.linhas.map(l=><tr key={l.chave}><td>{l.nome}</td><td>{l.total}</td>
     {a.colunas.map(c=><td key={c.chave} className={l.etapas[c.chave]===null?"gv-doc03-na":undefined}>{valor(l.etapas[c.chave])}</td>)}</tr>)}</tbody>
    <tfoot><tr><td>TOTAL</td><td>{a.total.total}</td>{a.colunas.map(c=><td key={c.chave}>{valor(a.total.etapas[c.chave])}</td>)}</tr></tfoot>
    </table>
   </div>
  </div></section>
  <p className="gv-rule" data-testid="gv-doc03-regra">
   Documento em fluxo = não emitido, não cancelado e com Status_In_Flow correspondente a um alias cadastrado de etapa aplicável do fluxo do seu projeto; Status_In_Flow sem etapa correspondente
   não é atribuído a nenhuma etapa (nunca vira Verificação).
   {a.foraDoFluxo.length>0&&<> Fora do fluxo: {a.foraDoFluxo.map(x=>`"${x.status}" ${x.quantidade}`).join("; ")}.</>}
   {a.emitidos>0&&<> Emitidos: {a.emitidos}.</>}{a.semStatus>0&&<> Sem Status_In_Flow: {a.semStatus}.</>}
   {a.repetidos>0&&<> {a.repetidos} linha(s) repetida(s) do mesmo projeto + código (revisões) contada(s) uma vez.</>}
   {a.cancelados>0&&<> {a.cancelados} documento(s) cancelado(s) fora da contagem.</>}
   {a.naoDocumentais>0&&<> {a.naoDocumentais} linha(s) da LDP sem número nem dados documentais (ex.: "----") fora da contagem.</>}
  </p>
 </div>;
}
