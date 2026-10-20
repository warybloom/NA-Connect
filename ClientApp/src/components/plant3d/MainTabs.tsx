import { useState, useEffect } from "react";
import { useSearchParams } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { rotaPermitida } from "@/features/permissions/permissionLogic";
import { AcessoNegado } from "@/features/permissions/PermissionRouteGuard";
import { Activity, BarChart3, Database, PackageSearch, UploadCloud, SplitSquareHorizontal, CalendarClock, Scale } from "lucide-react";

// Importação das abas
import TabDashboard from "./TabDashboard"; 
import TabSettings from "./TabSettings";   
import TabBOM from "./TabBOM";             
import TabDatabaseUpload from "./TabDatabaseUpload"; 
import TabTagRules from "./TabTagRules";
import Tab4D from "./Tab4D"; 
import TabRegrasMedicao from "./TabRegrasMedicao"; 
import RelatoriosTab from "./RelatoriosTab"; 

const tabs = [
  { id: "4d-schedule", label: "Avanço e Cronograma (4D)", icon: CalendarClock },
  { id: "rules", label: "Critérios de Medição", icon: Scale },
  { id: "dashboard", label: "Tela de Resultados", icon: BarChart3 },
  { id: "reports", label: "Relatórios Consolidados", icon: PackageSearch },
  { id: "database", label: "Banco de Dados", icon: UploadCloud }, 
  { id: "mapping", label: "Mapeamento DE/PARA", icon: Database },
  { id: "tag-rules", label: "Regras de Nomenclatura", icon: SplitSquareHorizontal },
  { id: "gen-reports", label: "Gestão à Vista (GEN)", icon: Activity },
] as const;

type TabId = (typeof tabs)[number]["id"];

// Sidebar fase 1: Gestão à Vista sai da navegação por enquanto — o componente (RelatoriosTab) e o
// render abaixo continuam intactos, só não há botão para ela.
const TABS_OCULTAS: readonly TabId[] = ["gen-reports"];

const MainTabs = () => {
  // Abas filtradas pela permissão de cada uma (mapa único: ROUTE_PERMISSIONS["/plant3d"].tab).
  const { can } = usePermissions();
  const tabsVisiveis = tabs.filter((t) => !TABS_OCULTAS.includes(t.id) && rotaPermitida("/plant3d", `?tab=${t.id}`, can));
  const tabValida = (valor: string | null): valor is TabId =>
    valor != null && tabsVisiveis.some((t) => t.id === valor);

  // ?tab=<id> (links do sidebar) tem prioridade sobre a última aba salva no localStorage.
  const [searchParams, setSearchParams] = useSearchParams();
  const tabDaUrl = searchParams.get("tab");

  const [active, setActive] = useState<TabId>(() => {
    if (tabValida(tabDaUrl)) return tabDaUrl;
    const savedTab = localStorage.getItem("plant3d_active_tab");
    if (savedTab === "live") return "4d-schedule";
    return tabValida(savedTab) ? savedTab : (tabsVisiveis.find((t) => t.id === "dashboard") ?? tabsVisiveis[0])?.id ?? "dashboard";
  });

  // Clique no sidebar com a página já aberta: a URL muda sem remontar o componente.
  useEffect(() => {
    if (tabValida(tabDaUrl) && tabDaUrl !== active) setActive(tabDaUrl);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabDaUrl]);

  useEffect(() => {
    localStorage.setItem("plant3d_active_tab", active);
    // Mantém a URL coerente com a aba (o sidebar destaca o item pela ?tab=).
    if (searchParams.get("tab") !== active) {
      const proximos = new URLSearchParams(searchParams);
      proximos.set("tab", active);
      setSearchParams(proximos, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active]);

  return (
    // 🎯 A MÁGICA ACONTECE AQUI: w-full h-full para ocupar 100% da tela livre!
    <div className="w-full h-full px-4 md:px-6">
      
      <div className="mb-6 flex gap-1 overflow-x-auto rounded-xl border border-border bg-card/60 p-1 backdrop-blur-sm custom-scrollbar">
        {tabsVisiveis.map((tab) => {
          const Icon = tab.icon;
          const isActive = active === tab.id;
          return (
            <button
              key={tab.id}
              onClick={() => setActive(tab.id)}
              className={`relative flex items-center gap-2 whitespace-nowrap rounded-lg px-4 py-2.5 text-sm font-medium transition-colors duration-200 ${
                isActive ? "text-primary-foreground" : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {isActive && (
                <motion.div
                  layoutId="activeTab"
                  className="absolute inset-0 rounded-lg bg-primary"
                  transition={{ type: "spring", stiffness: 400, damping: 30 }}
                />
              )}
              <span className="relative z-10 flex items-center gap-2">
                <Icon className="h-4 w-4" />
                {tab.label}
              </span>
            </button>
          );
        })}
      </div>

      {!tabsVisiveis.some((t) => t.id === active) ? <AcessoNegado detalhe="Você não tem permissão para esta aba do Plant 3D." /> :
      <AnimatePresence mode="wait">
        <motion.div
          key={active}
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -12 }}
          transition={{ duration: 0.25 }}
        >
          {active === "4d-schedule" && <Tab4D />}
          {active === "rules" && <TabRegrasMedicao />} 
          {active === "dashboard" && <TabDashboard />}
          {active === "reports" && <TabBOM />}
          {active === "database" && <TabDatabaseUpload />}
          {active === "mapping" && <TabSettings />}
          {active === "tag-rules" && <TabTagRules />} 
          {active === "gen-reports" && <RelatoriosTab />}
        </motion.div>
      </AnimatePresence>}
    </div>
  );
};

export default MainTabs;