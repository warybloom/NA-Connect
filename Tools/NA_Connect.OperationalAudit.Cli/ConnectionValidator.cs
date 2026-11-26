using System;
using System.Threading;
using System.Threading.Tasks;

namespace NA_Connect.OperationalAudit.Cli
{
    public sealed class ConnectionCheckResult
    {
        public string Nome { get; init; } = string.Empty;
        public bool Sucesso { get; init; }
        public string? ResumoSeguro { get; init; }
        public string? Erro { get; init; }
    }

    /// <summary>Abstração mínima para testar o teste de conectividade ACC sem GET real — Checkpoint 4, seção 7.</summary>
    public interface IAccConnectivityProbe
    {
        Task<bool> TestarTokenEGetAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Checkpoint 4, seção 7: <c>--validate-connections</c> — abre as DUAS conexões só para
    /// SELECT/metadata (nunca uma auditoria completa, nunca escrita) e testa token/GET do ACC. Existe no
    /// código, mas NÃO É EXECUTADO neste checkpoint contra infraestrutura real (seção 18) — só testado com
    /// fakes.
    /// </summary>
    public static class ConnectionValidator
    {
        public static async Task<ConnectionCheckResult> ValidarConexaoAsync(string nome, string? connectionString, ICliSqlQueryExecutor executor, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return new ConnectionCheckResult { Nome = nome, Sucesso = false, Erro = "Connection string não configurada." };
            }

            try
            {
                await executor.ExecutarAsync("SELECT 1 AS Ok;", null, ct);
                return new ConnectionCheckResult { Nome = nome, Sucesso = true, ResumoSeguro = SecretRedactor.ResumirConnectionString(connectionString) };
            }
            catch (Exception ex)
            {
                return new ConnectionCheckResult { Nome = nome, Sucesso = false, ResumoSeguro = SecretRedactor.ResumirConnectionString(connectionString), Erro = ex.Message };
            }
        }

        public static async Task<ConnectionCheckResult> ValidarAccAsync(IAccConnectivityProbe probe, CancellationToken ct = default)
        {
            try
            {
                bool ok = await probe.TestarTokenEGetAsync(ct);
                return new ConnectionCheckResult { Nome = "ACC (token + GET)", Sucesso = ok };
            }
            catch (Exception ex)
            {
                return new ConnectionCheckResult { Nome = "ACC (token + GET)", Sucesso = false, Erro = ex.Message };
            }
        }

        /// <summary>
        /// Checkpoint 4.1, seção 2: relatório estruturado das QUATRO checagens independentes —
        /// AUDIT_SOURCE_SQL / AUDIT_REPOSITORY_SQL / APS_AUTH / ACC_PROJECT_READ. Existe no código, mas
        /// NÃO É EXECUTADO neste checkpoint contra infraestrutura real.
        /// </summary>
        public static async Task<ValidateConnectionsReport> ValidarTudoAsync(
            string? auditSourceConnectionString, string? auditRepositoryConnectionString,
            ICliSqlQueryExecutor auditSourceExecutor, ICliSqlQueryExecutor auditRepositoryExecutor,
            IAccReadOnlyConnectivityValidator accValidator, string projectId, CancellationToken ct = default)
        {
            var origem = await ValidarConexaoAsync("AuditSourceConnection", auditSourceConnectionString, auditSourceExecutor, ct);
            var repositorio = await ValidarConexaoAsync("AuditRepositoryConnection", auditRepositoryConnectionString, auditRepositoryExecutor, ct);
            var acc = await accValidator.ValidarAsync(projectId, ct);

            return new ValidateConnectionsReport
            {
                AuditSourceSql = origem.Sucesso ? ConnectivityCheckStatus.Ok : ConnectivityCheckStatus.Failed,
                AuditRepositorySql = repositorio.Sucesso ? ConnectivityCheckStatus.Ok : ConnectivityCheckStatus.Failed,
                ApsAuth = acc.ApsAuth,
                AccProjectRead = acc.AccProjectRead
            };
        }
    }

    /// <summary>Checkpoint 4.1, seção 2 — as quatro checagens de <c>--validate-connections</c>, nunca colapsadas numa só flag booleana.</summary>
    public sealed class ValidateConnectionsReport
    {
        public ConnectivityCheckStatus AuditSourceSql { get; init; }
        public ConnectivityCheckStatus AuditRepositorySql { get; init; }
        public ConnectivityCheckStatus ApsAuth { get; init; }
        public ConnectivityCheckStatus AccProjectRead { get; init; }

        public bool TudoOk =>
            AuditSourceSql == ConnectivityCheckStatus.Ok && AuditRepositorySql == ConnectivityCheckStatus.Ok &&
            ApsAuth == ConnectivityCheckStatus.Ok && AccProjectRead == ConnectivityCheckStatus.Ok;
    }
}
