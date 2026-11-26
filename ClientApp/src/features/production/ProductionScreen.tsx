import { ReportState } from "@/components/reports/ReportComponents";
import type { PmoSelecionado } from "@/components/pmo/pmoMultiCore";
import { getProductionScreen } from "./productionScreens";

/**
 * Renderiza UMA tela do Relatório de Produção de forma isolada — sem depender da barra de navegação, do
 * seletor ou do cabeçalho do Relatório de Produção. É o ponto de reuso da futura Gestão à Vista/TV:
 *   <ProductionScreen screenId="hold" selection={projetos} />
 * `key` por projeto: trocar a seleção remonta a tela (filtros/página zerados, nenhum dado anterior visível).
 */
export default function ProductionScreen({ screenId, selection }: { screenId: string; selection: PmoSelecionado[] }) {
  const tela = getProductionScreen(screenId);
  if (!tela) return <ReportState title="Tela desconhecida" error>O identificador "{screenId}" não existe no registro de telas.</ReportState>;
  if (selection.length === 0) return <ReportState title="Nenhum projeto selecionado">Selecione um projeto para visualizar esta tela.</ReportState>;
  const Componente = tela.component;
  const chaveSelecao = selection.map(p => `${p.hubId}:${p.id}`).join("|");
  return <Componente key={`${tela.id}|${chaveSelecao}`} selection={selection} />;
}
