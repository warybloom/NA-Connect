/* 03 DOCUMENTAÇÃO — etapas do fluxo de revisão por projeto e contagem por disciplina (lógica pura, testada em __tests__/documentacaoFluxo.test.ts).
   Etapa atual = LdpAtual.Status_In_Flow (sincronizado com Autodesk Forma/ACC) resolvido contra as etapas EFETIVAS do projeto do documento.
   Status sem etapa correspondente fica "fora do fluxo" (nunca vira Verificação por padrão). Sem fases/gates.
   Nenhuma etapa é presumida: projeto sem fluxo = PENDENTE DE CONFIGURAÇÃO (etapas N/A); API indisponível = nenhuma etapa. */
import {agrupar,classificarDocumento,rotulo,SEM_DISCIPLINA,type CatalogoDisciplinas} from "../gestaoVistaJamileLogic";
import type {EffectiveProjectFlow} from "./reviewFlowsApi";

/** Campos do documento da LDP usados aqui (subconjunto de EnrichedDocument). */
export interface DocFluxo{
 rowId:string;projectId:string;codigo:string|null;statusInterno:string|null;emitido:boolean;cancelado:boolean;isActivity?:boolean;
 /** Linha vazia/placeholder da LDP segundo a regra oficial do backend (mesma do Normalizador de Siglas). */
 semConteudoDocumental?:boolean;
 disciplina:string|null;disciplinaNorma?:string|null;disciplinaCanonica?:string|null;siglaDoc3?:string|null;fonteDisciplinaNorma?:string|null;
}

export const normalizarEtapa=(v:string|null|undefined)=>(v??"").normalize("NFD").replace(/[̀-ͯ]/g,"").toUpperCase().replace(/\s+/g," ").trim();

export interface ColunaEtapa{chave:string;nome:string}
const ordenadas=(f:EffectiveProjectFlow)=>[...f.stages].sort((a,b)=>a.order-b.order);
/**
 * União ordenada e estável das etapas dos projetos (na ordem da seleção): o primeiro fluxo dá a ordem; etapa nova de outro
 * projeto entra logo antes da próxima etapa já conhecida DAQUELE fluxo (ou no fim, se não houver). Mesmo nome (sem acento/caixa) = uma coluna.
 */
export function uniaoEtapas(fluxos:readonly EffectiveProjectFlow[]):ColunaEtapa[]{
 const out:ColunaEtapa[]=[];
 for(const f of fluxos){
  const chaves=ordenadas(f).map(s=>({chave:normalizarEtapa(s.name),nome:s.name.trim()})).filter(s=>s.chave);
  chaves.forEach((s,i)=>{
   if(out.some(c=>c.chave===s.chave))return;
   const proxima=chaves.slice(i+1).map(x=>out.findIndex(c=>c.chave===x.chave)).find(j=>j>=0);
   out.splice(proxima??out.length,0,s);
  });
 }
 return out;
}

/** Etapa (chave da coluna) do Status_In_Flow no fluxo do projeto; null = fora do fluxo. Só etapas aplicáveis ao projeto. */
export function resolverEtapa(fluxo:EffectiveProjectFlow|undefined,status:string|null):string|null{
 if(!fluxo||!status?.trim())return null;
 const value=normalizarEtapa(status);
 for(const s of ordenadas(fluxo)){
  if(!s.applicable)continue;
  const chaves=(s.statusInFlowValues??[]).map(normalizarEtapa).filter(Boolean);
  if(chaves.includes(value))return normalizarEtapa(s.name);
 }
 return null;
}

type Situacao={tipo:"etapa";etapa:string}|{tipo:"emitido"}|{tipo:"fora";status:string}|{tipo:"sem-status"};
interface DocResolvido{chave:string;projectId:string;disciplina:string;situacao:Situacao}

/** Contagem por etapa: número, ou null = N/A (etapa não aplicável a nenhum projeto da linha). */
export interface LinhaDisciplina{chave:string;nome:string;total:number;etapas:Record<string,number|null>}
export interface AnaliseDocumentacao{
 colunas:ColunaEtapa[];linhas:LinhaDisciplina[];total:LinhaDisciplina;
 /** Documentos (projeto + código) com etapa atual resolvida no fluxo do projeto. */
 emFluxo:number;emitidos:number;semStatus:number;
 /** Status_In_Flow que não corresponde a nenhuma etapa aplicável do projeto (valor original × quantidade). */
 foraDoFluxo:{status:string;quantidade:number}[];
 /** Linhas da LDP do mesmo projeto + código além da primeira (revisões/repetições contadas uma vez). */
 repetidos:number;cancelados:number;
 /** Linhas da LDP sem número identificável nem qualquer dado documental (ex.: "----"): fora de toda contagem. */
 naoDocumentais:number;
 projetosSemFluxo:string[];
}

const chaveDocumento=(d:DocFluxo)=>d.projectId+"::"+(d.codigo?.trim().toUpperCase()||"#"+d.rowId);

/**
 * Documentos previstos = linhas documentais (sem AT) não canceladas, uma por projeto + código. Com várias linhas do mesmo
 * documento (revisões), vale a revisão em fluxo (não emitida, com etapa resolvida); senão a última não emitida; senão a última.
 */
export function analisarDocumentacao({docs,fluxos,cat}:{docs:readonly DocFluxo[];fluxos:readonly EffectiveProjectFlow[];cat:CatalogoDisciplinas}):AnaliseDocumentacao{
 const porProjeto=new Map(fluxos.map(f=>[f.projectId,f]));
 const colunas=uniaoEtapas(fluxos);
 const aplicaveis=new Map(fluxos.map(f=>[f.projectId,new Set(f.stages.filter(s=>s.applicable).map(s=>normalizarEtapa(s.name)))]));
 // Mesmo universo do Normalizador de Siglas: sem AT e sem as linhas vazias do template (regra do backend, não recalculada aqui).
 const documentais=docs.filter(d=>!d.isActivity&&!d.semConteudoDocumental);
 const naoDocumentais=docs.filter(d=>!d.isActivity&&d.semConteudoDocumental).length;
 const grupos=agrupar(documentais,chaveDocumento);
 let repetidos=0,cancelados=0;
 const resolvidos:DocResolvido[]=[];
 for(const [chave,linhas] of grupos){
  repetidos+=linhas.length-1;
  const ativas=linhas.filter(l=>!l.cancelado);
  if(!ativas.length){cancelados++;continue;}
  const fluxo=porProjeto.get(ativas[0].projectId);
  const emFluxo=ativas.filter(l=>!l.emitido&&resolverEtapa(fluxo,l.statusInterno)!==null);
  const naoEmitidas=ativas.filter(l=>!l.emitido);
  const d=emFluxo.at(-1)??naoEmitidas.at(-1)??ativas.at(-1)!;
  const etapa=d.emitido?null:resolverEtapa(fluxo,d.statusInterno);
  const situacao:Situacao=d.emitido?{tipo:"emitido"}:etapa?{tipo:"etapa",etapa}:d.statusInterno?.trim()?{tipo:"fora",status:d.statusInterno.trim()}:{tipo:"sem-status"};
  resolvidos.push({chave,projectId:d.projectId,disciplina:classificarDocumento(cat,d).chave,situacao});
 }
 const linha=(chave:string,nome:string,xs:readonly DocResolvido[],projetos:readonly string[]):LinhaDisciplina=>{
  const etapas:Record<string,number|null>={};
  for(const c of colunas){
   const aplicavel=projetos.some(p=>aplicaveis.get(p)?.has(c.chave));
   etapas[c.chave]=aplicavel?xs.filter(x=>x.situacao.tipo==="etapa"&&x.situacao.etapa===c.chave).length:null;
  }
  return {chave,nome,total:Object.values(etapas).reduce<number>((n,valor)=>n+(valor??0),0),etapas};
 };
 const linhas=[...agrupar(resolvidos,x=>x.disciplina)].map(([k,xs])=>linha(k,rotulo(cat,k),xs,[...new Set(xs.map(x=>x.projectId))]))
  .sort((a,b)=>(a.chave===SEM_DISCIPLINA?1:0)-(b.chave===SEM_DISCIPLINA?1:0)||a.nome.localeCompare(b.nome,"pt-BR"));
 const fora=new Map<string,number>();
 for(const x of resolvidos)if(x.situacao.tipo==="fora")fora.set(x.situacao.status,(fora.get(x.situacao.status)??0)+1);
 const total=linha("__TOTAL__","TOTAL",resolvidos,fluxos.map(f=>f.projectId));
 return {colunas,linhas,total,
  emFluxo:total.total,emitidos:resolvidos.filter(x=>x.situacao.tipo==="emitido").length,
  semStatus:resolvidos.filter(x=>x.situacao.tipo==="sem-status").length,
  foraDoFluxo:[...fora].map(([status,quantidade])=>({status,quantidade})).sort((a,b)=>b.quantidade-a.quantidade||a.status.localeCompare(b.status,"pt-BR")),
  repetidos,cancelados,naoDocumentais,projetosSemFluxo:fluxos.filter(f=>f.source==="none"||!f.stages.some(s=>s.applicable)).map(f=>f.projectId)};
}

/** Garante uma entrada por projeto selecionado: projeto ausente na resposta = sem fluxo (nada inventado). */
export function completarFluxos(projectIds:readonly string[],recebidos:readonly EffectiveProjectFlow[]):EffectiveProjectFlow[]{
 const m=new Map(recebidos.map(f=>[f.projectId,f]));
 return projectIds.map(id=>m.get(id)??{projectId:id,flowId:null,flowName:null,source:"none",stages:[]});
}
