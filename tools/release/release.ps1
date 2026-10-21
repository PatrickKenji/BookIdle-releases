<#
.SYNOPSIS
  Gera o instalador e os arquivos de atualização do BookIdle (Velopack).

.EXAMPLE
  .\tools\release\release.ps1 -Version 1.0.1                      # só gera os arquivos em tools\release\out\Releases
  .\tools\release\release.ps1 -Version 1.0.1 -Upload              # gera e publica no GitHub (precisa do token)

.NOTES
  Pré-requisitos (uma vez só):
    dotnet tool install -g vpk --version <a mesma versão do pacote Velopack do app>
  Para publicar (-Upload):
    $env:VPK_TOKEN = "<token do GitHub com permissão de escrever no repositório de releases>"
    e o endereço do repositório em -RepoUrl (ou na variável VPK_REPO_URL).
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RepoUrl = $env:VPK_REPO_URL,
    [string]$OutDir = "",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "out" }
$publish = Join-Path $OutDir "publish"
$releases = Join-Path $OutDir "Releases"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "A versão precisa ser no formato 1.2.3 (recebi '$Version')." }
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "A ferramenta 'vpk' não está instalada. Rode: dotnet tool install -g vpk --version <versão do pacote Velopack>"
}
if ($Upload -and -not $RepoUrl) { throw "Para publicar, informe -RepoUrl (ex.: https://github.com/seu-usuario/BookIdle-releases)." }
if ($Upload -and -not $env:VPK_TOKEN) { throw "Para publicar, defina `$env:VPK_TOKEN com o token do GitHub." }

# 1. Testes: só lança o que passa.
Write-Host "==> Rodando os testes" -ForegroundColor Cyan
dotnet test (Join-Path $root "tests\IdleDeck.Core.Tests") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Os testes falharam; nada foi gerado." }

# 2. Pacote do app, com o .NET dentro (o amigo não precisa instalar nada além do WebView2, que o Windows já traz).
Write-Host "==> Publicando a versão $Version" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\IdleDeck.App") -c Release -r win-x64 --self-contained `
    -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "O dotnet publish falhou." }

# 3. Baixa as versões anteriores (para gerar só a diferença, que é o que o app do amigo baixa nas atualizações).
New-Item -ItemType Directory -Force $releases | Out-Null
if ($RepoUrl) {
    Write-Host "==> Baixando as versões anteriores de $RepoUrl" -ForegroundColor Cyan
    $downloadArgs = @("download", "github", "--repoUrl", $RepoUrl, "--outputDir", $releases)
    if ($env:VPK_TOKEN) { $downloadArgs += @("--token", $env:VPK_TOKEN) }
    & vpk @downloadArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning "Não consegui baixar as versões anteriores (normal na primeira versão). Seguindo sem elas." }
}

# 4. Instalador + atualização.
Write-Host "==> Gerando o instalador" -ForegroundColor Cyan
vpk pack --packId BookIdle --packVersion $Version --packDir $publish --mainExe BookIdle.exe `
    --runtime win-x64 --framework webview2 `
    --packTitle "BookIdle" --packAuthors "BookIdle" `
    --icon (Join-Path $root "src\IdleDeck.App\Resources\AppIcon.ico") `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "O vpk pack falhou." }

# 5. Publica (só se pedido).
if ($Upload) {
    Write-Host "==> Publicando no GitHub" -ForegroundColor Cyan
    vpk upload github --repoUrl $RepoUrl --token $env:VPK_TOKEN --publish `
        --releaseName "BookIdle $Version" --tag "v$Version" --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "O upload falhou." }
    Write-Host "Pronto: a versão $Version está no ar. Quem já tem o app recebe sozinho." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Arquivos prontos em: $releases" -ForegroundColor Green
    Write-Host "O instalador para o seu amigo é o BookIdle-win-Setup.exe. Nada foi publicado (use -Upload para publicar)."
}
