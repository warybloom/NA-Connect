import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { TELAS_TV, TV_CURVA_S, getTelaTv, permissaoTelaTv } from "../tvScreens";
import { encontrarFolha } from "../../production/productionRoutes";
import { criarCan } from "../../permissions/permissionLogic";

test("TV: Curva S existente entre os conteúdos, com a MESMA permissão da rota /controle-producao/curva-s", () => {
  assert.ok(TELAS_TV.some((t) => t.id === TV_CURVA_S));
  assert.equal(getTelaTv("curva-s")?.label, "Curva S");
  assert.equal(getTelaTv("ldp")?.label, "LDP");                     // dashboards continuam do registry
  assert.deepEqual(permissaoTelaTv("curva-s"), encontrarFolha("curva-s")?.permissionKeys);
  assert.equal(permissaoTelaTv("ldp"), null);                       // demais telas: inalteradas
  const eff = (keys: string[]) => ({ enforced: true, schemaInstalled: true, isMasterAdmin: false, email: "u@na.com", projectIdAcc: null, permissions: [], anyProject: keys });
  assert.equal(criarCan(eff(["production.operational.curva-s.view"]), "any")(permissaoTelaTv("curva-s")), true);
  assert.equal(criarCan(eff(["production.operational.ldp.view"]), "any")(permissaoTelaTv("curva-s")), false);
});

test("Player: nome da tela em exibição no centro exato (destaque), nome da apresentação à esquerda (secundário)", () => {
  const src = readFileSync("src/features/tv/TvPlayerPage.tsx", "utf-8");
  // 20/10/2026: o texto vem de tituloCentralTv (rótulo original nas demais telas; "PROJETO — DISCIPLINA" na diária — tvTituloCentral.test.ts).
  assert.match(src, /absolute left-1\/2 top-1\/2[^"]*-translate-x-1\/2[^>]*\n?\s*data-testid="tv-tela-atual">\s*\{titulo\.texto\}/);
  assert.match(src, /const titulo = tituloCentralTv\(slide, def\?\.label, TV_PROGRAMACAO_DIARIA\);/);
  assert.match(src, /data-testid="tv-identificacao"[\s\S]*?>\{apresentacao\.name\}<\/p>/);
  assert.ok(src.indexOf("<ThemeLogo") < src.indexOf("tv-identificacao"));
  assert.match(src, /<ConteudoTv/);
});
