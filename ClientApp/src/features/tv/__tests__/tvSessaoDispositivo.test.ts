import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { classificarFalhaTv, mensagemFalhaTv, TV_FALHAS_TOLERADAS } from '../portal/portalTransport';
import { CHAVE_RETOMADA_TV, lerRetomadaTv, limparRetomadaTv, retomadaValida, salvarRetomadaTv } from '../portal/tvRetomada';

const fonte = (arquivo: string) => readFileSync(arquivo, 'utf-8');

test('revalidação: 401/403/404/409 encerram na hora; rede e 5xx são transitórios', () => {
  for (const status of [401, 403, 404, 409]) assert.equal(classificarFalhaTv(status), 'encerrado');
  for (const status of [null, 500, 502, 503, 504, 0]) assert.equal(classificarFalhaTv(status), 'transitorio');
  assert.match(mensagemFalhaTv(401), /Entre novamente/);
  assert.match(mensagemFalhaTv(403), /revogado/);
  assert.match(mensagemFalhaTv(503), /Reconectando/);
  // Tolerância curta: ~1 min a 15 s por tentativa; nunca "segura para sempre".
  assert.ok(TV_FALHAS_TOLERADAS >= 2 && TV_FALHAS_TOLERADAS <= 6);
});

test('player do portal: transitório não derruba; encerrado limpa a tela; sucesso recupera sozinho', () => {
  const player = fonte('src/features/tv/TvPlayerPage.tsx');
  assert.match(player, /classificarFalhaTv\(status\) === "transitorio" && \+\+falhas < TV_FALHAS_TOLERADAS\) return;/);
  assert.match(player, /setErro\(mensagemFalhaTv\(status\)\); setApresentacoes\(\[\]\);/);
  assert.match(player, /falhas = 0; setErro\(null\); setApresentacoes\(\[p\]\);/);
});

test('seletor: nova tentativa automática exceto 401; dispositivo registrado SEM clique', () => {
  const gate = fonte('src/features/tv/portal/TvPortalGate.tsx');
  assert.match(gate, /if \(e\?\.status !== 401\) retry = window\.setTimeout\(check, 15000\);/);
  assert.match(gate, /method: manter \? "POST" : "DELETE"/);
  // Automático, uma vez por carga, só quando o servidor diz que a conta pode (available) e o cookie não foi recusado.
  assert.match(gate, /status\.available && !status\.registered && !status\.cookieRejected && !autoRegistro\.current/);
  assert.match(gate, /await alterarDispositivo\(true\);/);
  assert.doesNotMatch(gate, /Manter esta TV conectada/);
});

test('player: revalida na hora ao voltar de suspensão, rede ou cache do navegador', () => {
  const player = fonte('src/features/tv/TvPlayerPage.tsx');
  assert.match(player, /document\.addEventListener\("visibilitychange", aoRetomar\)/);
  assert.match(player, /window\.addEventListener\("online", aoRetomar\)/);
  assert.match(player, /window\.addEventListener\("pageshow", aoRetomar\)/);
  assert.match(player, /document\.removeEventListener\("visibilitychange", aoRetomar\)/);
});

test('retomada: volta à mesma apresentação após recarga só se ainda autorizada e apta', () => {
  const mem = new Map<string, string>();
  const store = { getItem: (k: string) => mem.get(k) ?? null, setItem: (k: string, v: string) => void mem.set(k, v), removeItem: (k: string) => void mem.delete(k) };
  const escolhas = [{ id: 'a', supported: true }, { id: 'b', supported: false }];
  salvarRetomadaTv({ presentationId: 'a', seconds: 45 }, store);
  assert.deepEqual(retomadaValida(lerRetomadaTv(store), escolhas), { presentationId: 'a', seconds: 45 });
  // Guarda só id + intervalo: nada de credencial.
  assert.deepEqual(Object.keys(JSON.parse(mem.get(CHAVE_RETOMADA_TV)!)).sort(), ['presentationId', 'seconds']);
  assert.equal(retomadaValida({ presentationId: 'b', seconds: 45 }, escolhas), null); // NÃO APTA
  assert.equal(retomadaValida({ presentationId: 'x', seconds: 45 }, escolhas), null); // atribuição removida
  assert.equal(retomadaValida({ presentationId: 'a', seconds: 5 }, escolhas), null);  // intervalo fora de 10–300
  assert.equal(retomadaValida('lixo', escolhas), null);
  mem.set(CHAVE_RETOMADA_TV, '{quebrado'); assert.equal(lerRetomadaTv(store), null);
  limparRetomadaTv(store); assert.equal(lerRetomadaTv(store), null);
  assert.equal(lerRetomadaTv(null), null); // sem armazenamento: só não retoma
  const gate = fonte('src/features/tv/portal/TvPortalGate.tsx');
  assert.match(gate, /onExit: \(\) => \{ limparRetomadaTv\(\); setPlaying\(false\); \}/); // "Voltar" na TV não retoma
});

test('nenhum token, senha ou cookie em JS/localStorage no portal TV', () => {
  for (const arquivo of ['src/features/tv/portal/TvPortalGate.tsx', 'src/features/tv/portal/TvDeviceSessions.tsx',
    'src/features/tv/portal/portalTransport.ts', 'src/features/tv/portal/TvPortalAssignments.tsx', 'src/features/tv/TvPlayerPage.tsx']) {
    const src = fonte(arquivo);
    assert.doesNotMatch(src, /localStorage|sessionStorage|indexedDB|document\.cookie/, arquivo);
    assert.doesNotMatch(src, /password|senha\s*[:=]|token\s*[:=]/i, arquivo);
  }
});

test('admin revoga pela API dedicada; a TV não enxerga a lista', () => {
  const admin = fonte('src/features/tv/portal/TvDeviceSessions.tsx');
  assert.match(admin, /\/api\/tv-portal\/devices\/\$\{encodeURIComponent\(d\.id\)\}`, \{ method: "DELETE"/);
  assert.doesNotMatch(fonte('src/features/tv/portal/TvPortalGate.tsx'), /\/api\/tv-portal\/devices/);
});
