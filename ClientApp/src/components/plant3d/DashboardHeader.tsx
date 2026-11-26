import { RefreshCw, GitCompareArrows } from "lucide-react";
import { motion } from "framer-motion";

const DashboardHeader = () => {
  return (
    <motion.header
      initial={{ opacity: 0, y: -20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.4 }}
      className="sticky top-0 z-30 border-b border-border bg-background/80 backdrop-blur-xl px-4 py-4 md:px-6"
    >
      <div className="mx-auto flex max-w-[1600px] flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-center gap-3">
          <div className="rounded-lg bg-primary/10 p-2.5 ring-1 ring-primary/20">
            <GitCompareArrows className="h-6 w-6 text-primary" />
          </div>
          <div>
            <h1 className="text-xl font-bold tracking-tight text-foreground md:text-2xl">
              Cruzamento de Dados: P&ID vs Modelo 3D
            </h1>
            <p className="text-xs text-muted-foreground">
              Auditoria Plant 3D — N&A Connect
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-xs text-muted-foreground">
          <RefreshCw className="h-3.5 w-3.5" />
          Sincronização automática pelo Dashboard BIM
        </div>
      </div>
    </motion.header>
  );
};

export default DashboardHeader;
