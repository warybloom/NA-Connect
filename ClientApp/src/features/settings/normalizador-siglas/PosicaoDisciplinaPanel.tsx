// Configurações → Normalizador de Siglas → Exceções de posição da disciplina (26/10/2026, revisão BYD002).
// Padrão SEMPRE DOC3, em todos os projetos. Exceção pontual = número de documento exato, ou padrão limitado
// "DOC3 = X e DOC4 = sigla exata", só no projeto escolhido; a mesma disciplina pode ter documentos em DOC3 e exceções em DOC4.
// Fluxo: candidatos (documentos sem disciplina cujo DOC4 é sigla reconhecida) → seleção → prévia por documento → incluir.
// Reverter = desativar (auditoria preservada). O backend refaz a prévia e recusa piora/troca de disciplina sem confirmação.
import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, Eye, Loader2, Plus, RefreshCw, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { cn } from "@/lib/utils";
import {
  NormalizadorError, buscarCandidatos, definirAtivoExcecao, incluirExcecoes, listarExcecoes, preverExcecoes,
  type CandidatosExcecao, type CriterioExcecao, type ExcecaoRegistro, type ImpactoPosicao, type NovaExcecao, type SituacaoImpacto,
} from "./normalizadorSiglasApi";

const campo = "h-9 rounded-lg border border-border bg-background px-2 text-sm text-foreground";
const rotuloCampo = "flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground";
const msg = (e: unknown) => (e instanceof Error ? e.message : "Falha inesperada.");
const dataHora = (v: string) => { const d = new Date(v.endsWith("Z") ? v : v + "Z"); return Number.isNaN(d.valueOf()) ? v : d.toLocaleString("pt-BR"); };
const ROTULO: Record<SituacaoImpacto, string> = { CORRIGIDO: "Corrigido", PIOROU: "Piora", ALTERADO: "Disciplina alterada", PENDENTE: "Continua sem disciplina" };
const descreverExcecao = (e: { criterio: CriterioExcecao; codigoDocumento: string | null; segmentoDoc3: string | null; siglaEsperada: string; posicao: number }) =>
  e.criterio === "CODIGO" ? `${e.codigoDocumento}: sigla ${e.siglaEsperada} no DOC${e.posicao}` : `DOC3 = ${e.segmentoDoc3} e DOC${e.posicao} = ${e.siglaEsperada}`;

/** Exceções propostas a partir dos candidatos marcados. PADRAO gera um par (DOC3, sigla) distinto por grupo. */
export function propostas(selecionados: { codigo: string; doc3: string | null; sigla: string; posicao: number }[], criterio: CriterioExcecao): NovaExcecao[] {
  if (criterio === "CODIGO")
    return selecionados.map((c) => ({ criterio, codigoDocumento: c.codigo, segmentoDoc3: null, siglaEsperada: c.sigla, posicao: c.posicao }));
  const pares = new Map<string, NovaExcecao>();
  for (const c of selecionados) if (c.doc3)
    pares.set(`${c.doc3}|${c.sigla}|${c.posicao}`, { criterio, codigoDocumento: null, segmentoDoc3: c.doc3, siglaEsperada: c.sigla, posicao: c.posicao });
  return [...pares.values()];
}

export function PosicaoDisciplinaPanel({ projetos, carregandoProjetos, podeEditar }: {
  projetos: { id: string; attributes: { name: string } }[]; carregandoProjetos: boolean; podeEditar: boolean;
}) {
  const [projeto, setProjeto] = useState("");
  const [instalado, setInstalado] = useState<boolean | null>(null);
  const [avisoInstalacao, setAvisoInstalacao] = useState<string>();
  const [excecoes, setExcecoes] = useState<ExcecaoRegistro[]>([]);
  const [posicao, setPosicao] = useState(4);
  const [cand, setCand] = useState<CandidatosExcecao | null>(null);
  const [marcados, setMarcados] = useState<Set<string>>(new Set());
  const [criterio, setCriterio] = useState<CriterioExcecao>("CODIGO");
  const [motivo, setMotivo] = useState("");
  const [impacto, setImpacto] = useState<ImpactoPosicao | null>(null);
  const [previa, setPrevia] = useState<string | null>(null);
  const [filtro, setFiltro] = useState<SituacaoImpacto | "todas">("todas");
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string>();
  const [ok, setOk] = useState<string>();
  const [confirmar, setConfirmar] = useState(false);
  const [aceito, setAceito] = useState(false);
  const [alternar, setAlternar] = useState<{ excecao: ExcecaoRegistro; impacto: ImpactoPosicao | null } | null>(null);

  useEffect(() => { if (!projeto && projetos.length) setProjeto(projetos[0].id); }, [projeto, projetos]);
  const nomeProjeto = (id: string) => projetos.find((p) => p.id === id)?.attributes.name ?? id;

  const carregar = useCallback(async (signal?: AbortSignal) => {
    if (!projeto) return;
    setOcupado(true); setErro(undefined);
    try {
      const [l, c] = await Promise.all([listarExcecoes(projeto, signal), buscarCandidatos(projeto, posicao, signal)]);
      if (signal?.aborted) return;
      setInstalado(l.instalado); setAvisoInstalacao(l.message); setExcecoes(l.excecoes); setCand(c);
      setMarcados(new Set()); setImpacto(null); setPrevia(null);
    } catch (e) { if (!signal?.aborted) setErro(msg(e)); }
    finally { if (!signal?.aborted) setOcupado(false); }
  }, [projeto, posicao]);
  useEffect(() => { setOk(undefined); const c = new AbortController(); void carregar(c.signal); return () => c.abort(); }, [carregar]);

  const selecionados = useMemo(() => (cand?.candidatos ?? []).filter((c) => marcados.has(c.codigo)), [cand, marcados]);
  const novas = useMemo(() => propostas(selecionados, criterio), [selecionados, criterio]);
  const chave = JSON.stringify([projeto, novas]);
  const previaValida = impacto !== null && previa === chave;
  const grupos = useMemo(() => {
    const m = new Map<string, { disciplina: string; sigla: string; doc3: string | null; codigos: string[] }>();
    for (const c of cand?.candidatos ?? []) {
      const k = `${c.disciplina}|${c.sigla}|${c.doc3 ?? ""}`;
      const g = m.get(k) ?? { disciplina: c.disciplina, sigla: c.sigla, doc3: c.doc3, codigos: [] };
      g.codigos.push(c.codigo); m.set(k, g);
    }
    return [...m.values()];
  }, [cand]);
  const docs = useMemo(() => (impacto?.documentos ?? []).filter((d) => filtro === "todas" || d.situacao === filtro), [impacto, filtro]);

  const marcar = (codigos: string[], valor: boolean) => setMarcados((atual) => {
    const n = new Set(atual); for (const c of codigos) { if (valor) n.add(c); else n.delete(c); } return n;
  });
  const prever = async () => {
    setOcupado(true); setErro(undefined); setOk(undefined);
    try { setImpacto(await preverExcecoes(projeto, novas)); setPrevia(chave); setFiltro("todas"); }
    catch (e) { setErro(msg(e)); }
    finally { setOcupado(false); }
  };
  const incluir = async () => {
    setConfirmar(false); setOcupado(true); setErro(undefined);
    try {
      const r = await incluirExcecoes(projeto, novas, motivo.trim() || null, aceito);
      setOk(`${r.excecoes.length} exceção(ões) incluída(s) em ${nomeProjeto(projeto)}: ${r.resumo.corrigidosPorExcecao} documento(s) corrigido(s). As telas usam a nova leitura na próxima consulta.`);
      setMotivo(""); await carregar();
    } catch (e) { setErro(msg(e)); }
    finally { setOcupado(false); setAceito(false); }
  };
  const pedirAlternar = async (excecao: ExcecaoRegistro) => {
    setAlternar({ excecao, impacto: null });
    if (!excecao.ativo) return;
    try { const i = await preverExcecoes(projeto, [], [excecao.id]); setAlternar((a) => a && a.excecao.id === excecao.id ? { ...a, impacto: i } : a); }
    catch (e) { setErro(msg(e)); }
  };
  const confirmarAlternar = async () => {
    if (!alternar) return;
    const { excecao } = alternar; setAlternar(null); setOcupado(true); setErro(undefined);
    try {
      await definirAtivoExcecao(projeto, excecao.id, excecao.versao, !excecao.ativo);
      setOk(`Exceção ${excecao.id} ${excecao.ativo ? "desativada: os documentos dela voltam ao DOC3" : "reativada"}.`); await carregar();
    } catch (e) { setErro(msg(e)); if (e instanceof NormalizadorError && e.status === 409) void carregar(); }
    finally { setOcupado(false); }
  };

  const r = impacto?.resumo;
  return <div className="space-y-3">
    {instalado === false && <Aviso tipo="info"><b>Gravação indisponível até a migração ser aplicada.</b> {avisoInstalacao} Candidatos e prévia funcionam normalmente; todos os projetos seguem em DOC3.</Aviso>}
    {erro && <Aviso tipo="erro">{erro}</Aviso>}
    {ok && <Aviso tipo="ok">{ok}</Aviso>}

    <section className="space-y-3 rounded-xl border border-border bg-card/40 p-4">
      <p className="text-sm text-muted-foreground">
        A sigla de disciplina é lida no <b>DOC3</b> em todos os projetos. Quando documentos específicos trazem a sigla em outra posição
        (ex.: <code>BYD002-005-DE-TU3-001</code>), cadastre uma <b>exceção</b> só para eles: por número de documento ou por um padrão
        limitado (DOC3 exato + sigla exata). Os demais documentos — inclusive da mesma disciplina — continuam no DOC3. O número do
        documento, revisões, status, HH e custos não são alterados.
      </p>
      <div className="flex flex-wrap items-end gap-3">
        <label className={cn(rotuloCampo, "min-w-[18rem]")}>Projeto
          <select className={cn(campo, "normal-case")} value={projeto} disabled={carregandoProjetos || ocupado} onChange={(e) => setProjeto(e.target.value)}>
            {projetos.map((p) => <option key={p.id} value={p.id}>{p.attributes.name}</option>)}
          </select></label>
        <label className={rotuloCampo}>Pesquisar sigla no
          <select className={cn(campo, "normal-case")} value={posicao} disabled={ocupado} onChange={(e) => setPosicao(Number(e.target.value))}>
            {[4, 5, 2].map((p) => <option key={p} value={p}>DOC{p}</option>)}
          </select></label>
        <Button variant="ghost" disabled={ocupado || !projeto} onClick={() => void carregar()}>
          {ocupado ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />} Recarregar</Button>
      </div>
    </section>

    <section className="overflow-x-auto rounded-xl border border-border">
      <table className="w-full min-w-[56rem] text-sm">
        <caption className="px-3 py-2 text-left text-xs font-black uppercase tracking-wider text-muted-foreground">Exceções cadastradas neste projeto</caption>
        <thead className="bg-muted/50 text-left text-xs uppercase tracking-wider text-muted-foreground">
          <tr><th className="px-3 py-2">#</th><th className="px-3 py-2">Exceção</th><th className="px-3 py-2">Disciplina (na criação)</th><th className="px-3 py-2">Situação</th>
            <th className="px-3 py-2">Motivo</th><th className="px-3 py-2">Alterado</th><th className="px-3 py-2"><span className="sr-only">Ações</span></th></tr></thead>
        <tbody>{excecoes.map((e) => <tr key={e.id} className={cn("border-t border-border", !e.ativo && "text-muted-foreground")}>
          <td className="px-3 py-1.5">{e.id}</td><td className="px-3 py-1.5 font-mono text-xs">{descreverExcecao(e)}</td>
          <td className="px-3 py-1.5">{e.disciplinaNoCadastro}</td><td className="px-3 py-1.5">{e.ativo ? "Ativa" : "Desativada"}</td>
          <td className="px-3 py-1.5 text-xs">{e.motivo ?? "—"}</td><td className="px-3 py-1.5 text-xs">{e.alteradoPor} · {dataHora(e.alteradoEm)}</td>
          <td className="px-3 py-1.5">{podeEditar && <Button size="sm" variant="outline" disabled={ocupado} onClick={() => void pedirAlternar(e)}>{e.ativo ? "Desativar" : "Reativar"}</Button>}</td>
        </tr>)}</tbody>
      </table>
      {!excecoes.length && <p className="p-4 text-center text-sm text-muted-foreground">Nenhuma exceção: todos os documentos deste projeto são lidos no DOC3.</p>}
    </section>

    {cand && <section className="space-y-3 rounded-xl border border-border bg-card/40 p-4" aria-label="Documentos sem disciplina">
      <h2 className="text-sm font-black uppercase tracking-wider">Documentos sem disciplina: {cand.semMapeamento} de {cand.documentosAnalisados}
        <span className="ml-2 font-normal normal-case text-muted-foreground">({cand.fonte})</span></h2>
      <p className="text-xs text-muted-foreground">
        {cand.candidatos.length} têm sigla reconhecida no DOC{cand.posicao} (candidatos — nada é aplicado sem sua seleção);
        {" "}{cand.pendentes.length} não seriam resolvidos por exceção no DOC{cand.posicao}.</p>
      {grupos.length > 0 && <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full min-w-[48rem] text-sm"><caption className="sr-only">Candidatos por disciplina e sigla</caption>
          <thead className="bg-muted/50 text-left text-xs uppercase tracking-wider text-muted-foreground">
            <tr><th className="px-3 py-2">Disciplina</th><th className="px-3 py-2">DOC3 atual</th><th className="px-3 py-2">Sigla no DOC{cand.posicao}</th>
              <th className="px-3 py-2">Documentos</th><th className="px-3 py-2">Selecionados</th></tr></thead>
          <tbody>{grupos.map((g) => {
            const n = g.codigos.filter((c) => marcados.has(c)).length;
            return <tr key={`${g.disciplina}|${g.sigla}|${g.doc3}`} className="border-t border-border align-top">
              <td className="px-3 py-1.5 font-bold">{g.disciplina}</td><td className="px-3 py-1.5 font-mono">{g.doc3 ?? "—"}</td>
              <td className="px-3 py-1.5 font-mono">{g.sigla}</td>
              <td className="px-3 py-1.5"><details><summary className="cursor-pointer">{g.codigos.length}</summary>
                <ul className="mt-1 max-h-48 space-y-0.5 overflow-y-auto">{g.codigos.map((c) => <li key={c}><label className="flex items-center gap-2 font-mono text-xs">
                  <input type="checkbox" checked={marcados.has(c)} onChange={(e) => marcar([c], e.target.checked)} />{c}</label></li>)}</ul></details></td>
              <td className="px-3 py-1.5"><label className="flex items-center gap-2"><input type="checkbox" checked={n === g.codigos.length}
                ref={(el) => { if (el) el.indeterminate = n > 0 && n < g.codigos.length; }} onChange={(e) => marcar(g.codigos, e.target.checked)} />{n}</label></td>
            </tr>;
          })}</tbody>
        </table>
      </div>}

      {selecionados.length > 0 && <div className="space-y-2 rounded-lg border border-primary/40 p-3">
        <fieldset className="space-y-1" disabled={ocupado}>
          <legend className={rotuloCampo}>Critério da exceção</legend>
          <label className="flex items-center gap-2 text-sm"><input type="radio" name="criterio" checked={criterio === "CODIGO"} onChange={() => setCriterio("CODIGO")} />
            Por número de documento — {selecionados.length} exceção(ões), uma por documento selecionado (mais restrito)</label>
          <label className="flex items-center gap-2 text-sm"><input type="radio" name="criterio" checked={criterio === "PADRAO"} onChange={() => setCriterio("PADRAO")} />
            Por padrão (DOC3 exato + sigla exata) — {propostas(selecionados, "PADRAO").length} exceção(ões); vale também para documentos futuros com o mesmo padrão</label>
        </fieldset>
        <ul className="list-disc pl-5 text-xs text-muted-foreground">{novas.slice(0, 6).map((n) => <li key={descreverExcecao(n)} className="font-mono">{descreverExcecao(n)}</li>)}
          {novas.length > 6 && <li>… e mais {novas.length - 6}</li>}</ul>
        <label className={cn(rotuloCampo, "max-w-xl")}>Motivo (auditoria)
          <input className={cn(campo, "normal-case")} maxLength={500} value={motivo} onChange={(e) => setMotivo(e.target.value)}
            placeholder="Ex.: documentos de tubulação emitidos com tipo e disciplina trocados" /></label>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" disabled={ocupado || !novas.length} onClick={() => void prever()}><Eye className="h-4 w-4" /> Prévia por documento</Button>
          {podeEditar && <Button disabled={!previaValida || ocupado || instalado !== true} title={!previaValida ? "Gere a prévia desta seleção antes de incluir." : undefined}
            onClick={() => { setAceito(false); setConfirmar(true); }}><Plus className="h-4 w-4" /> Incluir exceções</Button>}
        </div>
      </div>}

      {cand.pendentes.length > 0 && <details className="rounded-lg border border-border p-3">
        <summary className="cursor-pointer text-sm font-bold"><Search className="mr-1 inline h-4 w-4" />Continuam sem disciplina (não resolvidos por exceção no DOC{cand.posicao}): {cand.pendentes.length}</summary>
        <ul className="mt-2 max-h-64 space-y-1 overflow-y-auto text-xs">{cand.pendentes.map((p) => <li key={p.codigo}><span className="font-mono">{p.codigo}</span> — {p.motivo}</li>)}</ul>
      </details>}
    </section>}

    {impacto && r && <section className="space-y-3 rounded-xl border border-border bg-card/40 p-4" aria-label="Prévia do impacto">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-black uppercase tracking-wider">Prévia: {impacto.excecoesDepois.length - impacto.excecoesAntes.length} exceção(ões) nova(s)</h2>
        <span className="text-xs text-muted-foreground">{impacto.projeto.trim()} · fonte: {impacto.fonte} · nada foi gravado{!previaValida && " · a seleção mudou: gere a prévia novamente"}</span>
      </div>
      <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-3 lg:grid-cols-6">
        <Indicador rotulo="Documentos analisados" valor={`${r.documentosAnalisados}`} />
        <Indicador rotulo="Com disciplina" valor={`${r.identificadosAntes} → ${r.identificadosDepois}`} />
        <Indicador rotulo="Sem mapeamento" valor={`${r.semMapeamentoAntes} → ${r.semMapeamentoDepois}`} />
        <Indicador rotulo="Corrigidos por exceção" valor={`${r.corrigidosPorExcecao}`} />
        <Indicador rotulo="Pioram" valor={`${r.pioraram}`} alerta={r.pioraram > 0} />
        <Indicador rotulo="Disciplina alterada" valor={`${r.disciplinaAlterada}`} alerta={r.disciplinaAlterada > 0} />
      </dl>
      <p className="text-xs text-muted-foreground">Os {r.identificadosAntes - r.pioraram - r.disciplinaAlterada} documentos já identificados continuam exatamente como estão (DOC3).</p>
      <label className={rotuloCampo}>Situação
        <select className={cn(campo, "max-w-xs normal-case")} value={filtro} onChange={(e) => setFiltro(e.target.value as SituacaoImpacto | "todas")}>
          <option value="todas">Todas ({impacto.documentos.length})</option>
          {(Object.keys(ROTULO) as SituacaoImpacto[]).map((s) => <option key={s} value={s}>{ROTULO[s]} ({impacto.documentos.filter((d) => d.situacao === s).length})</option>)}
        </select></label>
      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="w-full min-w-[60rem] text-sm"><caption className="sr-only">Documentos afetados</caption>
          <thead className="bg-muted/50 text-left text-xs uppercase tracking-wider text-muted-foreground">
            <tr><th className="px-3 py-2">Documento (número original)</th><th className="px-3 py-2">Antes</th><th className="px-3 py-2">Depois</th>
              <th className="px-3 py-2">Situação</th><th className="px-3 py-2">Motivo</th></tr></thead>
          <tbody>{docs.slice(0, 500).map((d) => <tr key={d.codigo} className="border-t border-border">
            <td className="px-3 py-1.5 font-mono text-xs">{d.codigo}</td>
            <td className="px-3 py-1.5">{d.siglaAntes ? `${d.siglaAntes} (DOC${d.posicaoAntes})` : "—"} · {d.disciplinaAntes ?? "sem disciplina"}</td>
            <td className="px-3 py-1.5">{d.siglaDepois ? `${d.siglaDepois} (DOC${d.posicaoDepois})` : "—"} · {d.disciplinaDepois ?? "sem disciplina"}</td>
            <td className={cn("px-3 py-1.5 font-bold", d.situacao === "PIOROU" || d.situacao === "ALTERADO" ? "text-destructive" : d.situacao === "CORRIGIDO" ? "text-emerald-600" : "")}>{ROTULO[d.situacao]}</td>
            <td className="px-3 py-1.5 text-xs text-muted-foreground">{d.motivo}</td>
          </tr>)}</tbody>
        </table>
        {docs.length > 500 && <p className="p-2 text-xs text-muted-foreground">Mostrando 500 de {docs.length}.</p>}
      </div>
    </section>}

    <AlertDialog open={confirmar} onOpenChange={(o) => { if (!o) setConfirmar(false); }}>
      <AlertDialogContent>
        <AlertDialogHeader><AlertDialogTitle>Incluir exceções de posição</AlertDialogTitle>
          <AlertDialogDescription asChild><div className="space-y-2 text-sm">
            <p><b>{nomeProjeto(projeto)}</b>: {novas.length} exceção(ões) {criterio === "CODIGO" ? "por número de documento" : "por padrão"}.</p>
            {r && <p>Sem mapeamento: {r.semMapeamentoAntes} → {r.semMapeamentoDepois}. Corrigidos: {r.corrigidosPorExcecao}. Pioram: {r.pioraram}. Disciplina alterada: {r.disciplinaAlterada}.</p>}
            <p>Só muda a leitura da sigla desses documentos. Os demais continuam no DOC3; outros projetos não são afetados. Para reverter, desative a exceção.</p>
            {r && (r.pioraram > 0 || r.disciplinaAlterada > 0) && <label className="flex items-start gap-2 text-destructive">
              <input type="checkbox" checked={aceito} onChange={(e) => setAceito(e.target.checked)} />
              Estou ciente de que {r.pioraram + r.disciplinaAlterada} documento(s) já identificados mudarão.</label>}
          </div></AlertDialogDescription></AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancelar</AlertDialogCancel>
          <AlertDialogAction disabled={!!r && (r.pioraram > 0 || r.disciplinaAlterada > 0) && !aceito} onClick={() => void incluir()}>Incluir</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog open={!!alternar} onOpenChange={(o) => { if (!o) setAlternar(null); }}>
      <AlertDialogContent>
        <AlertDialogHeader><AlertDialogTitle>{alternar?.excecao.ativo ? "Desativar" : "Reativar"} exceção {alternar?.excecao.id}</AlertDialogTitle>
          <AlertDialogDescription asChild><div className="space-y-2 text-sm">
            {alternar && <p className="font-mono text-xs">{descreverExcecao(alternar.excecao)}</p>}
            {alternar?.excecao.ativo && (alternar.impacto
              ? <p>Os documentos dela voltam ao DOC3: sem mapeamento {alternar.impacto.resumo.semMapeamentoAntes} → {alternar.impacto.resumo.semMapeamentoDepois}.</p>
              : <p><Loader2 className="mr-1 inline h-4 w-4 animate-spin" />Calculando impacto…</p>)}
            {!alternar?.excecao.ativo && <p>A exceção volta a valer como foi cadastrada. Gere uma prévia antes, se o inventário mudou.</p>}
          </div></AlertDialogDescription></AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancelar</AlertDialogCancel>
          <AlertDialogAction onClick={() => void confirmarAlternar()}>{alternar?.excecao.ativo ? "Desativar" : "Reativar"}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>;
}

function Aviso({ tipo, children }: { tipo: "ok" | "erro" | "info"; children: React.ReactNode }) {
  return <div role={tipo === "erro" ? "alert" : "status"} className={cn("flex items-start gap-2 rounded-lg border px-3 py-2 text-sm",
    tipo === "ok" ? "border-emerald-500/40 bg-emerald-500/10" : tipo === "erro" ? "border-destructive/50 bg-destructive/10" : "border-amber-500/40 bg-amber-500/10")}>
    {tipo === "ok" ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" /> : <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />}
    <div className="min-w-0 flex-1">{children}</div>
  </div>;
}

function Indicador({ rotulo, valor, alerta }: { rotulo: string; valor: string; alerta?: boolean }) {
  return <div className={cn("rounded-lg border px-3 py-2", alerta ? "border-destructive/50 bg-destructive/5" : "border-border bg-background/60")}>
    <dt className="text-[11px] font-bold uppercase tracking-wider text-muted-foreground">{rotulo}</dt>
    <dd className={cn("text-lg font-black tabular-nums", alerta && "text-destructive")}>{valor}</dd>
  </div>;
}
