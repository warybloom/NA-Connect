export interface PlantAsset {
  id: string;
  tag: string;
  categoria: string;
  tipo: string;
  spec: string;
  subarea: string;
  pidOrigem: string;
  status: "Verificado no 3D" | "Falta no 3D" | "Divergência de Spec";
}

export const mockAllAssets: PlantAsset[] = [
  { id: "1", tag: "FIT-001", categoria: "Instrumento", tipo: "Transmissor de Vazão", spec: '2" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Verificado no 3D" },
  { id: "2", tag: "TIT-045", categoria: "Instrumento", tipo: "Transmissor de Temperatura", spec: '1" 300# RF', subarea: "Área 200", pidOrigem: "PID-200-003.dwg", status: "Falta no 3D" },
  { id: "3", tag: "PIT-012", categoria: "Instrumento", tipo: "Transmissor de Pressão", spec: '1" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-002.dwg", status: "Verificado no 3D" },
  { id: "4", tag: "LIT-033", categoria: "Instrumento", tipo: "Transmissor de Nível", spec: '2" 150# RF', subarea: "Área 300", pidOrigem: "PID-300-001.dwg", status: "Divergência de Spec" },
  { id: "5", tag: "V-101", categoria: "Equipamento", tipo: "Vaso de Pressão", spec: "DN 1200 x 4000mm", subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Verificado no 3D" },
  { id: "6", tag: "P-201A", categoria: "Equipamento", tipo: "Bomba Centrífuga", spec: "50 HP / 3560 RPM", subarea: "Área 200", pidOrigem: "PID-200-001.dwg", status: "Verificado no 3D" },
  { id: "7", tag: "E-301", categoria: "Equipamento", tipo: "Trocador de Calor", spec: "Shell & Tube 500m²", subarea: "Área 300", pidOrigem: "PID-300-002.dwg", status: "Falta no 3D" },
  { id: "8", tag: "T-102", categoria: "Equipamento", tipo: "Torre de Destilação", spec: "DN 2000 x 25000mm", subarea: "Área 100", pidOrigem: "PID-100-003.dwg", status: "Verificado no 3D" },
  { id: "9", tag: "XV-001", categoria: "Válvula", tipo: "Válvula Gaveta", spec: '4" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Verificado no 3D" },
  { id: "10", tag: "BV-045", categoria: "Válvula", tipo: "Válvula Esfera", spec: '2" 300# RF', subarea: "Área 200", pidOrigem: "PID-200-001.dwg", status: "Falta no 3D" },
  { id: "11", tag: "CV-012", categoria: "Válvula", tipo: "Válvula Retenção", spec: '3" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-003.dwg", status: "Verificado no 3D" },
  { id: "12", tag: "FCV-007", categoria: "Válvula", tipo: "Válvula de Controle", spec: '2" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Divergência de Spec" },
  { id: "13", tag: '2"-P-001-A1-N', categoria: "Linha", tipo: "Processo", spec: '2" Sch 40 CS', subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Verificado no 3D" },
  { id: "14", tag: '4"-P-015-A2-N', categoria: "Linha", tipo: "Processo", spec: '4" Sch 80 SS316', subarea: "Área 200", pidOrigem: "PID-200-003.dwg", status: "Falta no 3D" },
  { id: "15", tag: '6"-CW-022-A1-N', categoria: "Linha", tipo: "Água de Resfriamento", spec: '6" Sch 40 CS', subarea: "Área 100", pidOrigem: "PID-100-010.dwg", status: "Verificado no 3D" },
  { id: "16", tag: "TI-001-A", categoria: "Tie-In", tipo: "Tie-In Existente", spec: '4" 150# RF', subarea: "Área 100", pidOrigem: "PID-100-001.dwg", status: "Verificado no 3D" },
  { id: "17", tag: "TI-002-B", categoria: "Tie-In", tipo: "Tie-In Novo", spec: '6" 300# RF', subarea: "Área 200", pidOrigem: "PID-200-005.dwg", status: "Falta no 3D" },
  { id: "18", tag: "TI-003-C", categoria: "Tie-In", tipo: "Tie-In Existente", spec: '3" 150# RF', subarea: "Área 300", pidOrigem: "PID-300-001.dwg", status: "Divergência de Spec" },
];

export const summaryData = {
  equipamentos: 1284,
  instrumentos: 3472,
  valvulas: 876,
  linhas: 2156,
  tieIns: 134,
  inconsistencias: 47,
};
