// Ficheiro: ClientApp/src/features/auth/AuthInfoPanels.tsx
// Painéis informativos do Auth Shell. Não existe cadastro público nem recuperação de senha por e-mail no backend:
// quando existirem, estes painéis passam a conter os formulários correspondentes.
import { useEffect, useRef } from "react";
import { ArrowLeft } from "lucide-react";

interface InfoPanelProps {
  onBack: () => void;
}

function InfoPanel({ eyebrow, title, children, onBack }: InfoPanelProps & { eyebrow: string; title: string; children: React.ReactNode }) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => headingRef.current?.focus({ preventScroll: true }), []);

  return (
    <section className="na-auth-info" aria-labelledby="na-auth-info-title">
      <p className="na-auth-form__eyebrow">{eyebrow}</p>
      <h2 id="na-auth-info-title" className="na-auth-form__title" tabIndex={-1} ref={headingRef}>
        {title}
      </h2>
      <div className="na-auth-info__body">{children}</div>
      <button type="button" className="na-auth-back" onClick={onBack}>
        <ArrowLeft aria-hidden="true" />
        Voltar para entrar
      </button>
    </section>
  );
}

export function ForgotPasswordInfo({ onBack }: InfoPanelProps) {
  return (
    <InfoPanel eyebrow="Login N&A Connect" title="Redefinir senha" onBack={onBack}>
      <p>Solicite a redefinição de senha ao administrador do N&amp;A Connect.</p>
      <p className="na-auth-info__hint">
        Após a redefinição, entre com o mesmo e-mail e a nova senha informada pelo administrador.
      </p>
    </InfoPanel>
  );
}

export function RequestAccessInfo({ onBack }: InfoPanelProps) {
  return (
    <InfoPanel eyebrow="Login N&A Connect" title="Solicitar acesso" onBack={onBack}>
      <p>Solicite seu acesso ao administrador do N&amp;A Connect.</p>
      <p className="na-auth-info__hint">
        O administrador cadastra o seu e-mail, define a senha inicial e vincula os projetos que você poderá acessar.
      </p>
    </InfoPanel>
  );
}
