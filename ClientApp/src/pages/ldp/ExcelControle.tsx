import { useState } from "react";
import * as XLSX from "xlsx";
import toast from "react-hot-toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Plus, Trash2, ArrowUp, ArrowDown, ClipboardList, ArrowLeft, Download } from "lucide-react";
import { LdpConfigPageHeader } from "./LdpConfigPageHeader";
import { useLdpConfig } from "./LdpConfigContext";
import { ExcelControleCampo } from "@/types/ldp-config";

// Geração LDP Controle: planilha LIMPA (sem XLSX base a preservar) — por isso é gerada 100%
// client-side com a mesma biblioteca (xlsx/SheetJS) já usada no resto do app, sem round-trip ao
// backend. Persistência da CONFIGURAÇÃO (quais abas/campos) segue o mesmo padrão real das outras
// páginas (api/ldp-config/controle/*), mas a GERAÇÃO do arquivo de teste não depende do backend.
const ExcelControle = () => {
  const ldp = useLdpConfig();
  const [templateId, setTemplateId] = useState<string>("");
  const [controleId, setControleId] = useState<string | null>(null);

  const [isNovoOpen, setIsNovoOpen] = useState(false);
  const [nomeNovo, setNomeNovo] = useState("");
  const [abasEscolhidas, setAbasEscolhidas] = useState<Set<string>>(new Set());

  const template = ldp.templates.find((t) => t.id === templateId) ?? null;
  const controlesDoTemplate = ldp.controles.filter((c) => c.templateId === templateId);
  const controle = ldp.controles.find((c) => c.id === controleId) ?? null;

  const abrirNovo = () => {
    setNomeNovo("");
    setAbasEscolhidas(new Set());
    setIsNovoOpen(true);
  };

  const toggleAbaEscolhida = (abaId: string) => {
    setAbasEscolhidas((prev) => {
      const next = new Set(prev);
      if (next.has(abaId)) next.delete(abaId);
      else next.add(abaId);
      return next;
    });
  };

  const confirmarNovo = () => {
    if (!template) return;
    if (!nomeNovo.trim()) return toast.error("Informe um nome para a configuração.");
    if (abasEscolhidas.size === 0) return toast.error("Selecione ao menos uma aba do Template.");
    const id = ldp.criarControle(template.id, nomeNovo.trim());
    for (const abaId of abasEscolhidas) ldp.criarAbaControle(id, abaId);
    setControleId(id);
    setIsNovoOpen(false);
  };

  const toggleCampo = (abaControleId: string, campoId: string) => {
    if (!controle) return;
    const aba = controle.abas.find((a) => a.id === abaControleId);
    if (!aba) return;
    const jaExiste = aba.campos.some((c) => c.templateCampoId === campoId);
    let novos: ExcelControleCampo[];
    if (jaExiste) {
      novos = aba.campos.filter((c) => c.templateCampoId !== campoId).map((c, i) => ({ ...c, ordem: i }));
    } else {
      novos = [...aba.campos, { id: `${abaControleId}_${campoId}`, templateCampoId: campoId, nomeExibidoOverride: null, ordem: aba.campos.length }];
    }
    ldp.definirCamposAbaControle(controle.id, abaControleId, novos);
  };

  const moverCampo = (abaControleId: string, campoId: string, direcao: -1 | 1) => {
    if (!controle) return;
    const aba = controle.abas.find((a) => a.id === abaControleId);
    if (!aba) return;
    const ordenados = [...aba.campos].sort((a, b) => a.ordem - b.ordem);
    const idx = ordenados.findIndex((c) => c.templateCampoId === campoId);
    const destino = idx + direcao;
    if (idx === -1 || destino < 0 || destino >= ordenados.length) return;
    [ordenados[idx], ordenados[destino]] = [ordenados[destino], ordenados[idx]];
    ldp.definirCamposAbaControle(controle.id, abaControleId, ordenados.map((c, i) => ({ ...c, ordem: i })));
  };

  const atualizarOverride = (abaControleId: string, campoId: string, override: string) => {
    if (!controle) return;
    const aba = controle.abas.find((a) => a.id === abaControleId);
    if (!aba) return;
    const novos = aba.campos.map((c) => (c.templateCampoId === campoId ? { ...c, nomeExibidoOverride: override || null } : c));
    ldp.definirCamposAbaControle(controle.id, abaControleId, novos);
  };

  // GERAR TESTE — planilha real gerada 100% client-side, dados SINTÉTICOS claramente identificados
  // (ver item 15/16 do pedido — nunca dado real disfarçado; nenhuma tabela de dados nova).
  const handleGerarTeste = () => {
    if (!template || !controle) return;
    const abasComCampos = controle.abas.filter((a) => a.campos.length > 0);
    if (abasComCampos.length === 0) return toast.error("Configure ao menos uma aba com campos antes de gerar o teste.");

    const wb = XLSX.utils.book_new();
    for (const abaControle of abasComCampos) {
      const tAba = template.abas.find((a) => a.id === abaControle.templateAbaId);
      const camposOrdenados = [...abaControle.campos].sort((a, b) => a.ordem - b.ordem);
      const cabecalhos = camposOrdenados.map((c) => {
        const tCampo = tAba?.campos.find((tc) => tc.id === c.templateCampoId);
        return c.nomeExibidoOverride || tCampo?.nomeExibido || c.templateCampoId;
      });
      const linhas = Array.from({ length: 5 }, (_, i) => cabecalhos.map(() => `TESTE ${i + 1}`));
      const dados = [cabecalhos, ...linhas];
      const nomeAba = (abaControle.nomeAbaExibido || tAba?.nomeAba || "Aba").slice(0, 31);
      const ws = XLSX.utils.aoa_to_sheet(dados);
      XLSX.utils.book_append_sheet(wb, ws, nomeAba);
    }

    XLSX.writeFile(wb, `LDP_Controle_TESTE_${controle.nomeConfiguracao.replace(/\s+/g, "_")}.xlsx`);
    toast.success("Arquivo de teste gerado — MODO DE TESTE, dados fictícios (\"TESTE 1\", \"TESTE 2\"...).", { duration: 6000 });
  };

  return (
    <div className="p-8 w-full">
      <LdpConfigPageHeader
        title="Configuração LDP Controle"
        description="Gere a LDP operacional/de controle direto da configuração do sistema — planilha limpa, sem XLSX base. Foco nesta fase: campos da LDP; arquitetura preparada para incluir Custo/HH/Weekly depois."
      />

      <div className="mb-6">
        <label className="text-[10px] font-black uppercase text-muted-foreground">1. Selecionar Template LDP</label>
        <Select value={templateId} onValueChange={(v) => { setTemplateId(v); setControleId(null); }}>
          <SelectTrigger className="bg-card/30 border-border/50 max-w-md"><SelectValue placeholder="Selecione um Template..." /></SelectTrigger>
          <SelectContent>
            {ldp.templates.map((t) => <SelectItem key={t.id} value={t.id}>{t.nomeTemplate}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      {!template ? null : !controle ? (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <h3 className="text-sm font-black uppercase italic text-muted-foreground">
              Configurações de Controle de "{template.nomeTemplate}"
            </h3>
            <Button onClick={abrirNovo} className="text-[10px] font-black uppercase gap-2 bg-primary hover:bg-primary/90">
              <Plus className="w-3.5 h-3.5" /> Nova Configuração
            </Button>
          </div>

          {controlesDoTemplate.length === 0 ? (
            <div className="glass-card p-12 rounded-2xl border border-dashed border-border/50 text-center">
              <ClipboardList className="w-8 h-8 text-muted-foreground opacity-40 mx-auto mb-3" />
              <p className="text-xs font-bold text-muted-foreground uppercase italic">Nenhuma configuração de Controle ainda para este template</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {controlesDoTemplate.map((c) => (
                <div key={c.id} onClick={() => setControleId(c.id)} className="glass-card p-5 rounded-xl border border-border/50 hover:border-primary/50 cursor-pointer transition-all group relative">
                  <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); ldp.removerControle(c.id); }} className="absolute top-2 right-2 h-7 w-7 p-0 opacity-0 group-hover:opacity-100 hover:text-destructive">
                    <Trash2 className="w-3.5 h-3.5" />
                  </Button>
                  <p className="font-bold text-sm uppercase italic">{c.nomeConfiguracao}</p>
                  <p className="text-[10px] text-muted-foreground uppercase font-black mt-1">{c.abas.length} aba(s)</p>
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-6">
          <button onClick={() => setControleId(null)} className="text-[10px] font-black uppercase text-muted-foreground hover:text-primary flex items-center gap-1">
            <ArrowLeft className="w-3 h-3" /> Todas as configurações de {template.nomeTemplate}
          </button>

          <div className="flex items-center justify-between">
            <h2 className="text-lg font-black uppercase italic">{controle.nomeConfiguracao}</h2>
            <Button onClick={handleGerarTeste} className="text-[10px] font-black uppercase gap-2 bg-emerald-600 hover:bg-emerald-700">
              <Download className="w-3.5 h-3.5" /> Gerar Teste (.xlsx real)
            </Button>
          </div>

          {controle.abas.map((abaControle) => {
            const tAba = template.abas.find((a) => a.id === abaControle.templateAbaId);
            if (!tAba) return null;
            return (
              <div key={abaControle.id} className="space-y-3">
                <div className="flex items-center gap-3">
                  <h3 className="text-xs font-black uppercase italic text-primary">{tAba.nomeAba}</h3>
                  <Input
                    value={abaControle.nomeAbaExibido ?? ""}
                    onChange={(e) => ldp.atualizarAbaControle(controle.id, abaControle.id, { nomeAbaExibido: e.target.value || null })}
                    placeholder={`Nome de exibição (padrão: "${tAba.nomeAba}")`}
                    className="h-7 text-[10px] max-w-xs bg-card/30 border-border/50"
                  />
                  <Button size="sm" variant="ghost" onClick={() => ldp.removerAbaControle(controle.id, abaControle.id)} className="h-6 w-6 p-0 hover:text-destructive ml-auto">
                    <Trash2 className="w-3 h-3" />
                  </Button>
                </div>

                <div className="glass-card rounded-xl border border-border/50 overflow-hidden">
                  <div className="grid grid-cols-[auto_1fr_1fr_auto] items-center bg-muted/40 text-[9px] uppercase text-muted-foreground font-black px-4 py-3 border-b border-border/50">
                    <span>Incluir</span>
                    <span>Campo do Template</span>
                    <span>Nome exibido (override)</span>
                    <span>Ordem</span>
                  </div>
                  <div className="divide-y divide-border/30">
                    {tAba.campos.map((c) => {
                      const incluido = abaControle.campos.find((ac) => ac.templateCampoId === c.id);
                      return (
                        <div key={c.id} className="grid grid-cols-[auto_1fr_1fr_auto] items-center gap-3 px-4 py-2.5 hover:bg-primary/5">
                          <input type="checkbox" checked={!!incluido} onChange={() => toggleCampo(abaControle.id, c.id)} className="accent-primary w-4 h-4" />
                          <span className="text-xs font-bold">{c.nomeExibido}</span>
                          {incluido ? (
                            <Input
                              value={incluido.nomeExibidoOverride ?? ""}
                              onChange={(e) => atualizarOverride(abaControle.id, c.id, e.target.value)}
                              placeholder={c.nomeExibido}
                              className="h-7 text-[10px] bg-card/30 border-border/50"
                            />
                          ) : <span className="text-[10px] text-muted-foreground italic">—</span>}
                          {incluido ? (
                            <div className="flex items-center gap-1">
                              <span className="text-[10px] font-black text-primary w-6 text-center">{incluido.ordem + 1}</span>
                              <Button size="sm" variant="ghost" onClick={() => moverCampo(abaControle.id, c.id, -1)} className="h-6 w-6 p-0"><ArrowUp className="w-3 h-3" /></Button>
                              <Button size="sm" variant="ghost" onClick={() => moverCampo(abaControle.id, c.id, 1)} className="h-6 w-6 p-0"><ArrowDown className="w-3 h-3" /></Button>
                            </div>
                          ) : <span />}
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Diálogo: nova configuração de Controle */}
      <Dialog open={isNovoOpen} onOpenChange={setIsNovoOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle className="uppercase italic font-black">Nova Configuração de Controle</DialogTitle></DialogHeader>
          <div className="space-y-4 py-2">
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Nome</label>
              <Input value={nomeNovo} onChange={(e) => setNomeNovo(e.target.value)} placeholder="Ex: Controle Semanal Documentos" className="bg-card/30 border-border/50" />
            </div>
            <div>
              <label className="text-[10px] font-black uppercase text-muted-foreground">Abas do Template a incluir</label>
              <div className="space-y-1.5 mt-1">
                {template?.abas.map((a) => (
                  <label key={a.id} className="flex items-center gap-2 text-xs font-bold cursor-pointer">
                    <input type="checkbox" checked={abasEscolhidas.has(a.id)} onChange={() => toggleAbaEscolhida(a.id)} className="accent-primary w-4 h-4" />
                    {a.nomeAba}
                  </label>
                ))}
                {(!template || template.abas.length === 0) && <p className="text-xs text-muted-foreground italic">Este template não tem abas ainda.</p>}
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNovoOpen(false)} className="text-[10px] font-black uppercase">Cancelar</Button>
            <Button onClick={confirmarNovo} className="text-[10px] font-black uppercase bg-primary hover:bg-primary/90">Criar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

export default ExcelControle;
