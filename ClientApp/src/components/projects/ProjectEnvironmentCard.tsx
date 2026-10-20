// Card (modo CARDS) e linha (modo LISTA) de projeto da HOME. A logo vem SEMPRE de <ProjectLogo> (fonte única:
// Configuração do Projeto → Logo do Projeto); sem logo, fallback com o código do projeto.
import { FolderOpen } from "lucide-react";
import { ProjectLogo, separarCodigo } from "@/features/project/ProjectIdentity";

export { separarCodigo };

interface Props {
  projectId: string;
  nome: string;
  plataforma?: string | null;
  /** Situação no N&A (dbo.Projetos): null = ainda não registrado pelo robô. */
  tipoLdp: number | null;
  /** dbo.Projetos.Is_Ativo (null = não registrado). */
  ativo?: boolean | null;
  onEntrar: () => void;
  /** Ação secundária opcional (abrir arquivos no ACC) — só passada quando o usuário tem permissão. */
  onArquivos?: () => void;
}

const SeloLdp = ({ tipoLdp }: { tipoLdp: number | null }) => tipoLdp != null
  ? <span className="rounded bg-muted px-1.5 py-0.5">LDP Tipo {tipoLdp}</span>
  : <span className="rounded border border-border/50 px-1.5 py-0.5 text-muted-foreground">LDP não informado</span>;

const BotaoArquivos = ({ onArquivos, className }: { onArquivos: () => void; className: string }) => (
  <button type="button" onClick={onArquivos} title="Abrir arquivos do projeto no ACC" aria-label="Abrir arquivos do projeto no ACC"
    className={className}>
    <FolderOpen className="h-3.5 w-3.5" />
  </button>
);

export function ProjectEnvironmentCard({ projectId, nome, plataforma, tipoLdp, onEntrar, onArquivos }: Props) {
  const { codigo, titulo } = separarCodigo(nome);
  return (
    <div className="group relative flex flex-col overflow-hidden rounded-xl border border-border/60 bg-card/50 transition-all hover:-translate-y-0.5 hover:border-primary/60 hover:shadow-lg hover:shadow-primary/10">
      <button type="button" onClick={onEntrar} className="flex flex-1 flex-col text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary" title={`Entrar no projeto ${nome}`}>
        <div className="border-b border-border/50 p-2">
          <ProjectLogo projectId={projectId} nome={nome} tamanho="card" className="rounded-lg" />
        </div>
        <div className="flex flex-1 flex-col gap-0.5 px-2.5 py-2">
          {/* Código maior que antes (10px), mas ainda menor que o nome do projeto (13px). */}
          {codigo && <span className="font-mono text-xs font-black tracking-wider text-primary">{codigo}</span>}
          <span className="line-clamp-2 min-h-[2.25rem] text-[13px] font-bold leading-tight text-foreground">{titulo}</span>
          <span className="mt-auto flex flex-wrap items-center gap-1.5 pt-1 text-[9px] font-bold uppercase tracking-wider text-muted-foreground">
            {plataforma && <span>{plataforma}</span>}
            <SeloLdp tipoLdp={tipoLdp} />
          </span>
        </div>
      </button>
      {onArquivos && (
        <BotaoArquivos onArquivos={onArquivos}
          className="absolute right-2 top-2 rounded-md border border-border/60 bg-card/80 p-1.5 text-muted-foreground opacity-0 transition-opacity hover:text-primary group-hover:opacity-100 focus:opacity-100" />
      )}
    </div>
  );
}

/** Linha compacta (modo LISTA) — para quem tem muitos projetos. */
export function ProjectEnvironmentRow({ projectId, nome, plataforma, tipoLdp, ativo, onEntrar, onArquivos }: Props) {
  const { codigo, titulo } = separarCodigo(nome);
  return (
    <div className="group flex items-center gap-3 border-b border-border/40 px-3 py-1.5 last:border-b-0 hover:bg-primary/5">
      <button type="button" onClick={onEntrar} title={`Entrar no projeto ${nome}`}
        className="flex min-w-0 flex-1 items-center gap-3 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
        <ProjectLogo projectId={projectId} nome={nome} tamanho="lista" />
        <span className="w-28 flex-shrink-0 truncate font-mono text-[11px] font-black tracking-widest text-primary">{codigo ?? "—"}</span>
        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-foreground">{titulo}</span>
        {plataforma && <span className="hidden w-20 flex-shrink-0 text-[10px] font-bold uppercase tracking-wider text-muted-foreground sm:block">{plataforma}</span>}
        <span className="hidden w-28 flex-shrink-0 text-[10px] font-bold uppercase tracking-wider text-muted-foreground md:block"><SeloLdp tipoLdp={tipoLdp} /></span>
        <span className="hidden w-16 flex-shrink-0 text-[10px] font-bold uppercase tracking-wider lg:block">
          {ativo == null ? <span className="text-muted-foreground">—</span> : ativo ? <span className="text-emerald-500">Ativo</span> : <span className="text-muted-foreground">Inativo</span>}
        </span>
      </button>
      <span className="w-8 flex-shrink-0">
        {onArquivos && <BotaoArquivos onArquivos={onArquivos} className="rounded-md p-1.5 text-muted-foreground hover:bg-accent hover:text-primary" />}
      </span>
    </div>
  );
}
