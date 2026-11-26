# Configura ConnectionStrings:AuditRepositoryConnection para DESENVOLVIMENTO (SQLEXPRESS local), com credencial DEDICADA.
#   1) gera uma senha forte EM MEMÓRIA (nunca exibida, nunca gravada em arquivo do repositório);
#   2) cria/atualiza o login [na_audit_writer] (Scripts\OperationalAuditRepositoryPrincipal.sql) — SELECT/INSERT/UPDATE só em [audit];
#   3) grava a connection string em user-secrets (via stdin — sem a senha na linha de comando);
#   4) verifica: o login escreve/lê em [audit] e NÃO enxerga [dbo]/[config].
# Não usa LocalConnection e não liga OperationalAudit:Enabled.
param(
    [string]$Server = ".\SQLEXPRESS",
    [string]$Database = "DB_NA_CONNECT"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$sqlcmd = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe"
if (-not (Test-Path $sqlcmd)) { $sqlcmd = "sqlcmd" }

# 1) Senha forte (32 caracteres, com maiúscula/minúscula/dígito/símbolo para atender CHECK_POLICY).
$alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
$bytes = New-Object byte[] 28
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$senha = -join ($bytes | ForEach-Object { $alfabeto[$_ % $alfabeto.Length] })
$senha = "Na$senha#9"   # garante as 4 categorias

try {
    # 2) Login/usuário/permissões. A senha vai como variável de ambiente lida pelo sqlcmd ($(AUDIT_PASSWORD)).
    $env:AUDIT_PASSWORD = $senha
    & $sqlcmd -S $Server -E -b -i (Join-Path $PSScriptRoot "OperationalAuditRepositoryPrincipal.sql")
    if ($LASTEXITCODE -ne 0) { throw "Falha ao criar/atualizar o login na_audit_writer (sqlcmd saiu com $LASTEXITCODE)." }

    # 3) user-secrets (stdin JSON → nada na linha de comando).
    $cs = "Server=$Server;Database=$Database;User Id=na_audit_writer;Password=$senha;Encrypt=False;TrustServerCertificate=True;"
    $json = @{ "ConnectionStrings:AuditRepositoryConnection" = $cs } | ConvertTo-Json -Compress
    $json | dotnet user-secrets set --project (Join-Path $repo "AccIntegration.WebApp.csproj") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Falha ao gravar o user-secret." }
    Write-Host "user-secret ConnectionStrings:AuditRepositoryConnection gravado (valor não exibido)."

    # 4) Verificação com a própria credencial (senha via SQLCMDPASSWORD, não na linha de comando).
    $env:SQLCMDPASSWORD = $senha
    $verif = @"
SET NOCOUNT ON;
SELECT 'audit SELECT=' + CAST(HAS_PERMS_BY_NAME('audit','SCHEMA','SELECT') AS varchar) + ' INSERT=' + CAST(HAS_PERMS_BY_NAME('audit','SCHEMA','INSERT') AS varchar)
     + ' UPDATE=' + CAST(HAS_PERMS_BY_NAME('audit','SCHEMA','UPDATE') AS varchar) + ' DELETE=' + CAST(HAS_PERMS_BY_NAME('audit','SCHEMA','DELETE') AS varchar)
     + ' | dbo SELECT=' + CAST(HAS_PERMS_BY_NAME('dbo','SCHEMA','SELECT') AS varchar) + ' | config SELECT=' + CAST(HAS_PERMS_BY_NAME('config','SCHEMA','SELECT') AS varchar);
SELECT 'AuditRun legivel: ' + CAST(COUNT(*) AS varchar) + ' execucoes' FROM audit.AuditRun;
"@
    & $sqlcmd -S $Server -d $Database -U na_audit_writer -h -1 -W -b -Q $verif
    if ($LASTEXITCODE -ne 0) { throw "O login na_audit_writer não conseguiu ler audit.AuditRun." }
    Write-Host "OK. Reinicie o backend (dotnet run) para carregar o user-secret."
}
finally {
    Remove-Item Env:AUDIT_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $senha = $null; $cs = $null; $json = $null
}
