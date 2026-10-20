// =============================================================================================
// LOGO DO PROJETO (Identidade Visual) — ponto ÚNICO do frontend (30/09/2026).
// Fonte: /api/project-identity (backend ProjectIdentityStore, App_Data/project-identity). Toda tela que mostra logo
// usa <ProjectLogo projectId=... />; nenhuma tela monta URL de imagem por conta própria. Trocar a logo na
// Configuração do Projeto chama recarregar() e a nova versão (?v=) aparece em Home, sidebar, TV, etc.
// =============================================================================================
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

interface LogoIndice { projectId: string; version: number }
interface Ctx { versoes: ReadonlyMap<string, number>; recarregar: () => void; carregado: boolean }

const IdentityContext = createContext<Ctx | null>(null);

/** URL oficial da logo (versionada para cache). Única função que conhece o endereço. */
export const urlLogoProjeto = (projectId: string, version: number) =>
  `/api/project-identity/${encodeURIComponent(projectId)}/logo?v=${version}`;

export function ProjectIdentityProvider({ children }: { children: ReactNode }) {
  const [versoes, setVersoes] = useState<ReadonlyMap<string, number>>(new Map());
  const [carregado, setCarregado] = useState(false);
  const [tentativa, setTentativa] = useState(0);

  useEffect(() => {
    const abort = new AbortController();
    fetch("/api/project-identity", { credentials: "same-origin", headers: { Accept: "application/json" }, signal: abort.signal })
      .then(async (r) => (r.ok && r.headers.get("content-type")?.includes("application/json") ? ((await r.json()) as LogoIndice[]) : []))
      .then((lista) => { if (!abort.signal.aborted) setVersoes(new Map(lista.map((i) => [i.projectId, i.version]))); })
      .catch(() => { /* sem logos: todas as telas usam o fallback */ })
      .finally(() => { if (!abort.signal.aborted) setCarregado(true); });
    return () => abort.abort();
  }, [tentativa]);

  const recarregar = useCallback(() => setTentativa((t) => t + 1), []);
  const value = useMemo(() => ({ versoes, recarregar, carregado }), [versoes, recarregar, carregado]);
  return <IdentityContext.Provider value={value}>{children}</IdentityContext.Provider>;
}

/** URL da logo do projeto, ou null (sem logo / fora do provider). */
export function useProjectLogoUrl(projectId: string | null | undefined): string | null {
  const ctx = useContext(IdentityContext);
  const v = projectId ? ctx?.versoes.get(projectId) : undefined;
  return projectId && v ? urlLogoProjeto(projectId, v) : null;
}

/** Teste "o projeto já tem logo?" a partir do mesmo índice (/api/project-identity). */
export function useTemLogo(): (projectId: string) => boolean {
  const versoes = useContext(IdentityContext)?.versoes;
  return useCallback((projectId: string) => !!versoes?.get(projectId), [versoes]);
}

export function useRecarregarIdentidade(): () => void {
  return useContext(IdentityContext)?.recarregar ?? (() => undefined);
}

/** "CHS001 - ETHANOL PLANT" → { codigo: "CHS001", titulo: "ETHANOL PLANT" }. Sem separador: título = nome. */
export function separarCodigo(nome: string): { codigo: string | null; titulo: string } {
  const m = /^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]\s*(.+)$/i.exec(nome);
  return m ? { codigo: m[1].toUpperCase(), titulo: m[2].trim() } : { codigo: null, titulo: nome.trim() };
}

/** Texto do fallback: código do projeto ("BML011.016") ou as 3 primeiras letras do nome. */
export const textoFallbackLogo = (nome: string) => {
  const { codigo, titulo } = separarCodigo(nome);
  return codigo ?? (titulo.slice(0, 3).toUpperCase() || "—");
};

const TAMANHOS = {
  xs: { box: "h-7 w-7", img: "max-h-6", texto: "text-[8px]" },
  sm: { box: "h-9 w-14", img: "max-h-8", texto: "text-[9px]" },
  md: { box: "h-12 w-20", img: "max-h-10", texto: "text-[10px]" },
  // Linha da Home (modo LISTA): mais larga que "sm" sem aumentar muito a altura da linha.
  lista: { box: "h-10 w-32", img: "max-h-9 max-w-[94%]", texto: "text-[10px]" },
  card: { box: "h-20 w-full", img: "max-h-16 max-w-[80%]", texto: "text-base" },
  tv: { box: "h-10 w-24", img: "max-h-10", texto: "text-xs" },
} as const;

/** Logo do projeto com fallback elegante (código do projeto). ÚNICO componente de logo do sistema. */
export function ProjectLogo({ projectId, nome, tamanho = "sm", className }:
  { projectId: string | null | undefined; nome: string; tamanho?: keyof typeof TAMANHOS; className?: string }) {
  const url = useProjectLogoUrl(projectId);
  const [falhou, setFalhou] = useState<string | null>(null);
  const t = TAMANHOS[tamanho];
  const temLogo = !!url && falhou !== url;
  return (
    <span className={cn("flex flex-shrink-0 items-center justify-center overflow-hidden rounded-md",
      temLogo ? "bg-white" : "border border-primary/30 bg-gradient-to-br from-primary/15 via-card to-card", t.box, className)}>
      {temLogo ? (
        <img src={url!} alt={`Logo ${nome}`} className={cn("object-contain p-0.5", t.img)} onError={() => setFalhou(url)} />
      ) : (
        <span className={cn("truncate px-1 font-mono font-black tracking-wider text-primary", t.texto)} aria-label={`Projeto ${nome}`}>
          {textoFallbackLogo(nome)}
        </span>
      )}
    </span>
  );
}

// ---------------------------------------------------------------------------------------------
// Alteração (Configuração do Projeto → Logo do Projeto). Backend exige settings.projects.edit.
// ---------------------------------------------------------------------------------------------
async function mensagem(r: Response, padrao: string) {
  try { const b = await r.json(); return typeof b?.message === "string" ? b.message : padrao; } catch { return padrao; }
}

export async function enviarLogoProjeto(projectId: string, arquivo: File): Promise<void> {
  const form = new FormData();
  form.append("arquivo", arquivo);
  const r = await fetch(`/api/project-identity/${encodeURIComponent(projectId)}/logo`, { method: "PUT", body: form, credentials: "same-origin" });
  if (!r.ok) throw new Error(r.status === 403 ? "Você não tem permissão para alterar a logo do projeto." : await mensagem(r, `Não foi possível salvar a logo (HTTP ${r.status}).`));
}

export async function removerLogoProjeto(projectId: string): Promise<void> {
  const r = await fetch(`/api/project-identity/${encodeURIComponent(projectId)}/logo`, { method: "DELETE", credentials: "same-origin" });
  if (!r.ok && r.status !== 404) throw new Error(r.status === 403 ? "Você não tem permissão para alterar a logo do projeto." : await mensagem(r, `Não foi possível remover a logo (HTTP ${r.status}).`));
}
