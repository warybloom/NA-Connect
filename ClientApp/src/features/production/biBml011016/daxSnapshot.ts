/**
 * Medidas do Resumo BML011.016 reproduzidas A PARTIR DO PBIX ORIGINAL.
 * A auditoria das expressoes esta em %LOCALAPPDATA%/NAConnect/BI_BML011016/
 * auditoria_pbir_privada/modelo_inspecao_privada.json (75 medidas).
 * Nao generaliza DAX arbitrario. Implementa medidas identificadas explicitamente
 * com as mesmas colunas, denominadores, datas de corte e ponderacoes documentadas.
 */
export type Linha = Record<string, unknown>;
export type TabelasSnapshot = Record<string, Linha[]>;
export type Selecoes = Record<string,string[]>;

const dia = (s: unknown) => typeof s === "string" ? s.slice(0,10) : "";
const dataMs = (s: unknown) => { const d = Date.parse(dia(s)+"T00:00:00Z"); return Number.isFinite(d) ? d : NaN; };
const add = (s:string,n:number) => new Date(dataMs(s)+n*86400000).toISOString().slice(0,10);
const n = (s:unknown) => typeof s==="number"&&Number.isFinite(s)?s:typeof s==="string"&&s.trim()!==""&&Number.isFinite(Number(s))?Number(s):0;
// A exportacao Tabular embutida carrega alguns captions ANSI com U+FFFD
// (In�cio/T�rmino). Normalizar esse defeito SEM modificar dados/colunas na fonte.
export const chave = (s:string) => s.normalize("NFD").replace(/[\u0300-\u036f]/g,"")
  .replace(/[^a-z0-9]/gi,"").toLowerCase().replace(/^incio/,"inicio").replace(/^trmino/,"termino")
  .replace(/^durao/,"duracao");
// Cache o mapa de colunas por schema de tabela; WBX tem ~300 colunas e milhares
// de chamadas DAX. Sem cache, abrir a Curva S bloquearia o browser.
const schemas=new Map<string,Map<string,string>>();
const cc=(linha:Linha,c:string)=>{
  const keys=Object.keys(linha);
  const ident=`${keys.length}:${keys[0]??""}:${keys[keys.length-1]??""}`;
  let cols=schemas.get(ident);
  if(!cols){cols=new Map(keys.map(k=>[chave(k),k]));schemas.set(ident,cols);}
  return cols.get(chave(c));
};
export const lerCol=(linha:Linha,c:string):unknown=>{const k=cc(linha,c);return k?linha[k]:null;};
const num=(l:Linha,c:string)=>n(lerCol(l,c));
const soma=(ls:Linha[],c:string)=>ls.reduce((a,l)=>a+num(l,c),0);
const div=(num:number,den:number)=>den>0?num/den:null;
const clamp=(x:number)=>Math.min(1,Math.max(0,x));
const wbx=(t:TabelasSnapshot):Linha[]=>t["WBX"]??[];

export function filtroWBX(t:TabelasSnapshot,s:Selecoes):Linha[] {
  return wbx(t).filter(l=>Object.entries(s).every(([key,vals])=>{
    if(!key.startsWith("WBX:")||!vals.length)return true;
    const col=key.slice(4);return vals.includes(String(lerCol(l,col)??""));
  }));
}
export function semanaAtual(t:TabelasSnapshot,s:Selecoes):string {
  const selecionada=s["dSemana:Data"]?.[0];
  if(selecionada)return dia(selecionada);
  // A selecao original no PBIX do BML011.016 usa 02/08/2026
  const semanas=(t["dSemana"]??[]).map(l=>dia(lerCol(l,"Data"))).filter(Boolean).sort();
  return semanas.filter(d=>d<="2026-08-05").at(-1)??semanas.at(-1)??"";
}
export function diasSemana(t:TabelasSnapshot):string[]{
  const weeks=(t["dSemana"]??[]).map(l=>dia(lerCol(l,"Data"))).filter(Boolean);
  return [...new Set(weeks)].sort();
}
export function filtraTrabalho(t:TabelasSnapshot,s:Selecoes):Linha[] {
  const all=t["AssignmentByDay"]??[];
  const selecionados=filtroWBX(t,s);
  const semFiltro=!Object.entries(s).some(([k,v])=>k.startsWith("WBX:")&&v.length);
  if(semFiltro)return all;
  const uids=new Set(selecionados.map(l=>String(lerCol(l,"NodeKey")??"")));
  return all.filter(l=>uids.has(String(lerCol(l,"TaskUID")??"")));
}
// % BASELINE: SUM(AssignmentByDay[Trab Linha de Base]) / CALCULATE(SUM(...),ALL(AssignmentByDay))
// % Complete Weekly: SUM(AssignmentByDay[Trabalho Real]) / CALCULATE(SUM([Trabalho]),ALL(AssignmentByDay))
export function medidasContexto(t:TabelasSnapshot,s:Selecoes) {
  const tasks=filtroWBX(t,s);
  const assignment=filtraTrabalho(t,s);
  const todas=t["AssignmentByDay"]??[];
  const totalBase=soma(todas,"Trab Linha de Base");
  const totalTrend=soma(todas,"Trabalho");
  const totalWbx=soma(tasks,"Trabalho");
  const semana=semanaAtual(t,s);
  const semanaFim=semana?add(semana,6):"";
  const noPeriodo=(l:Linha,start:string,end:string)=>dia(lerCol(l,"TimeByDay"))>=start&&dia(lerCol(l,"TimeByDay"))<=end;
  const sumAte=(lim:string,col:string)=>soma(assignment.filter(l=>dia(lerCol(l,"TimeByDay"))<=lim),col);
  const sumSemana=(inicio:string,col:string)=>soma(assignment.filter(l=>noPeriodo(l,inicio,add(inicio,6))),col);
  // % Previsto Reprog Curva / % Previsto LB Curva: SUMX(ALLSELECTED(WBX), Peso * Avanco) / SUM(WBX[Trabalho])
  const curva=(data:string,baseline:boolean,grupo=tasks)=>{
    const total=soma(grupo,"Trabalho");
    if(!(total>0)||!data)return null;
    const acum=grupo.reduce((acc,l)=>{
      const init=dataMs(lerCol(l,baseline?"Início da Linha de Base":"Início"));
      const end=dataMs(lerCol(l,baseline?"Término da Linha de Base":"Término"));
      if(!Number.isFinite(init)||!Number.isFinite(end))return acc;
      const at=dataMs(data);
      const f=at<init?0:at>=end?1:end<=init?1:clamp((at-init)/(end-init));
      return acc+num(l,"Trabalho")*f;
    },0);
    return div(acum,total);
  };
  return {tasks,assignment,semana,totalBase,totalTrend,totalWbx,
    sumAte,sumSemana,curva};
}
// Reaproveita contexto quando 34 visuais e 264 linhas WBX consultam o MESMO
// snapshot/filtros. Uma nova selecao cria novo objeto e invalida esse cache.
const contextoCache=new WeakMap<TabelasSnapshot,WeakMap<Selecoes,ReturnType<typeof medidasContexto>>>();
export function contexto(t:TabelasSnapshot,s:Selecoes) {
  let porFiltro=contextoCache.get(t);
  if(!porFiltro){porFiltro=new WeakMap();contextoCache.set(t,porFiltro);}
  let valor=porFiltro.get(s);
  if(!valor){valor=medidasContexto(t,s);porFiltro.set(s,valor);}
  return valor;
}
export function kpi(nome:string,t:TabelasSnapshot,s:Selecoes,semanaOverride?:string,grupo?:Linha[]): string|number|null {
  const ctx=contexto(t,s);
  const semana=semanaOverride??ctx.semana;
  const tasks=grupo??ctx.tasks;
  const tasksTrabalho=soma(tasks,"Trabalho");
  const corte="2026-08-05"; // constante explícita nas expressões DAX do PBIX fornecido
  const baseline=()=>div(ctx.sumSemana(semana,"Trab Linha de Base"),ctx.totalBase);
  const realSemana=()=>div(ctx.sumSemana(semana,"Trabalho Real"),ctx.totalTrend);
  const acumuladoReal=()=>div(ctx.sumAte(semana,"Trabalho Real"),ctx.totalTrend);
  const planned=()=>ctx.curva(semana?add(semana,6):"",false);
  switch(chave(nome)){
    case "activitytitle":return semana?`Week - ${semana.slice(8,10)}/${semana.slice(5,7)} to ${add(semana,6).slice(8,10)}/${add(semana,6).slice(5,7)}`:"Weekly Progress";
    case "trabalhoreal":return ctx.sumSemana(semana,"Trabalho Real");
    case "trabalholb":return ctx.sumSemana(semana,"Trab Linha de Base");
    case "previsto":return ctx.sumSemana(semana,"Trabalho");
    case "baseline":case "plannedweekly":return baseline();
    case "completeweekly":return realSemana();
    case "deviationweekly":{const b=baseline(),r=realSemana();return b===null||r===null?null:r-b;}
    case "accplanejado":return planned();
    case "accrealizadox":return acumuladoReal();
    case "accudeviation":{const p=planned(),r=acumuladoReal();return p===null||r===null?null:r-p;}
    case "previstoreprogcurva":return ctx.curva(semana?add(semana,3):"",false);
    case "previstolbcurva":return ctx.curva(semana,true);
    case "accrealizadocurva": {
      if(!semana)return null;
      const eixo=add(semana,3);
      if(eixo>corte)return null; // BLANK apos data de corte, como SWITCH() no PBIX
      if(eixo===corte){
        const totalReal=(t["AssignmentByDay"]??[]).reduce((acc,l)=>acc+Number(lerCol(l,"Trabalho Real")??0),0);
        return div(totalReal,ctx.totalTrend); // REMOVEFILTERS dCalendario no corte original
      }
      return div(ctx.sumAte(eixo,"Trabalho Real"),ctx.totalTrend);
    }
    case "actualworkcorte":return semana<=corte?div(ctx.sumSemana(semana,"Trabalho Real"),tasksTrabalho):null;
    case "plannedeap":return ctx.curva(semana?add(semana,6):"",false,tasks);
    case "actualworkeap": {
      // O numerador precisa respeitar as tarefas WBX do grupo do pivot,
      // atraves do relacionamento AssignmentByDay[TaskUID] -> WBX[NodeKey].
      const uids=new Set(tasks.map(l=>String(lerCol(l,"NodeKey")??"")));
      const ate=semana?add(semana,6):"";
      const realizadoGrupo=ctx.assignment.filter(l=>{
        const d=dia(lerCol(l,"TimeByDay"));
        return uids.has(String(lerCol(l,"TaskUID")??"")) && d>=semana && d<=ate;
      }).reduce((acc,l)=>acc+Number(lerCol(l,"Trabalho Real")??0),0);
      return div(realizadoGrupo,tasksTrabalho);
    }
    case "duraolb":case "duracaolb":case "durao":case "duracao":case "atraso":case "desviodeprazo": {
      const ini=String(kpi(chave(nome).endsWith("lb")?"Inicio LB":"Inicio",t,s,semana,tasks)??"");
      const fin=String(kpi("Termino",t,s,semana,tasks)??"");
      const fimLB=String(kpi("Término LB",t,s,semana,tasks)??"");
      const key=chave(nome);
      if(key==="atraso"||key==="desviodeprazo")return fin&&fimLB?Math.round((dataMs(fimLB)-dataMs(fin))/86400000):null;
      const end=key.endsWith("lb")?fimLB:fin;
      return ini&&end?Math.round((dataMs(end)-dataMs(ini))/86400000):null;
    }
    case "diasremanecentes": {
      const ini=String(kpi("Inicio",t,s,semana,tasks)??"");
      const fin=String(kpi("Termino",t,s,semana,tasks)??"");
      if(!ini||!fin)return null;
      const hoje=new Date().toISOString().slice(0,10);
      const d=(a:string,b:string)=>Math.round((dataMs(b)-dataMs(a))/86400000);
      if(hoje<ini)return `Projeto inicia em ${d(hoje,ini)}`;
      if(hoje>fin)return `Projeto concluído há ${d(fin,hoje)}`;
      return d(hoje,fin);
    }
    case "iniciolb":case "inicio":case "terminolb":case "termino": {
      const col=chave(nome)==="iniciolb"?"Início da Linha de Base":chave(nome)==="inicio"?"Início":chave(nome)==="terminolb"?"Término da Linha de Base":"Término";
      const ds=tasks.map(l=>dia(lerCol(l,col))).filter(Boolean).sort();
      if(!ds.length)return null;
      return chave(nome).startsWith("inicio")?ds[0]:ds[ds.length-1];
    }
    default:return null;
  }
}

export const kpiPercentual=(nome:string)=>[
  "baseline","plannedweekly","completeweekly","deviationweekly","accplanejado",
  "accrealizadox","accudeviation","previstoreprogcurva","previstolbcurva",
  "accrealizadocurva","actualworkcorte","plannedeap","actualworkeap"
].includes(chave(nome));
