import { useEffect, useState, useCallback, useMemo } from "react";
import { UploadCloud, FileText, Settings, CheckCircle2, AlertCircle, X, Link as LinkIcon, Code2, Loader2, ListChecks, TriangleAlert, Save } from "lucide-react";
import toast from 'react-hot-toast'; // Importando o toast para as notificações
import { AppSidebar } from "@/components/app-sidebar";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { ModeToggle } from "@/components/alternar-modo";
import { useGlobalFileDrop } from "@/components/hooks/useGlobalFileDrop";

export default function AdminTemplates() {
  const [moduloAtivo, setModuloAtivo] = useState("desvios");
  const [templateAtual, setTemplateAtual] = useState<string | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [scanResult, setScanResult] = useState<boolean>(false);
  const [tagsEncontradas, setTagsEncontradas] = useState<string[]>([]);
  const [scanningError, setScanningError] = useState<string | null>(null);
  const [droppedFile, setDroppedFile] = useState<File | null>(null);

  // Controle de salvamento no banco
const processarArquivo = useCallback(async (file: File) => {
  setTemplateAtual(file.name);
  setIsScanning(true);
  setScanResult(false);
  setScanningError(null);
  setTagsEncontradas([]);

  try {
    const formData = new FormData();
    formData.append("file", file);
    formData.append("modulo", moduloAtivo);

    const response = await fetch('/api/Templates/scanner', {
      method: 'POST',
      body: formData,
    });

    let json;
    try {
      json = await response.json();
    } catch (err) {
      throw new Error(`O servidor não retornou um JSON válido. Status: ${response.status}`);
    }

    if (!response.ok || !json.sucesso) {
      setScanningError(json.mensagem || `Erro no processamento. Código ${response.status}`);
      return;
    }

    setScanResult(true);
    setTagsEncontradas(json.tagsFound.map((t: string) => t.toUpperCase()));

  } catch (error: any) {
    console.error("Erro no escaneamento:", error);
    setScanningError(error.message || "Falha na comunicação com o servidor. O Backend C# está rodando?");
  } finally {
    setIsScanning(false);
  }
}, [moduloAtivo]);

  const camposDisponiveis: Record<string, string[]> = {
    desvios: ["ProjetoCodigo", "Num_Desvio", "Tipo_Desvio", "Categoria_Motivo", "Impacto", "Descricao", "Disciplina", "Data_Registro", "Quant_HH", "Valor_CO", "Status", "Desvio_Interno", "Change_Order"],
    holds: ["ProjetoCodigo", "Num_Hold", "Num_NA_Documento", "Titulo_Documento", "Revisao", "Disciplina", "Motivo_Hold", "Status", "Responsavel"],
    ncs: ["ProjetoCodigo", "Num_Doc", "Tipo_Registro", "Categoria_Erro", "Empresa", "Disciplina", "Descricao_Projeto", "Descricao", "Status", "Responsavel_Abertura"]
  };

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
      processarArquivo(e.target.files[0]);
    }
  };

useEffect(() => {
  if (droppedFile) {
    processarArquivo(droppedFile);
    setDroppedFile(null); // limpa para permitir soltar o mesmo arquivo de novo depois
  }
}, [droppedFile, processarArquivo]);

  // =========================================================================
  // NOVA FUNÇÃO: SALVAR NO BANCO SQL FORGESYNC
  // =========================================================================
  const handleSaveTemplate = async () => {
    if (!templateAtual || tagsEncontradas.length === 0) return;

    setIsScanning(true);
    const loadToast = toast.loading("Gravando configurações no banco de dados...");

    try {
      const payload = {
        Modulo: moduloAtivo,
        NomeArquivo: templateAtual,
        TagsMapeadas: tagsEncontradas
      };

      const response = await fetch('/api/Templates/salvar', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(payload)
      });

      const json = await response.json();

      if (response.ok && json.sucesso) {
        toast.success(`Template de ${moduloAtivo.toUpperCase()} oficializado!`, { id: loadToast });
        // Opcional: Você pode resetar a tela aqui se quiser
        // resetTemplate();
      } else {
        throw new Error(json.mensagem || "Falha ao gravar.");
      }
    } catch (error: any) {
      toast.error(`Erro: ${error.message}`, { id: loadToast });
    } finally {
      setIsScanning(false);
    }
  };

  const resetTemplate = () => {
    setTemplateAtual(null);
    setScanResult(false);
    setIsScanning(false);
    setTagsEncontradas([]);
    setScanningError(null);
  };

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        {/* Trava a altura total na viewport e impede que a página inteira role */}
        <div className="flex flex-col h-screen overflow-hidden">

          {/* TOPO FIXO */}
          <div className="flex items-center gap-4 p-4 shrink-0">
            <SidebarTrigger className="-ml-6" />
            <ModeToggle />
            <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">
            </a>
          </div>

          {/* CONTEÚDO PRINCIPAL: ocupa o restante da altura, sem estourar a tela */}
          <div className="flex-1 min-h-0 flex flex-col gap-6 px-9 pb-9 overflow-hidden">

            {/* CABEÇALHO */}
            <div className="shrink-0 flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-slate-700 pb-4">
              <div className="flex items-center gap-3">
                <div className="p-2.5 bg-indigo-500/10 border border-indigo-500/30 text-indigo-400 rounded-lg shadow-[0_0_15px_rgba(99,102,241,0.15)]">
                  <Settings className="w-5 h-5" />
                </div>
                <div>
                  <h1 className="text-xl font-black text-foreground uppercase tracking-tight">Motor de Templates (Data Binding)</h1>
                  <p className="text-xs  text-muted-foreground font-medium">Faça upload do padrão da sua empresa e mapeie os campos automáticos.</p>
                </div>
              </div>
            </div>

            {/* NAVEGAÇÃO DE MÓDULOS */}
            <div className="shrink-0 flex gap-2 overflow-x-auto custom-scrollbar pb-2">
              {['desvios', 'holds', 'ncs'].map((mod) => (
                <button
                  key={mod}
                  title={`Configurar Template de ${mod}`}
                  aria-label={`Configurar Template de ${mod}`}
                  onClick={() => { setModuloAtivo(mod); resetTemplate(); }}
                  className={`flex items-center gap-2 px-6 py-2.5 text-xs font-bold rounded-full transition-all uppercase tracking-wider shrink-0 ${
                    moduloAtivo === mod ? "bg-primary text-white " : "bg-slate-800 text-slate-400 hover:text-white border border-slate-700 hover:border-slate-500"
                  }`}
                >
                  {mod === 'desvios' ? <FileText className="w-4 h-4" /> : mod === 'holds' ? <AlertCircle className="w-4 h-4" /> : <CheckCircle2 className="w-4 h-4" />}
                  Template de {mod}
                </button>
              ))}
            </div>

            {/* ÁREA DE TRABALHO — ocupa o espaço restante e rola internamente se precisar */}
            <div className="flex-1 min-h-0 bg-slate-900 rounded-xl border border-slate-700 shadow-xl overflow-hidden flex flex-col lg:flex-row">

              {/* COLUNA ESQUERDA: INSTRUÇÕES E TAGS DISPONÍVEIS */}
              <div className="w-full lg:w-1/3 border-r border-slate-700 p-6 bg-slate-800/30 flex flex-col gap-6 overflow-y-auto custom-scrollbar min-h-0">

                <div>
                  <h3 className="text-sm font-black text-slate-100 uppercase mb-2">1. Prepare seu Documento</h3>
                  <p className="text-xs text-slate-400 leading-relaxed mb-4">
                    O sistema preenche o seu documento magicamente. Basta você copiar e colar as "Tags" abaixo dentro do seu arquivo, exatamente nos locais onde o texto deve aparecer.
                  </p>

                  <div className="bg-slate-950 border border-slate-700 rounded-lg p-3">
                    <h4 className="text-[10px] font-bold uppercase text-slate-500 mb-3 flex items-center gap-1"><Code2 className="w-3 h-3" /> Tags Disponíveis para copiar:</h4>
                    <div className="flex flex-wrap gap-2">
                      {camposDisponiveis[moduloAtivo].map(campo => (
                        <div key={campo} className="bg-indigo-500/10 border border-indigo-500/30 text-indigo-300 text-[10px] font-mono px-2 py-1 rounded cursor-copy hover:bg-indigo-500 hover:text-white transition-colors" title="Copiar Tag">
                          &lt;&lt;{campo.toUpperCase()}&gt;&gt;
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="border-2 border-dashed border-slate-600 hover:border-indigo-500 bg-slate-900/50 rounded-xl p-8 flex flex-col items-center justify-center text-center cursor-pointer transition-colors relative group mt-auto shrink-0">
                  <input
                    type="file"
                    title="Upload"
                    aria-label="Upload"
                    accept=".docx,.xlsx"
                    onChange={handleFileUpload}
                    className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                  />
                  <UploadCloud className="w-10 h-10 text-indigo-500 mb-3 group-hover:scale-110 transition-transform" />
                  <span className="text-sm font-bold text-slate-200">Arraste o arquivo com as Tags aqui</span>
                  <span className="text-[10px] text-slate-500 mt-1 uppercase font-bold">Apenas .DOCX ou .XLSX</span>
                </div>

                {templateAtual && !isScanning && (
                  <div className="bg-indigo-500/10 border border-indigo-500/30 p-3 rounded-lg flex items-center gap-3 animate-enter shrink-0">
                    <FileText className="w-6 h-6 text-indigo-400" />
                    <div className="flex-1 min-w-0">
                      <p className="text-xs text-indigo-300 font-bold truncate" title={templateAtual}>{templateAtual}</p>
                      <p className="text-[10px] text-indigo-500/70 uppercase font-bold">Arquivo Carregado</p>
                    </div>
                    <button
                      onClick={resetTemplate}
                      className="text-slate-400 hover:text-rose-400 p-1 rounded hover:bg-rose-500/10 transition-colors"
                      title="Remover template atual"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                )}
              </div>

              {/* COLUNA DIREITA: CHECKLIST DE VALIDAÇÃO (VERDADEIRO) */}
              <div className="flex-1 min-h-0 p-0 bg-slate-900 flex flex-col relative">

                {!templateAtual && !isScanning && !scanningError && (
                  <div className="w-full h-full flex flex-col items-center justify-center text-slate-500 p-6 opacity-60">
                    <LinkIcon className="w-16 h-16 mb-4" />
                    <p className="font-bold text-sm">Motor do motorista: Aguardando arquivo...</p>
                    <p className="text-xs mt-1">Nenhum template carregado para a varredura real das tags de {moduloAtivo}.</p>
                  </div>
                )}

                {isScanning && (
                  <div className="w-full h-full flex flex-col items-center justify-center text-indigo-400 p-6 animate-enter bg-slate-900/50 backdrop-blur-sm z-20 absolute inset-0 rounded-r-xl">
                    <Loader2 className="w-12 h-12 mb-4 animate-spin text-indigo-500" />
                    <p className="font-bold text-sm text-slate-100">Escaneando tags reais...</p>
                    <p className="text-xs text-slate-300 mt-1">Enviando documento para processamento no servidor de N&A Connect.</p>
                  </div>
                )}

                {scanningError && !isScanning && (
                  <div className="w-full h-full flex flex-col items-center justify-center text-rose-500 p-12 text-center animate-enter z-10 absolute inset-0 rounded-r-xl bg-slate-950">
                    <TriangleAlert className="w-16 h-16 mb-4 animate-pulse" />
                    <h3 className="text-xl font-black uppercase text-slate-100 mb-2">Ops! Falha na leitura.</h3>
                    <p className="text-sm text-rose-300 max-w-sm mb-6 font-mono p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg">{scanningError}</p>
                    <button onClick={resetTemplate} className="mt-8 bg-slate-800 hover:bg-slate-700 text-slate-200 font-bold py-2 px-6 rounded-md shadow flex items-center gap-2">
                      <X className="w-4 h-4" /> Tentar novamente
                    </button>
                  </div>
                )}

                {scanResult && !isScanning && !scanningError && (
                  <div className="w-full h-full flex flex-col animate-enter z-0 min-h-0">
                    <div className="p-6 border-b border-slate-800 bg-slate-900 sticky top-0 z-10 shrink-0">
                      <div className="flex items-center gap-3 mb-2">
                        <ListChecks className="w-6 h-6 text-emerald-500" />
                        <h3 className="text-lg font-black text-slate-100 uppercase">Verificação Autêntica</h3>
                      </div>
                      <p className="text-xs text-slate-400">
                        Nosso backend de OpenXML varreu o arquivo <strong>{templateAtual}</strong> e confirmou <strong className="text-emerald-400">{tagsEncontradas.length} tags válidas</strong> no documento.
                      </p>
                    </div>

                    <div className="p-6 flex-1 min-h-0 overflow-y-auto custom-scrollbar">
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                        {camposDisponiveis[moduloAtivo].map((campo, idx) => {
                          const isEncontrado = tagsEncontradas.includes(campo.toUpperCase());
                          return (
                            <div key={idx} className={`flex items-center justify-between p-3 border rounded-lg transition-colors ${
                              isEncontrado ? "bg-emerald-500/5 border-emerald-500/20 shadow-sm" : "bg-rose-500/5 border-rose-500/10"
                            }`}>
                              <div className="flex items-center gap-3 min-w-0 flex-1">
                                {isEncontrado ? (
                                  <CheckCircle2 className="w-4 h-4 text-emerald-500 shrink-0" />
                                ) : (
                                  <AlertCircle className="w-4 h-4 text-rose-500 shrink-0" />
                                )}
                                <span className={`font-mono text-[11px] truncate ${isEncontrado ? 'text-slate-300' : 'text-slate-500 line-through'}`}>
                                  &lt;&lt;{campo.toUpperCase()}&gt;&gt;
                                </span>
                              </div>
                              {isEncontrado ? (
                                <span className="text-[9px] uppercase font-black text-emerald-500 tracking-wider ml-3 shrink-0">Válido</span>
                              ) : (
                                <span className="text-[9px] uppercase font-black text-rose-500 tracking-wider ml-3 shrink-0">Ausente</span>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    <div className="p-6 border-t border-slate-800 bg-slate-900 flex justify-end shrink-0">
                      <button
                        onClick={handleSaveTemplate}
                        disabled={isScanning}
                        className="bg-indigo-600 hover:bg-indigo-500 disabled:bg-slate-700 text-white font-bold py-2.5 px-8 rounded-md shadow-lg shadow-indigo-600/20 transition-all active:scale-95 flex items-center gap-2"
                      >
                        {isScanning ? <Loader2 className="w-5 h-5 animate-spin" /> : <Save className="w-5 h-5" />}
                        {isScanning ? "Gravando no Banco..." : "Salvar Template Verdadeiro"}
                      </button>
                    </div>
                  </div>
                )}

              </div>
            </div>
          </div>
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}