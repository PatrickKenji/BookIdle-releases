# BookIdle

**🌐 Language / Idioma / Idioma:** **Português** · [English](README.en.md) · [Español](README.es.md)

**Gerenciador de contas e ferramentas para o [pokeidle.io](https://pokeidle.io)** — várias contas lado a lado, captura automática, voto no TopIdle, bônus de
live da Twitch, ranking de hunts e sniper do mercado. Programa de Windows, de código aberto e gratuito.

> **Projeto não oficial.** O BookIdle não tem ligação com o pokeidle.io, o TopIdle, a Twitch, a Nintendo ou a The Pokémon Company; todas as
> marcas são dos respectivos donos. Use por sua conta e risco e respeite as regras do jogo (veja [Uso responsável](#uso-responsável)).

<p align="center">
  <img src="docs/img/janela-principal.png" alt="Janela principal do BookIdle: contas na lateral, com os botões de voto e de Twitch, e o jogo ao lado" width="760">
</p>

<p align="center">
  <img src="docs/img/hunt.png" alt="Painel do /hunt: a melhor hunt por conta e o ranking de hunts por XP/h e ouro/h" width="760">
</p>

<p align="center">
  <img src="docs/img/sniper.png" alt="Painel do Sniper do mercado: nova regra, radar de Pokémon, regras ativas, proteção de saldo e feed" width="760">
</p>

<sub>Capturas com dados de exemplo (contas, saldos e hunts fictícios). O próprio app está disponível em português, inglês e espanhol.</sub>

---

## Baixar e instalar

1. Baixe o **[BookIdle-win-Setup.exe](https://github.com/PatrickKenji/BookIdle-releases/releases/latest/download/BookIdle-win-Setup.exe)**
   (ou veja todas as versões em [Releases](https://github.com/PatrickKenji/BookIdle-releases/releases)).
2. Rode o instalador. Ele instala só para o seu usuário (sem pedir administrador) e instala o WebView2 se faltar.
3. Na primeira vez o Windows pode mostrar **"O Windows protegeu seu PC"**: o instalador não tem assinatura digital paga. Clique em
   **Mais informações → Executar assim mesmo**. Se preferir, o código está aqui para você conferir e compilar sozinho.

O BookIdle **avisa quando há versão nova**: ele confere ao abrir e de hora em hora, mostra o que muda e só atualiza quando você clica em
**Atualizar**, baixando só o que mudou. Depois de reiniciar, mostra as novidades da versão em detalhe.

Precisa de **Windows 10 ou 11**.

---

## Privacidade e segurança

**Os autores do BookIdle não têm acesso às suas contas, senhas nem dados.** Não existe servidor nosso: o app roda inteiro no seu
computador e **nada é enviado para nós**.

- **Tudo fica no seu PC**, na pasta `%LOCALAPPDATA%\BookIdleData`: configurações, regras, logins das contas e senhas.
- **Cada conta tem o próprio navegador isolado** (WebView2): as sessões e os cookies de uma conta não se misturam com os das outras nem com
  o seu navegador.
- **Cofre de senhas opcional**, criptografado no seu PC: com o seu usuário do Windows (DPAPI) ou com uma **senha mestra** (AES-256-GCM,
  chave derivada por PBKDF2 com 600 mil iterações). Para contas **Google**, o BookIdle nunca digita a senha do Google: no máximo escolhe a conta
  na lista, e o resto fica com você.
- **Sem telemetria, sem análise de uso, sem relatório de erros enviado.** Os endereços que o app contata são só estes:
  - `pokeidle.io` — o próprio jogo e a lista pública de itens (`/assets/items.json`);
  - `topidle.com` — a página de voto, na janela de voto (quem vota é você; o app não clica em nada);
  - `twitch.tv` e `irc-ws.chat.twitch.tv` — só se você conectar a Twitch (veja abaixo);
  - `github.com` — conferir se há versão nova e baixá-la;
  - `livepix.gg` — só se você clicar em "Apoiar".
- **O código é aberto**: qualquer pessoa pode ler e conferir tudo isso aqui. Baixe o BookIdle só desta página.

**Sobre a Twitch:** você entra na Twitch pelo próprio site, dentro do navegador da conta. O app usa o login que ficou ali (no seu PC) para
entrar nos chats dos canais de bônus com uma conexão leve, **sem abrir um navegador por canal**. Esse login só é enviado à Twitch.

---

## Funcionalidades para o pokeidle.io

### Contas lado a lado
- **Várias contas** no mesmo programa, cada uma num navegador isolado, com o estado de cada uma (online, memória) à vista.
- **Três modos de janela:** **Abas**, **Grade** (todas as contas visíveis) e **1+N** (uma grande e as outras pequenas). Atalhos: `Ctrl+1` a
  `Ctrl+3`.
- **Arrastar para reordenar** as contas, na lista da lateral e nas janelas.
- **Botão de expandir (⛶)** em cada janela: vira o modo Abas já na conta escolhida.
- **Som:** mutar tudo, ou mutar cada conta (🔊/🔇 no cabeçalho de cada janela e no menu da conta).
- **Tema** Arcade (roxo) ou Escuro (`Ctrl+T`), **menu recolhível** (`Ctrl+B`) e **zoom** do jogo por perfil.
- **Bandeja do Windows:** minimizar manda o app para a bandeja; opção de **iniciar com o Windows** já minimizado.
- **Memória sob controle:** mostra a RAM de cada conta, recarrega sozinha a que passar do limite que você definir e tem o botão
  **Resetar perfil**. Contas escondidas não são desaceleradas (opção).
- **Modo foto** em Grade e 1+N: só a conta em destaque fica ao vivo; as outras viram uma foto atualizada a cada 10 s (ou 5/30) e o jogo
  segue rodando por trás. As contas são desenhadas sem a placa de vídeo, o que gasta bem menos RAM (dá para religar nas Configurações).
- **Bloqueio de anúncios e rastreadores** configurável por lista de domínios.
- **Captura automática por conta** (para quem não tem o VIP): o botão **Captura auto** em cada conta (ou o 🎯 na barrinha da janela)
  faz o app clicar sozinho nos pokémons caídos daquela conta, a cada segundo. Começa desligado e fica salvo por conta.
- **Login automático opcional:** preenche o login do jogo com o que está no Cofre e, no Google, escolhe a conta na lista.
- **Perfis de jogo:** além do PokeIdle você pode criar perfis para outros jogos web (veja [Outros jogos](#outros-jogos-idle)).

### Votar no TopIdle
- Um botão **"Votar no TopIdle"** em cada conta, que abre a página de voto **com a sessão daquela conta**. Você vota; o BookIdle **não clica
  por você**.
- O botão mostra **quanto falta** para poder votar de novo ("Votar em 5 h"), lendo o que o próprio jogo diz — vale até para votos feitos
  fora do app — e acende em amarelo quando já dá para votar.

### Bônus de live da Twitch
- **Conectar Twitch** em cada conta (cada uma com a sua Twitch), com a lista de canais de bônus do perfil.
- A conta fica presente no chat dos canais **sem você abrir nenhuma live** e sem gastar a memória de um navegador por canal.
- Ícone de estado por conta (conectada em todos os canais, parcial, sem chave).

### `/hunt` — qual hunt rende mais
- Mostra, **para o Pokémon que está em campo em cada conta**, as melhores hunts por **XP/h** e por **ouro/h líquido** (já descontando
  poções, revives e bolas que a conta gasta), com o risco de cada uma e as próximas que você destrava.
- Usa **as fórmulas e o catálogo do próprio jogo, ao vivo** — quando o jogo muda, o cálculo acompanha.
- Entende o **dia sazonal** (o tipo do dia dobra ataque, defesa e vida na hunt do mapa) e deixa ajustar os alvos por golpe do TM AoE.

### Sniper do mercado
- Compra sozinho, **em segundo plano**, ofertas do Mercado da Comunidade que batam com as suas regras — sem você olhar o mercado. As
  contas continuam jogando normalmente.
- **Regras por conta** (até 15): escolha o item ou o Pokémon numa busca, defina o **teto em coins** e o **teto em gemas** e a quantidade.
- **Radar global de Pokémon:** qualquer **shiny** (só em gemas), qualquer **P5** ou **nota mínima**, com os seus tetos.
- **Proteção de saldo:** uma reserva em coins e em gemas que o sniper nunca gasta.
- Respeita a **retenção de 2 minutos** e o **sorteio** do mercado: nada de furar fila. Compra uma unidade por pedido e só confirma depois
  do aviso do jogo; mostra tudo num **feed de eventos** por conta e grava um `sniper.log`.
- Escolha **quais contas** fazem o snipe (uma ou todas); duas contas nunca disputam a mesma oferta.

### E também
- **Atualização automática** (acima) e **botão de apoio** discreto no menu.

---

## Uso responsável

- **Respeite as regras do jogo.** O pokeidle.io limita as contas por pessoa (hoje, **4 por pessoa**, somando todos os computadores e redes) e
  pune quem passa disso. O BookIdle deixa criar mais por perfil; **o limite é seu, não do programa**. Leia os termos do jogo.
- O jogo permite automação que siga as mesmas regras de quem joga na mão (sem explorar falhas e sem sobrecarregar o servidor). O sniper foi
  feito assim; ainda assim **você é responsável por como usa**.
- **O sniper gasta moedas e gemas de verdade do jogo.** Comece com regras pequenas e uma reserva de saldo alta, e acompanhe o feed. Compra
  feita com preço errado não é desfeita pelo jogo.
- Sem garantias: o programa é fornecido "como está" (veja a [licença](LICENSE)). O jogo muda de vez em quando e algo pode quebrar até
  alguém corrigir — abra uma issue.

---

## Outros jogos idle

O BookIdle já abre **qualquer jogo web em perfis** (cada perfil com o seu limite de contas, janelas, memória, bandeja e login), então
**serve como gerenciador de várias contas de outros jogos idle** hoje mesmo. As ferramentas especiais (voto, `/hunt`, sniper, Twitch) são do
pokeidle.io.

**Vamos trabalhar em ferramentas para outros jogos idle se o pessoal quiser.** Diga qual jogo e o que faria diferença para você abrindo uma
[issue](https://github.com/PatrickKenji/BookIdle-releases/issues) ou uma conversa em
[Discussions](https://github.com/PatrickKenji/BookIdle-releases/discussions).

---

## Compilar e contribuir

Precisa do [SDK do .NET 10](https://dotnet.microsoft.com/download) e do Windows (o app é WPF + WebView2).

```powershell
dotnet build          # compila tudo
```

| Pasta | O que tem |
|---|---|
| `src/IdleDeck.Core` | lógica pura, sem tela: configurações, layouts, cofre, Twitch, voto, mercado, sniper |
| `src/IdleDeck.App` | o programa (WPF + WebView2), módulos `/hunt` e Sniper e os scripts injetados nas páginas |
| `docs/` | [protocolo do mercado](docs/pokeidle-mercado.md), [notas do `/hunt`](docs/pokeidle-notes.md) |

Sugestões e relatos de problemas são bem-vindos: abra uma [issue](https://github.com/PatrickKenji/BookIdle-releases/issues) ou
participe das [Discussions](https://github.com/PatrickKenji/BookIdle-releases/discussions). Para manter as atualizações seguras, **só quem
mantém o projeto altera o código** (pull requests de fora ficam desligados); você pode ler, copiar e modificar à vontade para uso próprio,
dentro da [licença](LICENSE).

---

## Licença e créditos

[MIT](LICENSE) — © 2026 Tickelicia. Você pode usar, copiar, modificar e distribuir.

Feito com [.NET](https://dotnet.microsoft.com), [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/), o
[WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) da Microsoft e o [Velopack](https://velopack.io) (instalador e
atualizações). Fontes: *Press Start 2P* e *Space Grotesk* (licença OFL, em `src/IdleDeck.App/Resources/Fonts`).

Gostou? O botão **♥ Apoiar** no menu do app leva à [página de apoio](https://livepix.gg/tickelicia).
