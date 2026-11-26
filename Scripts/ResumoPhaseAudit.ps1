$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$settings = Get-Content -LiteralPath (Join-Path $repo 'appsettings.json') -Raw | ConvertFrom-Json
$conn = New-Object System.Data.SqlClient.SqlConnection($settings.ConnectionStrings.LocalConnection)
$conn.Open()
function Read-Sql([string]$sql) {
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = $sql
  $cmd.CommandTimeout = 45
  $table = New-Object System.Data.DataTable
  $reader = $cmd.ExecuteReader()
  $table.Load($reader)
  $reader.Close()
  $table | Format-Table -AutoSize | Out-String -Width 240 | Write-Output
}
Read-Sql "SELECT @@SERVERNAME AS Servidor, DB_NAME() AS Banco"
Read-Sql "SELECT Nome_Projeto, Project_ID_ACC, Tipo_LDP FROM dbo.Projetos WHERE Is_Ativo=1 AND (Nome_Projeto LIKE '%BML011.016%' OR Tipo_LDP=1)"
Read-Sql "SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE (TABLE_NAME IN ('Ldp_Documento_Gates','VW_TimeSheet_Corrigido_Espelho_Gerencial','VW_TimesheetDocumentoGerencial','TimesheetSnapshotAtual') OR TABLE_NAME LIKE '%Histor%') AND (COLUMN_NAME LIKE '%Gate%' OR COLUMN_NAME LIKE '%Fase%' OR COLUMN_NAME LIKE '%Cost%' OR COLUMN_NAME LIKE '%Custo%' OR COLUMN_NAME LIKE '%HH%' OR COLUMN_NAME LIKE '%Horas%') ORDER BY TABLE_SCHEMA,TABLE_NAME,ORDINAL_POSITION"
$conn.Close()
