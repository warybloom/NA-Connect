import test from 'node:test';
import assert from 'node:assert/strict';
import { localTvInterval, setTvPortalScope, tvPortalUrl } from '../portal/portalTransport';

test('intervalo local aceita somente inteiros entre 10 e 300', () => {
  for (const value of [10, 30, 300]) assert.equal(localTvInterval(value), value);
  for (const value of [0, 9, 301, 600, -1, NaN, Infinity, 10.5, '30', null]) assert.equal(localTvInterval(value), null);
});
test('transporte normal fica intacto; portal vincula apresentação e índice, sem PUT', () => {
  setTvPortalScope(null);
  assert.equal(tvPortalUrl('/api/reports/gestao-vista?projectId=P'), '/api/reports/gestao-vista?projectId=P');
  setTvPortalScope({ presentation: 'tv-a', slide: 2 });
  const scoped = new URL(tvPortalUrl('/api/bi-chs001/ldp?projectId=P'), 'https://local');
  assert.equal(scoped.pathname, '/api/tv-portal/data/tv-a/2');
  assert.equal(scoped.searchParams.get('resource'), '/api/bi-chs001/ldp');
  assert.equal(scoped.searchParams.get('projectId'), 'P');
  assert.equal(tvPortalUrl('/api/tv-portal/presentations'), '/api/tv-portal/presentations');
  setTvPortalScope(null);
});
