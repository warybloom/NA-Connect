$ErrorActionPreference = 'Continue'
$root = 'C:\Dev\N-A-Connect aa\N-A-Connect'
$listener = Get-NetTCPConnection -State Listen -LocalPort 5277 -ErrorAction SilentlyContinue | Select-Object -First 1
"LISTENER_PID=$($listener.OwningProcess)"
$p=Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
"PROCESS_START=$($p.StartTime.ToString('yyyy-MM-dd HH:mm:ss'))"
$build = Get-Item (Join-Path $root 'bin\Debug\net9.0\AccIntegration.WebApp.dll')
"BACKEND_DLL=$($build.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) ($($build.Length) bytes)"
foreach ($u in @('http://localhost:5277/api/project-costs/packages/model-support?projectId=test','http://localhost:5277/api/sidebar/access','http://localhost:5173/api/project-costs/packages/model-support?projectId=test')) {
  $status = 0; $body = ''
  try {
    $response=Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 6
    $status = [int]$response.StatusCode
    $body = $response.Content
  } catch {
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    else { $body = $_.Exception.Message }
  }
  "CHECK $u HTTP=$status body=$($body.Substring(0,[Math]::Min(120,$body.Length)))"
}
