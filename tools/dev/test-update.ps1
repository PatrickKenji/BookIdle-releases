<#
.SYNOPSIS
  Prova uma atualização no BookIdle INSTALADO antes de publicar para todo mundo.

.DESCRIPTION
  1. Gera o pacote da versão nova (sem publicar nada) em tools\release\out\Releases.
  2. Fecha o BookIdle instalado e abre de novo apontando para esse pacote local (variável BOOKIDLE_UPDATE_SOURCE).
  3. Em até 30 s o app instalado mostra "Atualização disponível"; você clica em Sim e vê ele se atualizar de verdade.

  Se der tudo certo, publique com:  .\tools\release\release.ps1 -Version <a mesma> -RepoUrl <repositório> -Upload
  Regra de ouro: publique a MESMA versão e o MESMO código que você testou aqui. Se mudar o código, use um número novo.

  Obs.: depois da atualização o app instalado continua apontando para o pacote local até você fechá-lo e abri-lo pelo
  atalho de novo (aí ele volta a olhar o GitHub).

.EXAMPLE
  .\tools\dev\test-update.ps1 -Version 1.0.3
  .\tools\dev\test-update.ps1 -Version 1.0.3 -NoLaunch     # só gera o pacote, não mexe no app instalado
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RepoUrl = "https://github.com/PatrickKenji/BookIdle-releases",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$releaseOut = Join-Path $PSScriptRoot "..\release\out"
$releases = Join-Path $releaseOut "Releases"
$installed = Join-Path $env:LOCALAPPDATA "BookIdle\BookIdle.exe"

if (-not $NoLaunch -and -not (Test-Path $installed)) {
    throw "O BookIdle instalado não foi encontrado em $installed. Instale pelo BookIdle-win-Setup.exe primeiro."
}

# 1. Gera o pacote (com as versões anteriores baixadas, para o app instalado receber só a diferença).
& (Join-Path $PSScriptRoot "..\release\release.ps1") -Version $Version -RepoUrl $RepoUrl -OutDir $releaseOut
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "Não consegui gerar o pacote." }

if ($NoLaunch) {
    Write-Host "Pacote pronto em $releases (o app instalado não foi mexido)." -ForegroundColor Green
    return
}

# 2. Reabre o instalado apontando para o pacote local.
$running = Get-Process BookIdle -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$env:LOCALAPPDATA\BookIdle\*" }
if ($running) {
    Write-Host "Vou fechar o BookIdle instalado e abrir de novo para o teste. As contas reconectam em alguns segundos." -ForegroundColor Yellow
    $running | ForEach-Object { Stop-Process -Id $_.Id }
    Start-Sleep -Seconds 4
}

Write-Host "==> Abrindo o BookIdle instalado apontando para o pacote local" -ForegroundColor Green
$env:BOOKIDLE_UPDATE_SOURCE = (Resolve-Path $releases).Path
try { Start-Process $installed | Out-Null }
finally { Remove-Item Env:\BOOKIDLE_UPDATE_SOURCE -ErrorAction SilentlyContinue }
Write-Host "Espere até 30 s: deve aparecer 'Atualização disponível ($Version)'. Clique em Sim e confira a versão no menu da bandeja."
