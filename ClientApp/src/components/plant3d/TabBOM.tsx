import { motion } from "framer-motion";
import {
  ClipboardList,
  FileSpreadsheet,
  Download,
  Gauge,
  Anchor,
} from "lucide-react";
import { bomItems } from "@/data/auditMockData";

const exportButtons = [
  { label: "Exportar BOM Completa (Lista de Materiais)", icon: ClipboardList },
  { label: "Relatório de Válvulas", icon: Download },
  { label: "Relatório de Instrumentos", icon: Gauge },
  { label: "Exportar Tie-Ins", icon: Anchor },
];

const TabBOM = () => {
  return (
    <div className="space-y-6">
      {/* Export Buttons Grid */}
      <motion.section
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
        className="glass-panel p-5"
      >
        <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
          Extração de Relatórios e BOM
        </h2>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {exportButtons.map(({ label, icon: Icon }, idx) => (
            <motion.button
              key={label}
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: idx * 0.06, duration: 0.35 }}
              className="group flex items-center gap-3 rounded-lg border border-border bg-secondary/40 px-4 py-4 text-sm font-medium text-foreground transition-all duration-200 hover:bg-primary/10 hover:border-primary/50 hover:text-primary active:scale-[0.98]"
            >
              <div className="rounded-md bg-primary/10 p-2 transition-colors group-hover:bg-primary/20">
                <Icon className="h-5 w-5 text-primary" />
              </div>
              <span className="text-left">{label}</span>
              <FileSpreadsheet className="ml-auto h-4 w-4 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100" />
            </motion.button>
          ))}
        </div>
      </motion.section>

      {/* BOM Preview Table */}
      <motion.section
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.25, duration: 0.4 }}
        className="glass-panel overflow-hidden"
      >
        <div className="p-5">
          <h2 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
            Pré-visualização — Lista de Materiais (BOM do 3D)
          </h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-t border-border bg-secondary/30">
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Tag</th>
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Descrição</th>
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Qtd</th>
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Und</th>
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Especificação</th>
                <th className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wider text-muted-foreground">Origem</th>
              </tr>
            </thead>
            <tbody>
              {bomItems.map((item, idx) => (
                <motion.tr
                  key={item.id}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ delay: idx * 0.04 }}
                  className="border-t border-border/50 transition-colors duration-150 hover:bg-primary/5 even:bg-secondary/10"
                >
                  <td className="px-5 py-3 font-mono font-medium text-foreground">{item.tag}</td>
                  <td className="px-5 py-3 text-muted-foreground">{item.descricao}</td>
                  <td className="px-5 py-3 font-mono text-foreground">{item.quantidade}</td>
                  <td className="px-5 py-3 text-muted-foreground">{item.unidade}</td>
                  <td className="px-5 py-3 font-mono text-xs text-muted-foreground">{item.spec}</td>
                  <td className="px-5 py-3 font-mono text-xs text-muted-foreground">{item.origem}</td>
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>
      </motion.section>
    </div>
  );
};

export default TabBOM;
