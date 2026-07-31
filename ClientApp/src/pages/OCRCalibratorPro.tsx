import React, { useState, useRef, useEffect } from 'react';
// ✅ Importação padrão e segura, suportada por todas as versões modernas
import { Document, Page, pdfjs } from 'react-pdf';
import toast from 'react-hot-toast';

// ✅ Estilos importados diretamente da raiz do pacote
import 'react-pdf/dist/Page/AnnotationLayer.css';
import 'react-pdf/dist/Page/TextLayer.css';

// ✅ O Segredo para não dar "Tela Azul" no Vite:
// Forçar o worker a usar a mesma versão exata do pdfjs instalado via CDN
pdfjs.GlobalWorkerOptions.workerSrc = `https://unpkg.com/pdfjs-dist@${pdfjs.version}/build/pdf.worker.min.js`;

interface Props {
  pacoteId: number;
  tipoFolha: string;
  onBack: () => void;
}

export const OCRCalibratorPro: React.FC<Props> = ({ pacoteId, tipoFolha, onBack }) => {
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [isDrawing, setIsDrawing] = useState(false);
  const [activeField, setActiveField] = useState("Revisão");
  const [zones, setZones] = useState<Record<string, any>>({});
  const [currentRect, setCurrentRect] = useState<any>(null);
  
  const containerRef = useRef<HTMLDivElement>(null);
  const camposDisponiveis = ["Número do Documento", "Revisão", "Título", "Data", "Projetista"];

  // Limpa a URL temporária da memória quando o componente for destruído
  useEffect(() => {
    return () => {
      if (fileUrl) URL.revokeObjectURL(fileUrl);
    };
  }, [fileUrl]);

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const uploadedFile = e.target.files?.[0];
    if (!uploadedFile) return;

    if (uploadedFile.type !== "application/pdf") {
      toast.error("Formato Inválido! Suba um arquivo PDF.");
      e.target.value = "";
      return;
    }

    // ✅ Cria uma URL segura para o react-pdf não engasgar com o Buffer
    const safeUrl = URL.createObjectURL(uploadedFile);
    setFileUrl(safeUrl);
  };

  const handleMouseDown = (e: React.MouseEvent) => {
    if (!containerRef.current || !fileUrl) return;
    const rectBound = containerRef.current.getBoundingClientRect();
    setIsDrawing(true);
    setCurrentRect({
      x: e.clientX - rectBound.left,
      y: e.clientY - rectBound.top,
      width: 0,
      height: 0
    });
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!isDrawing || !containerRef.current) return;
    const rectBound = containerRef.current.getBoundingClientRect();
    setCurrentRect((prev: any) => ({
      ...prev,
      width: (e.clientX - rectBound.left) - prev.x,
      height: (e.clientY - rectBound.top) - prev.y
    }));
  };

  const finalizeZone = () => {
    setIsDrawing(false);
    if (currentRect && Math.abs(currentRect.width) > 5) {
      setZones({ ...zones, [activeField]: currentRect });
    }
    setCurrentRect(null);
  };

  const saveAllZones = async () => {
    if (Object.keys(zones).length === 0) {
      toast.error("Desenhe ao menos uma área no carimbo.");
      return;
    }
    
    const tid = toast.loading("Salvando coordenadas...");
    const payload = Object.keys(zones).map(campo => ({
      x: Math.round(zones[campo].x),
      y: Math.round(zones[campo].y),
      width: Math.round(zones[campo].width),
      height: Math.round(zones[campo].height),
      campoDestino: campo
    }));

    try {
      const res = await fetch(`/api/admin/ocr/salvar-coordenadas?pacoteId=${pacoteId}&tipoFolha=${tipoFolha}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        toast.success("✅ Setup salvo com sucesso!", { id: tid });
        onBack();
      } else {
        throw new Error();
      }
    } catch (err) {
      toast.error("Falha ao salvar no banco.", { id: tid });
    }
  };

  return (
    <div className="flex h-[calc(100vh-180px)] w-full bg-background rounded-3xl overflow-hidden border border-border shadow-sm animate-enter">
      {/* Sidebar */}
      <div className="w-80 bg-card border-r border-border p-6 flex flex-col gap-6 z-10 shadow-xl">
        <div className="border-b border-border pb-4">
          <h3 className="font-black text-foreground text-xl tracking-tight uppercase italic">Setup de Visão</h3>
          <p className="text-[10px] font-bold text-primary uppercase mt-1 tracking-widest">
            {tipoFolha} | Pacote: {pacoteId}
          </p>
        </div>

        <div className="flex flex-col gap-2">
          <label htmlFor="ocr-pdf-upload" className="text-[10px] font-black uppercase text-muted-foreground">
            1. Carregar Prancha (PDF)
          </label>
          <input 
            id="ocr-pdf-upload"
            title="Selecione o PDF para calibrar o carimbo"
            aria-label="Upload de PDF"
            type="file" 
            accept="application/pdf"
            className="text-[10px] text-muted-foreground cursor-pointer file:mr-4 file:py-2 file:px-4 file:rounded-full file:border-0 file:bg-primary/10 file:text-primary hover:file:bg-primary/20 transition-all font-bold"
            onChange={handleFileUpload} 
          />
        </div>

        <div className="flex flex-col gap-2 overflow-y-auto custom-scrollbar">
          <span className="text-[10px] font-black uppercase text-muted-foreground mb-1">2. Mapear Campos</span>
          {camposDisponiveis.map(campo => (
            <button
              key={campo}
              type="button"
              title={`Mapear o campo ${campo}`}
              aria-label={`Mapear o campo ${campo}`}
              onClick={() => setActiveField(campo)}
              className={`p-3 rounded-xl border-2 text-left text-xs font-bold transition-all flex justify-between items-center ${
                activeField === campo ? "border-primary bg-primary/5 shadow-inner scale-[1.02] text-foreground" : "border-border bg-background text-muted-foreground hover:border-primary/30 hover:text-foreground"
              }`}
            >
              <span>{campo}</span>
              {zones[campo] && <div className="w-2.5 h-2.5 rounded-full bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]" />}
            </button>
          ))}
        </div>

        <div className="mt-auto space-y-2">
          <button 
            onClick={saveAllZones} 
            title="Salvar configurações e enviar para o banco"
            aria-label="Salvar configurações"
            className="w-full py-4 bg-primary text-primary-foreground rounded-2xl font-black text-[10px] uppercase tracking-widest hover:bg-primary/90 transition-all shadow-lg active:scale-95"
          >
            Salvar Configuração
          </button>
          <button 
            onClick={onBack} 
            title="Cancelar e Voltar"
            aria-label="Cancelar"
            className="w-full py-2 text-muted-foreground text-[10px] font-black uppercase hover:text-foreground transition-colors"
          >
            Cancelar
          </button>
        </div>
      </div>

      {/* Visualizador do PDF */}
      <div className="flex-1 overflow-auto p-12 flex justify-center bg-muted/30 relative custom-scrollbar">
        {fileUrl ? (
          <div 
            ref={containerRef}
            className="relative shadow-2xl bg-white h-fit transition-all duration-500 cursor-crosshair"
            onMouseDown={handleMouseDown}
            onMouseMove={handleMouseMove}
            onMouseUp={finalizeZone}
          >
            <Document 
              file={fileUrl} 
              onLoadError={(err) => {
                console.error("Erro interno do react-pdf:", err);
                toast.error("O PDF está protegido ou corrompido.");
              }}
              loading={<div className="p-20 font-black text-muted-foreground animate-pulse text-[10px] uppercase tracking-widest text-center">Processando Prancha...</div>}
            >
              <Page 
                pageNumber={1} 
                renderTextLayer={false} 
                renderAnnotationLayer={false} 
                width={850} 
              />
            </Document>

            {Object.keys(zones).map(campo => (
              <div key={campo} className="absolute border-2 border-emerald-500 bg-emerald-500/20 pointer-events-none"
                   style={{ left: zones[campo].x, top: zones[campo].y, width: zones[campo].width, height: zones[campo].height }}>
                <span className="absolute -top-6 left-0 bg-emerald-600 text-white text-[8px] px-2 py-0.5 font-black rounded shadow-sm uppercase whitespace-nowrap">{campo}</span>
              </div>
            ))}

            {currentRect && (
              <div className="absolute border-2 border-primary bg-primary/20 pointer-events-none ring-4 ring-primary/10"
                   style={{ left: currentRect.x, top: currentRect.y, width: currentRect.width, height: currentRect.height }}>
                <span className="absolute -top-8 left-0 bg-primary text-primary-foreground text-[10px] px-2 py-1 font-black rounded shadow-sm uppercase tracking-wider whitespace-nowrap">Mapeando: {activeField}</span>
              </div>
            )}
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center border-4 border-dashed border-border rounded-[40px] w-full max-w-2xl h-[600px] bg-card/30 backdrop-blur-sm">
            <div className="bg-muted p-10 rounded-full mb-6 text-5xl opacity-50">📄</div>
            <p className="text-muted-foreground font-black text-[10px] uppercase tracking-[0.3em]">Aguardando PDF</p>
          </div>
        )}
      </div>
    </div>
  );
};