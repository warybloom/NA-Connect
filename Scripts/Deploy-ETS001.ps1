$ErrorActionPreference='Stop'
$root='C:\Dev\N-A-Connect aa\N-A-Connect'
Set-Location -LiteralPath $root
$log=Join-Path $root 'Scripts/ets001_deploy.log'
function Log($message){$line=(Get-Date -Format s)+' '+$message;Add-Content -LiteralPath $log -Value $line;Write-Output $line}
foreach($check in @('ets001_backend_full','ets001_focused_tests_final','ets001_front_typecheck_final','ets001_front_build_final')){
    $file=Join-Path $root ('Scripts/'+$check+'.exit')
    if(!(Test-Path $file) -or (Get-Content -LiteralPath $file -Raw).Trim() -ne '0'){throw "Validation missing/failed: $check"}
}
$live=Join-Path $root 'bin/Debug/net9.0'
$candidate=[IO.Path]::GetFullPath((Join-Path $root 'Tests/.ets001-validation/full'))+'\'
$backup=Join-Path $root '_backups/ets001-prompt08-20261013/backend-net9.0'
if(!(Test-Path (Join-Path $backup 'AccIntegration.WebApp.dll'))){throw 'Backend backup missing'}
# Do not stop any process. An occupied port or an existing backend blocks this deployment.
if(Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue){throw 'Port 5277 occupied: inspect critical tasks before restarting'}
$running=Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'AccIntegration.WebApp.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'AccIntegration.WebApp.dll|run --project.*AccIntegration')
}
if($running){throw 'Existing backend detected: no restart while task state is unconfirmed'}
# Use the application's dependency manifest; the existing MSBuild FileList has grown to >1 GB.
$deps=Get-Content -LiteralPath ($candidate+'AccIntegration.WebApp.deps.json') -Raw | ConvertFrom-Json
$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($target in $deps.targets.PSObject.Properties){
    foreach($library in $target.Value.PSObject.Properties){
        foreach($kind in @('runtime','native','resources','runtimeTargets')){
            if($library.Value.$kind){
                foreach($asset in $library.Value.$kind.PSObject.Properties){
                    $leaf=[IO.Path]::GetFileName($asset.Name)
                    if(Test-Path -LiteralPath ($candidate+$leaf) -PathType Leaf){[void]$names.Add($leaf)}
                    if(Test-Path -LiteralPath ($candidate+$asset.Name) -PathType Leaf){[void]$names.Add($asset.Name)}
                }
            }
        }
    }
}
foreach($name in @('AccIntegration.WebApp.dll','AccIntegration.WebApp.exe','AccIntegration.WebApp.pdb','AccIntegration.WebApp.deps.json','AccIntegration.WebApp.runtimeconfig.json','AccIntegration.WebApp.staticwebassets.endpoints.json','AccIntegration.WebApp.staticwebassets.runtime.json')){
    if(Test-Path -LiteralPath ($candidate+$name) -PathType Leaf){[void]$names.Add($name)}
}
$files=@($names | ForEach-Object {$candidate+$_})
if($files.Count -lt 10 -or !($names.Contains('AccIntegration.WebApp.dll'))){throw 'Candidate dependency manifest invalid'}
Log "Preflight passed; no backend to restart; copying $($files.Count) application artifacts from validated manifest"
foreach($source in $files){
    $relative=$source.Substring($candidate.Length)
    $target=[IO.Path]::GetFullPath((Join-Path $live $relative))
    if(!$target.StartsWith(([IO.Path]::GetFullPath($live)+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Artifact outside intended directory'}
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}
$expected=(Get-FileHash -LiteralPath ($candidate+'AccIntegration.WebApp.dll')).Hash
if((Get-FileHash -LiteralPath (Join-Path $live 'AccIntegration.WebApp.dll')).Hash -ne $expected){throw 'Deployed assembly hash mismatch'}
$env:Worker__RunOnStartup='false'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:5277'
$p=Start-Process -FilePath (Join-Path $live 'AccIntegration.WebApp.exe') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'Scripts/ets001_backend.stdout.log') -RedirectStandardError (Join-Path $root 'Scripts/ets001_backend.stderr.log')
Log "Started PID=$($p.Id); Worker__RunOnStartup=false; DLL_SHA256=$expected"
$p.Id | Set-Content -LiteralPath (Join-Path $root 'Scripts/ets001_deployed.pid')
$ready=$false
for($i=0;$i -lt 40;$i++){
    Start-Sleep -Milliseconds 500
    $listener=Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if($listener -and $listener.OwningProcess -eq $p.Id){$ready=$true;break}
    if($p.HasExited){throw 'Backend exited during startup; inspect stderr'}
}
if(!$ready){throw 'Backend did not listen on 5277'}
foreach($endpoint in @('/api/reports/gestao-vista?projectId=b.9907135f-ff67-43bb-887b-20c3a7f75807','/api/project-costs/packages/2/disciplines?projectId=b.9907135f-ff67-43bb-887b-20c3a7f75807','/api/sidebar/access','/api/worker/status')){
    $code=0
    try{$response=Invoke-WebRequest -Uri ('http://localhost:5277'+$endpoint) -UseBasicParsing -TimeoutSec 15;$code=[int]$response.StatusCode}
    catch{if($_.Exception.Response){$code=[int]$_.Exception.Response.StatusCode}else{throw}}
    Log "HTTP=$code $endpoint"
    if($code -ne 401){throw "Unexpected anonymous response: $endpoint ($code)"}
}
Log 'DEPLOYED; endpoints require session; report content validated by read-only repository integration tests'
'0' | Set-Content -LiteralPath (Join-Path $root 'Scripts/ets001_deploy.exit')
