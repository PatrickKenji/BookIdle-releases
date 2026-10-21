// Teste do script que detecta o voto confirmado no topidle.com (roda com: node --test tests/topidle-vote)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../src/IdleDeck.App/Scripts/topidle-vote.js', import.meta.url), 'utf8');

function run({ hostname = 'topidle.com', initialText = '' }) {
  const messages = [];
  const timers = [];
  let observerCallback = null;
  const body = { innerText: initialText };
  const context = {
    location: { hostname },
    document: { readyState: 'complete', body, documentElement: {}, addEventListener() {} },
    MutationObserver: class { constructor(cb) { observerCallback = cb; } observe() {} },
    window: { chrome: { webview: { postMessage: (m) => messages.push(m) } } },
    setTimeout: (fn) => { timers.push(fn); return timers.length; },
  };
  context.window.document = context.document;
  vm.runInNewContext(source, context);
  const flush = () => { while (timers.length) timers.shift()(); };
  return { messages, body, flush, mutate: () => observerCallback && observerCallback() };
}

test('avisa uma vez quando "Voto validado e contabilizado" aparece depois do carregamento', () => {
  const page = run({ initialText: 'Votar agora' });
  page.flush(); // primeira leitura (linha de base)
  page.body.innerText = 'Ação concluída. Voto validado e contabilizado.';
  page.mutate();
  page.flush();
  page.mutate();
  page.flush();
  assert.deepEqual(page.messages, ['topidle:voted']);
});

test('texto que já estava na página ao carregar não conta como voto', () => {
  const page = run({ initialText: 'Voto validado e contabilizado (texto fixo da página)' });
  page.flush();
  page.mutate();
  page.flush();
  assert.deepEqual(page.messages, []);
});

test('não faz nada fora do topidle.com', () => {
  const page = run({ hostname: 'pokeidle.io' });
  page.flush();
  page.body.innerText = 'voto validado e contabilizado';
  page.mutate();
  page.flush();
  assert.deepEqual(page.messages, []);
});
