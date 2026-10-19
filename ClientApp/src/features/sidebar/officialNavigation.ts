import {
  LogOut,
  ChevronRight,
  ChevronDown,
  Settings,
  Bell,
  LucideIcon,
  FileSpreadsheet,
  Box,
  FileText,
  BarChart3,
  LayoutTemplate,
  GitBranch,
  ClipboardList,
  CalendarClock,
  Calculator,
  FolderOpen,
  Factory,
  Layers,
  MonitorPlay,
  Bot,
  ShieldCheck,
  KeyRound,
  TrendingUp,
  FileStack,
  CalendarRange,
  ClipboardCheck,
  FileWarning,
  GitPullRequest,
  PauseCircle,
  Receipt,
  ShieldAlert,
  AlertOctagon,
  Coins,
  UserRound,
  Users,
  FolderKanban,
  CalendarCheck,
  Wallet,
  Gauge,
  BookOpen,
  CalendarDays,
  History,
  ListChecks,
  Timer,
  Database,
  ArrowLeftRight,
  FolderCog,
  Home,
  Palette,
  Scan,
  Scale,
  Languages,
} from "lucide-react";
import { navProducao, rotaProducao, type ProductionLeaf } from "@/features/production/productionRoutes";
import { BI_CHS001_GRUPO, BI_CHS001_PAGINAS, rotaBiChs001 } from "@/features/production/biChs001/biChs001Paginas";
import { PERMISSOES_CUSTOS_POR_CAMINHO, podeExibirCustosDuranteHomologacao, ROTAS_CUSTOS, TELAS_CUSTOS } from "@/features/planning/costs/custosRoutes";
import { PERMISSOES_AUDITORIA_PROJETO, ROTA_AUDITORIA_PROJETO } from "@/features/planning/auditoriaProjeto";
import { TEMPLATE_TYPES, rotaTemplate } from "@/features/settings/templateTypes";
type TomBadge = "dev" | "old" | "demo";

interface NavBadge {
  texto: string;
  tom: TomBadge;
}

export interface NavNode {
  id?: string;
  label: string;
  icon: LucideIcon;
  /** Folha: rota (pode ter ?tab=). Grupo: omitido. */
  to?: string;
  /** Permissão exigida (QUALQUER uma, se lista). OBRIGATÓRIA em toda folha; grupo aparece se algum filho aparecer. */
  permissionKey?: string | readonly string[];
  children?: NavNode[];
  /** Badge ao lado do rótulo (ex.: OLD). */
  badgeInline?: NavBadge;
  /** Badge abaixo do rótulo (ex.: EM DESENVOLVIMENTO, DEMONSTRAÇÃO). */
  badgeAbaixo?: NavBadge;
}

const EM_DESENVOLVIMENTO: NavBadge = { texto: "Em desenvolvimento", tom: "dev" };

// Ícone de cada tela do Controle de Produção (a estrutura/rotas/keys vêm de productionRoutes.ts).
const ICONE_PRODUCAO: Record<string, LucideIcon> = {
  "lista-documentos": ListChecks,
  "curva-s": TrendingUp, ldp: FileStack, programacao: CalendarRange, "cap-externo": ClipboardCheck, "cap-interno": ClipboardList,
  "desvio-escopo": FileWarning, "change-order": GitPullRequest, hold: PauseCircle, "boletim-medicao": Receipt,
  "desvio-qualidade": ShieldAlert, rnc: AlertOctagon, "controle-hh-custo": Coins,
  "analise-financeira/pessoa": UserRound, "analise-financeira/projeto": FolderKanban, "analise-financeira/documento": FileText,
  "indicadores-producao": Gauge,
  "lancamentos/pessoas": UserRound, "lancamentos/disciplinas": Layers, "lancamentos/documentos": FileText,
};
const ICONE_CUSTOS: Record<string, LucideIcon> = {
  [ROTAS_CUSTOS.impostos]: Receipt, [ROTAS_CUSTOS.profissionais]: UserRound, [ROTAS_CUSTOS.softwares]: Layers, [ROTAS_CUSTOS.pacotes]: FileStack,
};
const ICONE_GRUPO_PRODUCAO: Record<string, LucideIcon> = { Qualidade: ShieldCheck, "Análise Financeira": Wallet, "Lançamentos": ClipboardList };

const folhaProducao = (f: ProductionLeaf): NavNode =>
  ({ label: f.label, icon: ICONE_PRODUCAO[f.slug] ?? BarChart3, to: rotaProducao(f.slug), permissionKey: f.permissionKeys });

/** Controle de Produção SEM divisão Gerencial/Operacional: telas direto no grupo, liberadas pelas permissões. */
const CONTROLE_PRODUCAO: NavNode[] = navProducao().map((item): NavNode => item.kind === "leaf"
  ? folhaProducao(item)
  : { label: item.label, icon: ICONE_GRUPO_PRODUCAO[item.label] ?? Layers, children: item.children.map(folhaProducao) })
  // BI — CHS001 (Original): grupo exclusivo com as 23 páginas do PBIX, nomes e ordem do relatório (biChs001Paginas.ts).
  // Fora de PRODUCTION_NAV de propósito: não entra no catálogo da TV.
  .concat([{
    label: BI_CHS001_GRUPO, icon: Gauge,
    children: BI_CHS001_PAGINAS.map((p): NavNode => ({
      label: p.nome, icon: BarChart3, to: rotaBiChs001(p.slug), permissionKey: p.permissionKeys,
      ...(p.emDesenvolvimento ? { badgeAbaixo: EM_DESENVOLVIMENTO } : {}),
    })),
  }]);

export const NAVEGACAO: NavNode[] = [
  {
    label: "Painel de Planejamento",
    icon: CalendarClock,
    children: [
      { label: "Painel do Planejador", icon: ClipboardList, to: "/planejamento/painel-do-planejador", permissionKey: "planning.planner.view" },
      {
        label: "Custos do Projeto",
        icon: Coins,
        children: TELAS_CUSTOS.map((t) => ({ label: t.label, icon: ICONE_CUSTOS[t.to] ?? Coins, to: t.to, permissionKey: PERMISSOES_CUSTOS_POR_CAMINHO[t.to] })),
      },
      { label: "Auditoria do Projeto", icon: Scale, to: ROTA_AUDITORIA_PROJETO, permissionKey: PERMISSOES_AUDITORIA_PROJETO },
      { label: "Norma de Coordenação", icon: BookOpen, to: "/planejamento/normas-de-coordenacao", permissionKey: "planning.coordination.view" },
      // Configuração do Projeto: cadastro dos projetos (Tipo LDP, idioma, norma) + Logo do Projeto (identidade visual).
      { label: "Configuração do Projeto", icon: FolderCog, to: "/planejamento/configuracao-do-projeto", permissionKey: "settings.projects.view" },
      {
        label: "Programação Semanal",
        icon: CalendarDays,
        children: [
          { label: "Programação Semanal Vigente", icon: CalendarCheck, to: "/planejamento/programacao-semanal/vigente", permissionKey: "planning.weekly.current.view" },
          { label: "Análise da Programação da Semana Anterior", icon: History, to: "/planejamento/programacao-semanal/semana-anterior", permissionKey: "planning.weekly.previous.view", badgeAbaixo: EM_DESENVOLVIMENTO },
          { label: "Histograma do Projeto", icon: BarChart3, to: "/planejamento/programacao-semanal/histograma", permissionKey: "planning.weekly.histogram.view", badgeAbaixo: EM_DESENVOLVIMENTO },
        ],
      },
      // Antiga "Configuração LDP": sem página intermediária — cada etapa é um item próprio.
      { label: "Template LDP", icon: LayoutTemplate, to: "/configuracao-ldp/template", permissionKey: "settings.ldp.template.view" },
      { label: "Mapeamento do Cronograma", icon: GitBranch, to: "/configuracao-ldp/cronograma", permissionKey: "settings.ldp.cronograma.view" },
      { label: "Geração LDP Espelho", icon: FileSpreadsheet, to: "/configuracao-ldp/espelho", permissionKey: "settings.ldp.espelho.view" },
      { label: "Configuração LDP Controle", icon: ClipboardCheck, to: "/configuracao-ldp/controle", permissionKey: "settings.ldp.controle.view" },
    ],
  },
  { label: "Controle de Produção", icon: Factory, children: CONTROLE_PRODUCAO },
  {
    label: "Qualidade",
    icon: ShieldCheck,
    children: [
      { label: "Auditoria", icon: ShieldAlert, to: "/qualidade/auditoria", permissionKey: "quality.audit.view" },
      { label: "HH sem documento", icon: Timer, to: "/qualidade/hh-sem-documento", permissionKey: ["quality.unassigned.view", "quality.outside-ldp.view"] },
      // Mesmas keys de /api/information-quality-audit (progress-settings vive no mesmo controller).
      { label: "Configuração de Avanço Físico", icon: Gauge, to: "/qualidade/configuracao-avanco-fisico", permissionKey: "quality.progress.view" },
      { label: "Auditoria de Qualidade de Documentos", icon: ShieldCheck, to: "/qualidade/auditoria-documentos", permissionKey: "quality.documents.view" },
      { label: "Configuração de Pastas de Documentos", icon: FolderOpen, to: "/qualidade/configuracao-pastas-documentos", permissionKey: "quality.folders.view" },
      {
        label: "Auditoria LDP x ACC",
        icon: Scan,
        children: [
          { label: "Tem no ACC, não tem na LDP", icon: FolderOpen, to: "/qualidade/auditoria-ldp-acc?modo=acc-sem-ldp", permissionKey: "quality.ldp-acc.view" },
          { label: "Tem na LDP, não tem no ACC", icon: FileWarning, to: "/qualidade/auditoria-ldp-acc?modo=ldp-sem-acc", permissionKey: "quality.ldp-acc.view" },
        ],
      },
      { label: "Nomes das Disciplinas", icon: Languages, to: "/qualidade/auditoria-disciplinas", permissionKey: "quality.disciplines.view" },
    ],
  },
  {
    label: "CDE",
    icon: FolderOpen,
    children: [
      // Documentos = navegador REAL de pastas (Project Files) do projeto atual; o envio acontece dentro das pastas.
      // (A Lista de Documentos é tela do Controle de Produção; /cde e /cde/envio-documentos continuam válidas por compatibilidade.)
      { label: "Documentos", icon: FolderOpen, to: "/cde/documentos", permissionKey: "cde.docs.view" },
      { label: "Modelo 3D", icon: Box, to: "/modelo-3d", permissionKey: "cde.model3d.view", badgeAbaixo: EM_DESENVOLVIMENTO },
      { label: "Planejamento 4D", icon: CalendarClock, to: "/planejamento-4d", permissionKey: "cde.planning4d.view", badgeAbaixo: EM_DESENVOLVIMENTO },
      { label: "Orçamento 5D", icon: Calculator, to: "/orcamento-5d", permissionKey: "cde.budget5d.view", badgeAbaixo: EM_DESENVOLVIMENTO },
    ],
  },
  {
    label: "Relatórios",
    icon: BarChart3,
    children: [
      // Painel PMO (Reports.tsx → TabTimesheet): Gestão à Vista, Financeiro/Horas, Mapa de Calor, Curva S, 4D, Desvios.
      { label: "Painel Gerencial PMO", icon: MonitorPlay, to: "/relatorios", permissionKey: "reports.view" },
      { label: "Extrações de Engenharia", icon: Database, to: "/extracoes-engenharia", permissionKey: "reports.view", badgeAbaixo: { texto: "Em breve", tom: "demo" } },
    ],
  },
  {
    label: "Configurações",
    icon: Settings,
    children: [
      { label: "Gerenciador de Sidebar", icon: Settings, to: "/configuracoes/sidebar", permissionKey: "permissions.manage" },
      { label: "Permissões", icon: KeyRound, to: "/configuracoes/permissoes", permissionKey: "permissions.manage" },
      { label: "Configuração de Telas para TV", icon: MonitorPlay, to: "/configuracoes/telas-tv", permissionKey: "settings.tv.configure" },
      {
        label: "Configuração de Templates",
        icon: FileSpreadsheet,
        children: [
          ...TEMPLATE_TYPES.map((t) => ({ label: t.label, icon: FileText, to: rotaTemplate(t.tipo), permissionKey: t.permissionKey })),
          { label: "Auditoria Documental", icon: Scan, to: "/configuracoes/templates/auditoria-documental", permissionKey: "settings.ocr.view" },
        ],
      },
      { label: "Identidade Visual dos Projetos", icon: Palette, to: "/configuracoes/identidade-visual", permissionKey: "settings.projects.view" },
      { label: "Agendador do Robô", icon: Timer, to: "/agendamento", permissionKey: "robot.schedule.view" },
      { label: "Configurações do Robô", icon: Bot, to: "/automacao/configuracao-robo", permissionKey: "robot.view" },
    ],
  },
];


const controle: NavNode = {
        label: "Classificação de Profissionais",
        icon: Users,
        to: "/qualidade/classificacao-profissionais",
        permissionKey: "quality.professionals.view",
      };
const comparativo: NavNode = {
        label: "Comparativo de HH e Custos",
        icon: BarChart3,
        to: "/qualidade/comparativo-hh-custos",
        permissionKey: "quality.comparison.view",
      };
const divergencias: NavNode = {
        label: "Auditoria de Divergências",
        icon: Scale,
        to: "/qualidade/auditoria-divergencias",
        permissionKey: "quality.divergences.view",
      };

export const CONDITIONAL_NODES = [comparativo, divergencias, controle];
export const nodeId = (node: NavNode, parents: string[] = []) => node.to ? "page:" + node.to : "group:" + [...parents, node.label].join("/");
export function identify(nodes: NavNode[], parents: string[] = []): NavNode[] { return nodes.map(n => ({...n, id: nodeId(n, parents), ...(n.children ? {children: identify(n.children, [...parents,n.label])} : {})})); }

/** Preserves the pre-existing environment conditions before any persisted layout is applied. */
export function contextualNavigation(comparison:boolean,enforced:boolean,isMasterAdmin?:boolean):NavNode[] {
  const experiments=NAVEGACAO.map(node=>!comparison&&!enforced||node.label!=="Qualidade"?node:{...node,children:[...(node.children??[]).slice(0,2),...CONDITIONAL_NODES,...(node.children??[]).slice(2)]});
  return experiments.map(node=>node.label!=="Painel de Planejamento"||enforced||podeExibirCustosDuranteHomologacao(isMasterAdmin)?node:{...node,children:node.children?.filter(child=>child.label!=="Custos do Projeto")});
}

export function runtimeDefault(effective:{enforced:boolean;isMasterAdmin:boolean}|null,comparison:boolean):NavNode[]{return contextualNavigation(comparison,effective?.enforced??false,effective?.isMasterAdmin);}
