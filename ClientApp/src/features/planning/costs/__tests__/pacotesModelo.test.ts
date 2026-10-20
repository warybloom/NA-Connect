import test from "node:test";
import assert from "node:assert/strict";
import { __definirFetch, custosApi, CustosError } from "../costsApi";

test("modelo de pacotes: download GET autenticado no projeto e resposta binária", async () => {
  const projectId = "b.11111111-2222-3333-4444-555555555555";
  let called = false;
  __definirFetch(async (path, init) => {
    called = true;
    assert.match(path, /^\/api\/project-costs\/import\/template\?projectId=b\./);
    assert.equal(init?.credentials, "include");
    assert.equal(init?.method, undefined);
    return new Response(new Blob(["modelo"], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }), { status: 200 });
  });
  const result = await custosApi.baixarModeloPacotes(projectId);
  assert.equal(called, true);
  assert.equal(result.size, 6);
});

test("modelo de pacotes: respeita o bloqueio de acesso à API", async () => {
  __definirFetch(async () => new Response(JSON.stringify({ message: "Sem autorização" }), {
    status: 404, headers: { "Content-Type": "application/json" },
  }));
  await assert.rejects(
    custosApi.baixarModeloPacotes("b.11111111-2222-3333-4444-555555555555"),
    (error: unknown) => error instanceof CustosError && error.status === 404,
  );
});
