import React, { useState, useRef, useEffect, useCallback } from 'react';
import { Document, Page, pdfjs } from 'react-pdf';
import toast from 'react-hot-toast';
import { ZoomIn, ZoomOut, Maximize, Hand, UploadCloud } from 'lucide-react';
import { useGlobalFileDrop } from '@/components/hooks/useGlobalFileDrop';

// Estilos obrigatórios
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";

// Worker seguro do Vite (Sua base funcional)
pdfjs.GlobalWorkerOptions.workerSrc = `https://unpkg.com/pdfjs-dist@${pdfjs.version}/build/pdf.worker.min.mjs`;

interface Props {
  pacoteId: number;
  tipoFolha: string;
  onBack: () => void;
}

export const OCRCalibrator: React.FC<Props> = ({ pacoteId, tipoFolha, onBack }) => {
  const [fileUrl, setFileUrl] = useState<string | null>(null);
  const [isDrawing, setIsDrawing] = useState(false);
  const [activeField, setActiveField] = useState("Revisão");
  const [zones, setZones] = useState<Record<string, any>>({});
  const [currentRect, setCurrentRect] = useState<any>(null);
  
  // ESTADOS DE ZOOM
  const [zoom, setZoom] = useState<number>(1);
  const BASE_WIDTH = 850; 
  
  // ESTADOS DE NAVEGAÇÃO "MÃOZINHA" (Pan)
  const [isPanning, setIsPanning] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null); 
  const panPos = useRef({ x: 0, y: 0, left: 0, top: 0 }); 

  const containerRef = useRef<HTMLDivElement>(null);
  const camposDisponiveis = ["Número do Documento", "Revisão", "Título", "Data", "Projetista"];

  useEffect(() => {
    return () => {
      if (fileUrl) URL.revokeObjectURL(fileUrl);
    };
  }, [fileUrl]);

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const uploadedFile = e.target.files?.[0];
    if (!uploadedFile) return;
    processarPdf(uploadedFile);
    e.target.value = ""; // permite selecionar o mesmo arquivo de novo depois
  };
    

  const processarPdf = useCallback ((uploadFile: File)=>{
    if (uploadFile.type !== "application/pdf"){
      toast.error("Formato Inválido! Suba um documento PDF técnico.");
      return;
    }
    const safeUrl = URL.createObjectURL(uploadFile);
    setFileUrl(safeUrl);
    setZoom(1);
  } ,[]);

  // -------------------------------------------------------------
  // 🖱️ LÓGICA DA "MÃOZINHA" (PANNING - BOTÃO DIREITO)
  // -------------------------------------------------------------
  const handlePanStart = (e: React.MouseEvent) => {
    if (e.button !== 1 && e.button !== 2) return; 
    e.preventDefault();
    setIsPanning(true);
    if (scrollRef.current) {
      panPos.current = {
        x: e.clientX,
        y: e.clientY,
        left: scrollRef.current.scrollLeft,
        top: scrollRef.current.scrollTop,
      };
    }
  };

  const handlePanMove = (e: React.MouseEvent) => {
    if (!isPanning || !scrollRef.current) return;
    e.preventDefault();
    const dx = e.clientX - panPos.current.x;
    const dy = e.clientY - panPos.current.y;
    scrollRef.current.scrollLeft = panPos.current.left - dx;
    scrollRef.current.scrollTop = panPos.current.top - dy;
  };

  const handlePanEnd = () => {
    setIsPanning(false);
  };

  // -------------------------------------------------------------
  // 🔎 LÓGICA DE ZOOM NO SCROLL DO MOUSE (WHEEL)
  // -------------------------------------------------------------
  useEffect(() => {
    const container = scrollRef.current;
    if (!container) return;

    const handleWheel = (e: WheelEvent) => {
      // Impede que a página inteira role enquanto faz o zoom
      e.preventDefault();

      setZoom(prevZoom => {
        const zoomStep = 0.15;
        // Calcula o novo zoom, limitando entre 0.5 (50%) e 4 (400%)
        const newZoom = Math.min(Math.max(0.5, prevZoom + (e.deltaY < 0 ? zoomStep : -zoomStep)), 4);

        if (newZoom !== prevZoom) {
          // Lógica para aplicar o zoom focando na posição atual do mouse (Ponto de Ancoragem)
          const rect = container.getBoundingClientRect();
          const mouseX = e.clientX - rect.left;
          const mouseY = e.clientY - rect.top;

          const ratio = newZoom / prevZoom;

          // Timeout rápido para aguardar a renderização do React e ajustar o scroll
          setTimeout(() => {
            if (scrollRef.current) {
              scrollRef.current.scrollLeft = (scrollRef.current.scrollLeft + mouseX) * ratio - mouseX;
              scrollRef.current.scrollTop = (scrollRef.current.scrollTop + mouseY) * ratio - mouseY;
            }
          }, 0);
        }
        return newZoom;
      });
    };

    // Usamos { passive: false } para podermos rodar o e.preventDefault() com segurança
    container.addEventListener('wheel', handleWheel, { passive: false });
    return () => container.removeEventListener('wheel', handleWheel);
  }, [fileUrl]); // Re-injeta o evento se o arquivo mudar

  // -------------------------------------------------------------
  // ✏️ LÓGICA DE DESENHO (BOTÃO ESQUERDO)
  // -------------------------------------------------------------
  const handleDrawStart = (e: React.MouseEvent) => {
    if (e.button !== 0) return; 
    if (!containerRef.current || !fileUrl) return;
    
    const rectBound = containerRef.current.getBoundingClientRect();
    setIsDrawing(true);
    setCurrentRect({
      x: (e.clientX - rectBound.left) / zoom,
      y: (e.clientY - rectBound.top) / zoom,
      width: 0,
      height: 0
    });
  };

  const handleDrawMove = (e: React.MouseEvent) => {
    if (!isDrawing || !containerRef.current) return;
    const rectBound = containerRef.current.getBoundingClientRect();
    setCurrentRect((prev: any) => ({
      ...prev,
      width: ((e.clientX - rectBound.left) / zoom) - prev.x,
      height: ((e.clientY - rectBound.top) / zoom) - prev.y
    }));
  };

  const handleDrawEnd = () => {
    if (isDrawing) {
        setIsDrawing(false);
        if (currentRect && Math.abs(currentRect.width) > 5) {
        setZones({ ...zones, [activeField]: currentRect });
        }
        setCurrentRect(null);
    }
  };

    const handleFileDropped = useCallback((file: File) => {
    processarPdf(file);
  }, [processarPdf]);

  const isDraggingOverPage = useGlobalFileDrop(handleFileDropped);

  const saveAllZones = async () => {
    if (Object.keys(zones).length === 0) {
      toast.error("Desenhe ao menos uma área no carimbo.");
      return;
    }
    
    const tid = toast.loading("Enviando coordenadas...");
    
    try {
      const payload = Object.keys(zones).map(campo => ({
        X: Number(zones[campo].x.toFixed(2)),
        Y: Number(zones[campo].y.toFixed(2)),
        Width: Number(zones[campo].width.toFixed(2)),
        Height: Number(zones[campo].height.toFixed(2)),
        Campo: campo,
        CampoDestino: campo 
      }));

      console.log("Enviando Payload:", payload);

      const res = await fetch(`/api/admin/ocr/salvar-coordenadas?pacoteId=${pacoteId}&tipoFolha=${tipoFolha}`, {
        method: 'POST',
        headers: { 
          'Content-Type': 'application/json',
          'Accept': 'application/json'
        },
        body: JSON.stringify(payload)
      });

      if (res.ok) {
        toast.success("✅ Configuração salva!", { id: tid });
        onBack();
      } else {
        const errorDetail = await res.json().catch(() => null);
        console.error("Erro de Validação do .NET:", errorDetail);
        
        toast.error(`Erro ${res.status}: Dados incompatíveis.`, { id: tid });
      }
    } catch (err) {
      console.error("Erro na requisição:", err);
      toast.error("Não foi possível conectar ao servidor.", { id: tid });
    }
  };

  return (
    <div className="flex h-screen w-screen bg-background rounded-3xl overflow-hidden border border-border shadow-sm animate-enter relative">
      
      {/* Sidebar Esquerda */}
      <div className="w-max bg-card border-r border-border p-8 flex flex-col gap-4 z-auto shadow-xl">
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
            title="Selecione um arquivo PDF para calibrar as coordenadas"
            aria-label="Upload de PDF"
            placeholder="Nenhum arquivo"
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
              title={`Mapear ${campo}`}
              aria-label={`Mapear ${campo}`}
              onClick={() => setActiveField(campo)}
              className={`p-3 rounded-xl border-2 text-left text-xs font-bold transition-all flex justify-between items-center ${
                activeField === campo ? "border-primary bg-primary/5 shadow-inner scale-[1.02] text-foreground" : "border-border bg-background text-muted-foreground hover:border-primary/50 hover:text-foreground"
              }`}
            >
              <span>{campo}</span>
              {zones[campo] && <div className="w-2.5 h-2.5 rounded-full bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]" />}
            </button>
          ))}
        </div>

        <div className="bg-blue-500/10 p-3 rounded-xl border border-blue-500/20 flex items-start gap-3 mt-4">
            <Hand className="w-5 h-5 text-blue-500 shrink-0" />
            <p className="text-[9px] font-bold text-blue-600 dark:text-blue-400 leading-relaxed uppercase tracking-wider">
                Dicas:<br/>
                <b>Botão Direito:</b> Puxar e arrastar.<br/>
                <b>Scroll (Roda):</b> Zoom no ponteiro.
            </p>
        </div>

        <div className="mt-auto space-y-2">
          <button onClick={saveAllZones} aria-label="Salvar Configuração" className="w-full py-4 bg-primary text-primary-foreground rounded-2xl font-black text-[10px] uppercase tracking-widest hover:bg-primary/90 transition-all shadow-lg active:scale-95">Salvar Configuração</button>
          <button onClick={onBack} aria-label="Cancelar" className="w-full py-2 text-muted-foreground text-[10px] font-black uppercase hover:text-foreground transition-colors">Cancelar</button>
        </div>
      </div>

      {/* Visualizador à Direita com Scroll e Eventos de Panning */}
      <div 
        ref={scrollRef}
        className={`flex-1 overflow-auto p-12 flex justify-center bg-muted/30 relative custom-scrollbar ${isPanning ? 'cursor-grabbing' : ''}`}
        onContextMenu={(e) => e.preventDefault()}
        onMouseDown={handlePanStart}
        onMouseMove={handlePanMove}
        onMouseUp={handlePanEnd}
        onMouseLeave={handlePanEnd}
      >
        
        {/* BARRA DE ZOOM FLUTUANTE */}
        {fileUrl && (
            <div className="fixed top-24 right-12 z-30 flex items-center gap-2 bg-card/90 backdrop-blur-md p-2 rounded-full shadow-xl border border-border">
                <button onClick={() => setZoom(z => Math.max(0.5, z - 0.25))} aria-label="Afastar (-)" title="Afastar (-)" className="p-2 hover:bg-muted rounded-full text-muted-foreground hover:text-foreground transition-colors active:scale-90">
                    <ZoomOut className="w-5 h-5" />
                </button>
                <span className="text-xs font-black w-12 text-center text-foreground select-none cursor-default">
                    {Math.round(zoom * 100)}%
                </span>
                <button onClick={() => setZoom(z => Math.min(4, z + 0.25))} aria-label="Aproximar (+)" title="Aproximar (+)" className="p-2 hover:bg-muted rounded-full text-muted-foreground hover:text-foreground transition-colors active:scale-90">
                    <ZoomIn className="w-5 h-5" />
                </button>
                <div className="w-px h-6 bg-border mx-1"></div>
                <button onClick={() => setZoom(1)} aria-label="Escala Original (100%)" title="Escala Original (100%)" className="p-2 hover:bg-muted rounded-full text-primary transition-colors active:scale-90">
                    <Maximize className="w-4 h-4" />
                </button>
            </div>
        )}

        {fileUrl ? (
          <div 
            ref={containerRef}
            className={`relative shadow-2xl bg-white transition-opacity duration-300 ${isPanning ? 'cursor-grabbing' : 'cursor-crosshair'}`}
            style={{ width: 'fit-content', height: 'fit-content', margin: 'auto' }}
            onMouseDown={handleDrawStart}
            onMouseMove={handleDrawMove}
            onMouseUp={handleDrawEnd}
          >
            <Document 
              file={fileUrl} 
              onLoadError={(err) => toast.error("PDF protegido ou ilegível.")}
              loading={<div className="p-20 font-black text-muted-foreground animate-pulse text-[10px] uppercase tracking-widest text-center">Processando Prancha...</div>}
            >
              <Page 
                pageNumber={1} 
                renderTextLayer={false} 
                renderAnnotationLayer={false} 
                width={BASE_WIDTH * zoom} 
              />
            </Document>

            {Object.keys(zones).map(campo => (
              <div key={campo} className="absolute border-[3px] border-emerald-500 bg-emerald-500/20 pointer-events-none"
                   style={{ 
                     left: zones[campo].x * zoom, 
                     top: zones[campo].y * zoom, 
                     width: zones[campo].width * zoom, 
                     height: zones[campo].height * zoom 
                   }}>
                <span className="absolute -top-6 left-0 bg-emerald-600 text-white text-[9px] px-2 py-0.5 font-black rounded shadow-sm uppercase whitespace-nowrap">
                  {campo}
                </span>
              </div>
            ))}

            {currentRect && (
              <div className="absolute border-[3px] border-primary bg-primary/20 pointer-events-none ring-4 ring-primary/10"
                   style={{ 
                     left: currentRect.x * zoom, 
                     top: currentRect.y * zoom, 
                     width: currentRect.width * zoom, 
                     height: currentRect.height * zoom 
                   }}>
                <span className="absolute -top-8 left-0 bg-primary text-primary-foreground text-[10px] px-2 py-1 font-black rounded shadow-sm uppercase tracking-wider whitespace-nowrap">
                  Mapeando: {activeField}
                </span>
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