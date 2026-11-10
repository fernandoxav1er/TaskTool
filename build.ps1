<#
.SYNOPSIS
  Gera o executavel do TaskTool (um unico .exe) e um .zip pronto para publicar.

.EXAMPLE
  .\build.ps1                         # precisa do .NET 10 Desktop Runtime na maquina (exe pequeno)
  .\build.ps1 -SelfContained          # roda em qualquer Windows x64, sem instalar nada (exe grande)
  .\build.ps1 -Version 1.1.0          # define a versao gravada no exe e no nome do zip
#>
param(
    [switch]$SelfContained,
    [string]$Version,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\TaskTool\TaskTool.csproj"

if (-not $Version) {
    # Usa a <Version> do .csproj
    $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$tipo = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$out = Join-Path $root "publish\$Runtime-$tipo"
$zip = Join-Path $root "publish\TaskTool-$Version-$Runtime$(if ($SelfContained) { '-self-contained' }).zip"

Write-Host "==> TaskTool $Version ($Runtime, $tipo)" -ForegroundColor Cyan

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$dotnetArgs = @(
    "publish", $proj,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $out,
    "--self-contained", $SelfContained.IsPresent.ToString().ToLower(),
    "-p:Version=$Version",
    "-nologo"
)
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (codigo $LASTEXITCODE)" }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

$exe = Get-Item (Join-Path $out "TaskTool.exe")
Write-Host ""
Write-Host "Pronto:" -ForegroundColor Green
Write-Host ("  exe: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB))
Write-Host ("  zip: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
