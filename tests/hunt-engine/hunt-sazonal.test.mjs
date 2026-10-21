// O dia sazonal (jogo, 20/10/2026): na hunt do mapa, todo pokémon do tipo do dia fica 2× mais forte (ataque, defesa e vida).
// O jogo transforma isso no dano: ×2 quando quem bate é do tipo, ×¼ quando quem apanha é (shared/dia-sazonal.mjs).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { charmander, contexto } from './fixtures.mjs';

const require = createRequire(import.meta.url);
const E = require('../../src/IdleDeck.App/Modules/Hunt/hunt-engine.js');

/** A mesma conta do `multDanoSazonal` do jogo (cópia literal da regra, para o teste não depender de rede). */
const multDanoSazonal = (tipo, tiposAtacante, tiposDefensor) => {
  if (!tipo) return 1;
  const ataca = tiposAtacante?.includes(tipo) ? 2 : 1;
  const apanha = tiposDefensor?.includes(tipo) ? 4 : 1;
  return ataca / apanha;
};

const HUNTS = [
  { slug: 'mato', nome: 'Mato', nivel: 10, especies: [{ pokeId: 1, pontos: 10 }] },   // Bulbasaur, GRASS
  { slug: 'fogo', nome: 'Vulcão', nivel: 10, especies: [{ pokeId: 4, pontos: 10 }] },  // Charmander, FIRE
];

const analisar = (diaSazonal, over = {}) => {
  const ctx = { ...contexto({ hunts: HUNTS, jogo: { multDanoSazonal, ...(over.jogo ?? {}) } }), diaSazonal };
  const r = E.analisar(charmander(), ctx);
  return Object.fromEntries(r.linhas.map((l) => [l.slug, l]));
};

const perto = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) <= eps, `${a} ≉ ${b}`);

test('sem dia sazonal nada muda (nem com o módulo do jogo carregado)', () => {
  const base = analisar(null);
  const outroTipo = analisar('WATER'); // nem o herói (FIRE) nem os selvagens (GRASS, FIRE) são do tipo
  assert.deepEqual(outroTipo, base);
});

test('o herói é do tipo do dia: bate 2× mais forte e apanha ¼ do selvagem comum', () => {
  const base = analisar(null);
  const dia = analisar('FIRE');
  // contra o Bulbasaur (GRASS): ataca ×2, o selvagem não é do tipo ⇒ o dano que ele causa cai a ¼ (herói defende ×4)
  perto(dia.mato.maiorGolpePct, base.mato.maiorGolpePct / 4);
  assert.ok(dia.mato.lutaMediaS < base.mato.lutaMediaS, 'a luta fica mais curta');
  assert.ok(dia.mato.killsH > base.mato.killsH, 'mais abates por hora');
});

test('o selvagem é do tipo do dia: ele bate 2× mais forte e apanha ¼', () => {
  const base = analisar(null);
  const dia = analisar('GRASS'); // o Bulbasaur é GRASS, o herói não
  perto(dia.mato.maiorGolpePct, base.mato.maiorGolpePct * 2);
  assert.ok(dia.mato.lutaMediaS > base.mato.lutaMediaS, 'a luta fica mais longa');
  assert.ok(dia.mato.killsH < base.mato.killsH);
});

test('os dois do tipo do dia: cada um bate 2× e apanha ¼, o saldo é ×½ para os dois', () => {
  const base = analisar(null);
  const dia = analisar('FIRE'); // herói Charmander (FIRE) contra Charmander selvagem (FIRE)
  perto(dia.fogo.maiorGolpePct, base.fogo.maiorGolpePct / 2);
  assert.ok(dia.fogo.lutaMediaS > base.fogo.lutaMediaS, 'o herói também bate pela metade');
});

test('um golpe que acertava nunca cai abaixo de 1 de dano (o ¼ não zera golpe)', () => {
  const dia = analisar('FIRE', { jogo: { multDanoSazonal: () => 1e-9 } });
  assert.ok(dia.mato.maiorGolpePct > 0);
});

test('jogo antigo, sem o módulo do dia sazonal, é ignorado sem erro', () => {
  const ctx = { ...contexto({ hunts: HUNTS }), diaSazonal: 'FIRE' }; // sem jogo.multDanoSazonal
  const r = E.analisar(charmander(), ctx);
  assert.equal(r.linhas.length, 2);
  assert.equal(r.diaSazonal, 'FIRE'); // o relatório ainda diz qual é o tipo do dia
  assert.equal(r.heroiDoDia, true);
});

test('o relatório diz o tipo do dia e se o herói é dele', () => {
  const r = E.analisar(charmander(), { ...contexto({ hunts: HUNTS, jogo: { multDanoSazonal } }), diaSazonal: 'WATER' });
  assert.equal(r.diaSazonal, 'WATER');
  assert.equal(r.heroiDoDia, false);
  const sem = E.analisar(charmander(), contexto({ hunts: HUNTS }));
  assert.equal(sem.diaSazonal, null);
  assert.equal(sem.heroiDoDia, false);
});
