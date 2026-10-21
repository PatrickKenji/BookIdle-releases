# Como o mercado do pokeidle.io fala (notas para quem quer contribuir)

Medido em 2026-10-21, versão do jogo 1.345. O jogo muda: se algo aqui parar de bater, abra uma issue.
O BookIdle **não** inventa pedidos: ele usa o mesmo canal (o WebSocket que o próprio jogo abre) e as mesmas mensagens que a tela do
Mercado do jogo já usa.

## Canal

Um WebSocket (`wss://pokeidle.io/`) com mensagens JSON `{"t": "..."}`. Uma ponte injetada na página
(`src/IdleDeck.App/Scripts/market-bridge.js`) embrulha o WebSocket **que o jogo já abriu** (nada de segunda conexão): repassa ao app só o
que o sniper precisa (respostas do mercado, eventos de compra, saldo e relógio do servidor) e deixa o app mandar pedidos `market.*` pela
mesma conexão. Token, cookie e o `hello` nunca saem da página.

## Mensagens

| Para quê | Envia | Recebe |
|---|---|---|
| Resumo de todos os itens | `{"t":"market.itens","elementos":[],"categoria":""}` | `{"t":"market","aba":"itens","resumo":{"<itemId>":{"anuncios","unidades","minGold","minOrb"}}}` |
| Ofertas de um item | `{"t":"market.item","itemId":70060,"moeda":"gold"\|"orb"}` | `{"t":"market","aba":"item","itemId","moeda","linhas":[oferta…]}` |
| Resumo de Pokémon por espécie | `{"t":"market.especies","tipo":"pokemon","moeda":"","ordem":"recentes\|baratos","soShiny":false,"soP5":false,…,"pagina":0}` | `{"t":"market","aba":"especies","total","porPagina":24,"pagina","linhas":[{"speciesId","nome","anuncios","shinys","minGold","minOrb","temP5",…}]}` |
| Ofertas de Pokémon | `{"t":"market.listar","tipo":"pokemon",…mesmos filtros…,"pagina":0,"especieId":6}` (o `especieId` pode ser omitido: o jogo lista o mercado todo, já com os filtros) | `{"t":"market","aba":"vitrine","total","linhas":[oferta…]}` |
| Comprar | `{"t":"market.comprar","id":<oferta>,"qtd":1,"preco":<preço por unidade>,"moeda":"gold"\|"orb","sorteioUi":1}` | veja "Resposta da compra" |

**Oferta:** `id`, `vendedor`, `tipo` (`item`/`pokemon`), `itemId`, `pokemonId`, `qtd`, `preco` (**por unidade**), `moeda` (`gold` = coins,
`orb` = gemas), `estado:"aberto"`, `criadoEm`, `compravelEm` (fim da retenção de 2 min), `ficha`. Em Pokémon a `ficha` traz `nota`, `level`,
`shiny`, `ivs`, `ivTotal`, `quality`, `potencia`, `tmAoe`, `poder`.

**Shiny só existe em gemas** (`minGold: null` nos shinies).

## Resposta da compra

Chega em eventos dentro de um quadro `{"t":"batalha","ev":[…]}`:

- **Com sorteio** (anúncio recém-liberado: pedidos nos primeiros 3 s depois de `compravelEm` entram num sorteio, com chance igual):
  `{"k":"mktSorteio","fase":"entrou","id":…,"liberouEm","fechaEm","sorteioAte"}` → `fase:"lista"` → `fase:"fim","ok":true|false` e
  `{"k":"marketComprado","id":…,"total","moeda","sorteio":true}`.
- **Direta** (anúncio já liberado): só `{"k":"marketComprado","descricao","total","moeda",…}`, **sem `id` da oferta**. Por isso o
  sniper confere o valor e a moeda contra o pedido que acabou de sair.
- O formato exato de `fim` quando se perde o sorteio (`perdeu`) ou o pedido é recusado (`msg`) segue o módulo do próprio cliente do jogo
  (`shared/sorteio-janela.mjs`).

## Outras fontes que o app lê da página do jogo

- **Catálogo de itens do mercado:** vem no `welcome` (`mercado.catalogo`); casas, boxes e bicicletas só existem nele. O resto está em
  `/assets/items.json`.
- **Saldo e relógio do servidor:** `welcome` e `estado` (`gold`, `orbs`, `servidorAgora`). O app só compra com saldo conhecido.
- **Dia sazonal:** `estado.diaSazonal.tipo`; as regras estão em `shared/dia-sazonal.mjs` do jogo e o `/hunt` usa essa função ao vivo.
- **Voto no TopIdle:** `POST /topidle/me` devolve `ultimoVotoEm`, `liberaEm` e `esperaMs`; o app só observa essa resposta.

Notas sobre o motor do `/hunt` e os módulos do jogo que ele importa: [pokeidle-notes.md](pokeidle-notes.md).
