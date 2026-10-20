// Prévia e resultado das operações em lote (compartilhados por "Aplicar perfis" e "Copiar acesso").
import { AlertTriangle, CheckCircle2, MinusCircle, PencilLine, RotateCcw, UserPlus, XCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import type { BulkItem, BulkPreview, BulkResultado, BulkStatus } from "./permissionsAdminApi";

const STATUS: Record<BulkStatus, { rotulo: string; classe: string; Icone: typeof CheckCircle2 }> = {
  ALTERAR: { rotulo: "Alterar", classe: "bg-primary/15 text-primary", Icone: PencilLine },
  JA_POSSUI: { rotulo: "Já possui", classe: "bg-emerald-500/15 text-emerald-500", Icone: CheckCircle2 },
  IGNORAR: { rotulo: "Ignorar", classe: "bg-muted text-muted-foreground", Icone: MinusCircle },
};

/** Rótulo do item: o vínculo interno (UserProjects) é decidido pelo backend (CRIAR/REATIVAR); sem ele, só perfis. */
export function rotuloItem(i: Pick<BulkItem, "status" | "vinculo" | "perfisAdicionados" | "perfisRemovidos">) {
  if (i.status !== "ALTERAR") return STATUS[i.status];
  if (i.vinculo === "CRIAR") return { rotulo: "Adicionar", classe: "bg-primary/15 text-primary", Icone: UserPlus };
  if (i.vinculo === "REATIVAR") return { rotulo: "Reativar", classe: "bg-sky-500/15 text-sky-500", Icone: RotateCcw };
  if (i.perfisAdicionados.length > 0 && i.perfisRemovidos.length === 0) return { rotulo: "Adicionar perfil", classe: "bg-primary/15 text-primary", Icone: PencilLine };
  return STATUS.ALTERAR;
}

function Numero({ rotulo, valor, destaque }: { rotulo: string; valor: number; destaque?: string }) {
  return (
    <div className="rounded-lg border border-border bg-background/40 px-3 py-2">
      <div className={cn("text-xl font-black tabular-nums", destaque)}>{valor}</div>
      <div className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">{rotulo}</div>
    </div>
  );
}

function Mudancas({ i, copiaOverrides }: { i: BulkItem; copiaOverrides: boolean }) {
  if (i.status === "IGNORAR") return <span className="text-xs text-muted-foreground">{i.motivo}</span>;
  if (i.status === "JA_POSSUI") return <span className="text-xs text-muted-foreground">Nada a alterar</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {i.vinculo === "CRIAR" && <span className="rounded-full bg-primary/15 px-2 py-0.5 text-[11px] font-semibold text-primary">+ vínculo no projeto</span>}
      {i.vinculo === "REATIVAR" && <span className="rounded-full bg-sky-500/15 px-2 py-0.5 text-[11px] font-semibold text-sky-500">vínculo reativado</span>}
      {i.perfisAdicionados.map((p) => <span key={`+${p}`} className="rounded-full bg-emerald-500/15 px-2 py-0.5 text-[11px] font-semibold text-emerald-500">+ {p}</span>)}
      {i.perfisRemovidos.map((p) => <span key={`-${p}`} className="rounded-full bg-rose-500/15 px-2 py-0.5 text-[11px] font-semibold text-rose-400">− {p}</span>)}
      {copiaOverrides && i.overridesGravados > 0 && <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-[11px] font-semibold text-amber-500">{i.overridesGravados} exceç{i.overridesGravados === 1 ? "ão" : "ões"} gravada{i.overridesGravados === 1 ? "" : "s"}</span>}
      {copiaOverrides && i.overridesRemovidos > 0 && <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-[11px] font-semibold text-rose-400">{i.overridesRemovidos} exceç{i.overridesRemovidos === 1 ? "ão" : "ões"} removida{i.overridesRemovidos === 1 ? "" : "s"}</span>}
    </span>
  );
}

export function BulkPreviewView({ p }: { p: BulkPreview }) {
  const porUsuario = new Map<number, BulkItem[]>();
  for (const i of p.itens) porUsuario.set(i.userId, [...(porUsuario.get(i.userId) ?? []), i]);
  const modo = p.modo === "ADD" ? "adicionado" : "definido (substituição)";
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
        <Numero rotulo="Usuários" valor={p.usuarios} />
        <Numero rotulo="Projetos" valor={p.projetos} />
        <Numero rotulo="Alterar" valor={p.alterar} destaque="text-primary" />
        <Numero rotulo="Já corretos" valor={p.jaPossui} destaque="text-emerald-500" />
        <Numero rotulo="Ignorados" valor={p.ignoradas} destaque="text-muted-foreground" />
      </div>
      {p.copiaPerfis && (
        <p className="text-sm">
          Perfis a serem {modo}: {p.perfis.length ? p.perfis.map((n) => <strong key={n} className="mr-1 rounded bg-muted px-1.5 py-0.5 text-xs">{n}</strong>) : <em className="text-muted-foreground">nenhum</em>}
        </p>
      )}
      {p.avisos.map((a) => (
        <p key={a} className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-500">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />{a}
        </p>
      ))}
      <div className="max-h-[42vh] space-y-2 overflow-y-auto pr-1 custom-scrollbar">
        {[...porUsuario.values()].map((itens) => (
          <div key={itens[0].userId} className="rounded-lg border border-border">
            <div className="border-b border-border bg-muted/30 px-3 py-1.5 text-sm font-semibold">
              {itens[0].usuario} <span className="text-xs font-normal text-muted-foreground">{itens[0].email}</span>
            </div>
            <table className="w-full text-sm">
              <tbody>
                {itens.map((i) => {
                  const s = rotuloItem(i);
                  return (
                    <tr key={i.projetoId} className="border-t border-border/40 first:border-t-0">
                      <td className="w-[34%] px-3 py-1.5">{i.projeto}</td>
                      <td className="w-[120px]">
                        <span className={cn("inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-black uppercase tracking-wide", s.classe)}>
                          <s.Icone className="h-3 w-3" />{s.rotulo}
                        </span>
                      </td>
                      <td className="py-1.5 pr-3"><Mudancas i={i} copiaOverrides={p.copiaOverrides} /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ))}
      </div>
    </div>
  );
}

export function BulkResultadoView({ r }: { r: BulkResultado }) {
  return (
    <div className="space-y-4">
      <div className={cn("flex items-center gap-3 rounded-lg border px-4 py-3",
        r.sucesso ? "border-emerald-500/40 bg-emerald-500/10" : "border-rose-500/40 bg-rose-500/10")}>
        {r.sucesso ? <CheckCircle2 className="h-6 w-6 text-emerald-500" /> : <XCircle className="h-6 w-6 text-rose-400" />}
        <div>
          <div className="text-sm font-black uppercase tracking-widest">{r.sucesso ? "Operação concluída" : "Operação não aplicada"}</div>
          {r.erro && <div className="text-xs text-rose-400">{r.erro}</div>}
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Numero rotulo="Usuários processados" valor={r.plano.usuarios} />
        <Numero rotulo="Projetos processados" valor={r.plano.projetos} />
        <Numero rotulo="Vínculos criados" valor={r.vinculosCriados ?? 0} destaque="text-primary" />
        <Numero rotulo="Vínculos reativados" valor={r.vinculosReativados ?? 0} destaque="text-sky-500" />
        <Numero rotulo="Perfis adicionados" valor={r.perfisAdicionados ?? 0} destaque="text-primary" />
        <Numero rotulo="Já estavam corretos" valor={r.jaCorretos} destaque="text-emerald-500" />
        <Numero rotulo="Ignorados" valor={r.ignorados} destaque="text-muted-foreground" />
        <Numero rotulo="Falhas" valor={r.falhas} destaque={r.falhas ? "text-rose-400" : undefined} />
      </div>
    </div>
  );
}
