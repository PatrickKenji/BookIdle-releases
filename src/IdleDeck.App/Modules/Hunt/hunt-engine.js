// Motor do /hunt: simula o pokémon em campo de uma conta em cada hunt do PokeIdle e ordena por
// XP/h e por ouro/h líquido (já descontadas poções, revives e bolas que a conta gasta).
//
// Regras: Pokepédia do jogo (Combate, Stats, Tipos, Morte, TM, Bicicletas, Automações) e o ritmo
// de ondas do Hunt Analyser do jogo. As fórmulas e o catálogo vêm do próprio jogo, ao vivo, pelo
// objeto `ctx.jogo` — este arquivo não tem DOM nem rede, para rodar igual no Node (testes) e na página.
(function (raiz, fabrica) {
  const api = fabrica();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else raiz.IdleHuntEngine = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const REGRAS = {
    aleatorioMedio: 0.925, // dano × aleatório(0,85–1,00)
    stab: 1.5,
    intervaloJogadorMs: 900, // mínimo entre golpes seus
    intervaloSelvagemMs: 1400, // mínimo entre golpes dele
    investidaPoder: 30, // ataque básico: físico, no tipo do pokémon
    investidaRecargaMs: 2000,
    msPorIvSpd: 10, // Investida e intervalo: −10 ms por ponto de IV de SPD
    pisoMs: 400,
    poderMinimo: 15,
    hpCombateMult: 12, // hpDeCombate = máx(24, arred(hp × 12))
    hpCombateMin: 24,
    ivSelvagemMedio: 16.5, // IV sorteado de 1 a 32
    abatesSimulados: 64,
    maxAtaquesPorAbate: 1000,
    aoeAlvosPadrao: 2, // o jogo não diz quantos selvagens ficam no 11×11; ajustável por conta
    limiarPadrao: 0.3,
  };

  const tiposDe = (esp) => [esp.type1, esp.type2].filter(Boolean);
  const ivSpd = (iv) => Math.min(32, Math.max(1, Math.round(Number(iv) || 1)));
  const comDescontoDoIv = (ms, iv) => Math.max(REGRAS.pisoMs, ms - ivSpd(iv) * REGRAS.msPorIvSpd);

  /** Efetividade na hunt: ×2 → ×2,5, ×4 → ×5,5, ×0,5 → ×0,33; ×0 e ×1 não mudam. */
  function amplificar(v, fator) {
    if (v === 0 || v === 1) return v;
    return v > 1 ? 1 + (v - 1) * fator : v / fator;
  }

  function dano(nivel, poder, atk, def, stab, efetividade, aleatorio = REGRAS.aleatorioMedio) {
    const base = ((2 * nivel / 5 + 2) * poder * Math.max(1, atk) / Math.max(1, def)) / 50 + 2;
    return base * (stab ? REGRAS.stab : 1) * efetividade * aleatorio;
  }

  /** Stats do selvagem: qualidade 1, sem potência. */
  function statsSelvagem(esp, nivel, iv = REGRAS.ivSelvagemMedio) {
    const s = (base) => ((Number(base) || 0) + 2 * iv) * nivel / 100;
    return { hp: s(esp.baseHp), atk: s(esp.baseAtk), def: s(esp.baseDef), spAtk: s(esp.baseSpAtk), spDef: s(esp.baseSpDef) };
  }

  function hpSelvagem(esp, nivel, ivHp, multHp) {
    const hp = ((Number(esp.baseHp) || 0) + 2 * ivHp) * nivel / 100;
    return Math.max(REGRAS.hpCombateMin, Math.round(hp * REGRAS.hpCombateMult)) * multHp;
  }

  function golpe(a, recargaMs) {
    return {
      nome: a.name,
      tipo: a.type,
      power: Math.max(REGRAS.poderMinimo, Number(a.power) || 0),
      fisico: a.category !== 'SPECIAL',
      recargaS: recargaMs / 1000,
    };
  }

  /** O pokémon do jogador: stats reais, golpes liberados no nível (com o desconto do Speed) e o Z-Move. */
  function montarHeroi(pk, esp, jogo) {
    const iv = pk.ivs?.speed;
    const comSpeed = (cd) => jogo.cooldownGolpeComSpeed(cd, esp.baseSpeed, iv);
    const golpes = (esp.attacks ?? [])
      .filter((a) => (a.learnLevel ?? 1) <= (pk.level ?? 1))
      .map((a) => golpe(a, comSpeed(a.cooldownMs)));
    if (pk.tmElemental && jogo.golpeTmElemental) {
      const z = jogo.golpeTmElemental(pk.tmElemental);
      golpes.push(golpe(z, comSpeed(z.cooldownMs)));
    }
    const st = pk.stats ?? {};
    return {
      nivel: pk.level ?? 1,
      tipos: pk.tipos?.length ? pk.tipos : tiposDe(esp),
      atk: st.atk ?? 1, def: st.def ?? 1, spAtk: st.spAtk ?? 1, spDef: st.spDef ?? 1,
      hpMax: Number(pk.maxHp) > 0 ? Number(pk.maxHp) : Math.max(REGRAS.hpCombateMin, Math.round((st.hp ?? 1) * REGRAS.hpCombateMult)),
      intervaloS: comDescontoDoIv(REGRAS.intervaloJogadorMs, iv) / 1000,
      investidaS: comDescontoDoIv(REGRAS.investidaRecargaMs, iv) / 1000,
      golpes,
    };
  }

  /** A Investida sai no 1º tipo; só passa para o 2º se o alvo for imune ao 1º. */
  function tipoDaInvestida(tipos, ef, alvoTipos) {
    const [t1, t2] = tipos;
    if (t2 && ef(t1, alvoTipos[0], alvoTipos[1]) === 0) return t2;
    return t1;
  }

  /**
   * O dano de cada golpe do atacante e da Investida contra um defensor.
   * `sazonal(tiposAtacante, tiposDefensor)` é o multiplicador do dia sazonal (×2 quem bate é do tipo do dia, ×¼ quem apanha é);
   * o golpe que acertava nunca cai abaixo de 1 de dano, como no jogo (`danoSazonal`).
   */
  function opcoesContra(atacante, defensor, ef, mult = 1, sazonal = null) {
    const s = sazonal ? sazonal(atacante.tipos, defensor.tipos) : 1;
    const contra = (power, tipo, fisico) => {
      const d = dano(atacante.nivel, power, fisico ? atacante.atk : atacante.spAtk,
        fisico ? defensor.def : defensor.spDef, atacante.tipos.includes(tipo), ef(tipo, defensor.tipos[0], defensor.tipos[1])) * mult;
      return s === 1 ? d : Math.max(d > 0 ? 1 : 0, d * s);
    };
    const tipoInv = tipoDaInvestida(atacante.tipos, ef, defensor.tipos);
    const inv = contra(REGRAS.investidaPoder, tipoInv, true);
    return {
      golpes: atacante.golpes.map((g) => ({ d: contra(g.power, g.tipo, g.fisico), recargaS: g.recargaS, nome: g.nome })),
      investida: { d: inv, recargaS: atacante.investidaS, nome: 'Investida' },
    };
  }

  /** Próximo ataque: o golpe pronto de maior dano; sem golpe pronto, a Investida; senão espera. */
  function proximoAtaque(op, pronto, est) {
    for (;;) {
      let melhor = -1;
      for (let i = 0; i < op.golpes.length; i++) {
        if (pronto[i] <= est.t && op.golpes[i].d > 0 && (melhor < 0 || op.golpes[i].d > op.golpes[melhor].d)) melhor = i;
      }
      if (melhor >= 0) return melhor;
      if (op.investida.d > 0 && est.proxInv <= est.t) return -1;
      let prox = op.investida.d > 0 ? est.proxInv : Infinity;
      for (let i = 0; i < op.golpes.length; i++) if (op.golpes[i].d > 0 && pronto[i] < prox) prox = pronto[i];
      if (!Number.isFinite(prox)) return null;
      est.t = Math.max(est.t, prox);
    }
  }

  /** Fila de abates: cotas por `pontos`, IV de HP espalhado de 1 a 32 em cada espécie. */
  function filaDeAbates(especiesHunt, n = REGRAS.abatesSimulados) {
    const total = especiesHunt.reduce((s, e) => s + e.pontos, 0);
    const cotas = especiesHunt.map((e) => (e.pontos / total) * n);
    const qtd = cotas.map(Math.floor);
    let faltam = n - qtd.reduce((s, x) => s + x, 0);
    const restos = cotas.map((c, i) => [c - qtd[i], i]).sort((a, b) => b[0] - a[0]);
    for (let k = 0; faltam > 0; k++, faltam--) qtd[restos[k % restos.length][1]]++;
    const fila = [];
    qtd.forEach((q, e) => {
      for (let j = 0; j < q; j++) fila.push({ e, chave: (j + 0.5) / q, ivHp: 1 + Math.floor(((j + 0.5) / q) * 32) });
    });
    return fila.sort((a, b) => a.chave - b.chave);
  }

  const qtdDe = (v) => (typeof v === 'number' ? v : Number(v?.qtd ?? v?.quantidade ?? v?.count ?? v?.amount ?? v?.q) || 0);

  /** Estoque de um item numa coleção do jogo (lista de {id, qtd} ou mapa id → qtd). */
  function estoque(colecao, id) {
    if (!colecao) return 0;
    if (Array.isArray(colecao)) return qtdDe(colecao.find((x) => String(x?.id ?? x?.itemId) === String(id)));
    return qtdDe(colecao[id]);
  }

  /** O primeiro id da lista da automação que ainda tem estoque (é o que o jogo gasta). */
  const primeiroComEstoque = (ids, colecao) => (ids ?? []).find((id) => estoque(colecao, id) > 0) ?? null;

  function bonusDeXp(eu, jogo, tiposBoost, agora) {
    const partes = eu?.bonusXp?.partes;
    if (Array.isArray(partes) && partes.length) {
      const ativas = partes.filter((p) => !p.ate || p.ate > agora);
      return {
        pokemon: ativas.reduce((m, p) => m * (Number(p.pk) || 1), 1),
        treinador: ativas.reduce((m, p) => m * (Number(p.tr) || 1), 1),
        partes: ativas,
      };
    }
    return {
      pokemon: jogo.multXpPokemon?.(eu ?? {}, agora, tiposBoost ?? []) ?? 1,
      treinador: jogo.multXpTreinador?.(eu ?? {}, agora, tiposBoost ?? []) ?? 1,
      partes: [],
    };
  }

  /** Bicicleta equipada: { raridade, fator } (o fator divide a duração do passo), ou null a pé. */
  function bikeDaConta(eu, consts, jogo) {
    const b = eu?.bicicleta;
    const rar = b?.equipadaRar ?? (b?.equipada != null ? (b.lista ?? []).find((x) => x.id === b.equipada)?.rar : null) ?? null;
    if (!rar) return null;
    let fator = 1 + Number((consts?.bicicletas?.raridades ?? []).find((r) => r.id === rar)?.velocidade);
    if (!Number.isFinite(fator)) fator = Number(jogo.fatorPassoDaRaridade?.(rar));
    return fator > 1 ? { raridade: rar, fator } : null;
  }

  const temAoe = (pk) => {
    const v = pk?.tmAoe ?? pk?.tm_aoe;
    return v === true || v === 'true' || v === 1;
  };

  /** Consumíveis da conta, como a automação do jogo gasta. */
  function consumiveis(eu, ctx) {
    const a = eu?.automation ?? {};
    const itens = ctx.itens ?? new Map();
    const pocaoId = a.autoPotion ? primeiroComEstoque(a.potionIds, eu?.items) : null;
    const reviveId = a.autoRevive ? primeiroComEstoque(a.reviveIds, eu?.items) : null;
    const bolaId = (a.autoBallSemParar || a.autoBallAteCapturar) ? primeiroComEstoque(a.ballIds, eu?.balls) : null;
    const limiar = Math.min(1, Math.max(0.1, (Number(a.hpLimiar) || REGRAS.limiarPadrao * 100) / 100));
    return {
      pocao: pocaoId != null ? itens.get(Number(pocaoId)) ?? itens.get(pocaoId) ?? null : null,
      revive: reviveId != null ? itens.get(Number(reviveId)) ?? itens.get(reviveId) ?? null : null,
      precoBola: bolaId != null ? Number(ctx.precoBola?.get(Number(bolaId)) ?? ctx.precoBola?.get(bolaId)) || 0 : 0,
      autoPotion: !!a.autoPotion,
      limiar,
    };
  }

  function simularHunt(hunt, heroi, ctx) {
    const { especies, ef, jogo } = ctx;
    const especiesHunt = (hunt.especies ?? [])
      .map((e) => ({ esp: especies.get(e.pokeId), pontos: Number(e.pontos) || 0 }))
      .filter((e) => e.esp && e.pontos > 0);
    if (!especiesHunt.length) return null;
    const nivel = hunt.nivel ?? 1;
    const totalPontos = especiesHunt.reduce((s, e) => s + e.pontos, 0);
    const multHp = ctx.consts.multHpSelvagem ?? 5;
    const multDano = ctx.consts.multDanoSelvagem ?? 1.8;
    // Dia sazonal: só nas hunts do mapa (que é o que o /hunt analisa); o multiplicador vem do módulo do próprio jogo.
    const sazonal = ctx.diaSazonal && typeof jogo.multDanoSazonal === 'function'
      ? (atacante, defensor) => Number(jogo.multDanoSazonal(ctx.diaSazonal, atacante, defensor)) || 1
      : null;

    const alvos = especiesHunt.map(({ esp, pontos }) => {
      const st = statsSelvagem(esp, nivel);
      const tipos = tiposDe(esp);
      const selvagem = {
        nivel, tipos, atk: st.atk, def: st.def, spAtk: st.spAtk, spDef: st.spDef,
        golpes: (esp.attacks ?? []).filter((a) => (a.learnLevel ?? 1) <= nivel)
          .map((a) => golpe(a, jogo.cooldownGolpeComSpeed(a.cooldownMs, esp.baseSpeed, REGRAS.ivSelvagemMedio))),
        investidaS: comDescontoDoIv(REGRAS.investidaRecargaMs, REGRAS.ivSelvagemMedio) / 1000,
      };
      return {
        esp, peso: pontos / totalPontos, tipos,
        ataque: opcoesContra(heroi, { tipos, def: st.def, spDef: st.spDef }, ef, 1, sazonal),
        defesa: opcoesContra(selvagem, { tipos: heroi.tipos, def: heroi.def, spDef: heroi.spDef }, ef, multDano, sazonal),
        lutaS: 0, abates: 0, danoPorGolpe: new Map(),
      };
    });

    // Nem o golpe mais forte derruba o selvagem mais forte dentro do limite: não vale simular.
    for (const alvo of alvos) {
      const melhor = Math.max(alvo.ataque.investida.d, ...alvo.ataque.golpes.map((g) => g.d));
      if (!(melhor > 0) || hpSelvagem(alvo.esp, nivel, 32, multHp) / melhor > REGRAS.maxAtaquesPorAbate) return { inviavel: true };
    }

    // A luta do herói, abate a abate; as recargas correm de um abate para o outro.
    const desloc = (jogo.MS_DESLOC_MS ?? 600) / 1000 / (ctx.bike?.fator > 1 ? ctx.bike.fator : 1);
    const entreAbatesS = desloc + (jogo.CD_MOVIMENTO_MEDIO_MS ?? 2000) / 1000;
    const pronto = heroi.golpes.map(() => 0);
    const est = { t: 0, proxInv: 0 };
    for (const item of filaDeAbates(especiesHunt)) {
      const alvo = alvos[item.e];
      const hp = hpSelvagem(alvo.esp, nivel, item.ivHp, multHp);
      const inicio = est.t;
      let feito = 0, ultimo = est.t, ataques = 0;
      while (feito < hp) {
        if (++ataques > REGRAS.maxAtaquesPorAbate) return { inviavel: true };
        const i = proximoAtaque(alvo.ataque, pronto, est);
        if (i === null) return { inviavel: true };
        const op = i >= 0 ? alvo.ataque.golpes[i] : alvo.ataque.investida;
        if (i >= 0) pronto[i] = est.t + op.recargaS;
        else est.proxInv = est.t + op.recargaS;
        alvo.danoPorGolpe.set(op.nome, (alvo.danoPorGolpe.get(op.nome) ?? 0) + Math.min(op.d, hp - feito));
        feito += op.d;
        ultimo = est.t;
        est.t += heroi.intervaloS;
      }
      alvo.lutaS += ultimo - inicio;
      alvo.abates++;
      est.t = ultimo + heroi.intervaloS + entreAbatesS;
    }

    // O que cada selvagem faz no herói enquanto a luta dura (bate de 1,4 em 1,4 s, desde o instante 0).
    let danoPorAbate = 0, maiorGolpe = 0;
    for (const alvo of alvos) {
      const lutaMedia = alvo.abates ? alvo.lutaS / alvo.abates : 0;
      const op = alvo.defesa;
      const prontoS = op.golpes.map(() => 0);
      const e = { t: 0, proxInv: 0 };
      let total = 0;
      while (e.t <= lutaMedia) {
        const i = proximoAtaque(op, prontoS, e);
        if (i === null || e.t > lutaMedia) break;
        const g = i >= 0 ? op.golpes[i] : op.investida;
        if (i >= 0) prontoS[i] = e.t + g.recargaS;
        else e.proxInv = e.t + g.recargaS;
        total += g.d;
        e.t += REGRAS.intervaloSelvagemMs / 1000;
      }
      danoPorAbate += alvo.peso * total;
      const pior = Math.max(op.investida.d, ...op.golpes.map((g) => g.d)) / REGRAS.aleatorioMedio; // rolagem máxima
      if (pior > maiorGolpe) maiorGolpe = pior;
    }

    // Ritmo: modelo de ondas do Hunt Analyser do jogo, com a luta simulada somada.
    const lutaMediaS = alvos.reduce((s, a) => s + a.peso * (a.abates ? a.lutaS / a.abates : 0), 0);
    const cicloS = lutaMediaS + heroi.intervaloS + entreAbatesS;
    const porOnda = Math.min(jogo.MAX_MOBS ?? 16, totalPontos);
    const alvosPorGolpe = Math.max(1, Math.min(ctx.aoeAlvos, porOnda));
    const segPorOnda = porOnda * (cicloS / alvosPorGolpe) + (jogo.MS_ONDA ?? 2600) / 1000;
    const killsH = (porOnda / segPorOnda) * 3600;

    // Recompensa por abate: as curvas do jogo.
    let xpAbate = 0, ouroAbate = 0;
    for (const alvo of alvos) {
      xpAbate += alvo.peso * (Number(jogo.xpPorDerrota(hunt, nivel, alvo.esp)) || 0);
      ouroAbate += alvo.peso * (Number(jogo.ouroPorDerrotaHunt(hunt, nivel, alvo.esp, ctx.bonusLoot)) || 0);
    }

    // Golpe que mais causou dano na hunt.
    const danoTotal = new Map();
    for (const alvo of alvos) for (const [nome, d] of alvo.danoPorGolpe) danoTotal.set(nome, (danoTotal.get(nome) ?? 0) + alvo.peso * d);
    const golpePrincipal = [...danoTotal.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null;

    // Consumíveis e risco.
    const c = ctx.consumo;
    const danoH = danoPorAbate * killsH;
    const hpNoLimiar = c.limiar * heroi.hpMax;
    const risco = maiorGolpe >= heroi.hpMax ? 'umHit' : (c.autoPotion && maiorGolpe >= hpNoLimiar) ? 'arriscado' : 'seguro';
    let pocoesH = 0;
    if (c.pocao) {
      const cura = c.pocao.healPct > 0 ? c.pocao.healPct * heroi.hpMax : Number(c.pocao.healAmount) || 0;
      const curaEfetiva = Math.min(cura, heroi.hpMax - hpNoLimiar);
      if (curaEfetiva > 0) pocoesH = danoH / curaEfetiva;
    }
    const mortesH = c.pocao ? 0 : danoH / heroi.hpMax;
    const custoH = {
      pocoes: pocoesH * (Number(c.pocao?.npcPrice) || 0),
      revives: c.revive ? mortesH * (Number(c.revive.npcPrice) || 0) : 0,
      bolas: c.precoBola * killsH,
    };
    const ouroH = killsH * ouroAbate;

    return {
      inviavel: false,
      lutaMediaS, cicloS, killsH, xpAbate, ouroAbate,
      xpPokemonH: killsH * xpAbate * ctx.bonus.pokemon,
      xpTreinadorH: killsH * xpAbate * ctx.bonus.treinador,
      ouroH,
      custoH,
      liquidoH: ouroH - custoH.pocoes - custoH.revives - custoH.bolas,
      danoH, pocoesH, mortesH, risco,
      danoPorAbatePct: danoPorAbate / heroi.hpMax,
      maiorGolpePct: maiorGolpe / heroi.hpMax,
      golpePrincipal,
    };
  }

  const vazio = { inviavel: true, killsH: 0, xpPokemonH: 0, xpTreinadorH: 0, ouroH: 0, liquidoH: 0,
    custoH: { pocoes: 0, revives: 0, bolas: 0 }, danoH: 0, pocoesH: 0, mortesH: 0, risco: 'seguro' };

  /**
   * Todas as hunts para o pokémon `pk` (um item de `eu.pokemons`).
   * `ctx`: { jogo, especies: Map, hunts, consts: { tabelaTipos, ampliacaoHunt, multHpSelvagem,
   * multDanoSelvagem, tiposBoost, bicicletas }, eu, itens: Map, precoBola: Map, agora, aoeAlvos? }.
   */
  function analisar(pk, ctx) {
    const esp = ctx.especies.get(pk.speciesId);
    if (!esp) throw new Error(`espécie ${pk.speciesId} (${pk.nome}) não está no catálogo do jogo`);
    const agora = ctx.agora ?? Date.now();
    const tabela = ctx.consts.tabelaTipos ?? {};
    const fator = ctx.consts.ampliacaoHunt ?? 1.5;
    const cache = new Map();
    const ef = (atq, d1, d2) => {
      const k = `${atq}|${d1}|${d2}`;
      let v = cache.get(k);
      if (v === undefined) cache.set(k, (v = amplificar(ctx.jogo.efetividade(atq, d1, d2 ?? null, tabela), fator)));
      return v;
    };

    const heroi = montarHeroi(pk, esp, ctx.jogo);
    const bonus = bonusDeXp(ctx.eu, ctx.jogo, ctx.consts.tiposBoost, agora);
    const bike = bikeDaConta(ctx.eu, ctx.consts, ctx.jogo);
    const aoe = temAoe(pk);
    const aoeAlvos = Math.max(1, Number(ctx.aoeAlvos ?? (aoe ? REGRAS.aoeAlvosPadrao : 1)) || 1);
    const diaSazonal = ctx.diaSazonal || null;
    const interno = {
      ...ctx, ef, bike, bonus, aoeAlvos, diaSazonal,
      bonusLoot: Number(ctx.jogo.bonusLootPct?.(ctx.eu ?? {}, agora, ctx.consts.tiposBoost ?? [])) || 0,
      consumo: consumiveis(ctx.eu, ctx),
    };

    const nivelTreinador = ctx.eu?.level ?? Infinity;
    const linhas = [];
    for (const h of ctx.hunts ?? []) {
      const r = simularHunt(h, heroi, interno);
      if (!r) continue;
      linhas.push({
        slug: h.slug, nome: h.nome, nivel: h.nivel ?? 1, regiao: h.regiao ?? h.area ?? null,
        liberada: nivelTreinador >= (h.nivel ?? 1),
        atual: ctx.eu?.huntSlug === h.slug,
        ...(r.inviavel ? vazio : r),
      });
    }
    const ordenar = (chave) => [...linhas].sort((a, b) => (a.inviavel - b.inviavel) || (b[chave] - a[chave]));
    return {
      heroi, bonus, bike, aoe, aoeAlvos, limiar: interno.consumo.limiar,
      diaSazonal, heroiDoDia: !!diaSazonal && heroi.tipos.includes(diaSazonal),
      pocao: interno.consumo.pocao?.name ?? null,
      linhas,
      porXp: ordenar('xpPokemonH'),
      porOuro: ordenar('liquidoH'),
    };
  }

  return {
    REGRAS, amplificar, dano, statsSelvagem, hpSelvagem, comDescontoDoIv, montarHeroi, filaDeAbates,
    estoque, bonusDeXp, bikeDaConta, analisar,
  };
});
