// Ficheiro: ClientApp/src/features/login/LoginActions.tsx
import { forwardRef } from "react";
import { ArrowRight } from "lucide-react";

interface LoginActionsProps {
  /** Fluxo OAuth Autodesk existente (handleLogin de pages/Index.tsx), repassado sem alteração. */
  onAutodeskLogin: () => void;
  /** Abre o Auth Shell do login N&A (e-mail + senha). */
  onNaLogin: () => void;
}

/** O ref aponta para o CTA N&A, que recebe o foco ao voltar do Auth Shell. */
export const LoginActions = forwardRef<HTMLButtonElement, LoginActionsProps>(function LoginActions(
  { onAutodeskLogin, onNaLogin },
  naButtonRef,
) {
  return (
    <div className="na-login-actions">
      <div className="na-login-actions__row">
        <button type="button" className="na-login-btn na-login-btn--primary" onClick={onAutodeskLogin}>
          Login Autodesk
          <ArrowRight className="na-login-btn__icon" aria-hidden="true" />
        </button>

        <button ref={naButtonRef} type="button" className="na-login-btn na-login-btn--secondary" onClick={onNaLogin}>
          Login N&amp;A Connect
          <ArrowRight className="na-login-btn__icon" aria-hidden="true" />
        </button>
      </div>
      <p className="na-login-actions__note">
        Acesse com a sua conta Autodesk Construction Cloud ou com o seu login N&amp;A Connect.
      </p>
    </div>
  );
});
