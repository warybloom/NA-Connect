using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NA_Connect.OperationalAudit.Cli
{
    public sealed class PermissionReport
    {
        public bool PodeSelect { get; init; }
        public bool PodeInsert { get; init; }
        public bool PodeUpdate { get; init; }
        public bool PodeDelete { get; init; }
        public string? Erro { get; init; }

        /// <summary>Seção 11: relatar (nunca corrigir) quando a conexão de LEITURA tem permissões além do necessário.</summary>
        public bool TemPermissaoAlemDoNecessarioParaLeitura => PodeInsert || PodeUpdate || PodeDelete;
    }

    /// <summary>
    /// Checkpoint 4, seção 11: diagnóstico SOMENTE LEITURA de permissões via <c>HAS_PERMS_BY_NAME</c> —
    /// nunca tenta provar ausência de escrita executando um INSERT/UPDATE/DELETE real. Usado para AS DUAS
    /// conexões: <c>AuditSourceConnection</c> (relata se tem permissão além de SELECT) e
    /// <c>AuditRepositoryConnection</c> (relata se tem acesso ao schema <c>audit</c>).
    /// </summary>
    public static class PermissionReadOnlyValidator
    {
        public static async Task<PermissionReport> ValidarPermissoesDeBancoAsync(ICliSqlQueryExecutor executor, CancellationToken ct = default)
        {
            try
            {
                var tabela = await executor.ExecutarAsync(
                    @"SELECT
                        HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'SELECT') AS PodeSelect,
                        HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'INSERT') AS PodeInsert,
                        HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'UPDATE') AS PodeUpdate,
                        HAS_PERMS_BY_NAME(NULL, 'DATABASE', 'DELETE') AS PodeDelete;",
                    null, ct);

                if (tabela.Rows.Count == 0) return new PermissionReport { Erro = "Consulta de permissões não retornou linhas." };

                var linha = tabela.Rows[0];
                return new PermissionReport
                {
                    PodeSelect = ParaBool(linha["PodeSelect"]),
                    PodeInsert = ParaBool(linha["PodeInsert"]),
                    PodeUpdate = ParaBool(linha["PodeUpdate"]),
                    PodeDelete = ParaBool(linha["PodeDelete"])
                };
            }
            catch (Exception ex)
            {
                return new PermissionReport { Erro = ex.Message };
            }
        }

        public static async Task<bool?> ValidarAcessoAoSchemaAuditAsync(ICliSqlQueryExecutor executor, CancellationToken ct = default)
        {
            try
            {
                var tabela = await executor.ExecutarAsync(
                    "SELECT COUNT(*) AS Quantidade FROM sys.fn_my_permissions('audit', 'SCHEMA');", null, ct);
                if (tabela.Rows.Count == 0) return null;
                return Convert.ToInt32(tabela.Rows[0]["Quantidade"]) > 0;
            }
            catch
            {
                return null;
            }
        }

        private static bool ParaBool(object valor) => valor != DBNull.Value && Convert.ToInt32(valor) == 1;
    }
}
