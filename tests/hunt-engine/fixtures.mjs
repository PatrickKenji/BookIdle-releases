// Funções do jogo falsas e determinísticas para testar o motor sem rede.
export const TABELA = {
  FIRE: { GRASS: 2, WATER: 0.5 },
  WATER: { FIRE: 2 },
  NORMAL: { GHOST: 0 },
  GHOST: { NORMAL: 0 },
};

export function efetividade(atq, d1, d2, tabela) {
  const linha = tabela[atq] ?? {};
  return (linha[d1] ?? 1) * (d2 ? linha[d2] ?? 1 : 1);
}

export function criarJogo(over = {}) {
  return {
    cooldownGolpeComSpeed: (cd) => cd, // sem desconto nos testes
    golpeTmElemental: (tipo) => ({ name: `Z-Move ${tipo}`, type: tipo, power: 2800, category: 'SPECIAL', cooldownMs: 60000, learnLevel: 1 }),
    efetividade,
    xpPorDerrota: (hunt) => hunt.nivel * 10,
    ouroPorDerrotaHunt: (hunt) => hunt.nivel * 2,
    bonusLootPct: () => 0,
    multXpPokemon: () => 1,
    multXpTreinador: () => 1,
    MS_ONDA: 2600,
    MAX_MOBS: 16,
    MS_DESLOC_MS: 600,
    CD_MOVIMENTO_MEDIO_MS: 2000,
    ...over,
  };
}

export const ESPECIES = new Map([
  [1, { pokeId: 1, name: 'Bulbasaur', type1: 'GRASS', baseHp: 45, baseAtk: 49, baseDef: 49, baseSpAtk: 65, baseSpDef: 65, baseSpeed: 45,
    attacks: [{ name: 'Vine Whip', type: 'GRASS', power: 45, category: 'PHYSICAL', cooldownMs: 3000, learnLevel: 1 }] }],
  [4, { pokeId: 4, name: 'Charmander', type1: 'FIRE', baseHp: 39, baseAtk: 52, baseDef: 43, baseSpAtk: 60, baseSpDef: 50, baseSpeed: 65,
    attacks: [
      { name: 'Ember', type: 'FIRE', power: 40, category: 'SPECIAL', cooldownMs: 3000, learnLevel: 1 },
      { name: 'Flamethrower', type: 'FIRE', power: 90, category: 'SPECIAL', cooldownMs: 6000, learnLevel: 50 },
    ] }],
  [92, { pokeId: 92, name: 'Gastly', type1: 'GHOST', baseHp: 30, baseAtk: 35, baseDef: 30, baseSpAtk: 100, baseSpDef: 35, baseSpeed: 80,
    attacks: [{ name: 'Lick', type: 'GHOST', power: 30, category: 'PHYSICAL', cooldownMs: 3000, learnLevel: 1 }] }],
  [143, { pokeId: 143, name: 'Snorlax', type1: 'NORMAL', baseHp: 160, baseAtk: 110, baseDef: 65, baseSpAtk: 65, baseSpDef: 110, baseSpeed: 30,
    attacks: [{ name: 'Body Slam', type: 'NORMAL', power: 85, category: 'PHYSICAL', cooldownMs: 4000, learnLevel: 1 }] }],
]);

/** Charmander nível 30, só golpes de fogo (o de nível 50 ainda não liberou). */
export function charmander(over = {}) {
  return {
    id: 'pk1', speciesId: 4, nome: 'Charmander', level: 30, tipos: ['FIRE'],
    ivs: { speed: 16 }, stats: { hp: 80, atk: 60, def: 50, spAtk: 70, spDef: 55, speed: 70 },
    ...over,
  };
}

export const HUNTS = [
  { slug: 'mato', nome: 'Mato', nivel: 10, especies: [{ pokeId: 1, pontos: 10 }] },
  { slug: 'cemiterio', nome: 'Cemitério', nivel: 10, especies: [{ pokeId: 92, pontos: 10 }] },
  { slug: 'alto', nome: 'Montanha', nivel: 500, especies: [{ pokeId: 1, pontos: 10 }] },
  { slug: 'snorlax', nome: 'Snorlax', nivel: 60, especies: [{ pokeId: 143, pontos: 4 }] },
];

export const ITENS = new Map([
  [10, { id: 10, name: 'Potion', category: 'heal', healAmount: 50, npcPrice: 100 }],
  [11, { id: 11, name: 'Golden Potion', category: 'heal', healPct: 0.5, npcPrice: 5000 }],
  [20, { id: 20, name: 'Revive', category: 'revive', revivePct: 0.1, npcPrice: 1000 }],
]);

export function contexto(over = {}) {
  return {
    jogo: criarJogo(over.jogo),
    especies: ESPECIES,
    hunts: over.hunts ?? HUNTS,
    consts: { tabelaTipos: TABELA, ampliacaoHunt: 1.5, multHpSelvagem: 5, multDanoSelvagem: 1.8, tiposBoost: [] },
    eu: { level: 100, ...(over.eu ?? {}) },
    itens: ITENS,
    precoBola: over.precoBola ?? new Map([[1, 200]]),
    agora: 1_000_000,
    aoeAlvos: over.aoeAlvos,
  };
}
