import { useState, useRef, DragEvent, useEffect } from "react";
import toast from "react-hot-toast";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { UploadCloud, FileSpreadsheet, X } from "lucide-react";

interface UploadDocumentModalProps {
  isOpen: boolean;
  onClose: () => void;
  projectId: string;
  folderId?: string;
  projectName: string;
  hubId: string;
  initialFile?: File | null;
  onSuccess?: () => void | Promise<void>;
}

const EXTENSOES_ACEITAS = [".xlsx", ".xlsm", ".xls"];

export function UploadDocumentModal({
  isOpen,
  onClose,
  projectId,
  folderId,
  projectName,
  hubId,
  initialFile,
  onSuccess,
}: UploadDocumentModalProps) {
  const [file, setFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const validarExtensao = (nome: string) => {
    const ext = nome.slice(nome.lastIndexOf(".")).toLowerCase();
    return EXTENSOES_ACEITAS.includes(ext);

    
  };

    useEffect(() => {
    if (isOpen && initialFile) {
      if (validarExtensao(initialFile.name)) {
        setFile(initialFile);
      } else {
        toast.error("Envie apenas arquivos Excel (.xlsx, .xlsm ou .xls).");
      }
    }
  }, [isOpen, initialFile]);

  

  const aplicarArquivo = (novoArquivo: File | undefined) => {
    if (!novoArquivo) return;

    if (!validarExtensao(novoArquivo.name)) {
      toast.error("Envie apenas arquivos Excel (.xlsx, .xlsm ou .xls).");
      return;
    }

    setFile(novoArquivo);
  };

  const handleDragOver = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    if (!loading) setIsDragging(true);
  };

  const handleDragLeave = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);

    if (loading) return;

    const arquivoSolto = e.dataTransfer.files?.[0];
    aplicarArquivo(arquivoSolto);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!file) {
      toast.error("Por favor, selecione a planilha LDP.");
      return;
    }

    setLoading(true);
    const loadingToast = toast.loading("Validando e enviando planilha...");

    try {
      let targetFolderId = folderId;

      if (!targetFolderId) {
        const folderRes = await fetch(
          `/DataManagement/Data/GetProjectRootFolder?hubId=${hubId}&projectId=${projectId}`
        );

        if (!folderRes.ok) {
          throw new Error("Não foi possível obter a pasta do projeto.");
        }

        const { rootFolderId } = await folderRes.json();
        targetFolderId = rootFolderId;
      }

      if (!targetFolderId) {
        throw new Error("ID da pasta de destino não encontrado.");
      }

      const formData = new FormData();
      formData.append("arquivo", file);
      formData.append("projectId", projectId);
      formData.append("folderId", targetFolderId);

      const response = await fetch("/api/AdminMaster/upload-documento", {
        method: "POST",
        body: formData,
      });

      const data = await response.json().catch(() => null);

      if (response.ok) {
        toast.success(data?.mensagem || "Documento enviado com sucesso!", { id: loadingToast });
        handleClose();
        if (onSuccess) {
          await onSuccess();
        }
      } else if (response.status === 401) {
        toast.error("Sessão expirada. Faça login novamente.", { id: loadingToast });
      } else if (response.status === 422 && data?.aviso) {
        toast.error(data.mensagem || "Planilha LDP inválida.", {
          id: loadingToast,
          duration: 6000,
        });
      } else {
        toast.error(
          data?.mensagem || data?.message || data?.erro || "Falha no envio do documento.",
          { id: loadingToast }
        );
      }
    } catch (error: any) {
      console.error("Erro no upload:", error);
      toast.error(error.message || "Erro de conexão.", { id: loadingToast });
    } finally {
      setLoading(false);
    }
  };

  const handleClose = () => {
    setFile(null);
    setIsDragging(false);
    onClose();
  };

  return (
    <Dialog open={isOpen} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-[425px]">
        <DialogHeader>
          <DialogTitle>Upload de Documento</DialogTitle>
          <p className="text-sm text-muted-foreground">
            Projeto: <strong className="text-foreground">{projectName}</strong>
          </p>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4 py-2">
          <div className="space-y-2">
            <Label>Planilha LDP (Excel)</Label>

            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => !loading && fileInputRef.current?.click()}
              className={`
                relative flex flex-col items-center justify-center gap-2
                rounded-lg border-2 border-dashed p-6 text-center cursor-pointer
                transition-colors
                ${isDragging ? "border-primary bg-primary/5" : "border-muted-foreground/25 hover:border-muted-foreground/50"}
                ${loading ? "opacity-50 cursor-not-allowed" : ""}
              `}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept={EXTENSOES_ACEITAS.join(",")}
                onChange={(e) => aplicarArquivo(e.target.files?.[0])}
                disabled={loading}
                className="hidden"
              />

              {file ? (
                <>
                  <FileSpreadsheet className="h-8 w-8 text-primary" />
                  <p className="text-sm font-medium">{file.name}</p>
                  <p className="text-xs text-muted-foreground">
                    {(file.size / 1024).toFixed(0)} KB
                  </p>
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      setFile(null);
                    }}
                    className="absolute top-2 right-2 rounded-full p-1 hover:bg-muted"
                    disabled={loading}
                  >
                    <X className="h-4 w-4" />
                  </button>
                </>
              ) : (
                <>
                  <UploadCloud className="h-8 w-8 text-muted-foreground" />
                  <p className="text-sm text-muted-foreground">
                    Arraste a planilha aqui ou{" "}
                    <span className="text-primary font-medium">clique para selecionar</span>
                  </p>
                  <p className="text-xs text-muted-foreground">.xlsx, .xlsm ou .xls</p>
                </>
              )}
            </div>
          </div>

          <DialogFooter className="pt-4">
            <Button
              type="button"
              variant="outline"
              onClick={handleClose}
              disabled={loading}
            >
              Cancelar
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enviando..." : "Enviar Documento"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}