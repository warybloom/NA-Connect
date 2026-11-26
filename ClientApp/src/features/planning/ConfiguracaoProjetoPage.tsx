// Painel de Planejamento → Configuração do Projeto: cadastro dos projetos (Situação, Tipo LDP, idioma, norma) — ControlPanel,
// 10 por página. A Logo do Projeto saiu daqui (01/10/2026) para Configurações → Identidade Visual dos Projetos
// (/configuracoes/identidade-visual), com a MESMA implementação; aqui fica só o atalho.
// O template da Auditoria de Qualidade é uma COLUNA da tabela (01/10/2026); a pasta auditada é escolhida na própria tela da Auditoria.
import { Link } from "react-router-dom";
import ControlPanel from "@/pages/ControlPanel";

export default function ConfiguracaoProjetoPage() {
  return (
    <div className="space-y-8 animate-enter">
      <p className="text-xs text-muted-foreground">
        A logo do projeto agora fica em <Link className="text-primary underline" to="/configuracoes/identidade-visual">Configurações → Identidade Visual dos Projetos</Link>.
      </p>
      <section className="space-y-3">
        <div>
          <p className="text-[10px] font-black uppercase tracking-[0.3em] text-primary">Configuração do Projeto</p>
          <h2 className="text-xl font-black uppercase tracking-tight">Projetos</h2>
          <p className="text-xs text-muted-foreground">Situação, Tipo de LDP, idioma, norma aplicada e template de auditoria de cada projeto do N&amp;A Connect.</p>
        </div>
        <ControlPanel somenteProjetos />
      </section>
    </div>
  );
}
