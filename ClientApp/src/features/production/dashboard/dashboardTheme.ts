// Cores compartilhadas dos dashboards do Relatório de Produção. Status com significado conhecido têm cor
// semântica fixa (chaves canônicas do backend: ReportStatusNormalizer + valores brutos normalizados);
// o resto usa a paleta categórica, sempre na mesma ordem. Nenhuma tela define cores próprias.

const PRIMARIA = "hsl(19 89% 54%)"; // --primary do tema N&A Connect

export const PALETA_CATEGORICA = [
  PRIMARIA, "#38bdf8", "#a78bfa", "#34d399", "#fbbf24", "#f472b6", "#22d3ee", "#a3e635", "#fb7185", "#818cf8", "#2dd4bf", "#facc15",
];

const SEMANTICA: Record<string, string> = {
  // documental / situação
  EMITIDO: "#34d399", APROVADO: "#10b981", CANCELADO: "#64748b", A_EMITIR: "#fbbf24", EM_ANALISE_CLIENTE: "#f59e0b",
  EM_ELABORACAO: "#38bdf8", ATENDIMENTO_COMENTARIOS: "#fb923c", EM_FLUXO: "#a78bfa", EM_VERIFICACAO: "#818cf8", NAO_INICIADO: "#94a3b8",
  // RNC / CAP (valores brutos normalizados)
  ABERTO: "#fb7185", FECHADO: "#34d399", CONCLUDED: "#34d399", OVERDUE: "#fb7185", "ON TIME": "#38bdf8",
  // presença (com/sem) e reprogramação
  COM: "#34d399", SEM: "#94a3b8", APOS_BASELINE: "#fb7185", ATE_BASELINE: "#34d399", SEM_BASELINE: "#94a3b8", SEM_REPROGRAMACAO: "#cbd5e1",
  YES: "#fb7185", NO: "#94a3b8",
  __NULL__: "#475569",
};

export function corDoPonto(chave: string, indice: number): string {
  return SEMANTICA[chave] ?? PALETA_CATEGORICA[indice % PALETA_CATEGORICA.length];
}

const NUM = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 0 });
const DEC = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export function formatarValor(valor: number | null | undefined, formato: string): string {
  if (valor === null || valor === undefined) return "—";
  switch (formato) {
    case "percentual": return `${DEC.format(valor)}%`;
    case "horas": return `${DEC.format(valor)} h`;
    default: return NUM.format(valor);
  }
}

export function formatarCelula(valor: unknown, tipo: string): string {
  if (valor === null || valor === undefined || valor === "") return "—";
  if (tipo === "data" && typeof valor === "string") return new Date(valor).toLocaleDateString("pt-BR", { timeZone: "UTC" });
  if (tipo === "horas" && typeof valor === "number") return DEC.format(valor);
  return String(valor);
}
