<#
.SYNOPSIS
  Gera os pacotes do Bordadox em dist\: versão portátil (.zip) e instalador (.exe).

.EXAMPLE
  powershell -File build\publicar.ps1                 # testes + portátil + instalador
  powershell -File build\publicar.ps1 -SemTestes      # pula os testes
  powershell -File build\publicar.ps1 -SemInstalador  # só o portátil
#>
param(
    [switch]$SemTestes,
    [switch]$SemInstalador,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
Set-Location $raiz

$props = [xml](Get-Content (Join-Path $raiz 'Directory.Build.props') -Raw -Encoding UTF8)
$versao = ($props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
$dist = Join-Path $raiz 'dist'
$pub = Join-Path $dist 'publish'
$nomePortatil = "Bordadox-$versao-portable"
$pastaPortatil = Join-Path $dist $nomePortatil

function Passo($t) { Write-Host "`n== $t" -ForegroundColor Cyan }
function Checar($t) { if ($LASTEXITCODE -ne 0) { throw "$t falhou (código $LASTEXITCODE)" } }

Write-Host "Bordadox $versao ($Runtime)"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

if (-not $SemTestes) {
    Passo 'Testes (Release)'
    dotnet test Bordadox.slnx -c Release --nologo -v q
    Checar 'dotnet test'
}

Passo "Publicando (autocontido, $Runtime)"
dotnet publish src\Bordadox.App\Bordadox.App.csproj -c Release -r $Runtime --self-contained true `
    -p:DebugType=none -p:DebugSymbols=false -o $pub --nologo -v q
Checar 'dotnet publish'

Passo 'Versão portátil'
Copy-Item $pub $pastaPortatil -Recurse
@"
Bordadox $versao - versão portátil

Extraia esta pasta em qualquer lugar e execute Bordadox.App.exe.
Não precisa instalar nada (o .NET já vem junto) nem de permissão de administrador.

As preferências ficam em %APPDATA%\Bordadox\prefs.json.
Para remover o programa, apague a pasta (e, se quiser, a pasta acima).
"@ | Set-Content (Join-Path $pastaPortatil 'LEIA-ME.txt') -Encoding UTF8
$zip = Join-Path $dist "$nomePortatil.zip"
Compress-Archive -Path $pastaPortatil -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $pastaPortatil -Recurse -Force

if (-not $SemInstalador) {
    Passo 'Instalador (Inno Setup)'
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 não encontrado. Instale em https://jrsoftware.org/isdl.php ou use -SemInstalador.' }
    & $iscc "/DVersao=$versao" "/DOrigem=$pub" "/DSaida=$dist" "/Q" (Join-Path $PSScriptRoot 'instalador.iss')
    Checar 'ISCC'
}

Remove-Item $pub -Recurse -Force
Passo 'Pronto'
Get-ChildItem $dist | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
