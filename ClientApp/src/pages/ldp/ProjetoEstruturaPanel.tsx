import { useEffect, useMemo, useState } from "react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  ChevronDown,
  ChevronRight,
  Plus,
  Trash2,
  ArrowUp,
  ArrowDown,
  Upload,
  FileUp,
  PlayCircle,
  Save,
  AlertTriangle,
  CircleAlert,
} from "lucide-react";
import { getProjetosRegistrados, ProjetoRegistrado } from "@/lib/projectsRegistry";
import { useLdpConfig } from "./LdpConfigContext";
import { parseWorkbookFile } from "./excelPreview";
import { importarMpp, salvarProjetoConfiguracaoNoBanco } from "./ldpApi";
import { classificarDataset } from "./faseRuleEngine";
import {
  MODOS_ESTRUTURA,
  MODOS_GERACAO_FASE,
  OPERADORES_REGRA,
  ModoEstrutura,
  ModoGeracaoFase,
  ResultadoTesteRegras,
  LdpTemplate,
} from "@/types/ldp-config";

// Painel "Estrutura do Projeto" — pertence SOMENTE ao Novo Motor de LDP. Independente de
// dbo.Projetos.Tipo_LDP (legado) — nunca usar Tipo_LDP=2 como sinônimo de "por fase". Renderizado
// dentro do editor de Template (âncora natural: um projeto usa UM Template + Mapeamento + Espelho +
// Controle — "herança" é o padrão, nenhuma tela ainda permite override por fase).
export const ProjetoEstruturaPanel = ({ template }: { template: LdpTemplate }) => {
  const ldp = useLdpConfig();
  const [aberto, setAberto] = useState(false);
  const [fasesAberto, setFasesAberto] = useState(false);

  const [projetos, setProjetos] = useState<ProjetoRegistrado[]>([]);
  const [buscaProjeto, setBuscaProjeto] = useState("");
  const [projectIdAccSelecionado, setProjectIdAccSelecionado] = useState<string | null>(null);
  const [faseSelecionadaId, setFaseSelecionadaId] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  const [datasetTeste, setDatasetTeste] = useState<{ campos: string[]; linhas: Record<string, string>[] } | null>(null);
  const [resultadoTeste, setResultadoTeste] = useState<ResultadoTesteRegras | null>(null);
  const [importandoDataset, setImportandoDataset] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    getProjetosRegistrados()
      .then((mapa) => setProjetos(Array.from(mapa.values())))
      .catch(() => toast.error("Não foi possível carregar a lista de projetos reais (registry)."));
  }, [aberto]);

  const config = projectIdAccSelecionado
    ? ldp.projetosConfiguracao.find((p) => p.projectIdAcc === projectIdAccSelecionado) ?? null
    : null;
  const fase = config?.fases.find((f) => f.id === faseSelecionadaId) ?? null;

  const projetosFiltrados = useMemo(() => {
    const termo = buscaProjeto.trim().toUpperCase();
    if (!termo) return projetos.slice(0, 20);
    return projetos.filter((p) => p.nomeProjeto.toUpperCase().includes(termo) || p.projectIdAcc.toUpperCase().includes(termo)).slice(0, 20);
  }, [projetos, buscaProjeto]);

  const selecionarProjeto = (p: ProjetoRegistrado) => {
    setProjectIdAccSelecionado(p.projectIdAcc);
    ldp.obterOuCriarProjetoConfiguracao(p.projectIdAcc, p.nomeProjeto);
    ldp.atualizarProjetoConfiguracao(p.projectIdAcc, { templateId: template.id });
    setFaseSelecionadaId(null);
    setResultadoTeste(null);
  };

  const handleModoEstruturaChange = (v: ModoEstrutura) => {
    if (!config) return;
    ldp.atualizarProjetoConfiguracao(config.projectIdAcc, { modoEstrutura: v, modoGeracaoFase: v === "PROJETO_POR_FASE" ? "CONSOLIDADO" : null });
  };

  const handleAdicionarFase = () => {
    if (!config) return;
    const n = config.fases.length + 1;
    const id = ldp.criarFase(config.projectIdAcc, `FASE_${n}`, `Fase ${n}`);
    setFaseSelecionadaId(id);
  };

  const handleImportarDatasetExcel = async (file: File) => {
    setImportandoDataset(true);
    try {
      const preview = await parseWorkbookFile(file);
      const primeiraAba = preview.sheetNames[0];
      const rows = preview.getRows(primeiraAba);
      const cabecalho = (rows[0] ?? []).map((h, i) => String(h ?? "").trim() || `Coluna ${i + 1}`);
      const linhas = rows.slice(1).map((row) => Object.fromEntries(cabecalho.map((c, i) => [c, String(row[i] ?? "")])));
      setDatasetTeste({ campos: cabecalho, linhas });
      setResultadoTeste(null);
      toast.success(`Dataset de teste carregado: ${linhas.length} linha(s), ${cabecalho.length} coluna(s).`);
    } catch {
      toast.error("Não foi possível ler este Excel.");
    } finally {
      setImportandoDataset(false);
    }
  };

  const handleImportarDatasetMpp = async (file: File) => {
    setImportandoDataset(true);
    const tid = toast.loading("Lendo arquivo MPP...");
    try {
      const resultado = await importarMpp(file);
      setDatasetTeste({ campos: resultado.camposDisponiveis, linhas: resultado.linhas });
      setResultadoTeste(null);
      toast.success(`Dataset de teste carregado do MPP: ${resultado.linhas.length} linha(s).`, { id: tid });
      resultado.avisos.forEach((a) => toast(a, { icon: "⚠️" }));
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Falha ao ler o MPP: ${msg}`, { id: tid });
    } finally {
      setImportandoDataset(false);
    }
  };

  const handleTestarRegras = () => {
    if (!config) return;
    if (!datasetTeste) return toast.error("Importe um dataset de teste primeiro (Excel ou MPP).");
    if (config.fases.length === 0) return toast.error("Crie ao menos uma fase primeiro.");
    const resultado = classificarDataset(datasetTeste.linhas, config.fases);
    setResultadoTeste(resultado);
  };

  const handleSalvarNoBanco = async () => {
    if (!config) return;
    setSalvando(true);
    const tid = toast.loading("Salvando Estrutura do Projeto no banco...");
    try {
      await salvarProjetoConfiguracaoNoBanco(config, (tid2) => {
        const t = ldp.templates.find((x) => x.id === tid2);
        return t?.backendId ?? null;
      });
      ldp.marcarProjetoConfiguracaoSalva(config.projectIdAcc);
      toast.success("Salvo em ldp.ProjetoConfiguracao.", { id: tid });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Falha ao salvar: ${msg}`, { id: tid, duration: 6000 });
    } finally {
      setSalvando(false);
    }
  };

  return (
    <div className="rounded-xl border border-border/50 glass-card overflow-hidden">
      <button
        onClick={() => setAberto((v) => !v)}
        className="w-full flex items-center gap-2 px-4 py-3 text-left hover:bg-primary/5 transition-colors"
      >
        {aberto ? <ChevronDown className="w-4 h-4 text-primary" /> : <ChevronRight className="w-4 h-4 text-primary" />}
        <span className="text-[10px] font-black uppercase tracking-widest text-primary">Estrutura do Projeto</span>
        {config && (
          <span className="text-[9px] font-black uppercase text-muted-foreground ml-2">
            {config.nomeProjetoExibicao} · {MODOS_ESTRUTURA.find((m) => m.value === config.modoEstrutura)?.label}
          </span>
        )}
      </button>

      {aberto && (
        <div className="px-4 pb-4 space-y-4 border-t border-border/50 pt-4">
          <div>
            <label className="text-[10px] font-black uppercase text-muted-foreground">Projeto (real, Project_ID_ACC)</label>
            <Input
              value={buscaProjeto}
              onChange={(e) => setBuscaProjeto(e.target.value)}
              placeholder="Buscar projeto por nome ou Project_ID_ACC..."
              className="bg-card/30 border-border/50 h-9 text-xs mb-2"
            />
            {!projectIdAccSelecionado && (
              <div className="max-h-40 overflow-auto rounded-lg border border-border/50 divide-y divide-border/30">
                {projetosFiltrados.map((p) => (
                  <button
                    key={p.projectIdAcc}
                    onClick={() => selecionarProjeto(p)}
                    className="w-full text-left px-3 py-2 text-xs hover:bg-primary/10 flex justify-between items-center"
                  >
                    <span className="font-bold">{p.nomeProjeto}</span>
                    <span className="text-[9px] text-muted-foreground uppercase font-black">{p.projectIdAcc.slice(0, 16)}...</span>
                  </button>
                ))}
                {projetosFiltrados.length === 0 && <p className="px-3 py-3 text-xs italic text-muted-foreground">Nenhum projeto encontrado.</p>}
              </div>
            )}
          </div>

          {config && (
            <>
              <div className="flex items-center justify-between glass-card p-3 rounded-lg border border-primary/30 bg-primary/5">
                <div>
                  <p className="text-xs font-bold">{config.nomeProjetoExibicao}</p>
                  <p className="text-[9px] text-muted-foreground uppercase font-black">{config.projectIdAcc}</p>
                </div>
                <button onClick={() => setProjectIdAccSelecionado(null)} className="text-[9px] font-black uppercase text-muted-foreground hover:text-primary">
                  Trocar projeto
                </button>
              </div>

              <div>
                <label className="text-[10px] font-black uppercase text-muted-foreground">Estrutura do Projeto</label>
                <Select value={config.modoEstrutura} onValueChange={(v) => handleModoEstruturaChange(v as ModoEstrutura)}>
                  <SelectTrigger className="bg-card/30 border-border/50 max-w-xs h-9 text-xs"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {MODOS_ESTRUTURA.map((m) => <SelectItem key={m.value} value={m.value} className="text-xs">{m.label}</SelectItem>)}
                  </SelectContent>
                </Select>
                <p className="text-[10px] text-muted-foreground mt-1">
                  Herda automaticamente: Template = <strong>{template.nomeTemplate}</strong>. Mapeamento/Espelho/Controle desta configuração ainda não têm seletor nesta tela — herança sempre ativa, sem override.
                </p>
              </div>

              {config.modoEstrutura === "PROJETO_POR_FASE" && (
                <div className="rounded-lg border border-border/50 overflow-hidden">
                  <button onClick={() => setFasesAberto((v) => !v)} className="w-full flex items-center gap-2 px-3 py-2.5 bg-muted/30 hover:bg-muted/50 transition-colors">
                    {fasesAberto ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
                    <span className="text-[10px] font-black uppercase">Fases e Regras</span>
                    <span className="text-[9px] text-muted-foreground uppercase font-black ml-auto">{config.fases.length} fase(s)</span>
                  </button>

                  {fasesAberto && (
                    <div className="p-3 space-y-4">
                      {/* Modo de geração */}
                      <div>
                        <label className="text-[10px] font-black uppercase text-muted-foreground">Modo de Geração</label>
                        <div className="space-y-1.5 mt-1">
                          {MODOS_GERACAO_FASE.map((m) => (
                            <label key={m.value} className="flex items-start gap-2 text-xs cursor-pointer">
                              <input
                                type="radio"
                                checked={config.modoGeracaoFase === m.value}
                                onChange={() => ldp.atualizarProjetoConfiguracao(config.projectIdAcc, { modoGeracaoFase: m.value as ModoGeracaoFase })}
                                className="accent-primary mt-0.5"
                              />
                              <span>
                                <span className="font-bold">{m.label}</span>
                                <span className="block text-[10px] text-muted-foreground">{m.descricao}</span>
                              </span>
                            </label>
                          ))}
                        </div>
                      </div>

                      <div className="grid grid-cols-12 gap-4">
                        {/* Lista compacta de fases */}
                        <div className="col-span-4 space-y-2">
                          <div className="flex justify-between items-center">
                            <span className="text-[9px] font-black uppercase text-muted-foreground">Fases</span>
                            <Button size="sm" variant="outline" onClick={handleAdicionarFase} className="h-6 text-[9px] font-black uppercase gap-1">
                              <Plus className="w-3 h-3" /> Adicionar Fase
                            </Button>
                          </div>
                          {[...config.fases].sort((a, b) => a.ordem - b.ordem).map((f, idx) => (
                            <div
                              key={f.id}
                              onClick={() => setFaseSelecionadaId(f.id)}
                              className={`p-2 rounded-lg border cursor-pointer flex items-center justify-between gap-1 ${f.id === faseSelecionadaId ? "border-primary bg-primary/10" : "border-border/50"}`}
                            >
                              <div className="min-w-0">
                                <p className={`text-xs font-bold truncate ${f.ativa ? "" : "text-muted-foreground line-through"}`}>{f.nome}</p>
                                <p className="text-[9px] text-muted-foreground uppercase font-black">{f.codigo} · {f.regras.length} regra(s)</p>
                              </div>
                              <div className="flex items-center shrink-0" onClick={(e) => e.stopPropagation()}>
                                <button disabled={idx === 0} onClick={() => ldp.moverFase(config.projectIdAcc, f.id, -1)} className="disabled:opacity-20"><ArrowUp className="w-3 h-3" /></button>
                                <button disabled={idx === config.fases.length - 1} onClick={() => ldp.moverFase(config.projectIdAcc, f.id, 1)} className="disabled:opacity-20"><ArrowDown className="w-3 h-3" /></button>
                                <button onClick={() => { ldp.removerFase(config.projectIdAcc, f.id); if (faseSelecionadaId === f.id) setFaseSelecionadaId(null); }} className="hover:text-destructive"><Trash2 className="w-3 h-3" /></button>
                              </div>
                            </div>
                          ))}
                          {config.fases.length === 0 && <p className="text-[10px] text-muted-foreground italic">Nenhuma fase ainda.</p>}
                        </div>

                        {/* Editor da fase selecionada */}
                        <div className="col-span-8 space-y-3">
                          {!fase ? (
                            <p className="text-xs text-muted-foreground italic p-6 text-center border border-dashed border-border/50 rounded-lg">Selecione ou crie uma fase.</p>
                          ) : (
                            <>
                              <div className="flex items-center gap-2">
                                <Input value={fase.codigo} onChange={(e) => ldp.atualizarFase(config.projectIdAcc, fase.id, { codigo: e.target.value })} placeholder="Código (ex: I, FEL2, 30%)" className="h-8 text-xs bg-card/30 border-border/50 w-40" />
                                <Input value={fase.nome} onChange={(e) => ldp.atualizarFase(config.projectIdAcc, fase.id, { nome: e.target.value })} placeholder="Nome" className="h-8 text-xs bg-card/30 border-border/50 flex-1" />
                                <Select value={fase.operadorLogico} onValueChange={(v) => ldp.atualizarFase(config.projectIdAcc, fase.id, { operadorLogico: v as "ALL" | "ANY" })}>
                                  <SelectTrigger className="h-8 text-[10px] w-28 bg-card/30 border-border/50"><SelectValue /></SelectTrigger>
                                  <SelectContent>
                                    <SelectItem value="ALL" className="text-xs">ALL (todas)</SelectItem>
                                    <SelectItem value="ANY" className="text-xs">ANY (qualquer)</SelectItem>
                                  </SelectContent>
                                </Select>
                              </div>

                              <div className="glass-card rounded-lg border border-border/50 overflow-hidden">
                                <div className="grid grid-cols-[1fr_1fr_1fr_auto_auto] gap-2 items-center bg-muted/40 text-[9px] uppercase text-muted-foreground font-black px-3 py-2 border-b border-border/50">
                                  <span>Campo Origem</span><span>Operador</span><span>Valor</span><span>Ativa</span><span></span>
                                </div>
                                <div className="divide-y divide-border/30">
                                  {fase.regras.map((r) => (
                                    <div key={r.id} className="grid grid-cols-[1fr_1fr_1fr_auto_auto] gap-2 items-center px-3 py-2">
                                      {datasetTeste ? (
                                        <Select value={r.campoOrigem} onValueChange={(v) => ldp.atualizarRegra(config.projectIdAcc, fase.id, r.id, { campoOrigem: v })}>
                                          <SelectTrigger className="h-7 text-[10px] bg-card/30 border-border/50"><SelectValue placeholder="Campo..." /></SelectTrigger>
                                          <SelectContent>
                                            {datasetTeste.campos.map((c) => <SelectItem key={c} value={c} className="text-xs">{c}</SelectItem>)}
                                          </SelectContent>
                                        </Select>
                                      ) : (
                                        <Input value={r.campoOrigem} onChange={(e) => ldp.atualizarRegra(config.projectIdAcc, fase.id, r.id, { campoOrigem: e.target.value })} placeholder="Ex: WBS" className="h-7 text-[10px] bg-card/30 border-border/50" />
                                      )}
                                      <Select value={r.operador} onValueChange={(v) => ldp.atualizarRegra(config.projectIdAcc, fase.id, r.id, { operador: v as any })}>
                                        <SelectTrigger className="h-7 text-[10px] bg-card/30 border-border/50"><SelectValue /></SelectTrigger>
                                        <SelectContent>
                                          {OPERADORES_REGRA.map((o) => <SelectItem key={o.value} value={o.value} className="text-xs">{o.label}</SelectItem>)}
                                        </SelectContent>
                                      </Select>
                                      <Input value={r.valorComparacao} onChange={(e) => ldp.atualizarRegra(config.projectIdAcc, fase.id, r.id, { valorComparacao: e.target.value })} placeholder="Ex: ENR004-I." className="h-7 text-[10px] bg-card/30 border-border/50" />
                                      <input type="checkbox" checked={r.ativa} onChange={(e) => ldp.atualizarRegra(config.projectIdAcc, fase.id, r.id, { ativa: e.target.checked })} className="accent-primary w-4 h-4 justify-self-center" />
                                      <button onClick={() => ldp.removerRegra(config.projectIdAcc, fase.id, r.id)} className="hover:text-destructive justify-self-center"><Trash2 className="w-3.5 h-3.5" /></button>
                                    </div>
                                  ))}
                                  {fase.regras.length === 0 && <p className="px-3 py-4 text-center text-[10px] text-muted-foreground italic">Nenhuma regra ainda.</p>}
                                </div>
                              </div>
                              <Button size="sm" variant="outline" onClick={() => ldp.criarRegra(config.projectIdAcc, fase.id)} className="text-[9px] font-black uppercase gap-1">
                                <Plus className="w-3 h-3" /> Adicionar Regra
                              </Button>
                            </>
                          )}
                        </div>
                      </div>

                      {/* Dataset de teste + Testar Regras */}
                      <div className="border-t border-border/50 pt-3 space-y-2">
                        <div className="flex items-center gap-2">
                          <div className="relative">
                            <input type="file" accept=".xlsx,.xls" onChange={(e) => { const f = e.target.files?.[0]; if (f) handleImportarDatasetExcel(f); e.target.value = ""; }} className="absolute inset-0 opacity-0 cursor-pointer" disabled={importandoDataset} />
                            <Button size="sm" variant="outline" disabled={importandoDataset} className="text-[9px] font-black uppercase gap-1"><Upload className="w-3 h-3" /> Dataset Excel</Button>
                          </div>
                          <div className="relative">
                            <input type="file" accept=".mpp" onChange={(e) => { const f = e.target.files?.[0]; if (f) handleImportarDatasetMpp(f); e.target.value = ""; }} className="absolute inset-0 opacity-0 cursor-pointer" disabled={importandoDataset} />
                            <Button size="sm" variant="outline" disabled={importandoDataset} className="text-[9px] font-black uppercase gap-1"><FileUp className="w-3 h-3" /> Dataset MPP</Button>
                          </div>
                          {datasetTeste && <span className="text-[10px] text-muted-foreground uppercase font-black">{datasetTeste.linhas.length} linha(s) carregada(s)</span>}
                          <Button size="sm" onClick={handleTestarRegras} className="text-[9px] font-black uppercase gap-1 bg-primary hover:bg-primary/90 ml-auto">
                            <PlayCircle className="w-3.5 h-3.5" /> Testar Regras
                          </Button>
                        </div>

                        {resultadoTeste && (
                          <div className="space-y-2">
                            <div className="glass-card rounded-lg border border-border/50 overflow-hidden">
                              <div className="grid grid-cols-2 bg-muted/40 text-[9px] uppercase text-muted-foreground font-black px-3 py-2 border-b border-border/50">
                                <span>Fase</span><span>Registros identificados</span>
                              </div>
                              <div className="divide-y divide-border/30">
                                {resultadoTeste.porFase.map((f) => (
                                  <div key={f.faseId} className="grid grid-cols-2 px-3 py-1.5 text-xs">
                                    <span className="font-bold">{f.nome}</span><span>{f.quantidade}</span>
                                  </div>
                                ))}
                                <div className="grid grid-cols-2 px-3 py-1.5 text-xs bg-amber-500/5">
                                  <span className="font-bold flex items-center gap-1.5 text-amber-600"><AlertTriangle className="w-3 h-3" /> SEM FASE</span>
                                  <span>{resultadoTeste.semFase.length}</span>
                                </div>
                                <div className="grid grid-cols-2 px-3 py-1.5 text-xs bg-destructive/5">
                                  <span className="font-bold flex items-center gap-1.5 text-destructive"><CircleAlert className="w-3 h-3" /> CONFLITO</span>
                                  <span>{resultadoTeste.conflitos.length}</span>
                                </div>
                              </div>
                            </div>

                            {resultadoTeste.semFase.length > 0 && (
                              <details className="text-[10px]">
                                <summary className="cursor-pointer font-black uppercase text-amber-600">Ver registros SEM FASE ({resultadoTeste.semFase.length})</summary>
                                <div className="mt-1 max-h-32 overflow-auto space-y-1">
                                  {resultadoTeste.semFase.slice(0, 30).map((l, i) => <p key={i} className="text-muted-foreground truncate">{JSON.stringify(l)}</p>)}
                                </div>
                              </details>
                            )}
                            {resultadoTeste.conflitos.length > 0 && (
                              <details className="text-[10px]">
                                <summary className="cursor-pointer font-black uppercase text-destructive">Ver CONFLITOS ({resultadoTeste.conflitos.length})</summary>
                                <div className="mt-1 max-h-32 overflow-auto space-y-1">
                                  {resultadoTeste.conflitos.slice(0, 30).map((c, i) => (
                                    <p key={i} className="text-muted-foreground truncate">
                                      {JSON.stringify(c.linha)} → <strong>{c.fases.map((f) => f.codigo).join(" + ")}</strong>
                                    </p>
                                  ))}
                                </div>
                              </details>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  )}
                </div>
              )}

              <Button
                disabled={salvando}
                onClick={handleSalvarNoBanco}
                className="text-[10px] font-black uppercase gap-2 bg-emerald-600 hover:bg-emerald-700"
              >
                <Save className="w-3.5 h-3.5" /> {config.salvoNoBanco ? "Salvo — atualizar Estrutura do Projeto" : "Salvar Estrutura do Projeto no banco"}
              </Button>
            </>
          )}
        </div>
      )}
    </div>
  );
};
