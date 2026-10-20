import { AlertTriangle } from "lucide-react";

// Painel de Planejamento: cada etapa da LDP é item próprio do sidebar (sem página intermediária nem barra de abas).
export const LdpConfigPageHeader = ({ title, description }: { title: string; description: string }) => (
  <div className="mb-8 space-y-4">
    <div>
      <p className="text-[10px] font-black uppercase tracking-widest text-primary italic">
        Painel de Planejamento · LDP — Novo Motor (fundação)
      </p>
      <h1 className="text-2xl font-black uppercase italic tracking-tighter text-foreground mt-1">{title}</h1>
      <p className="text-xs text-muted-foreground mt-1">{description}</p>
    </div>

    <div className="flex items-start gap-3 p-4 rounded-xl border border-amber-500/30 bg-amber-500/5">
      <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
      <p className="text-[11px] text-muted-foreground leading-relaxed">
        <strong className="text-amber-600 dark:text-amber-400">Motor novo, isolado do legado — ainda não é produção.</strong>{" "}
        Importação de Excel/MPP, geração de LDP Espelho e Controle já produzem arquivos reais para
        validação. Persistência da configuração (Template/Mapeamento/Espelho/Controle) usa o schema{" "}
        <code className="text-[10px] bg-card/60 px-1 rounded">ldp.*</code>, que precisa das migrations
        propostas em <code className="text-[10px] bg-card/60 px-1 rounded">Migrations/ConfiguracaoLdp/</code>{" "}
        aplicadas manualmente antes de funcionar — enquanto isso, o "Salvar no banco" falha explicitamente
        (nunca finge sucesso). O Worker, Tipo_LDP 1/2 e a LDP Excel atual continuam funcionando
        exatamente como hoje, sem nenhuma conexão com esta tela.
      </p>
    </div>
  </div>
);
