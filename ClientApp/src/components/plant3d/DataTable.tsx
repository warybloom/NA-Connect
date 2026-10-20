import { useState, useMemo } from "react";
import { Search } from "lucide-react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import { mockAllAssets, type PlantAsset } from "@/data/mockPlantData";

const categories = ["Todos", "Equipamento", "Instrumento", "Válvula", "Linha", "Tie-In"] as const;

const statusStyles: Record<PlantAsset["status"], string> = {
  "Verificado no 3D": "bg-success/15 text-success border-success/30",
  "Falta no 3D": "bg-destructive/15 text-destructive border-destructive/30",
  "Divergência de Spec": "bg-yellow-500/15 text-yellow-400 border-yellow-500/30",
};

const DataTable = () => {
  const [activeTab, setActiveTab] = useState("Todos");
  const [search, setSearch] = useState("");

  const currentData = useMemo(() => {
    let data = [...mockAllAssets];
    if (activeTab !== "Todos") data = data.filter((i) => i.categoria === activeTab);
    if (search) {
      const q = search.toLowerCase();
      data = data.filter(
        (item) =>
          item.tag.toLowerCase().includes(q) ||
          item.tipo.toLowerCase().includes(q) ||
          item.subarea.toLowerCase().includes(q)
      );
    }
    return data;
  }, [activeTab, search]);

  return (
    <section className="glass-panel overflow-hidden">
      <div className="flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between">
        <Tabs value={activeTab} onValueChange={setActiveTab}>
          <TabsList className="bg-secondary/50 border border-border">
            {categories.map((cat) => (
              <TabsTrigger
                key={cat}
                value={cat}
                className="text-xs data-[state=active]:bg-primary data-[state=active]:text-primary-foreground"
              >
                {cat}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
        <div className="relative">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            placeholder="Pesquisar por tag, tipo..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-lg border border-border bg-secondary/50 py-2 pl-10 pr-4 text-sm text-foreground placeholder:text-muted-foreground focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary sm:w-[260px]"
          />
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-t border-border bg-secondary/30">
              <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Tag</th>
              <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Tipo</th>
              <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Subárea</th>
              <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">P&ID Origem</th>
              <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Status</th>
            </tr>
          </thead>
          <tbody>
            {currentData.map((item) => (
              <tr
                key={item.id}
                className="border-t border-border/50 transition-colors duration-150 hover:bg-primary/5 even:bg-secondary/10"
              >
                <td className="px-5 py-3 font-mono font-medium text-foreground">{item.tag}</td>
                <td className="px-5 py-3 text-muted-foreground">{item.tipo}</td>
                <td className="px-5 py-3 text-muted-foreground">{item.subarea}</td>
                <td className="px-5 py-3 font-mono text-xs text-muted-foreground">{item.pidOrigem}</td>
                <td className="px-5 py-3">
                  <Badge variant="outline" className={statusStyles[item.status]}>
                    {item.status}
                  </Badge>
                </td>
              </tr>
            ))}
            {currentData.length === 0 && (
              <tr>
                <td colSpan={5} className="px-5 py-10 text-center text-muted-foreground">
                  Nenhum resultado encontrado.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
};

export default DataTable;
