$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$statePath=Join-Path $repo '_build_verify/resumo/deployment.json'
$state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$artifact=[System.IO.Path]::GetFullPath($state.backendArtifact)
$allowed=[System.IO.Path]::GetFullPath((Join-Path $repo '_build_verify/resumo-serving'))+[System.IO.Path]::DirectorySeparatorChar
if(-not $artifact.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Artefato fora da instância isolada'}
$owner=@(Get-NetTCPConnection -State Listen -LocalPort 5288 -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
$process=Get-CimInstance Win32_Process -Filter "ProcessId=$($state.backendPid)"
if($owner.Count -ne 1 -or $owner[0] -ne $state.backendPid -or $process.Name -ne 'dotnet.exe' -or -not $process.CommandLine.Contains($artifact)){throw 'A instância isolada mudou; nenhum processo foi interrompido'}
$shared=@(Get-NetTCPConnection -State Listen -LocalPort 5277 | Select-Object -ExpandProperty OwningProcess -Unique)
$directory=Split-Path $artifact -Parent
foreach($name in @('appsettings.json','appsettings.Development.json')){
  $source=Join-Path $repo $name
  if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $directory $name)}
}
$settings=Get-Content -LiteralPath (Join-Path $repo 'appsettings.json') -Raw | ConvertFrom-Json
$env:ConnectionStrings__LocalConnection=$settings.ConnectionStrings.LocalConnection
$env:Worker__RunOnStartup='false'
$env:RobotConfiguration__LegacyStartup='false'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5288'
# This PID belongs exclusively to the new isolated server created by this task.
Stop-Process -Id $state.backendPid -ErrorAction Stop
for($i=0;$i -lt 40;$i++){if(-not (Get-Process -Id $state.backendPid -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 100}
$started=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$artifact+'"')) -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $repo '_build_verify/resumo/backend-serving.log') -RedirectStandardError (Join-Path $repo '_build_verify/resumo/backend-serving.stderr.log')
$state.backendPid=$started.Id
$state | Add-Member -NotePropertyName configurationSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $directory 'appsettings.json')).Hash -Force
$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8
for($i=0;$i -lt 80;$i++){if(Get-NetTCPConnection -State Listen -LocalPort 5288 -ErrorAction SilentlyContinue){break};if($started.HasExited){throw 'Backend isolado encerrou'};Start-Sleep -Milliseconds 300}
if(-not (Get-NetTCPConnection -State Listen -LocalPort 5288 -ErrorAction SilentlyContinue)){throw 'Backend isolado não abriu 5288'}
if(@(Get-NetTCPConnection -State Listen -LocalPort 5277 | Select-Object -ExpandProperty OwningProcess -Unique)[0] -ne $shared[0]){throw 'O servidor compartilhado mudou'}
Write-Output ('Backend isolado PID='+$started.Id+'; configuração real copiada; servidor compartilhado PID='+$shared[0]+' preservado.')
