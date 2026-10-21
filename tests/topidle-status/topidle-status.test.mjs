// Teste do script que lê o status do voto do TopIdle da página do jogo (roda com: node --test tests/topidle-status/topidle-status.test.mjs)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../src/IdleDeck.App/Scripts/topidle-status.js', import.meta.url), 'utf8');

function load({ hostname = 'pokeidle.io', responses = {} } = {}) {
  const posted = [];
  const calls = [];
  const originalFetch = async (input, init) => {
    const url = typeof input === 'string' ? input : input.url;
    calls.push({ url, init });
    const body = responses[url];
    return {
      ok: true,
      status: 200,
      clone() { return this; },
      json: async () => { if (body === undefined) throw new Error('sem corpo'); return body; },
    };
  };
  const window = { chrome: { webview: { postMessage: (m) => posted.push(m) } }, fetch: originalFetch };
  vm.runInNewContext(source, { window, location: { hostname }, console });
  return { window, posted, calls };
}

const settle = () => new Promise((r) => setTimeout(r, 10));

test('repassa só os dois horários do status do voto', async () => {
  const page = load({ responses: { 'https://pokeidle.io/topidle/me': { votos: 4, ultimoVotoEm: 111, liberaEm: 222, identificador: 'Fulano', url: 'https://x', token: 'segredo' } } });
  const res = await page.window.fetch('https://pokeidle.io/topidle/me', { method: 'POST', body: '{"token":"segredo"}' });
  await settle();
  assert.equal(res.status, 200);
  assert.equal(page.posted.length, 1);
  assert.equal(page.posted[0], 'topidle:status:{"ultimoVotoEm":111,"liberaEm":222}');
  assert.ok(!page.posted[0].includes('segredo') && !page.posted[0].includes('Fulano'));
});

test('devolve a resposta ao jogo sem mexer e repassa o pedido original', async () => {
  const page = load({ responses: { '/topidle/me': { ultimoVotoEm: 1, liberaEm: 2 } } });
  const res = await page.window.fetch('/topidle/me', { method: 'POST', body: 'abc' });
  assert.deepEqual(await res.json(), { ultimoVotoEm: 1, liberaEm: 2 }); // o jogo ainda consegue ler o corpo
  assert.deepEqual(page.calls[0], { url: '/topidle/me', init: { method: 'POST', body: 'abc' } });
});

test('ignora outras chamadas', async () => {
  const page = load({ responses: { 'https://pokeidle.io/saude': { ok: true } } });
  await page.window.fetch('https://pokeidle.io/saude');
  await settle();
  assert.equal(page.posted.length, 0);
});

test('resposta sem corpo ou com formato estranho não quebra nada nem repassa lixo', async () => {
  const page = load({ responses: { '/topidle/me': { votos: 0 } } });
  await page.window.fetch('/topidle/me');
  await settle();
  assert.equal(page.posted.length, 0); // sem horários: nada a avisar

  const semCorpo = load();
  const res = await semCorpo.window.fetch('/topidle/me'); // o json() falha: o jogo recebe a resposta normalmente
  await settle();
  assert.equal(res.status, 200);
  assert.equal(semCorpo.posted.length, 0);
});

test('não faz nada fora do pokeidle.io', async () => {
  const page = load({ hostname: 'topidle.com', responses: { '/topidle/me': { ultimoVotoEm: 1, liberaEm: 2 } } });
  await page.window.fetch('/topidle/me');
  await settle();
  assert.equal(page.posted.length, 0);
});
