import { FileSpreadsheet, FileWarning, Download, ClipboardList } from "lucide-react";

const actions = [
  { label: "Gerar Lista de Materiais (BOM)", icon: ClipboardList },
  { label: "Relatório de Inconsistências (De/Para)", icon: FileWarning },
  { label: "Exportar Instrumentos (.xlsx)", icon: FileSpreadsheet },
  { label: "Exportar Equipamentos (.xlsx)", icon: Download },
];

const QuickActions = () => {
  return (
    <section className="glass-panel p-5">
      <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
        Extração e Relatórios
      </h2>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {actions.map(({ label, icon: Icon }) => (
          <button
            key={label}
            className="flex items-center gap-3 rounded-lg border border-border bg-secondary/50 px-4 py-3 text-sm font-medium text-foreground transition-all duration-200 hover:bg-primary/10 hover:border-primary/50 hover:text-primary"
          >
            <Icon className="h-4 w-4 shrink-0" />
            {label}
          </button>
        ))}
      </div>
    </section>
  );
};

export default QuickActions;
