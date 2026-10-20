import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { classificarFalhaTv, mensagemFalhaTv, TV_FALHAS_TOLERADAS } from '../portal/portalTransport';

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

test('seletor: nova tentativa automática exceto 401; opt-in explícito do dispositivo', () => {
  const gate = fonte('src/features/tv/portal/TvPortalGate.tsx');
  assert.match(gate, /if \(e\?\.status !== 401\) retry = window\.setTimeout\(check, 15000\);/);
  assert.match(gate, /method: manter \? "POST" : "DELETE"/);
  assert.match(gate, /Manter esta TV conectada/);
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
