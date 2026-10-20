$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$destination=Join-Path $repo ('_backups/resumo-phase-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$paths=@('Services/Reports/PmoExecutiveData.cs','Services/Reports/GestaoVistaReportRepository.cs','ClientApp/src/features/production/analytics/ResumoProducaoPage.tsx','ClientApp/src/features/production/analytics/ResumoTvTestePage.tsx','ClientApp/src/features/production/analytics/resumoTvTeste.css')
foreach($path in $paths){
  $target=Join-Path $destination $path
  New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $repo $path) -Destination $target
}
Write-Output $destination
