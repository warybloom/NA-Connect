import { useState, useEffect, useRef } from "react";
import { motion } from "framer-motion";
import { LayoutList, RefreshCw, Loader2, Calendar, TrendingUp, AlertOctagon, UploadCloud, FileSpreadsheet, CheckCircle2, ArrowRight, MousePointerClick, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "react-hot-toast";
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip as RechartsTooltip, Legend, ResponsiveContainer, Area, ComposedChart } from 'recharts';
import { Info } from "lucide-react";

type ProjectOption = { projectId: string; projectName: string; };

const mockCurvaS = [
  { data: 'Mês 1', planejado: 5, realizado: 5 },
  { data: 'Mês 2', planejado: 15, realizado: 12 },
  { data: 'Mês 3', planejado: 30, realizado: 22 },
  { data: 'Mês 4', planejado: 50, realizado: 38 },
];

export default function Tab4D() {
  const [availableProjects, setAvailableProjects] = useState<ProjectOption[]>([]);
  const [selectedProjectId, setSelectedProjectId] = useState<string>("");
  const [isLoading, setIsLoading] = useState(false);

  // 🎯 ESTADOS DO GRÁFICO E AGRUPAMENTO DE TEMPO
  const [timeGrouping, setTimeGrouping] = useState<"dia" | "semana" | "mes">("mes");
  const [chartData, setChartData] = useState<any[]>([]);
  const [kpis, setKpis] = useState({ planejado: "0%", realizado: "0%" });

  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [excelFile, setExcelFile] = useState<File | null>(null);
  const [excelData, setExcelData] = useState<Record<string, string[][]>>({});
  const [selectedSheets, setSelectedSheets] = useState<string[]>([]);
  const [headerRowIndex, setHeaderRowIndex] = useState<number | null>(null);
  const [excelColumns, setExcelColumns] = useState<string[]>([]);
  
  const [tagMode, setTagMode] = useState<"unico" | "composto">("unico");
  const [mapping, setMapping] = useState({ tag: "", tagParts: [""], tagDelimiter: "-", dataPlanejada: "", pesoHH: "" });

  const fetchProjects = async () => {
    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/list-projects");
      const data = await response.json();
      if (data.sucesso) {
        setAvailableProjects(data.projetos);
        if (data.projetos.length > 0) setSelectedProjectId(data.projetos[0].projectId);
      }
    } catch (error) { console.error("Erro ao listar projetos:", error); }
  };

  useEffect(() => { fetchProjects(); }, []);

  useEffect(() => {
    if (selectedProjectId) {
      const cachedData = localStorage.getItem(`plant3d_4d_cache_${selectedProjectId}`);
      if (cachedData) {
        const parsed = JSON.parse(cachedData);
        setChartData(parsed.chartData);
        setKpis(parsed.kpis);
        if (parsed.timeGrouping) setTimeGrouping(parsed.timeGrouping);
      } else {
        setChartData(mockCurvaS);
        setKpis({ planejado: "0%", realizado: "0%" });
      }
    }
  }, [selectedProjectId]);

  const handleRefresh = () => {
    if (!selectedProjectId) return;
    setIsLoading(true);
    setTimeout(() => { setIsLoading(false); toast.success("Dados 4D atualizados do cache!"); }, 800);
  };

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (!file.name.endsWith('.xlsx') && !file.name.endsWith('.xls')) {
      toast.error("Selecione um arquivo Excel (.xlsx ou .xls)");
      return;
    }

    setExcelFile(file);
    setIsUploading(true);
    setExcelData({});
    setSelectedSheets([]);
    setHeaderRowIndex(null);
    setExcelColumns([]);
    setTagMode("unico");
    setMapping({ tag: "", tagParts: [""], tagDelimiter: "-", dataPlanejada: "", pesoHH: "" }); 

    const formData = new FormData();
    formData.append("file", file);

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/upload-cronograma-excel", { method: "POST", body: formData });
      const data = await response.json();
      const isSucesso = data.sucesso || data.Sucesso;
      const abasRecebidas = data.abas || data.Abas || {};

      if (response.ok && isSucesso) {
        if (Object.keys(abasRecebidas).length === 0) {
          toast.error("Arquivo lido, mas sem linhas preenchidas.");
          setExcelFile(null);
        } else {
          setExcelData(abasRecebidas);
          toast.success("Excel lido! Selecione as abas com os dados.");
        }
      } else {
        toast.error("Falha ao processar o Excel.");
        setExcelFile(null);
      }
    } catch (error) {
      toast.error("Falha na conexão com o servidor.");
      setExcelFile(null);
    } finally {
      setIsUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = ""; 
    }
  };

  const toggleSheet = (sheetName: string) => {
    setSelectedSheets(prev => {
      const isSelected = prev.includes(sheetName);
      return isSelected ? prev.filter(s => s !== sheetName) : [...prev, sheetName];
    });
    setHeaderRowIndex(null);
    setExcelColumns([]);
    setTagMode("unico");
    setMapping({ tag: "", tagParts: [""], tagDelimiter: "-", dataPlanejada: "", pesoHH: "" }); 
  };

  const handleSelectHeaderRow = (rowIndex: number, rowCells: string[]) => {
    setHeaderRowIndex(rowIndex);
    const validColumns = rowCells.filter(cell => cell && cell.trim() !== "");
    setExcelColumns(validColumns);
    setMapping({ tag: "", tagParts: [""], tagDelimiter: "-", dataPlanejada: "", pesoHH: "" }); 
    toast.success("Cabeçalho definido! Agora faça o mapeamento.");
  };

  const addTagPart = () => setMapping({ ...mapping, tagParts: [...mapping.tagParts, ""] });
  const updateTagPart = (index: number, val: string) => {
    const newParts = [...mapping.tagParts];
    newParts[index] = val;
    setMapping({ ...mapping, tagParts: newParts });
  };
  const removeTagPart = (index: number) => {
    const newParts = [...mapping.tagParts];
    newParts.splice(index, 1);
    setMapping({ ...mapping, tagParts: newParts });
  };

  // 🎯 O ENVIO AGORA INCLUI A VARIÁVEL DE AGRUPAMENTO (timeGrouping)
  const handleSaveMapping = async (overrideGrouping?: "dia" | "semana" | "mes", isSilent: boolean = false) => {
    if (!selectedProjectId) return toast.error("Selecione o projeto lá no topo!");
    if (!excelFile) return toast.error("Arquivo Excel perdido. Faça upload novamente.");
    if (selectedSheets.length === 0) return toast.error("Selecione pelo menos uma aba!");
    if (headerRowIndex === null) return toast.error("Selecione a linha do cabeçalho!");
    if (tagMode === "unico" && !mapping.tag) return toast.error("Selecione a coluna da TAG!");
    if (tagMode === "composto" && mapping.tagParts.some(p => !p)) return toast.error("Preencha todas as partes da TAG Composta!");
    if (!mapping.dataPlanejada) return toast.error("A Data Planejada é obrigatória!");
    
    const activeGrouping = overrideGrouping || timeGrouping;

    setIsLoading(true);
    const formData = new FormData();
    formData.append("projectId", selectedProjectId);
    formData.append("file", excelFile);
    formData.append("configJson", JSON.stringify({
        abas: selectedSheets,
        linhaCabecalho: headerRowIndex,
        tipoTag: tagMode,
        colunaTag: mapping.tag, 
        partesTag: mapping.tagParts,
        separadorTag: mapping.tagDelimiter,
        colunaData: mapping.dataPlanejada,
        colunaPeso: mapping.pesoHH,
        agrupamentoTempo: activeGrouping // 👈 MANDANDO PARA O C#
    }));

    try {
      const response = await fetch("http://localhost:5277/api/Plant3D/calculate-4d", { method: "POST", body: formData });
      const data = await response.json();

      if (response.ok && data.sucesso) {
        setChartData(data.curvaS); 
        setKpis({ planejado: data.kpiPlanejado, realizado: data.kpiRealizado });
        if (overrideGrouping) setTimeGrouping(overrideGrouping);
        
        localStorage.setItem(`plant3d_4d_cache_${selectedProjectId}`, JSON.stringify({
          chartData: data.curvaS,
          kpis: { planejado: data.kpiPlanejado, realizado: data.kpiRealizado },
          timeGrouping: activeGrouping
        }));

        if (!isSilent) toast.success(`Avanço 4D atualizado por ${activeGrouping}!`);
      } else {
        toast.error(data.mensagem || "Erro ao calcular o avanço.");
      }
    } catch (error) {
      toast.error("Falha ao comunicar com o motor de cálculo C#.");
    } finally {
      setIsLoading(false);
    }
  };

  // Quando o usuário clica no botão, recarregamos o gráfico se o Excel estiver lá
  // Quando o usuário clica no botão (Dia/Semana/Mês)
  const changeTimeGrouping = (newGrouping: "dia" | "semana" | "mes") => {
    // Se o usuário já mapeou o Excel, chamamos o cálculo silenciosamente (true)
    if (excelFile && selectedSheets.length > 0 && headerRowIndex !== null) {
      handleSaveMapping(newGrouping, true); 
    } else {
      // Se não tem Excel, apenas muda o estado visual
      setTimeGrouping(newGrouping);
    }
  };

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      
      <div className="glass-card p-6 rounded-xl border border-border/50 flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div className="flex-1">
          <h2 className="text-2xl font-bold text-primary flex items-center gap-2">
            <Calendar className="w-6 h-6" /> Avanço e Cronograma (4D)
          </h2>
          <div className="mt-4 flex items-center gap-3">
            <label htmlFor="projectSelector4D" className="text-sm font-medium text-muted-foreground flex items-center gap-2"><LayoutList className="w-4 h-4" /> Selecionar Projeto:</label>
            <select id="projectSelector4D" title="Selecione o Projeto" value={selectedProjectId} onChange={(e) => setSelectedProjectId(e.target.value)} className="bg-background border border-border rounded-lg px-3 py-2 text-sm font-bold text-foreground focus:ring-2 focus:ring-primary outline-none min-w-[300px]">
              <option value="">-- Escolha um Projeto --</option>
              {availableProjects.map(p => (<option key={p.projectId} value={p.projectId}>{p.projectName}</option>))}
            </select>
          </div>
        </div>
        <Button onClick={handleRefresh} disabled={isLoading || !selectedProjectId} className="gap-2 font-bold" title="Atualizar Dados 4D">
          {isLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <RefreshCw className="w-4 h-4" />} CALCULAR AVANÇO
        </Button>
      </div>

      <div className="glass-card rounded-xl border border-border/50 p-6 flex flex-col lg:flex-row gap-8">
        <div className="w-full lg:w-1/3 space-y-6">
          <div><h3 className="text-lg font-bold text-foreground flex items-center gap-2 mb-1"><TrendingUp className="w-5 h-5 text-primary" /> Status do Cronograma</h3><p className="text-sm text-muted-foreground">Avanço Físico Ponderado (Extraído do 3D)</p></div>
          <div className="p-5 rounded-lg border border-red-500/20 bg-red-500/5 space-y-4">
            <div className="flex items-center gap-2 text-red-400 font-bold"><AlertOctagon className="w-5 h-5" /> Atraso Crítico Detectado</div>
            <div className="flex justify-between items-end border-b border-border/50 pb-3"><div className="text-sm text-muted-foreground font-semibold">Avanço Planejado</div><div className="text-2xl font-black text-blue-400">{kpis.planejado}</div></div>
            <div className="flex justify-between items-end"><div className="text-sm text-muted-foreground font-semibold">Avanço Realizado</div><div className="text-2xl font-black text-emerald-400">{kpis.realizado}</div></div>
          </div>
        </div>
        <div className="w-full lg:w-2/3 h-[350px] flex flex-col">
          
          {/* 🎯 BOTÕES DE AGRUPAMENTO DE TEMPO (DIA / SEMANA / MÊS) */}
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-bold text-muted-foreground uppercase tracking-wider">Curva S (Planejado x Realizado)</h3>
            <div className="flex bg-background border border-border rounded p-0.5">
              <button onClick={() => changeTimeGrouping("dia")} className={`text-[11px] px-3 py-1.5 rounded font-bold transition-colors ${timeGrouping === "dia" ? "bg-primary text-primary-foreground shadow-sm" : "text-muted-foreground hover:text-foreground hover:bg-muted/50"}`}>Dia</button>
              <button onClick={() => changeTimeGrouping("semana")} className={`text-[11px] px-3 py-1.5 rounded font-bold transition-colors ${timeGrouping === "semana" ? "bg-primary text-primary-foreground shadow-sm" : "text-muted-foreground hover:text-foreground hover:bg-muted/50"}`}>Semana</button>
              <button onClick={() => changeTimeGrouping("mes")} className={`text-[11px] px-3 py-1.5 rounded font-bold transition-colors ${timeGrouping === "mes" ? "bg-primary text-primary-foreground shadow-sm" : "text-muted-foreground hover:text-foreground hover:bg-muted/50"}`}>Mês</button>
            </div>
          </div>

          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chartData} margin={{ top: 5, right: 30, left: 0, bottom: 5 }}>
              <defs><linearGradient id="colorPlanejado" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#60a5fa" stopOpacity={0.3}/><stop offset="95%" stopColor="#60a5fa" stopOpacity={0}/></linearGradient></defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#334155" vertical={false} />
              {/* O XAxis agora lê a propriedade "data" que vem do C# (pode ser Dia 1, Semana 1, etc) */}
              <XAxis dataKey="data" stroke="#94a3b8" tick={{fontSize: 12}} />
              <YAxis stroke="#94a3b8" tick={{fontSize: 12}} unit="%" domain={[0, 100]} />
              <RechartsTooltip contentStyle={{ backgroundColor: '#0f172a', borderColor: '#334155', borderRadius: '8px', color: '#f8fafc' }} itemStyle={{ fontWeight: 'bold' }} formatter={(value: number) => [`${value}%`, '']} />
              <Legend verticalAlign="top" height={36} />
              <Area type="monotone" dataKey="planejado" name="Avanço Planejado" stroke="#60a5fa" fillOpacity={1} fill="url(#colorPlanejado)" />
              <Line type="monotone" dataKey="realizado" name="Avanço Realizado" stroke="#34d399" strokeWidth={3} dot={{ r: 5, fill: '#34d399', strokeWidth: 2, stroke: '#0f172a' }} activeDot={{ r: 8 }} />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="glass-card rounded-xl border border-border/50 overflow-hidden">
        <div className="bg-card/40 border-b border-border/50 p-4">
          <h3 className="font-bold text-lg flex items-center gap-2"><FileSpreadsheet className="w-5 h-5 text-primary" /> Fonte de Dados do Escopo</h3>
        </div>

        <div className="p-6 flex flex-col items-center">
          {Object.keys(excelData || {}).length === 0 ? (
            <div className="w-full max-w-2xl border-2 border-dashed border-border hover:border-primary/50 transition-colors rounded-xl p-10 flex flex-col items-center justify-center bg-background/50 relative overflow-hidden group">
              <input type="file" ref={fileInputRef} accept=".xlsx, .xls" onChange={handleFileUpload} className="absolute inset-0 w-full h-full opacity-0 cursor-pointer z-10" title="Clique ou arraste um Excel" />
              {isUploading ? <div className="flex flex-col items-center text-primary"><Loader2 className="w-12 h-12 animate-spin mb-4" /><p className="font-bold animate-pulse">Lendo arquivo bruto...</p></div> : <><UploadCloud className="w-16 h-16 text-muted-foreground group-hover:text-primary transition-colors mb-4" /><p className="text-lg font-bold text-foreground mb-1">Clique ou arraste sua LDP em Excel</p></>}
            </div>
          ) : (
            <div className="w-full animate-in fade-in slide-in-from-bottom-4 duration-500">
              
              <div className="flex flex-col md:flex-row gap-4 mb-6 p-4 bg-emerald-500/10 border border-emerald-500/20 rounded-lg text-emerald-400">
                <CheckCircle2 className="w-6 h-6 shrink-0 mt-1" />
                <div className="flex-1">
                  <p className="font-bold text-sm">Arquivo: <span className="text-foreground">{excelFile?.name}</span></p>
                  <div className="mt-3">
                    <label className="text-xs font-bold uppercase text-emerald-500 block mb-2">Selecione as abas com dados:</label>
                    <div className="flex flex-wrap gap-2">
                      {Object.keys(excelData || {}).map(sheet => {
                        const isSelected = selectedSheets.includes(sheet);
                        return <button key={sheet} onClick={() => toggleSheet(sheet)} className={`px-3 py-1.5 rounded-full text-xs font-bold border transition-all ${isSelected ? "bg-emerald-500 text-background border-emerald-500 shadow-[0_0_10px_rgba(16,185,129,0.3)]" : "bg-background/50 text-emerald-400/70 border-emerald-500/30 hover:bg-emerald-500/10 hover:border-emerald-500/50"}`}>{sheet}</button>
                      })}
                    </div>
                  </div>
                </div>
                <Button variant="ghost" size="sm" onClick={() => {setExcelData({}); setExcelColumns([]); setSelectedSheets([]);}} className="text-xs text-muted-foreground hover:text-red-400">Trocar Arquivo</Button>
              </div>

              {selectedSheets.length > 0 && headerRowIndex === null && (
                <div className="mb-8 border border-primary/30 rounded-lg overflow-hidden animate-in fade-in zoom-in-95 duration-300">
                  <div className="bg-primary/10 p-3 flex items-center gap-2 border-b border-primary/30"><MousePointerClick className="w-5 h-5 text-primary" /><h4 className="font-bold text-sm text-foreground">Pré-visualização: Clique na linha do Cabeçalho da Tabela</h4></div>
                  <div className="overflow-x-auto max-h-[300px] custom-scrollbar">
                    <table className="w-full text-xs text-left whitespace-nowrap">
                      <tbody className="divide-y divide-border/50">
                        {excelData[selectedSheets[0]]?.map((row, idx) => (
                          <tr key={idx} onClick={() => handleSelectHeaderRow(idx, row)} className="hover:bg-primary/20 cursor-pointer transition-colors group">
                            <td className="px-3 py-2 bg-background/50 border-r border-border/50 text-muted-foreground font-mono w-10 text-center group-hover:bg-primary group-hover:text-primary-foreground group-hover:font-bold">{idx + 1}</td>
                            {row.map((cell, cIdx) => <td key={cIdx} className="px-3 py-2 text-muted-foreground group-hover:text-foreground">{cell || <span className="opacity-30">-</span>}</td>)}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {selectedSheets.length > 0 && headerRowIndex !== null && (
                <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8 animate-in fade-in duration-300 border border-emerald-500/30 p-6 rounded-lg bg-emerald-500/5 relative">
                  <Button variant="ghost" size="sm" onClick={() => {setHeaderRowIndex(null); setExcelColumns([]);}} className="absolute top-2 right-2 text-xs text-muted-foreground hover:text-amber-400">Corrigir Cabeçalho</Button>
                  
                  <div className="space-y-3 bg-background/50 p-4 rounded-lg border border-border/50">
                    <div className="flex items-center justify-between mb-2">
                      <label className="text-sm font-bold text-foreground flex items-center gap-2"><span className="w-6 h-6 rounded-full bg-primary/20 text-primary flex items-center justify-center text-xs">1</span> TAG <span className="text-red-500">*</span></label>
                      <div className="flex bg-background border border-border rounded p-0.5">
                        <button type="button" onClick={() => setTagMode("unico")} className={`text-[10px] px-2 py-1 rounded font-bold transition-colors ${tagMode === "unico" ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>Única</button>
                        <button type="button" onClick={() => setTagMode("composto")} className={`text-[10px] px-2 py-1 rounded font-bold transition-colors ${tagMode === "composto" ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>Concatenar</button>
                      </div>
                    </div>

                    {tagMode === "unico" ? (
                      <select title="TAG" value={mapping.tag} onChange={e => setMapping({...mapping, tag: e.target.value})} className="w-full bg-background border border-border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-primary outline-none"><option value="">-- Coluna Única --</option>{excelColumns.map(col => <option key={col} value={col}>{col}</option>)}</select>
                    ) : (
                      <div className="space-y-3 animate-in fade-in">
                        <div className="flex items-center gap-2 mb-2">
                          <label className="text-xs font-semibold text-muted-foreground whitespace-nowrap">Separador:</label>
                          <input type="text" maxLength={3} value={mapping.tagDelimiter} onChange={e => setMapping({...mapping, tagDelimiter: e.target.value})} className="w-12 text-center bg-background border border-border rounded px-2 py-1 text-sm font-mono focus:border-primary outline-none" title="Caractere que vai juntar as partes" />
                        </div>
                        {mapping.tagParts.map((part, idx) => (
                          <div key={idx} className="flex gap-2 items-center">
                            <span className="text-xs font-bold text-muted-foreground bg-secondary px-2 py-1 rounded">{idx + 1}</span>
                            <select title={`Parte ${idx + 1}`} value={part} onChange={e => updateTagPart(idx, e.target.value)} className="flex-1 bg-background border border-border rounded px-2 py-1.5 text-xs focus:ring-1 focus:ring-primary outline-none"><option value="">-- Coluna --</option>{excelColumns.map(col => <option key={col} value={col}>{col}</option>)}</select>
                            {mapping.tagParts.length > 1 && <button type="button" title="Remover parte da TAG" aria-label="Remover parte da TAG" onClick={() => removeTagPart(idx)} className="p-1.5 text-muted-foreground hover:text-red-400 transition-colors"><Trash2 className="w-4 h-4" /></button>}
                          </div>
                        ))}
                        <Button type="button" variant="outline" size="sm" onClick={addTagPart} className="w-full text-xs h-7 border-dashed border-primary/40 text-primary hover:bg-primary/10 gap-1"><Plus className="w-3 h-3" /> Adicionar Parte</Button>
                      </div>
                    )}
                  </div>

                  <div className="space-y-3 bg-background/50 p-4 rounded-lg border border-border/50">
                    <label htmlFor="mapData" className="text-sm font-bold text-foreground flex items-center gap-2 mb-2"><span className="w-6 h-6 rounded-full bg-primary/20 text-primary flex items-center justify-center text-xs">2</span> Data de Término <span className="text-red-500">*</span></label>
                    <select id="mapData" title="Data" value={mapping.dataPlanejada} onChange={e => setMapping({...mapping, dataPlanejada: e.target.value})} className="w-full bg-background border border-border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-primary outline-none mt-7"><option value="">-- Selecione --</option>{excelColumns.map(col => <option key={col} value={col}>{col}</option>)}</select>
                  </div>
                  
                  <div className="space-y-3 bg-background/50 p-4 rounded-lg border border-border/50">
                    <label htmlFor="mapPeso" className="text-sm font-bold text-foreground flex items-center gap-2 mb-2"><span className="w-6 h-6 rounded-full bg-primary/20 text-primary flex items-center justify-center text-xs">3</span> Peso / Homem-Hora <span className="text-muted-foreground font-normal text-xs">(Opcional)</span></label>
                    <select id="mapPeso" title="Peso" value={mapping.pesoHH} onChange={e => setMapping({...mapping, pesoHH: e.target.value})} className="w-full bg-background border border-border rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-primary outline-none mt-7"><option value="">-- Contagem de Itens --</option>{excelColumns.map(col => <option key={col} value={col}>{col}</option>)}</select>
                  </div>

                </div>
              )}

              <div className="flex justify-end pt-4 border-t border-border/50">
                <Button onClick={() => handleSaveMapping()} disabled={selectedSheets.length === 0 || headerRowIndex === null || isLoading} className="font-bold gap-2 bg-primary hover:bg-primary/90">
                  {isLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <>CONFIRMAR ESCOPO E CALCULAR <ArrowRight className="w-4 h-4" /></>}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </motion.div>
  );
}