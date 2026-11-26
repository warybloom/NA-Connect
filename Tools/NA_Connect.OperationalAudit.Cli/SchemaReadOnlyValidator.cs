using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NA_Connect.OperationalAudit.Cli
{
    public sealed class SchemaObjectCheck
    {
        public string NomeCompleto { get; init; } = string.Empty; // "[audit].[AuditRun]"
        public string Tipo { get; init; } = string.Empty; // "TABLE" | "VIEW"
        public bool Existe { get; init; }
    }

    public sealed class SchemaValidationResult
    {
        public IReadOnlyList<SchemaObjectCheck> Objetos { get; init; } = Array.Empty<SchemaObjectCheck>();
        public bool TodosPresentes => Objetos.Count > 0 && Objetos.All(o => o.Existe);
        public string? Erro { get; init; }
    }

    /// <summary>
    /// Checkpoint 4, seção 10: verifica SOMENTE LEITURA (via <c>sys.tables</c>/<c>sys.views</c> +
    /// <c>sys.schemas</c>) se as 7 tabelas e 5 views do schema <c>audit</c> já existem — o usuário as criou
    /// manualmente. NUNCA cria, altera ou executa <c>Scripts\OperationalAuditSchema.sql</c>.
    /// </summary>
    public static class SchemaReadOnlyValidator
    {
        private static readonly (string Nome, string Tipo)[] ObjetosEsperados =
        {
            ("AuditRun", "TABLE"),
            ("AuditProjectRun", "TABLE"),
            ("AuditStageResult", "TABLE"),
            ("AuditIssue", "TABLE"),
            ("AuditOccurrence", "TABLE"),
            ("AuditRequirementCatalog", "TABLE"),
            ("AuditRequirementResult", "TABLE"),
            ("VW_AuditIssues_BI", "VIEW"),
            ("VW_AuditOccurrences_BI", "VIEW"),
            ("VW_AuditStageSummary_BI", "VIEW"),
            ("VW_AuditProjectSummary_BI", "VIEW"),
            ("VW_AuditRequirementsCompliance_BI", "VIEW")
        };

        public static async Task<SchemaValidationResult> ValidarAsync(ICliSqlQueryExecutor executor, CancellationToken ct = default)
        {
            try
            {
                var tabela = await executor.ExecutarAsync(
                    @"SELECT t.name AS ObjectName, 'TABLE' AS ObjectType FROM sys.tables t
                      INNER JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'audit'
                      UNION ALL
                      SELECT v.name AS ObjectName, 'VIEW' AS ObjectType FROM sys.views v
                      INNER JOIN sys.schemas s ON s.schema_id = v.schema_id WHERE s.name = 'audit';",
                    null, ct);

                var existentes = new HashSet<(string, string)>(
                    tabela.Rows.Cast<System.Data.DataRow>().Select(r => (r["ObjectName"]!.ToString()!, r["ObjectType"]!.ToString()!)),
                    EqualityComparer<(string, string)>.Default);

                var objetos = ObjetosEsperados
                    .Select(o => new SchemaObjectCheck { NomeCompleto = $"[audit].[{o.Nome}]", Tipo = o.Tipo, Existe = existentes.Contains((o.Nome, o.Tipo)) })
                    .ToList();

                return new SchemaValidationResult { Objetos = objetos };
            }
            catch (Exception ex)
            {
                return new SchemaValidationResult { Objetos = Array.Empty<SchemaObjectCheck>(), Erro = ex.Message };
            }
        }
    }
}
