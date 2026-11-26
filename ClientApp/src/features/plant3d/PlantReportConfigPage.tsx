// Plant 3D › Relatórios › Configuração de Relatórios (F6): define, por projeto, de onde cada relatório extrai os dados.
//   Padrão    — categoria do DE/PARA com o id esperado (line/equip/inst/valve/tiein); Materiais e Suportes NÃO têm padrão → "não mapeado".
//   Categoria — outra categoria do DE/PARA.
//   Tabelas   — tabela(s) P&ID e/ou 3D escolhidas do /schema real do projeto (extração por colunas, sem comparar P&ID × 3D).
// Salvar exige plant3d.database.edit (validado no servidor). Nada é inventado: tabelas/categorias vêm do schema e do DE/PARA salvos.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Loader2, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { PlantSchema, descreverErro, isAbort, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { formatarUtc } from "@/components/plant3d/plantApi";
import { Estado, PlantHeader, SemProjeto } from "./plantShared";
import { PLANT_ROTAS } from "./plantRoutes";
import { REPORT_KINDS, getReportCatalog, getReportConfig, putReportConfig, type ReportCatalog, type ReportConfig, type ReportKind, type ReportSource } from "./plantReports";

type Modo = "padrao" | "categoria" | "tabelas";
type Linha = { modo: Modo; categoryId: string; pidTable: string; pipingTable: string };
type Categoria = { id: string; label: string };

const INPUT = "w-full rounded-lg border border-border bg-background px-3 py-2 text-sm text-foreground outline-none focus:ring-2 focus:ring-primary disabled:opacity-50";
const VAZIA: Linha = { modo: "padrao", categoryId: "", pidTable: "", pipingTable: "" };

const daConfig = (s?: ReportSource | null): Linha =>
  s?.pidTable || s?.pipingTable ? { modo: "tabelas", categoryId: "", pidTable: s.pidTable ?? "", pipingTable: s.pipingTable ?? "" }
    : s?.categoryId ? { ...VAZIA, modo: "categoria", categoryId: s.categoryId } : VAZIA;

/** Só linhas que fogem do padrão entram no PUT (o servidor ignora vazias). */
export function paraEnvio(linhas: Record<ReportKind, Linha>): ReportConfig["sources"] {
  const out: ReportConfig["sources"] = {};
  for (const { kind } of REPORT_KINDS) {
    const l = linhas[kind];
    if (l.modo === "categoria" && l.categoryId) out[kind] = { categoryId: l.categoryId, pidTable: null, pipingTable: null };
    else if (l.modo === "tabelas" && (l.pidTable || l.pipingTable)) out[kind] = { categoryId: null, pidTable: l.pidTable || null, pipingTable: l.pipingTable || null };
  }
  return out;
}

export default function PlantReportConfigPage() {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const [schema, setSchema] = useState<PlantSchema | null>(null);
  const [categorias, setCategorias] = useState<Categoria[]>([]);
  const [catalog, setCatalog] = useState<ReportCatalog | null>(null);
  const [config, setConfig] = useState<ReportConfig | null>(null);
  const [linhas, setLinhas] = useState<Record<ReportKind, Linha>>(() => Object.fromEntries(REPORT_KINDS.map(r => [r.kind, VAZIA])) as Record<ReportKind, Linha>);
  const [loading, setLoading] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState("");
  const [aviso, setAviso] = useState<{ tom: "ok" | "erro"; texto: string } | null>(null);
  const [sujo, setSujo] = useState(false);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    setSchema(null); setCategorias([]); setCatalog(null); setConfig(null); setErro(""); setAviso(null); setSujo(false);
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      const falhas: string[] = [];
      const [s, c, cat, cfg] = await Promise.all([
        plantRequest<PlantSchema>(`${projetoPath(projectIdAcc)}/schema`, { signal: abort.signal }).catch(e => { if (!isAbort(e)) falhas.push(descreverErro(e, "Schema do projeto")); return null; }),
        plantRequest<{ mapping?: Categoria[] }>(`${projetoPath(projectIdAcc)}/configuration`, { signal: abort.signal }).catch(e => { if (!isAbort(e)) falhas.push(descreverErro(e, "DE/PARA")); return null; }),
        getReportCatalog(projectIdAcc, abort.signal).catch(e => { if (!isAbort(e)) falhas.push(descreverErro(e, "Catálogo de relatórios")); return null; }),
        getReportConfig(projectIdAcc, abort.signal).catch(e => { if (!isAbort(e)) falhas.push(descreverErro(e, "Configuração de relatórios")); return null; }),
      ]);
      if (abort.signal.aborted) return;
      setSchema(s); setCategorias((c?.mapping ?? []).map(m => ({ id: m.id, label: m.label }))); setCatalog(cat); setConfig(cfg);
      if (cfg) setLinhas(Object.fromEntries(REPORT_KINDS.map(r => [r.kind, daConfig(cfg.sources[r.kind])])) as Record<ReportKind, Linha>);
      setErro(falhas.join(" "));
      setLoading(false);
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);

  const tabelasPid = useMemo(() => Object.keys(schema?.pid ?? {}).sort((a, b) => a.localeCompare(b)), [schema]);
  const tabelas3d = useMemo(() => Object.keys(schema?.piping ?? {}).sort((a, b) => a.localeCompare(b)), [schema]);

  const mudar = useCallback((kind: ReportKind, patch: Partial<Linha>) => { setLinhas(l => ({ ...l, [kind]: { ...l[kind], ...patch } })); setSujo(true); setAviso(null); }, []);

  async function salvar() {
    if (!projectIdAcc) return;
    for (const { kind, label } of REPORT_KINDS) {
      const l = linhas[kind];
      if (l.modo === "categoria" && !l.categoryId) { setAviso({ tom: "erro", texto: `${label}: escolha a categoria do DE/PARA ou volte para “Padrão”.` }); return; }
      if (l.modo === "tabelas" && !l.pidTable && !l.pipingTable) { setAviso({ tom: "erro", texto: `${label}: escolha ao menos uma tabela (P&ID ou 3D).` }); return; }
    }
    setSalvando(true); setAviso(null);
    try {
      const salvo = await putReportConfig(projectIdAcc, paraEnvio(linhas));
      setConfig(salvo); setSujo(false);
      try { setCatalog(await getReportCatalog(projectIdAcc)); } catch { /* o catálogo é só informativo aqui */ }
      setAviso({ tom: "ok", texto: "Configuração salva. Os relatórios já usam as novas origens." });
    } catch (e) { setAviso({ tom: "erro", texto: descreverErro(e, "Salvar configuração de relatórios") }); }
    finally { setSalvando(false); }
  }

  const carimbo = formatarUtc(config?.updatedAtUtc);
  const semSchema = !!projectIdAcc && !loading && !schema;

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Plant 3D · Configuração de Relatórios"
        subtitulo="Origem dos dados de cada relatório: categoria do DE/PARA ou tabelas reais do projeto (schema)."
        pj={pj} loading={loading} onRefresh={() => setTick(t => t + 1)} />
      <SemProjeto pj={pj} />
      {erro && <Estado tom="red" titulo="Parte da configuração não carregou">{erro}</Estado>}
      {semSchema && !erro && <Estado tom="amber" titulo="Schema indisponível">Sem versão-base do Dashboard BIM não há tabelas para escolher.</Estado>}

      {projectIdAcc && (
        <section className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
          {loading && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" /> Carregando…</p>}
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-border/50 text-[10px] uppercase text-muted-foreground">
                <tr><th className="px-3 py-2">Relatório</th><th className="px-3 py-2">Origem</th><th className="px-3 py-2">Detalhe</th><th className="px-3 py-2">Situação atual</th></tr>
              </thead>
              <tbody className="divide-y divide-border/30">
                {REPORT_KINDS.map(({ kind, label, rota }) => {
                  const l = linhas[kind];
                  const d = catalog?.reports.find(r => r.kind === kind);
                  return (
                    <tr key={kind} className="align-top">
                      <td className="px-3 py-3 font-bold"><Link className="text-primary hover:underline" to={rota}>{label}</Link></td>
                      <td className="px-3 py-3">
                        <select aria-label={`${label}: origem`} className={INPUT} value={l.modo} disabled={!schema} onChange={e => mudar(kind, { modo: e.target.value as Modo })}>
                          <option value="padrao">Padrão (DE/PARA){kind === "materials" || kind === "supports" ? " — sem padrão" : ""}</option>
                          <option value="categoria">Outra categoria do DE/PARA</option>
                          <option value="tabelas">Tabelas do projeto (schema)</option>
                        </select>
                      </td>
                      <td className="min-w-[280px] px-3 py-3">
                        {l.modo === "categoria" && (
                          <select aria-label={`${label}: categoria`} className={INPUT} value={l.categoryId} onChange={e => mudar(kind, { categoryId: e.target.value })}>
                            <option value="">— escolha —</option>
                            {categorias.map(c => <option key={c.id} value={c.id}>{c.label} ({c.id})</option>)}
                          </select>
                        )}
                        {l.modo === "tabelas" && (
                          <div className="grid gap-2 sm:grid-cols-2">
                            <select aria-label={`${label}: tabela P&ID`} className={INPUT} value={l.pidTable} onChange={e => mudar(kind, { pidTable: e.target.value })}>
                              <option value="">P&ID: (nenhuma)</option>
                              {tabelasPid.map(t => <option key={t} value={t}>P&ID: {t}</option>)}
                            </select>
                            <select aria-label={`${label}: tabela 3D`} className={INPUT} value={l.pipingTable} onChange={e => mudar(kind, { pipingTable: e.target.value })}>
                              <option value="">3D: (nenhuma)</option>
                              {tabelas3d.map(t => <option key={t} value={t}>3D: {t}</option>)}
                            </select>
                          </div>
                        )}
                        {l.modo === "padrao" && <span className="text-xs text-muted-foreground">Usa a categoria padrão do DE/PARA, se existir.</span>}
                      </td>
                      <td className="px-3 py-3 text-xs">
                        {!d ? <span className="text-muted-foreground">—</span>
                          : d.mode === "unmapped" ? <span className="font-bold text-amber-600">Não mapeado</span>
                          : d.mode === "mapped" ? <span>DE/PARA: <b>{d.categoryId}</b> ({d.fields.length} campos)</span>
                          : <span>Tabelas: <b>{[d.pidTable && `P&ID ${d.pidTable}`, d.pipingTable && `3D ${d.pipingTable}`].filter(Boolean).join(" · ")}</b> ({d.fields.length} campos)</span>}
                        {d?.reason && <p className="mt-1 text-muted-foreground">{d.reason}</p>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <Button className="gap-2 font-bold" disabled={salvando || !sujo || !schema} onClick={salvar}>
              {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} SALVAR CONFIGURAÇÃO
            </Button>
            {carimbo && <span className="text-xs text-muted-foreground">Última gravação: {carimbo}</span>}
            {aviso && <span role="status" className={`text-sm font-semibold ${aviso.tom === "erro" ? "text-red-600" : "text-muted-foreground"}`}>{aviso.texto}</span>}
          </div>
          <p className="text-xs text-muted-foreground">
            Regras de filtro globais (Configurações › cada categoria) valem para os relatórios por DE/PARA e também para uma tabela escolhida que seja a tabela dessa categoria.
            Para tabelas fora do DE/PARA (ex.: Materiais/Suportes), só regras salvas sob a chave do relatório (“materials”/“supports”), se existirem, são aplicadas;
            sem regras, a tabela é extraída integralmente (com paginação e limites).
            Veja também <Link className="font-bold text-primary underline" to={PLANT_ROTAS.configuracao.deParaTags}>DE/PARA e TAGs</Link>.
          </p>
        </section>
      )}
    </div>
  );
}
