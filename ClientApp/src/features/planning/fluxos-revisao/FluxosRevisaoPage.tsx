// Painel de Planejamento → Fluxos de Revisão (21/10/2026). Cadastro/edição dos fluxos (etapas ordenadas + valores de Status_In_Flow
// do Forma/ACC) e vínculo de cada fluxo a UM projeto ou a um GRUPO de projetos. Vínculo do projeto prevalece sobre o do grupo;
// projeto sem vínculo = PENDENTE DE CONFIGURAÇÃO (nenhuma etapa inventada). A 03. DOCUMENTAÇÃO da Gestão à Vista só lê daqui.
// Consome /api/review-flows (backend a cargo do Codex; contrato em _agent_handoffs/DOC03_FRONTEND_CONTRACT.md). API indisponível =
// erro explícito, sem dados de exemplo. Permissões: estrutura existente do Painel de Planejamento (PERMISSOES_FLUXOS_REVISAO).
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { AlertTriangle, ArrowDown, ArrowUp, CheckCircle2, GitBranch, Loader2, Pencil, Plus, RefreshCw, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { useProductionSelectionInterna } from "@/features/production/ProductionProjectSelector";
import {
  atualizarFluxo, atualizarVinculo, criarFluxo, criarVinculo, listarFluxos, obterCatalogo, type ReviewFlowCandidate,
  type DadosVinculo, type ReviewFlow, type ReviewFlowBinding,
} from "@/features/production/analytics/doc03/reviewFlowsApi";
import {
  mover, paraDadosFluxo, paraDadosVinculo, situacaoDoProjeto, validarFluxo, validarVinculo, type EtapaEditavel,
} from "./fluxosRevisaoLogic";
import { PERMISSOES_FLUXOS_REVISAO } from "./fluxosRevisaoRotas";
import ReviewFlowSuggestions from "./ReviewFlowSuggestions";
import { normalizarEtapa } from "@/features/production/analytics/doc03/documentacaoFluxoLogic";

type Aviso = { tipo: "ok" | "erro"; texto: string } | null;
type Projeto = { id: string; nome: string };
const campo = "h-9 rounded-lg border border-border bg-background px-2 text-sm text-foreground";
const rotuloCampo = "flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground";
const msg = (e: unknown) => (e instanceof Error ? e.message : "Falha inesperada.");
/** Ponto de partida de um fluxo NOVO no formulário (editável); não é gravado sem o usuário salvar. */
const ETAPAS_INICIAIS: EtapaEditavel[] = [{ name: "", valores: "" }];
const PENDENTE = "PENDENTE DE CONFIGURAÇÃO";

export default function FluxosRevisaoPage() {
  const { can } = usePermissions();
  const podeEditar = can(PERMISSOES_FLUXOS_REVISAO.editar);
  // Projetos a que o usuário tem acesso (fonte interna /api/me/projects — mesma do Controle de Produção).
  const sel = useProductionSelectionInterna({});
  const projetos = useMemo<Projeto[]>(() => sel.projects.map(p => ({ id: p.id, nome: p.attributes.name })), [sel.projects]);
  const nomeProjeto = useCallback((id: string) => projetos.find(p => p.id === id)?.nome ?? id, [projetos]);

  const [fluxos, setFluxos] = useState<ReviewFlow[]>([]);
  const [vinculos, setVinculos] = useState<ReviewFlowBinding[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<Error | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);
  const [editando, setEditando] = useState<ReviewFlow | "novo" | null>(null);
  const [editandoVinculo, setEditandoVinculo] = useState<ReviewFlowBinding | "novo" | null>(null);

  const carregar = useCallback(async (signal?: AbortSignal) => {
    setCarregando(true); setErro(null);
    try {
      const r = await listarFluxos(signal);
      if (!signal?.aborted) { setFluxos(r.flows); setVinculos(r.bindings); }
    } catch (e) { if (!signal?.aborted) setErro(e instanceof Error ? e : new Error(msg(e))); }
    finally { if (!signal?.aborted) setCarregando(false); }
  }, []);
  useEffect(() => { const c = new AbortController(); void carregar(c.signal); return () => c.abort(); }, [carregar]);

  const nomeFluxo = (id: string) => fluxos.find(f => f.id === id)?.name ?? id;
  const diretos = new Set(vinculos.filter(v => v.scope === "project").map(v => v.projectId));
  return (
    <div className="space-y-4 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <GitBranch className="h-6 w-6 text-primary" aria-hidden />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Fluxos de Revisão</h1>
          <p className="text-sm text-muted-foreground">
            Etapas do fluxo de revisão de documentos, na ordem, e os valores de Status_In_Flow (Autodesk Forma/ACC) que identificam cada etapa.
            Cada fluxo é associado a um projeto ou a um grupo de projetos; o vínculo do projeto prevalece sobre o do grupo.
            A tela 03. Documentação da Gestão à Vista apenas lê esta configuração.
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={() => void carregar()} disabled={carregando}><RefreshCw className="h-4 w-4" /> Atualizar</Button>
      </section>

      {aviso && <Faixa tipo={aviso.tipo} onFechar={() => setAviso(null)}>{aviso.texto}</Faixa>}
      {!podeEditar && !erro && <Faixa tipo="info">Somente leitura: criar, editar e associar fluxos exigem a permissão de edição do Painel de Planejamento ({PERMISSOES_FLUXOS_REVISAO.editar}).</Faixa>}
      {sel.error && <Faixa tipo="erro">Lista de projetos indisponível: {sel.error}. Os vínculos aparecem pelo Project ID.</Faixa>}

      {carregando ? <Estado><Loader2 className="h-5 w-5 animate-spin" /> Carregando fluxos…</Estado>
        : erro ? <Estado erro>
            <b>Não foi possível carregar os fluxos de revisão.</b>
            <span>{erro.message}</span>
            <span className="text-xs">Nenhum fluxo é exibido sem a API. A tela 03 também não exibe etapas enquanto a API estiver indisponível.</span>
            <Button size="sm" variant="outline" onClick={() => void carregar()}><RefreshCw className="h-4 w-4" /> Tentar novamente</Button>
          </Estado>
        : <>
          <section className="space-y-2 rounded-xl border border-border bg-card/40 p-4" aria-label="Fluxos cadastrados">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-sm font-black uppercase tracking-wider">Fluxos ({fluxos.length})</h2>
              {podeEditar && <Button size="sm" onClick={() => setEditando("novo")}><Plus className="h-4 w-4" /> Novo fluxo</Button>}
            </div>
            {!fluxos.length ? <p className="text-sm text-muted-foreground">Nenhum fluxo cadastrado.</p>
              : <ul className="divide-y divide-border">{fluxos.map(f => <li key={f.id} className="flex flex-wrap items-center gap-3 py-2 text-sm">
                <b className="min-w-[12rem]">{f.name}</b>
                <span className={cn("rounded px-1.5 text-xs font-bold", f.active ? "bg-emerald-500/15 text-emerald-700 dark:text-emerald-300" : "bg-muted text-muted-foreground")}>{f.active ? "ATIVO" : "INATIVO"}</span>
                <span className="flex-1 text-muted-foreground">{[...f.stages].sort((a, b) => a.order - b.order).map(s => s.name).join(" › ") || "—"}</span>
                {podeEditar && <Button size="sm" variant="outline" onClick={() => setEditando(f)}><Pencil className="h-4 w-4" /> Editar</Button>}
              </li>)}</ul>}
          </section>
          {editando && podeEditar && <FormFluxo key={editando === "novo" ? "novo" : editando.id} fluxo={editando === "novo" ? null : editando} projetos={projetos}
            projectIds={vinculos.filter(v => v.flowId === (editando === "novo" ? "" : editando.id)).flatMap(v => v.scope === "project" ? [v.projectId ?? ""] : v.projectIds).filter(Boolean)}
            onCancelar={() => setEditando(null)}
            onSalvo={(f, novo) => { setFluxos(xs => novo ? [...xs, f] : xs.map(x => x.id === f.id ? f : x)); setEditando(null); setAviso({ tipo: "ok", texto: `Fluxo "${f.name}" salvo.` }); }}
            onErro={e => setAviso({ tipo: "erro", texto: msg(e) })} />}

          <section className="space-y-2 rounded-xl border border-border bg-card/40 p-4" aria-label="Vínculos de fluxo">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-sm font-black uppercase tracking-wider">Vínculos projeto / grupo ({vinculos.length})</h2>
              {podeEditar && fluxos.length > 0 && <Button size="sm" onClick={() => setEditandoVinculo("novo")}><Plus className="h-4 w-4" /> Novo vínculo</Button>}
            </div>
            {!vinculos.length ? <p className="text-sm text-muted-foreground">Nenhum vínculo: todos os projetos estão com o fluxo {PENDENTE.toLowerCase()} (etapas N/A na 03).</p>
              : <table className="w-full text-sm"><thead><tr className="text-left text-xs uppercase text-muted-foreground"><th>Escopo</th><th>Projeto / grupo</th><th>Fluxo</th><th /></tr></thead>
                <tbody>{vinculos.map(v => <tr key={v.id} className="border-t border-border align-top">
                  <td className="py-1.5">{v.scope === "project" ? "Projeto" : "Grupo"}</td>
                  <td className="py-1.5">{v.scope === "project" ? nomeProjeto(v.projectId ?? "")
                    : <><b>{v.groupName}</b> <span className="text-xs text-muted-foreground">({v.projectIds.length} projeto{v.projectIds.length === 1 ? "" : "s"})</span>
                      <ul className="mt-0.5 flex flex-wrap gap-1">{v.projectIds.map(id => <li key={id} className="rounded bg-muted px-1.5 text-xs"
                        title={diretos.has(id) ? "Este projeto tem vínculo direto, que prevalece sobre o grupo." : undefined}>
                        {nomeProjeto(id)}{diretos.has(id) && " · direto prevalece"}</li>)}</ul></>}</td>
                  <td className="py-1.5">{nomeFluxo(v.flowId)}</td>
                  <td className="py-1.5 text-right">{podeEditar && <Button size="sm" variant="outline" onClick={() => setEditandoVinculo(v)}><Pencil className="h-4 w-4" /> Editar</Button>}</td>
                </tr>)}</tbody></table>}
          </section>
          {editandoVinculo && podeEditar && <FormVinculo key={editandoVinculo === "novo" ? "novo" : editandoVinculo.id} vinculo={editandoVinculo === "novo" ? null : editandoVinculo}
            fluxos={fluxos} vinculos={vinculos} projetos={projetos} nomeProjeto={nomeProjeto}
            onCancelar={() => setEditandoVinculo(null)}
            onSalvo={(v, novo) => { setVinculos(xs => novo ? [...xs, v] : xs.map(x => x.id === v.id ? v : x)); setEditandoVinculo(null); setAviso({ tipo: "ok", texto: "Vínculo salvo." }); }}
            onErro={e => setAviso({ tipo: "erro", texto: msg(e) })} />}

          <SituacaoProjetos projetos={projetos} carregando={sel.loadingProjects} vinculos={vinculos} nomeFluxo={nomeFluxo} />
        </>}
    </div>
  );
}

/** Fluxo efetivo de cada projeto acessível (mesma regra do backend): direto › grupo › PENDENTE DE CONFIGURAÇÃO. */
function SituacaoProjetos({ projetos, carregando, vinculos, nomeFluxo }: { projetos: Projeto[]; carregando: boolean; vinculos: ReviewFlowBinding[]; nomeFluxo: (id: string) => string }) {
  const linhas = projetos.map(p => ({ p, s: situacaoDoProjeto(p.id, vinculos) }));
  const pendentes = linhas.filter(l => l.s.tipo === "pendente").length;
  return <section className="space-y-2 rounded-xl border border-border bg-card/40 p-4" aria-label="Fluxo por projeto">
    <h2 className="text-sm font-black uppercase tracking-wider">Fluxo por projeto ({projetos.length}){pendentes > 0 && <span className="ml-2 text-amber-600 dark:text-amber-400">· {pendentes} {PENDENTE.toLowerCase()}</span>}</h2>
    {carregando ? <p className="text-sm text-muted-foreground">Carregando projetos…</p>
      : !projetos.length ? <p className="text-sm text-muted-foreground">Nenhum projeto acessível.</p>
      : <table className="w-full text-sm"><thead><tr className="text-left text-xs uppercase text-muted-foreground"><th>Projeto</th><th>Fluxo</th><th>Origem</th></tr></thead>
        <tbody>{linhas.map(({ p, s }) => <tr key={p.id} className="border-t border-border" data-testid="fluxo-projeto">
          <td className="py-1.5">{p.nome}</td>
          {s.tipo === "pendente"
            ? <td colSpan={2} className="py-1.5 font-bold text-amber-600 dark:text-amber-400">{PENDENTE}</td>
            : <><td className="py-1.5">{nomeFluxo(s.binding.flowId)}</td><td className="py-1.5 text-muted-foreground">{s.tipo === "projeto" ? "Vínculo do projeto" : `Grupo "${s.binding.groupName}"`}</td></>}
        </tr>)}</tbody></table>}
  </section>;
}

function FormFluxo({ fluxo, projetos, projectIds, onCancelar, onSalvo, onErro }: { fluxo: ReviewFlow | null; projetos: Projeto[]; projectIds: string[]; onCancelar: () => void;
  onSalvo: (f: ReviewFlow, novo: boolean) => void; onErro: (e: unknown) => void }) {
  const [nome, setNome] = useState(fluxo?.name ?? "");
  const [ativo, setAtivo] = useState(fluxo?.active ?? true);
  const [etapas, setEtapas] = useState<EtapaEditavel[]>(fluxo
    ? [...fluxo.stages].sort((a, b) => a.order - b.order).map(s => ({ id: s.id, name: s.name, valores: s.statusInFlowValues.join("; ") }))
    : ETAPAS_INICIAIS);
  const [salvando, setSalvando] = useState(false);
  const [catalogo, setCatalogo] = useState<ReviewFlowCandidate[]>([]);
  useEffect(() => { const c = new AbortController(); void obterCatalogo(c.signal).then(r => setCatalogo(r.candidates)).catch(() => {}); return () => c.abort(); }, []);
  const dados = paraDadosFluxo(nome, ativo, etapas);
  const erros = validarFluxo(dados);
  const alterar = (i: number, p: Partial<EtapaEditavel>) => setEtapas(xs => xs.map((x, j) => j === i ? { ...x, ...p } : x));
  const salvar = async () => {
    setSalvando(true);
    try { onSalvo(fluxo ? await atualizarFluxo(fluxo.id, fluxo.version, dados) : await criarFluxo(dados), !fluxo); }
    catch (e) { onErro(e); } finally { setSalvando(false); }
  };
  return <section className="space-y-3 rounded-xl border border-primary/40 bg-card/60 p-4" aria-label={fluxo ? "Editar fluxo" : "Novo fluxo"}>
    <h2 className="text-sm font-black uppercase tracking-wider">{fluxo ? `Editar fluxo: ${fluxo.name}` : "Novo fluxo"}</h2>
    <div className="flex flex-wrap items-end gap-3">
      <label className={cn(rotuloCampo, "min-w-[16rem] flex-1")}>Nome<input className={campo} value={nome} onChange={e => setNome(e.target.value)} /></label>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={ativo} onChange={e => setAtivo(e.target.checked)} /> Ativo</label>
    </div>
    <ol className="space-y-2">{etapas.map((e, i) => <li key={i} className="flex flex-wrap items-end gap-2">
      <span className="w-6 pb-2 text-right text-xs font-bold text-muted-foreground">{i + 1}.</span>
      <label className={cn(rotuloCampo, "min-w-[12rem]")}>Nome oficial em português<input className={campo} list="review-flow-name-candidates" value={e.name} onChange={ev => {
        const name = ev.target.value;
        const candidate = catalogo.find(c => normalizarEtapa(c.portuguese) === normalizarEtapa(name));
        alterar(i, { name, ...(candidate && !e.valores.trim() ? { valores: candidate.aliases.join("; ") } : {}) });
      }} />
        {normalizarEtapa(e.name).length >= 2 && <div className="flex max-h-24 flex-wrap gap-1 overflow-y-auto normal-case tracking-normal">
          {catalogo.filter(c => normalizarEtapa(c.portuguese).includes(normalizarEtapa(e.name)) || normalizarEtapa(c.english).includes(normalizarEtapa(e.name)))
            .slice(0, 6).map(c => <button type="button" key={`${c.portuguese}-${c.english}`} className="rounded border border-border px-1.5 py-0.5 text-xs"
              onClick={() => alterar(i, { name: c.portuguese, valores: e.valores.trim() || c.aliases.join("; ") })}>{c.portuguese} · {c.english}</button>)}</div>}</label>
      <label className={cn(rotuloCampo, "min-w-[16rem] flex-1")}>Valores de Status_In_Flow (separados por ;)
        <input className={campo} list="review-flow-alias-candidates" value={e.valores} placeholder="ex.: Em Verificação; Verification" onChange={ev => {
          const valores = ev.target.value;
          const matches = catalogo.filter(c => c.aliases.some(a => normalizarEtapa(a) === normalizarEtapa(valores)));
          alterar(i, { valores, ...(matches.length === 1 && !e.name.trim() ? { name: matches[0].portuguese } : {}) });
        }} />
        {normalizarEtapa(e.valores.split(/[;\n]/).at(-1)).length >= 2 && <div className="flex max-h-24 flex-wrap gap-1 overflow-y-auto normal-case tracking-normal">
          {catalogo.flatMap(c => c.aliases.map(alias => ({ alias, c })))
            .filter(x => normalizarEtapa(x.alias).includes(normalizarEtapa(e.valores.split(/[;\n]/).at(-1))))
            .slice(0, 6).map(x => <button type="button" key={`${x.c.portuguese}-${x.c.english}-${x.alias}`} className="rounded border border-border px-1.5 py-0.5 text-xs"
              onClick={() => {
                const parts = e.valores.split(/[;\n]/).map(v => v.trim()).filter(Boolean);
                parts.pop();
                alterar(i, { valores: [...parts, x.alias].join("; "), name: e.name.trim() || x.c.portuguese });
              }}>{x.alias} → {x.c.portuguese}</button>)}</div>}</label>
      <Button size="sm" variant="outline" aria-label="Subir etapa" disabled={i === 0} onClick={() => setEtapas(xs => mover(xs, i, -1))}><ArrowUp className="h-4 w-4" /></Button>
      <Button size="sm" variant="outline" aria-label="Descer etapa" disabled={i === etapas.length - 1} onClick={() => setEtapas(xs => mover(xs, i, 1))}><ArrowDown className="h-4 w-4" /></Button>
      <Button size="sm" variant="outline" aria-label="Remover etapa" onClick={() => setEtapas(xs => xs.filter((_, j) => j !== i))}><Trash2 className="h-4 w-4" /></Button>
    </li>)}</ol>
    <Button size="sm" variant="outline" onClick={() => setEtapas(xs => [...xs, { name: "", valores: "" }])}><Plus className="h-4 w-4" /> Etapa</Button>
    <datalist id="review-flow-name-candidates">{catalogo.map(c => <option key={`${c.portuguese}-${c.english}`} value={c.portuguese}>{c.kind}</option>)}</datalist>
    <datalist id="review-flow-alias-candidates">{catalogo.flatMap(c => c.aliases).map(a => <option key={a} value={a} />)}</datalist>
    <p className="text-xs text-muted-foreground">Somente aliases cadastrados reconhecem Status_In_Flow. O nome oficial define os títulos na Gestão à Vista. Sugestões não são gravadas automaticamente.</p>
    <ReviewFlowSuggestions projects={projetos} initialProjectIds={projectIds} stages={etapas} onStages={setEtapas} />
    {erros.length > 0 && <ul className="list-disc pl-5 text-xs text-destructive">{erros.map(x => <li key={x}>{x}</li>)}</ul>}
    <div className="flex gap-2">
      <Button size="sm" disabled={salvando || erros.length > 0} onClick={() => void salvar()}>{salvando && <Loader2 className="h-4 w-4 animate-spin" />} Salvar</Button>
      <Button size="sm" variant="outline" onClick={onCancelar}>Cancelar</Button>
    </div>
  </section>;
}

function FormVinculo({ vinculo, fluxos, vinculos, projetos, nomeProjeto, onCancelar, onSalvo, onErro }: {
  vinculo: ReviewFlowBinding | null; fluxos: ReviewFlow[]; vinculos: ReviewFlowBinding[]; projetos: Projeto[]; nomeProjeto: (id: string) => string;
  onCancelar: () => void; onSalvo: (v: ReviewFlowBinding, novo: boolean) => void; onErro: (e: unknown) => void }) {
  const [d, setD] = useState<DadosVinculo>({ flowId: vinculo?.flowId ?? fluxos[0]?.id ?? "", scope: vinculo?.scope ?? "project",
    projectId: vinculo?.projectId ?? "", groupName: vinculo?.groupName ?? "", projectIds: vinculo?.projectIds ?? [] });
  const [filtro, setFiltro] = useState("");
  const [salvando, setSalvando] = useState(false);
  const dados = paraDadosVinculo(d);
  const erros = validarVinculo(dados, vinculos, vinculo?.id, nomeProjeto);
  const acessiveis = new Set(projetos.map(p => p.id));
  // Projeto do grupo fora da lista do usuário (sem acesso): mantido como está, nunca removido em silêncio.
  const semAcesso = d.projectIds.filter(id => !acessiveis.has(id));
  const visiveis = projetos.filter(p => p.nome.toLowerCase().includes(filtro.trim().toLowerCase()));
  const alternar = (id: string) => setD(x => ({ ...x, projectIds: x.projectIds.includes(id) ? x.projectIds.filter(y => y !== id) : [...x.projectIds, id] }));
  const salvar = async () => {
    setSalvando(true);
    try { onSalvo(vinculo ? await atualizarVinculo(vinculo.id, vinculo.version, dados) : await criarVinculo(dados), !vinculo); }
    catch (e) { onErro(e); } finally { setSalvando(false); }
  };
  return <section className="space-y-3 rounded-xl border border-primary/40 bg-card/60 p-4" aria-label={vinculo ? "Editar vínculo" : "Novo vínculo"}>
    <h2 className="text-sm font-black uppercase tracking-wider">{vinculo ? "Editar vínculo" : "Novo vínculo"}</h2>
    <div className="flex flex-wrap items-end gap-3">
      <label className={rotuloCampo}>Fluxo<select className={campo} value={d.flowId} onChange={e => setD({ ...d, flowId: e.target.value })}>
        {fluxos.map(f => <option key={f.id} value={f.id}>{f.name}{f.active ? "" : " (inativo)"}</option>)}</select></label>
      <label className={rotuloCampo}>Escopo<select className={campo} value={d.scope} disabled={!!vinculo} onChange={e => setD({ ...d, scope: e.target.value as DadosVinculo["scope"] })}>
        <option value="project">Projeto</option><option value="group">Grupo de projetos</option></select></label>
      {d.scope === "project"
        ? <label className={cn(rotuloCampo, "min-w-[22rem] flex-1")}>Projeto<select className={campo} value={d.projectId ?? ""} onChange={e => setD({ ...d, projectId: e.target.value })}>
            <option value="">Selecione o projeto</option>
            {d.projectId && !acessiveis.has(d.projectId) && <option value={d.projectId}>{d.projectId} (sem acesso)</option>}
            {projetos.map(p => <option key={p.id} value={p.id}>{p.nome}</option>)}</select></label>
        : <label className={cn(rotuloCampo, "min-w-[16rem] flex-1")}>Nome do grupo<input className={campo} value={d.groupName ?? ""} onChange={e => setD({ ...d, groupName: e.target.value })} /></label>}
    </div>
    {d.scope === "group" && <fieldset className="space-y-2 rounded-lg border border-border p-3">
      <legend className="px-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">Projetos do grupo ({d.projectIds.length})</legend>
      <input className={cn(campo, "w-full max-w-sm")} placeholder="Filtrar projetos" aria-label="Filtrar projetos" value={filtro} onChange={e => setFiltro(e.target.value)} />
      <ul className="grid max-h-64 gap-1 overflow-y-auto sm:grid-cols-2">{visiveis.map(p => <li key={p.id}>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={d.projectIds.includes(p.id)} onChange={() => alternar(p.id)} />
          <span className="min-w-0 truncate" title={p.nome}>{p.nome}</span>
          {vinculos.some(v => v.scope === "project" && v.projectId === p.id) && <span className="text-[10px] text-muted-foreground">(vínculo direto prevalece)</span>}</label>
      </li>)}</ul>
      {semAcesso.length > 0 && <p className="text-xs text-muted-foreground">Mantidos no grupo (sem acesso para você): {semAcesso.join(", ")}.</p>}
    </fieldset>}
    {erros.length > 0 && <ul className="list-disc pl-5 text-xs text-destructive">{erros.map(x => <li key={x}>{x}</li>)}</ul>}
    <div className="flex gap-2">
      <Button size="sm" disabled={salvando || erros.length > 0} onClick={() => void salvar()}>{salvando && <Loader2 className="h-4 w-4 animate-spin" />} Salvar</Button>
      <Button size="sm" variant="outline" onClick={onCancelar}>Cancelar</Button>
    </div>
  </section>;
}

function Faixa({ tipo, children, onFechar }: { tipo: "ok" | "erro" | "info"; children: ReactNode; onFechar?: () => void }) {
  return <div role={tipo === "erro" ? "alert" : "status"} className={cn("flex items-start gap-2 rounded-lg border px-3 py-2 text-sm",
    tipo === "ok" ? "border-emerald-500/40 bg-emerald-500/10" : tipo === "erro" ? "border-destructive/50 bg-destructive/10" : "border-border bg-muted/40")}>
    {tipo === "ok" ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" /> : <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />}
    <div className="min-w-0 flex-1">{children}</div>
    {onFechar && <button type="button" className="text-xs underline" onClick={onFechar}>Fechar</button>}
  </div>;
}
function Estado({ children, erro }: { children: ReactNode; erro?: boolean }) {
  return <div role={erro ? "alert" : "status"} className={cn("flex flex-col items-center gap-2 rounded-xl border p-8 text-center text-sm",
    erro ? "border-destructive/40 bg-destructive/5" : "border-border bg-card/40 text-muted-foreground")}>{children}</div>;
}
