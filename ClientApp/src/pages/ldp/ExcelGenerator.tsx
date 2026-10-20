import { useState } from "react";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Plus, Trash2, Upload, ArrowUp, ArrowDown, FileSpreadsheet, ArrowLeft, Link2, Download } from "lucide-react";
import { LdpConfigPageHeader } from "./LdpConfigPageHeader";
import { useLdpConfig } from "./LdpConfigContext";
import { parseWorkbookFile, WorkbookPreview } from "./excelPreview";
import { ExcelPreviewTable } from "./ExcelPreviewTable";
import { uploadBaseEspelho, gerarTesteEspelho, dispararDownload, GerarTesteEspelhoAba } from "./ldpApi";
import { ExcelModeloCampo } from "@/types/ldp-config";

const ExcelGenerator = () => {
  const ldp = useLdpConfig();
  const [templateId, setTemplateId] = useState<string>("");
  const [modeloId, setModeloId] = useState<string | null>(null);
  const [abaModeloAtivaId, setAbaModeloAtivaId] = useState<string | null>(null);

  const [isNovoModeloOpen, setIsNovoModeloOpen] = useState(false);
  const [nomeNovoModelo, setNomeNovoModelo] = useState("");
  const [workbookPreview, setWorkbookPreview] = useState<WorkbookPreview | null>(null);
  const [arquivoNome, setArquivoNome] = useState<string | null>(null);
  const [arquivoToken, setArquivoToken] = useState<string | null>(null);
  const [enviandoBase, setEnviandoBase] = useState(false);

  const [abaFisicaEscolhida, setAbaFisicaEscolhida] = useState<Record<string, string>>({});
  const [gerandoTeste, setGerandoTeste] = useState(false);

  const template = ldp.templates.find((t) => t.id === templateId) ?? null;
  const modelosDoTemplate = ldp.modelos.filter((m) => m.templateId === templateId);
  const modelo = ldp.modelos.find((m) => m.id === modeloId) ?? null;
  const abaModeloAtiva = modelo?.abas.find((a) => a.id === abaModeloAtivaId) ?? null;
  const templateAbaDaAbaAtiva = template?.abas.find((a) => a.id === abaModeloAtiva?.templateAbaId) ?? null;

  const handleUploadBase = async (file: File) => {
    setEnviandoBase(true);
    try {
      // Preview visual (client-side, mesma técnica de sempre) + envio REAL ao servidor (armazenamento
      // temporário — decisão definitiva de infraestrutura ainda pendente, ver documento 17 §9/§20).
      const [preview, enviado] = await Promise.all([parseWorkbookFile(file), uploadBaseEspelho(file)]);
      setWorkbookPreview(preview);
      setArquivoNome(file.name);
      setArquivoToken(enviado.arquivoToken);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Não foi possível enviar o arquivo base: ${msg}`);
    } finally {
      setEnviandoBase(false);
    }
  };

  const criarModelo = () => {
    if (!template) return;
    if (!nomeNovoModelo.trim()) return toast.error("Informe um nome para o modelo.");
    if (!workbookPreview || !arquivoNome || !arquivoToken) return toast.error("Suba o arquivo XLSX base primeiro.");
    const id = ldp.criarModelo(template.id, nomeNovoModelo.trim(), arquivoNome, arquivoToken);
    setModeloId(id);
    setIsNovoModeloOpen(false);
    setNomeNovoModelo("");
  };

  const vincularAbaFisica = (templateAbaId: string) => {
    if (!modelo) return;
    const nomeAbaFisica = abaFisicaEscolhida[templateAbaId];
    if (!nomeAbaFisica) return toast.error("Selecione a aba física do Excel primeiro.");
    const id = ldp.criarAbaModelo(modelo.id, templateAbaId, nomeAbaFisica);
    setAbaModeloAtivaId(id);
  };

  const toggleCampoExportado = (campoId: string) => {
    if (!modelo || !abaModeloAtiva) return;
    const jaExiste = abaModeloAtiva.campos.some((c) => c.templateCampoId === campoId);
    let novosCampos: ExcelModeloCampo[];
    if (jaExiste) {
      novosCampos = abaModeloAtiva.campos
        .filter((c) => c.templateCampoId !== campoId)
        .map((c, i) => ({ ...c, ordem: i }));
    } else {
      novosCampos = [
        ...abaModeloAtiva.campos,
        { id: `${abaModeloAtiva.id}_${campoId}`, templateCampoId: campoId, ordem: abaModeloAtiva.campos.length },
      ];
    }
    ldp.definirCamposAbaModelo(modelo.id, abaModeloAtiva.id, novosCampos);
  };

  const moverCampoExportado = (campoId: string, direcao: -1 | 1) => {
    if (!modelo || !abaModeloAtiva) return;
    const ordenados = [...abaModeloAtiva.campos].sort((a, b) => a.ordem - b.ordem);
    const idx = ordenados.findIndex((c) => c.templateCampoId === campoId);
    const destino = idx + direcao;
    if (idx === -1 || destino < 0 || destino >= ordenados.length) return;
    [ordenados[idx], ordenados[destino]] = [ordenados[destino], ordenados[idx]];
    ldp.definirCamposAbaModelo(modelo.id, abaModeloAtiva.id, ordenados.map((c, i) => ({ ...c, ordem: i })));
  };

  // GERAR TESTE — arquivo real, não conecta ao Worker, não envia ao ACC, não sobrescreve a LDP atual.
  // Dados: como o datastore novo (Cronograma/Timesheet/Custo/Weekly reais) não está conectado nesta
  // fase, usa um pequeno dataset SINTÉTICO, claramente identificado como tal (nunca dado real
  // disfarçado) — ver item 15 do pedido.
  const handleGerarTeste = async () => {
    if (!template || !modelo) return;
    if (!modelo.arquivoToken) return toast.error("Este modelo não tem um arquivo base enviado nesta sessão — recrie o upload.");
    const abasParaGerar = modelo.abas.filter((am) => am.campos.length > 0);
    if (abasParaGerar.length === 0) return toast.error("Configure ao menos uma aba com campos exportados antes de gerar o teste.");

    setGerandoTeste(true);
    const tid = toast.loading("Gerando arquivo de teste (dados fictícios de validação)...");
    try {
      const abasRequest: GerarTesteEspelhoAba[] = abasParaGerar.map((am) => {
        const tAba = template.abas.find((a) => a.id === am.templateAbaId);
        const camposOrdenados = [...am.campos].sort((a, b) => a.ordem - b.ordem);
        const nomesCampos = camposOrdenados.map((c) => tAba?.campos.find((tc) => tc.id === c.templateCampoId)?.nomeExibido ?? c.templateCampoId);
        const linhasDados = Array.from({ length: 5 }, (_, i) =>
          Object.fromEntries(nomesCampos.map((nome) => [nome, `TESTE ${i + 1}`]))
        );
        return {
          nomeAbaFisica: am.nomeAbaFisica,
          linhaCabecalho: am.linhaCabecalho,
          colunaInicial: am.colunaInicial,
          camposOrdem: nomesCampos,
          linhasDados,
        };
      });

      const blob = await gerarTesteEspelho(modelo.arquivoToken, abasRequest);
      dispararDownload(blob, `LDP_Espelho_TESTE_${modelo.nomeModelo.replace(/\s+/g, "_")}.xlsx`);
      toast.success("Arquivo de teste gerado — MODO DE TESTE, dados fictícios. Verifique o download.", { id: tid, duration: 6000 });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error(`Falha ao gerar o teste: ${msg}`, { id: tid, duration: 6000 });
    } finally {
      setGerandoTeste(false);
    }
  };

  return (
    <div className="p-8 w-full">
      <LdpConfigPageHeader
        title="Geração LDP Espelho"
        description='Configure como o sistema gera a LDP espelho em Excel a partir de um XLSX base (logo, carimbo, estilos). Este arquivo é só um modelo VISUAL de saída — nunca a base de dados.'
      />

      <div className="mb-6">
        <label className="text-[10px] font-black uppercase text-muted-foreground">1. Selecionar Template LDP</label>
        <Select value={templateId} onValueChange={(v) => { setTemplateId(v); setModeloId(null); }}>
          <SelectTrigger className="bg-card/30 border-border/50 max-w-md"><SelectValue placeholder="Selecione um Template..." /></SelectTrigger>
          <SelectContent>
            {ldp.templates.map((t) => <SelectItem key={t.id} value={t.id}>{t.nomeTemplate}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      {!template ? null : !modelo ? (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h3 className="text-sm font-black uppercase italic text-muted-foreground">
              Modelos Espelho de "{template.nomeTemplate}"
            </h3>
            <Button onClick={() => setIsNovoModeloOpen(true)} className="text-[10px] font-black uppercase gap-2 bg-primary hover:bg-primary/90">
              <Plus className="w-3.5 h-3.5" /> Novo Modelo
            </Button>
          </div>

          {modelosDoTemplate.length === 0 ? (
            <div className="glass-card p-12 rounded-2xl border border-dashed border-border/50 text-center">
              <FileSpreadsheet className="w-8 h-8 text-muted-foreground opacity-40 mx-auto mb-3" />
              <p className="text-xs font-bold text-muted-foreground uppercase italic">Nenhum modelo Espelho ainda para este template</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {modelosDoTemplate.map((m) => (
                <div key={m.id} onClick={() => setModeloId(m.id)} className="glass-card p-5 rounded-xl border border-border/50 hover:border-primary/50 cursor-pointer transition-all group relative">
                  <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); ldp.removerModelo(m.id); }} className="absolute top-2 right-2 h-7 w-7 p-0 opacity-0 group-hover:opacity-100 hover:text-destructive">
                    <Trash2 className="w-3.5 h-3.5" />
                  </Button>
                  <p className="font-bold text-sm uppercase italic">{m.nomeModelo}</p>
                  <p className="text-[10px] text-muted-foreground uppercase font-black mt-1">
                    Base: {m.arquivoNomeOriginal ?? "—"} · {m.abas.length} aba(s) configurada(s)
                    {m.arquivoToken && " · arquivo no servidor"}
                  </p>
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-6">
          <button onClick={() => { setModeloId(null); setAbaModeloAtivaId(null); }} className="text-[10px] font-black uppercase text-muted-foreground hover:text-primary flex items-center gap-1">
            <ArrowLeft className="w-3 h-3" /> Todos os modelos de {template.nomeTemplate}
          </button>

          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-lg font-black uppercase italic">{modelo.nomeModelo}</h2>
              <p className="text-[10px] text-muted-foreground uppercase font-black">Base: {modelo.arquivoNomeOriginal ?? "arquivo não carregado nesta sessão"}</p>
            </div>
            <Button
              disabled={gerandoTeste || !modelo.arquivoToken}
              onClick={handleGerarTeste}
              className="text-[10px] font-black uppercase gap-2 bg-emerald-600 hover:bg-emerald-700"
            >
              <Download className="w-3.5 h-3.5" /> Gerar Teste (.xlsx real)
            </Button>
          </div>

          {!workbookPreview && (
            <div className="p-4 rounded-xl border border-amber-500/30 bg-amber-500/5 text-[11px] text-muted-foreground">
              O preview do arquivo base não está carregado nesta sessão de navegação (o arquivo em si,
              se já enviado antes, continua no armazenamento temporário do servidor — "Gerar Teste"
              funciona mesmo assim). Para configurar novas abas físicas, refaça o upload.
            </div>
          )}

          <div>
            <h3 className="text-[10px] font-black uppercase text-muted-foreground mb-3">2. Vincular abas lógicas às abas físicas do Excel</h3>
            <div className="space-y-2">
              {[...template.abas].sort((a, b) => a.ordem - b.ordem).map((tAba) => {
                const abaModelo = modelo.abas.find((am) => am.templateAbaId === tAba.id);
                return (
                  <div key={tAba.id} className={`glass-card p-4 rounded-xl border flex items-center gap-4 ${abaModelo?.id === abaModeloAtivaId ? "border-primary bg-primary/5" : "border-border/50"}`}>
                    <div className="flex-1">
                      <p className="text-xs font-bold uppercase">{tAba.nomeAba}</p>
                      <p className="text-[9px] text-muted-foreground uppercase font-black">{tAba.campos.length} campo(s) no template</p>
                    </div>
                    {abaModelo ? (
                      <>
                        <span className="text-[10px] font-black uppercase text-primary flex items-center gap-1">
                          <Link2 className="w-3 h-3" /> {abaModelo.nomeAbaFisica}
                        </span>
                        <Button size="sm" variant="outline" onClick={() => setAbaModeloAtivaId(abaModelo.id)} className="text-[9px] font-black uppercase">
                          Configurar campos
                        </Button>
                      </>
                    ) : (
                      <>
                        <Select
                          value={abaFisicaEscolhida[tAba.id] ?? ""}
                          onValueChange={(v) => setAbaFisicaEscolhida((prev) => ({ ...prev, [tAba.id]: v }))}
                          disabled={!workbookPreview}
                        >
                          <SelectTrigger className="w-48 h-8 text-xs bg-card/30 border-border/50"><SelectValue placeholder="Aba física..." /></SelectTrigger>
                          <SelectContent>
                            {workbookPreview?.sheetNames.map((s) => <SelectItem key={s} value={s} className="text-xs">{s}</SelectItem>)}
                          </SelectContent>
                        </Select>
                        <Button size="sm" onClick={() => vincularAbaFisica(tAba.id)} disabled={!workbookPreview} className="text-[9px] font-black uppercase bg-primary hover:bg-primary/90">
                          Vincular
                        </Button>
                      </>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {abaModeloAtiva && templateAbaDaAbaAtiva && (
            <div className="space-y-4">
              <h3 className="text-[10px] font-black uppercase text-muted-foreground">
                3. Campos exportados — "{templateAbaDaAbaAtiva.nomeAba}" → aba física "{abaModeloAtiva.nomeAbaFisica}"
              </h3>

              <div className="flex items-center gap-4">
                <div className="w-48">
                  <label className="text-[9px] font-black uppercase text-muted-foreground">Linha do cabeçalho</label>
                  <Input
                    type="number"
                    min={1}
                    value={abaModeloAtiva.linhaCabecalho}
                    onChange={(e) => ldp.atualizarAbaModelo(modelo.id, abaModeloAtiva.id, { linhaCabecalho: Math.max(1, Number(e.target.value) || 1) })}
                    className="bg-card/30 border-border/50 h-8 text-xs"
                  />
                </div>
                <div className="w-56">
                  <label className="text-[9px] font-black uppercase text-primary">Primeira coluna de dados</label>
                  <Input
                    type="number"
                    min={1}
                    value={abaModeloAtiva.colunaInicial}
                    onChange={(e) => ldp.atualizarAbaModelo(modelo.id, abaModeloAtiva.id, { colunaInicial: Math.max(1, Number(e.target.value) || 1) })}
                    className="bg-primary/5 border-primary/40 h-8 text-xs font-bold"
                    title="1 = primeira coluna da planilha. Use para pular colunas decorativas do XLSX base (ex.: margem/logo)."
                  />
                </div>
              </div>

              {workbookPreview && (
                <ExcelPreviewTable
                  rows={workbookPreview.getRows(abaModeloAtiva.nomeAbaFisica)}
                  headerRowIndex={Math.max(0, abaModeloAtiva.linhaCabecalho - 1)}
                />
              )}

              <div className="glass-card rounded-xl border border-border/50 overflow-hidden">
                <div className="grid grid-cols-[auto_1fr_auto] items-center bg-muted/40 text-[9px] uppercase text-muted-foreground font-black px-4 py-3 border-b border-border/50">
                  <span>Exportar</span>
                  <span>Campo do Template</span>
                  <span>Ordem</span>
                </div>
                <div className="divide-y divide-border/30">
                  {templateAbaDaAbaAtiva.campos.map((c) => {
                    const exportado = abaModeloAtiva.campos.find((ec) => ec.templateCampoId === c.id);
                    return (
                      <div key={c.id} className="grid grid-cols-[auto_1fr_auto] items-center gap-3 px-4 py-2.5 hover:bg-primary/5">
                        <input type="checkbox" checked={!!exportado} onChange={() => toggleCampoExportado(c.id)} className="accent-primary w-4 h-4" />
                        <span className="text-xs font-bold">{c.nomeExibido}</span>
                        {exportado ? (
                          <div className="flex items-center gap-1">
                            <span className="text-[10px] font-black text-primary w-6 text-center">{exportado.ordem + 1}</span>
                            <Button size="sm" variant="ghost" onClick={() => moverCampoExportado(c.id, -1)} className="h-6 w-6 p-0"><ArrowUp className="w-3 h-3" /></Button>
                            <Button size="sm" variant="ghost" onClick={() => moverCampoExportado(c.id, 1)} className="h-6 w-6 p-0"><ArrowDown className="w-3 h-3" /></Button>
                          </div>
                        ) : (
                          <span className="text-[10px] text-muted-foreground italic">não exportado</span>
                        )}
                      </div>
                    );
                  })}
                  {templateAbaDaAbaAtiva.campos.length === 0 && (
                    <p className="px-4 py-6 text-center text-xs text-muted-foreground italic">Esta aba do template não tem campos ainda.</p>
                  )}
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Diálogo: novo modelo Espelho */}
      <Dialog open={isNovoModeloOpen} onOpenChange={(o) => { setIsNovoModeloOpen(o); if (!o) { setWorkbookPreview(null); setArquivoNome(null); setArquivoToken(null); } }}>
        <DialogContent className="max-w-2xl">
          <DialogHeader><DialogTitle className="uppercase italic font-black">Novo Modelo Espelho</DialogTitle></DialogHeader>
          <div className="space-y-4 py-2">
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Nome do modelo</label>
              <Input value={nomeNovoModelo} onChange={(e) => setNomeNovoModelo(e.target.value)} placeholder="Ex: LDP Espelho Padrão N&A" className="bg-card/30 border-border/50" />
            </div>

            {!workbookPreview ? (
              <div className="glass-card p-8 border-2 border-dashed border-primary/30 text-center relative hover:bg-primary/5 transition-colors cursor-pointer rounded-xl">
                <input
                  type="file"
                  accept=".xlsx,.xls"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) handleUploadBase(f); }}
                  className="absolute inset-0 opacity-0 cursor-pointer"
                  title="Selecionar XLSX base"
                  disabled={enviandoBase}
                />
                <div className="flex flex-col items-center gap-2 pointer-events-none">
                  <Upload className="w-7 h-7 text-primary" />
                  <span className="font-black text-[11px] uppercase italic text-primary">
                    {enviandoBase ? "Enviando..." : "Selecionar XLSX base (logo, carimbo, estilos)"}
                  </span>
                </div>
              </div>
            ) : (
              <p className="text-[11px] text-muted-foreground uppercase font-bold">
                Arquivo enviado ao servidor: {arquivoNome} ({workbookPreview.sheetNames.length} aba(s) detectada(s): {workbookPreview.sheetNames.join(", ")})
              </p>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNovoModeloOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
            <Button onClick={criarModelo} disabled={!workbookPreview} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Criar Modelo</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

export default ExcelGenerator;
