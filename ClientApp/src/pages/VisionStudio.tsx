import React, { useState, useRef, useEffect } from 'react';
import { Crosshair, Save, Settings2, FileSearch, Ruler, ShieldCheck, AlertCircle } from 'lucide-react';
import { toast } from 'react-hot-toast';
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { ModeToggle } from '@/components/alternar-modo';

const VisionStudio: React.FC = () => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [isDrawing, setIsDrawing] = useState(false);
  const [startPos, setStartPos] = useState({ x: 0, y: 0 });
  const [rect, setRect] = useState({ x: 0, y: 0, width: 0, height: 0 });
  const [zoom, setZoom] = useState(1);
  const [sheetSize, setSheetSize] = useState("A1");

  // Simulação de carregamento do PDF convertido em imagem para o Canvas
  const img = useRef(new Image());

  useEffect(() => {
    img.current.src = "https://images.unsplash.com/photo-1581092160562-40aa08e78837?auto=format&fit=crop&q=80&w=2000"; // Placeholder de alta resolução
    img.current.onload = () => draw();
  }, []);

  const draw = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img.current, 0, 0, canvas.width, canvas.height);

    if (rect.width > 0) {
      // Estilo da Caixa de Seleção OCR (Neon Blue)
      ctx.setLineDash([5, 5]);
      ctx.strokeStyle = '#00f2ff';
      ctx.lineWidth = 2;
      ctx.strokeRect(rect.x, rect.y, rect.width, rect.height);

      // Preenchimento Suave
      ctx.fillStyle = 'rgba(0, 242, 255, 0.1)';
      ctx.fillRect(rect.x, rect.y, rect.width, rect.height);

      // Marcadores nos cantos
      ctx.setLineDash([]);
      ctx.fillStyle = '#00f2ff';
      ctx.fillRect(rect.x - 4, rect.y - 4, 8, 8);
      ctx.fillRect(rect.x + rect.width - 4, rect.y + rect.height - 4, 8, 8);
    }
  };

  const handleMouseDown = (e: React.MouseEvent) => {
    const rectCanvas = canvasRef.current?.getBoundingClientRect();
    if (!rectCanvas) return;
    setStartPos({ x: e.clientX - rectCanvas.left, y: e.clientY - rectCanvas.top });
    setIsDrawing(true);
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!isDrawing) return;
    const rectCanvas = canvasRef.current?.getBoundingClientRect();
    if (!rectCanvas) return;

    const currentX = e.clientX - rectCanvas.left;
    const currentY = e.clientY - rectCanvas.top;

    setRect({
      x: Math.min(startPos.x, currentX),
      y: Math.min(startPos.y, currentY),
      width: Math.abs(currentX - startPos.x),
      height: Math.abs(currentY - startPos.y)
    });
    draw();
  };

  const saveOcrConfig = async () => {
    const loadToast = toast.loading("Mapeando coordenadas no ForgeSync...");
    // Aqui vai o seu fetch para o C#
    setTimeout(() => {
        toast.success("Calibração Salva! O robô agora lerá esta área.", { id: loadToast });
    }, 1000);
  };

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        {/* Trava a altura total na viewport - nada aqui deve exceder a tela */}
        <div className="flex flex-col h-screen overflow-hidden">

          {/* Barra superior fixa, não cresce */}
          <div className="flex items-center gap-4 p-4 shrink-0">
            <SidebarTrigger />
          </div>

          {/* Estúdio ocupa o restante da altura disponível */}
          <div className="flex-1 min-h-0 flex bg-[#0a0a0c] text-slate-300 overflow-hidden font-sans">

            {/* Sidebar de Ferramentas Esquerda */}
            <div className="w-16 border-r border-white/5 flex flex-col items-center py-6 gap-6 bg-[#0f0f12] shrink-0">
              <div className="p-3 bg-blue-600/20 text-blue-400 rounded-xl cursor-pointer shadow-[0_0_15px_rgba(37,99,235,0.3)]">
                <Crosshair size={24} />
              </div>
              <div className="p-3 hover:bg-white/5 rounded-xl cursor-pointer transition-colors">
                <Ruler size={24} />
              </div>
              <div className="p-3 hover:bg-white/5 rounded-xl cursor-pointer transition-colors">
                <FileSearch size={24} />
              </div>
            </div>

            {/* Área Central do Canvas */}
            <div className="flex-1 min-w-0 flex flex-col relative">
              {/* Header Interno */}
              <div className="h-16 border-b border-white/5 flex items-center justify-between px-8 bg-[#0f0f12]/50 backdrop-blur-md shrink-0">
                <div className="flex items-center gap-3 min-w-0">
                  <span className="text-xs font-black tracking-widest text-slate-500 uppercase shrink-0">Vision Studio v1.0</span>
                  <div className="h-4 w-px bg-white/10 shrink-0" />
                  <h2 className="text-sm font-semibold text-white truncate">Projeto: TESTE 001 - API</h2>
                </div>

                <div className="flex items-center gap-4 shrink-0">
                  <div className="flex bg-black/40 rounded-lg p-1 border border-white/5">
                      <button onClick={() => setZoom(z => z - 0.1)} className="px-3 py-1 hover:bg-white/5 rounded">-</button>
                      <span className="px-3 py-1 text-xs flex items-center">{Math.round(zoom * 100)}%</span>
                      <button onClick={() => setZoom(z => z + 0.1)} className="px-3 py-1 hover:bg-white/5 rounded">+</button>
                  </div>
                  <button
                      onClick={saveOcrConfig}
                      className="flex items-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-5 py-2 rounded-lg text-sm font-bold transition-all shadow-lg shadow-blue-600/20"
                  >
                    <Save size={18} /> Salvar Calibração
                  </button>
                </div>
              </div>

              {/* Viewport do Canvas */}
              <div className="flex-1 min-h-0 overflow-auto bg-[#050506] flex items-center justify-center p-12 scrollbar-hide">
                <div className="relative shadow-[0_0_50px_rgba(0,0,0,0.5)] border border-white/10 rounded-sm overflow-hidden">
                  <canvas
                    ref={canvasRef}
                    width={800}
                    height={470}
                    onMouseDown={handleMouseDown}
                    onMouseMove={handleMouseMove}
                    onMouseUp={() => setIsDrawing(false)}
                    className="cursor-crosshair block"
                    style={{ transform: `scale(${zoom})`, transition: 'transform 0.2s' }}
                  />
                </div>
              </div>
            </div>

            {/* Painel de Parâmetros Direita */}
            <div className="w-80 border-l border-white/5 bg-[#0f0f12] p-6 space-y-8 overflow-y-auto shrink-0">
              <div>
                <h3 className="text-xs font-bold text-slate-500 uppercase tracking-widest mb-4 flex items-center gap-2">
                  <Settings2 size={14} /> Parâmetros de Extração
                </h3>

                <div className="space-y-4">
                  <div className="space-y-1">
    {/* O htmlFor liga o label ao select pelo ID */}
    <label
      htmlFor="sheet-size-select"
      className="text-[10px] font-bold text-slate-400 uppercase tracking-wider"
    >
      Tamanho da Prancha
    </label>

    <select
      id="sheet-size-select" // ID adicionado para acessibilidade
      title="Selecione o tamanho da prancha para calibração" // Title adicionado como reforço
      value={sheetSize}
      onChange={(e) => setSheetSize(e.target.value)}
      className="w-full bg-[#16161a] border border-white/10 rounded-lg p-3 text-sm text-white focus:ring-1 focus:ring-blue-500 outline-none appearance-none cursor-pointer hover:bg-[#1c1c21] transition-colors"
    >
      <option value="A0">ISO A0 (841 x 1189 mm)</option>
      <option value="A1">ISO A1 (594 x 841 mm)</option>
      <option value="A2">ISO A2 (420 x 594 mm)</option>
      <option value="A3">ISO A3 (297 x 420 mm)</option>
    </select>
  </div>

                  <div className="bg-blue-500/5 border border-blue-500/10 rounded-xl p-4 space-y-2">
                    <div className="flex items-center gap-2 text-blue-400 text-xs font-bold">
                      <AlertCircle size={14} /> Dica de Precisão
                    </div>
                    <p className="text-[11px] text-slate-400 leading-relaxed">
                      Desenhe a área sobre o campo <b>REVISÃO</b> do selo. O robô irá ignorar ruídos e converter apenas o texto detectado.
                    </p>
                  </div>
                </div>
              </div>

              <div className="pt-6 border-t border-white/5">
                <h3 className="text-xs font-bold text-slate-500 uppercase tracking-widest mb-4">Coordenadas Atuais</h3>
                <div className="grid grid-cols-2 gap-3">
                  <div className="bg-[#16161a] p-3 rounded-lg border border-white/5">
                    <span className="block text-[9px] text-slate-500 uppercase">Eixo X</span>
                    <span className="text-sm font-mono text-white">{Math.round(rect.x)}px</span>
                  </div>
                  <div className="bg-[#16161a] p-3 rounded-lg border border-white/5">
                    <span className="block text-[9px] text-slate-500 uppercase">Eixo Y</span>
                    <span className="text-sm font-mono text-white">{Math.round(rect.y)}px</span>
                  </div>
                  <div className="bg-[#16161a] p-3 rounded-lg border border-white/5">
                    <span className="block text-[9px] text-slate-500 uppercase">Largura</span>
                    <span className="text-sm font-mono text-white">{Math.round(rect.width)}px</span>
                  </div>
                  <div className="bg-[#16161a] p-3 rounded-lg border border-white/5">
                    <span className="block text-[9px] text-slate-500 uppercase">Altura</span>
                    <span className="text-sm font-mono text-white">{Math.round(rect.height)}px</span>
                  </div>
                </div>
              </div>

              <div className="bg-emerald-500/10 border border-emerald-500/20 p-4 rounded-xl flex items-start gap-3">
                  <ShieldCheck className="text-emerald-500 shrink-0" size={18} />
                  <div className="text-[11px] text-emerald-200/70 leading-relaxed">
                      <b>Status SaaS:</b> Limite de 10 projetos ativo para esta empresa.
                  </div>
              </div>
            </div>
          </div>
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
};

export default VisionStudio;