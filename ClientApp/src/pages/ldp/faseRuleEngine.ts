// Motor de classificação por Fase — puro, sem dependência de React/rede. Usado pelo botão
// "Testar Regras" (client-side, contra o dataset de preview já disponível na sessão) e reutilizável
// depois pelo backend caso a classificação precise rodar em geração real (mesma lógica, sem duplicar
// regra de negócio). Nenhum dado é persistido aqui — "SEM FASE"/"CONFLITO" nunca são escondidos.
import { ProjetoFase, OperadorRegra, ResultadoTesteRegras } from "@/types/ldp-config";

export function avaliarOperador(valorCampo: string, operador: OperadorRegra, valorComparacao: string): boolean {
  const a = (valorCampo ?? "").trim().toUpperCase();
  const b = (valorComparacao ?? "").trim().toUpperCase();
  switch (operador) {
    case "IGUAL_A":
      return a === b;
    case "CONTEM":
      return a.includes(b);
    case "COMECA_COM":
      return a.startsWith(b);
    case "TERMINA_COM":
      return a.endsWith(b);
    default:
      return false;
  }
}

export function linhaCorrespondeAFase(linha: Record<string, string>, fase: ProjetoFase): boolean {
  const regrasAtivas = fase.regras.filter((r) => r.ativa);
  if (regrasAtivas.length === 0) return false;
  const resultados = regrasAtivas.map((r) => avaliarOperador(linha[r.campoOrigem] ?? "", r.operador, r.valorComparacao));
  return fase.operadorLogico === "ANY" ? resultados.some(Boolean) : resultados.every(Boolean);
}

export function classificarDataset(linhas: Record<string, string>[], fases: ProjetoFase[]): ResultadoTesteRegras {
  const fasesAtivas = fases.filter((f) => f.ativa);
  const contagemPorFase = new Map<string, number>();
  fasesAtivas.forEach((f) => contagemPorFase.set(f.id, 0));

  const semFase: Record<string, string>[] = [];
  const conflitos: ResultadoTesteRegras["conflitos"] = [];

  for (const linha of linhas) {
    const fasesQueBateram = fasesAtivas.filter((f) => linhaCorrespondeAFase(linha, f));
    if (fasesQueBateram.length === 0) {
      semFase.push(linha);
    } else if (fasesQueBateram.length > 1) {
      conflitos.push({ linha, fases: fasesQueBateram.map((f) => ({ faseId: f.id, codigo: f.codigo })) });
    } else {
      contagemPorFase.set(fasesQueBateram[0].id, (contagemPorFase.get(fasesQueBateram[0].id) ?? 0) + 1);
    }
  }

  return {
    porFase: fasesAtivas
      .sort((a, b) => a.ordem - b.ordem)
      .map((f) => ({ faseId: f.id, codigo: f.codigo, nome: f.nome, quantidade: contagemPorFase.get(f.id) ?? 0 })),
    semFase,
    conflitos,
  };
}
