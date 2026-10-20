import { useEffect, useMemo, useState } from "react";
import { CheckCircle2, FileSearch2, Loader2, RefreshCw, Search, Settings2, XCircle } from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import {
  getLatestPresenceAudit,
  runPresenceAudit,
  type DocumentPresenceAuditResult,
  type DocumentPresenceRow,
} from "./documentQualityApi";

const MODO_ACC_SEM_LDP = "acc-sem-ldp";
const MODO_LDP_SEM_ACC = "ldp-sem-acc";
type ModoAuditoria = typeof MODO_ACC_SEM_LDP | typeof MODO_LDP_SEM_ACC;

type LinhaAuditoria = {
  key: string;
  naDocumentNumber: string | null;
  clientDocumentNumber: string | null;
  title: string | null;
  discipline: string | null;
  documentStatus: string | null;
  isCancelled: boolean;
  inLdp: boolean;
  inAcc: boolean;
  foundFiles: string[];
  folderPaths: string[];
  matching: string[];
  result: "OK_NOS_DOIS" | "ACC_SEM_LDP" | "LDP_SEM_ACC" | "CANCELADO_SEM_ACC" | "CANCELADO_NO_ACC";
};

const FILTRO_RESULTADO_PADRAO: Record<ModoAuditoria, LinhaAuditoria["result"]> = {
  [MODO_ACC_SEM_LDP]: "ACC_SEM_LDP",
  [MODO_LDP_SEM_ACC]: "LDP_SEM_ACC",
};

export default function DocumentPresenceAuditPage() {
  const { projetoAtual } = useProjetoAtual();
  const [params] = useSearchParams();
  const modo: ModoAuditoria = params.get("modo") === MODO_ACC_SEM_LDP ? MODO_ACC_SEM_LDP : MODO_LDP_SEM_ACC;

  const [resultado, setResultado] = useState<DocumentPresenceAuditResult | null>(null);
  const [executando, setExecutando] = useState(false);
  const [carregandoHistorico, setCarregandoHistorico] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [disciplina, setDisciplina] = useState("TODOS");
  const [statusDocumento, setStatusDocumento] = useState("EXCETO_CANCELADOS");
  const [resultadoFiltro, setResultadoFiltro] = useState<string>(FILTRO_RESULTADO_PADRAO[modo]);
  const [busca, setBusca] = useState("");

  useEffect(() => {
    setResultadoFiltro(FILTRO_RESULTADO_PADRAO[modo]);
    setStatusDocumento(modo === MODO_LDP_SEM_ACC ? "EXCETO_CANCELADOS" : "TODOS");
    setDisciplina("TODOS");
    setBusca("");
  }, [modo]);

  useEffect(() => {
    if (!projetoAtual?.projectId) return;
    const controller = new AbortController();
    setResultado(null);
    setCarregandoHistorico(true);
    setErro(null);
    void getLatestPresenceAudit(projetoAtual.projectId, controller.signal)
      .then((latest) => {
        if (latest) setResultado(latest);
      })
      .catch((e) => {
        if (e instanceof DOMException && e.name === "AbortError") return;
        setErro(e instanceof Error ? e.message : "Falha ao carregar o histórico da auditoria.");
      })
      .finally(() => setCarregandoHistorico(false));
    return () => controller.abort();
  }, [projetoAtual?.projectId]);

  const linhas = useMemo<LinhaAuditoria[]>(() => {
    if (!resultado) return [];

    const daLdp = resultado.documents.map((doc): LinhaAuditoria => {
      const files = [...new Set(doc.locations.map((x) => x.fileName).filter(Boolean))];
      const folders = [...new Set(doc.locations.map((x) => x.folderPath).filter(Boolean))];
      const matching = [...new Set(doc.locations
        .map((x) => x.matchedBy && x.matchedCode ? `${x.matchedBy}: ${x.matchedCode}` : "")
        .filter(Boolean))];

      const inAcc = doc.occurrences > 0;
      const result: LinhaAuditoria["result"] = doc.isCancelled
        ? (inAcc ? "CANCELADO_NO_ACC" : "CANCELADO_SEM_ACC")
        : (inAcc ? "OK_NOS_DOIS" : "LDP_SEM_ACC");

      return {
        key: `ldp:${doc.ldpRecordId}`,
        naDocumentNumber: doc.naDocumentNumber,
        clientDocumentNumber: doc.clientDocumentNumber,
        title: doc.title,
        discipline: doc.discipline,
        documentStatus: doc.documentStatus,
        isCancelled: doc.isCancelled,
        inLdp: true,
        inAcc,
        foundFiles: files,
        folderPaths: folders,
        matching,
        result,
      };
    });

    const soAcc = resultado.outsideFiles.map((file, index): LinhaAuditoria => ({
      key: `acc:${file.folderPath}:${file.fileName}:${index}`,
      naDocumentNumber: null,
      clientDocumentNumber: null,
      title: null,
      discipline: null,
      documentStatus: null,
      isCancelled: false,
      inLdp: false,
      inAcc: true,
      foundFiles: [file.fileName],
      folderPaths: [file.folderPath],
      matching: [file.reason],
      result: "ACC_SEM_LDP",
    }));

    return [...daLdp, ...soAcc];
  }, [resultado]);

  const disciplinas = useMemo(() =>
    [...new Set(linhas.map((x) => (x.discipline ?? "").trim()).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, "pt-BR")),
  [linhas]);

  const statusDisponiveis = useMemo(() =>
    [...new Set(linhas.map((x) => (x.documentStatus ?? "").trim()).filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, "pt-BR")),
  [linhas]);

  const filtradas = useMemo(() => {
    const q = busca.trim().toUpperCase();
    return linhas.filter((x) => {
      if (resultadoFiltro !== "TODOS" && x.result !== resultadoFiltro) return false;
      if (disciplina !== "TODOS" && (x.discipline ?? "") !== disciplina) return false;
      if (statusDocumento === "EXCETO_CANCELADOS" && x.isCancelled) return false;
      if (statusDocumento === "CANCELADOS" && !x.isCancelled) return false;
      if (statusDocumento !== "TODOS" && statusDocumento !== "EXCETO_CANCELADOS" && statusDocumento !== "CANCELADOS"
        && (x.documentStatus ?? "") !== statusDocumento) return false;
      if (!q) return true;
      return [
        x.naDocumentNumber, x.clientDocumentNumber, x.title, x.discipline, x.documentStatus,
        ...x.foundFiles, ...x.folderPaths, ...x.matching,
      ].some((v) => (v ?? "").toUpperCase().includes(q));
    });
  }, [linhas, resultadoFiltro, disciplina, statusDocumento, busca]);

  if (!projetoAtual)
    return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;

  const executar = async () => {
    setExecutando(true);
    setErro(null);
    try {
      setResultado(await runPresenceAudit(projetoAtual.projectId));
    } catch (e) {
      setErro(e instanceof Error ? e.message : "Falha ao executar a auditoria LDP x ACC.");
    } finally {
      setExecutando(false);
    }
  };

  const titulo = modo === MODO_ACC_SEM_LDP
    ? "Tem no ACC, não tem na LDP"
    : "Tem na LDP, não tem no ACC";

  return (
    <div className="space-y-5 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <FileSearch2 className="h-7 w-7 text-primary" />
        <div className="min-w-0 flex-1">
          <p className="text-[10px] font-black uppercase tracking-[0.22em] text-muted-foreground">Auditoria LDP x ACC</p>
          <h1 className="text-xl font-black">{titulo}</h1>
          <p className="text-sm text-muted-foreground">
            Mesma base e mesmo template nas duas auditorias. N&A Document Number é tentado primeiro; Client Document Number é o fallback.
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            A tabela também mostra exatamente qual arquivo/pasta foi encontrado no ACC e por qual código houve o matching para conferência manual.
          </p>
        </div>
        <Link to="/qualidade/configuracao-pastas-documentos">
          <Button size="sm" variant="outline" className="gap-2"><Settings2 className="h-4 w-4" />Configurar pastas</Button>
        </Link>
        <Button className="gap-2" onClick={() => void executar()} disabled={executando || carregandoHistorico}>
          {executando || carregandoHistorico ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
          {executando ? "Varrendo ACC..." : carregandoHistorico ? "Carregando histórico..." : resultado ? "Atualizar auditoria" : "Executar auditoria"}
        </Button>
      </section>

      {erro && <ReportState title={erro} error>Verifique a configuração das pastas e a conexão com a Autodesk.</ReportState>}

      {resultado && (
        <>
          <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
            <Card titulo="Documentos LDP" valor={resultado.totalLdpDocuments} />
            <Card titulo="Presentes nos dois" valor={resultado.present} />
            <Card titulo="LDP sem ACC" valor={resultado.missing} destaque={modo === MODO_LDP_SEM_ACC && resultado.missing > 0} />
            <Card titulo="ACC sem LDP" valor={resultado.outsideLdp} destaque={modo === MODO_ACC_SEM_LDP && resultado.outsideLdp > 0} />
            <Card titulo="Cancelados" valor={resultado.cancelledDocuments} />
            <Card titulo="Pastas varridas" valor={resultado.foldersVisited} />
          </section>

          <section className="rounded-xl border border-border bg-card/60 p-4 space-y-3">
            <div className="flex flex-wrap gap-3">
              <Busca value={busca} onChange={setBusca} />
              <select className="h-9 rounded-md border border-input bg-background px-3 text-sm" value={disciplina} onChange={(e) => setDisciplina(e.target.value)}>
                <option value="TODOS">Todas as disciplinas</option>
                {disciplinas.map((d) => <option key={d} value={d}>{d}</option>)}
              </select>
              <select className="h-9 rounded-md border border-input bg-background px-3 text-sm" value={statusDocumento} onChange={(e) => setStatusDocumento(e.target.value)}>
                <option value="TODOS">Todos os status</option>
                <option value="EXCETO_CANCELADOS">Exceto cancelados</option>
                <option value="CANCELADOS">Somente cancelados</option>
                {statusDisponiveis.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              <select className="h-9 rounded-md border border-input bg-background px-3 text-sm" value={resultadoFiltro} onChange={(e) => setResultadoFiltro(e.target.value)}>
                <option value="TODOS">Todos os resultados</option>
                <option value="LDP_SEM_ACC">LDP sem ACC</option>
                <option value="ACC_SEM_LDP">ACC sem LDP</option>
                <option value="OK_NOS_DOIS">Encontrado nos dois</option>
                <option value="CANCELADO_SEM_ACC">Cancelado sem ACC</option>
                <option value="CANCELADO_NO_ACC">Cancelado ainda no ACC</option>
              </select>
            </div>

            <p className="text-xs text-muted-foreground">
              Exibindo {filtradas.length.toLocaleString("pt-BR")} linha(s). O modo selecionado apenas define o filtro inicial; você pode abrir os encontrados para conferir o matching manualmente.
            </p>

            <AuditTable rows={filtradas} />
          </section>

          <p className="text-xs text-muted-foreground">
            Última auditoria salva: {new Date(resultado.executedAt).toLocaleString("pt-BR")} · Raízes: {resultado.rootsScanned}
            {" · "}O resultado permanece salvo; use "Atualizar auditoria" somente quando quiser refazer a varredura.
          </p>
        </>
      )}

      {!resultado && !erro && carregandoHistorico && (
        <ReportState title="Carregando histórico">Buscando a última auditoria salva deste projeto.</ReportState>
      )}

      {!resultado && !erro && !carregandoHistorico && (
        <ReportState title="Nenhuma auditoria salva">
          Execute uma vez. O mesmo snapshot será usado pelas duas telas e ficará disponível ao retornar depois.
        </ReportState>
      )}
    </div>
  );
}

function Busca({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <label className="relative min-w-[300px] flex-1">
      <Search className="absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="N&A, cliente, título, arquivo, pasta ou matching..."
        className="h-9 w-full rounded-md border border-input bg-background pl-9 pr-3 text-sm"
      />
    </label>
  );
}

function Card({ titulo, valor, destaque = false }: { titulo: string; valor: number; destaque?: boolean }) {
  return (
    <div className={"rounded-xl border p-4 " + (destaque ? "border-rose-500/40 bg-rose-500/5" : "border-border bg-card/60")}>
      <div className="text-[10px] font-black uppercase tracking-widest text-muted-foreground">{titulo}</div>
      <div className={"mt-1 text-3xl font-black " + (destaque ? "text-rose-500" : "")}>{valor.toLocaleString("pt-BR")}</div>
    </div>
  );
}

function AuditTable({ rows }: { rows: LinhaAuditoria[] }) {
  return (
    <div className="overflow-auto rounded-lg border border-border">
      <table className="w-full min-w-[1500px] text-sm">
        <thead className="bg-muted/50 text-left text-xs uppercase text-muted-foreground">
          <tr>
            <th className="px-3 py-2">N&A Document Number</th>
            <th className="px-3 py-2">Client Document Number</th>
            <th className="px-3 py-2">Título</th>
            <th className="px-3 py-2">Disciplina</th>
            <th className="px-3 py-2">Status do documento</th>
            <th className="px-3 py-2 text-center">LDP</th>
            <th className="px-3 py-2 text-center">ACC</th>
            <th className="px-3 py-2">Encontrado no ACC</th>
            <th className="px-3 py-2">Pasta / matching</th>
            <th className="px-3 py-2">Resultado</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => <AuditRow key={row.key} row={row} />)}
          {rows.length === 0 && (
            <tr><td colSpan={10} className="px-3 py-10 text-center text-muted-foreground">Nenhuma linha para os filtros atuais.</td></tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

function AuditRow({ row }: { row: LinhaAuditoria }) {
  return (
    <tr className="border-t border-border/60 align-top">
      <td className="px-3 py-2 font-semibold">{row.naDocumentNumber ?? "—"}</td>
      <td className="px-3 py-2 font-semibold">{row.clientDocumentNumber ?? "—"}</td>
      <td className="max-w-[280px] px-3 py-2">{row.title ?? "—"}</td>
      <td className="px-3 py-2">{row.discipline ?? "—"}</td>
      <td className="px-3 py-2">
        <div>{row.documentStatus ?? "—"}</div>
        {row.isCancelled && <div className="mt-1"><StatusBadge text="CANCELADO" tone="neutral" /></div>}
      </td>
      <PresenceCell present={row.inLdp} />
      <PresenceCell present={row.inAcc} />
      <td className="max-w-[340px] px-3 py-2 text-xs">
        {row.foundFiles.length
          ? row.foundFiles.map((f) => <div key={f} className="font-semibold text-foreground">{f}</div>)
          : <span className="text-muted-foreground">Não encontrado</span>}
      </td>
      <td className="max-w-[420px] px-3 py-2 text-xs text-muted-foreground">
        {row.folderPaths.map((p) => <div key={p}>{p}</div>)}
        {row.matching.length > 0 && <div className="mt-1 font-semibold text-foreground/70">{row.matching.join(" · ")}</div>}
        {!row.folderPaths.length && !row.matching.length ? "—" : null}
      </td>
      <td className="px-3 py-2"><ResultadoBadge result={row.result} /></td>
    </tr>
  );
}

function PresenceCell({ present }: { present: boolean }) {
  return (
    <td className="px-3 py-2 text-center">
      {present
        ? <CheckCircle2 className="mx-auto h-5 w-5 text-emerald-500" aria-label="Sim" />
        : <XCircle className="mx-auto h-5 w-5 text-rose-500" aria-label="Não" />}
    </td>
  );
}

function ResultadoBadge({ result }: { result: LinhaAuditoria["result"] }) {
  if (result === "OK_NOS_DOIS") return <StatusBadge text="OK NOS DOIS" tone="success" />;
  if (result === "ACC_SEM_LDP") return <StatusBadge text="ACC SEM LDP" tone="danger" />;
  if (result === "LDP_SEM_ACC") return <StatusBadge text="LDP SEM ACC" tone="danger" />;
  if (result === "CANCELADO_NO_ACC") return <StatusBadge text="CANCELADO · AINDA NO ACC" tone="warning" />;
  return <StatusBadge text="CANCELADO · SEM ACC" tone="neutral" />;
}

function StatusBadge({ text, tone }: { text: string; tone: "danger" | "warning" | "neutral" | "success" }) {
  const cls = tone === "danger"
    ? "border-rose-500/30 bg-rose-500/10 text-rose-600"
    : tone === "warning"
      ? "border-amber-500/30 bg-amber-500/10 text-amber-600"
      : tone === "success"
        ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-600"
        : "border-border bg-muted/40 text-muted-foreground";
  return <span className={"inline-flex rounded-full border px-2 py-0.5 text-xs font-bold " + cls}>{text}</span>;
}
