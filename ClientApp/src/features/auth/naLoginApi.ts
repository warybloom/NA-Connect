// Ficheiro: ClientApp/src/features/auth/naLoginApi.ts
// Transporte do login N&A local (backend 06A). Nenhuma chamada Autodesk aqui.

export const NA_LOGIN_ENDPOINT = "/Authentication/Na/Login";
/** Troca obrigatória da PRÓPRIA senha (primeiro acesso). O usuário vem da sessão; nunca se envia userId. */
export const NA_CHANGE_PASSWORD_ENDPOINT = "/Authentication/Na/ChangePassword";

/** Contrato de sucesso (200) de POST /Authentication/Na/Login. */
export interface NaLoginUser {
  userId: number;
  email: string;
  nome: string;
  provider: string;
  mustChangePassword: boolean;
}

export type NaLoginResult =
  | { kind: "success"; user: NaLoginUser }
  /** 401/400: credencial inválida — o backend não distingue inexistente, inativo, bloqueado ou sem senha. */
  | { kind: "invalid" }
  /** 503, outro status ou falha de rede. */
  | { kind: "unavailable" };

export const LOGIN_MESSAGES = {
  invalid: "E-mail ou senha inválidos.",
  unavailable: "Não foi possível acessar o serviço de autenticação. Tente novamente.",
  mustChangePassword: "É necessário alterar sua senha antes de continuar.",
  confirmacao: "A confirmação não confere com a nova senha.",
  sessaoExpirada: "Sua sessão expirou. Entre novamente com a senha inicial.",
  trocaIndisponivel: "Não foi possível alterar a senha agora. Tente novamente.",
} as const;

export type NaChangePasswordResult =
  | { kind: "success" }
  /** 400/409: mensagem de validação do backend (política de senha, confirmação, troca não pendente). */
  | { kind: "invalid"; message: string }
  /** 401: sem Auth:UserId na sessão. */
  | { kind: "sessionExpired" }
  | { kind: "unavailable" };

type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

export async function loginNa(email: string, password: string, fetchImpl: FetchLike = fetch): Promise<NaLoginResult> {
  let response: Response;
  try {
    response = await fetchImpl(NA_LOGIN_ENDPOINT, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ email, password }),
    });
  } catch {
    return { kind: "unavailable" };
  }

  if (response.status === 401 || response.status === 400) return { kind: "invalid" };
  if (!response.ok) return { kind: "unavailable" };

  try {
    const user = (await response.json()) as NaLoginUser;
    if (typeof user?.userId !== "number") return { kind: "unavailable" };
    return { kind: "success", user: { ...user, mustChangePassword: user.mustChangePassword === true } };
  } catch {
    return { kind: "unavailable" };
  }
}

/** Primeiro acesso: define a nova senha do usuário da sessão. Sucesso mantém a sessão (sem segundo login). */
export async function trocarSenhaPrimeiroAcesso(newPassword: string, confirmPassword: string, fetchImpl: FetchLike = fetch): Promise<NaChangePasswordResult> {
  let response: Response;
  try {
    response = await fetchImpl(NA_CHANGE_PASSWORD_ENDPOINT, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ newPassword, confirmPassword }),
    });
  } catch {
    return { kind: "unavailable" };
  }
  if (response.ok) return { kind: "success" };
  if (response.status === 401) return { kind: "sessionExpired" };
  if (response.status === 400 || response.status === 409) {
    let message = "";
    try { const corpo = (await response.json()) as { message?: unknown }; if (typeof corpo?.message === "string") message = corpo.message; } catch { /* sem corpo */ }
    return { kind: "invalid", message: message || LOGIN_MESSAGES.trocaIndisponivel };
  }
  return { kind: "unavailable" };
}
