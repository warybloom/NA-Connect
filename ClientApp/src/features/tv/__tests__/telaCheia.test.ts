import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { alternarTelaCheia, elementoEmTelaCheia, EVENTOS_TELA_CHEIA, sairDaTelaCheia, telaCheiaSuportada, type DocumentoTelaCheia, type ElementoTelaCheia } from "../telaCheia";

/** Navegador falso: requestFullscreen/exitFullscreen alteram fullscreenElement e disparam fullscreenchange. */
function navegador(opcoes: { rejeita?: boolean; semApi?: boolean; desabilitado?: boolean } = {}) {
  const ouvintes = new Map<string, Set<() => void>>();
  const chamadas: string[] = [];
  const disparar = () => (ouvintes.get("fullscreenchange") ?? new Set()).forEach((f) => f());
  const doc: DocumentoTelaCheia & { fullscreenElement: Element | null } = {
    fullscreenElement: null,
    fullscreenEnabled: !opcoes.desabilitado,
    exitFullscreen: async () => { chamadas.push("exit"); doc.fullscreenElement = null; disparar(); },
    addEventListener: (t, f) => { if (!ouvintes.has(t)) ouvintes.set(t, new Set()); ouvintes.get(t)!.add(f); },
    removeEventListener: (t, f) => { ouvintes.get(t)?.delete(f); },
  };
  const palco: ElementoTelaCheia & { nome: string } = {
    nome: "palco-da-apresentacao",
    requestFullscreen: opcoes.semApi ? undefined : async () => {
      chamadas.push("request:palco");
      if (opcoes.rejeita) throw new TypeError("Permissions check failed");
      doc.fullscreenElement = palco as unknown as Element;
      disparar();
    },
  };
  return { doc, palco, chamadas, ouvintes, esc: () => { doc.fullscreenElement = null; disparar(); } };
}

test("I/J: primeiro clique chama requestFullscreen no CONTAINER da apresentação e fullscreenElement passa a ser ele", async () => {
  const n = navegador();
  assert.equal(await alternarTelaCheia(n.palco, n.doc), "entrou");
  assert.deepEqual(n.chamadas, ["request:palco"]);
  assert.equal(elementoEmTelaCheia(n.doc), n.palco as unknown as Element);
});

test("K: segundo clique chama document.exitFullscreen", async () => {
  const n = navegador();
  await alternarTelaCheia(n.palco, n.doc);
  assert.equal(await alternarTelaCheia(n.palco, n.doc), "saiu");
  assert.deepEqual(n.chamadas, ["request:palco", "exit"]);
  assert.equal(elementoEmTelaCheia(n.doc), null);
});

test("L: ESC/fullscreenchange externo — estado é derivado do navegador (o hook escuta os eventos)", async () => {
  const n = navegador();
  let estado = false;
  const sincronizar = () => { estado = !!elementoEmTelaCheia(n.doc); };
  EVENTOS_TELA_CHEIA.forEach((t) => n.doc.addEventListener(t, sincronizar));
  await alternarTelaCheia(n.palco, n.doc);
  assert.equal(estado, true);
  n.esc(); // usuário pressiona ESC: o navegador sai sozinho e dispara fullscreenchange
  assert.equal(estado, false);
  assert.ok(EVENTOS_TELA_CHEIA.includes("fullscreenchange"));
  // Depois do ESC, o próximo clique ENTRA de novo (não tenta sair de algo que já saiu).
  assert.equal(await alternarTelaCheia(n.palco, n.doc), "entrou");
});

test("O: rejeição ou falta de suporte não quebram (sem exceção)", async () => {
  const avisos: unknown[] = []; const warn = console.warn; console.warn = (...a) => avisos.push(a);
  try {
    const rejeita = navegador({ rejeita: true });
    assert.equal(await alternarTelaCheia(rejeita.palco, rejeita.doc), "falhou");
    assert.equal(elementoEmTelaCheia(rejeita.doc), null);
    assert.equal(avisos.length, 1);

    const semApi = navegador({ semApi: true });
    assert.equal(telaCheiaSuportada(semApi.doc, semApi.palco), false);
    assert.equal(await alternarTelaCheia(semApi.palco, semApi.doc), "sem-suporte");

    const desabilitado = navegador({ desabilitado: true });
    assert.equal(await alternarTelaCheia(desabilitado.palco, desabilitado.doc), "sem-suporte");
    assert.equal(await alternarTelaCheia(null, navegador().doc), "sem-suporte");
  } finally { console.warn = warn; }
});

test("Safari: usa webkitRequestFullscreen/webkitExitFullscreen quando o padrão não existe", async () => {
  const chamadas: string[] = [];
  const doc: DocumentoTelaCheia = {
    webkitFullscreenElement: null,
    webkitExitFullscreen: () => { chamadas.push("webkitExit"); doc.webkitFullscreenElement = null; },
    addEventListener: () => undefined, removeEventListener: () => undefined,
  };
  const palco: ElementoTelaCheia = { webkitRequestFullscreen: () => { chamadas.push("webkitRequest"); doc.webkitFullscreenElement = {} as Element; } };
  assert.equal(await alternarTelaCheia(palco, doc), "entrou");
  await sairDaTelaCheia(doc);
  assert.deepEqual(chamadas, ["webkitRequest", "webkitExit"]);
});

const fonte = (relativo: string) => {
  for (const base of [process.cwd(), path.resolve(process.cwd(), "ClientApp")]) {
    try { return readFileSync(path.resolve(base, relativo), "utf8"); } catch { /* próximo */ }
  }
  throw new Error(relativo);
};

test("M/N: player pede tela cheia do container que contém controles e conteúdo; slide/estado não remontam", () => {
  const player = fonte("src/features/tv/TvPlayerPage.tsx");
  assert.ok(!player.includes("documentElement.requestFullscreen"), "não usa mais o documento inteiro");
  assert.match(player, /useTelaCheia\(palco\)/);
  // O MESMO container (Tela palco) envolve header (controles) e <main> (slide): entrar/sair não troca de componente.
  const inicio = player.indexOf("<Tela palco={palco}>");
  assert.ok(inicio > 0);
  const fim = player.indexOf("</Tela>", inicio);
  const trecho = player.slice(inicio, fim);
  for (const controle of ["irPara(-1)", "setPausado", "irPara(1)", "tv-tela-cheia", "onClick={sair}", "<ConteudoTv"]) assert.ok(trecho.includes(controle), controle);
  // Índice/pausa/decorrido vivem no componente do player (não dependem do modo tela cheia).
  assert.match(player, /const \[indice, setIndice\] = useState\(0\)/);
  assert.match(player, /\[&:fullscreen\]:h-full/);
});
