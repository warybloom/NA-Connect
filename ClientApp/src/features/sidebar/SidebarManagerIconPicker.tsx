import { useMemo, useState } from "react";
import * as Popover from "@radix-ui/react-popover";
import { Check, Search, X } from "lucide-react";
import { SIDEBAR_ICONS } from "./icons";

const SEARCH_ALIASES: Record<string, string> = {
  FolderOpen: "pasta abrir", FolderCog: "pasta configuracao", FolderKanban: "pasta quadro",
  LayoutDashboard: "painel dashboard", BarChart3: "grafico barras", TrendingUp: "tendencia crescimento",
  Settings: "configuracoes engrenagem", Users: "usuarios pessoas", UserRound: "usuario pessoa",
  CalendarDays: "calendario dias", FileText: "arquivo documento texto", FileSpreadsheet: "planilha excel",
  ClipboardList: "lista tarefas", ShieldCheck: "seguranca permissao", Database: "banco dados",
};

interface Props {
  value: string;
  itemLabel: string;
  disabled?: boolean;
  onChange: (icon: string) => void;
}

export function SidebarManagerIconPicker({ value, itemLabel, disabled, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const CurrentIcon = SIDEBAR_ICONS[value];
  const icons = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase("pt-BR");
    return Object.keys(SIDEBAR_ICONS).filter(name => `${name} ${SEARCH_ALIASES[name] ?? ""}`.toLocaleLowerCase("pt-BR").includes(normalized));
  }, [query]);

  return <Popover.Root open={open} onOpenChange={next => { setOpen(next); if (!next) setQuery(""); }}>
    <Popover.Trigger asChild>
      <button type="button" disabled={disabled} aria-label={`Escolher ícone de ${itemLabel}. Atual: ${value}`}
        className="inline-flex h-8 max-w-44 items-center gap-2 rounded-full border border-border/70 bg-background px-2.5 text-xs font-medium shadow-sm transition-colors hover:border-primary/40 hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50">
        <CurrentIcon className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <span className="truncate">{value}</span>
      </button>
    </Popover.Trigger>
    <Popover.Portal>
      <Popover.Content sideOffset={8} collisionPadding={12} onEscapeKeyDown={() => setOpen(false)}
        className="z-50 w-[min(92vw,340px)] rounded-xl border bg-popover p-3 text-popover-foreground shadow-xl outline-none">
        <div className="mb-2 flex items-center justify-between gap-2">
          <div><p className="text-sm font-semibold">Escolher ícone</p><p className="text-xs text-muted-foreground">{icons.length} de {Object.keys(SIDEBAR_ICONS).length}</p></div>
          <Popover.Close aria-label="Fechar seletor de ícones" className="rounded-full p-1.5 hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring"><X className="h-4 w-4" /></Popover.Close>
        </div>
        <label className="flex items-center gap-2 rounded-lg border bg-background px-2.5 focus-within:ring-2 focus-within:ring-ring">
          <Search className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
          <input autoFocus value={query} onChange={event => setQuery(event.target.value)} placeholder="Buscar ícone por nome"
            className="min-w-0 flex-1 bg-transparent py-2 text-sm outline-none" aria-label="Buscar ícone" />
        </label>
        {icons.length ? <div role="listbox" aria-label="Ícones disponíveis" className="mt-3 grid max-h-64 grid-cols-6 gap-1.5 overflow-y-auto overflow-x-hidden pr-1">
          {icons.map(name => { const Icon = SIDEBAR_ICONS[name]; const selected = name === value; return <button key={name} type="button" role="option" aria-selected={selected}
            aria-label={name} title={name} onClick={() => { onChange(name); setOpen(false); }}
            className={`group relative flex h-11 w-11 items-center justify-center rounded-lg border transition-all focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${selected ? "border-primary bg-primary/10 text-primary" : "border-transparent bg-muted/40 hover:border-border hover:bg-muted"}`}>
            <Icon className="h-5 w-5" aria-hidden="true" />
            {selected && <Check className="absolute right-0.5 top-0.5 h-3 w-3" aria-hidden="true" />}
          </button>; })}
        </div> : <p role="status" className="mt-3 rounded-lg bg-muted/50 px-3 py-6 text-center text-sm text-muted-foreground">Nenhum ícone encontrado para “{query}”.</p>}
        <Popover.Arrow className="fill-border" />
      </Popover.Content>
    </Popover.Portal>
  </Popover.Root>;
}
