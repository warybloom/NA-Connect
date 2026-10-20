// CDE → Envio de Documentos (governado pela LDP). Fluxo: escolher a PASTA DE DESTINO → escolher arquivos → PRÉ-VALIDAÇÃO
// (nada é enviado) → enviar só os permitidos. Se o destino está numa árvore protegida (pasta-raiz configurada ou qualquer
// subpasta dela), só documento que existe na LDP sobe; fora das árvores a regra não se aplica. O servidor revalida tudo.
import { useRef, useState } from "react";
import { AlertTriangle, CheckCircle2, FileUp, Folder, FolderLock, Loader2, Upload, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ReportState } from "@/components/reports/ReportComponents";
import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";
import { cn } from "@/lib/utils";
import NavegadorPastas from "./NavegadorPastas";
import { ROTULO_STATUS, enviarArquivos, permiteEnvio, validarArquivos, type Destino, type PastaProtegida, type ResultadoEnvio, type Validacao } from "./uploadGovernadoApi";

export default function EnvioDocumentosPage() {
  const { projetoAtual } = useProjetoAtual();
  const input = useRef<HTMLInputElement>(null);
  const [destino, setDestino] = useState<Destino | null>(null);
  const [navegando, setNavegando] = useState(false);
  const [arquivos, setArquivos] = useState<File[]>([]);
  const [validacoes, setValidacoes] = useState<Validacao[]>([]);
  const [protecao, setProtecao] = useState<{ protegida: boolean; raiz: PastaProtegida | null } | null>(null);
  const [envio, setEnvio] = useState<ResultadoEnvio[] | null>(null);
  const [ocupado, setOcupado] = useState<"validando" | "enviando" | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  if (!projetoAtual) return <ReportState title="Nenhum projeto selecionado">Escolha um projeto na tela Início.</ReportState>;

  const validar = async (d: Destino, fs: File[]) => {
    setValidacoes([]); setEnvio(null); setErro(null); setProtecao(null);
    if (fs.length === 0) return;
    setOcupado("validando");
    try {
      const r = await validarArquivos(projetoAtual.hubId, projetoAtual.projectId, d, fs.map((f) => f.name));
      setValidacoes(r.resultados); setProtecao({ protegida: r.protegida, raiz: r.raiz });
    } catch (e) { setErro(e instanceof Error ? e.message : "Falha na pré-validação."); }
    finally { setOcupado(null); }
  };
  const escolherArquivos = (lista: FileList | null) => {
    const fs = Array.from(lista ?? []);
    setArquivos(fs);
    if (destino) void validar(destino, fs);
  };
  const permitidos = arquivos.filter((_, i) => validacoes[i] && permiteEnvio(validacoes[i].status));
  const enviar = async () => {
    if (!destino || permitidos.length === 0) return;
    setOcupado("enviando"); setErro(null);
    try { setEnvio(await enviarArquivos(projetoAtual.hubId, projetoAtual.projectId, destino, permitidos)); }
    catch (e) { setErro(e instanceof Error ? e.message : "Falha no envio."); }
    finally { setOcupado(null); }
  };
  const resultadoDe = (nome: string) => envio?.find((r) => r.validacao.arquivo === nome);

  return (
    <div className="space-y-4 animate-enter">
      <section className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card/60 p-5">
        <FileUp className="h-6 w-6 text-primary" />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-black tracking-tight">Envio de Documentos</h1>
          <p className="text-sm text-muted-foreground">
            {projetoAtual.nome ?? projetoAtual.projectId} — em pastas protegidas, só sobe documento que existe na LDP atual.
          </p>
        </div>
        <Button variant="outline" size="sm" className="gap-2" disabled={!!ocupado} onClick={() => setNavegando(true)}>
          <Folder className="h-4 w-4" />{destino ? "Trocar pasta de destino" : "Escolher pasta de destino"}
        </Button>
        <input ref={input} type="file" multiple className="hidden" aria-label="Arquivos" onChange={(e) => { escolherArquivos(e.target.files); e.target.value = ""; }} />
        <Button variant="outline" size="sm" className="gap-2" disabled={!!ocupado || !destino} onClick={() => input.current?.click()}>
          <FileUp className="h-4 w-4" />Escolher arquivos
        </Button>
        <Button size="sm" className="gap-2" disabled={!!ocupado || permitidos.length === 0 || !!envio} onClick={() => void enviar()}>
          {ocupado === "enviando" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}Enviar {permitidos.length} arquivo(s)
        </Button>
      </section>

      {destino && (
        <p className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">Destino:</span><strong title={destino.caminho}>{destino.caminho}</strong>
          {protecao && (protecao.protegida
            ? <span className="flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-bold text-primary"><FolderLock className="h-3.5 w-3.5" />
                Protegida pela LDP (raiz: {protecao.raiz?.caminho.split("/").pop()})</span>
            : <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-bold text-muted-foreground">Fora das pastas protegidas — regra da LDP não se aplica</span>)}
        </p>
      )}
      {erro && <p className="flex items-center gap-2 text-sm text-destructive"><AlertTriangle className="h-4 w-4" />{erro}</p>}
      {ocupado === "validando" && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Validando na LDP…</p>}

      {validacoes.length > 0 && (
        <div className="overflow-x-auto rounded-xl border border-border bg-card/60">
          <table className="w-full text-xs">
            <thead className="bg-secondary text-left text-muted-foreground">
              <tr>{["Arquivo", "Documento identificado", "LDP", "Pasta", "Revisão", "Resultado"].map((h) => <th key={h} className="px-3 py-2 font-semibold">{h}</th>)}</tr>
            </thead>
            <tbody>
              {validacoes.map((v, i) => {
                const env = resultadoDe(v.arquivo);
                const ok = permiteEnvio(v.status);
                return (
                  <tr key={`${i}-${v.arquivo}`} className="border-t border-border align-top">
                    <td className="px-3 py-2 font-medium">{v.arquivo}</td>
                    <td className="px-3 py-2">{v.documento ?? "—"}</td>
                    <td className="px-3 py-2">{v.documento ? `Encontrado (${v.campoIdentificador})` : v.status === "PASTA_NAO_PROTEGIDA" ? "Não verificada" : "—"}</td>
                    <td className="px-3 py-2">{v.pasta ?? "—"}</td>
                    <td className="px-3 py-2">{v.revisao ?? "—"}</td>
                    <td className="px-3 py-2">
                      <span className={cn("flex items-center gap-1 font-bold", ok ? "text-emerald-600 dark:text-emerald-400" : "text-destructive")}>
                        {ok ? <CheckCircle2 className="h-3.5 w-3.5" /> : <XCircle className="h-3.5 w-3.5" />}{ROTULO_STATUS[v.status].toUpperCase()}
                      </span>
                      {!ok && <span className="block text-[11px] text-muted-foreground">{v.mensagem}</span>}
                      {env && <span className={cn("block text-[11px] font-bold", env.resultado === "ENVIADO" ? "text-emerald-600 dark:text-emerald-400" : "text-destructive")}>
                        {env.resultado} · {env.motivo}</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {navegando && (
        <NavegadorPastas hubId={projetoAtual.hubId} projectId={projetoAtual.projectId} titulo="Pasta de destino" acao="Enviar para esta pasta"
          onCancelar={() => setNavegando(false)}
          onEscolher={(folderId, caminho) => {
            const d = { folderId, caminho };
            setDestino(d); setNavegando(false);
            if (arquivos.length > 0) void validar(d, arquivos);
          }} />
      )}
    </div>
  );
}
