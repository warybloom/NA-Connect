// Ficheiro: ClientApp/src/features/auth/NaChangePasswordForm.tsx
// Primeiro acesso: troca obrigatória da própria senha, no mesmo Auth Shell, sem segundo login.
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { ArrowRight, Eye, EyeOff, LoaderCircle } from "lucide-react";
import { LOGIN_MESSAGES, trocarSenhaPrimeiroAcesso } from "./naLoginApi";

/** Mesma política mínima do backend (NaLocalAuthPolicy.MinPasswordLength); o backend continua sendo a validação final. */
export const MIN_PASSWORD_LENGTH = 8;

/** Validação local antes do envio. Retorna a mensagem ou null. */
export function validarNovaSenha(nova: string, confirmacao: string): string | null {
  if (!nova.trim()) return "Informe a nova senha.";
  if (nova.length < MIN_PASSWORD_LENGTH) return `A senha deve ter pelo menos ${MIN_PASSWORD_LENGTH} caracteres.`;
  if (nova !== confirmacao) return LOGIN_MESSAGES.confirmacao;
  return null;
}

interface Props {
  /** Senha trocada: a sessão já está liberada; o chamador segue para a Home. */
  onChanged: () => void;
}

export function NaChangePasswordForm({ onChanged }: Props) {
  const ids = { nova: useId(), confirmacao: useId(), feedback: useId(), regra: useId() };
  const novaRef = useRef<HTMLInputElement>(null);
  const [nova, setNova] = useState("");
  const [confirmacao, setConfirmacao] = useState("");
  const [mostrar, setMostrar] = useState(false);
  const [status, setStatus] = useState<"idle" | "loading" | "error" | "success">("idle");
  const [message, setMessage] = useState<string>(LOGIN_MESSAGES.mustChangePassword);
  const enviando = useRef(false);

  useEffect(() => { novaRef.current?.focus({ preventScroll: true }); }, []);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (enviando.current) return;
    const erroLocal = validarNovaSenha(nova, confirmacao);
    if (erroLocal) { setStatus("error"); setMessage(erroLocal); return; }

    enviando.current = true;
    setStatus("loading");
    const r = await trocarSenhaPrimeiroAcesso(nova, confirmacao);
    if (r.kind === "success") {
      setStatus("success");
      onChanged();
      return;
    }
    setStatus("error");
    setMessage(r.kind === "invalid" ? r.message : r.kind === "sessionExpired" ? LOGIN_MESSAGES.sessaoExpirada : LOGIN_MESSAGES.trocaIndisponivel);
    enviando.current = false;
  };

  const loading = status === "loading" || status === "success";

  return (
    <form className="na-auth-form" onSubmit={submit} aria-busy={loading} aria-describedby={ids.feedback}>
      <div className="na-auth-form__head">
        <p className="na-auth-form__eyebrow">Primeiro acesso</p>
        <h2 className="na-auth-form__title">Defina sua nova senha</h2>
        <p className="na-auth-form__lead" id={ids.regra}>Use pelo menos {MIN_PASSWORD_LENGTH} caracteres e uma senha diferente da inicial.</p>
      </div>

      <div className="na-auth-field">
        <label className="na-auth-field__label" htmlFor={ids.nova}>Nova senha</label>
        <div className="na-auth-field__control">
          <input ref={novaRef} id={ids.nova} className="na-auth-field__input na-auth-field__input--password"
            type={mostrar ? "text" : "password"} name="new-password" autoComplete="new-password" required
            minLength={MIN_PASSWORD_LENGTH} maxLength={256} value={nova} onChange={(e) => setNova(e.target.value)}
            disabled={loading} aria-describedby={ids.regra} aria-invalid={status === "error" || undefined} />
          <button type="button" className="na-auth-field__toggle" onClick={() => setMostrar((v) => !v)}
            aria-label={mostrar ? "Ocultar senhas" : "Mostrar senhas"} aria-pressed={mostrar} disabled={loading}>
            {mostrar ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
          </button>
        </div>
      </div>

      <div className="na-auth-field">
        <label className="na-auth-field__label" htmlFor={ids.confirmacao}>Confirmar nova senha</label>
        <input id={ids.confirmacao} className="na-auth-field__input" type={mostrar ? "text" : "password"}
          name="confirm-password" autoComplete="new-password" required maxLength={256} value={confirmacao}
          onChange={(e) => setConfirmacao(e.target.value)} disabled={loading} aria-invalid={status === "error" || undefined} />
      </div>

      <p id={ids.feedback} className="na-auth-form__feedback" data-tone={status === "error" ? "error" : "warning"} role="alert" aria-live="assertive">
        {message}
      </p>

      <button type="submit" className="na-auth-submit" disabled={loading}>
        {loading ? (
          <><LoaderCircle className="na-auth-submit__spinner" aria-hidden="true" />{status === "success" ? "Entrando…" : "Salvando…"}</>
        ) : (
          <>Salvar e entrar<ArrowRight className="na-auth-submit__icon" aria-hidden="true" /></>
        )}
      </button>
    </form>
  );
}
