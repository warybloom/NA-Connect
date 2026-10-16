# BI CHS001 (Original) — implantação CONTROLADA do backend com o módulo isolado (Services/BiChs001Schedule + BiChs001ScheduleController).
# Mesmo padrão de Scripts/Deploy-TvTelasProducao-20261015.ps1: troca só AccIntegration.WebApp.dll/.pdb no bin vivo, com backup,
# Worker__RunOnStartup=false (o robô NÃO dispara ao subir), verificação de 401 anônimo nas rotas e rollback automático.
# Pré-condições: candidato compilado e validado em $candidate; nenhum job crítico rodando; porta 5277 com o processo esperado.
# Uso: powershell -ExecutionPolicy Bypass -File Scripts\Deploy-BiChs001Isolado-20261015.ps1 [-Candidato <pasta>]
param([string]$Candidato = 'C:\Dev\N-A-Connect aa\_build_verify\backend_bi_isolado')
$ErrorActionPreference = 'Stop'
$root = 'C:\Dev\N-A-Connect aa\N-A-Connect'
$live = Join-Path $root 'bin\Debug\net9.0'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = "C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\binarios_$stamp"
$log = 'C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\inventario\deploy_backend.log'
$files = @('AccIntegration.WebApp.dll', 'AccIntegration.WebApp.pdb')
$projeto = 'b.fcdb7ebd-1b23-440d-a04b-90391098522e'
function Log([string]$m) { $line = (Get-Date -Format s) + ' ' + $m; Add-Content -LiteralPath $log -Value $line; Write-Output $line }
$oldPid = $null; $started = $null
try {
    # 1) Jobs/robô: nada crítico rodando (tarefas agendadas e escrita recente de log de execução do robô).
    $jobs = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.State -eq 'Running' -and $_.TaskName -match 'Planejamento|GRD|ACC|Rob[oô]|Sincronizar' -and $_.TaskName -notmatch 'Cloudflare' })
    if ($jobs.Count -gt 0) { throw 'Job agendado crítico em execução' }
    $logsRobo = @(Get-ChildItem (Join-Path $root 'Logs'), 'C:\Robo_NA_Connect\Logs_Historico' -File -ErrorAction SilentlyContinue) | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($logsRobo -and $logsRobo.LastWriteTime -gt (Get-Date).AddMinutes(-5)) { throw "Log do robô gravado há menos de 5 min ($($logsRobo.Name)) — possível ciclo em andamento" }
    # 2) Porta 5277: um único dono, o executável do bin vivo.
    $listeners = @(Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue)
    $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($processIds.Count -ne 1) { throw 'Dono da porta 5277 ausente ou ambíguo' }
    $oldPid = [int]$processIds[0]
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$oldPid"
    if (!$proc -or $proc.Name -ne 'AccIntegration.WebApp.exe' -or $proc.ExecutablePath -ne (Join-Path $live 'AccIntegration.WebApp.exe')) { throw 'Processo inesperado na porta 5277' }
    foreach ($f in $files) { if (!(Test-Path -LiteralPath (Join-Path $Candidato $f)) -or !(Test-Path -LiteralPath (Join-Path $live $f))) { throw "Artefato ausente: $f" } }
    # 3) Backup dos binários vivos.
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $live $f) -Destination (Join-Path $backup $f) -Force }
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $live $files[0])).Hash
    $newHash = (Get-FileHash -LiteralPath (Join-Path $Candidato $files[0])).Hash
    Log "PREFLIGHT oldPID=$oldPid backup=$backup oldDll=$oldHash newDll=$newHash"
    # 4) Troca e reinício (robô desligado no startup).
    Stop-Process -Id $oldPid -Force -ErrorAction Stop
    for ($i = 0; $i -lt 40; $i++) { if (!(Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 250 }
    if (Get-Process -Id $oldPid -ErrorAction SilentlyContinue) { throw 'Backend não parou' }
    foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $Candidato $f) -Destination (Join-Path $live $f) -Force }
    if ((Get-FileHash -LiteralPath (Join-Path $live $files[0])).Hash -ne $newHash) { throw 'Hash do assembly não confere' }
    $env:Worker__RunOnStartup = 'false'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://localhost:5277'
    $started = Start-Process -FilePath (Join-Path $live 'AccIntegration.WebApp.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $root 'Scripts\bi_chs001_isolado.stdout.log') -RedirectStandardError (Join-Path $root 'Scripts\bi_chs001_isolado.stderr.log')
    Log "RESTART newPID=$($started.Id) Worker__RunOnStartup=false"
    $ready = $false
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Milliseconds 500
        $listen = Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($listen -and $listen.OwningProcess -eq $started.Id) { $ready = $true; break }
        if ($started.HasExited) { throw 'Backend encerrou durante o startup' }
    }
    if (!$ready) { throw 'Backend não abriu a porta 5277' }
    # 5) Rotas: anônimo → 401 (rota nova publicada e protegida; legado intacto).
    foreach ($route in @("/api/bi-chs001/schedule/status?projectId=$projeto", "/api/bi-chs001/schedule/tarefas?projectId=$projeto",
                         "/api/bi-chs001/ldp?projectId=$projeto", "/api/bi-chs001/cronograma?projectId=$projeto", '/api/permissions/me', '/api/sidebar/access')) {
        $status = 0
        try { $res = Invoke-WebRequest ('http://127.0.0.1:5277' + $route) -UseBasicParsing -TimeoutSec 15; $status = [int]$res.StatusCode }
        catch { if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode } else { throw } }
        Log "HTTP=$status $route"
        if ($status -ne 401) { throw "Status anônimo inesperado $status em $route" }
    }
    Log 'SUCCESS: backend ativo com o assembly novo verificado'
} catch {
    Log ('FAIL: ' + $_.Exception.Message)
    try {
        if ($started -and (Get-Process -Id $started.Id -ErrorAction SilentlyContinue)) { Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 750 }
        if ($oldPid -and !(Get-Process -Id $oldPid -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $backup $files[0]))) {
            foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $live $f) -Force }
            $env:Worker__RunOnStartup = 'false'; $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = 'http://localhost:5277'
            $restored = Start-Process -FilePath (Join-Path $live 'AccIntegration.WebApp.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $root 'Scripts\bi_chs001_isolado_rollback.stdout.log') -RedirectStandardError (Join-Path $root 'Scripts\bi_chs001_isolado_rollback.stderr.log')
            Log "ROLLBACK: DLL anterior restaurada; PID=$($restored.Id)"
        }
    } catch { Log ('ROLLBACK ERROR: ' + $_.Exception.Message) }
    throw
}
