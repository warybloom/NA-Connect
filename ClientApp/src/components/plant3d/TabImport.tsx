import { useState, useCallback } from "react";
import { motion } from "framer-motion";
import {
  Upload,
  FileUp,
  Info,
  CheckCircle2,
  Rocket,
} from "lucide-react";

const importTargets = [
  "Atualizar Dados de Linhas",
  "Atualizar Válvulas",
  "Atualizar Equipamentos",
] as const;

const TabImport = () => {
  const [selectedTarget, setSelectedTarget] = useState<string>(importTargets[0]);
  const [isDragOver, setIsDragOver] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback(() => setIsDragOver(false), []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) setFileName(file.name);
  }, []);

  const handleFileInput = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) setFileName(file.name);
  }, []);

  return (
    <div className="space-y-6">
      {/* Info Banner */}
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.35 }}
        className="flex items-start gap-3 rounded-lg border border-primary/30 bg-primary/5 p-4"
      >
        <Info className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
        <div>
          <p className="text-sm font-medium text-foreground">Área de Importação de Dados</p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Utilize esta área para carregar planilhas Excel e atualizar atributos de Linhas,
            Equipamentos e Válvulas diretamente no banco de dados do projeto via ACC (Autodesk
            Construction Cloud).
          </p>
        </div>
      </motion.div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Drag & Drop Zone */}
        <motion.div
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1, duration: 0.4 }}
          className="lg:col-span-2"
        >
          <div
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            className={`group relative flex min-h-[240px] cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed transition-all duration-300 ${
              isDragOver
                ? "border-primary bg-primary/10 scale-[1.01]"
                : fileName
                ? "border-success/50 bg-success/5"
                : "border-border bg-card hover:border-primary/40 hover:bg-primary/5"
            }`}
          >
            <input 
              type="file" 
              title="Anexar arquivo Excel" // Adicione isso
              placeholder="Selecione o arquivo" // Adicione isso por garantia
              onChange={handleFileInput}
              className="absolute inset-0 cursor-pointer opacity-0"
            />
            {fileName ? (
              <>
                <CheckCircle2 className="mb-3 h-10 w-10 text-success" />
                <p className="text-sm font-medium text-foreground">{fileName}</p>
                <p className="mt-1 text-xs text-muted-foreground">Arquivo carregado com sucesso</p>
              </>
            ) : (
              <>
                <FileUp
                  className={`mb-3 h-10 w-10 transition-colors ${
                    isDragOver ? "text-primary" : "text-muted-foreground"
                  }`}
                />
                <p className="text-sm font-medium text-foreground">
                  Arraste e solte sua planilha aqui
                </p>
                <p className="mt-1 text-xs text-muted-foreground">
                  ou clique para selecionar — .xlsx, .xls, .csv
                </p>
              </>
            )}
          </div>
        </motion.div>

        {/* Target Selector + Actions */}
        <motion.div
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2, duration: 0.4 }}
          className="glass-panel flex flex-col gap-4 p-5"
        >
          <h3 className="text-sm font-semibold uppercase tracking-wider text-muted-foreground">
            Alvo da Importação
          </h3>
          <div className="flex flex-col gap-2">
            {importTargets.map((target) => (
              <button
                key={target}
                onClick={() => setSelectedTarget(target)}
                className={`rounded-lg border px-4 py-2.5 text-left text-sm font-medium transition-all ${
                  selectedTarget === target
                    ? "border-primary bg-primary/10 text-primary"
                    : "border-border bg-secondary/30 text-muted-foreground hover:text-foreground hover:border-border"
                }`}
              >
                {target}
              </button>
            ))}
          </div>

          <div className="mt-auto flex flex-col gap-2 pt-4">
            <button
              disabled={!fileName}
              className="flex items-center justify-center gap-2 rounded-lg border border-border bg-secondary/50 px-4 py-2.5 text-sm font-medium text-foreground transition-all hover:bg-secondary disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <CheckCircle2 className="h-4 w-4" />
              Validar Dados
            </button>
            <button
              disabled={!fileName}
              className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition-all hover:brightness-110 glow-blue disabled:opacity-40 disabled:cursor-not-allowed active:scale-[0.97]"
            >
              <Rocket className="h-4 w-4" />
              Injetar no Plant 3D (ACC)
            </button>
          </div>
        </motion.div>
      </div>
    </div>
  );
};

export default TabImport;
