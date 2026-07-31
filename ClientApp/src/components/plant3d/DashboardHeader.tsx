import { useState } from "react";
import { RefreshCw, GitCompareArrows } from "lucide-react";
import { motion } from "framer-motion";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/Select";

const DashboardHeader = () => {
  const [selectedHub, setSelectedHub] = useState<string>("");

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

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <Select value={selectedHub} onValueChange={setSelectedHub}>
            <SelectTrigger className="w-full bg-card border-border text-foreground sm:w-[180px]">
              <SelectValue placeholder="Selecionar Hub" />
            </SelectTrigger>
            <SelectContent className="bg-card border-border">
              <SelectItem value="nea">N&A Consultores</SelectItem>
              <SelectItem value="cliente-x">Cliente X</SelectItem>
              <SelectItem value="cliente-y">Cliente Y</SelectItem>
            </SelectContent>
          </Select>

          <Select disabled={!selectedHub}>
            <SelectTrigger className="w-full bg-card border-border text-foreground sm:w-[180px]">
              <SelectValue placeholder="Selecionar Projeto" />
            </SelectTrigger>
            <SelectContent className="bg-card border-border">
              <SelectItem value="proj-alpha">Projeto Alpha</SelectItem>
              <SelectItem value="proj-beta">Projeto Beta</SelectItem>
              <SelectItem value="proj-gamma">Projeto Gamma</SelectItem>
            </SelectContent>
          </Select>

          <button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground transition-all duration-200 hover:brightness-110 hover:shadow-lg glow-blue active:scale-[0.97]">
            <RefreshCw className="h-4 w-4" />
            Rodar Auditoria ACC
          </button>
        </div>
      </div>
    </motion.header>
  );
};

export default DashboardHeader;