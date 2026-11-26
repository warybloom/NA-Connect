$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$state=Join-Path $repo '_build_verify/resumo/deployment.json'
foreach($port in @(5288,5289)){
  if(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue){throw "Porta $port ocupada; nenhum processo foi interrompido."}
}
$validatedDirectory=Join-Path $repo '_build_verify/resumo-tests'
$artifact=Join-Path $validatedDirectory 'AccIntegration.WebApp.dll'
if(-not (Test-Path -LiteralPath $artifact)){throw 'Backend final validado não encontrado'}
$frontend=Join-Path $repo '_build_verify/resumo-vite/index.html'
if(-not (Test-Path -LiteralPath $frontend)){throw 'Frontend final não encontrado'}
$backup=Join-Path $repo ('_backups/resumo-ready-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach($relative in @('bin/Debug/net9.0/AccIntegration.WebApp.dll','bin/Debug/net9.0/AccIntegration.WebApp.pdb','ClientApp/vite.config.ts','Services/Reports/ResumoPhaseFacts.cs','Services/Reports/GestaoVistaReportRepository.cs')){
  $source=Join-Path $repo $relative
  if(Test-Path -LiteralPath $source){$destination=Join-Path $backup $relative;New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null;Copy-Item -LiteralPath $source -Destination $destination}
}
$live=@(Get-NetTCPConnection -State Listen -LocalPort 5277 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
$serving=Join-Path $repo ('_build_verify/resumo-serving/backend-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $serving -Force | Out-Null
Get-ChildItem -LiteralPath $validatedDirectory | Copy-Item -Destination $serving -Recurse
foreach($configName in @('appsettings.json','appsettings.Development.json')){
  $source=Join-Path $repo $configName
  if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $serving $configName)}
}
$artifact=Join-Path $serving 'AccIntegration.WebApp.dll'
$settings=Get-Content -LiteralPath (Join-Path $repo 'appsettings.json') -Raw | ConvertFrom-Json
$env:ConnectionStrings__LocalConnection=$settings.ConnectionStrings.LocalConnection
$env:Worker__RunOnStartup='false'
$env:RobotConfiguration__LegacyStartup='false'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5288'
$backend=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$artifact+'"')) -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $repo '_build_verify/resumo/backend-serving.log') -RedirectStandardError (Join-Path $repo '_build_verify/resumo/backend-serving.stderr.log')
$node=Start-Process -FilePath (Get-Command node).Source -ArgumentList @('scripts/serve-resumo.mjs') -WorkingDirectory (Join-Path $repo 'ClientApp') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $repo '_build_verify/resumo/frontend-serving.log') -RedirectStandardError (Join-Path $repo '_build_verify/resumo/frontend-serving.stderr.log')
$record=@{backendPid=$backend.Id;frontendPid=$node.Id;backend='http://localhost:5288';frontend='http://localhost:5289';backendArtifact=$artifact;backendSha256=(Get-FileHash -LiteralPath $artifact).Hash;backup=$backup;preservedBackendPids=$live;workerRunOnStartup=$false;startedAt=(Get-Date -Format o)}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state -Encoding UTF8
for($i=0;$i -lt 80;$i++){
  if($backend.HasExited -or $node.HasExited){throw 'Instância de teste encerrou; consultar logs em _build_verify/resumo'}
  $backendReady=Get-NetTCPConnection -State Listen -LocalPort 5288 -ErrorAction SilentlyContinue
  $frontendReady=Get-NetTCPConnection -State Listen -LocalPort 5289 -ErrorAction SilentlyContinue
  if($backendReady -and $frontendReady){break}
  Start-Sleep -Milliseconds 300
}
if(-not ($backendReady -and $frontendReady)){throw 'Instância de teste não abriu as duas portas'}
if(@(Get-NetTCPConnection -State Listen -LocalPort 5277 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)[0] -ne $live[0]){throw 'O servidor compartilhado mudou durante a preparação; verificar concorrência'}
Write-Output ($record | ConvertTo-Json -Depth 5)
