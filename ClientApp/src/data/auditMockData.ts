export interface AuditItem {
  id: string;
  tag: string;
  categoria: "Equipamento" | "Instrumento" | "Válvula" | "Linha" | "Tie-In";
  desenhoPID: string;
  desenho3D: string;
  specPID: string;
  spec3D: string;
  statusCruzamento: "Match Perfeito" | "Falta Modelar no 3D" | "Não existe no P&ID" | "Divergência de Spec";
}

export const auditItems: AuditItem[] = [
  { id: "1", tag: "V-101", categoria: "Equipamento", desenhoPID: "PID-100-001", desenho3D: "3D-MODEL-100", specPID: "DN 1200 x 4000mm", spec3D: "DN 1200 x 4000mm", statusCruzamento: "Match Perfeito" },
  { id: "2", tag: "P-201A", categoria: "Equipamento", desenhoPID: "PID-200-001", desenho3D: "3D-MODEL-200", specPID: "50 HP / 3560 RPM", spec3D: "50 HP / 3560 RPM", statusCruzamento: "Match Perfeito" },
  { id: "3", tag: "E-301", categoria: "Equipamento", desenhoPID: "PID-300-002", desenho3D: "—", specPID: "Shell & Tube 500m²", spec3D: "—", statusCruzamento: "Falta Modelar no 3D" },
  { id: "4", tag: "FIT-001", categoria: "Instrumento", desenhoPID: "PID-100-001", desenho3D: "3D-MODEL-100", specPID: '2" 150# RF', spec3D: '2" 150# RF', statusCruzamento: "Match Perfeito" },
  { id: "5", tag: "TIT-045", categoria: "Instrumento", desenhoPID: "PID-200-003", desenho3D: "—", specPID: '1" 300# RF', spec3D: "—", statusCruzamento: "Falta Modelar no 3D" },
  { id: "6", tag: "LIT-033", categoria: "Instrumento", desenhoPID: "PID-300-001", desenho3D: "3D-MODEL-300", specPID: '2" 150# RF', spec3D: '2" 300# RF', statusCruzamento: "Divergência de Spec" },
  { id: "7", tag: "XV-001", categoria: "Válvula", desenhoPID: "PID-100-001", desenho3D: "3D-MODEL-100", specPID: '4" 150# RF', spec3D: '4" 150# RF', statusCruzamento: "Match Perfeito" },
  { id: "8", tag: "BV-045", categoria: "Válvula", desenhoPID: "PID-200-001", desenho3D: "—", specPID: '2" 300# RF', spec3D: "—", statusCruzamento: "Falta Modelar no 3D" },
  { id: "9", tag: "FCV-007", categoria: "Válvula", desenhoPID: "PID-100-001", desenho3D: "3D-MODEL-100", specPID: '2" 150# RF', spec3D: '2" 300# RF', statusCruzamento: "Divergência de Spec" },
  { id: "10", tag: '2"-P-001-A1', categoria: "Linha", desenhoPID: "PID-100-001", desenho3D: "ISO-100-001", specPID: '2" Sch 40 CS', spec3D: '2" Sch 40 CS', statusCruzamento: "Match Perfeito" },
  { id: "11", tag: '4"-P-015-A2', categoria: "Linha", desenhoPID: "PID-200-003", desenho3D: "—", specPID: '4" Sch 80 SS316', spec3D: "—", statusCruzamento: "Falta Modelar no 3D" },
  { id: "12", tag: "EQ-GHOST-01", categoria: "Equipamento", desenhoPID: "—", desenho3D: "3D-MODEL-500", specPID: "—", spec3D: "DN 800 x 3000mm", statusCruzamento: "Não existe no P&ID" },
  { id: "13", tag: "TI-001-A", categoria: "Tie-In", desenhoPID: "PID-100-001", desenho3D: "3D-MODEL-100", specPID: '4" 150# RF', spec3D: '4" 150# RF', statusCruzamento: "Match Perfeito" },
  { id: "14", tag: "TI-002-B", categoria: "Tie-In", desenhoPID: "PID-200-005", desenho3D: "—", specPID: '6" 300# RF', spec3D: "—", statusCruzamento: "Falta Modelar no 3D" },
  { id: "15", tag: "TI-003-C", categoria: "Tie-In", desenhoPID: "PID-300-001", desenho3D: "3D-MODEL-300", specPID: '3" 150# RF', spec3D: '3" 300# RF', statusCruzamento: "Divergência de Spec" },
];

export interface CategorySummary {
  categoria: string;
  totalPID: number;
  total3D: number;
  faltamNo3D: number;
  faltamNoPID: number;
}

export const categorySummaries: CategorySummary[] = [
  { categoria: "Equipamentos", totalPID: 320, total3D: 305, faltamNo3D: 15, faltamNoPID: 0 },
  { categoria: "Instrumentos", totalPID: 480, total3D: 410, faltamNo3D: 70, faltamNoPID: 2 },
  { categoria: "Válvulas", totalPID: 215, total3D: 198, faltamNo3D: 17, faltamNoPID: 1 },
  { categoria: "Linhas", totalPID: 185, total3D: 152, faltamNo3D: 33, faltamNoPID: 0 },
  { categoria: "Tie-Ins", totalPID: 50, total3D: 35, faltamNo3D: 15, faltamNoPID: 2 },
];

export const crossSummary = {
  totalPID: 1250,
  total3D: 1100,
  faltamNo3D: 150,
  faltamNoPID: 5,
};

export interface BOMItem {
  id: string;
  tag: string;
  descricao: string;
  quantidade: number;
  unidade: string;
  spec: string;
  origem: string;
}

export const bomItems: BOMItem[] = [
  { id: "1", tag: "V-101", descricao: "Vaso de Pressão Vertical", quantidade: 1, unidade: "pç", spec: "DN 1200 x 4000mm ASME VIII", origem: "3D-MODEL-100" },
  { id: "2", tag: "P-201A/B", descricao: "Bomba Centrífuga (2x)", quantidade: 2, unidade: "pç", spec: "50 HP / API 610", origem: "3D-MODEL-200" },
  { id: "3", tag: "E-301", descricao: "Trocador de Calor Casco-Tubo", quantidade: 1, unidade: "pç", spec: "500m² / TEMA BEM", origem: "3D-MODEL-300" },
  { id: "4", tag: 'Tubulação 2" CS', descricao: "Tubo Aço Carbono Sch 40", quantidade: 340, unidade: "m", spec: 'ASTM A106 Gr.B 2"', origem: "ISO-100" },
  { id: "5", tag: 'Tubulação 4" SS', descricao: "Tubo Inox 316 Sch 80", quantidade: 120, unidade: "m", spec: 'ASTM A312 TP316 4"', origem: "ISO-200" },
  { id: "6", tag: "Flanges 150#", descricao: "Flange WN RF 150#", quantidade: 86, unidade: "pç", spec: "ASME B16.5", origem: "ISO-MTO" },
];
