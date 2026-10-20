import { ReactNode } from "react";

export function PageHeader({ title, description }: { title: string; description: string }) {
  return <div className="mb-8"><p className="text-xs uppercase tracking-widest text-primary mb-2">N&A Connect</p><h1 className="text-3xl font-semibold tracking-tight mb-3">{title}</h1><p className="text-muted-foreground max-w-3xl">{description}</p></div>;
}
export function KpiCard({ label, value, detail }: { label: string; value: ReactNode; detail: string }) {
  return <article className="rounded-xl border border-border bg-card/70 p-5"><h2 className="text-sm text-muted-foreground">{label}</h2><p className="text-3xl font-semibold my-3 tabular-nums">{value}</p><p className="text-xs text-muted-foreground">{detail}</p></article>;
}
export function ReportState({ title, children, error = false }: { title: string; children?: ReactNode; error?: boolean }) {
  return <div role={error ? 'alert' : 'status'} className="rounded-xl border border-border bg-card/60 p-8 text-center"><h2 className="font-semibold mb-2">{title}</h2><div className="text-muted-foreground">{children}</div></div>;
}
export function LoadingState() {
  return <div role="status" aria-label="Carregando" className="space-y-4"><p className="text-muted-foreground">Carregando...</p>{[1, 2, 3].map(n => <div key={n} className="h-16 rounded-xl bg-muted animate-pulse" />)}</div>;
}
