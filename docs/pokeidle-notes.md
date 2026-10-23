# Notas do pokeidle.io (investigação para o módulo /hunt)

Data: 2026-10-20 · versão do jogo no ar: 1.337.0

Fontes: a própria Wiki do jogo (Pokepédia, `pokepedia.mjs`, capítulos Combate, Stats, Níveis e XP, Tipos, Morte, Mapa), os módulos públicos do cliente (`app.js`, `hunt-analyser.mjs`, `shared/*.mjs`) e a extensão "/hunt for PokeIdle" v1.0.4 (lida só para saber onde ficam os dados; o código do nosso módulo é próprio).

## De onde vêm os dados

- **WebSocket do jogo** (mesmo host da página). Mensagens JSON com campo `t`:
  - `welcome`: quadro inicial. Traz `hunts` (lista de hunts: `slug`, `nome`, `nivel`, `regiao`/`area`, `especies: [{ pokeId, pontos }]`), `tabelaTipos`, `ampliacaoHunt` (1,5), `multHpSelvagem` (5), `multDanoSelvagem` (1,8), `tiposBoost`, `itensNossos` (preços), e `estado` completo.
  - `estado`: deltas do jogador. O jogo remonta com `shared/estado-delta.mjs` → `criarMescladorDeEstado()`; o resultado é o `eu` do cliente.
  - `campo` / `campo.init`: posições na hunt (não usamos na v1).
- **`eu`** (jogador): `level` (treinador, destranca hunts), `pokemons[]`, `activeId` (pokémon em campo), `huntSlug` (hunt atual), `loja` (boosts/VIP com validade), `guild`, `guildBonusPct`, `twitch`, `kick`.
- **Pokémon (`eu.pokemons[i]`)**: `id`, `speciesId`, `nome`, `level`, `ivs` (inclui `speed`), `stats` (hp, atk, def, spAtk, spDef, speed — já calculados pelo servidor com qualidade/potência/refino), `tipos`, `tmElemental`, `tmAoe`, `maxHp`.
- **Catálogo de espécies**: `assets/creatures.json` (+ `creatures-novos.json`, `creatures-outland-novos.json`, `creatures-formas.json`, `creatures-audit-overrides.json`), montado pelo `carregarCatalogoEspecies` do `app.js` com as funções de `shared/` (planilhas de golpes, herança Outland/Orre, cadeias truncadas, golpes especiais de 600, megas). Cada espécie: `pokeId`, `type1/type2`, `baseHp…baseSpeed`, `attacks[]` (`name`, `type`, `power`, `category`, `cooldownMs`, `learnLevel`), `loot[]`.

## Módulos do jogo que usamos (importados ao vivo de `https://pokeidle.io/...`)

| Módulo | Para quê |
|---|---|
| `hunt-analyser.mjs` | `efetividade`, `multXpTreinador`, `multXpPokemon`, `bonusLootPct`, `mediaPorKill`, constantes `MS_ONDA`, `MAX_MOBS`, `CD_GOLPE_MS`, `MS_DESLOC_MS`, `CD_MOVIMENTO_MEDIO_MS`, `VIP_MULT_XP` |
| `shared/recompensa-hunt.mjs` | `xpPorDerrota(hunt, nivel, esp)`, `ouroPorDerrotaHunt(hunt, nivel, esp, bonusLoot)` |
| `shared/cooldown-golpes.mjs` | `cooldownGolpeComSpeed(cd, speedBase, ivSpeed)` |
| `shared/tm-elemental.mjs` | `golpeTmElemental(tipo)` |
| `shared/estado-delta.mjs` | remontar o `eu` a partir dos deltas |
| os do catálogo | os mesmos passos do `carregarCatalogoEspecies` |

O analisador do jogo (`analisarHunt`) supõe **um golpe por abate**: é o teto teórico. O que falta é o tempo real de luta do pokémon do jogador.

## Regras de combate (Pokepédia > Combate / Stats / Tipos)

- Dano: `base = ((2 × nível/5 + 2) × power × atk/def) / 50 + 2`; `final = base × STAB(1,5) × efetividade × aleatório(0,85–1,00)` (média 0,925).
- Físico usa Ataque/Defesa; especial usa Sp.Atk/Sp.Def.
- A cada ataque o pokémon usa, entre os golpes já recarregados, o de **maior dano esperado** contra o alvo; se nenhum está pronto, **Investida**: power 30, física, no 1º tipo (2º só se o alvo for imune ao 1º), STAB sempre, recarga 2 s − 10 ms × IV de SPD (piso 400 ms).
- Intervalo mínimo entre golpes seus: 0,9 s − 10 ms × IV de SPD (piso 0,4 s). Do selvagem: 1,4 s.
- Recarga dos golpes: curva do jogo + desconto do Speed (`cooldownGolpeComSpeed`). Golpe de 600: 60 s.
- Efetividade multiplicativa nos dois tipos do defensor; **na hunt, amplificada ×1,5**: acima de 1 vira `1 + (v − 1) × 1,5`, abaixo de 1 vira `v / 1,5`; 0 e 1 não mudam.
- Stats: `stat = arred((base + 2 × IV) × nível/100 × qualidade^expo × mult)`; selvagem: qualidade 1, mult 1, IV sorteado 1–32.
- HP de combate: `máx(24, arred(hp × 12))`; selvagem leva × `multHpSelvagem` (5) por cima.
- Dano do selvagem × `multDanoSelvagem` (1,8).
- Ondas: no máximo `MAX_MOBS` (16) selvagens por onda; o campo vazio chama a próxima onda (`MS_ONDA`).
- XP: o mesmo XP vai para treinador e pokémon; multiplicadores se **multiplicam** (XP Boost, XP Boost Pokémon, VIP, Boost da Guild, ranking da guild, Twitch, Kick, Atrasados, evento). `multXp*` do jogo cobre loja/VIP/guild; Twitch, Kick, evento e Atrasados entram por cima.
- Hunt liberada: nível do treinador ≥ nível da hunt.
- Morte: desmaiar custa 10% do XP do nível (0 com Atrasados) + o tempo de voltar.

## Z-Move, AoE e bicicleta (Pokepédia > TM e Bicicletas; `shared/tm-elemental.mjs`, `shared/bicicletas.mjs`)

- **Z-Move (TM Elemental)** (`pk.tmElemental` = tipo): golpe extra `golpeTmElemental(tipo)` — power 2800, recarga 60 s (com desconto do Speed), especial; acerta um quadrado 3×3 **em volta do alvo** (o alvo e quem estiver encostado nele).
- **Disco AoE** (`pk.tmAoe`): **todos** os golpes acertam um quadrado 11×11 **em volta do pokémon** (raio 5 tiles). O Z-Move continua só no 3×3 do alvo.
- **Bicicleta** (`eu.bicicleta`, raridade): o passo dura `passo / fatorPassoDaRaridade(raridade)`; fator 1 a pé … 2 na Lendária. Passo do pokémon a pé: 260 ms por tile. Só a caminhada muda.

## Poções, revives e bolas (Pokepédia > Morte e Automações; `eu.automation`, `assets/items.json`)

- `eu.automation`: `autoPotion`, `potionIds` (ordem de gasto), `hpLimiar` (10–100%, padrão 30%), `autoRevive`, `reviveIds`, `autoBallSemParar` / `autoBallAteCapturar`, `ballIds`, `autoVoltarHunt`. As automações rodam no servidor.
- Itens (`items.json`, categorias `heal`/`revive`): `healAmount` (HP fixo) ou `healPct` (fração do HP máx.; tem prioridade), `revivePct`, `npcPrice`. Estoque em `eu.items`. A automação usa o primeiro da lista com estoque.
- Desmaio sem revive: time volta ao Centro, perde 10% do XP do nível do treinador (0 com Atrasados) e o tempo de voltar.
- Auto-bola (VIP): uma bola por corpo (1,2 s entre arremessos), primeira da lista com estoque.

## Por conta, sempre ao vivo

Tudo vem do `eu` de cada conta logada: nível do treinador, pokémon em campo (stats reais, IVs, golpes liberados pelo nível, Z-Move, AoE), bicicleta, boosts, automações, estoque e preços. Nada fixo de uma conta específica — o app pode ser usado por outras pessoas.

## O que não é documentado (medido em jogo pelo próprio módulo)

- Distância andada por abate e quantos selvagens ficam juntos (densidade): medidos pelas mensagens `campo`/`campo.init` (posições do herói e dos selvagens) na hunt em que a conta está, guardados por hunt; padrão para hunts nunca medidas. Segundo a Wiki, de Unova em diante todas as áreas têm o mesmo ritmo.

## Bônus de live: Twitch e Kick (`pokepedia.mjs` §2b, `i18n.mjs`)

Fonte: seção `bonusLive` da Pokepédia + textos `kick.*`/`twitch.*` do i18n (constantes no topo do pokepedia: `PCT_TWITCH=15`, `PCT_TWITCH_EXTRA=2.5`, `PCT_KICK=15`, `MIN_KICK=60`, `CUSTO_KICK={xp:300, captura:500, shiny:1000}`).

**Twitch — % enquanto assiste.** Vincular a conta no painel (Conectar Twitch). Assistir **logado**, e "assistir" = estar **no chat** da live (a Twitch não informa quem vê; o jogo lê quem está conectado ao chat). +15% de XP (treinador e pokémon) enquanto estiver na live; **cada live oficial extra aberta ao mesmo tempo soma +2,5%** (2 = 17,5%, 3 = 20%). Só contam lives com o selo **BÔNUS ATIVADO**. A lista é conferida a cada minuto; o bônus cai alguns minutos depois de sair.

**Kick — horas compradas com pontos do canal.** A Kick **não diz ao jogo quem está assistindo**: o sinal é o **resgate**. Assistindo logado você junta os pontos do canal da própria Kick (10 a cada 5 min; assinante ganha o dobro) e troca nas recompensas que **todo canal oficial** tem:

| Recompensa | Custo | Efeito por 1 h |
|---|---|---|
| XP | 300 pontos | +15% XP (treinador e pokémon) |
| Captura | 500 pontos | +15% Capture Boost |
| Shiny | 1000 pontos | +15% Secret Lure |

- As **horas se somam** (mesmo bônus 3× no mesmo canal ou em canais diferentes = 3 h); o **percentual NÃO cresce** (fica em +15%).
- Vínculo é **permanente**: cada conta da Kick só pode ser vinculada a **UM treinador**, sem desvincular/trocar. Resgate sem vínculo é recusado e a Kick devolve os pontos.
- Só canais **oficiais** têm as recompensas ("pontos ativos"); canal offline: os pontos que já tem continuam resgatáveis.
- Os % de captura/shiny **multiplicam** o boost da Loja (Capture Boost ×2 × 1,15 = ×2,3).
- `kick.desligado`: "O bônus da Kick ainda não está ligado neste servidor" — conferir na linha "Bônus na Kick" do painel.

**Kick, regras da plataforma (help.kick.com):** Channel Points são por canal e não se transferem entre canais; acumulam com o viewer **logado e assistindo**; **espelhamento de tela e picture-in-picture não contam**. Não há regra oficial proibindo várias lives simultâneas — cada canal acumula o seu.

**Contas múltiplas:** como o vínculo é 1 conta da Kick por treinador, cada conta do jogo precisa de uma conta própria da Kick. Farm: N canais oficiais abertos ao mesmo tempo = N × 120 pontos/h (assinante: 240/h) → ~1,6 h de +15% de XP por hora assistida com 4 canais, mais captura/shiny conforme a necessidade.

### Kick: mais de uma live aberta ao mesmo tempo rende pontos em todas?

**Testado em 21/10 pelo Patrick: SIM.** Duas lives de canais oficiais abertas ao mesmo tempo, no mesmo PC e na **mesma conta da Kick**, contabilizaram pontos **nas duas**. A dedução anterior (baseada na FAQ de Drops) estava **errada** — a FAQ de Drops fala de progresso de *campanha de drop*, não dos Channel Points.

O que continua valendo (docs oficiais):
- Pontos são **por canal** e não se transferem; resgate só no mesmo canal (10 pts/5 min; assinante 20).
- **PiP e espelhamento de tela não contam** para pontos — a exibição precisa ser "de verdade".
- Pontos acumulam com o viewer **logado e assistindo**.

Consequência prática: várias lives oficiais abertas na mesma conta somam pontos em paralelo (N canais ≈ N × 120 pontos/h). O que **não** multiplica é o percentual do bônus no jogo — 15% por resgate, e o que empilha é o **tempo**.
