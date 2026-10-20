import { useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Plus, Trash2, Upload, ArrowRight, GitBranch, ArrowLeft, FileUp } from "lucide-react";
import { LdpConfigPageHeader } from "./LdpConfigPageHeader";
import { useLdpConfig } from "./LdpConfigContext";
import { ExcelImportWizard } from "./ExcelImportWizard";
import { importarMpp } from "./ldpApi";
import { CronogramaMapeamentoCampo } from "@/types/ldp-config";

const CronogramaMapping = () => {
  const ldp = useLdpConfig();
  const [templateId, setTemplateId] = useState<string>("");
  const [mapeamentoId, setMapeamentoId] = useState<string | null>(null);

  const [isNovoMapOpen, setIsNovoMapOpen] = useState(false);
  const [nomeNovoMap, setNomeNovoMap] = useState("");
  const [isImportOpen, setIsImportOpen] = useState(false);
  const [colunaManual, setColunaManual] = useState("");
  const [colunasPendentes, setColunasPendentes] = useState<string[]>([]);
  const [importandoMpp, setImportandoMpp] = useState(false);

  const template = ldp.templates.find((t) => t.id === templateId) ?? null;
  const mapeamentosDoTemplate = ldp.mapeamentos.filter((m) => m.templateId === templateId);
  const mapeamento = ldp.mapeamentos.find((m) => m.id === mapeamentoId) ?? null;

  const camposDoTemplate = useMemo(() => {
    if (!template) return [];
    return [...template.abas]
      .sort((a, b) => a.ordem - b.ordem)
      .flatMap((aba) => aba.campos.map((c) => ({ ...c, abaNome: aba.nomeAba })));
  }, [template]);

  const abrirNovoMapeamento = () => {
    setNomeNovoMap("");
    setColunasPendentes([]);
    setIsNovoMapOpen(true);
  };

  const adicionarColunaManual = () => {
    if (!colunaManual.trim()) return;
    setColunasPendentes((prev) => [...prev, colunaManual.trim()]);
    setColunaManual("");
  };

  const confirmarImportColunas = (headers: string[]) => {
    setColunasPendentes((prev) => [...prev, ...headers]);
    toast.success(`${headers.length} coluna(s) detectada(s) do Excel.`);
  };

  // MPP como caminho principal (item 7 do pedido) — lê via MPXJ.Net no backend (mesma lib já usada
  // por Cronograma Tipo 1/2, sem depender de MS Project instalado). "NÃO assumir nomes": só os campos
  // que o backend confirmou terem valor real no arquivo entram na lista.
  const handleImportarMpp = async (file: File) => {
    setImportandoMpp(true);
    const tid = toast.loading("Lendo arquivo MPP...");
    try {
      const resultado = await importarMpp(file);
      if (resultado.camposDisponiveis.length === 0) {
        toast.error("Nenhum campo com valor real foi encontrado neste MPP.", { id: tid });
        return;
      }
      setColunasPendentes((prev) => [...prev, ...resultado.camposDisponiveis]);
      toast.success(`${resultado.camposDisponiveis.length} campo(s) detectado(s) no MPP.`, { id: tid });
      resultado.avisos.forEach((a) => toast(a, { icon: "⚠️", duration: 5000 }));
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Falha ao ler o MPP: ${msg}`, { id: tid, duration: 6000 });
    } finally {
      setImportandoMpp(false);
    }
  };

  const criarMapeamento = () => {
    if (!template) return;
    if (!nomeNovoMap.trim()) return toast.error("Informe um nome para o mapeamento.");
    if (colunasPendentes.length === 0) return toast.error("Adicione ao menos uma coluna do cronograma (manual ou via Excel).");
    const id = ldp.criarMapeamento(template.id, nomeNovoMap.trim());
    const linhas: CronogramaMapeamentoCampo[] = colunasPendentes.map((col, i) => ({
      id: `${id}_${i}`,
      templateCampoId: "",
      colunaCronogramaOrigem: col,
      ordem: i,
    }));
    ldp.definirLinhasMapeamento(id, linhas);
    setMapeamentoId(id);
    setIsNovoMapOpen(false);
  };

  const atualizarDestino = (linhaId: string, templateCampoId: string) => {
    if (!mapeamento) return;
    const novasLinhas = mapeamento.campos.map((l) => (l.id === linhaId ? { ...l, templateCampoId } : l));
    ldp.definirLinhasMapeamento(mapeamento.id, novasLinhas);
  };

  const removerLinha = (linhaId: string) => {
    if (!mapeamento) return;
    ldp.definirLinhasMapeamento(mapeamento.id, mapeamento.campos.filter((l) => l.id !== linhaId));
  };

  return (
    <div className="p-8 w-full">
      <LdpConfigPageHeader
        title="Mapeamento do Cronograma"
        description="Configure como as colunas de um Cronograma alimentam os campos do Template LDP. Aqui só se grava o DE/PARA — nenhum dado real do cronograma é lido ou copiado nesta fase."
      />

      <div className="mb-6">
        <label className="text-[10px] font-black uppercase text-muted-foreground">1. Selecionar Template LDP</label>
        <Select value={templateId} onValueChange={(v) => { setTemplateId(v); setMapeamentoId(null); }}>
          <SelectTrigger className="bg-card/30 border-border/50 max-w-md"><SelectValue placeholder="Selecione um Template..." /></SelectTrigger>
          <SelectContent>
            {ldp.templates.length === 0 && <p className="px-3 py-2 text-xs text-muted-foreground italic">Nenhum template criado ainda — crie um na página "Template" primeiro.</p>}
            {ldp.templates.map((t) => <SelectItem key={t.id} value={t.id}>{t.nomeTemplate}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      {!template ? null : !mapeamento ? (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h3 className="text-sm font-black uppercase italic text-muted-foreground">
              Mapeamentos de "{template.nomeTemplate}"
            </h3>
            <Button onClick={abrirNovoMapeamento} className="text-[10px] font-black uppercase gap-2 bg-primary hover:bg-primary/90">
              <Plus className="w-3.5 h-3.5" /> Novo Mapeamento
            </Button>
          </div>

          {mapeamentosDoTemplate.length === 0 ? (
            <div className="glass-card p-12 rounded-2xl border border-dashed border-border/50 text-center">
              <GitBranch className="w-8 h-8 text-muted-foreground opacity-40 mx-auto mb-3" />
              <p className="text-xs font-bold text-muted-foreground uppercase italic">Nenhum mapeamento ainda para este template</p>
              <p className="text-[11px] text-muted-foreground mt-1">
                Um mapeamento é reutilizável — vários projetos que usam o mesmo cronograma (ex.: mesma ferramenta) podem apontar para ele.
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {mapeamentosDoTemplate.map((m) => (
                <div key={m.id} onClick={() => setMapeamentoId(m.id)} className="glass-card p-5 rounded-xl border border-border/50 hover:border-primary/50 cursor-pointer transition-all group relative">
                  <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); ldp.removerMapeamento(m.id); }} className="absolute top-2 right-2 h-7 w-7 p-0 opacity-0 group-hover:opacity-100 hover:text-destructive">
                    <Trash2 className="w-3.5 h-3.5" />
                  </Button>
                  <p className="font-bold text-sm uppercase italic">{m.nomeMapeamento}</p>
                  <p className="text-[10px] text-muted-foreground uppercase font-black mt-1">{m.campos.length} coluna(s) mapeada(s)</p>
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-4">
          <button onClick={() => setMapeamentoId(null)} className="text-[10px] font-black uppercase text-muted-foreground hover:text-primary flex items-center gap-1">
            <ArrowLeft className="w-3 h-3" /> Todos os mapeamentos de {template.nomeTemplate}
          </button>

          <div className="flex items-center justify-between">
            <Input
              value={mapeamento.nomeMapeamento}
              readOnly
              className="bg-transparent border-none text-lg font-black uppercase italic px-0 h-auto"
            />
          </div>

          <div className="glass-card rounded-xl border border-border/50 overflow-hidden">
            <div className="grid grid-cols-[1fr_auto_1fr_auto] items-center bg-muted/40 text-[9px] uppercase text-muted-foreground font-black px-4 py-3 border-b border-border/50">
              <span>Cronograma (origem)</span>
              <span></span>
              <span>Template LDP (destino)</span>
              <span></span>
            </div>
            <div className="divide-y divide-border/30">
              {mapeamento.campos.map((linha) => (
                <div key={linha.id} className="grid grid-cols-[1fr_auto_1fr_auto] items-center gap-3 px-4 py-2.5 hover:bg-primary/5">
                  <span className="text-xs font-bold text-foreground truncate">{linha.colunaCronogramaOrigem}</span>
                  <ArrowRight className="w-3.5 h-3.5 text-muted-foreground" />
                  <Select value={linha.templateCampoId} onValueChange={(v) => atualizarDestino(linha.id, v)}>
                    <SelectTrigger className="h-8 text-xs bg-card/30 border-border/50"><SelectValue placeholder="Selecione o campo destino..." /></SelectTrigger>
                    <SelectContent>
                      {camposDoTemplate.map((c) => (
                        <SelectItem key={c.id} value={c.id} className="text-xs">{c.abaNome} · {c.nomeExibido}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Button size="sm" variant="ghost" onClick={() => removerLinha(linha.id)} className="h-7 w-7 p-0 hover:text-destructive">
                    <Trash2 className="w-3.5 h-3.5" />
                  </Button>
                </div>
              ))}
              {mapeamento.campos.length === 0 && (
                <p className="px-4 py-6 text-center text-xs text-muted-foreground italic">Nenhuma coluna neste mapeamento.</p>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Diálogo: novo mapeamento (nome + colunas) */}
      <Dialog open={isNovoMapOpen} onOpenChange={setIsNovoMapOpen}>
        <DialogContent className="max-w-2xl">
          <DialogHeader><DialogTitle className="uppercase italic font-black">Novo Mapeamento de Cronograma</DialogTitle></DialogHeader>
          <div className="space-y-4 py-2">
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Nome do mapeamento</label>
              <Input value={nomeNovoMap} onChange={(e) => setNomeNovoMap(e.target.value)} placeholder="Ex: Mapeamento Primavera" className="bg-card/30 border-border/50" />
            </div>

            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Importar cronograma</label>
              <div className="flex gap-2 mt-1">
                <div className="relative">
                  <input
                    type="file"
                    accept=".mpp"
                    onChange={(e) => { const f = e.target.files?.[0]; if (f) handleImportarMpp(f); e.target.value = ""; }}
                    className="absolute inset-0 opacity-0 cursor-pointer"
                    disabled={importandoMpp}
                    title="Selecionar arquivo .mpp"
                  />
                  <Button disabled={importandoMpp} className="text-[10px] font-black uppercase gap-1.5 bg-primary hover:bg-primary/90">
                    <FileUp className="w-3.5 h-3.5" /> {importandoMpp ? "Lendo .mpp..." : "Arquivo MPP"}
                  </Button>
                </div>
                <Button variant="outline" onClick={() => setIsImportOpen(true)} className="text-[10px] font-black uppercase gap-1.5">
                  <Upload className="w-3.5 h-3.5" /> Excel
                </Button>
              </div>
              <p className="text-[10px] text-muted-foreground mt-2 mb-1">Ou informe manualmente:</p>
              <div className="flex gap-2">
                <Input
                  value={colunaManual}
                  onChange={(e) => setColunaManual(e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); adicionarColunaManual(); } }}
                  placeholder="Digite o nome de uma coluna e pressione Enter"
                  className="bg-card/30 border-border/50"
                />
                <Button variant="outline" onClick={adicionarColunaManual} className="text-[10px] font-black uppercase shrink-0">Adicionar</Button>
              </div>
            </div>

            {colunasPendentes.length > 0 && (
              <div className="flex flex-wrap gap-2">
                {colunasPendentes.map((c, i) => (
                  <span key={i} className="px-2.5 py-1 rounded-md bg-primary/10 border border-primary/20 text-primary text-[10px] font-black uppercase flex items-center gap-1.5">
                    {c}
                    <button onClick={() => setColunasPendentes((prev) => prev.filter((_, idx) => idx !== i))}>×</button>
                  </span>
                ))}
              </div>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNovoMapOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
            <Button onClick={criarMapeamento} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Criar Mapeamento</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ExcelImportWizard
        open={isImportOpen}
        onOpenChange={setIsImportOpen}
        title="Detectar colunas de um Cronograma (Excel)"
        description="Selecione as colunas que devem entrar no mapeamento. Só os NOMES das colunas são usados — os dados desta planilha não são lidos nem copiados."
        onConfirm={confirmarImportColunas}
      />
    </div>
  );
};

export default CronogramaMapping;
