# Distribuir o BookIdle (instalador + atualização automática)

Como o BookIdle chega ao computador de quem usa e como cada versão nova chega até a pessoa sozinha (guia de quem mantém o projeto).

## Como funciona

- **Instalador:** o `BookIdle-win-Setup.exe`. A pessoa baixa uma vez e roda. Instala só para ela (sem pedir administrador) em
  `%LOCALAPPDATA%\BookIdle`, cria atalhos e instala o WebView2 se faltar.
- **Atualização:** o app instalado olha as **Releases** de um repositório do GitHub 30 s depois de abrir e de hora em hora
  (e no menu da bandeja, "Verificar atualizações"). Se houver versão nova, pergunta "Atualizar agora?"; ao aceitar, baixa só
  a diferença (centenas de KB, não os 80 MB), fecha, troca os arquivos e abre de novo.
- **Dados de cada pessoa ficam separados:** contas, logins, regras e configurações moram em `%LOCALAPPDATA%\IdleDeckPessoal`,
  fora da pasta do app. Atualizar ou reinstalar não apaga nada disso.
- Uma cópia que **não** veio do instalador (a do Visual Studio, a pasta `bin`) nunca se atualiza sozinha.

## Configuração (uma vez só)

1. **Repositório público** onde ficam as Releases (e o código aberto). Ex.: `https://github.com/seu-usuario/BookIdle-releases`.
   Público porque o app instalado lê as Releases sem senha. O endereço fica em `AppUpdater.RepoUrl`.
2. **Endereço no código:** em `src/IdleDeck.App/Updates/AppUpdater.cs`, preencha `RepoUrl` com o endereço acima.
   **Faça isto ANTES do primeiro lançamento:** uma versão sem o endereço nunca procura atualização.
3. **Ferramenta de empacotar:**
   `dotnet tool install -g vpk --version 1.2.161` (a mesma versão do pacote `Velopack` do app).
4. **Token do GitHub** para publicar: GitHub → Settings → Developer settings → Personal access tokens → *Fine-grained*,
   só o repositório de releases, permissão **Contents: Read and write**. Guarde; ele não vai para o código.

## Lançar uma versão (a primeira e as seguintes)

No PowerShell, na pasta do projeto:

```powershell
$env:VPK_TOKEN = "<seu token>"
.\tools\release\release.ps1 -Version 1.0.0 -RepoUrl https://github.com/seu-usuario/BookIdle-releases -Upload
```

O script roda os testes (se algum falhar, não lança), gera o pacote, baixa as versões anteriores para calcular só a
diferença, cria o instalador e publica a Release `v1.0.0`. Sem `-Upload`, só gera os arquivos em `tools\release\out\Releases`
e não publica nada.

- **Versão nova:** repita com um número maior (`-Version 1.0.1`). Quem já tem o app recebe sozinho.
- **Quem for instalar** baixa o `BookIdle-win-Setup.exe` da página de Releases do repositório.
- Os números seguem `1.2.3`; só pode subir (o app ignora versão igual ou menor).

## Avisos

- **Windows SmartScreen:** o instalador não tem assinatura digital paga, então na primeira vez o Windows mostra "O Windows
  protegeu seu PC". É só clicar em **Mais informações → Executar assim mesmo**. Some com certificado de código (pago).
- **Teste a atualização antes de publicar para todo mundo:** instale a 1.0.0 no seu PC, publique a 1.0.1 e veja se o aviso aparece.
- **Testar sem publicar:** defina `BOOKIDLE_UPDATE_SOURCE` com uma pasta (ou endereço) de Releases; o app usa essa origem
  no lugar do GitHub. Foi assim que a atualização foi provada de ponta a ponta antes de existir repositório.
- **Cada pessoa joga com as próprias contas.** O limite do jogo (4 contas por pessoa) vale para cada um.

## Ver e testar antes de publicar (ferramentas de desenvolvimento)

Você pode ter duas cópias ao mesmo tempo: a **instalada** (a de verdade, com as suas contas) e uma de **teste**.

**1. Ver o código novo, em uma cópia separada** — `.	ools\devun-dev.ps1`
- Compila o código que está na pasta agora e abre uma cópia de teste com **dados separados**
  (`%LOCALAPPDATA%\BookIdleDev`): outras contas, outros logins, outras regras. A janela e a bandeja mostram **(TESTE)**.
- Convive com o BookIdle instalado e não mexe em nada dele. `-Fresh` apaga os dados de teste e começa do zero.
- **Cuidado com o jogo:** o limite é de 4 contas conectadas por rede. Não entre com as mesmas contas na cópia de teste
  enquanto o instalado estiver com elas abertas (seriam 8 conexões e pode dar ban). Teste como visitante ou com uma conta
  de teste.

**2. Provar a atualização no instalado, sem publicar** — `.	ools\dev	est-update.ps1 -Version 1.0.3`
- Gera o pacote da versão nova (sem publicar), fecha o BookIdle instalado e abre de novo apontando para esse pacote local.
  Em até 30 s aparece "Atualização disponível"; você clica em Sim e vê a atualização acontecer de verdade.
- `-NoLaunch` só gera o pacote, sem mexer no app instalado.
- Regra de ouro: publique a **mesma versão e o mesmo código** que você testou. Se mudar o código, use um número novo.

**Fluxo recomendado para cada versão:** mexer no código → `run-dev.ps1` para ver → `test-update.ps1 -Version X` para provar a
atualização → `release.ps1 -Version X ... -Upload` para publicar.
