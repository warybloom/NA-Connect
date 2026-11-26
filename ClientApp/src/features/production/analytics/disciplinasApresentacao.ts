// APRESENTAÇÃO ÚNICA: os nomes vêm exclusivamente de DOC3 + Norma de Coordenação,
// resolvidos pelo CoordinationDisciplineResolver no backend; nunca adivinhar pelo título.
// Os valores vêm de projectFacts, e não são recontados a partir dos documentos.
import { consumption, factKey, projectFacts, type FactsReport } from "@/components/pmo/pmoFacts";
import { chaveDisciplina } from "../lancamentos/disciplinasNorma";

type DisciplinaFacts = ReturnType<typeof projectFacts>["disciplines"][number];
export interface DisciplinaExibida extends DisciplinaFacts {
  /** ID de apresentação (NORMA:* ou NAO_MAPEADA:*); jamais usado como filtro SQL. */
  idExibicao: string;
  /** Chaves REAIS dos dados, preservadas para filtros de lançamento. */
  origens: string[];
  oficial: boolean;
  diagnostico: string | null;
}
const somaConhecida = (valores: (number | null)[]) => {
  const encontrados = valores.filter((x): x is number => typeof x === "number" && Number.isFinite(x));
  return encontrados.length ? encontrados.reduce((a,b) => a+b,0) : null;
};

/**
 * Uma mesma disciplina pode estar escrita de maneiras diferentes na LDP e no Timesheet.
 * Só substitui seu nome se TODOS os documentos correspondentes apontarem para o MESMO
 * nome da Norma. Em caso de ambiguidade/ausência, não inventa: conserva em "Sem mapeamento".
 * Une aliases que foram provados equivalentes, somando as métricas prontas de projectFacts.
 */
export function disciplinasParaExibicao(reports: readonly FactsReport[]): DisciplinaExibida[] {
  const dados = projectFacts([...reports]).disciplines;
  // Compatibilidade durante o deploy: backend antigo ainda não expõe a resolução DOC3.
  // Não transformar todos os registros em "Sem mapeamento" por ausência do campo no JSON.
  const resolverDisponivel = reports.some(r => r.documentos.some(d => d.fonteDisciplinaNorma !== undefined));
  if (!resolverDisponivel) return dados.map(d => ({
    ...d,idExibicao:d.chave,origens:[d.chave],oficial:false,
    diagnostico:"Aguardando ativação da resolução DOC3/Norma no backend"
  }));
  const nomesPorOrigem = new Map<string, Set<string>>();
  for (const report of reports) {
    for (const doc of report.documentos) {
      if (!doc.disciplinaNorma) continue;
      // 15/10/2026: com DOC3 resolvido, disciplinaCanonica JÁ é a disciplina dos 2 primeiros caracteres do DOC3 (mesma
      // chave do previsto e do realizado no backend). O texto bruto da LDP só entra em backend antigo, sem esse campo.
      const nomes = doc.disciplinaCanonica ? [doc.disciplinaCanonica] : [doc.disciplina, doc.disciplinaPtPadrao, doc.disciplinaEnPadrao];
      for (const nome of nomes) {
        if (!nome) continue;
        const chave = chaveDisciplina(nome);
        if (!chave) continue;
        if (!nomesPorOrigem.has(chave)) nomesPorOrigem.set(chave,new Set());
        nomesPorOrigem.get(chave)!.add(doc.disciplinaNorma.trim());
      }
    }
  }
  const grupos = new Map<string, { nome: string; membros: DisciplinaFacts[]; oficial: boolean; diagnostico: string | null }>();
  for (const d of dados) {
    const vazio = d.chave === "__PMO_NULL__" || chaveDisciplina(d.Disciplina) === "GERAL / SEM DISCIPLINA";
    const nomes = nomesPorOrigem.get(chaveDisciplina(d.Disciplina));
    const canonicos = [...new Map([...(nomes??[])].map(nome => [chaveDisciplina(nome),nome])).values()];
    const oficial = !vazio && canonicos.length===1;
    const chave = oficial ? "NORMA:"+chaveDisciplina(canonicos[0]) : "NAO_MAPEADA:"+d.chave;
    const nome = oficial ? canonicos[0] : vazio ? "SEM DISCIPLINA" : "SEM MAPEAMENTO";
    const diagnostico = oficial ? null : vazio ? "Origem sem disciplina identificada" : canonicos.length>1 ?
      "DOC3 aponta para mais de uma disciplina da Norma; conferir documento" :
      "Origem sem correspondência comprovada na Norma: "+d.Disciplina;
    const group = grupos.get(chave);
    if (group) group.membros.push(d);
    else grupos.set(chave,{nome,membros:[d],oficial,diagnostico});
  }
  return [...grupos].map(([idExibicao,g]) => {
    const origens=g.membros.map(x=>x.chave);
    const hhPrevisto=somaConhecida(g.membros.map(x=>x.HH_Previsto));
    const hhRealizado=somaConhecida(g.membros.map(x=>x.HH_Realizado));
    const custoPrevisto=somaConhecida(g.membros.map(x=>x.Custo_Previsto));
    const custoRealizado=somaConhecida(g.membros.map(x=>x.Custo_Realizado));
    return {
      ...g.membros[0],idExibicao,origens,oficial:g.oficial,diagnostico:g.diagnostico,
      chave:idExibicao,Disciplina:g.nome,
      HH_Previsto:hhPrevisto,HH_Realizado:hhRealizado,
      SaldoHH:hhPrevisto!==null&&hhRealizado!==null?hhPrevisto-hhRealizado:null,
      ConsumoHH:consumption(hhRealizado,hhPrevisto),
      Custo_Previsto:custoPrevisto,Custo_Realizado:custoRealizado,
      SaldoCusto:custoPrevisto!==null&&custoRealizado!==null?custoPrevisto-custoRealizado:null,
      ConsumoCusto:consumption(custoRealizado,custoPrevisto),
      DocsSemCusto:g.membros.reduce((n,d)=>n+d.DocsSemCusto,0),
      DocsComCusto:g.membros.reduce((n,d)=>n+d.DocsComCusto,0),
      DocsCusto:g.membros.reduce((n,d)=>n+d.DocsCusto,0)
    };
  }).sort((a,b)=>a.Disciplina.localeCompare(b.Disciplina,"pt-BR")||a.idExibicao.localeCompare(b.idExibicao));
}
export function pertenceAoGrupo(disciplinaCanonica:string|null|undefined,disciplinaOriginal:string|null|undefined,grupo:DisciplinaExibida):boolean {
  const origens=new Set(grupo.origens);
  return origens.has(factKey(disciplinaCanonica??disciplinaOriginal??null)) ||
    origens.has(factKey(disciplinaOriginal??null));
}
