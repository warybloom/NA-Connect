// Configurações › Catálogo de Padrões e Validação: catálogos por projeto (GET/PUT /standards), valores individuais (+ Novo) ou
// importados de XLSX no navegador, tabela/coluna do P&ID e do 3D escolhidas do /schema real e coluna-chave da linha (TagLine).
// Salvar exige plant3d.database.edit. Catálogo sem valores/fonte NUNCA vira "válido": o backend marca "não avaliado".
import { useEffect, useMemo, useRef, useState } from "react";
import { toast } from "react-hot-toast";
import { AlertTriangle, FileSpreadsheet, Loader2, Lock, Plus, Save, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import { usePlantProjects } from "@/components/plant3d/usePlantProjects";
import { PlantSchema, descreverErro, isAbort, plantRequest, projetoPath } from "@/components/plant3d/plantApi";
import { CATEGORIAS_META, newId } from "@/components/plant3d/plantRulesApi";
import { getStandards, putStandards } from "@/components/plant3d/plantStandardsApi";
import {
  LIMITES_PADROES, PlantStandards, SourceRef, StandardCatalog, TAG_LINE_PADRAO, TIPOS_CATALOGO,
  rotuloTipo, semFonte, tabelasComColuna, unirValores, validarPadroes,
} from "@/components/plant3d/plantStandardsLogic";
import { PlanilhaLida, lerPlanilha, nomesColunas, valoresDaColuna } from "@/components/plant3d/plantStandardsXlsx";
import { Estado, PlantHeader, SemProjeto, usePlantResults } from "./plantShared";

const INPUT = "w-full rounded-md border border-border bg-background px-2 py-1.5 text-sm text-foreground outline-none focus:border-primary disabled:opacity-60";
const LABEL = "text-[10px] font-bold uppercase tracking-wider text-muted-foreground";
const MAX_LINHAS_VALORES = 200;

const clonar = (c: StandardCatalog[]): StandardCatalog[] => c.map(x => ({ ...x, values: [...x.values], pid: { ...x.pid }, piping: { ...x.piping } }));

function novoCatalogo(): StandardCatalog {
  return { id: newId(), name: "", kind: "custom", targetCategory: "", values: [], pid: { table: "", column: "" }, piping: { table: "", column: "" }, enabled: true };
}

/** Seletor tabela+coluna a partir do /schema. Valor salvo fora do schema continua visível (marcado), nunca é descartado em silêncio. */
function SeletorFonte({ rotulo, fonte, schema, desabilitado, onChange }: {
  rotulo: string; fonte: SourceRef; schema: Record<string, string[]> | null; desabilitado: boolean; onChange: (f: SourceRef) => void;
}) {
  const tabelas = Object.keys(schema ?? {}).sort((a, b) => a.localeCompare(b, "pt-BR"));
  const colunas = schema?.[fonte.table] ?? [];
  const tabelaFora = !!fonte.table && !tabelas.includes(fonte.table);
  const colunaFora = !!fonte.column && !colunas.includes(fonte.column);
  return (
    <div className="space-y-1">
      <span className={LABEL}>{rotulo}</span>
      <div className="grid grid-cols-2 gap-2">
        <select className={INPUT} disabled={desabilitado} aria-label={`${rotulo}: tabela`} value={fonte.table}
          onChange={e => onChange({ table: e.target.value, column: "" })}>
          <option value="">— tabela —</option>
          {tabelaFora && <option value={fonte.table}>{fonte.table} (fora do schema)</option>}
          {tabelas.map(t => <option key={t} value={t}>{t}</option>)}
        </select>
        <select className={INPUT} disabled={desabilitado || !fonte.table} aria-label={`${rotulo}: coluna`} value={fonte.column}
          onChange={e => onChange({ ...fonte, column: e.target.value })}>
          <option value="">— coluna —</option>
          {colunaFora && <option value={fonte.column}>{fonte.column} (fora do schema)</option>}
          {colunas.map(c => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>
    </div>
  );
}

/** Importação XLSX/CSV (processada aqui; só a lista de valores segue no PUT). */
function ImportadorPlanilha({ onAplicar, onFechar }: { onAplicar: (valores: string[], substituir: boolean) => void; onFechar: () => void }) {
  const [planilha, setPlanilha] = useState<PlanilhaLida | null>(null);
  const [lendo, setLendo] = useState(false);
  const [erro, setErro] = useState("");
  const [aba, setAba] = useState(0);
  const [cabecalho, setCabecalho] = useState(true);
  const [coluna, setColuna] = useState(0);
  const entrada = useRef<HTMLInputElement>(null);

  const abrir = async (arquivo: File | undefined) => {
    if (!arquivo) return;
    setLendo(true); setErro(""); setPlanilha(null);
    try { const p = await lerPlanilha(arquivo); setPlanilha(p); setAba(0); setColuna(0); }
    catch (e) { setErro(e instanceof Error ? e.message : String(e)); }
    finally { setLendo(false); if (entrada.current) entrada.current.value = ""; }
  };

  const atual = planilha?.abas[aba] ?? null;
  const colunas = useMemo(() => (atual ? nomesColunas(atual, cabecalho) : []), [atual, cabecalho]);
  const bruto = useMemo(() => (atual ? valoresDaColuna(atual, coluna, cabecalho) : []), [atual, coluna, cabecalho]);
  const unico = useMemo(() => unirValores(bruto), [bruto]);

  return (
    <div className="space-y-3 rounded-lg border border-border/60 bg-secondary/20 p-4">
      <div className="flex items-center justify-between gap-2">
        <b className="text-sm">Importar planilha (.xlsx, .xls, .csv)</b>
        <Button variant="ghost" size="sm" aria-label="Fechar importação" onClick={onFechar}><X className="h-4 w-4" /></Button>
      </div>
      <input ref={entrada} type="file" accept=".xlsx,.xls,.csv" aria-label="Arquivo da planilha" onChange={e => void abrir(e.target.files?.[0])} className="text-sm" />
      <p className="text-xs text-muted-foreground">
        O arquivo é lido no seu navegador (máx. {Math.round(LIMITES_PADROES.arquivoBytes / 1024 / 1024)} MB, {LIMITES_PADROES.linhasPlanilha.toLocaleString("pt-BR")} linhas); fórmulas não são executadas e nada é enviado antes de salvar.
      </p>
      {lendo && <p className="flex items-center gap-2 text-sm"><Loader2 className="h-4 w-4 animate-spin" /> Lendo…</p>}
      {erro && <p role="alert" className="text-sm font-semibold text-red-600">{erro}</p>}
      {planilha && atual && (
        <div className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="space-y-1"><span className={LABEL}>Aba</span>
              <select className={INPUT} value={aba} onChange={e => { setAba(Number(e.target.value)); setColuna(0); }}>
                {planilha.abas.map((a, i) => <option key={a.nome} value={i}>{a.nome}</option>)}
              </select></label>
            <label className="space-y-1"><span className={LABEL}>Coluna</span>
              <select className={INPUT} value={coluna} onChange={e => setColuna(Number(e.target.value))}>
                {colunas.map((c, i) => <option key={i} value={i}>{c}</option>)}
              </select></label>
            <label className="flex items-end gap-2 pb-1.5 text-sm"><input type="checkbox" checked={cabecalho} onChange={e => setCabecalho(e.target.checked)} /> 1ª linha é cabeçalho</label>
          </div>
          {atual.truncada && <p className="text-xs font-semibold text-amber-700">Aba truncada em {LIMITES_PADROES.linhasPlanilha.toLocaleString("pt-BR")} linhas.</p>}
          <p className="text-sm">
            <b>{unico.valores.length.toLocaleString("pt-BR")}</b> valores distintos · {unico.duplicados.toLocaleString("pt-BR")} repetidos e {unico.vazios.toLocaleString("pt-BR")} vazios descartados.
          </p>
          <div className="flex max-h-28 flex-wrap gap-1 overflow-auto">
            {unico.valores.slice(0, 40).map(v => <span key={v} className="rounded bg-background px-1.5 py-0.5 text-xs">{v}</span>)}
            {unico.valores.length > 40 && <span className="text-xs text-muted-foreground">… +{unico.valores.length - 40}</span>}
          </div>
          <div className="flex flex-wrap gap-2">
            <Button size="sm" disabled={unico.valores.length === 0} onClick={() => onAplicar(unico.valores, false)}>Adicionar aos valores</Button>
            <Button size="sm" variant="outline" disabled={unico.valores.length === 0} onClick={() => onAplicar(unico.valores, true)}>Substituir valores</Button>
          </div>
        </div>
      )}
    </div>
  );
}

export default function PlantCatalogoPage() {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const { can } = useProjectPermissions(projectIdAcc);
  const podeEditar = can("plant3d.database.edit");
  const { results } = usePlantResults(projectIdAcc);

  const [salvo, setSalvo] = useState<PlantStandards | null>(null);
  const [catalogos, setCatalogos] = useState<StandardCatalog[]>([]);
  const [tagLine, setTagLine] = useState(TAG_LINE_PADRAO);
  const [schema, setSchema] = useState<PlantSchema | null>(null);
  const [schemaErro, setSchemaErro] = useState("");
  const [loading, setLoading] = useState(false);
  const [loadErro, setLoadErro] = useState("");
  const [saveErro, setSaveErro] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [sel, setSel] = useState("");
  const [importando, setImportando] = useState(false);
  const [filtro, setFiltro] = useState("");
  const [novoValor, setNovoValor] = useState("");
  const [tick, setTick] = useState(0);

  useEffect(() => {
    setSalvo(null); setCatalogos([]); setTagLine(TAG_LINE_PADRAO); setSchema(null); setSchemaErro(""); setLoadErro(""); setSaveErro(""); setSel("");
    if (!projectIdAcc) return;
    const abort = new AbortController();
    setLoading(true);
    (async () => {
      const [padroes, esquema] = await Promise.allSettled([
        getStandards(projectIdAcc, abort.signal),
        plantRequest<PlantSchema>(projetoPath(projectIdAcc) + "/schema", { signal: abort.signal }),
      ]);
      if (abort.signal.aborted) return;
      if (padroes.status === "fulfilled") {
        setSalvo(padroes.value); setCatalogos(clonar(padroes.value.catalogs)); setTagLine(padroes.value.tagLineColumn);
        setSel(padroes.value.catalogs[0]?.id ?? "");
      } else if (!isAbort(padroes.reason)) setLoadErro(descreverErro(padroes.reason, "Catálogo de padrões"));
      if (esquema.status === "fulfilled") setSchema(esquema.value);
      else if (!isAbort(esquema.reason)) setSchemaErro(descreverErro(esquema.reason, "Schema do projeto"));
      setLoading(false);
    })();
    return () => abort.abort();
  }, [projectIdAcc, tick]);

  const pidCols = schema?.pid ?? null;
  const pipingCols = schema?.piping ?? null;
  const categorias = useMemo(() => {
    const mapa = new Map<string, string>(CATEGORIAS_META.map(c => [c.id, c.label]));
    results?.categories?.forEach(c => mapa.set(c.id, c.label));
    catalogos.forEach(c => { if (c.targetCategory && !mapa.has(c.targetCategory)) mapa.set(c.targetCategory, c.targetCategory); });
    return [...mapa.entries()].map(([id, label]) => ({ id, label }));
  }, [results, catalogos]);

  const validacao = useMemo(() => validarPadroes({ catalogs: catalogos, tagLineColumn: tagLine }, pidCols ?? {}, pipingCols ?? {}, []), [catalogos, tagLine, pidCols, pipingCols]);
  const totalErros = validacao.global.length + validacao.porCatalogo.reduce((n, v) => n + v.erros.length, 0);
  const sujo = !!salvo && JSON.stringify({ c: catalogos, t: tagLine }) !== JSON.stringify({ c: salvo.catalogs, t: salvo.tagLineColumn });
  const idx = catalogos.findIndex(c => c.id === sel);
  const cat = idx >= 0 ? catalogos[idx] : null;
  const val = idx >= 0 ? validacao.porCatalogo[idx] : null;
  const bloqueado = !podeEditar;

  const tabsPid = useMemo(() => tabelasComColuna(pidCols ?? {}, tagLine), [pidCols, tagLine]);
  const tabsPiping = useMemo(() => tabelasComColuna(pipingCols ?? {}, tagLine), [pipingCols, tagLine]);

  const alterar = (fn: (c: StandardCatalog) => StandardCatalog) => setCatalogos(l => l.map(c => (c.id === sel ? fn(c) : c)));
  const adicionar = () => {
    if (catalogos.length >= LIMITES_PADROES.catalogos) { toast.error(`Máximo de ${LIMITES_PADROES.catalogos} catálogos.`); return; }
    const c = novoCatalogo();
    setCatalogos(l => [...l, c]); setSel(c.id); setImportando(false);
  };
  const remover = () => {
    if (!cat) return;
    const resto = catalogos.filter(c => c.id !== cat.id);
    setCatalogos(resto); setSel(resto[0]?.id ?? ""); setImportando(false);
  };
  const incluirValor = () => {
    const { valores } = unirValores(cat?.values ?? [], [novoValor]);
    if (valores.length > LIMITES_PADROES.valoresPorCatalogo) { toast.error(`Máximo de ${LIMITES_PADROES.valoresPorCatalogo} valores por catálogo.`); return; }
    if (!novoValor.trim()) return;
    alterar(c => ({ ...c, values: valores })); setNovoValor("");
  };
  const aplicarImportacao = (valores: string[], substituir: boolean) => {
    const { valores: unidos, duplicados } = unirValores(substituir ? [] : cat?.values ?? [], valores);
    if (unidos.length > LIMITES_PADROES.valoresPorCatalogo) { toast.error(`Resultado excederia ${LIMITES_PADROES.valoresPorCatalogo} valores; reduza a planilha.`); return; }
    alterar(c => ({ ...c, values: unidos }));
    setImportando(false);
    toast.success(`${unidos.length} valores no catálogo${duplicados ? ` (${duplicados} já existiam)` : ""}. Salve para gravar.`);
  };

  const salvar = async () => {
    if (!podeEditar || salvando || totalErros > 0 || !salvo) return;
    setSalvando(true); setSaveErro("");
    try {
      const r = await putStandards(projectIdAcc, { catalogs: catalogos, tagLineColumn: tagLine });
      setSalvo(r); setCatalogos(clonar(r.catalogs)); setTagLine(r.tagLineColumn);
      if (!r.catalogs.some(c => c.id === sel)) setSel(r.catalogs[0]?.id ?? "");
      toast.success("Catálogo de padrões salvo.");
    } catch (e) {
      const msg = descreverErro(e, "Catálogo não foi salvo");
      setSaveErro(msg); toast.error(msg, { duration: 7000 });
    } finally { setSalvando(false); }
  };

  const valoresVisiveis = useMemo(() => {
    if (!cat) return [];
    const f = filtro.trim().toLowerCase();
    return cat.values.map((v, i) => ({ v, i })).filter(x => !f || x.v.toLowerCase().includes(f)).slice(0, MAX_LINHAS_VALORES);
  }, [cat, filtro]);

  return (
    <div className="space-y-6">
      <PlantHeader titulo="Catálogo de Padrões e Validação" subtitulo="Valores permitidos por tipo e a tabela/coluna do P&ID e do 3D que serão validados."
        pj={pj} loading={loading} onRefresh={() => setTick(t => t + 1)}
        extra={sujo && <span className="text-xs font-semibold text-amber-700 dark:text-amber-400">Alterações não salvas</span>} />
      <SemProjeto pj={pj} />
      {loading && <div className="flex justify-center p-6"><Loader2 className="h-8 w-8 animate-spin text-primary" /></div>}
      {loadErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Catálogo indisponível">{loadErro} Salvar fica bloqueado para não sobrescrever o que já existe.</Estado>}
      {schemaErro && <Estado tom="amber" titulo="Schema do projeto indisponível">{schemaErro} Sem o schema não é possível escolher tabela/coluna novas; as já salvas continuam visíveis.</Estado>}
      {projectIdAcc && !podeEditar && <Estado tom="blue" icon={<Lock className="h-5 w-5" />} titulo="Somente leitura">Alterar o catálogo exige a permissão <code>plant3d.database.edit</code>.</Estado>}

      {projectIdAcc && salvo && (
        <>
          <section className="glass-card space-y-3 rounded-xl border border-border/50 p-5">
            <h3 className="text-sm font-bold">Atributo-chave da linha</h3>
            <div className="flex flex-wrap items-end gap-3">
              <label className="min-w-[220px] space-y-1"><span className={LABEL}>Nome da coluna (sem diferenciar maiúsculas)</span>
                <input className={INPUT} disabled={bloqueado} value={tagLine} maxLength={LIMITES_PADROES.tamanhoColuna} onChange={e => setTagLine(e.target.value)} /></label>
              {podeEditar && tagLine !== TAG_LINE_PADRAO && <Button variant="outline" size="sm" onClick={() => setTagLine(TAG_LINE_PADRAO)}>Restaurar “{TAG_LINE_PADRAO}”</Button>}
            </div>
            {schema ? (
              <p className="text-xs text-muted-foreground">
                Encontrada em {tabsPid.length} tabela(s) do P&ID e {tabsPiping.length} do 3D no schema atual.
                {tabsPid.length + tabsPiping.length === 0 && " Nenhuma coluna com esse nome: validações por linha ficarão não avaliadas."}
              </p>
            ) : <p className="text-xs text-muted-foreground">Sem schema carregado: a existência da coluna não pôde ser conferida.</p>}
          </section>

          <div className="grid gap-4 lg:grid-cols-[18rem_minmax(0,1fr)]">
            <aside className="glass-card space-y-2 rounded-xl border border-border/50 p-4">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-sm font-bold">Catálogos ({catalogos.length})</h3>
                {podeEditar && <Button size="sm" variant="outline" className="gap-1" onClick={adicionar}><Plus className="h-4 w-4" /> Novo</Button>}
              </div>
              {catalogos.length === 0 && <p className="text-sm text-muted-foreground">Nenhum catálogo cadastrado. Sem catálogo, os itens ficam “não avaliados”.</p>}
              <ul className="space-y-1">
                {catalogos.map((c, i) => (
                  <li key={c.id}>
                    <button type="button" onClick={() => { setSel(c.id); setImportando(false); setFiltro(""); }} aria-current={c.id === sel}
                      className={`w-full rounded-md border px-3 py-2 text-left text-sm transition-colors ${c.id === sel ? "border-primary bg-primary/5" : "border-border/50 hover:border-primary/60"}`}>
                      <b className="block truncate">{c.name || "(sem nome)"}</b>
                      <span className="text-xs text-muted-foreground">
                        {rotuloTipo(c.kind)} · {c.values.length.toLocaleString("pt-BR")} valores{!c.enabled && " · inativo"}
                        {(validacao.porCatalogo[i]?.erros.length ?? 0) > 0 && <span className="font-semibold text-red-600"> · com erro</span>}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </aside>

            <section className="glass-card space-y-4 rounded-xl border border-border/50 p-5">
              {!cat ? <p className="text-sm text-muted-foreground">Selecione ou crie um catálogo.</p> : (
                <>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <label className="space-y-1"><span className={LABEL}>Nome</span>
                      <input className={INPUT} disabled={bloqueado} value={cat.name} maxLength={LIMITES_PADROES.tamanhoNome} placeholder="Ex.: Spec de tubulação"
                        onChange={e => alterar(c => ({ ...c, name: e.target.value }))} /></label>
                    <label className="space-y-1"><span className={LABEL}>Tipo</span>
                      <select className={INPUT} disabled={bloqueado} value={cat.kind} onChange={e => alterar(c => ({ ...c, kind: e.target.value }))}>
                        {!TIPOS_CATALOGO.some(t => t.id === cat.kind) && <option value={cat.kind}>{cat.kind}</option>}
                        {TIPOS_CATALOGO.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
                      </select></label>
                    <label className="space-y-1"><span className={LABEL}>Categoria validada</span>
                      <select className={INPUT} disabled={bloqueado} value={cat.targetCategory} onChange={e => alterar(c => ({ ...c, targetCategory: e.target.value }))}>
                        <option value="">— escolha —</option>
                        {categorias.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
                      </select></label>
                    <label className="flex items-end gap-2 pb-1.5 text-sm"><input type="checkbox" disabled={bloqueado} checked={cat.enabled} onChange={e => alterar(c => ({ ...c, enabled: e.target.checked }))} /> Catálogo ativo</label>
                  </div>

                  <div className="grid gap-3 sm:grid-cols-2">
                    <SeletorFonte rotulo="Origem no P&ID" fonte={cat.pid} schema={pidCols} desabilitado={bloqueado || !pidCols && !cat.pid.table} onChange={f => alterar(c => ({ ...c, pid: f }))} />
                    <SeletorFonte rotulo="Origem no 3D (Piping)" fonte={cat.piping} schema={pipingCols} desabilitado={bloqueado || !pipingCols && !cat.piping.table} onChange={f => alterar(c => ({ ...c, piping: f }))} />
                  </div>
                  {semFonte(cat) && <p className="text-xs text-muted-foreground">Sem tabela/coluna de origem o catálogo não valida nada.</p>}

                  <div className="space-y-2">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className={LABEL}>Valores permitidos ({cat.values.length.toLocaleString("pt-BR")})</span>
                      {podeEditar && <Button size="sm" variant="outline" className="gap-1" onClick={() => setImportando(v => !v)}><FileSpreadsheet className="h-4 w-4" /> Importar Excel</Button>}
                    </div>
                    {importando && podeEditar && <ImportadorPlanilha onAplicar={aplicarImportacao} onFechar={() => setImportando(false)} />}
                    {podeEditar && (
                      <div className="flex gap-2">
                        <input className={INPUT} value={novoValor} maxLength={LIMITES_PADROES.tamanhoValor} placeholder="Novo valor individual" aria-label="Novo valor"
                          onChange={e => setNovoValor(e.target.value)} onKeyDown={e => { if (e.key === "Enter") { e.preventDefault(); incluirValor(); } }} />
                        <Button size="sm" className="gap-1" disabled={!novoValor.trim()} onClick={incluirValor}><Plus className="h-4 w-4" /> Novo</Button>
                      </div>
                    )}
                    {cat.values.length > 8 && <input className={INPUT} value={filtro} placeholder="Filtrar valores…" aria-label="Filtrar valores" onChange={e => setFiltro(e.target.value)} />}
                    {cat.values.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum valor cadastrado.</p> : (
                      <ul className="grid max-h-80 gap-1 overflow-auto sm:grid-cols-2">
                        {valoresVisiveis.map(({ v, i }) => (
                          <li key={i} className="flex items-center gap-1">
                            <input className={INPUT} disabled={bloqueado} value={v} maxLength={LIMITES_PADROES.tamanhoValor} aria-label={`Valor ${i + 1}`}
                              onChange={e => alterar(c => ({ ...c, values: c.values.map((x, k) => (k === i ? e.target.value : x)) }))} />
                            {podeEditar && <Button variant="ghost" size="sm" aria-label={`Remover valor ${v}`} onClick={() => alterar(c => ({ ...c, values: c.values.filter((_, k) => k !== i) }))}><X className="h-4 w-4" /></Button>}
                          </li>
                        ))}
                      </ul>
                    )}
                    {cat.values.length > MAX_LINHAS_VALORES && <p className="text-xs text-muted-foreground">Exibindo até {MAX_LINHAS_VALORES} valores por vez; use o filtro. Duplicatas e vazios são removidos ao salvar.</p>}
                  </div>

                  {val && val.erros.length > 0 && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Corrija este catálogo"><ul className="list-disc pl-4">{val.erros.map(m => <li key={m}>{m}</li>)}</ul></Estado>}
                  {val && val.avisos.length > 0 && <Estado tom="amber" titulo="Atenção"><ul className="list-disc pl-4">{val.avisos.map(m => <li key={m}>{m}</li>)}</ul></Estado>}
                  {podeEditar && <div className="flex justify-end"><Button variant="ghost" size="sm" className="gap-1 text-destructive" onClick={remover}><Trash2 className="h-4 w-4" /> Excluir catálogo</Button></div>}
                </>
              )}
            </section>
          </div>

          {validacao.global.length > 0 && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Corrija antes de salvar"><ul className="list-disc pl-4">{validacao.global.map(m => <li key={m}>{m}</li>)}</ul></Estado>}
          {saveErro && <Estado tom="red" icon={<AlertTriangle className="h-5 w-5" />} titulo="Não salvo">{saveErro}</Estado>}
          {podeEditar && (
            <div className="flex justify-end gap-2">
              <Button variant="outline" disabled={!sujo || salvando} onClick={() => { setCatalogos(clonar(salvo.catalogs)); setTagLine(salvo.tagLineColumn); setSaveErro(""); }}>Descartar</Button>
              <Button className="gap-2 font-bold" disabled={!sujo || salvando || totalErros > 0} onClick={salvar}>
                {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} SALVAR CATÁLOGO
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
