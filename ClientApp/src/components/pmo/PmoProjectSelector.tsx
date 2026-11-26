import { useEffect, useMemo, useRef, useState } from "react";
import { Search, ChevronDown } from "lucide-react";
import type { AccItem } from "@/lib/api";

const STORAGE_KEY = "na-connect:pmo:selected-projects:v1";

/**
 * Persiste só os IDs selecionados no localStorage, restaura automaticamente quando a lista de projetos
 * acessíveis (do hub atual) chega, e SEMPRE intersecta com os projetos atualmente acessíveis — nunca
 * confia no localStorage como autorização (cada endpoint continua validando IProjectReportAccess por
 * request; isto é só conveniência de UI).
 */
export function usePmoProjectSelection(projects: AccItem[]) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const restored = useRef(false);

  useEffect(() => {
    if (restored.current || !projects.length) return;
    restored.current = true;
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return;
      const ids: unknown = JSON.parse(raw);
      if (!Array.isArray(ids)) return;
      const valid = new Set(projects.map(p => p.id));
      const intersecao = new Set(ids.filter((id): id is string => typeof id === 'string' && valid.has(id)));
      if (intersecao.size) setSelected(intersecao);
    } catch {
      // localStorage indisponível/corrompido — segue sem restaurar, nunca quebra a tela.
    }
  }, [projects]);

  useEffect(() => {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify([...selected])); } catch { /* ignore */ }
  }, [selected]);

  return [selected, setSelected] as const;
}

export default function PmoProjectSelector({ projects, selected, onChange }: {
  projects: AccItem[]; selected: Set<string>; onChange: (next: Set<string>) => void;
}) {
  const [open, setOpen] = useState(false);
  const [busca, setBusca] = useState('');
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onClickFora(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onClickFora);
    return () => document.removeEventListener('mousedown', onClickFora);
  }, []);

  const ordenados = useMemo(() => [...projects].sort((a, b) => a.attributes.name.localeCompare(b.attributes.name, 'pt-BR')), [projects]);
  const filtrados = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    return termo ? ordenados.filter(p => p.attributes.name.toLowerCase().includes(termo)) : ordenados;
  }, [ordenados, busca]);

  const todosSelecionados = projects.length > 0 && selected.size === projects.length;
  const toggle = (id: string) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id); else next.add(id);
    onChange(next);
  };
  const toggleTodos = () => onChange(todosSelecionados ? new Set() : new Set(projects.map(p => p.id)));

  const label = !projects.length ? 'Selecione uma conta'
    : selected.size === 0 ? 'Nenhum projeto selecionado'
    : todosSelecionados ? 'Todos os projetos acessíveis'
    : selected.size === 1 ? (projects.find(p => selected.has(p.id))?.attributes.name ?? '1 projeto selecionado')
    : `${selected.size} projetos selecionados`;

  return (
    <div className="relative" ref={ref}>
      <button type="button" onClick={() => setOpen(o => !o)} disabled={!projects.length}
        aria-haspopup="listbox" aria-expanded={open}
        className="flex items-center gap-2 bg-slate-800/80 border border-slate-600 rounded-lg px-3 py-2 text-sm font-semibold text-slate-100 disabled:opacity-50 w-full md:w-64 justify-between shadow-sm">
        <span className="truncate">{label}</span>
        <ChevronDown className="w-4 h-4 text-slate-400 shrink-0" />
      </button>

      {open && (
        <div className="absolute z-20 mt-1 w-80 max-h-96 overflow-auto bg-slate-800 border border-slate-600 rounded-lg shadow-xl p-2" role="listbox" aria-label="Projetos">
          <div className="flex items-center gap-2 bg-slate-900 border border-slate-700 rounded px-2 py-1.5 mb-2">
            <Search className="w-3.5 h-3.5 text-slate-500 shrink-0" />
            <input value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar projeto..."
              className="bg-transparent text-sm text-slate-100 outline-none w-full" />
          </div>

          <label className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-700 cursor-pointer border-b border-slate-700 mb-1">
            <input type="checkbox" checked={todosSelecionados} onChange={toggleTodos} className="accent-sky-500" />
            <span className="text-sm font-semibold text-amber-400">Todos os projetos acessíveis</span>
          </label>

          {filtrados.map(p => (
            <label key={p.id} className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-700 cursor-pointer">
              <input type="checkbox" checked={selected.has(p.id)} onChange={() => toggle(p.id)} className="accent-sky-500" />
              <span className="text-sm text-slate-200 truncate">{p.attributes.name}</span>
            </label>
          ))}
          {!filtrados.length && <p className="text-xs text-slate-500 px-2 py-2">Nenhum projeto encontrado.</p>}
        </div>
      )}
    </div>
  );
}
