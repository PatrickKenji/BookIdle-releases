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

## Mudanças do jogo até a 1.380.2 (conferidas em 2026-10-28) e o que o /hunt faz com elas

- **Ouro e XP por abate** (`shared/recompensa-hunt.mjs`): uma curva só, pelo nível do mob; o loot saiu do jogo (15/10) e virou *bônus de loot* no ouro (+15% até o nível 25.000), que o Loot Boost multiplica. O motor já chama as funções do jogo, então acompanha sozinho.
- **Pódio de ginásio** (v1.377.0, `shared/ginasios.mjs` → `multBuffGinasio(tipos, eu.ginasiosPodio ?? eu.ginasios)`): o pokémon de um tipo cujo ginásio o dono lidera bate **+25%** (2º lugar +10%, 3º +5%) na hunt e no boss, sem empilhar entre os tipos. Entra no dano do herói (`multGinasio`); o relatório diz `buffGinasioPct`.
- **Hunts em bandos** (v1.377.0): toda hunt, Outland incluída, ganhou o layout em bandos, "mesmo ritmo e mesmo ganho de bicicleta em todo mapa". O ritmo continua o modelo de ondas do Hunt Analyser; o AoE por bando segue sendo o ajuste manual da conta.
- **Outland** (`shared/outland-tiers.mjs`, `welcome.outlandTiers`): mesmas 47 hunts, **todas nível 150**; o degrau (Outland 1 a 8, `eu.outlandTier`, travado pelo nível 150/1.000/2.000/4.000/8.000/16.000/24.000/36.000) só multiplica os raros: Bronze Boss Token (`welcome.chanceBossToken`, 0,05% na Outland 1), Fragmento de Chave, de Shiny Stone e de Bicicleta (0,0005% cada). Mult: 1 · 1,25 · 1,5 · 2 · 3 · 4 · 6 · 8. Shiny e Fragmento de Ovo Shiny não caem lá.
- Como o nível é 150 em todas, **XP e ouro por abate são iguais na Outland**: a melhor hunt é a que limpa mais rápido (mais abates/h), e token, fragmento, XP e ouro por hora andam juntos com os abates/h. O /hunt tem a aba **Outland (tokens)** com isso.

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

### Pendente: replicar na Kick o que fizemos na Twitch (presença só pelo chat)

**Ideia (22/10, Patrick):** na Twitch o app não abre o vídeo de cada canal: uma conexão leve de chat por conta (`TwitchChatConnection`) basta para o jogo contar a presença. Fazer o equivalente na Kick — login pelo site no WebView2 da conta, cookies/sessão do perfil, e uma conexão leve de chat por canal em vez de uma página de vídeo por canal (muito menos RAM).

**NÃO TESTADO — a dúvida é se só o chat rende pontos.** Na Twitch a presença no chat é o que o jogo lê; na Kick o sinal é o resgate, e os pontos do canal talvez dependam do **vídeo sendo exibido** (a Kick diz "logado e assistindo"; PiP e espelhamento já não contam, o que sugere checagem de exibição real). Se só o chat não render pontos, a alternativa é manter o player (talvez mudo, em baixa qualidade) mas só nos canais necessários.

**Teste a fazer antes de implementar** (conta vinculada, canal oficial, 2 janelas de comparação):
1. Conta logada com **só o chat** aberto (sem player): ver se o saldo de pontos sobe após 5–10 min (esperado: +10, ou +20 se assinante).
2. Mesma coisa com o player aberto e silenciado/qualidade mínima, para ter a linha de base.
3. Se o chat sozinho render: ver como a Kick entrega o chat (WebSocket Pusher público de `chatroom`; conferir se precisa de login para contar presença) antes de desenhar a conexão.

Até o teste, **não implementar**. Resultado do teste vai nesta seção.

**Resultado (22/10, Patrick): só o chat NÃO rende pontos.** Precisa estar logado **e** com a live aberta inteira (vídeo). O jeito da Twitch (só chat) não serve para a Kick.

**Investigação inicial do site (22/10, sem login nosso, só observando):** ao abrir um canal o site chama `websockets.kick.com/viewer/v1/token` (indício de um WebSocket de "espectador" separado do player), `kick.com/current-viewers`, `kick.com/api/v2/channels/<slug>/points` e `.../channel-points/channel-settings`, além de `web.kick.com/api/v1/realtime/auth/*`. Um observador de `fetch`/`WebSocket` na thread da página não viu heartbeat repetido em 30 s nem WebSocket aberto (pode estar em Web Worker ou ter sido aberto antes do observador). **Não confirmado** que esse sinal é o que conta os pontos.

**Teste com live aberta (22/10, canal yetz, conta logada, ~10,5 min observados):** saldo 130 → 140 aos ~5,5 min (10 pontos, ritmo oficial; não precisou de 10 min). Durante todo o teste o vídeo estava tocando. O site manda um **batimento a cada 60 s**: `kick.com/current-viewers`, `web.kick.com/api/v1/realtime/connection` e `web.kick.com/api/v1/realtime/channels/<id>/chat/connection`. Na carga inicial também chama `realtime/auth/connection`, `realtime/auth/channel` e `stream/<id>/playback`. Nenhuma chamada nova coincidiu com o aumento dos pontos (o crédito parece vir do servidor). **Ainda não testado:** se esses batimentos sozinhos bastam, ou se a Kick exige o vídeo tocando (ex.: pela sessão de playback). Não foi feita nenhuma chamada manual a esses endpoints.

**Correção (22/10, observando o formato dos batimentos):** os três sinais de 60 s provavelmente **NÃO** são a presença. `current-viewers?ids[]=` é só a consulta de contagem de espectadores (a resposta é a contagem). `POST realtime/connection` e `POST realtime/channels/<id>/chat/connection` (corpo `client,capabilities`, resposta `connections,mode`) parecem a negociação da conexão de chat — coerente com "só o chat não rende pontos". Todos usam cookie + cabeçalho `authorization` + `x-app-platform`. O candidato a sinal de presença continua sendo o WebSocket `websockets.kick.com/viewer/v1/*`, que só aparece na carga inicial da página (não reabre ao trocar de canal pelo site) e **não foi possível observar** com observadores injetados depois do carregamento. Para ver os quadros dele seria preciso um navegador de teste com depuração (CDP) e login do Patrick nele.

**Protocolo da presença na Kick, observado (22/10, Edge de teste com depuração, só leitura):** a presença é um WebSocket de espectador separado do vídeo e do chat. Sequência: `GET websockets.kick.com/viewer/v1/token` (cookie `session_token` como Bearer, resposta `{token}`) → `wss://websockets.kick.com/viewer/v1/connect?token=…`. Mensagens do cliente: `user_event` `tracking.user.watch.livestream` com `channel_id`, `livestream_id` e `vod_id` (na abertura e a cada ~45–75 s); `channel_handshake` com `channelId` a cada 15 s; `ping` a cada 30 s (resposta `pong`). `livestream_id` e `vod_id` vêm de `GET kick.com/api/v2/channels/<slug>/info` (objeto da livestream). O vídeo (HLS) corre em paralelo, mas não passa pelos servidores da Kick. Também aparece para a página inicial (player de destaque).

**NÃO validado — teste barrado:** o passo seguinte era abrir esse WebSocket numa aba sem player e ver se o saldo sobe (isso responderia "os sinais bastam?"). O teste foi barrado pela checagem automática de permissões (classificado como simular espectador na Kick) e não foi contornado. Para rodar, o Patrick precisa liberar explicitamente (regra de permissão) ou fazer o teste por conta própria. Se for testar: canal único, conta de teste, sinais iguais aos do site, saldo lido por `GET /api/v2/channels/<slug>/points`.

**Canais parceiros na Kick (lista do Patrick, 22/10):** yetz, guaxilol1, tftoddy, axt, rodil, mandiocaa, faturetosl, matta, luccatv, patopapao2, clovaobengalalol, biscoitao, techzz, dersin2k (14 canais). A lista oficial fica dentro do jogo, atrás do login; conferir lá se mudar.

**Decisão pendente:** imitar o sinal de espectador sem vídeo seria simular presença (a Kick pode tratar como viewbot, e a conta fica vinculada para sempre a um treinador). Alternativa dentro das regras: player real por canal, mas o mais leve possível (mudo, qualidade mínima, só nos canais necessários).
