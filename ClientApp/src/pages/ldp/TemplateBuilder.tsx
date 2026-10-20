import { useState } from "react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import {
  Plus,
  Trash2,
  ArrowUp,
  ArrowDown,
  FileSpreadsheet,
  Copy,
  Upload,
  ArrowLeft,
  LayoutTemplate,
  Save,
} from "lucide-react";
import { LdpConfigPageHeader } from "./LdpConfigPageHeader";
import { useLdpConfig } from "./LdpConfigContext";
import { ExcelImportWizard } from "./ExcelImportWizard";
import { useColumnWidths, ResizeHandle } from "./ResizableColumns";
import { salvarTemplateNoBanco } from "./ldpApi";
import { ProjetoEstruturaPanel } from "./ProjetoEstruturaPanel";
import { TIPOS_DADO_CAMPO, ORIGENS_CAMPO, CAMPOS_SISTEMA, TemplateCampo, OrigemCriacaoTemplate } from "@/types/ldp-config";

const TemplateBuilder = () => {
  const ldp = useLdpConfig();
  const [templateSelecionadoId, setTemplateSelecionadoId] = useState<string | null>(null);
  const [abaSelecionadaId, setAbaSelecionadaId] = useState<string | null>(null);
  const colWidths = useColumnWidths([40, 220, 160, 140, 220, 100, 70, 160, 60]);
  const [salvandoTemplate, setSalvandoTemplate] = useState(false);

  const [isNovoOpen, setIsNovoOpen] = useState(false);
  const [modoNovo, setModoNovo] = useState<OrigemCriacaoTemplate>("MANUAL");
  const [nomeNovo, setNomeNovo] = useState("");
  const [descNovo, setDescNovo] = useState("");
  const [cloneBaseId, setCloneBaseId] = useState<string>("");
  const [isImportOpen, setIsImportOpen] = useState(false);

  const [isNovaAbaOpen, setIsNovaAbaOpen] = useState(false);
  const [nomeNovaAba, setNomeNovaAba] = useState("");

  const [isNovoCampoOpen, setIsNovoCampoOpen] = useState(false);
  const [isImportColunasOpen, setIsImportColunasOpen] = useState(false);
  const [novoCampo, setNovoCampo] = useState<Omit<TemplateCampo, "id" | "ordem">>({
    nomeExibido: "",
    chaveCampo: "",
    tipoDado: "TEXTO",
    obrigatorio: false,
    origem: "MANUAL",
    ativo: true,
  });

  const template = ldp.templates.find((t) => t.id === templateSelecionadoId) ?? null;
  const aba = template?.abas.find((a) => a.id === abaSelecionadaId) ?? null;

  const chaveSugerida = (nome: string) =>
    nome.trim().toUpperCase().replace(/[^A-Z0-9]+/g, "_").replace(/^_+|_+$/g, "") || "CAMPO";

  const abrirNovoTemplate = (modo: OrigemCriacaoTemplate) => {
    setModoNovo(modo);
    setNomeNovo("");
    setDescNovo("");
    setCloneBaseId("");
    if (modo === "IMPORT_EXCEL") {
      setIsImportOpen(true);
    } else {
      setIsNovoOpen(true);
    }
  };

  const confirmarNovoTemplate = () => {
    if (!nomeNovo.trim()) return toast.error("Informe um nome para o Template.");
    if (modoNovo === "CLONE" && !cloneBaseId) return toast.error("Selecione o Template base para clonar.");
    const id = ldp.criarTemplate(nomeNovo.trim(), descNovo.trim(), modoNovo, modoNovo === "CLONE" ? cloneBaseId : undefined);
    toast.success("Template criado (em memória — ver aviso no topo da página).");
    setTemplateSelecionadoId(id);
    setAbaSelecionadaId(null);
    setIsNovoOpen(false);
  };

  const confirmarImportExcel = (headers: string[], fileName: string) => {
    if (!nomeNovo.trim()) {
      // Import flow abre sem passar pelo diálogo de nome — pedir agora.
      const nome = window.prompt("Nome do novo Template:", fileName.replace(/\.(xlsx|xls)$/i, "")) || "";
      if (!nome.trim()) return toast.error("Import cancelado — nome obrigatório.");
      setNomeNovo(nome);
    }
    const id = ldp.criarTemplate(nomeNovo.trim() || fileName, `Importado de ${fileName}`, "IMPORT_EXCEL");
    const abaId = ldp.criarAba(id, "Principal");
    ldp.importarCamposDeExcel(id, abaId, headers);
    toast.success(`Template criado com ${headers.length} campo(s) importado(s) da planilha.`);
    setTemplateSelecionadoId(id);
    setAbaSelecionadaId(abaId);
  };

  const confirmarNovaAba = () => {
    if (!template) return;
    if (!nomeNovaAba.trim()) return toast.error("Informe um nome para a aba.");
    const id = ldp.criarAba(template.id, nomeNovaAba.trim());
    setAbaSelecionadaId(id);
    setNomeNovaAba("");
    setIsNovaAbaOpen(false);
  };

  const confirmarNovoCampo = () => {
    if (!template || !aba) return;
    if (!novoCampo.nomeExibido.trim()) return toast.error("Informe o nome exibido do campo.");
    const chave = novoCampo.chaveCampo.trim() || chaveSugerida(novoCampo.nomeExibido);
    if (aba.campos.some((c) => c.chaveCampo === chave)) {
      return toast.error(`Já existe um campo com a chave "${chave}" nesta aba.`);
    }
    ldp.criarCampo(template.id, aba.id, { ...novoCampo, chaveCampo: chave });
    setNovoCampo({ nomeExibido: "", chaveCampo: "", tipoDado: "TEXTO", obrigatorio: false, origem: "MANUAL", ativo: true });
    setIsNovoCampoOpen(false);
  };

  const confirmarImportColunas = (headers: string[]) => {
    if (!template || !aba) return;
    ldp.importarCamposDeExcel(template.id, aba.id, headers);
    toast.success(`${headers.length} campo(s) importado(s) para a aba "${aba.nomeAba}".`);
  };

  // Persistência REAL contra api/ldp-config/templates (schema ldp.* ainda não executado — ver
  // Migrations/ConfiguracaoLdp/README.md). Falha explícita, nunca finge sucesso.
  const handleSalvarTemplateNoBanco = async () => {
    if (!template) return;
    setSalvandoTemplate(true);
    const tid = toast.loading("Salvando no banco...");
    try {
      const backendId = await salvarTemplateNoBanco(template);
      ldp.marcarTemplateSalvo(template.id, backendId);
      toast.success(`Salvo — ldp.Template TemplateId=${backendId}.`, { id: tid });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Falha ao salvar no banco: ${msg}`, { id: tid, duration: 6000 });
    } finally {
      setSalvandoTemplate(false);
    }
  };

  // ------------------------------------------------------------------
  // Tela 1: lista de templates
  // ------------------------------------------------------------------
  if (!template) {
    return (
      <div className="p-8 w-full">
        <LdpConfigPageHeader
          title="Template LDP"
          description="Crie a estrutura lógica da LDP: abas e campos dinâmicos. Nada aqui gera coluna nova no banco — cada campo é um metadado."
        />

        <div className="flex justify-between items-center mb-6">
          <h2 className="text-sm font-black uppercase italic text-muted-foreground">
            {ldp.templates.length} template(s) nesta sessão
          </h2>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => abrirNovoTemplate("CLONE")} className="text-[10px] font-black uppercase gap-2">
              <Copy className="w-3.5 h-3.5" /> Clonar existente
            </Button>
            <Button variant="outline" onClick={() => abrirNovoTemplate("IMPORT_EXCEL")} className="text-[10px] font-black uppercase gap-2">
              <Upload className="w-3.5 h-3.5" /> Importar Excel
            </Button>
            <Button onClick={() => abrirNovoTemplate("MANUAL")} className="text-[10px] font-black uppercase gap-2 bg-primary hover:bg-primary/90">
              <Plus className="w-3.5 h-3.5" /> Criar do zero
            </Button>
          </div>
        </div>

        {ldp.templates.length === 0 ? (
          <div className="glass-card p-12 rounded-2xl border border-dashed border-border/50 text-center">
            <LayoutTemplate className="w-10 h-10 text-muted-foreground mx-auto mb-4 opacity-40" />
            <p className="text-sm font-bold text-muted-foreground uppercase italic">Nenhum template criado nesta sessão</p>
            <p className="text-xs text-muted-foreground mt-1">Comece criando do zero, clonando um existente ou importando um Excel.</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {ldp.templates.map((t) => (
              <div
                key={t.id}
                onClick={() => setTemplateSelecionadoId(t.id)}
                className="group cursor-pointer glass-card p-6 rounded-xl border border-border/50 hover:border-primary/50 transition-all shadow-2xl relative"
              >
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={(e) => { e.stopPropagation(); ldp.removerTemplate(t.id); }}
                  className="absolute top-2 right-2 h-8 w-8 p-0 opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive rounded-lg"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </Button>
                <div className="w-12 h-12 rounded-lg bg-primary/10 flex items-center justify-center mb-4 group-hover:scale-110 transition-transform">
                  <LayoutTemplate className="w-6 h-6 text-primary" />
                </div>
                <h3 className="font-bold text-foreground text-lg uppercase italic pr-8">{t.nomeTemplate}</h3>
                <p className="text-[10px] text-muted-foreground mt-1 line-clamp-2">{t.descricao || "Sem descrição"}</p>
                <div className="mt-4 flex items-center justify-between text-[10px] text-muted-foreground uppercase font-black">
                  <span>{t.abas.length} aba(s) · {t.abas.reduce((n, a) => n + a.campos.length, 0)} campo(s)</span>
                  <span className="opacity-0 group-hover:opacity-100 transition-opacity text-primary italic">Configurar →</span>
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Diálogo: criar do zero / clonar */}
        <Dialog open={isNovoOpen} onOpenChange={setIsNovoOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle className="uppercase italic font-black tracking-tighter">
                {modoNovo === "CLONE" ? "Clonar Template existente" : "Novo Template"}
              </DialogTitle>
            </DialogHeader>
            <div className="space-y-4 py-2">
              {modoNovo === "CLONE" && (
                <div>
                  <label className="text-[10px] font-black uppercase text-muted-foreground">Template base</label>
                  <Select value={cloneBaseId} onValueChange={setCloneBaseId}>
                    <SelectTrigger className="bg-card/30 border-border/50"><SelectValue placeholder="Selecione..." /></SelectTrigger>
                    <SelectContent>
                      {ldp.templates.map((t) => (
                        <SelectItem key={t.id} value={t.id}>{t.nomeTemplate}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )}
              <div>
                <label className="text-[10px] font-black uppercase text-muted-foreground">Nome do Template</label>
                <Input value={nomeNovo} onChange={(e) => setNomeNovo(e.target.value)} placeholder="Ex: LDP Padrão N&A" className="bg-card/30 border-border/50" />
              </div>
              <div>
                <label className="text-[10px] font-black uppercase text-muted-foreground">Descrição (opcional)</label>
                <Textarea value={descNovo} onChange={(e) => setDescNovo(e.target.value)} className="bg-card/30 border-border/50" />
              </div>
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={() => setIsNovoOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
              <Button onClick={confirmarNovoTemplate} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Criar</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <ExcelImportWizard
          open={isImportOpen}
          onOpenChange={setIsImportOpen}
          title="Importar estrutura de um Excel existente"
          description="Selecione as colunas que devem virar CAMPOS do novo Template. Isso importa apenas a ESTRUTURA (nomes de coluna) — os dados desta planilha não são copiados para o banco."
          onConfirm={confirmarImportExcel}
        />
      </div>
    );
  }

  // ------------------------------------------------------------------
  // Tela 2: edição de um template (abas + campos)
  // ------------------------------------------------------------------
  return (
    <div className="p-8 w-full">
      <LdpConfigPageHeader
        title="Template LDP"
        description="Crie a estrutura lógica da LDP: abas e campos dinâmicos. Nada aqui gera coluna nova no banco — cada campo é um metadado."
      />

      <button
        onClick={() => { setTemplateSelecionadoId(null); setAbaSelecionadaId(null); }}
        className="text-[10px] font-black uppercase text-muted-foreground hover:text-primary mb-4 flex items-center gap-1"
      >
        <ArrowLeft className="w-3 h-3" /> Todos os templates
      </button>

      <div className="flex items-start justify-between mb-6 gap-4">
        <div className="flex-1">
          <Input
            value={template.nomeTemplate}
            onChange={(e) => ldp.atualizarTemplate(template.id, { nomeTemplate: e.target.value })}
            className="bg-transparent border-none text-2xl font-black uppercase italic tracking-tighter px-0 h-auto focus-visible:ring-0"
          />
          <Textarea
            value={template.descricao}
            onChange={(e) => ldp.atualizarTemplate(template.id, { descricao: e.target.value })}
            placeholder="Descrição do template..."
            className="bg-transparent border-none text-xs text-muted-foreground px-0 mt-1 resize-none focus-visible:ring-0"
          />
        </div>
        <div className="flex items-center gap-3 shrink-0">
          <span className="text-[10px] font-black uppercase text-muted-foreground">Ativo</span>
          <Switch checked={template.ativo} onCheckedChange={(v) => ldp.atualizarTemplate(template.id, { ativo: v })} />
          <Button
            size="sm"
            disabled={salvandoTemplate}
            onClick={handleSalvarTemplateNoBanco}
            className="text-[9px] font-black uppercase gap-1.5 bg-emerald-600 hover:bg-emerald-700"
          >
            <Save className="w-3.5 h-3.5" /> {template.backendId ? "Salvo no banco — atualizar" : "Salvar no banco"}
          </Button>
        </div>
      </div>
      {template.backendId && (
        <p className="text-[10px] text-emerald-600 dark:text-emerald-400 font-black uppercase -mt-4 mb-4">
          Persistido em ldp.Template (TemplateId={template.backendId})
        </p>
      )}

      {/* Estrutura do Projeto (Projeto Único x Projeto por Fase) — painel recolhível, não rouba
          espaço do editor principal. Pertence só ao Novo Motor de LDP. */}
      <div className="mb-6">
        <ProjetoEstruturaPanel template={template} />
      </div>

      <div className="grid grid-cols-12 gap-6">
        {/* Coluna de abas */}
        <div className="col-span-4 space-y-3">
          <div className="flex justify-between items-center">
            <h3 className="text-[10px] font-black uppercase text-muted-foreground">Abas</h3>
            <Button size="sm" variant="outline" onClick={() => setIsNovaAbaOpen(true)} className="h-7 text-[9px] font-black uppercase gap-1">
              <Plus className="w-3 h-3" /> Criar aba
            </Button>
          </div>

          {template.abas.length === 0 && (
            <p className="text-[11px] text-muted-foreground italic p-4 text-center border border-dashed border-border/50 rounded-xl">
              Nenhuma aba ainda. Crie a primeira para começar a adicionar campos.
            </p>
          )}

          {[...template.abas].sort((a, b) => a.ordem - b.ordem).map((a, idx) => (
            <div
              key={a.id}
              onClick={() => setAbaSelecionadaId(a.id)}
              className={`glass-card p-3 rounded-xl border cursor-pointer transition-all flex items-center justify-between gap-2 ${
                a.id === abaSelecionadaId ? "border-primary bg-primary/5" : "border-border/50 hover:border-primary/30"
              }`}
            >
              <div className="flex-1 min-w-0">
                <p className={`text-xs font-bold truncate ${a.ativo ? "text-foreground" : "text-muted-foreground line-through"}`}>{a.nomeAba}</p>
                <p className="text-[9px] text-muted-foreground uppercase font-black">{a.campos.length} campo(s)</p>
              </div>
              <div className="flex items-center gap-0.5" onClick={(e) => e.stopPropagation()}>
                <Button size="sm" variant="ghost" disabled={idx === 0} onClick={() => ldp.moverAba(template.id, a.id, -1)} className="h-6 w-6 p-0">
                  <ArrowUp className="w-3 h-3" />
                </Button>
                <Button size="sm" variant="ghost" disabled={idx === template.abas.length - 1} onClick={() => ldp.moverAba(template.id, a.id, 1)} className="h-6 w-6 p-0">
                  <ArrowDown className="w-3 h-3" />
                </Button>
                <Button size="sm" variant="ghost" onClick={() => { ldp.removerAba(template.id, a.id); if (abaSelecionadaId === a.id) setAbaSelecionadaId(null); }} className="h-6 w-6 p-0 hover:text-destructive">
                  <Trash2 className="w-3 h-3" />
                </Button>
              </div>
            </div>
          ))}
        </div>

        {/* Coluna de campos da aba selecionada */}
        <div className="col-span-8">
          {!aba ? (
            <div className="glass-card p-12 rounded-2xl border border-dashed border-border/50 text-center h-full flex flex-col items-center justify-center">
              <FileSpreadsheet className="w-8 h-8 text-muted-foreground opacity-40 mb-3" />
              <p className="text-xs font-bold text-muted-foreground uppercase italic">Selecione ou crie uma aba para configurar os campos</p>
            </div>
          ) : (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <Input
                    value={aba.nomeAba}
                    onChange={(e) => ldp.atualizarAba(template.id, aba.id, { nomeAba: e.target.value })}
                    className="bg-transparent border-none text-lg font-black uppercase italic px-0 h-auto focus-visible:ring-0 w-auto"
                  />
                  <Switch checked={aba.ativo} onCheckedChange={(v) => ldp.atualizarAba(template.id, aba.id, { ativo: v })} />
                </div>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => setIsImportColunasOpen(true)} className="text-[9px] font-black uppercase gap-1">
                    <Upload className="w-3 h-3" /> Importar do Excel
                  </Button>
                  <Button size="sm" onClick={() => setIsNovoCampoOpen(true)} className="text-[9px] font-black uppercase gap-1 bg-primary hover:bg-primary/90">
                    <Plus className="w-3 h-3" /> Adicionar Campo
                  </Button>
                </div>
              </div>

              <div className="w-full overflow-x-auto rounded-xl border border-border/50 glass-card">
                <table className="text-left border-collapse text-xs" style={{ tableLayout: "fixed", width: colWidths.widths.reduce((s, w) => s + w, 0) }}>
                  <colgroup>
                    {colWidths.widths.map((w, i) => <col key={i} style={{ width: w }} />)}
                  </colgroup>
                  <thead>
                    <tr className="bg-muted/40 text-[9px] uppercase text-muted-foreground font-black border-b border-border/50">
                      {["", "Nome exibido", "Chave interna", "Tipo", "Origem", "Obrigatório", "Ativo", "Mover p/ aba", ""].map((label, i) => (
                        <th key={i} className="px-3 py-3 relative overflow-hidden text-ellipsis" style={i === 5 || i === 6 ? { textAlign: "center" } : undefined}>
                          {label}
                          <ResizeHandle index={i} currentWidth={colWidths.widths[i]} onResize={colWidths.setWidth} />
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/30 bg-card/10">
                    {[...aba.campos].sort((a, b) => a.ordem - b.ordem).map((c, idx) => (
                      <tr key={c.id} className="hover:bg-primary/5">
                        <td className="px-3 py-2">
                          <div className="flex flex-col">
                            <button disabled={idx === 0} onClick={() => ldp.moverCampo(template.id, aba.id, c.id, -1)} className="disabled:opacity-20"><ArrowUp className="w-3 h-3" /></button>
                            <button disabled={idx === aba.campos.length - 1} onClick={() => ldp.moverCampo(template.id, aba.id, c.id, 1)} className="disabled:opacity-20"><ArrowDown className="w-3 h-3" /></button>
                          </div>
                        </td>
                        <td className="px-3 py-2 overflow-hidden">
                          <Input
                            value={c.nomeExibido}
                            onChange={(e) => ldp.atualizarCampo(template.id, aba.id, c.id, { nomeExibido: e.target.value })}
                            className="bg-transparent border-none h-7 px-1 text-xs font-bold focus-visible:ring-1 focus-visible:ring-primary/50"
                          />
                        </td>
                        <td className="px-3 py-2 text-[10px] font-black uppercase text-primary truncate" title={c.chaveCampo}>{c.chaveCampo}</td>
                        <td className="px-3 py-2 overflow-hidden">
                          <Select value={c.tipoDado} onValueChange={(v) => ldp.atualizarCampo(template.id, aba.id, c.id, { tipoDado: v as any })}>
                            <SelectTrigger className="h-7 text-[10px] bg-transparent border-border/40"><SelectValue /></SelectTrigger>
                            <SelectContent>
                              {TIPOS_DADO_CAMPO.map((t) => <SelectItem key={t.value} value={t.value} className="text-xs">{t.label}</SelectItem>)}
                            </SelectContent>
                          </Select>
                        </td>
                        <td className="px-3 py-2 overflow-hidden">
                          <Select value={c.origem} onValueChange={(v) => ldp.atualizarCampo(template.id, aba.id, c.id, { origem: v as any })}>
                            <SelectTrigger className="h-7 text-[10px] bg-transparent border-border/40"><SelectValue /></SelectTrigger>
                            <SelectContent>
                              {ORIGENS_CAMPO.map((o) => (
                                <SelectItem key={o.value} value={o.value} className="text-xs">
                                  {o.label}{!o.conectado && " (sem leitura real ainda)"}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </td>
                        <td className="px-3 py-2 text-center">
                          <Switch checked={c.obrigatorio} onCheckedChange={(v) => ldp.atualizarCampo(template.id, aba.id, c.id, { obrigatorio: v })} />
                        </td>
                        <td className="px-3 py-2 text-center">
                          <Switch checked={c.ativo} onCheckedChange={(v) => ldp.atualizarCampo(template.id, aba.id, c.id, { ativo: v })} />
                        </td>
                        <td className="px-3 py-2 overflow-hidden">
                          {template.abas.length > 1 && (
                            <Select value="" onValueChange={(destinoAbaId) => ldp.moverCampoParaAba(template.id, aba.id, destinoAbaId, c.id)}>
                              <SelectTrigger className="h-7 text-[10px] bg-transparent border-border/40"><SelectValue placeholder="Mover para..." /></SelectTrigger>
                              <SelectContent>
                                {template.abas.filter((outraAba) => outraAba.id !== aba.id).map((outraAba) => (
                                  <SelectItem key={outraAba.id} value={outraAba.id} className="text-xs">{outraAba.nomeAba}</SelectItem>
                                ))}
                              </SelectContent>
                            </Select>
                          )}
                        </td>
                        <td className="px-3 py-2 text-right">
                          <Button size="sm" variant="ghost" onClick={() => ldp.removerCampo(template.id, aba.id, c.id)} className="h-6 w-6 p-0 hover:text-destructive">
                            <Trash2 className="w-3 h-3" />
                          </Button>
                        </td>
                      </tr>
                    ))}
                    {aba.campos.length === 0 && (
                      <tr><td colSpan={9} className="px-3 py-6 text-center italic text-muted-foreground text-xs">Nenhum campo nesta aba ainda.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Diálogo: nova aba */}
      <Dialog open={isNovaAbaOpen} onOpenChange={setIsNovaAbaOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle className="uppercase italic font-black">Criar Aba</DialogTitle></DialogHeader>
          <div className="py-2">
            <label className="text-[10px] font-black uppercase text-muted-foreground">Nome da Aba</label>
            <Input value={nomeNovaAba} onChange={(e) => setNomeNovaAba(e.target.value)} placeholder="Ex: LDP, Custo, Horas..." className="bg-card/30 border-border/50" />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNovaAbaOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
            <Button onClick={confirmarNovaAba} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Criar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Diálogo: novo campo */}
      <Dialog open={isNovoCampoOpen} onOpenChange={setIsNovoCampoOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle className="uppercase italic font-black">Adicionar Campo</DialogTitle></DialogHeader>
          <div className="space-y-3 py-2">
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Nome exibido</label>
              <Input
                value={novoCampo.nomeExibido}
                onChange={(e) => setNovoCampo((p) => ({ ...p, nomeExibido: e.target.value, chaveCampo: chaveSugerida(e.target.value) }))}
                placeholder="Ex: N&A Document Number"
                className="bg-card/30 border-border/50"
              />
            </div>
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Chave interna</label>
              <Input value={novoCampo.chaveCampo} onChange={(e) => setNovoCampo((p) => ({ ...p, chaveCampo: e.target.value }))} className="bg-card/30 border-border/50 font-mono text-xs" />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-[10px] font-black uppercase text-muted-foreground">Tipo de dado</label>
                <Select value={novoCampo.tipoDado} onValueChange={(v) => setNovoCampo((p) => ({ ...p, tipoDado: v as any }))}>
                  <SelectTrigger className="bg-card/30 border-border/50"><SelectValue /></SelectTrigger>
                  <SelectContent>{TIPOS_DADO_CAMPO.map((t) => <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>)}</SelectContent>
                </Select>
              </div>
              <div>
                <label className="text-[10px] font-black uppercase text-muted-foreground">Origem</label>
                <Select value={novoCampo.origem} onValueChange={(v) => setNovoCampo((p) => ({ ...p, origem: v as any }))}>
                  <SelectTrigger className="bg-card/30 border-border/50"><SelectValue /></SelectTrigger>
                  <SelectContent>{ORIGENS_CAMPO.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}</SelectContent>
                </Select>
              </div>
            </div>
            {novoCampo.origem === "SISTEMA" && (
              <div>
                <label className="text-[10px] font-black uppercase text-primary">Campo de sistema (derivado — nunca duplicar em dados operacionais)</label>
                <Select
                  value=""
                  onValueChange={(chave) => {
                    const item = CAMPOS_SISTEMA.find((c) => c.chave === chave);
                    if (item) setNovoCampo((p) => ({ ...p, nomeExibido: item.label, chaveCampo: item.chave }));
                  }}
                >
                  <SelectTrigger className="bg-primary/5 border-primary/40"><SelectValue placeholder="Escolher campo de sistema..." /></SelectTrigger>
                  <SelectContent>
                    {CAMPOS_SISTEMA.map((c) => <SelectItem key={c.chave} value={c.chave} className="text-xs">{c.label}</SelectItem>)}
                  </SelectContent>
                </Select>
                {novoCampo.chaveCampo === "PROJECT_PHASE" && (
                  <p className="text-[10px] text-muted-foreground mt-1">Só faz sentido quando o Modo de Geração da fase for "Consolidado" (Estrutura do Projeto → Fases).</p>
                )}
              </div>
            )}
            <div className="flex items-center gap-2">
              <Switch checked={novoCampo.obrigatorio} onCheckedChange={(v) => setNovoCampo((p) => ({ ...p, obrigatorio: v }))} />
              <span className="text-xs font-bold text-muted-foreground">Obrigatório</span>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNovoCampoOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
            <Button onClick={confirmarNovoCampo} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Adicionar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ExcelImportWizard
        open={isImportColunasOpen}
        onOpenChange={setIsImportColunasOpen}
        title="Importar colunas do Excel para esta aba"
        description="As colunas selecionadas viram novos campos desta aba. Os dados da planilha não são copiados."
        onConfirm={confirmarImportColunas}
      />
    </div>
  );
};

export default TemplateBuilder;
