// Ficheiro: ClientApp/src/features/auth/NaLoginForm.tsx
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { ArrowRight, Eye, EyeOff, LoaderCircle } from "lucide-react";
import { LOGIN_MESSAGES, loginNa, type NaLoginUser } from "./naLoginApi";
import { NaChangePasswordForm } from "./NaChangePasswordForm";

type Status = "idle" | "loading" | "error" | "success" | "mustChangePassword";

interface NaLoginFormProps {
  /** Sessão N&A criada (200, sem troca de senha pendente): o chamador segue o roteamento atual. */
  onAuthenticated: (user: NaLoginUser) => void;
  onForgotPassword: () => void;
  onRequestAccess: () => void;
  autoFocus?: boolean;
}

export function NaLoginForm({ onAuthenticated, onForgotPassword, onRequestAccess, autoFocus }: NaLoginFormProps) {
  const ids = { email: useId(), password: useId(), feedback: useId() };
  const emailRef = useRef<HTMLInputElement>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [status, setStatus] = useState<Status>("idle");
  const [message, setMessage] = useState("");
  const [usuarioTroca, setUsuarioTroca] = useState<NaLoginUser | null>(null);
  const enviando = useRef(false);

  useEffect(() => {
    if (autoFocus) emailRef.current?.focus({ preventScroll: true });
  }, [autoFocus]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    // Bloqueio síncrono: dois Enter seguidos não disparam duas requisições.
    if (enviando.current) return;
    enviando.current = true;
    setStatus("loading");
    setMessage("");

    const result = await loginNa(email.trim(), password);
    if (result.kind === "success") {
      if (result.user.mustChangePassword) {
        // Primeiro acesso: a sessão (Auth:UserId) continua; o sistema só libera depois da troca (CheckStatus).
        setPassword("");
        setStatus("mustChangePassword");
        setUsuarioTroca(result.user);
        enviando.current = false;
        return;
      }
      setStatus("success");
      onAuthenticated(result.user);
      return;
    }

    setPassword("");
    setStatus("error");
    setMessage(result.kind === "invalid" ? LOGIN_MESSAGES.invalid : LOGIN_MESSAGES.unavailable);
    enviando.current = false;
  };

  const loading = status === "loading" || status === "success";

  if (status === "mustChangePassword" && usuarioTroca)
    return <NaChangePasswordForm onChanged={() => onAuthenticated({ ...usuarioTroca, mustChangePassword: false })} />;

  return (
    <form className="na-auth-form" onSubmit={submit} noValidate={false} aria-busy={loading} aria-describedby={message ? ids.feedback : undefined}>
      <div className="na-auth-form__head">
        <p className="na-auth-form__eyebrow">Login N&amp;A Connect</p>
        <h2 className="na-auth-form__title">Entrar</h2>
        <p className="na-auth-form__lead">Use o e-mail e a senha cadastrados pelo administrador do N&amp;A Connect.</p>
      </div>

      <div className="na-auth-field">
        <label className="na-auth-field__label" htmlFor={ids.email}>E-mail</label>
        <input
          ref={emailRef}
          id={ids.email}
          className="na-auth-field__input"
          type="email"
          name="email"
          autoComplete="email"
          inputMode="email"
          spellCheck={false}
          required
          maxLength={256}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          disabled={loading}
          aria-invalid={status === "error" || undefined}
        />
      </div>

      <div className="na-auth-field">
        <div className="na-auth-field__row">
          <label className="na-auth-field__label" htmlFor={ids.password}>Senha</label>
          <button type="button" className="na-auth-link" onClick={onForgotPassword} disabled={loading}>
            Esqueci minha senha
          </button>
        </div>
        <div className="na-auth-field__control">
          <input
            id={ids.password}
            className="na-auth-field__input na-auth-field__input--password"
            type={showPassword ? "text" : "password"}
            name="password"
            autoComplete="current-password"
            required
            maxLength={256}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={loading}
            aria-invalid={status === "error" || undefined}
          />
          <button
            type="button"
            className="na-auth-field__toggle"
            onClick={() => setShowPassword((v) => !v)}
            aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
            aria-pressed={showPassword}
            aria-controls={ids.password}
            disabled={loading}
          >
            {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
          </button>
        </div>
      </div>

      <p
        id={ids.feedback}
        className="na-auth-form__feedback"
        data-tone={status === "mustChangePassword" ? "warning" : "error"}
        role="alert"
        aria-live="assertive"
      >
        {message}
      </p>

      <button type="submit" className="na-auth-submit" disabled={loading} aria-disabled={loading}>
        {loading ? (
          <>
            <LoaderCircle className="na-auth-submit__spinner" aria-hidden="true" />
            {status === "success" ? "Entrando…" : "Verificando…"}
          </>
        ) : (
          <>
            Entrar
            <ArrowRight className="na-auth-submit__icon" aria-hidden="true" />
          </>
        )}
      </button>

      <p className="na-auth-form__alt">
        Ainda não possui acesso?{" "}
        <button type="button" className="na-auth-link" onClick={onRequestAccess} disabled={loading}>
          Criar uma conta
        </button>
      </p>
    </form>
  );
}
