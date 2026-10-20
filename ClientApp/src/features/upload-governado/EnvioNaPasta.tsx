// ENVIO DIRETO NA PASTA (FilesPage): o usuário já está na pasta — arrasta (ou escolhe) os arquivos, o N&A Connect PRÉ-VALIDA
// todos na LDP (nada sobe antes) e os VÁLIDOS sobem AUTOMATICAMENTE (sem segundo clique), um por vez, com progresso por arquivo;
// os inválidos não sobem e a linha mostra o motivo. Ao fim, a pasta é atualizada.
// Mesma regra e mesmos endpoints do envio governado (/api/upload-governado/validar e /enviar → UploadGovernadoRules,
// RaizQueContemAsync, LDP atual, UploadGovernadoLog). O servidor revalida cada arquivo no envio.
import { useRef, useState, type ReactNode } from "react";
import { AlertTriangle, CheckCircle2, FolderLock, Loader2, Upload, X, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { enviarArquivoComProgresso, validarArquivos, type PastaProtegida } from "./uploadGovernadoApi";
import { aplicarValidacao, contagem, ESTADOS_CONCLUIDOS, MENSAGEM_BLOQUEIO, prontosDoLote, type ArquivoNaFila } from "./uploadNaPasta";

interface Props {
  hubId: string;
  projectId: string;
  nomeProjeto: string;
  folderId: string;
  /** Caminho legível da pasta atual (breadcrumb). */
  caminho: string;
  /** Sem cde.docs.upload: a pasta continua navegável, só não aceita envio. */
  podeEnviar: boolean;
  /** Pasta numa árvore protegida pela LDP (consultada ao abrir a pasta; null = consultando). */
  protegida?: boolean | null;
  /** Chamado ao fim do envio para atualizar o conteúdo da pasta. */
  onEnviados: () => void;
  children: ReactNode;
}

export default function EnvioNaPasta({ hubId, projectId, nomeProjeto, folderId, caminho, podeEnviar, protegida, onEnviados, children }: Props) {
  const input = useRef<HTMLInputElement>(null);
  const [fila, setFila] = useState<ArquivoNaFila[]>([]);
  const [protecao, setProtecao] = useState<{ protegida: boolean; raiz: PastaProtegida | null } | null>(null);
  const [arrastando, setArrastando] = useState(false);
  const [enviando, setEnviando] = useState(false);
  // Fila de envio entre lotes: soltar mais arquivos durante um envio só acrescenta à fila (um envio por vez contra o ACC).
  const aEnviar = useRef<ArquivoNaFila[]>([]);
  const processando = useRef(false);
  const c = contagem(fila);
  // Proteção consultada ao abrir a pasta (prop) ou confirmada pela última pré-validação.
  const pastaProtegida = protecao?.protegida ?? protegida === true;
  const destino = { folderId, caminho };
  const atualizar = (chave: string, p: Partial<ArquivoNaFila>) => setFila((f) => f.map((a) => (a.chave === chave ? { ...a, ...p } : a)));

  // SOLTOU → valida todos na hora → os válidos entram direto na fila de envio.
  const adicionar = async (lista: FileList | File[] | null) => {
    const arquivos = Array.from(lista ?? []).filter((f) => f.name);
    if (!podeEnviar || arquivos.length === 0) return;
    const novos: ArquivoNaFila[] = arquivos.map((arquivo, i) => ({ chave: `${Date.now()}-${i}-${arquivo.name}`, arquivo, estado: "VALIDANDO" }));
    setFila((f) => [...f, ...novos]);
    let resultados;
    try {
      // PRÉ-VALIDAÇÃO (só nomes): nenhum storage/item/versão é criado no ACC aqui.
      const r = await validarArquivos(hubId, projectId, destino, novos.map((n) => n.arquivo.name));
      setProtecao({ protegida: r.protegida, raiz: r.raiz });
      resultados = r.resultados;
    } catch (e) {
      const mensagem = e instanceof Error ? e.message : "Não foi possível validar os arquivos.";
      setFila((f) => f.map((a) => (novos.some((n) => n.chave === a.chave) ? { ...a, estado: "ERRO", mensagem } : a)));
      return;
    }
    setFila((f) => aplicarValidacao(f, novos.map((n) => n.chave), resultados));
    aEnviar.current.push(...prontosDoLote(novos, resultados));
    void processarFila();
  };

  // Um arquivo por vez (sem paralelismo contra o ACC); falha num arquivo não interrompe os demais. O servidor revalida cada um.
  const processarFila = async () => {
    if (processando.current) return;
    processando.current = true;
    setEnviando(true);
    let algumEnviado = false;
    try {
      for (let item = aEnviar.current.shift(); item; item = aEnviar.current.shift()) {
        const chave = item.chave;
        atualizar(chave, { estado: "ENVIANDO", progresso: 0 });
        try {
          const r = await enviarArquivoComProgresso(hubId, projectId, destino, item.arquivo, (pct) => atualizar(chave, { progresso: pct }));
          if (r.resultado === "ENVIADO") { algumEnviado = true; atualizar(chave, { estado: "ENVIADO", progresso: 100, mensagem: r.motivo }); }
          else atualizar(chave, { estado: r.resultado === "BLOQUEADO" ? "BLOQUEADO" : "ERRO", validacao: r.validacao,
            mensagem: MENSAGEM_BLOQUEIO[r.validacao.status]?.texto ?? r.motivo });
        } catch (e) {
          atualizar(chave, { estado: "ERRO", mensagem: e instanceof Error ? e.message : "Falha no envio." });
        }
      }
    } finally {
      processando.current = false;
      setEnviando(false);
      if (algumEnviado) onEnviados();
    }
  };

  const remover = (chave: string) => setFila((f) => f.filter((a) => a.chave !== chave));
  const foraDaLdp = fila.filter((a) => a.estado === "BLOQUEADO" && a.validacao?.status === "FORA_DA_LDP");

  return (
    <div
      className="relative"
      onDragOver={(e) => { if (!podeEnviar || !e.dataTransfer.types.includes("Files")) return; e.preventDefault(); setArrastando(true); }}
      onDragLeave={(e) => { if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setArrastando(false); }}
      onDrop={(e) => { if (!podeEnviar) return; e.preventDefault(); setArrastando(false); void adicionar(e.dataTransfer.files); }}
    >
      {/* Linha enxuta (sem card/texto explicativo): indicador laranja da pasta protegida — já ao abrir a pasta — e o botão de envio.
          Arrastar arquivos sobre a área da pasta continua funcionando. */}
      {(podeEnviar || pastaProtegida) && (
        <div className="mb-3 flex flex-wrap items-center justify-end gap-2">
          {pastaProtegida && <span className="mr-auto flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-bold text-primary"><FolderLock className="h-3.5 w-3.5" />Pasta protegida pela LDP</span>}
          {podeEnviar && <>
            <input ref={input} type="file" multiple className="hidden" aria-label="Arquivos para enviar" onChange={(e) => { void adicionar(e.target.files); e.target.value = ""; }} />
            <Button size="sm" variant="outline" className="gap-2" onClick={() => input.current?.click()}><Upload className="h-4 w-4" />Enviar arquivos</Button>
          </>}
        </div>
      )}

      {fila.length > 0 && (
        <section className="mb-4 space-y-3 rounded-xl border border-border bg-card/60 p-4" aria-label="Arquivos para envio">
          <div className="flex flex-wrap items-center gap-3">
            <h3 className="text-xs font-black uppercase tracking-widest">Envio para {caminho || "esta pasta"}</h3>
            <span className="text-xs text-muted-foreground">
              {c.validando > 0 && `${c.validando} validando · `}{c.prontos + c.enviando > 0 && `${c.prontos + c.enviando} na fila de envio · `}{c.bloqueados} bloqueado(s){c.enviados > 0 && ` · ${c.enviados} enviado(s)`}{c.erros > 0 && ` · ${c.erros} com erro`}
            </span>
            {enviando && <span className="flex items-center gap-1 text-xs font-bold text-primary"><Loader2 className="h-3.5 w-3.5 animate-spin" />Enviando...</span>}
          </div>

          {foraDaLdp.length > 0 && (
            <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
              <p className="flex items-center gap-2 font-black uppercase tracking-wide text-destructive"><AlertTriangle className="h-4 w-4" />{MENSAGEM_BLOQUEIO.FORA_DA_LDP!.titulo}</p>
              <p className="mt-1">{MENSAGEM_BLOQUEIO.FORA_DA_LDP!.texto}</p>
              <dl className="mt-2 grid gap-x-4 gap-y-0.5 text-xs sm:grid-cols-[auto_1fr]">
                <dt className="font-bold text-muted-foreground">Arquivo</dt><dd>{foraDaLdp.map((a) => a.arquivo.name).join(", ")}</dd>
                <dt className="font-bold text-muted-foreground">Projeto</dt><dd>{nomeProjeto}</dd>
                <dt className="font-bold text-muted-foreground">Pasta</dt><dd>{caminho}</dd>
                <dt className="font-bold text-muted-foreground">Status</dt><dd className="font-bold text-destructive">FORA DA LDP</dd>
              </dl>
            </div>
          )}

          <ul className="divide-y divide-border rounded-lg border border-border">
            {fila.map((a) => (
              <li key={a.chave} className="flex flex-wrap items-center gap-3 px-3 py-2 text-sm">
                <span className="min-w-[12rem] flex-1">
                  <span className="block truncate font-medium" title={a.arquivo.name}>{a.arquivo.name}</span>
                  {a.validacao?.documento && <span className="block text-[11px] text-muted-foreground">Documento {a.validacao.documento} ({a.validacao.campoIdentificador})</span>}
                  {a.mensagem && a.estado !== "ENVIADO" && <span className="block text-[11px] text-muted-foreground">{a.mensagem}</span>}
                </span>
                {a.estado === "ENVIANDO" && (
                  <span className="flex w-40 items-center gap-2">
                    <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted"><span className="block h-full bg-primary transition-all" style={{ width: `${a.progresso ?? 0}%` }} /></span>
                    <span className="w-9 text-right text-xs tabular-nums">{a.progresso ?? 0}%</span>
                  </span>
                )}
                <Situacao item={a} />
                {/* Fechar só a mensagem de um arquivo já concluído (enviado, bloqueado ou com erro). */}
                {ESTADOS_CONCLUIDOS.includes(a.estado) && (
                  <Button size="icon" variant="ghost" className="h-7 w-7" title="Fechar" aria-label={`Fechar resultado de ${a.arquivo.name}`} onClick={() => remover(a.chave)}><X className="h-3.5 w-3.5" /></Button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      {children}

      {arrastando && (
        <div className="pointer-events-none absolute inset-0 z-20 flex items-center justify-center rounded-xl border-2 border-dashed border-primary bg-primary/10 backdrop-blur-[1px]">
          <p className="rounded-lg bg-card px-4 py-2 text-sm font-bold shadow">Solte para validar e enviar para <span className="text-primary">{caminho || "esta pasta"}</span></p>
        </div>
      )}
    </div>
  );
}

function Situacao({ item }: { item: ArquivoNaFila }) {
  const base = "flex w-36 items-center justify-end gap-1 text-xs font-bold";
  switch (item.estado) {
    case "VALIDANDO": return <span className={cn(base, "text-muted-foreground")}><Loader2 className="h-3.5 w-3.5 animate-spin" />Validando na LDP</span>;
    case "PRONTO": return <span className={cn(base, "text-emerald-600 dark:text-emerald-400")}><CheckCircle2 className="h-3.5 w-3.5" />
      Validado · aguardando envio</span>;
    case "BLOQUEADO": return <span className={cn(base, "text-destructive")}><XCircle className="h-3.5 w-3.5" />
      {item.validacao?.status === "FORA_DA_LDP" ? "Fora da LDP" : item.validacao?.status === "DOCUMENTO_AMBIGUO" ? "Ambíguo" : item.validacao?.status === "NOME_INVALIDO" ? "Nome inválido" : "Bloqueado"}</span>;
    case "ENVIANDO": return <span className={cn(base, "text-primary")}><Loader2 className="h-3.5 w-3.5 animate-spin" />Enviando</span>;
    case "ENVIADO": return <span className={cn(base, "text-emerald-600 dark:text-emerald-400")} title={item.mensagem}><CheckCircle2 className="h-3.5 w-3.5" />Enviado</span>;
    case "ERRO": return <span className={cn(base, "text-destructive")}><AlertTriangle className="h-3.5 w-3.5" />Erro</span>;
  }
}
