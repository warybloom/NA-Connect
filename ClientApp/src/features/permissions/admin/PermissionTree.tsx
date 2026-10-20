// Árvore hierárquica de permissões. Cada grupo: expansível, contador "marcadas / total", checkbox do pai (marca/desmarca
// todos os filhos) com estado indeterminado. Filhos continuam configuráveis um a um. A seleção fica fora da árvore,
// então a busca filtra o que é exibido sem perder o que já foi marcado.
import { useEffect, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import { alternar, contagem, estadoDoNo, type TreeNode } from "./permissionTreeLogic";

const TIPO: Record<string, string> = {
  MODULE: "Módulo", PAGE: "Página", TAB: "Aba", REPORT: "Relatório", DASHBOARD: "Dashboard", SETTING: "Configuração", ACTION: "Ação",
};

function Checkbox({ estado, disabled, onChange, label }: { estado: "todos" | "alguns" | "nenhum"; disabled?: boolean; onChange: (v: boolean) => void; label: string }) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => { if (ref.current) ref.current.indeterminate = estado === "alguns"; }, [estado]);
  return (
    <input ref={ref} type="checkbox" aria-label={label} className="h-4 w-4 shrink-0 cursor-pointer accent-primary disabled:cursor-not-allowed"
      checked={estado === "todos"} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
  );
}

function Contador({ marcadas, total }: { marcadas: number; total: number }) {
  return (
    <span className={cn("ml-auto shrink-0 rounded-full px-2 py-0.5 text-[11px] font-semibold tabular-nums",
      marcadas === 0 ? "bg-muted text-muted-foreground" : marcadas === total ? "bg-primary/15 text-primary" : "bg-amber-500/15 text-amber-500")}>
      {marcadas} / {total}
    </span>
  );
}

interface NoProps {
  node: TreeNode; selecionadas: ReadonlySet<string>; onChange: (s: Set<string>) => void; readOnly?: boolean; depth: number; forcarAberto: boolean;
}

function No({ node, selecionadas, onChange, readOnly, depth, forcarAberto }: NoProps) {
  const [aberto, setAberto] = useState(depth === 0);
  useEffect(() => { if (forcarAberto) setAberto(true); }, [forcarAberto]);
  const r = node.resource;
  const grupo = node.children.length > 0;
  const estado = estadoDoNo(selecionadas, node);
  const marcarSo = (v: boolean) => {
    const n = new Set(selecionadas);
    if (v) n.add(r.key); else n.delete(r.key);
    onChange(n);
  };

  const linha = (
    <div className={cn("group flex items-center gap-2.5 rounded-md py-1.5 pr-2 transition-colors hover:bg-muted/40", depth === 0 && "py-2")}
      style={{ paddingLeft: depth === 0 ? 8 : depth * 20 + 8 }}>
      {grupo ? (
        <button type="button" onClick={() => setAberto((a) => !a)} aria-label={aberto ? "Recolher" : "Expandir"}
          className="flex h-5 w-5 items-center justify-center rounded text-muted-foreground hover:bg-muted hover:text-foreground">
          <ChevronRight className={cn("h-4 w-4 transition-transform", aberto && "rotate-90")} />
        </button>
      ) : <span className="w-5" />}
      <Checkbox estado={estado} disabled={readOnly} label={r.label} onChange={(v) => onChange(alternar(selecionadas, node, v))} />
      <button type="button" disabled={!grupo} onClick={() => setAberto((a) => !a)}
        className={cn("min-w-0 truncate text-left", depth === 0 ? "text-xs font-black uppercase tracking-widest text-foreground" : "text-sm text-foreground",
          !grupo && "cursor-default")}>
        {r.label}
      </button>
      {r.scope === "PROJECT" && <span className="shrink-0 rounded bg-primary/10 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary">por projeto</span>}
      <span className="hidden shrink-0 text-[10px] uppercase tracking-wide text-muted-foreground/70 group-hover:inline">{TIPO[r.type] ?? r.type} · {r.key}</span>
      {grupo && <Contador {...contagem(selecionadas, node)} />}
    </div>
  );

  if (!grupo) return <li>{linha}</li>;
  return (
    <li className={cn(depth === 0 && "rounded-lg border border-border bg-background/40")}>
      {linha}
      {aberto && (
        <ul className={cn("pb-1", depth === 0 && "border-t border-border/60 pt-1")}>
          {/* O próprio nó do grupo também é uma permissão (ex.: "acesso ao módulo"): aparece como primeiro item. */}
          <li>
            <div className="flex items-center gap-2.5 rounded-md py-1.5 pr-2 hover:bg-muted/40" style={{ paddingLeft: (depth + 1) * 20 + 8 }}>
              <span className="w-5" />
              <input type="checkbox" className="h-4 w-4 shrink-0 cursor-pointer accent-primary disabled:cursor-not-allowed" aria-label={`${r.label} — acesso`}
                checked={selecionadas.has(r.key)} disabled={readOnly} onChange={(e) => marcarSo(e.target.checked)} />
              <span className="text-sm text-muted-foreground">{depth === 0 ? "Acesso ao módulo" : "Acesso"}</span>
            </div>
          </li>
          {node.children.map((c) => (
            <No key={c.resource.key} node={c} selecionadas={selecionadas} onChange={onChange} readOnly={readOnly} depth={depth + 1} forcarAberto={forcarAberto} />
          ))}
        </ul>
      )}
    </li>
  );
}

export default function PermissionTree({ nodes, selecionadas, onChange, readOnly, expandirTudo = false }: {
  nodes: TreeNode[]; selecionadas: ReadonlySet<string>; onChange: (s: Set<string>) => void; readOnly?: boolean; expandirTudo?: boolean;
}) {
  if (nodes.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">Nenhuma permissão encontrada.</p>;
  // Módulos/grupos em cartões próprios; permissões de topo sem filhos (Início, 4D, ações de sistema…) num cartão "Geral".
  const grupos = nodes.filter((n) => n.children.length > 0);
  const soltas = nodes.filter((n) => n.children.length === 0);
  const marcadasSoltas = soltas.filter((n) => selecionadas.has(n.resource.key)).length;
  return (
    <ul className="space-y-2">
      {soltas.length > 0 && (
        <li className="rounded-lg border border-border bg-background/40">
          <div className="flex items-center gap-2.5 px-2 py-2">
            <span className="w-5" />
            <span className="text-xs font-black uppercase tracking-widest text-foreground">Geral</span>
            <Contador marcadas={marcadasSoltas} total={soltas.length} />
          </div>
          <ul className="border-t border-border/60 pb-1 pt-1">
            {soltas.map((n) => (
              <No key={n.resource.key} node={n} selecionadas={selecionadas} onChange={onChange} readOnly={readOnly} depth={1} forcarAberto={expandirTudo} />
            ))}
          </ul>
        </li>
      )}
      {grupos.map((n) => (
        <No key={n.resource.key} node={n} selecionadas={selecionadas} onChange={onChange} readOnly={readOnly} depth={0} forcarAberto={expandirTudo} />
      ))}
    </ul>
  );
}
