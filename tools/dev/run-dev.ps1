<#
.SYNOPSIS
  Abre a cópia de TESTE do BookIdle (o código que está na sua pasta agora), separada da instalada.

.DESCRIPTION
  Compila o código atual numa pasta própria (tools\dev\out\build) e abre com dados SEPARADOS
  (%LOCALAPPDATA%\BookIdleDev): outras contas, outros logins, outras regras. Por isso ela convive com o BookIdle
  instalado e não mexe em nada dele. A janela mostra "(TESTE)" no título.

  CUIDADO COM O JOGO: o limite é de 4 contas CONECTADAS por rede. Não entre com as mesmas contas na cópia de teste
  enquanto o BookIdle instalado estiver com elas abertas (seriam 8 conexões e pode dar ban). Para testar, use uma conta
  de teste ou deixe as páginas como visitante.

.EXAMPLE
  .\tools\dev\run-dev.ps1            # compila e abre (reaproveita os dados da cópia de teste)
  .\tools\dev\run-dev.ps1 -Fresh     # apaga os dados da cópia de teste antes (começa do zero)
#>
param(
    [switch]$Fresh,
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA "BookIdleDev")
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$build = Join-Path $PSScriptRoot "out\build"

# Fecha a cópia de teste anterior (só a de teste, pelo caminho; nunca a instalada nem a de desenvolvimento).
Get-Process BookIdle -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$build*" } | ForEach-Object {
    Write-Host "==> Fechando a cópia de teste anterior ($($_.Id))" -ForegroundColor Cyan
    Stop-Process -Id $_.Id -Force
    Start-Sleep -Seconds 2
}

Write-Host "==> Compilando o código atual" -ForegroundColor Cyan
dotnet build (Join-Path $root "src\IdleDeck.App") -c Debug -o $build --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "A compilação falhou; nada foi aberto." }

if ($Fresh -and (Test-Path $DataDir)) {
    Write-Host "==> Apagando os dados da cópia de teste ($DataDir)" -ForegroundColor Yellow
    Remove-Item $DataDir -Recurse -Force
}
New-Item -ItemType Directory -Force $DataDir | Out-Null

Write-Host "==> Abrindo a cópia de teste (dados em $DataDir)" -ForegroundColor Green
$env:BOOKIDLE_HOME = $DataDir
try { Start-Process (Join-Path $build "BookIdle.exe") | Out-Null }
finally { Remove-Item Env:\BOOKIDLE_HOME -ErrorAction SilentlyContinue }
Write-Host "Pronto. A janela diz (TESTE) no título. Para fechar: ícone na bandeja → Sair."
