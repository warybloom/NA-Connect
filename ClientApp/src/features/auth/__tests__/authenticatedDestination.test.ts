import test from "node:test";
import assert from "node:assert/strict";
import { authenticatedDestination } from "../authenticatedDestination";

const response = (body: unknown, status = 200) => async () =>
  new Response(JSON.stringify(body), { status });

test("TV restrito segue para entrada isolada; admin e misto administrativo preservam dashboard", async () => {
  assert.equal(await authenticatedDestination(response({ restricted: true, canConfigure: false })), "/tv/iniciar");
  assert.equal(await authenticatedDestination(response({ restricted: false, canConfigure: true })), null);
  assert.equal(await authenticatedDestination(response({ restricted: false, canConfigure: false })), null);
});

test("sessao expirada retorna ao login", async () => {
  assert.equal(await authenticatedDestination(response({}, 401)), "/login");
});

test("TV indisponivel ou resposta invalida nao bloqueia administradores", async () => {
  for (const status of [403, 404, 500, 503])
    assert.equal(await authenticatedDestination(response({}, status)), null);
  for (const body of [{}, null, { restricted: "true" }, { restricted: 1 }])
    assert.equal(await authenticatedDestination(response(body)), null);
  assert.equal(await authenticatedDestination(async () => { throw Error("offline"); }), null);
  assert.equal(await authenticatedDestination(async () => new Response("HTML")), null);
});

test("consulta usa cookie e cancelamento", async () => {
  const outer = new AbortController();
  const pending = authenticatedDestination(async (url, options) => {
    assert.equal(url, "/api/tv-portal/session");
    assert.equal(options?.credentials, "include");
    assert.ok(options?.signal);
    return new Promise<Response>((_, reject) => {
      options.signal!.addEventListener("abort", () => reject(Error("cancelado")), { once: true });
    });
  }, outer.signal);
  outer.abort();
  assert.equal(await pending, null);
});
