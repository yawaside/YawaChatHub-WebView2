<#
.SYNOPSIS
  Синхронизирует номер версии из файла VERSION в src/version.ts и desktop .csproj.

.DESCRIPTION
  Исправление ошибки CI «ParserError … Unexpected token '$v\"'»:
  в старом release.yml replacement-строка писалась как "\"$v\"" — в PowerShell
  обратный слэш НЕ экранирует кавычки, поэтому парсер pwsh падал ещё до запуска.
  Вся логика вынесена сюда, где кавычки пишутся нативно для PowerShell:

      '"' + $Version + '"'      # или  "`"$Version`""

  Workflow теперь просто вызывает этот скрипт:
      scripts/sync-version.ps1 -Version "4.0.1"

.PARAMETER Version
  Номер версии вида 4.0.1 (без префикса v).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+$')]
  [string]$Version
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
  $quoted = '"' + $Version + '"'

  # src/version.ts — единый источник номера версии в UI
  $versionTs = Join-Path $Root 'src/lib/version.ts'
  if (-not (Test-Path $versionTs)) { $versionTs = Join-Path $Root 'src/version.ts' }
  if (Test-Path $versionTs) {
    (Get-Content $versionTs) -replace '"[\d.]+"', $quoted |
      Set-Content $versionTs -Encoding utf8
    Write-Host "version.ts -> $Version"
  } else {
    Write-Warning 'src/version.ts не найден — пропускаю'
  }

  # desktop csproj <Version>…</Version>
  $csproj = Join-Path $Root 'desktop/YawaChatHub/YawaChatHub.csproj'
  if (Test-Path $csproj) {
    (Get-Content $csproj) -replace '<Version>.*?</Version>', "<Version>$Version</Version>" |
      Set-Content $csproj -Encoding utf8
    Write-Host "csproj -> $Version"
  } else {
    Write-Warning 'desktop csproj не найден — пропускаю'
  }
}
finally {
  Pop-Location
}
