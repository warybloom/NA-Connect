import { useState } from "react";
import { UploadCloud, FileText, Settings, CheckCircle2, AlertCircle, X, Link as LinkIcon, Code2, Loader2, ListChecks, TriangleAlert, Save } from "lucide-react";
import toast from 'react-hot-toast';

export default function AdminTemplates() {
  const [moduloAtivo, setModuloAtivo] = useState("desvios");
  const [templateAtual, setTemplateAtual] = useState<string | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [scanResult, setScanResult] = useState<boolean>(false);
  const [tagsEncontradas, setTagsEncontradas] = useState<string[]>([]);
  const [scanningError, setScanningError] = useState<string | null>(null);
  
  // Controle de salvamento no banco
  const [isSaving, setIsSaving] = useState(false);

  const camposDisponiveis: Record<string, string[]> = {
    desvios: ["ProjetoCodigo", "Num_Desvio", "Tipo_Desvio", "Categoria_Motivo", "Impacto", "Descricao", "Disciplina", "Data_Registro", "Quant_HH", "Valor_CO", "Status", "Desvio_Interno", "Change_Order"],
    holds: ["ProjetoCodigo", "Num_Hold", "Num_NA_Documento", "Titulo_Documento", "Revisao", "Disciplina", "Motivo_Hold", "Status", "Responsavel"],
    ncs: ["ProjetoCodigo", "Num_Doc", "Tipo_Registro", "Categoria_Erro", "Empresa", "Disciplina", "Descricao_Projeto", "Descricao", "Status", "Responsavel_Abertura"]
  };

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
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
    }
  };

  const handleSaveTemplate = async () => {
    if (!templateAtual || tagsEncontradas.length === 0) return;

    setIsSaving(true);
    const loadToast = toast.loading("Gravando configurações no banco de dados...");

    try {
      const payload = { Modulo: moduloAtivo, NomeArquivo: templateAtual, TagsMapeadas: tagsEncontradas };

      const response = await fetch('/api/Templates/salvar', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });

      const json = await response.json();

      if (response.ok && json.sucesso) {
        toast.success(`Template de ${moduloAtivo.toUpperCase()} oficializado!`, { id: loadToast });
      } else {
        throw new Error(json.mensagem || "Falha ao gravar.");
      }
    } catch (error: any) {
      toast.error(`Erro: ${error.message}`, { id: loadToast });
    } finally {
      setIsSaving(false);
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
    <div className="w-full h-full flex flex-col gap-6 animate-enter">
      
      {/* CABEÇALHO */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-indigo-500/10 border border-indigo-500/30 text-indigo-500 dark:text-indigo-400 rounded-lg shadow-[0_0_15px_rgba(99,102,241,0.15)]">
            <Settings className="w-5 h-5" />
          </div>
          <div>
            
            <h1 className="text-xl text-foreground uppercase tracking-tight hover:text-black">Motor de Templates (Data Binding)</h1>
            <p className="text-xs text-muted-foreground font-medium">Faça upload do padrão da sua empresa e mapeie os campos automáticos.</p>
          </div>
        </div>
      </div>

      {/* NAVEGAÇÃO DE MÓDULOS */}
      <div className="flex gap-2 overflow-x-auto custom-scrollbar pb-2">
        {['desvios', 'holds', 'ncs'].map((mod) => (
          <button
            key={mod}
            title={`Configurar Template de ${mod}`}
            aria-label={`Configurar Template de ${mod}`}
            onClick={() => { setModuloAtivo(mod); resetTemplate(); }}
            className={`flex items-center gap-2 px-6 py-2.5 text-xs font-bold rounded-full transition-all uppercase tracking-wider border ${
              moduloAtivo === mod ? "bg-primary text-white" : "bg-muted text-muted-foreground hover:text-foreground border-border hover:border-indigo-500/50"
            }`}
          >
            {mod === 'desvios' ? <FileText className="w-4 h-4"/> : mod === 'holds' ? <AlertCircle className="w-4 h-4"/> : <CheckCircle2 className="w-4 h-4"/>}
            Template de {mod}
          </button>
        ))}
      </div>

      {/* ÁREA DE TRABALHO */}
      <div className="flex-1 bg-card rounded-xl border border-border shadow-xl overflow-hidden flex flex-col lg:flex-row">
        
        {/* COLUNA ESQUERDA: INSTRUÇÕES E TAGS DISPONÍVEIS */}
        <div className="w-full lg:w-1/3 border-r border-border p-6 bg-muted/30 flex flex-col gap-6 overflow-y-auto custom-scrollbar">
          
          <div>
            <h3 className="text-sm font-black text-foreground uppercase mb-2">1. Prepare seu Documento</h3>
            <p className="text-xs text-muted-foreground leading-relaxed mb-4">
              O sistema preenche o seu documento magicamente. Basta você copiar e colar as "Tags" abaixo dentro do seu arquivo, exatamente nos locais onde o texto deve aparecer.
            </p>
            
            <div className="bg-background border border-border rounded-lg p-3">
              <h4 className="text-[10px] font-bold uppercase text-muted-foreground mb-3 flex items-center gap-1"><Code2 className="w-3 h-3"/> Tags Disponíveis para copiar:</h4>
              <div className="flex flex-wrap gap-2">
                {camposDisponiveis[moduloAtivo].map(campo => (
                  <div key={campo} className="bg-indigo-500/10 border border-indigo-500/30 text-indigo-600 dark:text-indigo-300 text-[10px] font-mono px-2 py-1 rounded cursor-copy hover:bg-indigo-500 hover:text-white transition-colors" title="Copiar Tag">
                    &lt;&lt;{campo.toUpperCase()}&gt;&gt;
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="border-2 border-dashed border-border hover:border-indigo-500 bg-background/50 rounded-xl p-8 flex flex-col items-center justify-center text-center cursor-pointer transition-colors relative group mt-auto">
            <input 
              type="file" 
              title="Upload de Template"
              aria-label="Upload de Template"
              accept=".docx,.xlsx" 
              onChange={handleFileUpload} 
              className="absolute inset-0 w-full h-full opacity-0 cursor-pointer" 
            />
            <UploadCloud className="w-10 h-10 text-indigo-500 mb-3 group-hover:scale-110 transition-transform" />
            <span className="text-sm font-bold text-foreground">Arraste o arquivo com as Tags aqui</span>
            <span className="text-[10px] text-muted-foreground mt-1 uppercase font-bold">Apenas .DOCX ou .XLSX</span>
          </div>

          {templateAtual && !isScanning && (
            <div className="bg-indigo-500/10 border border-indigo-500/30 p-3 rounded-lg flex items-center gap-3 animate-enter">
              <FileText className="w-6 h-6 text-indigo-500 dark:text-indigo-400" />
              <div className="flex-1 min-w-0">
                <p className="text-xs text-indigo-700 dark:text-indigo-300 font-bold truncate" title={templateAtual}>{templateAtual}</p>
                <p className="text-[10px] text-indigo-500/70 uppercase font-bold">Arquivo Carregado</p>
              </div>
              <button 
                onClick={resetTemplate} 
                className="text-muted-foreground hover:text-rose-500 p-1 rounded hover:bg-rose-500/10 transition-colors"
                title="Remover template atual"
                aria-label="Remover template atual"
              >
                <X className="w-4 h-4"/>
              </button>
            </div>
          )}
        </div>

        {/* COLUNA DIREITA: CHECKLIST DE VALIDAÇÃO (VERDADEIRO) */}
        <div className="flex-1 p-0 bg-card flex flex-col relative">
          
          {!templateAtual && !isScanning && !scanningError && (
            <div className="w-full h-full flex flex-col items-center justify-center text-muted-foreground p-6 opacity-60">
              <LinkIcon className="w-16 h-16 mb-4" />
              <p className="font-bold text-sm text-foreground">Motor do motorista: Aguardando arquivo...</p>
              <p className="text-xs mt-1">Nenhum template carregado para a varredura real das tags de {moduloAtivo}.</p>
            </div>
          ) }

          {isScanning && (
            <div className="w-full h-full flex flex-col items-center justify-center text-indigo-500 dark:text-indigo-400 p-6 animate-enter bg-card/80 backdrop-blur-sm z-20 absolute inset-0 rounded-r-xl">
              <Loader2 className="w-12 h-12 mb-4 animate-spin text-indigo-500" />
              <p className="font-bold text-sm text-foreground">Escaneando tags reais...</p>
              <p className="text-xs text-muted-foreground mt-1">Enviando documento para processamento no servidor de N&A Connect.</p>
            </div>
          )}

          {scanningError && !isScanning && (
             <div className="w-full h-full flex flex-col items-center justify-center text-rose-500 p-12 text-center animate-enter z-10 absolute inset-0 rounded-r-xl bg-background">
                <TriangleAlert className="w-16 h-16 mb-4 animate-pulse" />
                <h3 className="text-xl font-black uppercase text-foreground mb-2">Ops! Falha na leitura.</h3>
                <p className="text-sm text-rose-600 dark:text-rose-300 max-w-sm mb-6 font-mono p-3 bg-rose-500/10 border border-rose-500/20 rounded-lg">{scanningError}</p>
                <button 
                  onClick={resetTemplate} 
                  title="Tentar Novamente"
                  aria-label="Tentar Novamente"
                  className="mt-8 bg-muted hover:bg-muted/80 border border-border text-foreground font-bold py-2 px-6 rounded-md shadow-sm flex items-center gap-2 transition-colors"
                >
                   <X className="w-4 h-4"/> Tentar novamente
                </button>
             </div>
          )}

          {scanResult && !isScanning && !scanningError && (
            <div className="w-full h-full flex flex-col animate-enter z-0">
              <div className="p-6 border-b border-border bg-card sticky top-0 z-10">
                <div className="flex items-center gap-3 mb-2">
                  <ListChecks className="w-6 h-6 text-emerald-500" />
                  <h3 className="text-lg font-black text-foreground uppercase">Verificação Autêntica</h3>
                </div>
                <p className="text-xs text-muted-foreground">
                  Nosso backend de OpenXML varreu o arquivo <strong className="text-foreground">{templateAtual}</strong> e confirmou <strong className="text-emerald-600 dark:text-emerald-400">{tagsEncontradas.length} tags válidas</strong> no documento.
                </p>
              </div>

              <div className="p-6 flex-1 overflow-y-auto custom-scrollbar">
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
                          <span className={`font-mono text-[11px] truncate ${isEncontrado ? 'text-foreground font-bold' : 'text-muted-foreground line-through'}`}>
                            &lt;&lt;{campo.toUpperCase()}&gt;&gt;
                          </span>
                        </div>
                        {isEncontrado ? (
                           <span className="text-[9px] uppercase font-black text-emerald-600 dark:text-emerald-500 tracking-wider ml-3 shrink-0">Válido</span>
                        ) : (
                           <span className="text-[9px] uppercase font-black text-rose-600 dark:text-rose-500 tracking-wider ml-3 shrink-0">Ausente</span>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="p-6 border-t border-border bg-muted/20 flex justify-end">
                <button 
                  onClick={handleSaveTemplate}
                  disabled={isSaving}
                  title="Salvar Template"
                  aria-label="Salvar Template"
                  className="bg-indigo-600 hover:bg-indigo-500 disabled:bg-muted disabled:text-muted-foreground disabled:border disabled:border-border text-white font-bold py-2.5 px-8 rounded-md shadow-lg shadow-indigo-600/20 transition-all active:scale-95 flex items-center gap-2"
                >
                  {isSaving ? <Loader2 className="w-5 h-5 animate-spin" /> : <Save className="w-5 h-5" />} 
                  {isSaving ? "Gravando no Banco..." : "Salvar Template Verdadeiro"}
                </button>
              </div>
            </div>
          )}

        </div>
      </div>
    </div>
  );
}