// Tipos de TEMPLATE EXCEL configuráveis (Configurações → Configuração de Templates). Um único editor parametrizado
// (TemplateConfigPage) atende todos os tipos — nada de nove motores. Dados puros (sem React): usados pelo sidebar,
// pelas rotas, pelo guard de URL e pelos testes de contrato.
// moduloMotor = módulo já suportado pelo motor existente (components/AdminTemplates.tsx); null = tipo ainda sem
// campos definidos no motor (a página mostra o estado honestamente, sem inventar funcionalidade).
export type ModuloMotorTemplate = "desvios" | "holds" | "ncs";

export interface TemplateType {
  tipo: string;
  label: string;
  permissionKey: string;
  moduloMotor: ModuloMotorTemplate | null;
}

export const TEMPLATE_TYPES: readonly TemplateType[] = [
  { tipo: "rnc", label: "RNC", permissionKey: "settings.templates.rnc.edit", moduloMotor: "ncs" },
  { tipo: "desvio-de-escopo", label: "Desvio de Escopo", permissionKey: "settings.templates.desvio-escopo.edit", moduloMotor: null },
  { tipo: "controle-de-hold", label: "Controle de HOLD", permissionKey: "settings.templates.hold.edit", moduloMotor: "holds" },
  { tipo: "cap-interno", label: "CAP Interno", permissionKey: "settings.templates.cap-interno.edit", moduloMotor: null },
  { tipo: "cap-externo", label: "CAP Externo", permissionKey: "settings.templates.cap-externo.edit", moduloMotor: null },
  { tipo: "controle-de-desvio", label: "Controle de Desvio", permissionKey: "settings.templates.desvio.edit", moduloMotor: "desvios" },
  { tipo: "ordem-de-servico", label: "Ordem de Serviço", permissionKey: "settings.templates.ordem-servico.edit", moduloMotor: null },
  { tipo: "controle-de-contratos", label: "Controle de Contratos", permissionKey: "settings.templates.contratos.edit", moduloMotor: null },
  { tipo: "controle-de-custos", label: "Controle de Custos", permissionKey: "settings.templates.custos.edit", moduloMotor: null },
];

export const rotaTemplate = (tipo: string) => `/configuracoes/templates/${tipo}`;
export const encontrarTemplate = (tipo: string | undefined) => TEMPLATE_TYPES.find((t) => t.tipo === tipo);
