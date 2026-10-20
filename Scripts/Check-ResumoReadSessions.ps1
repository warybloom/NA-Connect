$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$cfg=Get-Content -LiteralPath (Join-Path $repo 'appsettings.json') -Raw | ConvertFrom-Json
$conn=New-Object System.Data.SqlClient.SqlConnection($cfg.ConnectionStrings.LocalConnection)
try{
  $conn.Open()
  $cmd=$conn.CreateCommand()
  $cmd.CommandTimeout=10
  $cmd.CommandText="SELECT s.session_id,s.host_process_id,s.status,s.open_transaction_count,r.command,r.status request_status FROM sys.dm_exec_sessions s LEFT JOIN sys.dm_exec_requests r ON r.session_id=s.session_id WHERE s.is_user_process=1 AND s.host_name=HOST_NAME() AND s.session_id<>@@SPID AND s.program_name LIKE '%SqlClient%'"
  $table=New-Object System.Data.DataTable
  $reader=$cmd.ExecuteReader();$table.Load($reader);$reader.Close()
  $rows=@($table | ForEach-Object { @{session=$_.session_id;pid=$_.host_process_id;status=$_.status;transactions=$_.open_transaction_count;command=if($_.command -is [DBNull]){$null}else{$_.command}} })
  $rows | ConvertTo-Json -Depth 5
  $metadataProcesses=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*$phaseCmd.CommandText*INFORMATION_SCHEMA.COLUMNS*'})
  Write-Output ('Own interrupted metadata processes: '+$metadataProcesses.Count)
}finally{$conn.Dispose()}
