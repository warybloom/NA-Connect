// Configurações › Critérios de Medição e Fases: fases ilimitadas (nome livre, ordem, contagens previstas por categoria — reuso de
// PlantPhasePage) + pesos de linha por projeto (GET/PUT /line-weights, soma 100%). Nada daqui afirma consistência P&ID × 3D.
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import PlantPhasePage from "./PlantPhasePage";
import PlantLineWeightsSection from "./PlantLineWeightsSection";

export default function PlantCriteriosPage() {
  const { projectIdAcc } = usePlantProjects();
  return (
    <div className="space-y-8">
      <PlantPhasePage titulo="Critérios de Medição e Fases"
        subtitulo="Crie, renomeie, ordene e exclua fases e digite as contagens previstas por categoria (linhas, equipamentos, instrumentos, válvulas, tie-ins)." />
      {projectIdAcc
        ? <PlantLineWeightsSection projectIdAcc={projectIdAcc} />
        : <p className="text-sm text-muted-foreground">Pesos de linha: defina o projeto atual na Home para editar os pesos (eles são por projeto).</p>}
    </div>
  );
}
