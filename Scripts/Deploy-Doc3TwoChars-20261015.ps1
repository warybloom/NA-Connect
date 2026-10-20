$ErrorActionPreference='Stop'
$root='C:\Dev\N-A-Connect aa\N-A-Connect'
$live=Join-Path $root 'bin\Debug\net9.0'
$candidate='C:\Dev\_combined_doc3_20261015'
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$backup=Join-Path (Split-Path $root -Parent) ('_backups\doc3_twochars_'+$stamp)
$log=Join-Path $root 'Scripts\doc3_twochars_deploy.log'
$success=Join-Path $root 'Scripts\doc3_twochars_deploy.exit'
Remove-Item -LiteralPath $success -ErrorAction SilentlyContinue
function Log([string]$m){$line=(Get-Date -Format s)+' '+$m;Add-Content -LiteralPath $log -Value $line;Write-Output $line}
$files=@('AccIntegration.WebApp.dll','AccIntegration.WebApp.pdb')
$oldPid=$null
$started=$null
try{
    foreach($n in @('combined_doc3_build')){
      if(!(Test-Path -LiteralPath ('C:\Dev\'+$n+'.exit')) -or (Get-Content -LiteralPath ('C:\Dev\'+$n+'.exit') -Raw).Trim() -ne '0'){throw "Validation missing or failed: $n"}
    }
    $jobs=@(Get-ScheduledTask -ErrorAction SilentlyContinue|Where-Object {$_.State -eq 'Running' -and $_.TaskName -match 'Planejamento|GRD|ACC|Rob[oô]|Sincronizar' -and $_.TaskName -notmatch 'Cloudflare'})
    if($jobs.Count -gt 0){throw 'Critical scheduled jobs running'}
    $listeners=@(Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue)
    $processIds=@($listeners|Select-Object -ExpandProperty OwningProcess -Unique)
    if($processIds.Count -ne 1){throw 'Ambiguous/missing backend listener'}
    $oldPid=[int]$processIds[0]
    $proc=Get-CimInstance Win32_Process -Filter "ProcessId=$oldPid"
    if(!$proc -or $proc.Name -ne 'AccIntegration.WebApp.exe' -or $proc.ExecutablePath -ne (Join-Path $live 'AccIntegration.WebApp.exe')){throw 'Unexpected port owner'}
    foreach($f in $files){
      if(!(Test-Path -LiteralPath (Join-Path $candidate $f)) -or !(Test-Path -LiteralPath (Join-Path $live $f))){throw "Artifact unavailable: $f"}
    }
    New-Item -ItemType Directory -Path $backup -Force|Out-Null
    foreach($f in $files){Copy-Item -LiteralPath (Join-Path $live $f) -Destination (Join-Path $backup $f) -Force}
    $oldHash=(Get-FileHash -LiteralPath (Join-Path $live $files[0])).Hash
    $newHash=(Get-FileHash -LiteralPath (Join-Path $candidate $files[0])).Hash
    Log "PREFLIGHT oldPID=$oldPid backup=$backup oldDll=$oldHash newDll=$newHash"
    Stop-Process -Id $oldPid -Force -ErrorAction Stop
    for($i=0;$i -lt 20;$i++){if(!(Get-Process -Id $oldPid -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 250}
    if(Get-Process -Id $oldPid -ErrorAction SilentlyContinue){throw 'Backend did not stop'}
    foreach($f in $files){Copy-Item -LiteralPath (Join-Path $candidate $f) -Destination (Join-Path $live $f) -Force}
    if((Get-FileHash -LiteralPath (Join-Path $live $files[0])).Hash -ne $newHash){throw 'Assembly hash mismatch'}
    $env:Worker__RunOnStartup='false'
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $env:ASPNETCORE_URLS='http://localhost:5277'
    $exe=Join-Path $live 'AccIntegration.WebApp.exe'
    $started=Start-Process -FilePath $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'Scripts\doc3_twochars.stdout.log') -RedirectStandardError (Join-Path $root 'Scripts\doc3_twochars.stderr.log')
    Log "RESTART newPID=$($started.Id) Worker__RunOnStartup=false"
    $ready=$false
    for($i=0;$i -lt 60;$i++){
        Start-Sleep -Milliseconds 300
        $listen=Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue|Select-Object -First 1
        if($listen -and $listen.OwningProcess -eq $started.Id){$ready=$true;break}
        if($started.HasExited){throw 'Backend exited during startup'}
    }
    if(!$ready){throw 'Backend failed to listen 5277'}
    foreach($route in @('/api/sidebar/access','/api/permissions/me','/api/coordination-standards/normas')){
        $status=0
        try{$res=Invoke-WebRequest ('http://127.0.0.1:5277'+$route) -UseBasicParsing -TimeoutSec 10;$status=[int]$res.StatusCode}
        catch{if($_.Exception.Response){$status=[int]$_.Exception.Response.StatusCode}else{throw}}
        Log "HTTP=$status $route"
        if($status -ne 401){throw "Unexpected anonymous status $status $route"}
    }
    Set-Content -LiteralPath $success -Value '0'
    Log 'SUCCESS: backend active and new assembly verified'
}catch{
    Log ('FAIL: '+$_.Exception.Message)
    try{
        if($started -and (Get-Process -Id $started.Id -ErrorAction SilentlyContinue)){
            Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue;Start-Sleep -Milliseconds 750
        }
        if(Test-Path -LiteralPath (Join-Path $backup $files[0])){
            foreach($f in $files){Copy-Item -LiteralPath (Join-Path $backup $f) -Destination (Join-Path $live $f) -Force}
            $env:Worker__RunOnStartup='false'
            $env:ASPNETCORE_ENVIRONMENT='Development'
            $env:ASPNETCORE_URLS='http://localhost:5277'
            $restored=Start-Process -FilePath (Join-Path $live 'AccIntegration.WebApp.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'Scripts\doc3_twochars_rollback.stdout.log') -RedirectStandardError (Join-Path $root 'Scripts\doc3_twochars_rollback.stderr.log')
            Log "ROLLBACK: old DLL restored; fallbackPID=$($restored.Id)"
        }
    }catch{Log ('ROLLBACK ERROR: '+$_.Exception.Message)}
    Set-Content -LiteralPath $success -Value '1'
    throw
}
