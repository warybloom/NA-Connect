// Configurações → Normalizador de Siglas (19/10/2026). Siglas de disciplina diferentes → MESMO nome canônico PT-BR, para
// somar HH/entregas nos painéis MULTIPROJETO (Gerência da Engenharia). Não renomeia LDP, Timesheet, Norma, projeto nem
// cadastros. Regras: tabela editável (pesquisar/adicionar/corrigir/ativar/desativar/excluir com confirmação) e importação
// assistida de Excel (cabeçalhos REAIS do arquivo → de/para → prévia linha a linha → confirmar). Persistência auditável
// no schema próprio normalizador.* (migração manual em Migrations/Normalizador).
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, CheckCircle2, FileSpreadsheet, Languages, Loader2, Pencil, Plus, RefreshCw, Search, Trash2, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { cn } from "@/lib/utils";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { FONTES, type ContextoTipo, type RegraSigla } from "@/features/disciplinas/normalizadorSiglas";
import {
  NormalizadorError, atualizarRegra, confirmarImportacao, criarRegra, definirAtivo, excluirRegra, listarImportacoes, listarRegras, preverImportacao,
  type DadosRegra, type ImportacaoRegistrada, type LinhaPlano, type MapeamentoColunas, type PreviaImportacao, type ResultadoImportacao,
} from "./normalizadorSiglasApi";
import { ORDEM_ACOES, filtrarRegras, gruposPorNomeCanonico, mapeamentoCompleto, rotuloContexto, validarRegra, type FiltroStatus } from "./normalizadorSiglasLogic";

type Aba = "regras" | "importar" | "historico";
type Aviso = { tipo: "ok" | "erro"; texto: string } | null;
const VAZIA: DadosRegra = { siglaOriginal: "", disciplinaCanonica: "", contextoTipo: null, contextoValor: null, ativo: true };
const campo = "h-9 rounded-lg border border-border bg-background px-2 text-sm text-foreground";
const rotuloCampo = "flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted-foreground";
const dataHora = (v: string) => { const d = new Date(v.endsWith("Z") ? v : v + "Z"); return Number.isNaN(d.valueOf()) ? v : d.toLocaleString("pt-BR"); };
const msg = (e: unknown) => (e instanceof Error ? e.message : "Falha inesperada.");

export default function NormalizadorSiglasPage() {
  const { can } = usePermissions();
  const podeEditar = can("settings.projects.edit");
  const [aba, setAba] = useState<Aba>("regras");
  const [regras, setRegras] = useState<RegraSigla[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<NormalizadorError | Error | null>(null);
  const [aviso, setAviso] = useState<Aviso>(null);

  const carregar = useCallback(async (signal?: AbortSignal) => {
    setCarregando(true); setErro(null);
    try { const r = await listarRegras(signal); if (!signal?.aborted) setRegras(r.regras); }
    catch (e) { if (!signal?.aborted) setErro(e instanceof Error ? e : new Error(msg(e))); }
    finally { if (!signal?.aborted) setCarregando(false); }
  }, []);
  useEffect(() => { const c = new AbortController(); void carregar(c.signal); return () => c.abort(); }, [carregar]);

  const naoInstalado = erro instanceof NormalizadorError && erro.naoInstalado;
  return (
    <div className="space-y-4 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <Languages className="h-6 w-6 text-primary" aria-hidden />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Normalizador de Siglas</h1>
          <p className="text-sm text-muted-foreground">
            Siglas de disciplina diferentes com o mesmo nome canônico em português, para somar HH e entregas nos painéis multiprojeto.
            Não altera LDP, Timesheet, Norma de Coordenação nem cadastros: as telas de um projeto continuam com as disciplinas originais.
          </p>
        </div>
        <div role="tablist" aria-label="Seções do normalizador" className="flex rounded-lg border border-border bg-background/60 p-0.5">
          {([["regras", "Regras"], ["importar", "Importar Excel"], ["historico", "Histórico"]] as const).map(([valor, rotulo]) => (
            <button key={valor} type="button" role="tab" aria-selected={aba === valor} onClick={() => setAba(valor)}
              className={cn("rounded-md px-3 py-1.5 text-xs font-black uppercase tracking-wider", aba === valor ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:text-foreground")}>
              {rotulo}
            </button>
          ))}
        </div>
      </section>

      {aviso && <Faixa tipo={aviso.tipo} onFechar={() => setAviso(null)}>{aviso.texto}</Faixa>}
      {!podeEditar && !erro && <Faixa tipo="info">Somente leitura: editar e importar exigem a permissão de edição de Configurações (settings.projects.edit).</Faixa>}

      {carregando ? <Estado><Loader2 className="h-5 w-5 animate-spin" /> Carregando regras…</Estado>
        : naoInstalado ? <Estado erro>
            <b>Normalizador ainda não instalado no banco.</b>
            <span>A migração aditiva <code>Migrations/Normalizador/20261019_001_NormalizadorSiglas_UP.sql</code> precisa ser aplicada (com backup) pelo responsável.
              Até lá, os painéis multiprojeto seguem sem normalização.</span>
          </Estado>
        : erro ? <Estado erro><b>{erro.message}</b><Button size="sm" variant="outline" onClick={() => void carregar()}><RefreshCw className="h-4 w-4" /> Tentar novamente</Button></Estado>
        : aba === "regras" ? <Regras regras={regras} podeEditar={podeEditar} onMudou={(r, texto) => { setRegras(r); setAviso({ tipo: "ok", texto }); }}
            onErro={(e) => setAviso({ tipo: "erro", texto: msg(e) })} onRecarregar={() => void carregar()} />
        : aba === "importar" ? <Importar podeEditar={podeEditar} onConcluido={(texto) => { setAviso({ tipo: "ok", texto }); void carregar(); }} />
        : <Historico />}
    </div>
  );
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
function Confirmar({ aberto, titulo, children, acao, onConfirmar, onCancelar, perigo }: {
  aberto: boolean; titulo: string; children: ReactNode; acao: string; onConfirmar: () => void; onCancelar: () => void; perigo?: boolean;
}) {
  return <AlertDialog open={aberto} onOpenChange={(o) => { if (!o) onCancelar(); }}>
    <AlertDialogContent>
      <AlertDialogHeader><AlertDialogTitle>{titulo}</AlertDialogTitle><AlertDialogDescription asChild><div className="space-y-2 text-sm">{children}</div></AlertDialogDescription></AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>Cancelar</AlertDialogCancel>
        <AlertDialogAction className={perigo ? "bg-destructive text-destructive-foreground hover:bg-destructive/90" : undefined} onClick={onConfirmar}>{acao}</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>;
}

/* ------------------------------------------------------------------ REGRAS */
function Regras({ regras, podeEditar, onMudou, onErro, onRecarregar }: {
  regras: RegraSigla[]; podeEditar: boolean; onMudou: (r: RegraSigla[], texto: string) => void; onErro: (e: unknown) => void; onRecarregar: () => void;
}) {
  const [busca, setBusca] = useState("");
  const [status, setStatus] = useState<FiltroStatus>("todas");
  const [edicao, setEdicao] = useState<{ id: number | null; versao: number; dados: DadosRegra } | null>(null);
  const [excluir, setExcluir] = useState<RegraSigla | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const visiveis = useMemo(() => filtrarRegras(regras, busca, status), [regras, busca, status]);
  const grupos = useMemo(() => gruposPorNomeCanonico(regras).filter((g) => g.siglas.length > 1), [regras]);
  const errosEdicao = edicao ? validarRegra(edicao.dados, regras, edicao.id) : [];

  const executar = async (f: () => Promise<RegraSigla[]>, texto: string) => {
    setOcupado(true);
    try { onMudou(await f(), texto); }
    catch (e) { onErro(e); if (e instanceof NormalizadorError && e.status === 409) onRecarregar(); }
    finally { setOcupado(false); }
  };
  const salvar = () => edicao && void executar(async () => {
    const salva = edicao.id === null ? await criarRegra(edicao.dados) : await atualizarRegra(edicao.id, edicao.versao, edicao.dados);
    setEdicao(null);
    return edicao.id === null ? [...regras, salva] : regras.map((r) => (r.id === salva.id ? salva : r));
  }, edicao.id === null ? "Regra incluída." : "Regra corrigida.");
  const alternar = (r: RegraSigla) => void executar(async () => {
    const salva = await definirAtivo(r.id, r.versao, !r.ativo);
    return regras.map((x) => (x.id === salva.id ? salva : x));
  }, r.ativo ? `Sigla ${r.siglaOriginal} desativada.` : `Sigla ${r.siglaOriginal} ativada.`);

  return <div className="space-y-3">
    <div className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card/40 p-3">
      <label className={cn(rotuloCampo, "min-w-[14rem] flex-1")}>Pesquisar
        <span className="relative"><Search className="pointer-events-none absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" aria-hidden />
          <input type="search" className={cn(campo, "w-full pl-8 normal-case")} placeholder="Sigla, disciplina ou contexto" value={busca} onChange={(e) => setBusca(e.target.value)} /></span>
      </label>
      <label className={rotuloCampo}>Situação
        <select className={cn(campo, "normal-case")} value={status} onChange={(e) => setStatus(e.target.value as FiltroStatus)}>
          <option value="todas">Todas</option><option value="ativas">Ativas</option><option value="inativas">Inativas</option>
        </select></label>
      {podeEditar && <Button onClick={() => setEdicao({ id: null, versao: 0, dados: { ...VAZIA } })}><Plus className="h-4 w-4" /> Adicionar regra</Button>}
    </div>

    {edicao && <form className="space-y-3 rounded-xl border border-primary/40 bg-card/60 p-4" onSubmit={(e) => { e.preventDefault(); if (!errosEdicao.length) salvar(); }}
      aria-label={edicao.id === null ? "Nova regra" : "Corrigir regra"}>
      <h2 className="text-sm font-black uppercase tracking-wider">{edicao.id === null ? "Nova regra" : "Corrigir regra"}</h2>
      <FormRegra dados={edicao.dados} onChange={(dados) => setEdicao({ ...edicao, dados })} />
      {errosEdicao.length > 0 && <ul className="list-disc pl-5 text-xs text-destructive">{errosEdicao.map((x) => <li key={x}>{x}</li>)}</ul>}
      <div className="flex gap-2">
        <Button type="submit" disabled={ocupado || errosEdicao.length > 0}>{ocupado ? <Loader2 className="h-4 w-4 animate-spin" /> : null}Salvar</Button>
        <Button type="button" variant="outline" onClick={() => setEdicao(null)}>Cancelar</Button>
      </div>
    </form>}

    <div className="overflow-x-auto rounded-xl border border-border">
      <table className="w-full min-w-[46rem] text-sm">
        <caption className="sr-only">Regras de normalização de siglas</caption>
        <thead className="bg-muted/50 text-left text-xs uppercase tracking-wider text-muted-foreground">
          <tr><th className="px-3 py-2">Sigla original</th><th className="px-3 py-2">Disciplina canônica (PT-BR)</th><th className="px-3 py-2">Contexto</th>
            <th className="px-3 py-2">Ativo</th><th className="px-3 py-2">Origem</th><th className="px-3 py-2">Alterado</th><th className="px-3 py-2"><span className="sr-only">Ações</span></th></tr>
        </thead>
        <tbody>
          {visiveis.map((r) => <tr key={r.id} className={cn("border-t border-border", !r.ativo && "opacity-60")}>
            <td className="px-3 py-2 font-mono font-bold">{r.siglaOriginal}</td>
            <td className="px-3 py-2">{r.disciplinaCanonica}</td>
            <td className="px-3 py-2">{rotuloContexto(r)}</td>
            <td className="px-3 py-2">
              <label className="inline-flex items-center gap-2"><input type="checkbox" checked={r.ativo} disabled={!podeEditar || ocupado} onChange={() => alternar(r)}
                aria-label={`${r.ativo ? "Desativar" : "Ativar"} sigla ${r.siglaOriginal}`} />{r.ativo ? "Sim" : "Não"}</label>
            </td>
            <td className="px-3 py-2 text-xs">{r.origem === "IMPORTACAO" ? `Importação #${r.importacaoId}` : "Manual"}</td>
            <td className="px-3 py-2 text-xs" title={`Criado em ${dataHora(r.criadoEm)} por ${r.criadoPor}`}>{dataHora(r.alteradoEm)}<br />{r.alteradoPor}</td>
            <td className="px-3 py-2 text-right whitespace-nowrap">
              {podeEditar && <>
                <Button size="sm" variant="ghost" aria-label={`Corrigir sigla ${r.siglaOriginal}`} onClick={() => setEdicao({ id: r.id, versao: r.versao,
                  dados: { siglaOriginal: r.siglaOriginal, disciplinaCanonica: r.disciplinaCanonica, contextoTipo: r.contextoTipo, contextoValor: r.contextoValor, ativo: r.ativo } })}>
                  <Pencil className="h-4 w-4" /></Button>
                <Button size="sm" variant="ghost" aria-label={`Excluir sigla ${r.siglaOriginal}`} onClick={() => setExcluir(r)}><Trash2 className="h-4 w-4 text-destructive" /></Button>
              </>}
            </td>
          </tr>)}
        </tbody>
      </table>
      {!visiveis.length && <p className="p-6 text-center text-sm text-muted-foreground">
        {regras.length ? "Nenhuma regra corresponde à pesquisa." : "Nenhuma regra cadastrada. Importe a planilha oficial de mapeamento ou adicione regras manualmente."}</p>}
    </div>
    <p className="text-xs text-muted-foreground">{regras.filter((r) => r.ativo).length} ativa(s) de {regras.length}. Prioridade na agregação: regra do contexto
      (projeto/fonte) → regra global → nome original. Conflitos não são resolvidos automaticamente: o item fica com o nome original e o painel sinaliza.</p>
    {grupos.length > 0 && <details className="rounded-xl border border-border bg-card/40 p-3 text-sm">
      <summary className="cursor-pointer font-bold">{grupos.length} nome(s) canônico(s) que somam siglas diferentes</summary>
      <ul className="mt-2 space-y-1">{grupos.map((g) => <li key={g.nome}><b>{g.nome}</b>: {g.siglas.join(", ")}</li>)}</ul>
    </details>}

    <Confirmar aberto={!!excluir} titulo="Excluir regra?" acao="Excluir" perigo onCancelar={() => setExcluir(null)}
      onConfirmar={() => { const r = excluir!; setExcluir(null); void executar(async () => { await excluirRegra(r.id, r.versao); return regras.filter((x) => x.id !== r.id); }, `Regra ${r.siglaOriginal} excluída.`); }}>
      {excluir && <><p><b>{excluir.siglaOriginal}</b> → {excluir.disciplinaCanonica} ({rotuloContexto(excluir)})</p>
        <p>A regra sai da agregação multiprojeto. O retrato completo fica na auditoria. Para suspender sem excluir, use "Ativo".</p></>}
    </Confirmar>
  </div>;
}

function FormRegra({ dados, onChange }: { dados: DadosRegra; onChange: (d: DadosRegra) => void }) {
  return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
    <label className={rotuloCampo}>Sigla original
      <input className={cn(campo, "normal-case")} maxLength={50} required value={dados.siglaOriginal} onChange={(e) => onChange({ ...dados, siglaOriginal: e.target.value })} /></label>
    <label className={cn(rotuloCampo, "lg:col-span-2")}>Disciplina canônica (PT-BR)
      <input className={cn(campo, "normal-case")} maxLength={200} required value={dados.disciplinaCanonica} onChange={(e) => onChange({ ...dados, disciplinaCanonica: e.target.value })} /></label>
    <label className={rotuloCampo}>Contexto
      <select className={cn(campo, "normal-case")} value={dados.contextoTipo ?? ""}
        onChange={(e) => { const t = (e.target.value || null) as ContextoTipo | null; onChange({ ...dados, contextoTipo: t, contextoValor: t ? (t === "FONTE" ? FONTES[0] : "") : null }); }}>
        <option value="">Global (todos)</option><option value="PROJETO">Projeto específico</option><option value="FONTE">Fonte específica</option>
      </select></label>
    {dados.contextoTipo === "PROJETO" && <label className={rotuloCampo}>Projeto (código ou ID ACC)
      <input className={cn(campo, "normal-case")} maxLength={200} placeholder="Ex.: código do projeto" value={dados.contextoValor ?? ""} onChange={(e) => onChange({ ...dados, contextoValor: e.target.value })} /></label>}
    {dados.contextoTipo === "FONTE" && <label className={rotuloCampo}>Fonte
      <select className={cn(campo, "normal-case")} value={dados.contextoValor ?? FONTES[0]} onChange={(e) => onChange({ ...dados, contextoValor: e.target.value })}>
        {FONTES.map((f) => <option key={f} value={f}>{f}</option>)}</select></label>}
    <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={dados.ativo} onChange={(e) => onChange({ ...dados, ativo: e.target.checked })} /> Ativa</label>
  </div>;
}

/* ------------------------------------------------------------------ IMPORTAR */
const PAPEIS: [keyof MapeamentoColunas, string, boolean][] = [
  ["sigla", "Sigla original", true], ["disciplina", "Disciplina canônica PT-BR", true],
  ["contextoTipo", "Tipo de contexto (PROJETO/FONTE)", false], ["contextoValor", "Contexto (projeto/fonte)", false], ["ativo", "Ativo (SIM/NÃO)", false],
];
const COR_ACAO: Record<LinhaPlano["acao"], string> = {
  Incluir: "text-emerald-700 dark:text-emerald-400", Alterar: "text-amber-700 dark:text-amber-400", Inalterada: "text-muted-foreground",
  Rejeitada: "text-destructive", Duplicada: "text-muted-foreground",
};

function Importar({ podeEditar, onConcluido }: { podeEditar: boolean; onConcluido: (texto: string) => void }) {
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [previa, setPrevia] = useState<PreviaImportacao | null>(null);
  const [aba, setAba] = useState<string | null>(null);
  const [mapa, setMapa] = useState<Partial<MapeamentoColunas>>({});
  const [aplicarAlteracoes, setAplicarAlteracoes] = useState(false);
  const [filtro, setFiltro] = useState<LinhaPlano["acao"] | "todas">("todas");
  const [ocupado, setOcupado] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [confirmar, setConfirmar] = useState(false);
  const [resultado, setResultado] = useState<ResultadoImportacao | null>(null);
  const entrada = useRef<HTMLInputElement>(null);

  const prever = async (f: File, abaEscolhida: string | null, m: Partial<MapeamentoColunas>, alterar: boolean) => {
    setOcupado(true); setErro(null); setResultado(null);
    try {
      const p = await preverImportacao(f, abaEscolhida, mapeamentoCompleto(m) ? m : null, alterar);
      setPrevia(p); setAba(p.aba);
      // Primeira leitura: sugestão SÓ por nome exato de cabeçalho; o usuário confirma/corrige antes da prévia.
      if (!mapeamentoCompleto(m) && p.mapeamentoSugerido) setMapa(p.mapeamentoSugerido);
    } catch (e) { setErro(msg(e)); } finally { setOcupado(false); }
  };
  const escolher = (f: File | null) => { setArquivo(f); setPrevia(null); setMapa({}); setAba(null); setResultado(null); setAplicarAlteracoes(false); if (f) void prever(f, null, {}, false); };
  const linhas = previa?.plano?.linhas ?? [];
  const visiveis = (filtro === "todas" ? linhas : linhas.filter((l) => l.acao === filtro))
    .slice().sort((a, b) => ORDEM_ACOES.indexOf(a.acao) - ORDEM_ACOES.indexOf(b.acao) || a.linha - b.linha);
  const r = previa?.resumo;
  const gravaveis = r ? r.incluidas + r.alteradas : 0;

  const aplicar = async () => {
    if (!arquivo || !previa || !mapeamentoCompleto(mapa)) return;
    setOcupado(true); setErro(null);
    try {
      const res = await confirmarImportacao(arquivo, previa.hashSha256, aba, mapa, aplicarAlteracoes);
      setResultado(res); setPrevia(null);
      onConcluido(`Importação #${res.importacaoId}: ${res.resumo.incluidas} incluída(s), ${res.resumo.alteradas} alterada(s), ${res.resumo.inalteradas} inalterada(s), ${res.resumo.rejeitadas} rejeitada(s), ${res.resumo.duplicadas} duplicada(s).`);
    } catch (e) { setErro(msg(e)); } finally { setOcupado(false); }
  };

  if (!podeEditar) return <Estado>Importar exige a permissão de edição de Configurações.</Estado>;
  return <div className="space-y-3">
    <div className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card/40 p-3">
      <input ref={entrada} type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" className="sr-only" aria-label="Arquivo Excel (.xlsx)"
        onChange={(e) => escolher(e.target.files?.[0] ?? null)} />
      <Button variant="outline" onClick={() => entrada.current?.click()} disabled={ocupado}><Upload className="h-4 w-4" /> Escolher planilha (.xlsx)</Button>
      {arquivo && <span className="flex items-center gap-1 text-sm"><FileSpreadsheet className="h-4 w-4" /> {arquivo.name}</span>}
      {previa && previa.abas.length > 1 && <label className={rotuloCampo}>Aba
        <select className={cn(campo, "normal-case")} value={aba ?? ""} onChange={(e) => { setAba(e.target.value); setMapa({}); void prever(arquivo!, e.target.value, {}, aplicarAlteracoes); }}>
          {previa.abas.map((x) => <option key={x} value={x}>{x}</option>)}</select></label>}
      {ocupado && <Loader2 className="h-5 w-5 animate-spin" aria-label="Processando" />}
    </div>
    <p className="text-xs text-muted-foreground">Nenhuma coluna é presumida: escolha abaixo qual cabeçalho do arquivo corresponde a cada informação. Linhas ausentes no arquivo
      nunca apagam regras existentes; reimportar o mesmo arquivo não muda nada.</p>
    {erro && <Faixa tipo="erro">{erro}</Faixa>}

    {previa && <>
      {previa.erros.length > 0 && <Faixa tipo="erro"><ul className="list-disc pl-4">{previa.erros.map((x) => <li key={x}>{x}</li>)}</ul></Faixa>}
      <section className="space-y-2 rounded-xl border border-border bg-card/40 p-3" aria-label="De/para de colunas">
        <h2 className="text-sm font-black uppercase tracking-wider">De/para de colunas — aba “{previa.aba}”</h2>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          {PAPEIS.map(([papel, rotulo, obrigatorio]) => <label key={papel} className={rotuloCampo}>{rotulo}{obrigatorio ? " *" : ""}
            <select className={cn(campo, "normal-case")} value={mapa[papel] ?? ""} onChange={(e) => setMapa({ ...mapa, [papel]: e.target.value || null })}>
              <option value="">{obrigatorio ? "Selecione…" : "(não usar)"}</option>
              {previa.cabecalhos.map((h) => <option key={h} value={h}>{h}</option>)}</select></label>)}
        </div>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={aplicarAlteracoes} onChange={(e) => setAplicarAlteracoes(e.target.checked)} />
          Aplicar alterações de disciplina em regras já existentes (conflitos). Desmarcado = conflitos são rejeitados e listados.</label>
        <Button disabled={!mapeamentoCompleto(mapa) || ocupado} onClick={() => void prever(arquivo!, aba, mapa, aplicarAlteracoes)}>Gerar prévia</Button>
        {!previa.plano && previa.amostra.length > 0 && <Amostra cabecalhos={previa.cabecalhos} linhas={previa.amostra} />}
      </section>

      {r && <section className="space-y-2" aria-label="Prévia da importação">
        <div className="flex flex-wrap gap-2 text-sm" role="group" aria-label="Filtrar linhas da prévia">
          {([["todas", "Todas", linhas.length], ["Incluir", "Incluir", r.incluidas], ["Alterar", "Alterar", r.alteradas], ["Inalterada", "Inalteradas", r.inalteradas],
            ["Rejeitada", "Rejeitadas", r.rejeitadas], ["Duplicada", "Duplicadas", r.duplicadas]] as const).map(([v, rot, n]) =>
            <button key={v} type="button" aria-pressed={filtro === v} onClick={() => setFiltro(v)}
              className={cn("rounded-full border px-3 py-1", filtro === v ? "border-primary bg-primary text-primary-foreground" : "border-border")}>{rot}: <b>{n}</b></button>)}
          {r.conflitosComExistente > 0 && <span className="rounded-full border border-amber-500/60 px-3 py-1">Conflitos com regras existentes: <b>{r.conflitosComExistente}</b></span>}
        </div>
        <TabelaPlano linhas={visiveis} />
        <Button disabled={!gravaveis || ocupado || previa.erros.length > 0} onClick={() => setConfirmar(true)}>Confirmar importação ({gravaveis} gravação(ões))</Button>
      </section>}
    </>}

    {resultado && <section className="space-y-2" aria-label="Relatório da importação">
      <h2 className="text-sm font-black uppercase tracking-wider">Relatório da importação #{resultado.importacaoId}</h2>
      <TabelaPlano linhas={[...resultado.linhas].sort((a, b) => ORDEM_ACOES.indexOf(a.acao) - ORDEM_ACOES.indexOf(b.acao) || a.linha - b.linha)} />
    </section>}

    <Confirmar aberto={confirmar} titulo="Confirmar importação?" acao="Importar" onCancelar={() => setConfirmar(false)} onConfirmar={() => { setConfirmar(false); void aplicar(); }}>
      {r && <><p>{r.incluidas} regra(s) nova(s) e {r.alteradas} alteração(ões) serão gravadas com auditoria. {r.rejeitadas} linha(s) rejeitada(s) e {r.duplicadas} duplicada(s) não são gravadas.</p>
        {aplicarAlteracoes && r.conflitosComExistente > 0 && <p><b>{r.conflitosComExistente} regra(s) existente(s) terão a disciplina substituída.</b></p>}
        <p>O servidor recalcula a prévia no momento da gravação (mesmo arquivo, conferido por hash).</p></>}
    </Confirmar>
  </div>;
}

function Amostra({ cabecalhos, linhas }: { cabecalhos: string[]; linhas: Record<string, string | null>[] }) {
  return <div className="overflow-x-auto"><table className="w-full text-xs"><caption className="text-left text-muted-foreground">Primeiras {linhas.length} linha(s) do arquivo</caption>
    <thead><tr>{cabecalhos.map((h) => <th key={h} className="border-b border-border px-2 py-1 text-left">{h}</th>)}</tr></thead>
    <tbody>{linhas.map((l, i) => <tr key={i}>{cabecalhos.map((h) => <td key={h} className="border-b border-border/50 px-2 py-1">{l[h] ?? ""}</td>)}</tr>)}</tbody></table></div>;
}

function TabelaPlano({ linhas }: { linhas: LinhaPlano[] }) {
  return <div className="max-h-[28rem] overflow-auto rounded-xl border border-border">
    <table className="w-full min-w-[46rem] text-sm">
      <thead className="sticky top-0 bg-muted text-left text-xs uppercase tracking-wider text-muted-foreground">
        <tr><th className="px-3 py-2">Linha</th><th className="px-3 py-2">Resultado</th><th className="px-3 py-2">Sigla</th><th className="px-3 py-2">Disciplina canônica</th>
          <th className="px-3 py-2">Contexto</th><th className="px-3 py-2">Ativo</th><th className="px-3 py-2">Detalhe</th></tr>
      </thead>
      <tbody>{linhas.map((l) => <tr key={l.linha} className="border-t border-border">
        <td className="px-3 py-1.5 tabular-nums">{l.linha}</td>
        <td className={cn("px-3 py-1.5 font-bold", COR_ACAO[l.acao])}>{l.acao}{l.conflitoComExistente ? " ⚠" : ""}</td>
        <td className="px-3 py-1.5 font-mono">{l.siglaOriginal ?? "—"}</td>
        <td className="px-3 py-1.5">{l.disciplinaCanonica ?? "—"}{l.disciplinaAtual && l.disciplinaAtual !== l.disciplinaCanonica && <small className="block text-muted-foreground">atual: {l.disciplinaAtual}</small>}</td>
        <td className="px-3 py-1.5">{l.contextoTipo ? `${l.contextoTipo}: ${l.contextoValor ?? ""}` : "Global"}</td>
        <td className="px-3 py-1.5">{l.ativo === null ? "—" : l.ativo ? "Sim" : "Não"}</td>
        <td className="px-3 py-1.5 text-xs">{l.motivo ?? ""}</td>
      </tr>)}</tbody>
    </table>
    {!linhas.length && <p className="p-4 text-center text-sm text-muted-foreground">Nenhuma linha neste filtro.</p>}
  </div>;
}

/* ------------------------------------------------------------------ HISTÓRICO */
function Historico() {
  const [itens, setItens] = useState<ImportacaoRegistrada[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  useEffect(() => { const c = new AbortController(); listarImportacoes(c.signal).then(setItens, (e) => { if (!c.signal.aborted) setErro(msg(e)); }); return () => c.abort(); }, []);
  if (erro) return <Estado erro>{erro}</Estado>;
  if (!itens) return <Estado><Loader2 className="h-5 w-5 animate-spin" /> Carregando histórico…</Estado>;
  if (!itens.length) return <Estado>Nenhuma importação registrada.</Estado>;
  return <div className="overflow-x-auto rounded-xl border border-border"><table className="w-full min-w-[46rem] text-sm">
    <thead className="bg-muted/50 text-left text-xs uppercase tracking-wider text-muted-foreground"><tr>
      <th className="px-3 py-2">#</th><th className="px-3 py-2">Arquivo</th><th className="px-3 py-2">Aba</th><th className="px-3 py-2">Incl.</th><th className="px-3 py-2">Alt.</th>
      <th className="px-3 py-2">Inalt.</th><th className="px-3 py-2">Rejeit.</th><th className="px-3 py-2">Dupl.</th><th className="px-3 py-2">Quando / quem</th></tr></thead>
    <tbody>{itens.map((i) => <tr key={i.id} className="border-t border-border">
      <td className="px-3 py-2">{i.id}</td><td className="px-3 py-2" title={`SHA-256 ${i.hashSha256}`}>{i.nomeArquivo}</td><td className="px-3 py-2">{i.aba}</td>
      <td className="px-3 py-2">{i.incluidas}</td><td className="px-3 py-2">{i.alteradas}</td><td className="px-3 py-2">{i.inalteradas}</td><td className="px-3 py-2">{i.rejeitadas}</td>
      <td className="px-3 py-2">{i.duplicadas}</td><td className="px-3 py-2 text-xs">{dataHora(i.importadoEm)}<br />{i.importadoPor}</td></tr>)}</tbody>
  </table></div>;
}
