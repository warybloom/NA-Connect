// Multiselect PESQUISÁVEL de projetos da Configuração de Telas para TV (substitui a lista aberta de checkboxes).
// Fechado: chips dos selecionados (até 3; depois "+N"). Aberto: busca por código ou nome + lista com marcação.
import { useMemo, useState } from "react";
import { Check, ChevronsUpDown, Search, X } from "lucide-react";
import type { AccItem } from "@/lib/api";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";
import { pesquisarProjetos, resumoSelecao } from "./tvLogic";
import { separarCodigo } from "@/features/project/ProjectIdentity";

const MAX_CHIPS = 3;

export default function SeletorProjetosTv({ opcoes, selecionados, onChange, carregando }:
  { opcoes: readonly AccItem[]; selecionados: string[]; onChange: (ids: string[]) => void; carregando?: boolean }) {
  const [aberto, setAberto] = useState(false);
  const [busca, setBusca] = useState("");
  const itens = useMemo(() => opcoes.map((p) => ({ id: p.id, nome: p.attributes.name })), [opcoes]);
  const filtrados = useMemo(() => pesquisarProjetos(itens, busca), [itens, busca]);
  const nomeDe = (id: string) => itens.find((p) => p.id === id)?.nome ?? id;
  const codigo = (id: string) => separarCodigo(nomeDe(id)).codigo ?? nomeDe(id);
  const alternar = (id: string) => onChange(selecionados.includes(id) ? selecionados.filter((x) => x !== id) : [...selecionados, id]);
  const visiveis = selecionados.slice(0, MAX_CHIPS);
  const resto = selecionados.length - visiveis.length;

  return (
    <Popover open={aberto} onOpenChange={(v) => { setAberto(v); if (!v) setBusca(""); }}>
      <PopoverTrigger asChild>
        <button type="button" aria-label="Projetos" disabled={carregando}
          title={selecionados.map(nomeDe).join("\n") || undefined}
          className="flex min-h-9 w-full max-w-xl flex-wrap items-center gap-1 rounded-lg border border-border bg-background px-2 py-1 text-left text-sm disabled:opacity-50">
          {selecionados.length === 0 ? (
            <span className="flex flex-1 items-center gap-2 px-1 text-muted-foreground"><Search className="h-3.5 w-3.5" />
              {carregando ? "Carregando projetos..." : "Pesquisar e selecionar projetos..."}</span>
          ) : (
            <span className="flex flex-1 flex-wrap items-center gap-1">
              {visiveis.map((id) => (
                <span key={id} className="flex items-center gap-1 rounded-md bg-primary/10 px-1.5 py-0.5 text-xs font-bold text-primary">
                  {codigo(id)}
                  <span role="button" tabIndex={0} aria-label={`Remover ${codigo(id)}`} className="rounded hover:bg-primary/20"
                    onClick={(e) => { e.stopPropagation(); alternar(id); }}
                    onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); e.stopPropagation(); alternar(id); } }}>
                    <X className="h-3 w-3" />
                  </span>
                </span>
              ))}
              {resto > 0 && <span className="rounded-md bg-muted px-1.5 py-0.5 text-xs font-bold text-muted-foreground"
                title={resumoSelecao(selecionados.map((id) => ({ id, nome: nomeDe(id) })))}>+{resto}</span>}
            </span>
          )}
          <ChevronsUpDown className="h-4 w-4 flex-shrink-0 text-muted-foreground" />
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <div className="flex items-center gap-2 border-b border-border px-3">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input autoFocus value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Código ou nome do projeto"
            aria-label="Pesquisar projeto" className="h-10 flex-1 bg-transparent text-sm outline-none" />
          {selecionados.length > 0 && (
            <button type="button" className="text-xs text-muted-foreground hover:text-foreground" onClick={() => onChange([])}>Limpar</button>
          )}
        </div>
        <ul role="listbox" aria-multiselectable className="max-h-72 overflow-y-auto p-1">
          {filtrados.length === 0 && <li className="px-2 py-3 text-xs text-muted-foreground">Nenhum projeto encontrado.</li>}
          {filtrados.map((p) => {
            const marcado = selecionados.includes(p.id);
            return (
              <li key={p.id} role="option" aria-selected={marcado}>
                <button type="button" onClick={() => alternar(p.id)}
                  className={cn("flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm hover:bg-accent", marcado && "font-bold")}>
                  <span className={cn("flex h-4 w-4 flex-shrink-0 items-center justify-center rounded border", marcado ? "border-primary bg-primary text-primary-foreground" : "border-border")}>
                    {marcado && <Check className="h-3 w-3" />}
                  </span>
                  <span className="truncate">{p.nome}</span>
                </button>
              </li>
            );
          })}
        </ul>
      </PopoverContent>
    </Popover>
  );
}
