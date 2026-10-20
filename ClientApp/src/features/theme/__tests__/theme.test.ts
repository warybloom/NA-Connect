import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { normalizarPreferencia, resolverTema, OPCOES_TEMA, PREFERENCIA_PADRAO, THEME_STORAGE_KEY } from "../themeLogic";

const fonte = (relativo: string) => {
  const candidatos = [path.resolve(process.cwd(), relativo), path.resolve(process.cwd(), "ClientApp", relativo)];
  for (const c of candidatos) {
    try { return readFileSync(c, "utf8"); } catch { /* tenta o próximo */ }
  }
  throw new Error(`Fonte não encontrada: ${relativo}`);
};

/** Corpo de um bloco CSS que começa no seletor dado (até a chave de fechamento correspondente). */
const bloco = (css: string, inicio: string) => {
  const i = css.indexOf(inicio);
  assert.ok(i >= 0, `bloco não encontrado: ${inicio}`);
  let nivel = 0;
  for (let j = css.indexOf("{", i); j < css.length; j++) {
    if (css[j] === "{") nivel++;
    if (css[j] === "}" && --nivel === 0) return css.slice(i, j + 1);
  }
  throw new Error("bloco sem fechamento");
};
const variaveis = (corpo: string) => new Set(Array.from(corpo.matchAll(/(--[\w-]+)\s*:/g), (m) => m[1]));

test("Três opções na ordem Claro, Escuro, Automático", () => {
  assert.deepEqual(OPCOES_TEMA.map((o) => o.valor), ["light", "dark", "system"]);
  assert.deepEqual(OPCOES_TEMA.map((o) => o.rotulo), ["Claro", "Escuro", "Automático"]);
});

test("Automático segue o sistema operacional; Claro/Escuro ignoram o sistema", () => {
  assert.equal(resolverTema("system", true), "dark");
  assert.equal(resolverTema("system", false), "light");
  assert.equal(resolverTema("light", true), "light");
  assert.equal(resolverTema("dark", false), "dark");
});

test("Preferência salva inválida volta ao padrão (escuro histórico)", () => {
  assert.equal(PREFERENCIA_PADRAO, "dark");
  for (const v of [null, undefined, "", "azul", "DARK"]) assert.equal(normalizarPreferencia(v), "dark");
  for (const v of ["light", "dark", "system"] as const) assert.equal(normalizarPreferencia(v), v);
});

test("Script anti-piscar de index.html usa a mesma chave e o mesmo padrão do ThemeProvider", () => {
  const html = fonte("index.html");
  assert.ok(html.includes(`localStorage.getItem("${THEME_STORAGE_KEY}")`));
  assert.ok(html.includes(`var pref = "${PREFERENCIA_PADRAO}"`));
  assert.ok(html.includes("prefers-color-scheme: dark"));
  const provider = fonte("src/features/theme/ThemeProvider.tsx");
  assert.ok(provider.includes('attribute="class"') && provider.includes("enableSystem") && provider.includes("storageKey={THEME_STORAGE_KEY}"));
  assert.ok(fonte("src/main.tsx").includes("<ThemeProvider>"));
});

test("Tokens: claro (:root) e escuro (.dark) definem exatamente as mesmas variáveis, inclusive a slate do tema", () => {
  const css = fonte("src/index.css");
  const claro = variaveis(bloco(css, ":root {"));
  const escuro = variaveis(bloco(css, ".dark {"));
  const soNoClaro = [...claro].filter((v) => !escuro.has(v) && v !== "--radius" && v !== "--transition-smooth");
  const soNoEscuro = [...escuro].filter((v) => !claro.has(v));
  assert.deepEqual(soNoClaro, []);
  assert.deepEqual(soNoEscuro, []);
  for (const n of [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]) assert.ok(claro.has(`--slate-${n}`), `--slate-${n}`);
  for (const t of ["--warning", "--chart-grid", "--chart-axis", "--chart-cursor"]) assert.ok(claro.has(t), t);
  assert.ok(fonte("tailwind.config.js").includes("rgb(var(--slate-${n}) / <alpha-value>)"));
});

test("Seletor presente no header do Layout, na Home e no Login", () => {
  assert.ok(fonte("src/components/Layout.tsx").includes("<ThemeSelector"));
  assert.ok(fonte("src/pages/Dashboard.tsx").includes("<ThemeSelector"));
  assert.ok(fonte("src/features/auth/AuthShell.tsx").includes("<ThemeSelector"));
  const seletor = fonte("src/features/theme/ThemeSelector.tsx");
  assert.ok(seletor.includes('role="radiogroup"') && seletor.includes('role="radio"') && seletor.includes("aria-checked"));
});

test("Gestão à Vista (Jamile): tema escuro só redefine tokens; layout não muda entre os temas", () => {
  const css = fonte("src/features/production/analytics/gestaoVistaJamile.css");
  const escuro = bloco(css, ".dark .gv-shell{");
  // o bloco escuro contém apenas declarações de variáveis + color-scheme (nenhuma regra de layout)
  const declaracoes = escuro.slice(escuro.indexOf("{") + 1, -1).split(";").map((d) => d.trim()).filter(Boolean);
  for (const d of declaracoes) assert.ok(d.startsWith("--") || d.startsWith("color-scheme"), d);
  // todo token do claro tem par no escuro (exceto os derivados por var())
  const claro = variaveis(bloco(css, ".gv-shell{"));
  const esc = variaveis(escuro);
  const faltando = [...claro].filter((v) => !esc.has(v));
  assert.deepEqual(faltando, []);
  // fora dos tokens, nenhuma cor fixa clara que quebraria o escuro (#fff/#222 etc.)
  const semTokens = css.replace(bloco(css, ".gv-shell{"), "").replace(escuro, "");
  assert.equal(/#(?:fff|ffffff|222|1b1b1b|f3f5f8)\b/i.test(semTokens), false);
});

test("ThemeLogo: arquivo por tema via CSS (.dark), usado no sidebar/Home/TV; caminhos da logo só em brandAssets.ts", () => {
  const logo = fonte("src/features/theme/ThemeLogo.tsx");
  assert.ok(logo.includes('"hidden dark:block"') && logo.includes("dark:hidden"));
  for (const arq of ["src/components/Layout.tsx", "src/pages/Dashboard.tsx", "src/features/tv/TvPlayerPage.tsx"]) {
    assert.ok(fonte(arq).includes("<ThemeLogo"), arq);
  }
  for (const arq of ["src/features/login/loginMedia.ts", "src/features/production/analytics/GestaoVistaJamilePage.tsx", "src/components/ui/logo.tsx"]) {
    assert.equal(/na-30-anos-|logo-na-30anos/.test(fonte(arq)), false, arq);
  }
});
