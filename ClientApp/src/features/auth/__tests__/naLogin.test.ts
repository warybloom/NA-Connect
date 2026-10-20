import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { LOGIN_MESSAGES, NA_CHANGE_PASSWORD_ENDPOINT, NA_LOGIN_ENDPOINT, loginNa, trocarSenhaPrimeiroAcesso } from "../naLoginApi";
import { FADE_MS, PIXEL_COVER_MS, PIXEL_REVEAL_MS } from "../AuthTransition";
import { MIN_PASSWORD_LENGTH, validarNovaSenha } from "../NaChangePasswordForm";

type Chamada = { url: string; init?: RequestInit };

const fetchFalso = (resposta: () => Response | Promise<Response>) => {
  const chamadas: Chamada[] = [];
  const impl = async (url: string, init?: RequestInit) => {
    chamadas.push({ url, init });
    return resposta();
  };
  return { chamadas, impl };
};

const json = (status: number, corpo: unknown) =>
  new Response(JSON.stringify(corpo), { status, headers: { "Content-Type": "application/json" } });

const SUCESSO = { userId: 42, email: "interno@na.com", nome: "Usuário Interno", provider: "N&A", mustChangePassword: false };

test("Login N&A chama POST /Authentication/Na/Login com credentials include, JSON e somente email/password", async () => {
  const f = fetchFalso(() => json(200, SUCESSO));
  await loginNa("interno@na.com", "Correta#2026", f.impl);

  assert.equal(f.chamadas.length, 1);
  const [{ url, init }] = f.chamadas;
  assert.equal(url, "/Authentication/Na/Login");
  assert.equal(NA_LOGIN_ENDPOINT, url);
  assert.equal(init?.method, "POST");
  assert.equal(init?.credentials, "include");
  assert.equal((init?.headers as Record<string, string>)["Content-Type"], "application/json");
  assert.deepEqual(JSON.parse(String(init?.body)), { email: "interno@na.com", password: "Correta#2026" });
  assert.ok(!url.includes("OAuth"), "login N&A não passa pelo fluxo Autodesk");
});

test("200 → success com o contrato do backend", async () => {
  const r = await loginNa("a@b.com", "x", fetchFalso(() => json(200, SUCESSO)).impl);
  assert.deepEqual(r, { kind: "success", user: SUCESSO });
});

test("200 com mustChangePassword=true é preservado (não ignorado)", async () => {
  const r = await loginNa("a@b.com", "x", fetchFalso(() => json(200, { ...SUCESSO, mustChangePassword: true })).impl);
  assert.equal(r.kind, "success");
  assert.equal(r.kind === "success" && r.user.mustChangePassword, true);
});

test("401 e 400 → invalid (mensagem genérica, sem distinguir o motivo)", async () => {
  const corpoBackend = { message: "E-mail ou senha inválidos. Após várias tentativas o acesso fica temporariamente bloqueado." };
  assert.deepEqual(await loginNa("a@b.com", "x", fetchFalso(() => json(401, corpoBackend)).impl), { kind: "invalid" });
  assert.deepEqual(await loginNa("a@b.com", "x", fetchFalso(() => json(400, {})).impl), { kind: "invalid" });
  assert.equal(LOGIN_MESSAGES.invalid, "E-mail ou senha inválidos.");
  for (const vazamento of ["inexistente", "inativo", "bloqueado", "credencial"])
    assert.ok(!LOGIN_MESSAGES.invalid.toLowerCase().includes(vazamento));
});

test("503, 500, rede e JSON inválido → unavailable", async () => {
  assert.deepEqual(await loginNa("a@b.com", "x", fetchFalso(() => json(503, { message: "x" })).impl), { kind: "unavailable" });
  assert.deepEqual(await loginNa("a@b.com", "x", fetchFalso(() => json(500, {})).impl), { kind: "unavailable" });
  assert.deepEqual(await loginNa("a@b.com", "x", async () => { throw new TypeError("Failed to fetch"); }), { kind: "unavailable" });
  assert.deepEqual(await loginNa("a@b.com", "x", fetchFalso(() => new Response("<html>", { status: 200 })).impl), { kind: "unavailable" });
  assert.match(LOGIN_MESSAGES.unavailable, /serviço de autenticação/);
});

test("Primeiro acesso: POST /Authentication/Na/ChangePassword com credentials include, sem userId", async () => {
  const f = fetchFalso(() => new Response(null, { status: 204 }));
  const r = await trocarSenhaPrimeiroAcesso("Nova-Senha-2026", "Nova-Senha-2026", f.impl);

  assert.deepEqual(r, { kind: "success" });
  assert.equal(f.chamadas.length, 1);
  const [{ url, init }] = f.chamadas;
  assert.equal(url, "/Authentication/Na/ChangePassword");
  assert.equal(NA_CHANGE_PASSWORD_ENDPOINT, url);
  assert.equal(init?.method, "POST");
  assert.equal(init?.credentials, "include");
  const corpo = JSON.parse(String(init?.body));
  assert.deepEqual(corpo, { newPassword: "Nova-Senha-2026", confirmPassword: "Nova-Senha-2026" });
  assert.ok(!("userId" in corpo), "self-service: o usuário vem da sessão");
  assert.ok(!url.includes("Logout"), "sucesso não encerra a sessão");
});

test("Primeiro acesso: 400/409 trazem a mensagem do backend; 401 sessão expirada; 503/rede indisponível", async () => {
  const politica = { message: "Senha muito simples. Escolha outra." };
  assert.deepEqual(await trocarSenhaPrimeiroAcesso("x", "x", fetchFalso(() => json(400, politica)).impl), { kind: "invalid", message: politica.message });
  assert.deepEqual(await trocarSenhaPrimeiroAcesso("x", "x", fetchFalso(() => json(409, { message: "Não há troca de senha pendente para este usuário." })).impl),
    { kind: "invalid", message: "Não há troca de senha pendente para este usuário." });
  assert.deepEqual(await trocarSenhaPrimeiroAcesso("x", "x", fetchFalso(() => json(401, {})).impl), { kind: "sessionExpired" });
  assert.deepEqual(await trocarSenhaPrimeiroAcesso("x", "x", fetchFalso(() => json(503, {})).impl), { kind: "unavailable" });
  assert.deepEqual(await trocarSenhaPrimeiroAcesso("x", "x", async () => { throw new TypeError("offline"); }), { kind: "unavailable" });
});

test("Primeiro acesso: validação local (vazia, mínimo da política, confirmação)", () => {
  assert.equal(MIN_PASSWORD_LENGTH, 8);
  assert.ok(validarNovaSenha("", ""));
  assert.ok(validarNovaSenha("        ", "        "));
  assert.ok(validarNovaSenha("curta1", "curta1"));
  assert.equal(validarNovaSenha("Nova-Senha-2026", "Outra-Senha-2026"), LOGIN_MESSAGES.confirmacao);
  assert.equal(validarNovaSenha("Nova-Senha-2026", "Nova-Senha-2026"), null);
});

test("Login com troca pendente não chama Logout (a sessão Auth:UserId permanece até a troca)", () => {
  const form = fonte("src/features/auth/NaLoginForm.tsx");
  assert.ok(!/Logout|encerrarSessao/.test(form));
  assert.match(form, /NaChangePasswordForm/);
});

test("Transição pixel dentro de 600–1000 ms; reduced-motion é um fade curto", () => {
  const total = PIXEL_COVER_MS + PIXEL_REVEAL_MS;
  assert.ok(total >= 600 && total <= 1000, `total=${total}`);
  assert.ok(FADE_MS * 2 <= 400);
});

const fonte = (relativo: string) => {
  const candidatos = [path.resolve(process.cwd(), relativo), path.resolve(process.cwd(), "ClientApp", relativo)];
  for (const c of candidatos) {
    try { return readFileSync(c, "utf8"); } catch { /* tenta o próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};

test("Login Autodesk preservado no Index: GetLoginUrl com credentials include e redirect para loginUrl", () => {
  const index = fonte("src/pages/Index.tsx");
  assert.match(index, /fetch\('\/Authentication\/OAuth\/GetLoginUrl', \{\s*credentials: 'include'\s*\}\)/);
  assert.match(index, /window\.location\.href = data\.loginUrl;/);
  assert.match(index, /onAutodeskLogin=\{handleLogin\}/);
});

test("Sem cadastro público nem recuperação inventados: nenhum endpoint de register/forgot no frontend de auth", () => {
  for (const arquivo of ["src/features/auth/naLoginApi.ts", "src/features/auth/AuthInfoPanels.tsx", "src/features/auth/NaLoginForm.tsx", "src/features/auth/AuthShell.tsx", "src/features/auth/NaChangePasswordForm.tsx"]) {
    const texto = fonte(arquivo);
    assert.ok(!/register|forgot-password|reset-password|\/signup/i.test(texto.replace(/ForgotPassword|onForgotPassword/g, "")), arquivo);
    assert.ok(!/\balert\(/.test(texto), `${arquivo} não usa alert()`);
  }
});
