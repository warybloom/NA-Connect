// Configuração DE/PARA + TAGs (Plant 3D). Backend: /api/plant-integration/* (CONTRATO_02).
// Chave de projeto = Project_ID_ACC. Nada é salvo/simulado fora do PUT /configuration; tabelas/colunas vêm do GET /schema.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Save, Database, Loader2, Plus, Trash2, RefreshCw, AlertCircle, AlertTriangle, LayoutList, Key, ListTree,
  ArrowUp, ArrowDown, Lock, Info,
} from "lucide-react";
import { toast } from "react-hot-toast";
import { motion } from "framer-motion";
import { useProjectPermissions } from "@/features/permissions/PermissionProvider";
import PlantLinkPanel from "./PlantLinkPanel";
import { usePlantProjects } from "./usePlantProjects";
import {
  AttributeMap, CategoryMapping, PlantApiError, PlantSchema, descreverErro, formatarUtc, isAbort, plantRequest, projetoPath,
} from "./plantApi";

type Columns = Record<string, string[]>;
type CatCheck = { errors: string[]; warnings: string[] };

const newId = () => (typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : Math.random().toString(36).slice(2));

// Modelo inicial SUGERIDO, usado só quando o projeto ainda não tem configuração salva. Não é gravado até o usuário salvar.
// Sugestões não persistidas: apenas TAGs confirmadas no schema real de CHS001.
// Válvulas/instrumentos 3D herdam Tag de PipeRunComponent via PnPID no backend.
const DEFAULT_MAPPINGS: CategoryMapping[] = [
  { id: "equip", label: "Equipamentos", pidTable: "Equipment", pipingTable: "Equipment", isComposedTag: false, delimiter: "-",
    attributes: [{ id: "tag-fixa", name: "TAG Completa", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true }] },
  { id: "valve", label: "Válvulas", pidTable: "HandValves", pipingTable: "Valve", isComposedTag: false, delimiter: "-",
    attributes: [{ id: "tag-fixa", name: "TAG Completa", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true }] },
  { id: "inst", label: "Instrumentos", pidTable: "Instrumentation", pipingTable: "Instrument", isComposedTag: false, delimiter: "-",
    attributes: [{ id: "tag-fixa", name: "TAG Completa", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true }] },
  { id: "line", label: "Linhas", pidTable: "PipeLineGroup", pipingTable: "P3dLineGroup", isComposedTag: false, delimiter: "-",
    attributes: [{ id: "tag-fixa", name: "TAG Completa", pidCol: "Tag", pipingCol: "Tag", isTagPart: true, isFixedTag: true }] },
];

const fromServer = (list: CategoryMapping[]): CategoryMapping[] =>
  list.map(c => ({
    ...c,
    delimiter: c.delimiter ?? "-",
    isComposedTag: c.isComposedTag ?? false,
    attributes: (c.attributes ?? []).map(a => ({ ...a, id: a.id || newId() })),
  }));

/** Forma enviada ao PUT: TAG simples = um único atributo isTagPart+isFixedTag; composta = partes isTagPart na ordem do array. */
const paraEnvio = (list: CategoryMapping[]): CategoryMapping[] =>
  list.map(c => ({
    id: c.id.trim(),
    label: c.label.trim(),
    pidTable: c.pidTable.trim(),
    pipingTable: c.pipingTable.trim(),
    isComposedTag: !!c.isComposedTag,
    delimiter: c.isComposedTag ? (c.delimiter ?? "") : undefined,
    attributes: c.attributes.map(a => ({
      id: a.id, name: a.name.trim(), pidCol: a.pidCol.trim(), pipingCol: a.pipingCol.trim(),
      isTagPart: !!a.isTagPart,
      isFixedTag: !c.isComposedTag && !!a.isTagPart,
    })),
  }));

function validar(list: CategoryMapping[], pid: Columns, piping: Columns, temSchema: boolean): CatCheck[] {
  const ids = new Map<string, number>();
  list.forEach(c => ids.set(c.id, (ids.get(c.id) ?? 0) + 1));
  return list.map(c => {
    const errors: string[] = [];
    const warnings: string[] = [];
    if (!c.id.trim()) errors.push("Categoria sem identificador.");
    if ((ids.get(c.id) ?? 0) > 1) errors.push(`Identificador "${c.id}" duplicado entre categorias.`);
    if (!c.pidTable) errors.push("Escolha a tabela P&ID.");
    if (!c.pipingTable) errors.push("Escolha a tabela 3D.");
    if (temSchema) {
      if (c.pidTable && !pid[c.pidTable]) errors.push(`Tabela P&ID "${c.pidTable}" não existe na versão-base atual.`);
      if (c.pipingTable && !piping[c.pipingTable]) errors.push(`Tabela 3D "${c.pipingTable}" não existe na versão-base atual.`);
    }
    const partes = c.attributes.filter(a => a.isTagPart);
    if (partes.length === 0) errors.push("Defina ao menos uma coluna de TAG.");
    if (!c.isComposedTag && partes.length > 1) errors.push("TAG única aceita apenas uma coluna.");
    if (c.isComposedTag && partes.length === 1) warnings.push("TAG composta com uma só parte equivale a TAG única.");
    if (c.isComposedTag && partes.length > 1 && !(c.delimiter ?? "")) warnings.push("Separador vazio: as partes serão coladas sem separação.");
    const checarColuna = (col: string, tabela: string, colunas: Columns, lado: string, nome: string) => {
      if (temSchema && col && colunas[tabela] && !colunas[tabela].includes(col)) errors.push(`${nome}: coluna ${lado} "${col}" não existe em ${tabela}.`);
    };
    c.attributes.forEach((a, i) => {
      const nome = a.name.trim() || `Atributo ${i + 1}`;
      if (a.isTagPart) {
        if (!a.pidCol || !a.pipingCol) errors.push(`${nome}: TAG precisa de coluna no P&ID e no 3D.`);
      } else {
        if (!a.name.trim()) errors.push(`Atributo extra ${i + 1} sem nome.`);
        if (!!a.pidCol !== !!a.pipingCol) errors.push(`${nome}: informe as duas colunas (P&ID e 3D) ou nenhuma.`);
        if (!a.pidCol && !a.pipingCol) warnings.push(`${nome}: sem colunas — será ignorado na comparação.`);
      }
      checarColuna(a.pidCol, c.pidTable, pid, "P&ID", nome);
      checarColuna(a.pipingCol, c.pipingTable, piping, "3D", nome);
    });
    const nomes = new Map<string, number>();
    c.attributes.filter(a => !a.isTagPart && a.name.trim()).forEach(a => nomes.set(a.name.trim().toLowerCase(), (nomes.get(a.name.trim().toLowerCase()) ?? 0) + 1));
    nomes.forEach((n, nome) => { if (n > 1) errors.push(`Atributo extra "${nome}" duplicado.`); });
    return { errors, warnings };
  });
}

function opcoes(atual: string, disponiveis: string[] | undefined, schemaOk: boolean) {
  const lista = disponiveis ?? [];
  return (
    <>
      {(!atual || (schemaOk && !lista.includes(atual))) && <option value={atual}>{atual ? `${atual} (inexistente no schema)` : "-- Escolha --"}</option>}
      {!schemaOk && atual && <option value={atual}>{atual}</option>}
      {lista.map(c => <option key={c} value={c}>{c}</option>)}
    </>
  );
}

const SELECT = "w-full rounded-lg border bg-background px-3 py-2 text-sm font-semibold text-foreground outline-none disabled:opacity-60";

export default function TabSettings() {
  const pj = usePlantProjects();
  const { projectIdAcc } = pj;
  const { can } = useProjectPermissions(projectIdAcc);
  const podeSalvar = can("plant3d.mapping.view") && can("plant3d.database.edit");
  const podeNomenclatura = can("plant3d.naming.view");
  const podeVerCatalogo = can("plant3d.database.view");

  const [mappings, setMappings] = useState<CategoryMapping[]>([]);
  const [savedJson, setSavedJson] = useState("");
  const [usandoModelo, setUsandoModelo] = useState(false);
  const [schema, setSchema] = useState<PlantSchema | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [configOk, setConfigOk] = useState(false);
  const [configErro, setConfigErro] = useState("");
  const [schemaErro, setSchemaErro] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [saveErro, setSaveErro] = useState("");
  const [loadTick, setLoadTick] = useState(0);

  const pid = schema?.pid ?? {};
  const piping = schema?.piping ?? {};
  const temSchema = Object.keys(pid).length > 0 && Object.keys(piping).length > 0;

  // Carga: schema + configuração. Só dispara com o vínculo conhecido (ou lista de projetos indisponível).
  useEffect(() => {
    setMappings([]); setSavedJson(""); setUsandoModelo(false); setSchema(null);
    setConfigOk(false); setConfigErro(""); setSchemaErro(""); setSaveErro("");
    if (!projectIdAcc || !pj.loaded || pj.naoVinculado === true) return;
    const abort = new AbortController();
    setCarregando(true);
    (async () => {
      const base = projetoPath(projectIdAcc);
      const [sch, cfg] = await Promise.allSettled([
        plantRequest<PlantSchema>(base + "/schema", { signal: abort.signal }),
        plantRequest<{ mapping: CategoryMapping[] }>(base + "/configuration", { signal: abort.signal }),
      ]);
      if (abort.signal.aborted) return;
      if (sch.status === "fulfilled") setSchema(sch.value);
      else if (!isAbort(sch.reason)) {
        setSchemaErro(descreverErro(sch.reason, "Schema P&ID/3D"));
        if (sch.reason instanceof PlantApiError && sch.reason.status === 404) pj.reload(); // pode ser vínculo desfeito
      }
      if (cfg.status === "fulfilled") {
        const salvo = Array.isArray(cfg.value.mapping) ? cfg.value.mapping : [];
        if (salvo.length) {
          const lista = fromServer(salvo);
          setMappings(lista); setSavedJson(JSON.stringify(paraEnvio(lista)));
        } else {
          setMappings(structuredClone(DEFAULT_MAPPINGS)); setSavedJson(""); setUsandoModelo(true);
        }
        setConfigOk(true);
      } else if (!isAbort(cfg.reason)) setConfigErro(descreverErro(cfg.reason, "Configuração salva"));
      setCarregando(false);
    })();
    return () => abort.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectIdAcc, pj.loaded, pj.naoVinculado, loadTick]);

  const recarregar = useCallback(() => setLoadTick(t => t + 1), []);

  const checks = useMemo(() => validar(mappings, pid, piping, temSchema), [mappings, pid, piping, temSchema]);
  const totalErros = checks.reduce((n, c) => n + c.errors.length, 0);
  const sujo = configOk && JSON.stringify(paraEnvio(mappings)) !== savedJson;
  const bloqueado = !podeSalvar;

  const patchCat = (idx: number, fn: (c: CategoryMapping) => CategoryMapping) =>
    setMappings(prev => prev.map((c, i) => (i === idx ? fn(c) : c)));
  const patchAttr = (idx: number, attrId: string, patch: Partial<AttributeMap>) =>
    patchCat(idx, c => ({ ...c, attributes: c.attributes.map(a => (a.id === attrId ? { ...a, ...patch } : a)) }));

  const alternarComposicao = (idx: number, composta: boolean) => patchCat(idx, c => {
    if (composta) {
      const attrs = c.attributes.some(a => a.isTagPart)
        ? c.attributes
        : [{ id: newId(), name: "", pidCol: "", pipingCol: "", isTagPart: true }, ...c.attributes];
      return { ...c, isComposedTag: true, delimiter: c.delimiter ?? "-", attributes: attrs };
    }
    const primeira = c.attributes.find(a => a.isTagPart);
    const attrs = primeira
      ? c.attributes.map(a => (a === primeira ? { ...a, isTagPart: true, isFixedTag: true } : { ...a, isTagPart: false, isFixedTag: false }))
      : [{ id: newId(), name: "TAG Completa", pidCol: "", pipingCol: "", isTagPart: true, isFixedTag: true }, ...c.attributes];
    return { ...c, isComposedTag: false, attributes: attrs };
  });

  const moverParte = (idx: number, attrId: string, dir: -1 | 1) => patchCat(idx, c => {
    const posicoes = c.attributes.map((a, i) => (a.isTagPart ? i : -1)).filter(i => i >= 0);
    const k = posicoes.findIndex(p => c.attributes[p].id === attrId);
    const alvo = k + dir;
    if (k < 0 || alvo < 0 || alvo >= posicoes.length) return c;
    const attrs = [...c.attributes];
    [attrs[posicoes[k]], attrs[posicoes[alvo]]] = [attrs[posicoes[alvo]], attrs[posicoes[k]]];
    return { ...c, attributes: attrs };
  });

  const adicionarParte = (idx: number) => patchCat(idx, c => {
    const ultima = c.attributes.map((a, i) => (a.isTagPart ? i : -1)).reduce((m, i) => Math.max(m, i), -1);
    const attrs = [...c.attributes];
    attrs.splice(ultima + 1, 0, { id: newId(), name: "", pidCol: "", pipingCol: "", isTagPart: true });
    return { ...c, attributes: attrs };
  });

  const removerAttr = (idx: number, attrId: string) => patchCat(idx, c => ({ ...c, attributes: c.attributes.filter(a => a.id !== attrId) }));

  const salvar = async () => {
    if (!projectIdAcc || !configOk || !temSchema || bloqueado || totalErros > 0) return;
    setSalvando(true); setSaveErro("");
    try {
      const envio = paraEnvio(mappings);
      await plantRequest<{ success: boolean }>(projetoPath(projectIdAcc) + "/configuration", {
        method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ mapping: envio }),
      });
      setSavedJson(JSON.stringify(envio)); setUsandoModelo(false);
      toast.success("Configuração DE/PARA e TAGs salva.");
    } catch (error) {
      const msg = descreverErro(error, "Configuração não foi salva");
      setSaveErro(msg);
      toast.error(msg, { duration: 7000 });
    } finally {
      setSalvando(false);
    }
  };

  const ultimaPid = schema?.latest?.pnid;
  const ultimaPiping = schema?.latest?.piping;
  const descreverVersao = (v?: { atUtc?: string | null; rows?: number } | null) =>
    v ? `${v.rows ?? "?"} linhas · ${formatarUtc(v.atUtc) ?? "data não informada"}` : "sem versão";

  return (
    <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="space-y-6">
      <div className="glass-card rounded-xl border border-border/50 p-6">
        <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
          <div className="flex-1">
            <h2 className="flex items-center gap-2 text-xl font-bold text-primary">
              <Database className="h-6 w-6" /> Configuração DE/PARA e TAGs
            </h2>
            {!pj.temProjetoAtual ? (
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <label htmlFor="projectSelect" className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                  <LayoutList className="h-4 w-4" /> Selecionar Projeto:
                </label>
                <select
                  id="projectSelect" aria-label="Selecionar Projeto" title="Selecionar Projeto"
                  value={pj.escolhido} onChange={e => pj.setEscolhido(e.target.value)}
                  className="min-w-[260px] rounded-lg border border-border bg-background px-3 py-2 text-sm font-bold text-foreground outline-none focus:ring-2 focus:ring-primary"
                >
                  <option value="">-- Escolha um Projeto Disponível --</option>
                  {pj.projects.map(p => (
                    <option key={p.projectIdAcc} value={p.projectIdAcc}>{p.name || p.projectIdAcc}{p.dashboardProjectId ? "" : " — não vinculado"}</option>
                  ))}
                </select>
              </div>
            ) : (
              <p className="mt-3 text-sm text-muted-foreground">
                Projeto atual: <b className="text-foreground">{pj.nome || projectIdAcc}</b>
                {pj.naoVinculado === true && <span className="ml-2 rounded-full bg-amber-500/15 px-2 py-0.5 text-xs font-semibold text-amber-700 dark:text-amber-400">não vinculado ao Dashboard</span>}
                {pj.entry?.dashboardProjectId && <span className="ml-2 rounded-full bg-emerald-500/15 px-2 py-0.5 text-xs font-semibold text-emerald-700 dark:text-emerald-400">vinculado</span>}
              </p>
            )}
            {schema && (
              <p className="mt-2 text-xs text-muted-foreground">
                Base atual — P&ID: {descreverVersao(ultimaPid)} · 3D: {descreverVersao(ultimaPiping)}
              </p>
            )}
          </div>

          <div className="flex h-fit items-center gap-3">
            {sujo && <span className="text-xs font-semibold text-amber-700 dark:text-amber-400">Alterações não salvas</span>}
            <Button variant="outline" onClick={() => { pj.reload(); recarregar(); }} disabled={carregando || !projectIdAcc} title="Recarregar dados" aria-label="Recarregar">
              <RefreshCw className={`h-4 w-4 ${carregando ? "animate-spin" : ""}`} />
            </Button>
            <Button onClick={salvar} disabled={salvando || carregando || !configOk || !temSchema || bloqueado || totalErros > 0} className="gap-2 font-bold" title={totalErros > 0 ? "Corrija os erros de validação" : "Salvar Configuração"}>
              {salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              SALVAR CONFIGURAÇÃO
            </Button>
          </div>
        </div>

        <div className="mt-4 space-y-2 empty:hidden">
          {!projectIdAcc && <Aviso tom="amber" icon={<AlertCircle className="h-5 w-5 shrink-0" />}>Selecione um projeto para configurar o mapeamento.</Aviso>}
          {pj.error && <Aviso tom="red" icon={<AlertTriangle className="h-5 w-5 shrink-0" />}>{pj.error}</Aviso>}
          {configErro && <Aviso tom="red" icon={<AlertTriangle className="h-5 w-5 shrink-0" />}>{configErro} Salvar está bloqueado para não sobrescrever a configuração existente.</Aviso>}
          {schemaErro && <Aviso tom="red" icon={<AlertTriangle className="h-5 w-5 shrink-0" />}>{schemaErro}</Aviso>}
          {projectIdAcc && !carregando && pj.naoVinculado !== true && !schemaErro && schema && !temSchema && (
            <Aviso tom="amber" icon={<AlertCircle className="h-5 w-5 shrink-0" />}>Aguardando versão-base: o Dashboard BIM ainda não gerou uma versão completa de P&ID e 3D para este projeto. Tabelas e colunas aparecem quando o DBSYNC criar a base.</Aviso>
          )}
          {usandoModelo && configOk && <Aviso tom="blue" icon={<Info className="h-5 w-5 shrink-0" />}>Este projeto ainda não tem configuração salva. Abaixo está um modelo inicial <b>sugerido</b> (não gravado) — ajuste conforme a versão-base e salve.</Aviso>}
          {projectIdAcc && !podeSalvar && <Aviso tom="blue" icon={<Lock className="h-5 w-5 shrink-0" />}>Modo somente leitura: salvar a configuração exige as permissões <code>plant3d.mapping.view</code> e <code>plant3d.database.edit</code>.</Aviso>}
          {projectIdAcc && podeSalvar && !podeNomenclatura && <Aviso tom="blue" icon={<Lock className="h-5 w-5 shrink-0" />}>Sem <code>plant3d.naming.view</code>: a seção de TAG fica somente leitura.</Aviso>}
          {saveErro && <Aviso tom="red" icon={<AlertTriangle className="h-5 w-5 shrink-0" />}>{saveErro}</Aviso>}
          {totalErros > 0 && configOk && <Aviso tom="red" icon={<AlertTriangle className="h-5 w-5 shrink-0" />}>{totalErros} problema(s) de validação — veja os avisos em cada categoria. Salvar fica bloqueado até corrigir.</Aviso>}
        </div>
      </div>

      {projectIdAcc && pj.naoVinculado === true && pj.entry && (
        <PlantLinkPanel
          project={{ projectIdAcc, name: pj.entry.name }}
          projects={pj.projects}
          canViewCatalog={podeVerCatalogo}
          canEdit={can("plant3d.database.edit")}
          onLinked={pj.reload}
        />
      )}

      {carregando && <div className="flex justify-center p-10"><Loader2 className="h-10 w-10 animate-spin text-primary" /></div>}

      {!carregando && configOk && (
        <div className="space-y-6">
          {mappings.map((cat, catIdx) => {
            const partes = cat.attributes.filter(a => a.isTagPart);
            const extras = cat.attributes.filter(a => !a.isTagPart);
            const check = checks[catIdx] ?? { errors: [], warnings: [] };
            const tagBloqueada = bloqueado || !podeNomenclatura;
            const formato = (lado: "pidCol" | "pipingCol") => {
              const cols = partes.map(p => p[lado] || "?");
              return cat.isComposedTag ? cols.join(cat.delimiter ?? "") : (cols[0] ?? "?");
            };
            return (
              <div key={cat.id + catIdx} className="glass-card rounded-lg border border-border/50 bg-card/30 p-4 sm:p-6">
                <div className="mb-6 flex flex-col justify-between gap-4 border-b border-border/50 pb-4 lg:flex-row lg:items-center">
                  <h4 className="flex items-center gap-2 text-lg font-bold text-primary">{cat.label}</h4>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:min-w-[520px]">
                    <label className="space-y-1">
                      <span className="text-[10px] font-bold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">Tabela P&ID</span>
                      <select aria-label={`Tabela P&ID para ${cat.label}`} disabled={bloqueado} value={cat.pidTable}
                        onChange={e => patchCat(catIdx, c => ({ ...c, pidTable: e.target.value }))}
                        className={`${SELECT} border-emerald-500/30 focus:border-emerald-500`}>
                        {opcoes(cat.pidTable, Object.keys(pid).sort(), temSchema)}
                      </select>
                    </label>
                    <label className="space-y-1">
                      <span className="text-[10px] font-bold uppercase tracking-wider text-blue-600 dark:text-blue-400">Tabela 3D</span>
                      <select aria-label={`Tabela 3D para ${cat.label}`} disabled={bloqueado} value={cat.pipingTable}
                        onChange={e => patchCat(catIdx, c => ({ ...c, pipingTable: e.target.value }))}
                        className={`${SELECT} border-blue-500/30 focus:border-blue-500`}>
                        {opcoes(cat.pipingTable, Object.keys(piping).sort(), temSchema)}
                      </select>
                    </label>
                  </div>
                </div>

                {(check.errors.length > 0 || check.warnings.length > 0) && (
                  <ul className="mb-4 space-y-1 text-xs">
                    {check.errors.map(m => <li key={m} className="flex items-start gap-1.5 text-red-600 dark:text-red-400"><AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />{m}</li>)}
                    {check.warnings.map(m => <li key={m} className="flex items-start gap-1.5 text-amber-700 dark:text-amber-400"><Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />{m}</li>)}
                  </ul>
                )}

                <div className="mb-6 rounded-xl border border-primary/20 bg-primary/5 p-4">
                  <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                    <h5 className="flex items-center gap-2 text-xs font-black uppercase text-primary">
                      <Key className="h-4 w-4" /> Chave Principal de Cruzamento (TAG)
                    </h5>
                    <div className="flex rounded-lg border border-primary/20 bg-background/50 p-1">
                      {([false, true] as const).map(composta => (
                        <button key={String(composta)} type="button" disabled={tagBloqueada}
                          onClick={() => alternarComposicao(catIdx, composta)}
                          className={`rounded-md px-3 py-1 text-xs font-bold transition-colors disabled:cursor-not-allowed ${!!cat.isComposedTag === composta ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground"}`}>
                          {composta ? "Compor TAG" : "TAG Única"}
                        </button>
                      ))}
                    </div>
                  </div>

                  {cat.isComposedTag && (
                    <div className="mb-3 flex items-center gap-3">
                      <label htmlFor={`delim-${catIdx}`} className="text-xs font-semibold text-muted-foreground">Separador da TAG</label>
                      <input id={`delim-${catIdx}`} aria-label={"Separador TAG " + cat.label} disabled={tagBloqueada} maxLength={5}
                        value={cat.delimiter ?? ""} onChange={e => patchCat(catIdx, c => ({ ...c, delimiter: e.target.value }))}
                        className="w-20 rounded border border-border bg-background px-2 py-1.5 text-center text-foreground disabled:opacity-60" />
                    </div>
                  )}

                  <div className="space-y-3">
                    <div className="hidden grid-cols-12 gap-3 px-1 text-[10px] font-bold uppercase text-muted-foreground md:grid">
                      <div className="col-span-4">{cat.isComposedTag ? "Parte (ordem de composição)" : "Nome"}</div>
                      <div className="col-span-3 text-emerald-600 dark:text-emerald-400">Coluna P&ID</div>
                      <div className="col-span-3 text-blue-600 dark:text-blue-400">Coluna 3D</div>
                      <div className="col-span-2 text-right">{cat.isComposedTag ? "Ordem / remover" : ""}</div>
                    </div>
                    {partes.map((attr, k) => (
                      <div key={attr.id} className="grid grid-cols-12 items-center gap-3">
                        <div className="col-span-12 flex items-center gap-2 md:col-span-4">
                          {cat.isComposedTag && <div className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary/20 text-xs font-bold text-primary">{k + 1}</div>}
                          <input type="text" aria-label={`Nome da TAG ${k + 1}`} disabled={tagBloqueada} value={attr.name} placeholder="Ex: Área"
                            onChange={e => patchAttr(catIdx, attr.id, { name: e.target.value })}
                            className="w-full rounded-lg border border-primary/30 bg-background px-3 py-2 text-sm font-bold text-primary outline-none focus:border-primary disabled:opacity-60" />
                        </div>
                        <div className="col-span-6 md:col-span-3">
                          <select aria-label={`Coluna P&ID para TAG ${k + 1}`} disabled={tagBloqueada} value={attr.pidCol}
                            onChange={e => patchAttr(catIdx, attr.id, { pidCol: e.target.value })}
                            className={`${SELECT} border-emerald-500/30 focus:border-emerald-500`}>
                            {opcoes(attr.pidCol, pid[cat.pidTable], !!pid[cat.pidTable])}
                          </select>
                        </div>
                        <div className="col-span-6 md:col-span-3">
                          <select aria-label={`Coluna 3D para TAG ${k + 1}`} disabled={tagBloqueada} value={attr.pipingCol}
                            onChange={e => patchAttr(catIdx, attr.id, { pipingCol: e.target.value })}
                            className={`${SELECT} border-blue-500/30 focus:border-blue-500`}>
                            {opcoes(attr.pipingCol, piping[cat.pipingTable], !!piping[cat.pipingTable])}
                          </select>
                        </div>
                        <div className="col-span-12 flex justify-end gap-1 md:col-span-2">
                          {cat.isComposedTag && (
                            <>
                              <button type="button" title="Subir parte da TAG" aria-label="Subir parte da TAG" disabled={tagBloqueada || k === 0} onClick={() => moverParte(catIdx, attr.id, -1)} className="rounded p-1.5 text-muted-foreground hover:bg-primary/10 hover:text-foreground disabled:opacity-30"><ArrowUp className="h-4 w-4" /></button>
                              <button type="button" title="Descer parte da TAG" aria-label="Descer parte da TAG" disabled={tagBloqueada || k === partes.length - 1} onClick={() => moverParte(catIdx, attr.id, 1)} className="rounded p-1.5 text-muted-foreground hover:bg-primary/10 hover:text-foreground disabled:opacity-30"><ArrowDown className="h-4 w-4" /></button>
                              <button type="button" title="Remover parte da TAG" aria-label="Remover parte da TAG" disabled={tagBloqueada || partes.length <= 1} onClick={() => removerAttr(catIdx, attr.id)} className="rounded p-1.5 text-muted-foreground transition-colors hover:text-destructive disabled:opacity-30"><Trash2 className="h-4 w-4" /></button>
                            </>
                          )}
                        </div>
                      </div>
                    ))}
                    {cat.isComposedTag && (
                      <Button variant="ghost" size="sm" disabled={tagBloqueada} onClick={() => adicionarParte(catIdx)} className="mt-2 w-full border border-dashed border-primary/30 text-xs font-bold text-primary hover:bg-primary/10 sm:w-auto">
                        <Plus className="mr-2 h-4 w-4" /> ADICIONAR PARTE DA TAG
                      </Button>
                    )}
                    <p className="pt-1 font-mono text-[11px] text-muted-foreground" title="Estrutura da TAG; a API não fornece amostra de valores.">
                      Formato (estrutura, sem amostra) — P&ID: <span className="text-emerald-600 dark:text-emerald-400">{formato("pidCol")}</span> · 3D: <span className="text-blue-600 dark:text-blue-400">{formato("pipingCol")}</span>
                    </p>
                  </div>
                </div>

                <div className="space-y-3">
                  <h5 className="mb-2 flex items-center gap-2 text-xs font-bold uppercase text-muted-foreground">
                    <ListTree className="h-4 w-4" /> Atributos Extras (comparados após o pareamento por TAG)
                  </h5>
                  {extras.length === 0 ? (
                    <div className="rounded-lg border border-dashed border-border/50 py-4 text-center text-xs italic text-muted-foreground">
                      Nenhum atributo extra mapeado. O cruzamento será feito apenas pela TAG principal.
                    </div>
                  ) : (
                    <div className="hidden grid-cols-12 gap-3 border-b border-border/30 px-2 pb-1 text-[10px] font-bold uppercase text-muted-foreground md:grid">
                      <div className="col-span-4">Nome do Atributo</div>
                      <div className="col-span-3 text-emerald-600 dark:text-emerald-400">Coluna P&ID</div>
                      <div className="col-span-3 text-blue-600 dark:text-blue-400">Coluna 3D</div>
                      <div className="col-span-2 text-right">Ação</div>
                    </div>
                  )}
                  {extras.map(attr => (
                    <div key={attr.id} className="grid grid-cols-12 items-center gap-3 rounded-lg border border-transparent bg-background/30 p-2 transition-all hover:border-border/50">
                      <div className="col-span-12 md:col-span-4">
                        <input type="text" aria-label="Nome do Atributo Extra" placeholder="Ex: Diâmetro, Spec..." disabled={bloqueado} value={attr.name}
                          onChange={e => patchAttr(catIdx, attr.id, { name: e.target.value })}
                          className="w-full rounded-md border border-border bg-background/80 px-3 py-2 text-sm text-foreground outline-none focus:border-primary disabled:opacity-60" />
                      </div>
                      <div className="col-span-6 md:col-span-3">
                        <select aria-label="Coluna P&ID Atributo Extra" disabled={bloqueado} value={attr.pidCol}
                          onChange={e => patchAttr(catIdx, attr.id, { pidCol: e.target.value })} className={`${SELECT} border-border`}>
                          {opcoes(attr.pidCol, pid[cat.pidTable], !!pid[cat.pidTable])}
                        </select>
                      </div>
                      <div className="col-span-6 md:col-span-3">
                        <select aria-label="Coluna 3D Atributo Extra" disabled={bloqueado} value={attr.pipingCol}
                          onChange={e => patchAttr(catIdx, attr.id, { pipingCol: e.target.value })} className={`${SELECT} border-border`}>
                          {opcoes(attr.pipingCol, piping[cat.pipingTable], !!piping[cat.pipingTable])}
                        </select>
                      </div>
                      <div className="col-span-12 flex justify-end md:col-span-2">
                        <button type="button" title="Remover Atributo Extra" aria-label="Remover Atributo Extra" disabled={bloqueado}
                          onClick={() => removerAttr(catIdx, attr.id)}
                          className="rounded-lg p-2 text-muted-foreground transition-colors hover:bg-destructive/10 hover:text-destructive disabled:opacity-30">
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </div>
                  ))}
                  <Button variant="ghost" size="sm" disabled={bloqueado}
                    onClick={() => patchCat(catIdx, c => ({ ...c, attributes: [...c.attributes, { id: newId(), name: "", pidCol: "", pipingCol: "", isTagPart: false }] }))}
                    className="mt-2 text-xs font-bold text-primary hover:bg-primary/10">
                    <Plus className="mr-2 h-4 w-4" /> ADICIONAR ATRIBUTO EXTRA
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </motion.div>
  );
}

const TONS = {
  amber: "border-amber-500/30 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  red: "border-red-500/30 bg-red-500/10 text-red-700 dark:text-red-300",
  blue: "border-blue-500/30 bg-blue-500/10 text-blue-800 dark:text-blue-300",
} as const;

function Aviso({ tom, icon, children }: { tom: keyof typeof TONS; icon: React.ReactNode; children: React.ReactNode }) {
  return <div className={`flex items-start gap-3 rounded-lg border p-3 text-sm ${TONS[tom]}`}>{icon}<div>{children}</div></div>;
}
