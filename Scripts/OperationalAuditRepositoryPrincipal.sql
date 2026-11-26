/*
    OperationalAuditRepositoryPrincipal.sql
    ---------------------------------------------------------------------------
    Credencial DEDICADA de ESCRITA no schema [audit] — destino de
    ConnectionStrings:AuditRepositoryConnection (persistência da Auditoria de
    Qualidade de Documentos e do OperationalAudit).

    NUNCA é executado pela aplicação. Forma recomendada de executar (gera a senha
    em memória, grava o user-secret e verifica as permissões, sem exibir a senha):

        powershell -ExecutionPolicy Bypass -File Scripts\Configurar-AuditRepositoryConnection.ps1

    A senha chega aqui pela variável do sqlcmd $(AUDIT_PASSWORD) (o script .ps1 a
    define como variável de ambiente — nunca em linha de comando, arquivo ou log).

    Permissões do login [na_audit_writer]:
      - SELECT, INSERT, UPDATE no schema [audit] (exatamente o que AuditRepository usa);
      - SEM DELETE; SEM acesso a [dbo], [config] ou qualquer outro schema.
    Idempotente: se o login já existir, só rotaciona a senha (para casar com o user-secret).
    ---------------------------------------------------------------------------
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'na_audit_writer')
    CREATE LOGIN [na_audit_writer] WITH PASSWORD = N'$(AUDIT_PASSWORD)', CHECK_POLICY = ON, DEFAULT_DATABASE = [DB_NA_CONNECT];
ELSE
    ALTER LOGIN [na_audit_writer] WITH PASSWORD = N'$(AUDIT_PASSWORD)';
GO

USE [DB_NA_CONNECT];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'na_audit_writer')
    CREATE USER [na_audit_writer] FOR LOGIN [na_audit_writer] WITH DEFAULT_SCHEMA = [audit];
GO
GRANT SELECT, INSERT, UPDATE ON SCHEMA::[audit] TO [na_audit_writer];
GO
PRINT 'na_audit_writer configurado (SELECT/INSERT/UPDATE em [audit]).';
GO
