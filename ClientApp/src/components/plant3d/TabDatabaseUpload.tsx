import { useState, useRef } from "react";
import { Box, GitBranch, UploadCloud, Loader2, Save, FolderKanban } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "react-hot-toast";
import { useParams } from 'react-router-dom';

export default function TabDatabaseUpload() {
  const [pipingFile, setPipingFile] = useState<File | null>(null);
  const [pidFile, setPidFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [projectName, setProjectName] = useState(""); 
  
  const { projectId } = useParams();

  const pipingRef = useRef<HTMLInputElement>(null);
  const pidRef = useRef<HTMLInputElement>(null);

  const handleUpload = async () => {
    // Validação: NOME do projeto é obrigatório
    if (!projectName.trim()) {
      toast.error("Por favor, digite um nome para identificar este projeto.");
      return;
    }

    if (!pipingFile || !pidFile) {
      toast.error("Selecione os dois arquivos (.dcf) para continuar.");
      return;
    }

    setIsUploading(true);
    const formData = new FormData();
    
    // 🎯 A MÁGICA ESTÁ AQUI: Se não tem ID na URL, cria um ID único (Ex: PROJ-1715893201)
    const finalProjectId = projectId || `PROJ-${Date.now()}`;

    formData.append("pipingFile", pipingFile);
    formData.append("pidFile", pidFile);
    formData.append("projectName", projectName); 
    formData.append("projectId", finalProjectId); // 👈 Usa o ID dinâmico
    formData.append("configJson", "[]"); 

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/run-audit", {
        method: "POST",
        body: formData,
      });

      if (response.ok) {
        toast.success(`Projeto "${projectName}" salvo com sucesso!`);
        // Opcional: Limpar os campos após salvar um projeto novo
        if (!projectId) {
          setProjectName("");
          setPipingFile(null);
          setPidFile(null);
        }
      } else {
        const errorText = await response.text();
        console.error("Erro do servidor:", errorText);
        toast.error("Erro ao atualizar bancos de dados.");
      }
    } catch (error) {
      toast.error("Erro de conexão com o servidor.");
    } finally {
      setIsUploading(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="glass-card p-6 rounded-xl border border-border/50">
        <div className="mb-6">
          <h2 className="text-xl font-bold flex items-center gap-2 text-primary">
            <UploadCloud className="w-6 h-6" /> Gestão de Projetos Plant 3D
          </h2>
          <p className="text-sm text-muted-foreground mt-1">
            Identifique o projeto e suba os bancos de dados para atualizar a auditoria.
          </p>
        </div>

        <div className="grid grid-cols-1 gap-6 max-w-2xl">
          {/* CAMPO DO NOME DO PROJETO */}
          <div className="space-y-2">
            <label htmlFor="projName" className="text-sm font-bold flex items-center gap-2">
              <FolderKanban className="w-4 h-4 text-primary" /> Nome do Projeto
            </label>
            <input 
              id="projName"
              type="text"
              placeholder="Ex: Refinaria XYZ - Unidade 100"
              title="Digite o nome do projeto"
              value={projectName}
              onChange={(e) => setProjectName(e.target.value)}
              className="w-full bg-background border border-border rounded-lg px-4 py-2 focus:ring-2 focus:ring-primary outline-none"
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* BOX PIPING */}
            <div 
              onClick={() => pipingRef.current?.click()}
              className="border-2 border-dashed border-blue-500/30 bg-blue-500/5 hover:bg-blue-500/10 rounded-lg p-8 text-center cursor-pointer transition-all"
            >
              <input 
                type="file" 
                className="hidden" 
                ref={pipingRef} 
                accept=".dcf,.sqlite"
                title="Arquivo Piping"
                onChange={(e) => setPipingFile(e.target.files?.[0] || null)} 
              />
              <Box className="w-10 h-10 mx-auto text-blue-400 mb-2" />
              <p className="text-xs font-bold text-blue-400">{pipingFile ? pipingFile.name : "Subir Piping.dcf"}</p>
            </div>

            {/* BOX P&ID */}
            <div 
              onClick={() => pidRef.current?.click()}
              className="border-2 border-dashed border-emerald-500/30 bg-emerald-500/5 hover:bg-emerald-500/10 rounded-lg p-8 text-center cursor-pointer transition-all"
            >
              <input 
                type="file" 
                className="hidden" 
                ref={pidRef} 
                accept=".dcf,.sqlite"
                title="Arquivo ProcessPower"
                onChange={(e) => setPidFile(e.target.files?.[0] || null)} 
              />
              <GitBranch className="w-10 h-10 mx-auto text-emerald-400 mb-2" />
              <p className="text-xs font-bold text-emerald-400">{pidFile ? pidFile.name : "Subir ProcessPower.dcf"}</p>
            </div>
          </div>

          <Button 
            onClick={handleUpload} 
            disabled={isUploading}
            className="w-full h-12 font-bold gap-2 text-lg"
          >
            {isUploading ? <Loader2 className="animate-spin" /> : <Save />}
            {isUploading ? "ATUALIZANDO..." : "SALVAR E SINCRONIZAR"}
          </Button>
        </div>
      </div>
    </div>
  );
}