import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { charmander, contexto, criarJogo, ESPECIES } from './fixtures.mjs';

const require = createRequire(import.meta.url);
const E = require('../../src/IdleDeck.App/Modules/Hunt/hunt-engine.js');

const perto = (a, b, eps = 1e-9) => assert.ok(Math.abs(a - b) <= eps, `${a} ≉ ${b}`);
const linha = (r, slug) => r.linhas.find((l) => l.slug === slug);

test('amplificar segue a Pokepédia (×1,5 na hunt)', () => {
  perto(E.amplificar(2, 1.5), 2.5);
  perto(E.amplificar(4, 1.5), 5.5);
  perto(E.amplificar(0.5, 1.5), 1 / 3);
  assert.equal(E.amplificar(0, 1.5), 0);
  assert.equal(E.amplificar(1, 1.5), 1);
});

test('dano usa a fórmula do jogo com aleatório médio 0,925', () => {
  // ((2×10/5+2)×40×20/20)/50+2 = 6,8 → ×1,5 STAB ×0,925
  perto(E.dano(10, 40, 20, 20, true, 1), 6.8 * 1.5 * 0.925);
});

test('HP do selvagem: máx(24, arred(hp × 12)) × multHp', () => {
  // hp = (50 + 2×16) × 10/100 = 8,2 → arred(98,4) = 98 → ×5
  assert.equal(E.hpSelvagem({ baseHp: 50 }, 10, 16, 5), 490);
  assert.equal(E.hpSelvagem({ baseHp: 1 }, 1, 1, 5), 24 * 5);
});

test('intervalo e Investida descontam 10 ms por IV de SPD, com piso de 400 ms', () => {
  assert.equal(E.comDescontoDoIv(900, 16), 740);
  assert.equal(E.comDescontoDoIv(2000, 32), 1680);
  assert.equal(E.comDescontoDoIv(500, 32), 400);
});

test('herói: só golpes liberados no nível, e o Z-Move entra como golpe extra', () => {
  const h = E.montarHeroi(charmander({ tmElemental: 'FIRE' }), ESPECIES.get(4), criarJogo());
  assert.deepEqual(h.golpes.map((g) => g.nome), ['Ember', 'Z-Move FIRE']);
  assert.equal(h.golpes[1].power, 2800);
  assert.equal(h.golpes[1].recargaS, 60);
  assert.equal(h.hpMax, Math.round(80 * 12));
});

test('hunt onde o selvagem é imune a tudo é inviável e vai para o fim', () => {
  // Charmander só tem golpes de FIRE e Investida FIRE; um alvo com efetividade 0 a FIRE:
  const ctx = contexto({ jogo: { efetividade: (atq, d1) => (d1 === 'GHOST' ? 0 : 1) } });
  const r = E.analisar(charmander(), ctx);
  assert.equal(linha(r, 'cemiterio').inviavel, true);
  const ordem = r.porXp.map((l) => l.inviavel);
  assert.deepEqual(ordem, [...ordem].sort((x, y) => x - y)); // inviáveis sempre depois das viáveis
  assert.equal(r.porXp[0].inviavel, false);
});

test('hunt acima do nível do treinador vem bloqueada', () => {
  const r = E.analisar(charmander(), contexto({ eu: { level: 100 } }));
  assert.equal(linha(r, 'alto').liberada, false);
  assert.equal(linha(r, 'mato').liberada, true);
});

test('ritmo: ciclo = luta + intervalo + deslocamento/bike + espera média; ondas somam a pausa', () => {
  // Um golpe que mata qualquer coisa: luta 0.
  const jogo = criarJogo({ golpeTmElemental: () => ({ name: 'Mata', type: 'FIRE', power: 1e9, category: 'SPECIAL', cooldownMs: 1, learnLevel: 1 }) });
  const ctx = contexto({ hunts: [{ slug: 'mato', nome: 'Mato', nivel: 10, especies: [{ pokeId: 1, pontos: 10 }] }] });
  ctx.jogo = jogo;
  const r = E.analisar(charmander({ tmElemental: 'FIRE' }), ctx);
  const l = linha(r, 'mato');
  const ciclo = 0.74 + (0.6 + 2.0); // intervalo com IV 16 + deslocamento + espera média
  perto(l.cicloS, ciclo, 1e-6);
  perto(l.killsH, (10 / (10 * ciclo + 2.6)) * 3600, 1e-6);
});

test('bicicleta divide só o deslocamento', () => {
  const jogo = criarJogo({ golpeTmElemental: () => ({ name: 'Mata', type: 'FIRE', power: 1e9, category: 'SPECIAL', cooldownMs: 1, learnLevel: 1 }) });
  const ctx = contexto({ eu: { level: 100, bicicleta: { equipadaRar: 'lendaria' } } });
  ctx.jogo = jogo;
  ctx.consts.bicicletas = { raridades: [{ id: 'lendaria', velocidade: 1 }] };
  const r = E.analisar(charmander({ tmElemental: 'FIRE' }), ctx);
  perto(linha(r, 'mato').cicloS, 0.74 + 0.3 + 2.0, 1e-6);
  assert.equal(r.bike.fator, 2);
});

test('TM AoE divide o ciclo pelos alvos por golpe (padrão 2), sem mexer na pausa entre ondas', () => {
  const jogo = criarJogo({ golpeTmElemental: () => ({ name: 'Mata', type: 'FIRE', power: 1e9, category: 'SPECIAL', cooldownMs: 1, learnLevel: 1 }) });
  const ctx = contexto({});
  ctx.jogo = jogo;
  const r = E.analisar(charmander({ tmElemental: 'FIRE', tmAoe: true }), ctx);
  const ciclo = 0.74 + 2.6;
  perto(linha(r, 'mato').killsH, (10 / (10 * ciclo / 2 + 2.6)) * 3600, 1e-6);
  assert.equal(r.aoeAlvos, 2);
});

test('bônus de XP: usa eu.bonusXp.partes ativas quando o servidor manda', () => {
  const eu = { level: 100, bonusXp: { partes: [{ nome: 'VIP', pk: 1.5, tr: 1.5 }, { nome: 'Velho', pk: 9, tr: 9, ate: 1 }] } };
  const r = E.analisar(charmander(), contexto({ eu }));
  perto(r.bonus.pokemon, 1.5);
  const l = linha(r, 'mato');
  perto(l.xpPokemonH, l.killsH * 100 * 1.5, 1e-6);
});

test('poções: dano/h ÷ cura efetiva da 1ª poção com estoque; gold líquido desconta', () => {
  const eu = {
    level: 100,
    automation: { autoPotion: true, potionIds: [10, 11], hpLimiar: 30 },
    items: [{ id: 10, qtd: 0 }, { id: 11, qtd: 5 }],
  };
  const r = E.analisar(charmander(), contexto({ eu }));
  const l = linha(r, 'snorlax');
  assert.ok(l.danoH > 0);
  // Golden Potion (a 10 está sem estoque): cura 50% do HP, mas no limiar de 30% só cabe 70%.
  const cura = Math.min(0.5 * 960, 0.7 * 960);
  perto(l.pocoesH, l.danoH / cura, 1e-6);
  perto(l.custoH.pocoes, l.pocoesH * 5000, 1e-6);
  perto(l.liquidoH, l.ouroH - l.custoH.pocoes - l.custoH.revives - l.custoH.bolas, 1e-6);
});

test('risco: um golpe mata quando o maior golpe tira o HP cheio', () => {
  const fraco = charmander({ stats: { hp: 1, atk: 60, def: 1, spAtk: 70, spDef: 1, speed: 70 } });
  const r = E.analisar(fraco, contexto({ eu: { level: 100, automation: { autoPotion: true, potionIds: [10], hpLimiar: 30 }, items: [{ id: 10, qtd: 9 }] } }));
  assert.equal(linha(r, 'snorlax').risco, 'umHit');
});

test('sem poção: mortes/h = dano/h ÷ HP; com auto-revive, cada morte custa um revive', () => {
  const eu = { level: 100, automation: { autoPotion: false, autoRevive: true, reviveIds: [20] }, items: [{ id: 20, qtd: 3 }] };
  const l = linha(E.analisar(charmander(), contexto({ eu })), 'snorlax');
  perto(l.mortesH, l.danoH / 960, 1e-6);
  perto(l.custoH.revives, l.mortesH * 1000, 1e-6);
});

test('auto-bola: uma bola por abate', () => {
  const eu = { level: 100, automation: { autoBallSemParar: true, ballIds: [1] }, balls: { 1: 50 } };
  const l = linha(E.analisar(charmander(), contexto({ eu })), 'mato');
  perto(l.custoH.bolas, l.killsH * 200, 1e-6);
});

test('ranking por ouro usa o gold líquido', () => {
  const r = E.analisar(charmander(), contexto({}));
  const valores = r.porOuro.filter((l) => !l.inviavel).map((l) => l.liquidoH);
  assert.deepEqual(valores, [...valores].sort((a, b) => b - a));
});
