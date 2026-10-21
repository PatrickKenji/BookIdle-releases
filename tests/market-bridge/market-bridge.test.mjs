// Teste da ponte do sniper com o WebSocket do jogo (roda com: node --test tests/market-bridge)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../src/IdleDeck.App/Scripts/market-bridge.js', import.meta.url), 'utf8');

class FakeSocket {
  constructor(url) {
    this.url = url;
    this.readyState = 1;
    this.sent = [];
    this.handlers = {};
  }
  addEventListener(type, fn) { (this.handlers[type] ||= []).push(fn); }
  send(text) { this.sent.push(text); }
  emit(type, event) { for (const fn of this.handlers[type] || []) fn(event); }
  receive(frame) { this.emit('message', { data: typeof frame === 'string' ? frame : JSON.stringify(frame) }); }
}
FakeSocket.prototype.CONNECTING = 0;

function load() {
  const posted = [];
  const window = { chrome: { webview: { postMessage: (m) => posted.push(JSON.parse(m)) } }, WebSocket: FakeSocket };
  vm.runInNewContext(source, { window, JSON, Object, String, Number, console });
  // o jogo abre o soquete DEPOIS do script, pela classe que o script instalou
  const open = (url = 'wss://pokeidle.io/') => new window.WebSocket(url);
  return { window, posted, open, frames: () => posted.map((p) => JSON.parse(p.data.frame)) };
}

test('repassa as respostas do mercado como vieram', () => {
  const page = load();
  const ws = page.open();
  const raw = JSON.stringify({ t: 'market', aba: 'itens', resumo: { 23: { anuncios: 1, minGold: 5, minOrb: null } } });
  ws.receive(raw);
  assert.equal(page.posted.length, 1);
  assert.equal(page.posted[0].module, 'sniper');
  assert.equal(page.posted[0].data.frame, raw);
});

test('de um quadro de batalha leva só os eventos do mercado', () => {
  const page = load();
  const ws = page.open();
  ws.receive({ t: 'batalha', ev: [{ k: 'ataque', dano: 5 }, { k: 'mktSorteio', fase: 'fim', id: 7, ok: true }, { k: 'marketComprado', id: 7, total: 10, moeda: 'gold', segredo: 'x' }] });
  const [frame] = page.frames();
  assert.equal(frame.t, 'batalha');
  assert.deepEqual(frame.ev.map((e) => e.k), ['mktSorteio', 'marketComprado']);
});

test('batalha sem mercado não é repassada', () => {
  const page = load();
  const ws = page.open();
  ws.receive({ t: 'batalha', ev: [{ k: 'ataque', dano: 5 }] });
  assert.equal(page.posted.length, 0);
});

test('do saldo leva só ouro, gemas e o relógio do servidor', () => {
  const page = load();
  const ws = page.open();
  ws.receive({ t: 'welcome', admin: false, estado: { nick: 'Treinador', gold: 123, orbs: 4, servidorAgora: 99, token: 'segredo', diamonds: 1 } });
  ws.receive({ t: 'estado', estado: { gold: 100, selvagem: { nome: 'x' } } });
  const frames = page.frames();
  assert.deepEqual(frames[0], { t: 'welcome', estado: { gold: 123, orbs: 4, servidorAgora: 99 } });
  assert.deepEqual(frames[1], { t: 'estado', estado: { gold: 100 } });
  assert.ok(!JSON.stringify(page.posted).includes('segredo'));
});

test('não repassa campo, chat nem o hello', () => {
  const page = load();
  const ws = page.open();
  ws.receive({ t: 'campo', seq: 1 });
  ws.receive({ t: 'chat', texto: 'oi' });
  ws.receive({ t: 'hello', token: 'segredo' });
  ws.receive({ t: 'estado', estado: { selvagem: null } }); // sem ouro, gemas nem relógio
  ws.receive('lixo');
  ws.receive('{"t":');
  assert.equal(page.posted.length, 0);
});

test('send devolve false sem soquete ou com o soquete fechado', () => {
  const page = load();
  assert.equal(page.window.__biSniper.send('{"t":"market.itens"}'), false);
  const ws = page.open();
  ws.readyState = 3;
  assert.equal(page.window.__biSniper.send('{"t":"market.itens"}'), false);
  assert.deepEqual(ws.sent, []);
});

test('send manda exatamente o texto pelo soquete do jogo', () => {
  const page = load();
  const ws = page.open();
  const text = '{"t":"market.item","itemId":70060,"moeda":"orb"}';
  assert.equal(page.window.__biSniper.send(text), true);
  assert.deepEqual(ws.sent, [text]);
});

test('send só aceita pedidos do mercado', () => {
  const page = load();
  const ws = page.open();
  assert.equal(page.window.__biSniper.send('{"t":"chat","texto":"oi"}'), false);
  assert.equal(page.window.__biSniper.send('{"t":"hello","token":"x"}'), false);
  assert.equal(page.window.__biSniper.send('nada'), false);
  assert.equal(page.window.__biSniper.send('{"t":123}'), false);
  assert.deepEqual(ws.sent, []);
});

test('ignora soquetes que não são do jogo', () => {
  const page = load();
  const outro = page.open('wss://outro.exemplo/');
  assert.equal(page.window.__biSniper.send('{"t":"market.itens"}'), false);
  outro.receive({ t: 'market', aba: 'itens', resumo: {} });
  assert.equal(page.posted.length, 0);
});

test('usa o soquete mais novo do jogo e solta o que fechou', () => {
  const page = load();
  const velho = page.open();
  const novo = page.open();
  page.window.__biSniper.send('{"t":"market.itens"}');
  assert.equal(velho.sent.length, 0);
  assert.equal(novo.sent.length, 1);
  novo.emit('close', {});
  assert.equal(page.window.__biSniper.send('{"t":"market.itens"}'), false);
});

test('do welcome leva também o catálogo do mercado, só com id, nome e categoria', () => {
  const page = load();
  const ws = page.open();
  ws.receive({
    t: 'welcome',
    estado: { gold: 5, orbs: 1, token: 'segredo' },
    itensNossos: [{ id: 9, name: 'Inventário', secret: 'x' }],
    mercado: { catalogo: [{ id: 70041, name: 'Uncommon House', category: 'casa', icon: '/img/a.png', npcPrice: 5 }, { id: 70060, nome: 'Founder Box', categoria: 'caixa' }, { semId: true }] },
  });
  const [frame] = page.frames();
  assert.deepEqual(frame, {
    t: 'welcome',
    estado: { gold: 5, orbs: 1 },
    mercado: { catalogo: [{ id: 70041, name: 'Uncommon House', category: 'casa' }, { id: 70060, name: 'Founder Box', category: 'caixa' }] },
  });
  assert.ok(!JSON.stringify(page.posted).includes('segredo'));
  assert.ok(!JSON.stringify(page.posted).includes('Inventário'));
});

test('welcome só com catálogo (sem saldo) também é repassado', () => {
  const page = load();
  const ws = page.open();
  ws.receive({ t: 'welcome', mercado: { catalogo: [{ id: 1, name: 'Air Tank' }] } });
  assert.deepEqual(page.frames(), [{ t: 'welcome', mercado: { catalogo: [{ id: 1, name: 'Air Tank', category: '' }] } }]);
});
