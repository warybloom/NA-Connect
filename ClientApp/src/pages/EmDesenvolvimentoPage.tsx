// Placeholder genérico para módulos do novo sidebar que ainda NÃO têm tela funcional
// (Planejamento 4D, Orçamento 5D, CDE, Controle de Produção, Modelo Federado).
// Só evita link morto — nenhuma funcionalidade, dado ou chamada de API.
import { Construction } from "lucide-react";

interface EmDesenvolvimentoPageProps {
  title: string;
  description?: string;
}

const EmDesenvolvimentoPage = ({ title, description }: EmDesenvolvimentoPageProps) => (
  <div className="flex h-full min-h-[60vh] items-center justify-center">
    <div className="w-full max-w-lg rounded-xl border border-border bg-card/60 p-10 text-center backdrop-blur-sm">
      <div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-full border border-amber-500/30 bg-amber-500/10 text-amber-500">
        <Construction className="h-7 w-7" />
      </div>
      <h2 className="text-xl font-bold tracking-tight">{title}</h2>
      <span className="mt-3 inline-block rounded-full border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-[10px] font-black uppercase tracking-widest text-amber-500">
        Em desenvolvimento
      </span>
      <p className="mt-4 text-sm text-muted-foreground">
        {description ?? "Este módulo ainda está em desenvolvimento e será disponibilizado em uma próxima fase."}
      </p>
    </div>
  </div>
);

export default EmDesenvolvimentoPage;
