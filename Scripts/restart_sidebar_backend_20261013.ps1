$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\N-A-Connect aa\N-A-Connect'
$live = Join-Path $root 'bin\Debug\net9.0'
$candidate = Join-Path $root 'Tests\.sidebar-validation\bin\AccIntegration.WebApp\debug'
$backup = Join-Path (Split-Path $root -Parent) ('_backups\sidebar_backend_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
$log = Join-Path $root 'Scripts\sidebar_restart_20261013.log'
$oldPid = 488452
$startedPid = $null
$stoppedOld = $false
$copied = $false
function Log([string]$m) { $line = ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m); Add-Content -Path $log -Value $line; Write-Host $line }
function StopApp([int]$target) {
  Stop-Process -Id $target -Force -ErrorAction Stop
  for ($i=0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    if (-not (Get-Process -Id $target -ErrorAction SilentlyContinue)) { return }
  }
  throw "PID $target nao parou"
}
function StartApp([string]$prefix) {
  $env:Worker__RunOnStartup = 'false'
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_URLS = 'http://localhost:5277'
  $exe = Join-Path $live 'AccIntegration.WebApp.exe'
  $p = Start-Process -FilePath $exe -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root "Scripts\sidebar_$prefix.stdout.log") -RedirectStandardError (Join-Path $root "Scripts\sidebar_$prefix.stderr.log") -PassThru
  Log "Backend iniciado PID=$($p.Id), Worker__RunOnStartup=false"
  return $p.Id
}
function AwaitApi {
  for ($i=0; $i -lt 25; $i++) {
    Start-Sleep -Seconds 1
    $listener = Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $listener) { continue }
    $code = 0
    try { $resp = Invoke-WebRequest -Uri 'http://localhost:5277/api/sidebar/access' -UseBasicParsing -TimeoutSec 3; $code = [int]$resp.StatusCode }
    catch { if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } }
    Log "Sidebar /api/sidebar/access HTTP=$code; porta5277 PID=$($listener.OwningProcess)"
    if ($code -eq 401) { return $true }
    if ($code -eq 404) { throw 'API sidebar ainda 404' }
  }
  return $false
}
try {
  Log 'Iniciando reinicio seguro solicitado para Sidebar'
  if ((Get-Content (Join-Path $root 'Scripts\sidebar_backend_build_20261013.exit') -Raw).Trim() -ne '0') { throw 'Compilacao nao validada' }
  if ((Get-Content (Join-Path $root 'Scripts\sidebar_security_tests_20261013.exit') -Raw).Trim() -ne '0') { throw 'Testes nao validados' }
  $fresh = Get-Item (Join-Path $candidate 'AccIntegration.WebApp.dll')
  if ($fresh.LastWriteTime -le (Get-Item (Join-Path $live 'AccIntegration.WebApp.dll')).LastWriteTime) { throw 'DLL candidata nao mais recente' }
  $original = Get-CimInstance Win32_Process | Where-Object ProcessId -eq $oldPid
  if (-not $original -or $original.ExecutablePath -ne (Join-Path $live 'AccIntegration.WebApp.exe')) { throw 'Processo ativo nao corresponde ao backend autorizado' }
  $listener = Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $listener -or $listener.OwningProcess -ne $oldPid) { throw 'Porta 5277 nao pertence ao PID esperado' }
  $external = Get-NetTCPConnection -OwningProcess $oldPid -State Established -ErrorAction SilentlyContinue | Where-Object { $_.RemoteAddress -notin @('::1','127.0.0.1') }
  if ($external) { throw 'Conexoes externas ativas detectadas; nao reiniciar durante automacao' }
  Log 'Processo antigo validado, sem conexoes externas estabelecidas'
  New-Item -ItemType Directory -Path $backup -Force | Out-Null
  $files = @('AccIntegration.WebApp.dll','AccIntegration.WebApp.exe','AccIntegration.WebApp.pdb','AccIntegration.WebApp.deps.json','AccIntegration.WebApp.runtimeconfig.json','AccIntegration.WebApp.staticwebassets.endpoints.json','AccIntegration.WebApp.staticwebassets.runtime.json')
  foreach ($file in $files) { if (Test-Path (Join-Path $live $file)) { Copy-Item -Path (Join-Path $live $file) -Destination (Join-Path $backup $file) -Force } }
  Log "Backup completo dos artefatos do app: $backup"
  StopApp $oldPid
  $stoppedOld = $true
  Log "Processo antigo $oldPid interrompido"
  foreach ($file in $files) { if (Test-Path (Join-Path $candidate $file)) { Copy-Item -Path (Join-Path $candidate $file) -Destination (Join-Path $live $file) -Force } }
  $copied = $true
  Log "Novos artefatos copiados, DLL=$($fresh.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"
  $startedPid = StartApp 'deployed_backend'
  if (-not (AwaitApi)) { throw 'API nao retornou 401 no teste anonimo; nao confirmado' }
  Log 'DEPLOY_SUCCESS=True; api/sidebar/access=401 sem login (endpoint existe e exige sessao)'
  Write-Output 'SIDEBAR_BACKEND_DEPLOY_OK'
  exit 0
}
catch {
  Log "ERRO: $($_.Exception.Message)"
  if ($stoppedOld) {
    try {
      if ($startedPid) { Stop-Process -Id $startedPid -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1 }
      foreach ($file in $files) { if (Test-Path (Join-Path $backup $file)) { Copy-Item -Path (Join-Path $backup $file) -Destination (Join-Path $live $file) -Force } }
      Log 'ROLLBACK: artefatos antigos restaurados'
      $rolledPid = StartApp 'rollback_backend'
      Log "ROLLBACK_PID=$rolledPid"
    } catch { Log "ROLLBACK_FAILED: $($_.Exception.Message)" }
  }
  exit 1
}
