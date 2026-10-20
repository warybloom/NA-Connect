import { useState, useEffect } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Settings2, Save, Database } from "lucide-react";
import { toast } from "react-hot-toast";

interface Plant3DSettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
  // futuramente passaremos o projectId aqui para buscar a config certa no banco
}

export function Plant3DSettingsModal({ isOpen, onClose }: Plant3DSettingsModalProps) {
  const [isSaving, setIsSaving] = useState(false);
  const [config, setConfig] = useState({
    folderIdDcf: "",
    tabelaLinhas: "PnPBase", // Exemplo de padrão do Plant 3D
    colunaTag: "Tag",
    colunaSpec: "Spec",
    colunaStatus: "Status"
  });

  const handleSave = async () => {
    setIsSaving(true);
    // Aqui vai entrar a chamada real para a sua API C# posteriormente
    setTimeout(() => {
      toast.success("Mapeamento do Plant 3D salvo com sucesso!");
      setIsSaving(false);
      onClose();
    }, 1000);
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-[600px] glass-card border-border/50">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-xl font-bold text-primary">
            <Settings2 className="w-5 h-5" />
            Configurações e Mapeamento DCF
          </DialogTitle>
        </DialogHeader>

        <div className="grid gap-6 py-4">
          {/* Sessão de Pastas */}
          <div className="space-y-3">
            <h4 className="text-sm font-semibold flex items-center gap-2 text-muted-foreground border-b border-border/50 pb-2">
              <Database className="w-4 h-4" /> Diretórios ACC
            </h4>
            <div className="grid gap-2">
              <Label htmlFor="folderId">ID da Pasta dos arquivos .DCF</Label>
              <Input 
                id="folderId" 
                placeholder="urn:adsk.wipprod:fs.folder:..." 
                value={config.folderIdDcf}
                onChange={(e) => setConfig({...config, folderIdDcf: e.target.value})}
              />
            </div>
          </div>

          {/* Sessão de Mapeamento */}
          <div className="space-y-3">
            <h4 className="text-sm font-semibold flex items-center gap-2 text-muted-foreground border-b border-border/50 pb-2">
              <Settings2 className="w-4 h-4" /> Mapeamento de Colunas (SQLite)
            </h4>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="tblLinhas">Tabela Base (Piping)</Label>
                <Input 
                  id="tblLinhas" 
                  value={config.tabelaLinhas}
                  onChange={(e) => setConfig({...config, tabelaLinhas: e.target.value})}
                />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="colTag">Coluna de TAG</Label>
                <Input 
                  id="colTag" 
                  value={config.colunaTag}
                  onChange={(e) => setConfig({...config, colunaTag: e.target.value})}
                />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="colSpec">Coluna de SPEC</Label>
                <Input 
                  id="colSpec" 
                  value={config.colunaSpec}
                  onChange={(e) => setConfig({...config, colunaSpec: e.target.value})}
                />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="colStatus">Coluna de Status</Label>
                <Input 
                  id="colStatus" 
                  value={config.colunaStatus}
                  onChange={(e) => setConfig({...config, colunaStatus: e.target.value})}
                />
              </div>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={onClose} disabled={isSaving}>Cancelar</Button>
          <Button onClick={handleSave} disabled={isSaving} className="font-bold gap-2 shadow-lg shadow-primary/20">
            <Save className="w-4 h-4" />
            {isSaving ? "Salvando..." : "Salvar Configuração"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}