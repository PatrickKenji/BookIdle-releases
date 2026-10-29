// Script do /hunt dentro de cada conta do pokeidle.io (mundo da página, antes do jogo carregar).
//
// Só LÊ: acrescenta um ouvinte de mensagem ao WebSocket do jogo, nada é enviado nem alterado.
// Remonta o estado com o `estado-delta` do próprio jogo, importa ao vivo as fórmulas e o catálogo
// do jogo, roda o motor (IdleHuntEngine) e manda o resumo para o app:
//   chrome.webview.postMessage(JSON.stringify({ module: 'hunt', data }))
// O app pode ajustar os alvos por golpe do TM AoE com window.__idleHunt.definirAoe(n).
(function () {
  'use strict';
  if (window.top !== window || window.__idleHunt) return;

  const ORIGEM = location.origin;
  const SEM_CACHE = { cache: 'no-cache' };
  const MIN_MS_ENTRE_CALCULOS = 2000;
  const RECALCULAR_A_CADA_MS = 60000;
  const TOP = 25;

  const enviar = (data) => {
    try { window.chrome.webview.postMessage(JSON.stringify({ module: 'hunt', data })); } catch { /* fora do app */ }
  };
  const status = (texto) => enviar({ tipo: 'status', texto });
  /** Marco para o hunt.log do app (o painel ignora). Só quantidades e nomes de campos, nunca dados da conta. */
  const diag = (texto) => enviar({ tipo: 'diag', texto });
  const marcos = new Set();
  const marco = (chave, texto) => { if (marcos.size < 60 && !marcos.has(chave)) { marcos.add(chave); diag(texto); } };

  let welcome = null;
  let eu = null;
  let mesclar = null;
  const fila = [];
  let aoeAlvos = null;
  try { aoeAlvos = Number(localStorage.getItem('idleHunt.aoeAlvos')) || null; } catch { /* sem storage */ }

  // ---- WebSocket: só escuta -----------------------------------------------------------------
  function aoReceber(ev) {
    if (typeof ev.data !== 'string') return;
    const cab = ev.data.slice(0, 40);
    const tipoMsg = /"t":"([^"]{1,40})"/.exec(cab)?.[1];
    if (tipoMsg) marco('t:' + tipoMsg, `mensagem do jogo: t=${tipoMsg}`);
    const ehWelcome = cab.includes('"t":"welcome"');
    if (!ehWelcome && !cab.includes('"t":"estado"')) return;
    let m;
    try { m = JSON.parse(ev.data); } catch { return; }
    if (ehWelcome) {
      welcome = m;
      diag(`welcome: hunts=${Array.isArray(m.hunts) ? m.hunts.length : typeof m.hunts}, estado=${m.estado ? Object.keys(m.estado).length + ' chaves' : 'ausente'}, `
        + `pokemons=${Array.isArray(m.estado?.pokemons) ? m.estado.pokemons.length : 'n/d'}, diaSazonal=${m.estado?.diaSazonal?.tipo ?? 'n/d'}`);
      if (m.estado) entregarEstado({ ...m.estado, cheio: true });
    } else if (m.estado) {
      entregarEstado(m.estado);
    }
  }

  function entregarEstado(parcial) {
    if (!mesclar) { fila.push(parcial); return; }
    const novo = mesclar(parcial);
    if (novo) {
      eu = novo;
      marco('eu', `estado remontado: pokemons=${(eu.pokemons ?? []).length}, activeId=${eu.activeId ?? 'n/d'}, nível=${eu.level ?? 'n/d'}`);
      agendar();
    }
  }

  const Original = window.WebSocket;
  function WebSocketObservado(...args) {
    const ws = new Original(...args);
    try {
      if (new URL(String(args[0]), location.href).host === location.host) ws.addEventListener('message', aoReceber);
    } catch { /* não é o socket do jogo */ }
    return ws;
  }
  WebSocketObservado.prototype = Original.prototype;
  for (const k of ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED']) WebSocketObservado[k] = Original[k];
  window.WebSocket = WebSocketObservado;

  import(`${ORIGEM}/shared/estado-delta.mjs`)
    .then((m) => {
      mesclar = m.criarMescladorDeEstado();
      for (const p of fila.splice(0)) entregarEstado(p);
    })
    .catch((err) => status(`Não consegui ler o estado do jogo (estado-delta): ${err.message}`));

  // ---- o jogo: fórmulas e catálogo, sempre da versão no ar -----------------------------------
  /** Módulos que o /hunt usa se existirem: um jogo sem eles continua calculando (sem esse detalhe). */
  const OPCIONAIS = new Set(['diaSazonal', 'ginasios']);
  const MODULOS = {
    analyser: 'hunt-analyser.mjs',
    recompensa: 'shared/recompensa-hunt.mjs',
    cooldown: 'shared/cooldown-golpes.mjs',
    tm: 'shared/tm-elemental.mjs',
    bicicletas: 'shared/bicicletas.mjs',
    outland: 'shared/outland.mjs',
    planilhas: 'shared/golpes-planilhas.mjs',
    baseOutland: 'shared/herdar-base-outland.mjs',
    evolucoes: 'shared/evolucoes-cruzadas.mjs',
    ajustes: 'shared/ajustes-especie-espelho.mjs',
    alias: 'shared/alias-item.mjs',
    vendaNpc: 'shared/venda-npc-item.mjs',
    especiais: 'shared/golpes-especiais.mjs',
    golpesOrre: 'shared/herdar-golpes-orre.mjs',
    megas: 'shared/megas.mjs',
    diaSazonal: 'shared/dia-sazonal.mjs',
    ginasios: 'shared/ginasios.mjs',
  };
  const json = (caminho) => fetch(`${ORIGEM}/${caminho}`, SEM_CACHE).then((r) => (r.ok ? r.json() : null)).catch(() => null);

  async function importarModulos() {
    const pares = await Promise.all(Object.entries(MODULOS).map(async ([k, caminho]) => {
      try { return [k, await import(`${ORIGEM}/${caminho}`)]; }
      catch (err) {
        if (OPCIONAIS.has(k)) { diag(`módulo opcional ausente: ${caminho} (${err.message})`); return [k, null]; }
        throw new Error(`não consegui carregar ${caminho} (${err.message})`);
      }
    }));
    return Object.fromEntries(pares);
  }

  /** O catálogo de espécies na mesma ordem do `carregarCatalogoEspecies` do jogo (só o que mexe em combate). */
  function montarCatalogo(mod, fontes) {
    const lista = [];
    for (const f of fontes.creatures) if (f?.creatures?.length) lista.push(...f.creatures);
    mod.outland.aplicarRemapOutlandLista(lista);
    for (const o of fontes.overrides?.overrides ?? []) {
      const c = lista.find((x) => x.pokeId === o.pokeId);
      if (!c) continue;
      for (const k of ['baseHp', 'baseAtk', 'baseDef', 'baseSpAtk', 'baseSpDef', 'baseSpeed']) if (typeof o[k] === 'number') c[k] = o[k];
      if (Array.isArray(o.attacks)) c.attacks = o.attacks.map((a) => ({ ...a }));
    }
    mod.planilhas.aplicarGolpesDasPlanilhas((id) => lista.filter((c) => c.pokeId === id));
    mod.baseOutland.herdarBaseOutland(lista);
    mod.evolucoes.aplicarCadeiasTruncadas(lista);
    mod.evolucoes.normalizarEvolveLevel600(lista);
    mod.ajustes.aplicarAjustesHuntLevel?.((id) => lista.find((c) => c.pokeId === id));
    const especies = new Map();
    for (const c of lista) {
      if (mod.outland.isEspelhoFantasmaPokeId(c.pokeId)) continue;
      mod.especiais.injetarGolpesEspeciais(c);
      especies.set(c.pokeId, c);
    }
    mod.golpesOrre.herdarGolpesOrre(lista);
    for (const mega of mod.megas.criarEspeciesMega?.(lista) ?? []) especies.set(mega.pokeId, mega);
    return especies;
  }

  let jogoPromessa = null;
  const carregarJogo = () => (jogoPromessa ??= (async () => {
    const [mod, cr, novos, outland, formas, overrides, itens] = await Promise.all([
      importarModulos(),
      json('assets/creatures.json'),
      json('assets/creatures-novos.json'),
      json('assets/creatures-outland-novos.json'),
      json('assets/creatures-formas.json'),
      json('assets/creatures-audit-overrides.json'),
      json('assets/items.json'),
    ]);
    if (!cr?.creatures?.length) throw new Error('não consegui baixar o catálogo de espécies (creatures.json)');
    const especies = montarCatalogo(mod, { creatures: [cr, novos, outland, formas], overrides });
    const mapaItens = new Map();
    for (const i of itens?.items ?? itens ?? []) {
      if (i && i.id != null) mapaItens.set(Number(i.id), i);
    }
    return { mod, especies, itens: mapaItens };
  })().catch((err) => { jogoPromessa = null; throw err; }));

  // ---- cálculo ---------------------------------------------------------------------------------
  let ultimoCalculo = 0;
  let ultimaAssinatura = '';
  let timer = null;

  function pokemonEmCampo() {
    const id = eu?.activeId;
    return id == null ? null : (eu.pokemons ?? []).find((p) => p.id === id) ?? null;
  }

  /** O tipo do dia sazonal que o servidor mandou (valendo só nas hunts do mapa), ou null. */
  function tipoDoDia(modulo) {
    const tipo = eu?.diaSazonal?.tipo;
    if (typeof tipo !== 'string' || !tipo) return null;
    return !modulo?.TIPOS_SAZONAIS || modulo.TIPOS_SAZONAIS.includes(tipo) ? tipo : null;
  }

  /** O que muda o resultado: se nada disso mudou, não recalcula (exceto a cada minuto). */
  function assinatura(pk) {
    const a = eu?.automation ?? {};
    return JSON.stringify([
      pk?.id, pk?.level, pk?.stats, pk?.tmElemental, pk?.tmAoe, eu?.level, eu?.bonusXp, eu?.bicicleta?.equipadaRar,
      a.autoPotion, a.potionIds, a.hpLimiar, a.autoRevive, a.reviveIds, a.autoBallSemParar, a.autoBallAteCapturar, a.ballIds,
      eu?.huntSlug, aoeAlvos, eu?.diaSazonal?.tipo, eu?.outlandTier, eu?.ginasiosPodio ?? eu?.ginasios,
    ]);
  }

  function agendar(forcar = false) {
    if (!welcome || !eu) return;
    const pk = pokemonEmCampo();
    const sig = assinatura(pk);
    const agora = Date.now();
    if (!forcar && sig === ultimaAssinatura && agora - ultimoCalculo < RECALCULAR_A_CADA_MS) return;
    clearTimeout(timer);
    timer = setTimeout(calcular, Math.max(0, MIN_MS_ENTRE_CALCULOS - (agora - ultimoCalculo)));
  }

  const r0 = (x) => (Number.isFinite(x) ? Math.round(x) : 0);
  const r2 = (x) => (Number.isFinite(x) ? Math.round(x * 100) / 100 : 0);
  const resumo = (l) => ({
    slug: l.slug, nome: l.nome, nivel: l.nivel, regiao: l.regiao, liberada: l.liberada, atual: !!l.atual, inviavel: !!l.inviavel,
    killsH: r0(l.killsH), lutaS: r2(l.lutaMediaS), xpPokemonH: r0(l.xpPokemonH), xpTreinadorH: r0(l.xpTreinadorH),
    ouroH: r0(l.ouroH), liquidoH: r0(l.liquidoH),
    custoPocoesH: r0(l.custoH?.pocoes), custoRevivesH: r0(l.custoH?.revives), custoBolasH: r0(l.custoH?.bolas),
    pocoesH: r2(l.pocoesH), mortesH: r2(l.mortesH), risco: l.risco,
    maiorGolpePct: r2(l.maiorGolpePct), danoPorAbatePct: r2(l.danoPorAbatePct), golpe: l.golpePrincipal ?? null,
    outland: !!l.outland, tokensH: r2(l.tokensH), fragmentosH: r2(l.fragmentosH),
  });
  /** As melhores liberadas + as 5 bloqueadas mais baratas de destravar (para mostrar o que vem). */
  const corte = (lista) => [
    ...lista.filter((l) => l.liberada).slice(0, TOP),
    ...lista.filter((l) => !l.liberada && !l.inviavel).sort((a, b) => a.nivel - b.nivel).slice(0, 5),
  ].map(resumo);

  /** As liberadas da Outland (todas têm o mesmo nível, então o corte é só por tamanho) + as que o nível ainda não abre. */
  const corteOutland = (lista) => [
    ...lista.filter((l) => l.liberada).slice(0, TOP * 2),
    ...lista.filter((l) => !l.liberada && !l.inviavel).slice(0, 3),
  ].map(resumo);

  async function calcular() {
    ultimoCalculo = Date.now();
    const pk = pokemonEmCampo();
    marco('calculo', `primeiro cálculo: pokémon em campo=${pk ? pk.nome ?? pk.speciesId : 'nenhum'}`);
    ultimaAssinatura = assinatura(pk);
    if (!pk) { status('Coloque um pokémon em campo'); return; }
    try {
      const jogo = await carregarJogo();
      const { analyser, recompensa, cooldown, tm, bicicletas, diaSazonal } = jogo.mod;
      const precoBola = new Map((welcome.catalogoBolas ?? []).map((b) => [Number(b.id), Number(b.preco ?? b.price ?? b.npcPrice) || 0]));
      const itens = new Map(jogo.itens);
      for (const c of welcome.itensCompraveis ?? []) {
        const base = itens.get(Number(c.itemId));
        if (base && Number(c.preco) > 0) itens.set(Number(c.itemId), { ...base, npcPrice: Number(c.preco) });
      }
      const r = window.IdleHuntEngine.analisar(pk, {
        jogo: {
          cooldownGolpeComSpeed: cooldown.cooldownGolpeComSpeed,
          golpeTmElemental: tm.golpeTmElemental,
          efetividade: analyser.efetividade,
          xpPorDerrota: recompensa.xpPorDerrota,
          ouroPorDerrotaHunt: recompensa.ouroPorDerrotaHunt,
          bonusLootPct: analyser.bonusLootPct,
          multXpPokemon: analyser.multXpPokemon,
          multXpTreinador: analyser.multXpTreinador,
          fatorPassoDaRaridade: bicicletas.fatorPassoDaRaridade,
          MS_ONDA: analyser.MS_ONDA, MAX_MOBS: analyser.MAX_MOBS,
          MS_DESLOC_MS: analyser.MS_DESLOC_MS, CD_MOVIMENTO_MEDIO_MS: analyser.CD_MOVIMENTO_MEDIO_MS,
          multDanoSazonal: diaSazonal?.multDanoSazonal,
          multBuffGinasio: jogo.mod.ginasios?.multBuffGinasio,
        },
        especies: jogo.especies,
        hunts: welcome.hunts ?? [],
        consts: {
          tabelaTipos: welcome.tabelaTipos, ampliacaoHunt: welcome.ampliacaoHunt,
          multHpSelvagem: welcome.multHpSelvagem, multDanoSelvagem: welcome.multDanoSelvagem,
          tiposBoost: welcome.tiposBoost, bicicletas: welcome.bicicletas,
          chanceBossToken: welcome.chanceBossToken, outlandTiers: welcome.outlandTiers,
          chanceFragmentos: [welcome.casas?.fragmento?.chanceDrop, welcome.shinyStone?.chanceDrop, welcome.bicicletas?.fragmento?.chanceDrop],
        },
        eu,
        itens,
        precoBola,
        agora: Date.now(),
        aoeAlvos: aoeAlvos ?? undefined,
        diaSazonal: tipoDoDia(diaSazonal),
      });
      enviar({
        tipo: 'relatorio',
        versaoJogo: document.querySelector('link[href*="estilo.css?v="]')?.getAttribute('href')?.split('v=')[1] ?? null,
        geradoEm: Date.now(),
        treinador: { nick: eu.nick ?? null, level: eu.level ?? 0 },
        pokemon: {
          nome: pk.nome ?? null, level: pk.level ?? 0, tipos: r.heroi.tipos, hpMax: r.heroi.hpMax,
          zMove: pk.tmElemental ?? null, aoe: r.aoe, aoeAlvos: r.aoeAlvos,
        },
        bonusXp: { pokemon: r2(r.bonus.pokemon), treinador: r2(r.bonus.treinador) },
        bike: r.bike ? { raridade: r.bike.raridade, fator: r2(r.bike.fator) } : null,
        pocao: r.pocao, limiar: r2(r.limiar),
        huntAtual: eu.huntSlug ?? null,
        diaSazonal: r.diaSazonal, heroiDoDia: r.heroiDoDia,
        porXp: corte(r.porXp),
        porOuro: corte(r.porOuro),
        buffGinasioPct: r.buffGinasioPct,
        outland: { tier: r.outland.tier, rotulo: r.outland.rotulo, mult: r2(r.outland.mult), chanceBoss: r.outland.chanceBoss, chanceFragmentos: r.outland.chanceFragmentos },
        porOutland: corteOutland(r.porOutland),
      });
    } catch (err) {
      diag(`falha no cálculo: ${err.stack ?? err.message}`.slice(0, 600));
      status(`Erro no cálculo: ${err.message}`);
    }
  }

  setInterval(() => agendar(), RECALCULAR_A_CADA_MS);
  status('Esperando o jogo conectar…');

  window.__idleHunt = {
    /** Alvos por golpe do TM AoE (1 = ignorar); guardado na conta (localStorage da página). */
    definirAoe(n) {
      aoeAlvos = Math.max(1, Math.min(16, Number(n) || 1));
      try { localStorage.setItem('idleHunt.aoeAlvos', String(aoeAlvos)); } catch { /* sem storage */ }
      agendar(true);
      return aoeAlvos;
    },
    recalcular: () => agendar(true),
  };
})();
